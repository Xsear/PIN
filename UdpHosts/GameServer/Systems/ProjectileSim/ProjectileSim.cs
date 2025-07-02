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

    public void FireProjectile(CharacterEntity entity, uint trace, Vector3 origin, Vector3 direction, Ammo ammo)
    {
        _shard.Physics.ProjectileRayCast(origin, direction, entity, trace, ammo);
    }

    public void OnProjectileImpact(IEntity sourceEntity, Ammo ammo, Vector3 impactPosition, IEntity impactEntity)
    {
        var damageValue = 1337;
        _combatSim.TookWeaponHit(impactEntity, damageValue, sourceEntity);
    }
}