using AeroMessages.Common;
using AeroMessages.GSS.V66;
using AeroMessages.GSS.V66.Character.Event;
using GameServer.Entities;
using GameServer.Entities.Character;
using GameServer.Entities.Deployable;
using Serilog;

namespace GameServer.Systems.Combat;

public class CombatSim
{
    private readonly IShard _shard;
    private readonly ILogger _logger;
    private readonly EntityManager _entityMan;

    public CombatSim(IShard shard, ILogger logger, EntityManager entityMan)
    {
        _shard = shard;
        _logger = logger;
        _entityMan = entityMan;
    }

    public void TookWeaponHit(IEntity target, int damage, IEntity source = null)
    {
        if (damage <= 0)
        {
            _logger.Warning("Ignoring negative or 0 damage value {Damage}", damage);
            return;
        }

        // Deal damage
        if (target is CharacterEntity targetCharacter)
        {
            var newHealth = targetCharacter.CurrentHealth - damage;
            targetCharacter.SetCurrentHealth(newHealth);
        }

        // Build feedback
        DamageResponseFlags damageFlags = 0;
        ushort shortTime = _shard.CurrentShortTime;
        byte unk2 = 0;
        DamageHitStruct damageData = new()
        {
            Target = target.AeroEntityId,
            HaveDealer = (byte)(source != null ? 1 : 0),
            Dealer = source != null ? source.AeroEntityId : new EntityId(),
            DamageValue = damage,
        };

        // Player Dealt Hit Feedback
        if (source is CharacterEntity sourceCharacter && sourceCharacter.IsPlayerControlled)
        {
            var player = sourceCharacter.Player;
            player.NetChannels[ChannelType.ReliableGss].SendMessage(new DealtHit
                {
                    HaveDamage = 1,
                    DamageData = damageData,
                    DamageFlags = damageFlags,
                },
                sourceCharacter.EntityId);
        }

        // Target Took Hit Feedback
        if (target is CharacterEntity || target is DeployableEntity)
        {
            _entityMan.SendToScoped(target,
                new TookHit
                {
                    HaveDamage = 1,
                    DamageData = damageData,
                    DamageFlags = damageFlags,
                    ShortTime = shortTime,
                    Unk2 = unk2,
                });
        }
    }

    public void TookAbilityHit()
    {
    }

    public void TookCollisionHit()
    {
    }
}