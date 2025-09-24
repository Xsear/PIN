using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace GameServer.Physics.PoseLoader;

public class PoseLoader
{
    private readonly string _assetRoot;
    private readonly ILogger _logger;

    private readonly HashSet<string> _availableFolders;
    private readonly ConcurrentDictionary<string, string> _pathCache = new();
    private readonly ConcurrentDictionary<string, PoseData> _dataCache = new();

    public PoseLoader(string assetRoot, ILogger logger)
    {
        _assetRoot = assetRoot ?? throw new ArgumentNullException(nameof(assetRoot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Directory.Exists(_assetRoot))
        {
            throw new DirectoryNotFoundException($"Asset root not found: {_assetRoot}");
        }

        _availableFolders = new HashSet<string>(
            Directory.GetDirectories(_assetRoot)
                     .Select(Path.GetFileName)
                     .Where(name => name != null && name.All(char.IsDigit))
        );

        _logger.Information("PoseLoader initialized with {FolderCount} folders", _availableFolders.Count);
    }

    public PoseData Load(string assetId)
    {
        if (!TryLoad(assetId, out var result))
        {
            throw new FileNotFoundException($"Pose file not found for ID: {assetId}");
        }

        return result;
    }

    public bool TryLoad(string assetId, out PoseData result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(assetId) || assetId.Length != 8 || !assetId.All(char.IsDigit))
        {
            _logger.Warning("Invalid asset ID format: {AssetId}", assetId);
            return false;
        }

        if (_dataCache.TryGetValue(assetId, out result))
        {
            return true;
        }

        var path = ResolvePath(assetId);
        if (path == null)
        {
            _logger.Warning("Pose file not found for ID {AssetId}", assetId);
            return false;
        }

        try
        {
            var ini = IniLoader.LoadFromFile(path);
            result = PoseData.LoadFromIni(ini);
            _dataCache[assetId] = result;
            _logger.Debug("Loaded pose from {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load pose from file {Path}", path);
            return false;
        }
    }

    public void DoSomething(string id)
    {
        if (TryLoad(id, out var pose))
        {
            // Do something with pose
        }
        else
        {
            // Handle not found
        }
    }

    private string? ResolvePath(string assetId)
    {
        if (_pathCache.TryGetValue(assetId, out var cached))
        {
            return cached;
        }

        string folderName = (int.Parse(assetId) / 1000 * 1000).ToString("D8");

        if (!_availableFolders.Contains(folderName))
        {
            return null;
        }

        string folderPath = Path.Combine(_assetRoot, folderName);
        if (!Directory.Exists(folderPath))
        {
            return null;
        }

        var file = Directory.EnumerateFiles(folderPath)
                            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == assetId);

        if (file != null)
        {
            _pathCache[assetId] = file;
            return file;
        }

        return null;
    }
}
