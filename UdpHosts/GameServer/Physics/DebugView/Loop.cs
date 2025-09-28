using System;
using System.Diagnostics;
using System.Threading;
using BepuUtilities;
using BepuUtilities.Memory;
using DemoRenderer;
using DemoUtilities;

namespace Demos;

public class GameLoop : IDisposable
{
    private bool _disposed;
    private const double TargetFrameTime = 1.0 / 60;

    public GameLoop(Window window)
    {
        Window = window;
        Input = new Input(window, Pool);
        Surface = new RenderSurface(
            window.WindowInfo,
            window.Resolution,
            enableDeviceDebugLayer: false);
        Renderer = new Renderer(Surface);
        Camera = new Camera(window.Resolution.X / (float)window.Resolution.Y, (float)Math.PI / 3, 0.01f, 512);
    }

    public Window Window { get; private set; }
    public Input Input { get; private set; }
    public Camera Camera { get; private set; }
    public RenderSurface Surface { get; private set; }
    public Renderer Renderer { get; private set; }
    public DemoHarness DemoHarness { get; set; }
    public BufferPool Pool { get; } = new BufferPool();

    public void Run(DemoHarness harness)
    {
        DemoHarness = harness;
        Window.Run(Update, OnResize);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Input.Dispose();
            Renderer.Dispose();
            Pool.Clear();
        }
    }

    private void Update(float dt)
    {
        var frameStart = Stopwatch.GetTimestamp();

        Input.Start();
        if (DemoHarness != null)
        {
            // We'll let the delegate's logic handle the variable time steps.
            DemoHarness.Update(dt);

            // At the moment, rendering just follows sequentially. Later on we might want to distinguish it a bit more with fixed time stepping or something. Maybe.
            DemoHarness.Render(Renderer);
        }

        Renderer.Render(Camera);
        Surface.Present();
        Input.End();

        // Cap framerate
        var frameEnd = Stopwatch.GetTimestamp();
        double elapsedSeconds = (frameEnd - frameStart) / (double)Stopwatch.Frequency;
        double remainingTime = TargetFrameTime - elapsedSeconds;

        if (remainingTime > 0)
        {
            // Convert seconds to milliseconds (and sleep a little less to avoid oversleeping)
            int sleepMs = (int)(remainingTime * 1000);
            if (sleepMs > 0)
                Thread.Sleep(sleepMs);
        }
    }

    private void OnResize(Int2 resolution)
    {
        // We just don't support true fullscreen in the demos. Would be pretty pointless.
        Renderer.Surface.Resize(resolution, false);
        Camera.AspectRatio = resolution.X / (float)resolution.Y;
        DemoHarness?.OnResize(resolution);
    }
}
