using System.Numerics;
using GameServer.Data.SDB.Records.dbcharacter;

namespace GameServer;

public class PhysicsInfo
{
    public uint HitboxCollisionId;
    public float Scale = 1f;
}

public class CharacterPhysicsInfo : PhysicsInfo
{
    public PoseType PoseTypeRecord;
    public bool RequiresRagdoll;
    public uint RagdollCollisionId;
    public uint AttachmentPoseId;
    public Vector3 AttachmentPoseOffset;
}