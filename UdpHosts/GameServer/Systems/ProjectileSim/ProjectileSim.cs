using System;
using System.Numerics;
using GameServer.Data.SDB.Records.dbitems;
using GameServer.Entities;
using GameServer.Entities.Character;
using GameServer.Systems.Combat;
using Serilog;

namespace GameServer.Systems.ProjectileSim;

public class ProjectileSim
{
    private readonly IShard _shard;
    private readonly ILogger _logger;
    private readonly CombatSim _combatSim;

    public ProjectileSim(IShard shard, ILogger logger, CombatSim combatSim)
    {
        _shard = shard;
        _logger = logger;
        _combatSim = combatSim;
    }

    public void FireProjectileCommand(CharacterEntity entity, Ammo ammo, Vector3 origin, Vector3 direction, float range)
    {
        byte round = 0;
        var time = _shard.CurrentTime;
        uint trace = PRNG.Trace(time, round);
        var projectile = new ProjectileData
        {
            TraceId = trace,
            SourceEntity = entity,
            SourceWeapon = null, // Yikes
            Ammo = ammo,
            Origin = origin,
            Direction = direction,
            Speed = Math.Max(ammo.MinSpeed, ammo.ProjectileSpeed), // TODO: Modify ProjectileSpeed by ProjectileSpeedStat?,
            MaxRange = range,
        };
        _shard.Physics.ProjectileRayCast(projectile);
    }

    public void FireProjectile(CharacterEntity entity, uint trace, Vector3 origin, Vector3 direction, Ammo ammo, CharacterEntity.ActiveWeaponDetails weapon)
    {
        if (ammo.ProjectileSpeedStat != 0)
        {
            _logger.Debug("Ammo {ammoId} {ammoName} has ProjectileSpeedStat {speedStatId} specified, this is not implemented! (Fired by {entity} with {weaponDebugName})", ammo.Id, ammo.Name, ammo.ProjectileSpeedStat, entity, weapon.Weapon.DebugName);
        }

        var projectile = new ProjectileData
        {
            TraceId = trace,
            SourceEntity = entity,
            SourceWeapon = weapon,
            Ammo = ammo,
            Origin = origin,
            Direction = direction,
            Speed = Math.Max(ammo.MinSpeed, ammo.ProjectileSpeed), // TODO: Modify ProjectileSpeed by ProjectileSpeedStat?,
            MaxRange = weapon.Weapon.Range,
        };
        _shard.Physics.ProjectileRayCast(projectile);
    }

    public void OnProjectileImpact(ProjectileData projectile, HitData hit)
    {
        if (hit.ImpactEntity != null)
        {
            // Don't hit friendlies
            bool isFriendly = false;
            if (hit.ImpactEntity is BaseEntity impactBaseEntity)
            {
                if (projectile.SourceEntity is BaseEntity sourceBaseEntity)
                {
                    if (sourceBaseEntity.IsFriendly(impactBaseEntity))
                    {
                        isFriendly = true;
                    }
                }
            }

            if (!isFriendly)
            {
                _combatSim.TookWeaponHit(hit.ImpactEntity, projectile, hit);
            }
        }
    }

    public struct ProjectileData
    {
        public uint TraceId;
        public IEntity SourceEntity;
        public CharacterEntity.ActiveWeaponDetails SourceWeapon;
        public Ammo Ammo;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Speed;
        public float MaxRange;
    }

    public struct HitData
    {
        public Vector3 ImpactPosition;
        public IEntity ImpactEntity;
        public bool IsHeadshot;
        public bool IsCrit;
        public float DamageMod;
    }
}