using GameServer.Data.SDB.Records.aptfs;
using GameServer.Entities.Character;

namespace GameServer.Aptitude;

public class DeployableCalldownCommand : Command, ICommand
{
    private DeployableCalldownCommandDef Params;

    public DeployableCalldownCommand(DeployableCalldownCommandDef par)
: base(par)
    {
        Params = par;
    }

    public bool Execute(Context context)
    {
        var caller = context.Self;
        var request = context.Abilities.TryConsumeDeployableCalldownRequest(caller.EntityId);
        if (request != null)
        {
            var entityMan = context.Shard.EntityMan;
            var typeId = Params.DeployableType;
            var position = request.Position;
            var orientation = request.Rotation;
            var useOwnerFaction = Params.UseOwnerFaction == 1;
            var factionOverride = Params.OverrideFactionId;
            entityMan.SpawnDeployable(typeId, position, orientation, caller as CharacterEntity, useOwnerFaction, factionOverride);

            return true;
        }
        else
        {
            return false;
        }
    }
}