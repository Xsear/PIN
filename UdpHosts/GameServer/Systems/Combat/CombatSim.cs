using AeroMessages.Common;
using AeroMessages.GSS.V66;
using AeroMessages.GSS.V66.Character;
using AeroMessages.GSS.V66.Character.Event;
using GameServer.Entities;
using GameServer.Entities.Character;
using GameServer.Entities.Deployable;
using Serilog;
namespace GameServer.Systems.Combat;

public class CombatSim
{
    private const uint _deadNpcLifetimeMs = 3000;

    private readonly IShard _shard;
    private readonly ILogger _logger;
    private readonly EntityManager _entityMan;

    public CombatSim(IShard shard, ILogger logger, EntityManager entityMan)
    {
        _shard = shard;
        _logger = logger;
        _entityMan = entityMan;
    }

    public void TookWeaponHit(IEntity target, ProjectileSim.ProjectileSim.ProjectileData projectile, ProjectileSim.ProjectileSim.HitData hit)
    {
        // TODO: DamageDecay?
        var weaponDamage = projectile.SourceWeapon?.Weapon.DamagePerRound ?? 0;
        var ammoDamageType = projectile.Ammo.Damagetype;
        var ammoDamageResponse = projectile.Ammo.DamageResponse;
        var source = projectile.SourceEntity;

        var damageValue = 100;
        int damage = (int)(damageValue * hit.DamageMod);

        if (damage <= 0)
        {
            _logger.Warning("Ignoring negative or 0 damage value {Damage}", damage);
            return;
        }

        bool killed = false;

        // Deal damage
        if (target is CharacterEntity targetCharacter)
        {
            bool wasAlive = (targetCharacter.CharacterState.State == CharacterStateData.CharacterStatus.Living || targetCharacter.CharacterState.State == CharacterStateData.CharacterStatus.Incapacitated) && targetCharacter.CurrentHealth > 0;

            if (!wasAlive)
            {
                _logger.Debug("Ignoring TookWeaponHit because target character is not alive");
                return;
            }

            var newHealth = targetCharacter.CurrentHealth - damage;
            targetCharacter.SetCurrentHealth(newHealth);

            if (targetCharacter.CurrentHealth == 0)
            {
                // TODO: Hand over to gamemode logic?
                if (targetCharacter.IsPlayerControlled)
                {
                    if (targetCharacter.CharacterState.State == CharacterStateData.CharacterStatus.Living)
                    {
                        targetCharacter.SetCharacterState(CharacterStateData.CharacterStatus.Incapacitated, _shard.CurrentTime);
                        targetCharacter.SetCurrentHealth(1000); // FIXME: Bleed out health calc?

                        // TODO: On Downed Handler/Event
                    }
                    else if (targetCharacter.CharacterState.State == CharacterStateData.CharacterStatus.Incapacitated)
                    {
                        targetCharacter.SetCharacterState(CharacterStateData.CharacterStatus.Dead, _shard.CurrentTime);

                        // TODO: Trigger respawn timer
                        // TODO: On Dead Handler/Event
                    }
                }
                else
                {
                    targetCharacter.SetCharacterState(CharacterStateData.CharacterStatus.Dead, _shard.CurrentTime);

                    // TODO: NPC On Dead Handler/Event
                    _entityMan.SetRemainingLifetime(targetCharacter, _deadNpcLifetimeMs);
                }
            }

            bool stillAlive = (targetCharacter.CharacterState.State == CharacterStateData.CharacterStatus.Living || targetCharacter.CharacterState.State == CharacterStateData.CharacterStatus.Incapacitated) && targetCharacter.CurrentHealth > 0;
            killed = wasAlive && !stillAlive;
        }

        // Build feedback
        DamageResponseFlags damageFlags = 0;
        if (hit.IsCrit || hit.IsHeadshot)
        {
            damageFlags |= DamageResponseFlags.Critical;
        }

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