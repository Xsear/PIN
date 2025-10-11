using System;
using System.Numerics;
using BepuUtilities;
using DemoContentLoader;
using DemoRenderer;
using DemoRenderer.UI;
using Demos.UI;
using DemoUtilities;
using GameServer.Physics;

namespace Demos;

public class DemoHarness : IDisposable
{
    internal PhysicsEngine _engine;
    internal GameLoop _loop;
    ContentArchive _content;
    internal Controls _controls;
    private Font _font;
    private bool _showControls;
    private bool _showConstraints = true;
    private bool _showContacts;
    private bool _showBoundingBoxes;
    private int _frameCount;
    private bool _disposed;
    private TimingDisplayMode _timingDisplayMode;
    private Graph _timingGraph;
    private SimulationTimeSamples _timeSamples;
    private CameraMoveSpeedState _cameraSpeedState;
    private TextBuilder _uiText = new TextBuilder(128);

    public DemoHarness(GameLoop loop, ContentArchive content, PhysicsEngine engine, Controls? controls = null)
    {
        _loop = loop;
        _content = content;
        _engine = engine;
        _timeSamples = new SimulationTimeSamples(512, loop.Pool);
        if (controls == null)
        {
            _controls = Controls.Default;
        }

        var fontContent = _content.Load<FontContent>(@"Content\Carlito-Regular.ttf");
        _font = new Font(fontContent);

        _timingGraph = new Graph(new GraphDescription
        {
            BodyLineColor = new Vector3(1, 1, 1),
            AxisLabelHeight = 16,
            AxisLineRadius = 0.5f,
            HorizontalAxisLabel = "Frames",
            VerticalAxisLabel = "Time (ms)",
            VerticalIntervalValueScale = 1e3f,
            VerticalIntervalLabelRounding = 2,
            BackgroundLineRadius = 0.125f,
            IntervalTextHeight = 12,
            IntervalTickRadius = 0.25f,
            IntervalTickLength = 6f,
            TargetHorizontalTickCount = 5,
            HorizontalTickTextPadding = 0,
            VerticalTickTextPadding = 3,

            LegendMinimum = new Vector2(20, 200),
            LegendNameHeight = 12,
            LegendLineLength = 7,

            TextColor = new Vector3(1, 1, 1),
            Font = _font,

            LineSpacingMultiplier = 1f,

            ForceVerticalAxisMinimumToZero = true
        });
        _timingGraph.AddSeries("Total", new Vector3(1, 1, 1), 0.75f, _timeSamples.Simulation);
        _timingGraph.AddSeries("Pose Integrator", new Vector3(0, 0, 1), 0.25f, _timeSamples.PoseIntegrator);
        _timingGraph.AddSeries("Sleeper", new Vector3(0.5f, 0, 1), 0.25f, _timeSamples.Sleeper);
        _timingGraph.AddSeries("Broad Update", new Vector3(1, 1, 0), 0.25f, _timeSamples.BroadPhaseUpdate);
        _timingGraph.AddSeries("Collision Test", new Vector3(0, 1, 0), 0.25f, _timeSamples.CollisionTesting);
        _timingGraph.AddSeries("Narrow Flush", new Vector3(1, 0, 1), 0.25f, _timeSamples.NarrowPhaseFlush);
        _timingGraph.AddSeries("Solver", new Vector3(1, 0, 0), 0.5f, _timeSamples.Solver);
        _timingGraph.AddSeries("Batch Compress", new Vector3(0, 0.5f, 0), 0.125f, _timeSamples.BatchCompressor);

        loop.Camera.Position += new Vector3(0, 0, 1.5f);

        OnResize(loop.Window.Resolution);
    }

    private enum TimingDisplayMode
    {
        Regular,
        Big,
        Minimized
    }

    private enum CameraMoveSpeedState
    {
        Regular,
        Slow,
        Fast
    }

    public void OnResize(Int2 resolution)
    {
        UpdateTimingGraphForMode(_timingDisplayMode);
    }

    public void Update(float dt)
    {
        // Don't bother responding to input if the window isn't focused.
        var input = _loop.Input;
        var window = _loop.Window;
        var camera = _loop.Camera;
        if (_loop.Window.Focused)
        {
            if (_controls.Exit.WasTriggered(input))
            {
                window.Close();
                return;
            }

            if (_controls.MoveFaster.WasTriggered(input))
            {
                switch (_cameraSpeedState)
                {
                    case CameraMoveSpeedState.Slow:
                        _cameraSpeedState = CameraMoveSpeedState.Regular;
                        break;
                    case CameraMoveSpeedState.Regular:
                        _cameraSpeedState = CameraMoveSpeedState.Fast;
                        break;
                }
            }

            if (_controls.MoveSlower.WasTriggered(input))
            {
                switch (_cameraSpeedState)
                {
                    case CameraMoveSpeedState.Regular:
                        _cameraSpeedState = CameraMoveSpeedState.Slow;
                        break;
                    case CameraMoveSpeedState.Fast:
                        _cameraSpeedState = CameraMoveSpeedState.Regular;
                        break;
                }
            }

            var cameraOffset = new Vector3();
            if (_controls.MoveForward.IsDown(input))
            {
                cameraOffset += camera.Forward;
            }

            if (_controls.MoveBackward.IsDown(input))
            {
                cameraOffset += camera.Backward;
            }

            if (_controls.MoveLeft.IsDown(input))
            {
                cameraOffset += camera.Left;
            }

            if (_controls.MoveRight.IsDown(input))
            {
                cameraOffset += camera.Right;
            }

            if (_controls.MoveUp.IsDown(input))
            {
                cameraOffset += camera.Up;
            }

            if (_controls.MoveDown.IsDown(input))
            {
                cameraOffset += camera.Down;
            }

            var length = cameraOffset.Length();

            if (length > 1e-7f)
            {
                float cameraMoveSpeed;
                switch (_cameraSpeedState)
                {
                    case CameraMoveSpeedState.Slow:
                        cameraMoveSpeed = _controls.CameraSlowMoveSpeed;
                        break;
                    case CameraMoveSpeedState.Fast:
                        cameraMoveSpeed = _controls.CameraFastMoveSpeed;
                        break;
                    default:
                        cameraMoveSpeed = _controls.CameraMoveSpeed;
                        break;
                }

                cameraOffset *= dt * cameraMoveSpeed / length;
            }
            else
            {
                cameraOffset = new Vector3();
            }

            camera.Position += cameraOffset;

            var grabRotationIsActive = _controls.Grab.IsDown(input) && _controls.GrabRotate.IsDown(input);

            // Don't turn the camera while rotating a grabbed object.
            if (!grabRotationIsActive)
            {
                if (input.MouseLocked)
                {
                    var delta = input.MouseDelta;
                    if (delta.X != 0 || delta.Y != 0)
                    {
                        camera.Yaw += delta.X * _controls.MouseSensitivity;
                        camera.Pitch += delta.Y * _controls.MouseSensitivity;
                    }
                }
            }

            if (_controls.LockMouse.WasTriggered(input))
            {
                input.MouseLocked = !input.MouseLocked;
            }

            if (_controls.ShowControls.WasTriggered(input))
            {
                _showControls = !_showControls;
            }

            if (_controls.ShowConstraints.WasTriggered(input))
            {
                _showConstraints = !_showConstraints;
            }

            if (_controls.ShowContacts.WasTriggered(input))
            {
                _showContacts = !_showContacts;
            }

            if (_controls.ShowBoundingBoxes.WasTriggered(input))
            {
                _showBoundingBoxes = !_showBoundingBoxes;
            }

            if (_controls.ChangeTimingDisplayMode.WasTriggered(input))
            {
                var newDisplayMode = (int)_timingDisplayMode + 1;
                if (newDisplayMode > 2)
                {
                    newDisplayMode = 0;
                }

                UpdateTimingGraphForMode((TimingDisplayMode)newDisplayMode);
            }
        }
        else
        {
            input.MouseLocked = false;
        }

        ++_frameCount;
        if (!_controls.SlowTimesteps.IsDown(input) || _frameCount % 3 == 0)
        {
            // Any additional Update Logic Here!
            if (_engine.DebugViewEntity != 0)
            {
                if (!input.MouseLocked)
                {
                    float halfHeight = 0.9f;
                    float radius = 0.3f;
                    float cameraBackwardOffsetScale = 4f; // Tweak this as needed

                    var characterPosition = _engine.DebugViewPose.Position;
                    var characterOrientation = _engine.DebugViewPose.Orientation;
                    var characterForward = Vector3.Transform(Vector3.UnitY, characterOrientation);
                    var characterBehind = -characterForward;
                    var characterPitch = _engine.DebugViewHeading[1];

                    var cameraOrientation = characterOrientation;
                    var cameraRight = Vector3.Transform(Vector3.UnitX, cameraOrientation);
                    var cameraPitch = Quaternion.CreateFromAxisAngle(cameraRight, characterPitch);

                    camera.Position = characterPosition
                    + new Vector3(0, halfHeight, 0)
                    + (camera.Up * (radius * 1.2f))
                    - (camera.Forward * (halfHeight + radius) * cameraBackwardOffsetScale);

                    camera.OrientationQuaternion = cameraPitch * cameraOrientation;
                }
            }
        }

        _timeSamples.RecordFrame(_engine.Simulation);
    }

    public void Render(Renderer renderer)
    {
        // Clear first so that any demo-specific logic doesn't get lost.
        renderer.Shapes.ClearInstances();
        renderer.Lines.ClearInstances();

        // Put Any additional Rendering Here!
        // Common rendering
#if DEBUG
        float warningHeight = 15f;
        renderer.TextBatcher.Write(_uiText.Clear().Append("Running in Debug configuration. Compile in Release configuration for performance testing."),
            new Vector2((_loop.Window.Resolution.X - GlyphBatch.MeasureLength(_uiText, _font, warningHeight)) * 0.5f, warningHeight),
            warningHeight,
            new Vector3(1, 0, 0),
            _font);
#endif            
        float textHeight = 16;
        float lineSpacing = textHeight * 1.0f;
        var textColor = new Vector3(1, 1, 1);
        if (_showControls)
        {
            var penPosition = new Vector2(_loop.Window.Resolution.X - (textHeight * 6) - 25, _loop.Window.Resolution.Y - 25);
            penPosition.Y -= 19 * lineSpacing;
            _uiText.Clear().Append("Controls: ");
            var headerHeight = textHeight * 1.2f;
            renderer.TextBatcher.Write(_uiText, penPosition - new Vector2(0.5f * GlyphBatch.MeasureLength(_uiText, _font, headerHeight), 0), headerHeight, textColor, _font);
            penPosition.Y += lineSpacing;

            var controlPosition = penPosition;
            controlPosition.X += textHeight * 0.5f;

            void WriteInstantName(string controlName, InstantBind control)
            {
                _uiText.Clear().Append(controlName).Append(":");
                renderer.TextBatcher.Write(_uiText, penPosition - new Vector2(GlyphBatch.MeasureLength(_uiText, _font, textHeight), 0), textHeight, textColor, _font);
                penPosition.Y += lineSpacing;

                control.AppendString(_uiText.Clear());
                renderer.TextBatcher.Write(_uiText, controlPosition, textHeight, textColor, _font);
                controlPosition.Y += lineSpacing;
            }

            void WriteHoldableName(string controlName, HoldableBind control)
            {
                _uiText.Clear().Append(controlName).Append(":");
                renderer.TextBatcher.Write(_uiText, penPosition - new Vector2(GlyphBatch.MeasureLength(_uiText, _font, textHeight), 0), textHeight, textColor, _font);
                penPosition.Y += lineSpacing;

                control.AppendString(_uiText.Clear());
                renderer.TextBatcher.Write(_uiText, controlPosition, textHeight, textColor, _font);
                controlPosition.Y += lineSpacing;
            }

            WriteInstantName(nameof(_controls.LockMouse), _controls.LockMouse);
            WriteHoldableName(nameof(_controls.Grab), _controls.Grab);
            WriteHoldableName(nameof(_controls.GrabRotate), _controls.GrabRotate);
            WriteHoldableName(nameof(_controls.MoveForward), _controls.MoveForward);
            WriteHoldableName(nameof(_controls.MoveBackward), _controls.MoveBackward);
            WriteHoldableName(nameof(_controls.MoveLeft), _controls.MoveLeft);
            WriteHoldableName(nameof(_controls.MoveRight), _controls.MoveRight);
            WriteHoldableName(nameof(_controls.MoveUp), _controls.MoveUp);
            WriteHoldableName(nameof(_controls.MoveDown), _controls.MoveDown);
            WriteInstantName(nameof(_controls.MoveSlower), _controls.MoveSlower);
            WriteInstantName(nameof(_controls.MoveFaster), _controls.MoveFaster);
            WriteHoldableName(nameof(_controls.SlowTimesteps), _controls.SlowTimesteps);
            WriteInstantName(nameof(_controls.Exit), _controls.Exit);
            WriteInstantName(nameof(_controls.ShowConstraints), _controls.ShowConstraints);
            WriteInstantName(nameof(_controls.ShowContacts), _controls.ShowContacts);
            WriteInstantName(nameof(_controls.ShowBoundingBoxes), _controls.ShowBoundingBoxes);
            WriteInstantName(nameof(_controls.ChangeTimingDisplayMode), _controls.ChangeTimingDisplayMode);
            WriteInstantName(nameof(_controls.ChangeDemo), _controls.ChangeDemo);
            WriteInstantName(nameof(_controls.ShowControls), _controls.ShowControls);
        }
        else
        {
            _controls.ShowControls.AppendString(_uiText.Clear().Append("Press ")).Append(" for controls.");
            const float inset = 25;
            renderer.TextBatcher.Write(_uiText,
                new Vector2(_loop.Window.Resolution.X - inset - GlyphBatch.MeasureLength(_uiText, _font, textHeight), _loop.Window.Resolution.Y - inset),
                textHeight,
                textColor,
                _font);
        }

        if (_timingDisplayMode != TimingDisplayMode.Minimized)
        {
            _timingGraph.Draw(_uiText, renderer.UILineBatcher, renderer.TextBatcher);
        }
        else
        {
            const float timingTextSize = 14;
            const float inset = 25;
            renderer.TextBatcher.Write(
                _uiText.Clear().Append(1e3 * _timeSamples.Simulation[_timeSamples.Simulation.End - 1], _timingGraph.Description.VerticalIntervalLabelRounding).Append(" ms/step"),
                new Vector2(_loop.Window.Resolution.X - inset - GlyphBatch.MeasureLength(_uiText, _font, timingTextSize), inset),
                timingTextSize,
                _timingGraph.Description.TextColor,
                _font);
        }

        if (true)
        {
            float posHeight = 20f;
            var camera = _loop.Camera;
            var pos = camera.Position;
            renderer.TextBatcher.Write(_uiText.Clear().Append($"CAMERA POSITION: {pos}"),
                new Vector2((_loop.Window.Resolution.X - GlyphBatch.MeasureLength(_uiText, _font, posHeight)) * 0.5f, posHeight),
                posHeight,
                new Vector3(1, 0, 0),
                _font);
        }

        renderer.Shapes.AddInstances(_engine.Simulation, _engine.DebugThreadDispatcher);
        renderer.Lines.ShowConstraints = _showConstraints;
        renderer.Lines.ShowContacts = _showContacts;
        renderer.Lines.ShowBoundingBoxes = _showBoundingBoxes;
        renderer.Lines.Extract(_engine.Simulation, _engine.DebugThreadDispatcher);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _timeSamples.Dispose();
            _font.Dispose();
        }
    }

    private void UpdateTimingGraphForMode(TimingDisplayMode newDisplayMode)
    {
        _timingDisplayMode = newDisplayMode;
        ref var description = ref _timingGraph.Description;
        var resolution = _loop.Window.Resolution;
        switch (_timingDisplayMode)
        {
            case TimingDisplayMode.Big:
                {
                    const float inset = 150;
                    description.BodyMinimum = new Vector2(inset);
                    description.BodySpan = new Vector2(resolution.X, resolution.Y) - description.BodyMinimum - new Vector2(inset);
                    description.LegendMinimum = description.BodyMinimum - new Vector2(110, 0);
                    description.TargetVerticalTickCount = 5;
                }

                break;
            case TimingDisplayMode.Regular:
                {
                    const float inset = 50;
                    var targetSpan = new Vector2(400, 150);
                    description.BodyMinimum = new Vector2(resolution.X - targetSpan.X - inset, inset);
                    description.BodySpan = targetSpan;
                    description.LegendMinimum = description.BodyMinimum - new Vector2(130, 0);
                    description.TargetVerticalTickCount = 3;
                }

                break;
        }
    }
}
