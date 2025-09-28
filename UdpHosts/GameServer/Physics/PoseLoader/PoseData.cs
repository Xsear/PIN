#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using static GameServer.Physics.PoseLoader.PoseUtil;

namespace GameServer.Physics.PoseLoader;

public class PoseData
{
    public string Name;
    public Dictionary<string, Shape> Shapes;

    public enum ShapeType
    {
        Sphere,
        Cylinder,
        Capsule,
        Triangle,
        HKX,
        Unknown
    }

    public static PoseData LoadFromIni(IniData ini)
    {
        var file = ini.Sections.GetValueOrDefault("File");
        if (file == null)
        {
            throw new InvalidDataException(".pose file did not contain a File section");
        }

        if (file.GetValueOrDefault("Version") != "1" || file.GetValueOrDefault("Type") != "Pose" || file.GetValueOrDefault("Name") == null)
        {
            throw new InvalidDataException(".pose file invalid File section");
        }

        var result = new PoseData();
        result.Name = file.GetValueOrDefault("Name") ?? string.Empty;
        result.Shapes = MapShapes(ini);

        return result;
    }

    public static Dictionary<string, Shape> MapShapes(IniData ini)
    {
        var shapes = new Dictionary<string, Shape>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in ini.Sections)
        {
            if (!section.Key.StartsWith("Shape-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = section.Key["Shape-".Length..]; // Remove "Shape-" prefix
            var values = section.Value;

            var shape = new Shape
            {
                Name = name,
                Type = Enum.TryParse<ShapeType>(values.GetValueOrDefault("Type")?.Trim('"'), true, out var type)
                    ? type
                    : ShapeType.Unknown,
                Flags = ParseFlags(values.GetValueOrDefault("Flags") ?? string.Empty),
                Origin = ParseVector3(values.GetValueOrDefault("Origin") ?? "<0 0 0>"),
                Radius = TryParseFloat(values.GetValueOrDefault("Radius")),
                Height = TryParseFloat(values.GetValueOrDefault("Height")),
                Material = TryParseInt(values.GetValueOrDefault("Material"))
            };

            if (values.TryGetValue("Rotation", out var rotStr))
            {
                shape.Rotation = ParseMatrix3x3(rotStr);
            }

            shapes[name] = shape;
        }

        return shapes;
    }

    public static ShapeFlags ParseFlags(string flagString)
    {
        var flags = flagString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new ShapeFlags();

        foreach (var flag in flags)
        {
            switch (flag.ToUpperInvariant())
            {
                case "HEADSHOT":
                    result.Headshot = true;
                    break;
                case "RAGDOLL_QUERY_ONLY":
                    result.RagdollQueryOnly = true;
                    break;
                case "NPC_WARNING":
                    result.NPCWarning = true;
                    break;
            }
        }

        return result;
    }

    public class ShapeFlags
    {
        public bool Headshot { get; set; }
        public bool RagdollQueryOnly { get; set; }
        public bool NPCWarning { get; set; }
    }

    public class Shape
    {
        public string Name { get; set; } = string.Empty;
        public ShapeType Type { get; set; } = ShapeType.Unknown;
        public ShapeFlags Flags { get; set; } = new ShapeFlags();
        public Vector3 Origin { get; set; }
        public Matrix3x3? Rotation { get; set; } // Optional
        public float? Radius { get; set; }
        public float? Height { get; set; }
        public int? Material { get; set; }
    }
}