using System.Collections.Generic;
using System.Numerics;

namespace GameServer.Physics;

public interface ITagfile
{
    VertBlockContent[] VertBlocks { get; }
    IndiceBlockContent[] IndiceBlocks { get; }
    Dictionary<string, BaseTagfileObject> TagfileObjects { get; }
}

public struct VertBlockContent
{
    public Vector3[] Verts;
}

public struct IndiceBlockContent
{
    public uint[][] Indices;
}