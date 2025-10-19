using System;
using System.Collections.Generic;
using System.IO;

namespace GameServer.Physics.PoseLoader;

public static class IniLoader
{
    public static IniData LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("INI file not found.", path);
        }

        var iniData = new IniData();
        var currentSection = string.Empty;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
            {
                continue;
            }

            // Section
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentSection = line[1..^1].Trim();
                if (!iniData.Sections.ContainsKey(currentSection))
                {
                    iniData.Sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            // Key=Value
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex > 0)
            {
                var key = line.Substring(0, separatorIndex).Trim();
                var value = line[(separatorIndex + 1)..].Trim();

                // Remove surrounding quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                }

                if (!iniData.Sections.ContainsKey(currentSection))
                {
                    iniData.Sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                iniData.Sections[currentSection][key] = value;
            }
        }

        return iniData;
    }
}
