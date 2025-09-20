using System;
using System.Numerics;
using AeroMessages.GSS.V66.Character.Event;
using GameServer.Data.SDB;
using GameServer.Data.SDB.Records.aptfs;
using GameServer.Entities.Character;

namespace GameServer.Aptitude;

public class FireProjectileCommand : Command, ICommand
{
    private FireProjectileCommandDef Params;

    public FireProjectileCommand(FireProjectileCommandDef par)
: base(par)
    {
        Params = par;
    }

    public bool Execute(Context context)
    {
        // Invoke sending ability projectile and communicate to remote players
        if (context.Self is CharacterEntity character)
        {
            // TODO: Lots of Params
            var ammo = SDBInterface.GetAmmo(Params.Ammotype);
            var aim = character.AimDirection;
            var origin = character.GetProjectileOrigin(aim);
            var range = Params.Range;
            var velocity = Vector3.Zero;
            var time = context.Shard.CurrentTime + 50; // Time diff seen ingame and in capture, n=1. The game client sends a FireWeaponProjectile with a time.
            context.Shard.ProjectileSim.FireProjectileCommand(character, ammo, origin, aim, range);

            context.Shard.EntityMan.SendToScoped(character, new WeaponProjectileFired
            {
                ShortTime = (ushort)time,
                Aim = aim,
                HaveShooterVelocity = velocity == Vector3.Zero ? (byte)0 : (byte)1,
                ShooterVelocity = velocity
            });

            context.Shard.EntityMan.SendToScoped(character, new AbilityProjectileFired
            {
                ShortTime = (ushort)time,
                Aim = aim,
                MaybeHalfs = velocity,
                AmmoType = (ushort)Params.Ammotype,
                Range = range,
                Unk1 = 0,
                Unk2 = 1,
                Unk3 = 0.0f,
                Unk4 = 156,
                Unk5 = 0,
                Hardpoint = Params.Hardpoint,
                UnkFlag = 0,
                UnkFlaggedEntity = 0,
            });

            Console.WriteLine($"AbilityProjectileFired {ammo.Name} ({Params.Ammotype}) at {time}");
        }
        else
        {
            Console.WriteLine("FireProjectileCommand cant handle this source entity");
        }

        return true;
    }
}