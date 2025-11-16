using System.Numerics;
using AeroMessages.Common;
using GameServer.Data.SDB;
using GameServer.Systems.Combat;

namespace GameServer.Entities;

public class BaseEntity : IEntity
{
    public BaseEntity(IShard shard, ulong id)
    {
        Shard = shard;
        EntityId = id;
        AeroEntityId = new EntityId() { Backing = EntityId, ControllerId = Controller.Generic };
        Hostility = new HostilityComponent();
    }

    public ulong EntityId { get; }
    public EntityId AeroEntityId { get; protected set; }
    public IShard Shard { get; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public bool HasPhysicsBody { get; set; }

    public InteractionComponent Interaction { get; set; }
    public ScopingComponent Scoping { get; set; }
    public EncounterComponent Encounter { get; set; }
    public HostilityComponent Hostility { get; set; }

    public virtual bool IsInteractable()
    {
        return false;
    }

    public virtual bool CanBeInteractedBy(IEntity other)
    {
        return false;
    }

    public byte GetInteractionType()
    {
        return (Interaction != null) ? (byte)Interaction.Type : (byte)0;
    }

    public uint GetInteractionDuration()
    {
        return (Interaction != null) ? Interaction.DurationMs : 0;
    }

    public bool IsGlobalScope()
    {
        return (Scoping != null) && Scoping.Global;
    }

    public float GetScopeRange()
    {
        return (Scoping != null) ? Scoping.Range : 100f;
    }

    public bool IsHostile(uint otherFactionId)
    {
        return FactionHostility.IsHostileFaction(Hostility.FactionId, otherFactionId);
    }

    public bool IsFriendly(uint otherFactionId)
    {
        return FactionHostility.IsFriendlyFaction(Hostility.FactionId, otherFactionId);
    }

    public bool IsNeutral(uint otherFactionId)
    {
        var hostile = IsHostile(otherFactionId);
        var friendly = IsFriendly(otherFactionId);
        return !hostile && !friendly;
    }

    public bool IsFriendly(BaseEntity otherEntity)
    {
        return IsFriendly(otherEntity.Hostility.FactionId);
    }

    public bool IsHostile(BaseEntity otherEntity)
    {
        return IsHostile(otherEntity.Hostility.FactionId);
    }

    public bool IsNeutral(BaseEntity otherEntity)
    {
        return IsNeutral(otherEntity.Hostility.FactionId);
    }
}
