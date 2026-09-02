using PCWheelReceiver.Models;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Output;

public enum ControllerOutputMode
{
    WebSafe,
    WebGamepad,
    Xbox360,
}

/// <summary>
/// Owns exactly one active output at a time. WebSafe has no virtual gamepad at all;
/// WebGamepad exposes a virtual DualShock 4 for browser analog input; Xbox360 keeps
/// the native XInput + rumble path for desktop games.
/// </summary>
public sealed class SwitchableControllerOutput : IControllerOutput, IGameFeedbackSource
{
    private readonly OutputConfig _config;
    private readonly object _sync = new();
    private IControllerOutput _active;
    private bool _disposed;

    public SwitchableControllerOutput(OutputConfig config)
    {
        _config = config;
        Mode = ControllerOutputMode.WebSafe;
        _active = CreateOutput(Mode);
        SubscribeFeedback(_active);
    }

    public ControllerOutputMode Mode { get; private set; }

    public bool IsConnected
    {
        get
        {
            lock (_sync) return !_disposed && _active.IsConnected;
        }
    }

    public string Status
    {
        get
        {
            lock (_sync)
            {
                var prefix = Mode switch
                {
                    ControllerOutputMode.WebSafe => "WEB SAFE",
                    ControllerOutputMode.WebGamepad => "WEB GAMEPAD",
                    _ => "XBOX",
                };
                return $"{prefix}: {_active.Status}";
            }
        }
    }

    public event Action<byte, byte>? GameFeedbackReceived;
    public event EventHandler? ModeChanged;

    public bool TrySetMode(ControllerOutputMode mode, out string? error)
    {
        error = null;
        IControllerOutput? replacement = null;
        IControllerOutput? previous = null;
        ControllerOutputMode previousMode;

        lock (_sync)
        {
            if (_disposed)
            {
                error = "Output is already disposed.";
                return false;
            }

            if (mode == Mode) return true;

            try
            {
                replacement = CreateOutput(mode);
                SubscribeFeedback(replacement);
            }
            catch (Exception ex)
            {
                replacement?.Dispose();
                error = ex.Message;
                return false;
            }

            previous = _active;
            previousMode = Mode;
            UnsubscribeFeedback(previous);
            _active = replacement;
            Mode = mode;
        }

        // Dispose outside the lock. Virtual-device teardown can enter the driver stack and
        // must not block packet processing behind the output lock.
        previous.Dispose();

        // When leaving any rumble-capable virtual gamepad, explicitly clear cached rumble.
        // Android's stale watchdog remains a backup if the stop packet is lost.
        if (previousMode is ControllerOutputMode.Xbox360 or ControllerOutputMode.WebGamepad)
            GameFeedbackReceived?.Invoke(0, 0);

        ModeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Apply(ControllerState state)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _active.Apply(state);
        }
    }

    private IControllerOutput CreateOutput(ControllerOutputMode mode) => mode switch
    {
        ControllerOutputMode.WebSafe => new WebKeyboardOutput(_config),
        ControllerOutputMode.WebGamepad => new DualShock4Output(_config),
        ControllerOutputMode.Xbox360 => new Xbox360Output(_config),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private void SubscribeFeedback(IControllerOutput output)
    {
        if (output is IGameFeedbackSource source)
            source.GameFeedbackReceived += ForwardFeedback;
    }

    private void UnsubscribeFeedback(IControllerOutput output)
    {
        if (output is IGameFeedbackSource source)
            source.GameFeedbackReceived -= ForwardFeedback;
    }

    private void ForwardFeedback(byte largeMotor, byte smallMotor) =>
        GameFeedbackReceived?.Invoke(largeMotor, smallMotor);

    public void Dispose()
    {
        IControllerOutput active;
        ControllerOutputMode mode;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            active = _active;
            mode = Mode;
            UnsubscribeFeedback(active);
        }

        active.Dispose();
        if (mode is ControllerOutputMode.Xbox360 or ControllerOutputMode.WebGamepad)
            GameFeedbackReceived?.Invoke(0, 0);
    }
}
