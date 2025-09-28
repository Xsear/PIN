using System;
using System.Threading;
using BepuUtilities;
using DemoContentLoader;
using Demos;
using DemoUtilities;
using OpenTK;
using Shared.Udp;

namespace GameServer.Physics;

public class DebugView
{
    private static Window _window;
    private static GameLoop _loop;
    private static DemoHarness _harness;
    private static PhysicsEngine _engine;

    public static void Init(PhysicsEngine engine)
    {
        _engine = engine;
        var Source = new CancellationTokenSource();
        var ct = Source.Token;
        var runThread = Utils.RunThread(RunThreadAsync, ct);
    }

    public static async void RunThreadAsync(CancellationToken ct)
    {
        _window = new Window(
      "PIN BepuPhysics DebugView",
      new Int2((int)(DisplayDevice.Default.Width * 0.75f),
      (int)(DisplayDevice.Default.Height * 0.75f)),
      WindowMode.Windowed);
        _loop = new GameLoop(_window);

        ContentArchive content;
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("GameServer.Physics.DebugView.Demos.contentarchive"))
        {
            content = ContentArchive.Load(stream);
        }

        _harness = new DemoHarness(_loop, content, _engine);
        _loop.Run(_harness);
    }

    public static void Dispose()
    {
        _loop?.Dispose();
        _window?.Dispose();
    }
}