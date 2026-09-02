using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using PCWheelReceiver.Models;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Output;

/// <summary>
/// Browser-oriented analog gamepad output. Uses a virtual DualShock 4 instead of
/// the Xbox 360 target so browser games get a standard analog gamepad path without
/// reusing the Xbox desktop-layout path that was observed moving the mouse pointer.
/// </summary>
public sealed class DualShock4Output : IControllerOutput, IGameFeedbackSource
{
    private readonly OutputConfig _config;
    private readonly ViGEmClient _client;
    private readonly IDualShock4Controller _controller;
    private readonly Stopwatch _outputClock = Stopwatch.StartNew();
    private long _lastAnalogOutputTicks;
    private float _smoothedSteering;
    private bool _disposed;

    public DualShock4Output(OutputConfig config)
    {
        _config = config;
        _client = new ViGEmClient();
        _controller = _client.CreateDualShock4Controller();
        _controller.AutoSubmitReport = false;
#pragma warning disable CS0618
        _controller.FeedbackReceived += OnFeedbackReceived;
#pragma warning restore CS0618
        _controller.Connect();
        IsConnected = true;
        Status = "DualShock 4 virtual controller connected";
    }

    public bool IsConnected { get; private set; }
    public string Status { get; private set; }
    public event Action<byte, byte>? GameFeedbackReceived;

    private void OnFeedbackReceived(object sender, DualShock4FeedbackReceivedEventArgs e)
    {
        if (_disposed) return;
        GameFeedbackReceived?.Invoke(e.LargeMotor, e.SmallMotor);
    }

    public void Apply(ControllerState state)
    {
        if (_disposed || !IsConnected) return;

        _controller.SetButtonState(DualShock4Button.Cross,
            state.Handbrake >= Math.Clamp(_config.HandbrakeButtonThreshold, 0f, 1f));
        _controller.SetButtonState(DualShock4Button.ShoulderRight, IsBitSet(state.Buttons, _config.ShiftUpBit));
        _controller.SetButtonState(DualShock4Button.ShoulderLeft, IsBitSet(state.Buttons, _config.ShiftDownBit));
        _controller.SetButtonState(DualShock4Button.Circle, IsBitSet(state.Buttons, _config.HornBit));
        _controller.SetButtonState(DualShock4Button.Triangle, IsBitSet(state.Buttons, _config.CameraBit));
        _controller.SetButtonState(DualShock4Button.Square, IsBitSet(state.Buttons, _config.ResetBit));

        if (ShouldUpdateAnalog())
        {
            _controller.SetAxisValue(DualShock4Axis.LeftThumbX, ToStickByte(TransformSteering(state.Steering)));
            _controller.SetAxisValue(DualShock4Axis.LeftThumbY, 128);
            _controller.SetSliderValue(DualShock4Slider.RightTrigger, ToByte(TransformPedal(state.Throttle)));
            _controller.SetSliderValue(DualShock4Slider.LeftTrigger, ToByte(TransformPedal(state.Brake)));

            if (_config.MapClutchToRightStickY)
            {
                // DS4 stick axes are unsigned with 128 at center. Clutch 0..1 maps
                // center..full-positive so it cannot look like a permanently-held axis.
                var clutch = TransformPedal(state.Clutch);
                _controller.SetAxisValue(DualShock4Axis.RightThumbY,
                    (byte)Math.Round(128f + (clutch * 127f)));
            }
            else
            {
                _controller.SetAxisValue(DualShock4Axis.RightThumbY, 128);
            }

            _controller.SetAxisValue(DualShock4Axis.RightThumbX, 128);
        }

        _controller.SubmitReport();
    }

    private float TransformSteering(float raw)
    {
        var value = Math.Clamp(raw, -1f, 1f);
        if (_config.InvertSteering) value = -value;

        var deadzone = Math.Clamp(_config.SteeringDeadzone, 0f, 0.5f);
        var magnitude = Math.Abs(value);
        if (magnitude <= deadzone)
        {
            value = 0f;
        }
        else
        {
            magnitude = (magnitude - deadzone) / Math.Max(0.0001f, 1f - deadzone);
            magnitude = MathF.Pow(magnitude, Math.Clamp(_config.SteeringCurve, 0.25f, 4f));
            magnitude *= Math.Clamp(_config.SteeringSensitivity, 0.1f, 3f);
            value = MathF.CopySign(Math.Clamp(magnitude, 0f, 1f), value);
        }

        var smoothing = Math.Clamp(_config.SteeringSmoothing, 0f, 0.95f);
        _smoothedSteering = (_smoothedSteering * smoothing) + (value * (1f - smoothing));
        return Math.Clamp(_smoothedSteering, -1f, 1f);
    }

    private float TransformPedal(float raw)
    {
        var value = Math.Clamp(raw, 0f, 1f);
        var deadzone = Math.Clamp(_config.PedalDeadzone, 0f, 0.5f);
        if (value <= deadzone) return 0f;
        return Math.Clamp((value - deadzone) / Math.Max(0.0001f, 1f - deadzone), 0f, 1f);
    }

    private bool ShouldUpdateAnalog()
    {
        var cap = Math.Clamp(_config.OutputRateCapHz, 0, 500);
        if (cap <= 0) return true;
        var now = _outputClock.ElapsedTicks;
        var minimumTicks = Math.Max(1L, Stopwatch.Frequency / cap);
        if (now - _lastAnalogOutputTicks < minimumTicks) return false;
        _lastAnalogOutputTicks = now;
        return true;
    }

    private static bool IsBitSet(uint mask, int bit) =>
        bit is >= 0 and < 32 && (mask & (1u << bit)) != 0;

    private static byte ToByte(float value) =>
        (byte)Math.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue);

    private static byte ToStickByte(float value) =>
        (byte)Math.Round((Math.Clamp(value, -1f, 1f) + 1f) * 127.5f);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
#pragma warning disable CS0618
            _controller.FeedbackReceived -= OnFeedbackReceived;
#pragma warning restore CS0618
            if (IsConnected) _controller.Disconnect();
        }
        finally
        {
            (_controller as IDisposable)?.Dispose();
            _client.Dispose();
            IsConnected = false;
            Status = "Disconnected";
        }
    }
}
