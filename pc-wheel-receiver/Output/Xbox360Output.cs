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

        var steering = state.Steering >= 0
            ? (short)Math.Round(state.Steering * short.MaxValue)
            : (short)Math.Round(state.Steering * -short.MinValue);

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, steering);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, ToByte(state.Throttle));
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ToByte(state.Brake));

        if (_config.MapClutchToRightStickY)
        {
            _controller.SetAxisValue(Xbox360Axis.RightThumbY,
                (short)Math.Round(Math.Clamp(state.Clutch, 0f, 1f) * short.MaxValue));
        }

        _controller.SetButtonState(Xbox360Button.A,
            state.Handbrake >= _config.HandbrakeButtonThreshold);
        _controller.SetButtonState(Xbox360Button.RightShoulder, IsBitSet(state.Buttons, _config.ShiftUpBit));
        _controller.SetButtonState(Xbox360Button.LeftShoulder, IsBitSet(state.Buttons, _config.ShiftDownBit));
        _controller.SetButtonState(Xbox360Button.B, IsBitSet(state.Buttons, _config.HornBit));
        _controller.SetButtonState(Xbox360Button.Y, IsBitSet(state.Buttons, _config.CameraBit));
        _controller.SetButtonState(Xbox360Button.X, IsBitSet(state.Buttons, _config.ResetBit));
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
