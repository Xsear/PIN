#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Serilog;
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

    public static PoseData LoadFromIni(IniData ini, ILogger logger)
    {
        foreach (var (section, data) in ini.Sections)
        {
            logger.Debug("Section {section}", section);
        }

        var file = ini.Sections.GetValueOrDefault("File");
        if (file == null)
        {
            throw new InvalidDataException(".pose file did not contain a File section");
        }

        if (file.GetValueOrDefault("Version") != "1" || file.GetValueOrDefault("Type") != "Pose" || file.GetValueOrDefault("Name") == null)
        {
            foreach (var (field, value) in file)
            {
                logger.Debug("File field {field} - {value}", field, value);
            }
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
                Filename = TryParseFilename(values.GetValueOrDefault("Filename")?.Trim('"') ?? string.Empty),
                Material = TryParseInt(values.GetValueOrDefault("Material")),
            };

            if (values.TryGetValue("Rotation", out var rotStr))
            {
                shape.Rotation = ParseRotation(rotStr);
            }

            shapes[name] = shape;
        }

        return shapes;
    }

    // "00213000\\00213550.HKX";
    public static string TryParseFilename(string input)
    {
        int lastIndex = input.LastIndexOf('\\');
        if (lastIndex >= 0)
        {
            return Path.GetFileNameWithoutExtension(input.Substring(lastIndex + 1));
        }

        return string.Empty;
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
                case "SHIELD":
                    result.Shield = true;
                    break;
                case "AREAONLY":
                    result.AreaOnly = true;
                    break;
                case "SHOOTOUT":
                    result.ShootOut = true;
                    break;
                case "SHOOTIN":
                    result.ShootIn = true;
                    break;
                case "SHOOTTHROUGH":
                    result.ShootThrough = true;
                    break;
                case "INTERACT_ONLY":
                    result.InteractOnly = true;
                    break;
                case "NON_INTERACTIVE":
                    result.NonInteractive = true;
                    break;
                case "TRANSPARENT":
                    result.Transparent = true;
                    break;
                case "BOUNCE_BULLETS":
                    result.BounceBullets = true;
                    break;
                case "REFRACT_BULLETS":
                    result.RefractBullets = true;
                    break;
                case "NPC_WARNING":
                    result.NpcWarning = true;
                    break;
                case "RAGDOLL_QUERY_ONLY":
                    result.RagdollQueryOnly = true;
                    break;
                case "NPC_QUERY_ONLY":
                    result.NpcQueryOnly = true;
                    break;
                case "IGNORE_PROJECTILES":
                    result.IgnoreProjectiles = true;
                    break;
            }
        }

        return result;
    }

    public class ShapeFlags
    {
        public bool Headshot { get; set; }
        public bool Shield { get; set; }
        public bool AreaOnly { get; set; }
        public bool ShootOut { get; set; }
        public bool ShootIn { get; set; }
        public bool ShootThrough { get; set; }
        public bool InteractOnly { get; set; }
        public bool NonInteractive { get; set; }
        public bool Transparent { get; set; }
        public bool BounceBullets { get; set; }
        public bool RefractBullets { get; set; }
        public bool NpcWarning { get; set; }
        public bool RagdollQueryOnly { get; set; }
        public bool NpcQueryOnly { get; set; }
        public bool IgnoreProjectiles { get; set; }
    }

    public class Shape
    {
        public string Name { get; set; } = string.Empty;
        public ShapeType Type { get; set; } = ShapeType.Unknown;
        public ShapeFlags Flags { get; set; } = new ShapeFlags();
        public Vector3 Origin { get; set; }
        public Quaternion? Rotation { get; set; }
        public float? Radius { get; set; }
        public float? Height { get; set; }
        public int? Material { get; set; }
        public float? DamageMod { get; set; }
        public string? HitTagType { get; set; }
        public Vector3? Vertex0 { get; set; }
        public Vector3? Vertex1 { get; set; }
        public Vector3? Vertex2 { get; set; }
        public string Filename { get; set; } = string.Empty;
    }
}