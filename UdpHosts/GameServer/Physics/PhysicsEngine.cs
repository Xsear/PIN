using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using AeroMessages.GSS.V66.Character.Event;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using GameServer.Data.SDB;
using GameServer.Data.SDB.Records.dbitems;
using GameServer.Entities;
using GameServer.Entities.Character;
using GameServer.Entities.Vehicle;
using GameServer.Physics.PoseLoader;
using GameServer.Systems.ProjectileSim;
using Serilog;
using static GameServer.Physics.PoseLoader.PoseData;

namespace GameServer.Physics;

public struct AssetCompoundKey : IEquatable<AssetCompoundKey>
{
    public uint AssetId;
    public float Scale;
    public Vector3 Offset;

    public AssetCompoundKey(uint assetId, Vector3 offset, float scale)
    {
        AssetId = assetId;
        Scale = scale;
        Offset = offset;
    }

    // TOOD: Fixme to account offset
    public bool Equals(AssetCompoundKey other)
    {
        return AssetId == other.AssetId &&
               BitConverter.SingleToInt32Bits(Scale) == BitConverter.SingleToInt32Bits(other.Scale);
    }

    public override bool Equals(object obj)
    {
        return obj is AssetCompoundKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + AssetId.GetHashCode();
            hash = (hash * 31) + BitConverter.SingleToInt32Bits(Scale);
            return hash;
        }
    }
}

public struct CompoundCacheEntry
{
    public TypedIndex ShapeIndex;
}

public class PhysicsEngine
{
    public const float TargetTimestepDuration = 50; // (1/20f)

    public bool IsZoneLoaded = false;

    public RigidPose DebugViewPose;
    public Vector2 DebugViewHeading;
    public ulong DebugViewEntity = 0;

    private readonly IShard _shard;
    private readonly ILogger _logger;
    private readonly GameServerSettings _settings;
    private readonly ProjectileSim _projectileSim;

    private TypedIndex _fallbackShape;
    private Dictionary<ulong, BodyHandle> _entityIdToBody = [];
    private Dictionary<BodyHandle, ulong> _bodyToEntityId = [];

    /// <summary>
    ///     AssetId + Scale to Shape (TypedIndex) cache
    ///     This ensures we don't store duplicates of the same shapes, well, except for the actual primitives used... :thinking:
    /// </summary>
    private Dictionary<AssetCompoundKey, CompoundCacheEntry> _compoundCache = new();

    /// <summary>
    ///     (Pose) AssetId to Compound based metadata (Same for all Scales)
    ///     This is so that we can look up the source Pose data. It's dependant on the Compound generation for the child index, but those will be the same for all Scales.
    /// </summary>
    private Dictionary<uint, Dictionary<int, ActivePoseShapeData>> _assetIdToPoseCompoundData = new();

    /// <summary>
    ///     Compound/Shape (TypedIndex) to AssetId
    ///     When we have a hit and want to go look up the extra data, we need the asset id.
    /// </summary>
    private Dictionary<TypedIndex, uint> _poseCompoundToAssetId = new();

    public PhysicsEngine(IShard shard, ILogger logger, GameServerSettings settings, ProjectileSim projectileSim, PoseLoader.PoseLoader poseLoader)
    {
        _shard = shard;
        _logger = logger;
        _settings = settings;
        _projectileSim = projectileSim;
        PoseLoader = poseLoader;

        // Determine number of threads to use
        var targetThreadCount = int.Max(1,
            Environment.ProcessorCount > 4 ? Environment.ProcessorCount - 2 : Environment.ProcessorCount - 1);

        // Setup BepuPhysics
        BufferPool = new BufferPool();
        PhysicsThreadDispatcher = new ThreadDispatcher(targetThreadCount);
        LoaderThreadDispatcher = new ThreadDispatcher(1);
        DebugThreadDispatcher = new ThreadDispatcher(1);
        Simulation = Simulation.Create(BufferPool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(new Vector3(0, 0, -8)),
            new SolveDescription(8, 1));

        // Default shapes
        _fallbackShape = Simulation.Shapes.Add(new Sphere(0.9f));

        // Load zone
        if (_settings.LoadMapsCollision)
        {
            TagfileLoader = new TagfileLoader(Simulation, BufferPool, PhysicsThreadDispatcher, _logger);
            ZoneLoader = new ZoneLoader.ZoneLoader(Simulation, BufferPool, PhysicsThreadDispatcher, TagfileLoader, _logger);
            ZoneLoader.LoadCollision(_settings.MapsPath,
                _shard.ZoneId,
                () =>
                {
                    _logger.Information("ZoneLoader Done");
                    if (true)
                    {
                        _logger.Information("Starting Physics DebugView");
                        DebugView.Init(this);
                    }
                });
        }
    }

    public Simulation Simulation { get; protected set; }
    public BufferPool BufferPool { get; private set; }
    public ThreadDispatcher PhysicsThreadDispatcher { get; private set; }
    public ThreadDispatcher LoaderThreadDispatcher { get; private set; }
    public ThreadDispatcher DebugThreadDispatcher { get; private set; }
    public double TimeAccumulator { get; protected set; }
    public TagfileLoader TagfileLoader { get; private set; }
    public ZoneLoader.ZoneLoader ZoneLoader { get; private set; }
    public PoseLoader.PoseLoader PoseLoader { get; private set; }

    public void Tick(double deltaTime, ulong currentTime, CancellationToken ct)
    {
        TimeAccumulator += deltaTime;
        while (!ct.IsCancellationRequested && TimeAccumulator >= TargetTimestepDuration)
        {
            Simulation.Timestep(TargetTimestepDuration, PhysicsThreadDispatcher);
            TimeAccumulator -= TargetTimestepDuration;
        }
    }

    public (CompoundCacheEntry, Dictionary<int, ActivePoseShapeData>) CreateActivePose(PoseData poseDef, Vector3 offset, float scale = 1f)
    {
        var result = new Dictionary<int, ActivePoseShapeData>();
        var builder = new CompoundBuilder(BufferPool, Simulation.Shapes, poseDef.Shapes.Capacity);
        QuaternionEx.GetQuaternionBetweenNormalizedVectors(Vector3.UnitY, Vector3.UnitZ, out Quaternion rotateYToZ);
        int childIndex = 0; // We need this for lookups so we manually track it...
        foreach (var (name, shapeDef) in poseDef.Shapes)
        {
            if (name == "CatchAll" || name == "NPCWarning")
            {
                // TODO: CatchAll and NPCWarning should have some separate collision check but don't know how it should work yet
                continue;
            }

            TypedIndex shapeId;
            var pose = RigidPose.Identity;
            pose.Position = shapeDef.Origin;

            if (shapeDef.Type == ShapeType.HKX)
            {
                _logger.Debug("HKX shape {Filename}", shapeDef.Filename);

                var assetId = shapeDef.Filename;
                var assetPath = $"{_settings.AssetsPath}\\{assetId}.pinasset.json";
                var statics = TagfileLoader.TEMP_ProcessRigidBody(Vector3.Zero, assetPath);
                for (int i = 0; i < statics.Length; i++)
                {
                    var stat = statics[i];
                    var statPose = stat.Pose;
                    statPose.Position += shapeDef.Origin * scale; // NOTE: Not sure if hkx shapes in .pose files can reference a rotation?
                    var statShapeId = stat.Shape;

                    builder.AddForKinematic(statShapeId, statPose, 1);
                    result.Add(childIndex++, new ActivePoseShapeData()
                    {
                        DamageMod = 1.0f,
                        HitTagType = shapeDef.HitTagType,
                        Material = shapeDef.Material ?? 0,
                        Name = shapeDef.Name,
                        ShapeFlags = shapeDef.Flags,
                        ShapeId = statShapeId,
                    });
                }
            }
            else
            {
                switch (shapeDef.Type)
                {
                    case ShapeType.Capsule:
                        var capsuleRadius = (float)shapeDef.Radius * scale;
                        var capsuleHeight = (float)shapeDef.Height * scale;
                        shapeId = Simulation.Shapes.Add(new Capsule(capsuleRadius, capsuleHeight));
                        pose.Position = shapeDef.Origin * scale;
                        pose.Orientation = (Quaternion)shapeDef.Rotation * rotateYToZ;
                        break;
                    case ShapeType.Cylinder:
                        var cylinderRadius = (float)shapeDef.Radius * scale;
                        var cylinderHeight = (float)shapeDef.Height * scale;
                        shapeId = Simulation.Shapes.Add(new Cylinder(cylinderRadius, cylinderHeight));
                        pose.Position = shapeDef.Origin * scale;
                        pose.Orientation = (Quaternion)shapeDef.Rotation * rotateYToZ;
                        break;
                    case ShapeType.Sphere:
                        shapeId = Simulation.Shapes.Add(new Sphere((float)shapeDef.Radius * scale));
                        pose.Position = shapeDef.Origin * scale;
                        break;
                    case ShapeType.Triangle:
                        shapeId = Simulation.Shapes.Add(new Triangle((Vector3)shapeDef.Vertex0, (Vector3)shapeDef.Vertex1, (Vector3)shapeDef.Vertex2));
                        break;
                    default:
                        _logger.Debug("Unhandled shape type {type}", shapeDef.Type);
                        shapeId = Simulation.Shapes.Add(new Sphere((float)shapeDef.Radius));
                        break;
                }

                builder.AddForKinematic(shapeId, pose, 1);

                // FIXME: The HKX route will add multiple children so the child index no longer aligns with the shape defs
                result.Add(childIndex++, new ActivePoseShapeData()
                {
                    DamageMod = 1.0f,
                    HitTagType = shapeDef.HitTagType,
                    Material = shapeDef.Material ?? 0,
                    Name = shapeDef.Name,
                    ShapeFlags = shapeDef.Flags,
                    ShapeId = shapeId,
                });
            }
        }

        builder.BuildKinematicCompound(out var children, out Vector3 center);
        var compound = new Compound(children);

        // Origin at bottom
        Vector3 origin = new Vector3(0, 0, center.Z);
        for (int i = 0; i < childIndex; ++i)
        {
            ref var child = ref compound.Children[i];
            child.LocalPosition += origin + offset;
        }

        var entry = new CompoundCacheEntry
        {
            ShapeIndex = Simulation.Shapes.Add(compound)
        };

        return (entry, result);
    }

    public TypedIndex GetAssetShape(uint assetId, Vector3 offset, float scale = 1f)
    {
        var key = new AssetCompoundKey(assetId, offset, scale);

        if (_compoundCache.TryGetValue(key, out var cacheEntry))
        {
            return cacheEntry.ShapeIndex;
        }

        var ok = PoseLoader.TryLoad(assetId.ToString("D8"), out var poseDef);
        if (ok)
        {
            _logger.Debug("PoseLoader OK");
            var (entry, result) = CreateActivePose(poseDef, offset, scale);
            _compoundCache[key] = entry;
            _assetIdToPoseCompoundData.TryAdd(assetId, result);
            _poseCompoundToAssetId.Add(entry.ShapeIndex, assetId);
            return entry.ShapeIndex;
        }

        _logger.Debug("Returning fallback shape for assetId {assetId}", assetId);
        return _fallbackShape;
    }

    public TypedIndex GetCharacterShape(CharacterEntity character)
    {
        var mov = character.MovementStateContainer;
        var movestate = character.MovementStateContainer.Movestate;
        var info = character.PhysicsPoseInfo;
        if (info == null)
        {
            _logger.Debug("GetCharacterShape but no PhysicsPoseInfo");
            return _fallbackShape;
        }

        uint collisionId = info.PoseTypeRecord.StandingCollisionid;
        Vector3 offset = Vector3.Zero;

        if (info.AttachmentPoseId != 0)
        {
            collisionId = info.AttachmentPoseId;
            offset = info.AttachmentPoseOffset;
        }
        else if (info.PoseTypeRecord.PoseId == 0)
        {
            // PoseTypeRecord 0 provides no collision ids so let's look at the visual record instead
            if (info.HitboxCollisionId != 0)
            {
                collisionId = info.HitboxCollisionId;
            }
            else if (info.RagdollCollisionId != 0)
            {
                collisionId = info.RagdollCollisionId;
            }
            else
            {
                _logger.Warning("No suitable collisionId found during GetCharacterShape");
            }
        }
        else if (movestate == Movestate.Glider || movestate == Movestate.GliderThrusters || movestate == Movestate.GliderStalling)
        {
            collisionId = info.PoseTypeRecord.ProneCollisionid;
        }
        else if (movestate == Movestate.Falling)
        {
            collisionId = info.PoseTypeRecord.FallingCollisionid;
        }
        else if (movestate == Movestate.Knockdown || movestate == Movestate.KnockdownFalling)
        {
            collisionId = info.PoseTypeRecord.ProneCollisionid;
        }
        else if (mov.Crouch)
        {
            collisionId = info.PoseTypeRecord.CrouchedCollisionid;
        }
        else if (mov.Sprint)
        {
            collisionId = info.PoseTypeRecord.SprintingCollisionid;
        }
        else if (movestate == Movestate.Running)
        {
            collisionId = info.PoseTypeRecord.RunningCollisionid;
        }

        return GetAssetShape(collisionId, offset, character.PhysicsPoseInfo.Scale);
    }

    public BodyHandle CreateKineticEntity(CharacterEntity entity)
    {
        if (_entityIdToBody.ContainsKey(entity.EntityId))
        {
            _logger.Warning("CreateKineticEntity was called for {entity} but there is already a body! Returning the existing body.", entity.ToString());
            return _entityIdToBody[entity.EntityId];
        }

        var pose = new RigidPose(entity.Position, Quaternion.Inverse(entity.Rotation));
        var shape = GetCharacterShape(entity);
        var body = Simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, shape, 1));
        _bodyToEntityId[body] = entity.EntityId;
        _entityIdToBody[entity.EntityId] = body;

        return body;
    }

    public BodyHandle CreateKineticEntity(ICommonPhysicsEntity entity)
    {
        if (_entityIdToBody.ContainsKey(entity.EntityId))
        {
            _logger.Warning("CreateKineticEntity was called for {entity} but there is already a body! Returning the existing body.", entity.ToString());
            return _entityIdToBody[entity.EntityId];
        }

        var pose = new RigidPose(entity.Position, Quaternion.Inverse(entity.Rotation));
        var shape = GetAssetShape(entity.PhysicsPoseInfo.HitboxCollisionId, Vector3.Zero, entity.PhysicsPoseInfo.Scale);
        var body = Simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, shape, 1));
        _bodyToEntityId[body] = entity.EntityId;
        _entityIdToBody[entity.EntityId] = body;

        return body;
    }

    public void UpdateEntity(CharacterEntity entity)
    {
        if (!_entityIdToBody.ContainsKey(entity.EntityId))
        {
            _logger.Warning("UpdateEntity was called for {entity} but there is no body!", entity.ToString());
            return;
        }

        // Handle pose shape change
        var bodyHandle = _entityIdToBody[entity.EntityId];
        var body = Simulation.Bodies[bodyHandle];
        body.Awake = true;
        var expectedShape = GetCharacterShape(entity); // Maybe it would be better to determine the pose id on the character and update accordingly here
        if (body.Collidable.Shape != expectedShape)
        {
            body.SetShape(expectedShape);
        }

        // Handle position and orientation
        ref var currentPose = ref Simulation.Bodies[bodyHandle].Pose;
        currentPose.Orientation = Quaternion.Inverse(entity.Rotation);
        currentPose.Position = entity.Position;

        // Handle debug viewer focus
        if (entity.EntityId == DebugViewEntity)
        {
            DebugViewPose = currentPose;
            DebugViewHeading = new Vector2(entity.HeadingYaw, entity.HeadingPitch);
        }
    }

    public void UpdateEntity(VehicleEntity entity)
    {
        if (!_entityIdToBody.ContainsKey(entity.EntityId))
        {
            _logger.Warning("UpdateEntity was called for {entity} but there is no body!", entity.ToString());
            return;
        }

        // Handle position and orientation
        var bodyHandle = _entityIdToBody[entity.EntityId];
        var body = Simulation.Bodies[bodyHandle];
        body.Awake = true;
        ref var currentPose = ref Simulation.Bodies[bodyHandle].Pose;
        currentPose.Orientation = Quaternion.Inverse(entity.Rotation);
        currentPose.Position = entity.Position;
    }

    public void UpdateEntity(IEntity entity)
    {
        if (!_entityIdToBody.ContainsKey(entity.EntityId))
        {
            _logger.Warning("UpdateEntity was called for {entity} but there is no body!", entity.ToString());
            return;
        }

        // Handle position and orientation
        var bodyHandle = _entityIdToBody[entity.EntityId];
        var body = Simulation.Bodies[bodyHandle];
        body.Awake = true;
        ref var currentPose = ref Simulation.Bodies[bodyHandle].Pose;
        currentPose.Orientation = Quaternion.Inverse(entity.Rotation);
        currentPose.Position = entity.Position;
    }

    public void RemoveEntity(IEntity entity)
    {
        if (!_entityIdToBody.ContainsKey(entity.EntityId))
        {
            _logger.Warning("RemoveEntity was called for {entity} but there is no body!", entity.ToString());
            return;
        }

        var bodyHandle = _entityIdToBody[entity.EntityId];
        _entityIdToBody.Remove(entity.EntityId);
        _bodyToEntityId.Remove(bodyHandle);
        Simulation.Bodies.Remove(bodyHandle);
    }

    public void ProjectileRayCast(ProjectileSim.ProjectileData projectile)
    {
        Vector3 origin = projectile.Origin;
        Vector3 direction = projectile.Direction;
        IEntity source = projectile.SourceEntity;
        uint trace = projectile.TraceId;
        Ammo ammo = projectile.Ammo;
        var speed = projectile.Speed;
        var maxRange = projectile.MaxRange;

        SendDebugProjectileSpawn(source, trace, origin, direction, speed);

        var hitHandler = default(RayHitHandler);
        hitHandler.T = maxRange;
        hitHandler.AvoidSourceBody = true;
        hitHandler.SourceBody = _entityIdToBody[source.EntityId];

        Simulation.RayCast(origin, direction, float.MaxValue, BufferPool, ref hitHandler);
        if (hitHandler.T < maxRange)
        {
            var hitPosition = origin + (direction * hitHandler.T);
            _logger.Debug("ProjectileRayCast Impact with range {range} on {mobility} {collidable} at {position}", hitHandler.T, hitHandler.HitCollidable.Mobility, hitHandler.HitCollidable, hitPosition);

            SendDebugProjectileImpact(source, trace, hitPosition, hitHandler.Normal);

            if (hitHandler.HitCollidable.Mobility == CollidableMobility.Kinematic)
            {
                var hitEntityId = _bodyToEntityId.GetValueOrDefault(hitHandler.HitCollidable.BodyHandle);
                _logger.Debug("ProjectileRayCast Impact EntityId {hitEntityId}", hitEntityId);
                if (hitEntityId != 0)
                {
                    _shard.Entities.TryGetValue(hitEntityId, out IEntity hitEntity);
                    _logger.Debug("ProjectileRayCast Impact Entity {hitEntity}", hitEntity);
                    if (hitEntity != null)
                    {
                        var body = Simulation.Bodies[hitHandler.HitCollidable.BodyHandle];
                        var shape = body.Collidable.Shape;
                        bool headshot = false;
                        bool crit = false;
                        float damageMod = 1.0f;
                        if (_poseCompoundToAssetId.ContainsKey(shape))
                        {
                            var poseId = _poseCompoundToAssetId[shape];
                            var poseData = _assetIdToPoseCompoundData[poseId];
                            var poseShapeData = poseData[hitHandler.ChildIndex];
                            var physicsMaterial = SDBInterface.GetPhysicsMaterial((uint)poseShapeData.Material); // TODO: Material can be 0 which will result in null here, but what should we do? Is there a default to fallback to?

                            headshot = poseShapeData.ShapeFlags.Headshot;
                            crit = physicsMaterial?.IsCritHit == 1;
                            damageMod = poseShapeData.DamageMod;

                            _logger.Debug($"ProjectileRayCast Impact on {poseShapeData.Name}");
                            _shard.Chat.SendToAll($"You hit {poseShapeData.Name} of {hitEntity}", Enums.ChatChannel.Debug, source);
                        }

                        var bodyPosition = Simulation.Bodies[hitHandler.HitCollidable.BodyHandle].Pose.Position;
                        SendDebugProjectilePoseHit(source, trace, hitPosition, bodyPosition, hitEntity);

                        var hit = new ProjectileSim.HitData
                        {
                            ImpactPosition = hitPosition,
                            ImpactEntity = hitEntity,
                            IsHeadshot = headshot,
                            IsCrit = crit,
                            DamageMod = damageMod
                        };
                        _projectileSim.OnProjectileImpact(projectile, hit);
                    }
                }
            }
        }
        else
        {
            var timeoutPosition = origin + (direction * maxRange);
            var timeoutDirection = -Vector3.Normalize(direction);
            _logger.Verbose("ProjectileRayCast Timeout at {timeoutPosition}", timeoutPosition);
            SendDebugProjectileTimeout(source, trace, timeoutPosition, timeoutDirection);
        }
    }

    public (bool, Vector3, ulong) TargetRayCast(Vector3 origin,
        Vector3 direction,
        CharacterEntity source,
        float maxRange = 500f)
    {
        bool outHit = false;
        Vector3 outPos = Vector3.Zero;
        ulong outEnt = 0;

        var hitHandler = default(RayHitHandler);
        hitHandler.T = maxRange;
        hitHandler.AvoidSourceBody = true;
        hitHandler.SourceBody = _entityIdToBody[source.EntityId];
        Simulation.RayCast(origin, direction, float.MaxValue, BufferPool, ref hitHandler);
        if (hitHandler.T < maxRange)
        {
            outHit = true;
            outPos = origin + (direction * hitHandler.T);
            outEnt = _bodyToEntityId[hitHandler.HitCollidable.BodyHandle];
        }

        return (outHit, outPos, outEnt);
    }

    private BodyDescription CreateTestBall(Vector3 pos)
    {
        var bulletShape = new Sphere(3f);
        var bulletDescription = BodyDescription.CreateDynamic(new Vector3(),
            bulletShape.ComputeInertia(100),
            new(Simulation.Shapes.Add(bulletShape), 0.1f),
            0.01f);
        bulletDescription.Pose.Position = pos;
        Simulation.Bodies.Add(bulletDescription);
        return bulletDescription;
    }

    private void SendDebugProjectileSpawn(IEntity entity,
        uint traceId,
        Vector3 origin,
        Vector3 direction,
        float speed)
    {
        if (entity is not CharacterEntity source)
        {
            return;
        }

        var rayVector = direction * speed;
        var msg = new TookDebugWeaponHit
        {
            Data = new()
            {
                Time = _shard.CurrentTime,
                TraceType = AeroMessages.GSS.V66.TookDebugWeaponHitData.DebugTraceType.Spawn,
                Unk2_TraceId = traceId,
                Position = origin,
                Direction = rayVector,
            }
        };
        if (source.IsPlayerControlled && source.Player.Preferences.DebugWeapon > 0)
        {
            // Console.WriteLine($"SendDebugProjectileSpawn");
            source.Player.NetChannels[ChannelType.ReliableGss].SendMessage(msg, source.EntityId);
        }
    }

    private void SendDebugProjectileImpact(IEntity entity, uint traceId, Vector3 position, Vector3 normal)
    {
        if (entity is not CharacterEntity source)
        {
            return;
        }

        var msg = new TookDebugWeaponHit
        {
            Data = new()
            {
                Time = _shard.CurrentTime,
                TraceType = AeroMessages.GSS.V66.TookDebugWeaponHitData.DebugTraceType.Impact,
                Unk2_TraceId = traceId,
                Position = position,
                Direction = normal,
            }
        };
        if (source.IsPlayerControlled && source.Player.Preferences.DebugWeapon > 0)
        {
            // Console.WriteLine($"SendDebugProjectileImpact");
            source.Player.NetChannels[ChannelType.ReliableGss].SendMessage(msg, source.EntityId);
        }
    }

    private void SendDebugProjectilePoseHit(IEntity entity,
        uint traceId,
        Vector3 markerOrigin,
        Vector3 poseOrigin,
        IEntity hitEntity)
    {
        if (entity is not CharacterEntity source)
        {
            return;
        }

        var msg = new TookDebugWeaponHit
        {
            Data = new()
            {
                Time = _shard.CurrentTime,
                TraceType = AeroMessages.GSS.V66.TookDebugWeaponHitData.DebugTraceType.Posefile_Hit,
                Unk2_TraceId = traceId,
                Position = markerOrigin,
                Direction = new Vector3(0.225f, 0.974f, 0),
                HaveUnk8 = 1,
                Unk8 = new AeroMessages.GSS.V66.TookDebugWeaponHitRelatedData
                {
                    Target = hitEntity.AeroEntityId,
                    Origin = poseOrigin,
                    Orientation = Quaternion.Identity,
                    Unk4 = 0,
                    Unk5 = 0xFF,
                },
                HaveRagdoll = 0,
            }
        };
        if (source.IsPlayerControlled && source.Player.Preferences.DebugWeapon > 0)
        {
            // Console.WriteLine($"SendDebugProjectilePoseHit");
            source.Player.NetChannels[ChannelType.ReliableGss].SendMessage(msg, source.EntityId);
        }
    }

    private void SendDebugProjectileTimeout(IEntity entity, uint traceId, Vector3 position, Vector3 direction)
    {
        if (entity is not CharacterEntity source)
        {
            return;
        }

        var msg = new TookDebugWeaponHit
        {
            Data = new()
            {
                Time = _shard.CurrentTime,
                TraceType = AeroMessages.GSS.V66.TookDebugWeaponHitData.DebugTraceType.Spawn,
                Unk2_TraceId = traceId,
                Position = position,
                Direction = direction,
            }
        };
        if (source.IsPlayerControlled && source.Player.Preferences.DebugWeapon > 0)
        {
            // Console.WriteLine($"SendDebugProjectileTimeout");
            source.Player.NetChannels[ChannelType.ReliableGss].SendMessage(msg, source.EntityId);
        }
    }

    public struct ActivePoseShapeData
    {
        public TypedIndex ShapeId;
        public ShapeFlags ShapeFlags;
        public int Material;
        public string Name;
        public float DamageMod = 1.0f;
        public string HitTagType = "Default";

        public ActivePoseShapeData()
        {
        }
    }

    public struct EntityData
    {
        public ulong EntityId;
        public uint PoseId;
    }

    private struct RayHitHandler : IRayHitHandler
    {
        public float T;
        public CollidableReference HitCollidable;
        public bool AvoidSourceBody;
        public BodyHandle SourceBody;
        public Vector3 Normal;
        public int ChildIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable)
        {
            if (AvoidSourceBody && collidable.Mobility != CollidableMobility.Static &&
                collidable.BodyHandle.Equals(SourceBody))
            {
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            if (AvoidSourceBody && collidable.Mobility != CollidableMobility.Static &&
                collidable.BodyHandle.Equals(SourceBody))
            {
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRayHit(in RayData ray,
            ref float maximumT,
            float t,
            Vector3 normal,
            CollidableReference collidable,
            int childIndex)
        {
            // We are only interested in the earliest hit. This callback is executing within the traversal, so modifying maximumT informs the traversal
            // that it can skip any AABBs which are more distant than the new maximumT.
            maximumT = t;

            // Cache the earliest impact.
            T = t;
            HitCollidable = collidable;
            Normal = normal;
            ChildIndex = childIndex;
        }
    }
}
