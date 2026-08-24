using System;
using System.Diagnostics;
using System.Text;
using Serilog;

namespace GameServer;

public enum TickPhase
{
    Total = 0,
    Net,
    Ai,
    Physics,
    EntityMan,
    Encounter,
    Abilities,
    Weapon,
    Projectile,
    Damage,
    Lifecycle,
    Respawn,
    EventBus,
    EntSpawn,
    EntScopeIn,
    EntFlush,
    EntLifetime,
    EntScopeCheck,
    Count,
}

// Stages of a single Bepu sub-step, as executed by MeasuredTimestepper
public enum PhysStage
{
    Sleep = 0,
    Predict,
    Collision,
    Solve,
    Optimize,
    Count,
}

// Single-writer (the shard thread): no locks, no allocations in the steady state.
public sealed class TickStats
{
    private const int ReportIntervalTicks = 300;

    private readonly long[] _ns = new long[(int)TickPhase.Count];
    private readonly long[] _maxNs = new long[(int)TickPhase.Count];
    private readonly long[] _physNs = new long[(int)PhysStage.Count];
    private long _ticks;
    private long _gapNs;
    private long _gapMaxNs;
    private int _flushedEntities;
    private long _flushBytes;
    private int _physSteps;
    private long _physStepMaxNs;
    private int _gc0Base;
    private int _gc1Base;
    private int _gc2Base;

    public TickStats()
    {
        _gc0Base = GC.CollectionCount(0);
        _gc1Base = GC.CollectionCount(1);
        _gc2Base = GC.CollectionCount(2);
    }

    public void Accumulate(TickPhase phase, ref long startTimestamp)
    {
        var now = Stopwatch.GetTimestamp();
        var delta = now - startTimestamp;
        var i = (int)phase;
        _ns[i] += delta;

        if (delta > _maxNs[i])
        {
            _maxNs[i] = delta;
        }

        if (phase == TickPhase.Total)
        {
            _ticks++;
        }

        startTimestamp = now;
    }

    public void AddGap(long sinceTimestamp)
    {
        var delta = Stopwatch.GetTimestamp() - sinceTimestamp;
        _gapNs += delta;

        if (delta > _gapMaxNs)
        {
            _gapMaxNs = delta;
        }
    }

    public void AddFlush(int entities, int bytes)
    {
        _flushedEntities += entities;
        _flushBytes += bytes;
    }

    public void AddPhysStage(PhysStage stage, ref long startTimestamp)
    {
        var now = Stopwatch.GetTimestamp();
        _physNs[(int)stage] += now - startTimestamp;
        startTimestamp = now;
    }

    public void AccumulatePhysStep(ref long startTimestamp)
    {
        var delta = Stopwatch.GetTimestamp() - startTimestamp;
        _physSteps++;

        if (delta > _physStepMaxNs)
        {
            _physStepMaxNs = delta;
        }
    }

    public bool TryReport(ILogger log, ulong instanceId, int entityCount, int clientCount)
    {
        if (_ticks < ReportIntervalTicks)
        {
            return false;
        }

        var freq = (double)Stopwatch.Frequency;

        double Avg(TickPhase phase)
        {
            return _ns[(int)phase] * 1000.0 / freq / _ticks;
        }

        double Max(TickPhase phase)
        {
            return _maxNs[(int)phase] * 1000.0 / freq;
        }

        var sb = new StringBuilder(320);
        sb.Append("[PERF] inst=").Append(instanceId)
            .Append(" tick=").Append(Avg(TickPhase.Total).ToString("0.0")).Append("ms max=").Append(Max(TickPhase.Total).ToString("0.0")).Append("ms")
            .Append(" gap=").Append((_gapNs * 1000.0 / freq / _ticks).ToString("0.0")).Append("ms");
        sb.Append(" | net=").Append(Avg(TickPhase.Net).ToString("0.0"))
            .Append(" ai=").Append(Avg(TickPhase.Ai).ToString("0.0"))
            .Append(" phys=").Append(Avg(TickPhase.Physics).ToString("0.0"))
            .Append(" ent=").Append(Avg(TickPhase.EntityMan).ToString("0.0"))
            .Append(" [sp=").Append(Avg(TickPhase.EntSpawn).ToString("0.0"))
            .Append(" sci=").Append(Avg(TickPhase.EntScopeIn).ToString("0.0"))
            .Append(" fl=").Append(Avg(TickPhase.EntFlush).ToString("0.0"))
            .Append(" lf=").Append(Avg(TickPhase.EntLifetime).ToString("0.0"))
            .Append(" sc=").Append(Avg(TickPhase.EntScopeCheck).ToString("0.0")).Append(']')
            .Append(" enc=").Append(Avg(TickPhase.Encounter).ToString("0.0"))
            .Append(" ab=").Append(Avg(TickPhase.Abilities).ToString("0.0"))
            .Append(" wep=").Append(Avg(TickPhase.Weapon).ToString("0.0"))
            .Append(" proj=").Append(Avg(TickPhase.Projectile).ToString("0.0"))
            .Append(" dmg=").Append(Avg(TickPhase.Damage).ToString("0.0"))
            .Append(" life=").Append(Avg(TickPhase.Lifecycle).ToString("0.0"))
            .Append(" resp=").Append(Avg(TickPhase.Respawn).ToString("0.0"))
            .Append(" evt=").Append(Avg(TickPhase.EventBus).ToString("0.0"));
        sb.Append(" ms | ents=").Append(entityCount)
            .Append(" clients=").Append(clientCount)
            .Append(" flushes=").Append(_flushedEntities)
            .Append(" kb=").Append(_flushBytes / 1024)
            .Append(" heap=").Append(GC.GetTotalMemory(false) / 1048576).Append("MB");

        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        sb.Append(" gc=").Append(gc0 - _gc0Base).Append('/').Append(gc1 - _gc1Base).Append('/').Append(gc2 - _gc2Base)
            .Append(" physSteps=").Append(_physSteps)
            .Append(" physStepMax=").Append((_physStepMaxNs * 1000.0 / freq).ToString("0.0")).Append("ms")
            .Append(" phys[sl=").Append((_physNs[(int)PhysStage.Sleep] * 1000.0 / freq / Math.Max(1, _physSteps)).ToString("0.0"))
            .Append(" pr=").Append((_physNs[(int)PhysStage.Predict] * 1000.0 / freq / Math.Max(1, _physSteps)).ToString("0.0"))
            .Append(" cd=").Append((_physNs[(int)PhysStage.Collision] * 1000.0 / freq / Math.Max(1, _physSteps)).ToString("0.0"))
            .Append(" sv=").Append((_physNs[(int)PhysStage.Solve] * 1000.0 / freq / Math.Max(1, _physSteps)).ToString("0.0"))
            .Append(" op=").Append((_physNs[(int)PhysStage.Optimize] * 1000.0 / freq / Math.Max(1, _physSteps)).ToString("0.0")).Append(']');

        log.Information(sb.ToString());

        Array.Clear(_ns);
        Array.Clear(_maxNs);
        Array.Clear(_physNs);
        _ticks = 0;
        _gapNs = 0;
        _gapMaxNs = 0;
        _flushedEntities = 0;
        _flushBytes = 0;
        _physSteps = 0;
        _physStepMaxNs = 0;
        _gc0Base = gc0;
        _gc1Base = gc1;
        _gc2Base = gc2;

        return true;
    }
}
