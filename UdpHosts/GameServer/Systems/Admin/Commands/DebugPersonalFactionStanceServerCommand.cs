using System.Text;
using AeroMessages.GSS.V66;
using GameServer.Data.SDB;
using Serilog;

namespace GameServer.Admin;

[ServerCommand("Debug PersonalFactionStance", "dbgpfs [<1|2> <factionId>]", "dbgpfs")]
public class DebugPersonalFactionStanceServerCommand : ServerCommand
{
    public override void Execute(string[] parameters, ServerCommandContext context)
    {
        if (context.SourcePlayer == null || context.SourcePlayer.CharacterEntity == null)
        {
            SourceFeedback("Cannot without a valid player character", context);
            return;
        }

        if (parameters.Length != 2)
        {
            PrintState(context);
            return;
        }

        var character = context.SourcePlayer.CharacterEntity;
        byte value1 = (byte)ParseUIntParameter(parameters[0]);
        byte factionId = (byte)ParseUIntParameter(parameters[1]);

        if (value1 != 1 && value1 != 2)
        {
            SourceFeedback("Param 1 indicates bitfield and should be 1 or 2", context);
            return;
        }

        if (factionId < 1 || factionId > 50)
        {
            SourceFeedback("Param 2 indicates factionId and must be in the 1-50 range (valid faction ids)", context);
            return;
        }

        // Get the bitfield
        var prevData = (PersonalFactionStanceData)character.Character_BaseController.PersonalFactionStanceProp;
        PersonalFactionStanceBitfield field = value1 == 1 ? prevData.Friendly : prevData.Hostile;

        // Get the faction value
        byte index = (byte)(factionId - 1);
        byte bitIndex = (byte)(index % 8);
        byte byteIndex = (byte)(index / 8);
        var prevValue = (field.Bitfield[byteIndex] & (1 << bitIndex)) != 0;

        // Invert the faction value
        if (prevValue == false)
        {
            field.Bitfield[byteIndex] |= (byte)(1 << bitIndex);
        }
        else
        {
            field.Bitfield[byteIndex] &= (byte)~(1 << bitIndex);
        }

        // Update the field value
        var newData = prevData;
        if (value1 == 1)
        {
            newData.Friendly = field;
        }
        else
        {
            newData.Hostile = field;
        }

        // Sync
        character.Character_ObserverView.PersonalFactionStanceProp = newData;
        character.Character_BaseController.PersonalFactionStanceProp = newData;

        // Report
        var newValue = (field.Bitfield[byteIndex] & (1 << bitIndex)) != 0;
        var factionDef = SDBInterface.GetFaction(factionId);
        var factionInternalName = factionDef.InternalName;
        SourceFeedback($"Set field {value1} faction {factionId} ({factionInternalName}) from prev {prevValue} to {newValue}", context);
    }

    private void PrintState(ServerCommandContext context)
    {
        var character = context.SourcePlayer.CharacterEntity;
        var data = (PersonalFactionStanceData)character.Character_BaseController.PersonalFactionStanceProp;

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Friendly:");
        BuildFieldState(builder, data.Friendly);
        builder.AppendLine("Hostile:");
        BuildFieldState(builder, data.Hostile);
        var message = builder.ToString();

        Log.Information(message);

        context.SourcePlayer.SendDebugLog(message);
        context.SourcePlayer.SendDebugChat("PFS State printed to console");

        return;
    }

    private void BuildFieldState(StringBuilder builder, PersonalFactionStanceBitfield field)
    {
        for (byte index = 0; index < 50; index++)
        {
            byte bitIndex = (byte)(index % 8);
            byte byteIndex = (byte)(index / 8);
            var factionId = (uint)index + 1;
            var factionDef = SDBInterface.GetFaction(factionId);
            var factionInternalName = factionDef.InternalName;
            var bitValue = (field.Bitfield[byteIndex] & (1 << bitIndex)) != 0;

            builder.AppendLine($"byte {byteIndex} bit {bitIndex} value {bitValue} faction {factionId} ({factionInternalName})");
        }
    }
}