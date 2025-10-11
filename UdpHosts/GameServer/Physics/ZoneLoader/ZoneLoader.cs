using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BepuPhysics;
using BepuUtilities;
using BepuUtilities.Memory;
using Serilog;
using static GameServer.Physics.ZoneLoader.ENWFData;

namespace GameServer.Physics.ZoneLoader;

public class ZoneLoader
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly ILogger _logger;

    public ZoneLoader(Simulation simulation, BufferPool pool, ThreadDispatcher dispatcher, TagfileLoader tagfileLoader, ILogger logger)
    {
        _logger = logger;
        Simulation = simulation;
        BufferPool = pool;
        ThreadDispatcher = dispatcher;
        TagfileLoader = tagfileLoader;

        _serializerOptions.Converters.Add(new TagfileObjectJsonConverter());
        _serializerOptions.Converters.Add(new Vector4Converter());
        _serializerOptions.Converters.Add(new Vector3Converter());
        _serializerOptions.Converters.Add(new StringBooleanConverter());
    }

    public Simulation Simulation { get; protected set; }
    public BufferPool BufferPool { get; private set; }
    public ThreadDispatcher ThreadDispatcher { get; private set; }
    public TagfileLoader TagfileLoader { get; private set; }

    public void LoadCollision(string mapsPath, uint zoneId, Action onComplete)
    {
        Stopwatch stopWatch = new Stopwatch();
        stopWatch.Start();

        var zoneFilePath = $"{mapsPath}\\{zoneId}.pinzone.json";
        PinZone zoneData = LoadZoneJSON(zoneFilePath);
        if (zoneData == null)
        {
            _logger.Error("Failed to load {zoneFilePath}", zoneFilePath);
            return;
        }

        Console.WriteLine($"Loading {zoneData.Chunks.Length} chunks");
        var counter = 0;
        foreach (var chunk in zoneData.Chunks)
        {
            var chunkFilePath = $"{mapsPath}\\chunks\\{chunk.Name}.pinchunk.json";
            var success = LoadChunkJSON(chunk.Origin, chunkFilePath);
            _logger.Debug("({counter}/{total}) Chunk {chunkName} ({successStr})", ++counter, zoneData.Chunks.Length, chunk.Name, success ? "Loaded" : "Failed");
        }

        stopWatch.Stop();
        TimeSpan ts = stopWatch.Elapsed;
        string elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            ts.Hours,
            ts.Minutes,
            ts.Seconds,
            ts.Milliseconds / 10);

        _logger.Debug("LoadCollision Finished in {elapsedTime}", elapsedTime);
        onComplete.Invoke();
    }

    private PinZone LoadZoneJSON(string path)
    {
        _logger.Information("LoadZoneJSON {path}", path);
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PinZone>(json, _serializerOptions);
        }
        catch (Exception e)
        {
            _logger.Error("LoadZoneJSON Failed {exceptionMessage} ({exceptionType})", e.Message, e.GetType().Name);
            return null;
        }
    }

    private bool LoadChunkJSON(Vector3 origin, string path)
    {
        _logger.Debug("LoadChunkJSON {path}", path);
        try
        {
            string json = File.ReadAllText(path);
            PinChunk chunk = JsonSerializer.Deserialize<PinChunk>(json, _serializerOptions);

            foreach (PinChunkSubChunk subChunk in chunk.SubChunks)
            {
                if (subChunk.Cg != null)
                {
                    var myLayer = subChunk.Cg as ITagfileExternalStorage;
                    var root = subChunk.Cg.GetTagfileObject("#0001");
                    var statics = TagfileLoader.TEMP_ProcessChunkObject(root, ref myLayer);
                    for (int i = 0; i < statics.Length; i++)
                    {
                        var stat = statics[i];
                        stat.Pose.Position += origin;
                        Simulation.Statics.Add(stat);
                    }
                }

                // TODO: Process subChunk.Cg2, subChunk.Cg3
            }

            return true;
        }
        catch (Exception e)
        {
            _logger.Error("LoadChunkJSON Failed {exceptionMessage} ({exceptionType}) on {path}\n{more}", e.Message, e.GetType().Name, path, e.StackTrace);
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private class PinZone
    {
        public string Name;

        // public ulong Timestamp
        // public uint GeneratedAt;
        public PinZoneChunk[] Chunks;
        public ENWFLayer[] Imports;
    }

    [StructLayout(LayoutKind.Sequential)]
    private class PinZoneChunk
    {
        public string Name;
        public Vector3 Origin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private class PinChunk
    {
        public string Name;
        public PinChunkSubChunk[] SubChunks;
    }

    [StructLayout(LayoutKind.Sequential)]
    private class PinChunkSubChunk
    {
        public string Name;
        public ENWFLayer Cg;
        public ENWFLayer Cg2 = null;
        public ENWFLayer Cg3 = null;
    }
}