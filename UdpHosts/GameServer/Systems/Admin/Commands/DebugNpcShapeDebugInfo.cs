using System.Numerics;
using AeroMessages.GSS.V66.Character;

namespace GameServer.Admin;

[ServerCommand("Debug NpcShapeDebugInfo", "dbgshape <str>", "dbgshape")]
public class DebugNpcShapeDebugInfo : ServerCommand
{
    public override void Execute(string[] parameters, ServerCommandContext context)
    {
        if (context.SourcePlayer == null || context.SourcePlayer.CharacterEntity == null)
        {
            SourceFeedback("Cannot without a valid player character", context);
            return;
        }

        if (parameters.Length > 3)
        {
            SourceFeedback("Bad params", context);
            return;
        }

        if (context.Target == null)
        {
            SourceFeedback("Target required", context);
            return;
        }

        var character = context.SourcePlayer.CharacterEntity;
        var target = context.Target;



        var typeStr = string.Empty;
        if (parameters.Length >= 1)
        {
            typeStr = parameters[0];
        }

        byte unk1 = 0;
        if (parameters.Length >= 2)
        {
            unk1 = (byte)ParseUIntParameter(parameters[1]);
        }

        byte unk2 = 0;
        if (parameters.Length >= 3)
        {
            unk2 = (byte)ParseUIntParameter(parameters[2]);
        }

        var position = new Vector4(target.Position, 0);

        /*
                AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData[] matrixData = new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData[]
        {
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Matrix = new Vector4[]
                {
                    new Vector4(-0.03702736f, -0.06737494f, 0.9970406f, 0f),
                    new Vector4(0.6072752f, -0.7938831f, -0.0310941f, 0f),
                    new Vector4(0.7936284f, 0.6043266f, 0.07031029f, 0f),
                    new Vector4(-11.43197f, -3.957283f, 1.165523f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Matrix = new Vector4[]
                {
                    new Vector4(-0.07484412f, -0.04050088f, 0.996372f, 0f),
                    new Vector4(0.4593539f, -0.8882511f, -0.001600683f, 0f),
                    new Vector4(0.8850941f, 0.4575678f, 0.08508533f, 0f),
                    new Vector4(-11.43651f, -3.965549f, 1.287872f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Matrix = new Vector4[]
                {
                    new Vector4(-0.01102209f, -0.1495699f, 0.988689f, 0f),
                    new Vector4(0.7225589f, -0.6846764f, -0.09552297f, 0f),
                    new Vector4(0.6912205f, 0.7133336f, 0.1156209f, 0f),
                    new Vector4(-11.43197f, -3.957284f, 1.165529f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Matrix = new Vector4[]
                {
                    new Vector4(0.0569728f, 0.006670356f, 0.9983525f, 0f),
                    new Vector4(0.3293187f, -0.9441342f, -0.01248471f, 0f),
                    new Vector4(0.9424974f, 0.3294877f, -0.05598485f, 0f),
                    new Vector4(-11.44793f, -3.971748f, 1.439976f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.09242851f, 0.6291457f, -0.7717714f, 0f),
                    new Vector4(0.4843631f, 0.648796f, 0.5869033f, 0f),
                    new Vector4(0.8699699f, -0.4280641f, -0.2447677f, 0f),
                    new Vector4(-11.49961f, -3.855649f, 1.067515f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.5300808f, -0.02051964f, -0.847698f, 0f),
                    new Vector4(0.7180769f, 0.5425516f, 0.4358921f, 0f),
                    new Vector4(0.4509749f, -0.839771f, 0.3023312f, 0f),
                    new Vector4(-11.34193f, -4.004974f, 1.046734f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.1816757f, -0.03159598f, 0.9828497f, 0f),
                    new Vector4(-0.7655271f, -0.6318837f, 0.1211901f, 0f),
                    new Vector4(0.6172197f, -0.7744163f, -0.1389838f, 0f),
                    new Vector4(-11.36881f, -3.949971f, 1.87292f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.6991228f, 0.05960613f, -0.7125123f, 0f),
                    new Vector4(-0.6590412f, 0.440193f, -0.6098308f, 0f),
                    new Vector4(0.2772928f, 0.8959219f, 0.3470321f, 0f),
                    new Vector4(-11.5478f, -3.723034f, 1.657978f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(-0.3468292f, -0.1785423f, -0.9207769f, 0f),
                    new Vector4(0.350502f, 0.8859174f, -0.3038066f, 0f),
                    new Vector4(0.8699746f, -0.4281037f, -0.2446824f, 0f),
                    new Vector4(-11.45131f, -3.530771f, 0.6690682f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.1490347f, -0.4858274f, -0.8612542f, 0f),
                    new Vector4(-0.5661833f, -0.7559986f, 0.3284802f, 0f),
                    new Vector4(-0.8106932f, 0.4386736f, -0.3877362f, 0f),
                    new Vector4(-11.36956f, -4.233709f, 1.65132f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(-0.3164957f, -0.4670389f, -0.8256509f, 0f),
                    new Vector4(0.8344767f, 0.2768164f, -0.4764642f, 0f),
                    new Vector4(0.4510802f, -0.8397882f, 0.3021247f, 0f),
                    new Vector4(-11.06765f, -4.015908f, 0.6092566f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.1002241f, 0.7131963f, -0.6937609f, 0f),
                    new Vector4(0.100093f, 0.6865084f, 0.7201993f, 0f),
                    new Vector4(0.9899165f, -0.1416214f, -0.00258112f, 0f),
                    new Vector4(-11.64949f, -3.633239f, 0.1414568f, 1f),
                }
            },
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData {
                Unk2 = new Vector4[]
                {
                    new Vector4(0.5376205f, 0.8199386f, -0.1966321f, 0f),
                    new Vector4(0.2318928f, -0.3679869f, -0.9004495f, 0f),
                    new Vector4(-0.8106721f, 0.4385021f, -0.3879747f, 0f),
                    new Vector4(-11.31695f, -4.404658f, 1.347889f, 1f),
                }
            },

        */
        var shapes = new[]
        {
            new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfoData
            {
                Unk1 = unk1,
                Unk2 = new Vector4[]
                {
                    new Vector4(-0.03702736f, -0.06737494f, 0.9970406f, 0f),
                    new Vector4(0.6072752f, -0.7938831f, -0.0310941f, 0f),
                    new Vector4(0.7936284f, 0.6043266f, 0.07031029f, 0f),
                    new Vector4(-11.43197f, -3.957283f, 1.165523f, 1f),
                },
                Unk3 = position,
                Unk4 = unk2,
            }
        };

        var message = new AeroMessages.GSS.V66.Generic.NpcShapeDebugInfo()
        {
            Unk1 = target.AeroEntityId,
            Unk2 = typeStr,
            Unk3 = shapes
        };

        var player = character.Player;
        player.NetChannels[ChannelType.ReliableGss].SendMessage(message);

        SourceFeedback($"NpcShapeDebugInfo Sent", context);

        /*

        if (character.AttachedTo == null)
        {
            SourceFeedback("Character must be attached", context);
            return;
        }

        if (parameters.Length != 2)
        {
            SourceFeedback("Bad params", context);
            return;
        }

        byte value1 = (byte)ParseUIntParameter(parameters[0]);
        byte value2 = (byte)ParseUIntParameter(parameters[1]);

        var prevData = (AttachedToData)character.AttachedTo;
        character.SetAttachedTo(new AttachedToData
        {
            Id1 = prevData.Id1,
            Id2 = prevData.Id2,
            Role = prevData.Role,
            Unk2 = value1,
            Unk3 = value2,
        },
                                character.AttachedToEntity);
        SourceFeedback($"Setting Unk2 = {value1}, Unk3 = {value2} (Role {prevData.Role})", context);
        */
    }
}