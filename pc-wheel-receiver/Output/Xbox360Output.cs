using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PCWheelReceiver.Models;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Output;

public sealed class Xbox360Output : IControllerOutput
{
    private readonly OutputConfig _config;
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _controller;
    private readonly Stopwatch _outputClock = Stopwatch.StartNew();
    private long _lastAnalogOutputTicks;
    private long _lastPredictionTicks;
    private float _lastPredictionSample;
    private float _smoothedSteering;
    private bool _disposed;

    public Xbox360Output(OutputConfig config)
    {
        _config = config;
        _client = new ViGEmClient();
        _controller = _client.CreateXbox360Controller();
        _controller.Connect();
        IsConnected = true;
        Status = "Xbox 360 virtual controller connected";
    }

    public bool IsConnected { get; private set; }
    public string Status { get; private set; }

    public void Apply(ControllerState state)
    {
        if (_disposed || !IsConnected) return;

        // Buttons are always updated so a low analog rate cap cannot swallow short presses.
        _controller.SetButtonState(Xbox360Button.A,
            state.Handbrake >= Math.Clamp(_config.HandbrakeButtonThreshold, 0f, 1f));
        _controller.SetButtonState(Xbox360Button.RightShoulder, IsBitSet(state.Buttons, _config.ShiftUpBit));
        _controller.SetButtonState(Xbox360Button.LeftShoulder, IsBitSet(state.Buttons, _config.ShiftDownBit));
        _controller.SetButtonState(Xbox360Button.B, IsBitSet(state.Buttons, _config.HornBit));
        _controller.SetButtonState(Xbox360Button.Y, IsBitSet(state.Buttons, _config.CameraBit));
        _controller.SetButtonState(Xbox360Button.X, IsBitSet(state.Buttons, _config.ResetBit));

        if (!ShouldUpdateAnalog()) return;

        var steeringValue = TransformSteering(state.Steering);
        var steering = steeringValue >= 0
            ? (short)Math.Round(steeringValue * short.MaxValue)
            : (short)Math.Round(steeringValue * -short.MinValue);

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, steering);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, ToByte(TransformPedal(state.Throttle)));
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ToByte(TransformPedal(state.Brake)));

        if (_config.MapClutchToRightStickY)
        {
            _controller.SetAxisValue(Xbox360Axis.RightThumbY,
                (short)Math.Round(TransformPedal(state.Clutch) * short.MaxValue));
        }
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
            var curve = Math.Clamp(_config.SteeringCurve, 0.25f, 4f);
            magnitude = MathF.Pow(magnitude, curve);
            magnitude *= Math.Clamp(_config.SteeringSensitivity, 0.1f, 3f);
            value = MathF.CopySign(Math.Clamp(magnitude, 0f, 1f), value);
        }

        value = ApplySteeringPrediction(value);

        var smoothing = Math.Clamp(_config.SteeringSmoothing, 0f, 0.95f);
        _smoothedSteering = (_smoothedSteering * smoothing) + (value * (1f - smoothing));
        return Math.Clamp(_smoothedSteering, -1f, 1f);
    }

    private float ApplySteeringPrediction(float value)
    {
        var now = _outputClock.ElapsedTicks;
        var lookAheadMs = Math.Clamp(_config.SteeringPredictionMs, 0f, 60f);

        if (_lastPredictionTicks == 0)
        {
            _lastPredictionTicks = now;
            _lastPredictionSample = value;
            return value;
        }

        var dt = (now - _lastPredictionTicks) / (float)Stopwatch.Frequency;
        var previous = _lastPredictionSample;
        _lastPredictionTicks = now;
        _lastPredictionSample = value;

        if (lookAheadMs <= 0f || dt <= 0f || dt > 0.100f)
        {
            return value;
        }

        // Do not predict through the center or across a direction reversal; those are
        // the cases where lead compensation is most likely to cause visible overshoot.
        if (value == 0f || value * previous < 0f)
        {
            return value;
        }

        var velocity = Math.Clamp((value - previous) / dt, -12f, 12f);
        var predictedDelta = velocity * (lookAheadMs / 1000f);
        var maxBoost = Math.Clamp(_config.SteeringPredictionMaxBoost, 0f, 0.30f);
        predictedDelta = Math.Clamp(predictedDelta, -maxBoost, maxBoost);

        return Math.Clamp(value + predictedDelta, -1f, 1f);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (IsConnected)
            {
                _controller.Disconnect();
            }
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
