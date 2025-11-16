using System.Collections.Generic;
using AeroMessages.Common;
using AeroMessages.GSS.V66;
using AeroMessages.GSS.V66.Character;
using AeroMessages.GSS.V66.Character.Event;
using BepuPhysics.CollisionDetection;
using GameServer.Data.SDB;
using GameServer.Data.SDB.Records.dbcharacter;
using GameServer.Entities;
using GameServer.Entities.Character;
using GameServer.Entities.Deployable;
using Serilog;

namespace GameServer.Systems.Combat;

public static class FactionHostility
{
    private static bool[,] _factionFriendlyMap;
    private static bool[,] _factionHostileMap;

    private static Dictionary<(uint, uint), bool> _factionFriendlyDict;
    private static Dictionary<(uint, uint), bool> _factionHostileDict;

    public static void Init()
    {
        var factions = SDBInterface.GetFactions();
        var relations = SDBInterface.GetFactionRelations();
        _factionFriendlyDict = new();
        _factionHostileDict = new();
        foreach (var primaryFaction in factions)
        {
            foreach (var secondaryFaction in factions)
            {
                bool friendly = false;
                bool hostile = false;

                if (primaryFaction.DefaultStance == 1)
                {
                    friendly = true;
                }
                else if (primaryFaction.DefaultStance == -1)
                {
                    hostile = true;
                }

                var key = (primaryFaction.Id, secondaryFaction.Id);
                _factionFriendlyDict.Add(key, friendly);
                _factionHostileDict.Add(key, hostile);
            }
        }

        Log.Debug($"FactionHostility initalized");
    }

    public static bool IsFriendlyFaction(uint sourceFactionId, uint targetFactionId)
    {
        var key = (sourceFactionId, targetFactionId);
        var found = _factionFriendlyDict.TryGetValue(key, out bool result);
        if (found)
        {
            return result;
        }
        else
        {
            Log.Warning($"IsFriendlyFaction Failed to get relation {sourceFactionId} - {targetFactionId}");
            return false;
        }
    }

    public static bool IsHostileFaction(uint sourceFactionId, uint targetFactionId)
    {
        var key = (sourceFactionId, targetFactionId);
        var found = _factionHostileDict.TryGetValue(key, out bool result);
        if (found)
        {
            return result;
        }
        else
        {
            Log.Warning($"IsHostileFaction Failed to get relation {sourceFactionId} - {targetFactionId}");
            return false;
        }
    }

    public static void ComputePersonalFactionStance(uint factionId)
    {
        var factions = SDBInterface.GetFactions();
        var totalBytes = (((uint)factions.Count >> 6) + 1) << 3; // 8
        var byteIndex = 0;
        var bitIndex = 0;
        var friendly = new byte[totalBytes];
        var hostile = new byte[totalBytes];
        foreach (var faction in factions)
        {

        }
    }
}