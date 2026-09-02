using PCWheelReceiver.Models;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Output;

public enum ControllerOutputMode
{
    WebSafe,
    Xbox360,
}

/// <summary>
/// Owns exactly one active output at a time. In WebSafe mode no ViGEm/Xbox target exists,
/// which prevents desktop controller mappings from turning wheel angle into mouse motion.
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
                var prefix = Mode == ControllerOutputMode.WebSafe ? "WEB SAFE" : "XBOX";
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

        // Dispose outside the lock. Xbox disposal can enter the driver stack and we do not
        // want controller packet processing blocked behind that work.
        previous.Dispose();

        // When leaving Xbox mode, explicitly clear any cached rumble in the relay before
        // the feedback source disappears. Android's stale watchdog remains a backup.
        if (previousMode == ControllerOutputMode.Xbox360 && mode != ControllerOutputMode.Xbox360)
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
        if (mode == ControllerOutputMode.Xbox360)
            GameFeedbackReceived?.Invoke(0, 0);
    }
}
