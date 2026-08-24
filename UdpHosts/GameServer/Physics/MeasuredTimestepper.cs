using System.Diagnostics;
using BepuPhysics;
using BepuUtilities;

namespace GameServer.Physics;

// Mirrors DefaultTimestepper's stage order, timing each stage into TickStats
public sealed class MeasuredTimestepper : ITimestepper
{
    private readonly TickStats _stats;

    public event TimestepperStageHandler BeforeCollisionDetection;

    public event TimestepperStageHandler CollisionsDetected;

    public MeasuredTimestepper(TickStats stats)
    {
        _stats = stats;
    }

    public void Timestep(Simulation simulation, float dt, IThreadDispatcher threadDispatcher = null)
    {
        var t = Stopwatch.GetTimestamp();
        var stepStart = t;

        simulation.Sleep(threadDispatcher);
        _stats.AddPhysStage(PhysStage.Sleep, ref t);

        simulation.PredictBoundingBoxes(dt, threadDispatcher);
        _stats.AddPhysStage(PhysStage.Predict, ref t);

        BeforeCollisionDetection?.Invoke(dt, threadDispatcher);

        simulation.CollisionDetection(dt, threadDispatcher);
        _stats.AddPhysStage(PhysStage.Collision, ref t);

        CollisionsDetected?.Invoke(dt, threadDispatcher);

        simulation.Solve(dt, threadDispatcher);
        _stats.AddPhysStage(PhysStage.Solve, ref t);

        simulation.IncrementallyOptimizeDataStructures(threadDispatcher);
        _stats.AddPhysStage(PhysStage.Optimize, ref t);

        _stats.AccumulatePhysStep(ref stepStart);
    }
}
