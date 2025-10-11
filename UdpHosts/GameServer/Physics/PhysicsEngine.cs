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
using GameServer.Data.SDB.Records.dbitems;
using GameServer.Entities;
using GameServer.Entities.Character;
using GameServer.Systems.ProjectileSim;
using Serilog;

namespace GameServer.Physics;

public class PhysicsEngine
{
    public const float TargetTimestepDuration = 50; // (1/20f)

    public bool IsZoneLoaded = false;

    public RigidPose DebugViewPose;
    public Vector2 DebugViewHeading;
    public ulong DebugViewEntity = 0;

    private const float _tempBodySphereSize = 0.9f;

    private readonly IShard _shard;
    private readonly ILogger _logger;
    private readonly GameServerSettings _settings;
    private readonly ProjectileSim _projectileSim;

    private TypedIndex _defaultCharacterShape;
    private Dictionary<BodyHandle, ulong> _bodyToEntityId = new();

    public PhysicsEngine(IShard shard, ILogger logger, GameServerSettings settings, ProjectileSim projectileSim)
    {
        _shard = shard;
        _logger = logger;
        _settings = settings;
        _projectileSim = projectileSim;

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
        _defaultCharacterShape = Simulation.Shapes.Add(new Sphere(_tempBodySphereSize));

        // Simulation.Shapes.Add(new Sphere(0.9f));
        // new Cylinder(0.4f * 1.2f, 1.6f * 1.2f)
        // Simulation.Shapes.Add(new Cylinder(0.4f, 1.6f));

        // Load zone
        if (_settings.LoadMapsCollision)
        {
            TagfileLoader = new TagfileLoader(Simulation, BufferPool, LoaderThreadDispatcher, _logger);
            ZoneLoader = new ZoneLoader.ZoneLoader(Simulation, BufferPool, LoaderThreadDispatcher, TagfileLoader, _logger);
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

    public void Tick(double deltaTime, ulong currentTime, CancellationToken ct)
    {
        TimeAccumulator += deltaTime;
        while (!ct.IsCancellationRequested && TimeAccumulator >= TargetTimestepDuration)
        {
            Simulation.Timestep(TargetTimestepDuration, PhysicsThreadDispatcher);
            TimeAccumulator -= TargetTimestepDuration;
        }
    }

    public BodyHandle CreateKineticEntity(CharacterEntity entity)
    {
        var pose = new RigidPose
        {
            Position = entity.Position,
            Orientation = entity.Rotation
        };
        var body = Simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, _defaultCharacterShape, -1));
        _bodyToEntityId[body] = entity.EntityId;

        return body;
    }

    public void UpdateEntity(CharacterEntity entity)
    {
        ref var currentPose = ref Simulation.Bodies[entity.BodyHandle].Pose;
        currentPose.Position = entity.Position;
        currentPose.Position.Z += _tempBodySphereSize; // Body size with scale
        currentPose.Orientation = Quaternion.Inverse(entity.Rotation);

        if (entity.EntityId == DebugViewEntity)
        {
            DebugViewPose = currentPose;
            DebugViewHeading = new Vector2(entity.HeadingYaw, entity.HeadingPitch);
        }
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
        hitHandler.SourceBody = source.BodyHandle;

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
                        var bodyPosition = Simulation.Bodies[hitHandler.HitCollidable.BodyHandle].Pose.Position;
                        bodyPosition.Z -= _tempBodySphereSize; // Body size with scale
                        SendDebugProjectilePoseHit(source, trace, hitPosition, bodyPosition, hitEntity);

                        var hit = new ProjectileSim.HitData
                        {
                            ImpactPosition = hitPosition,
                            ImpactEntity = hitEntity
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
        hitHandler.SourceBody = source.BodyHandle;
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
                    Unk2 = poseOrigin,
                    Unk3 = Quaternion.Identity,
                    Unk4 = 0,
                    Unk5 = 0xFF,
                },
                HaveUnk9 = 0,
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

    private struct RayHitHandler : IRayHitHandler
    {
        public float T;
        public CollidableReference HitCollidable;
        public bool AvoidSourceBody;
        public BodyHandle SourceBody;
        public Vector3 Normal;

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
        }
    }
}