using System.Runtime.InteropServices;
using PCWheelReceiver.Models;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Output;

/// <summary>
/// Browser-safe fallback output. It deliberately does not create any virtual gamepad,
/// so Steam Desktop Layout / controller-to-mouse mappers have nothing to intercept.
/// Steering is mapped to A/D, throttle/brake to W/S, and handbrake to Space.
/// </summary>
public sealed class WebKeyboardOutput : IControllerOutput
{
    private const ushort VkA = 0x41;
    private const ushort VkD = 0x44;
    private const ushort VkW = 0x57;
    private const ushort VkS = 0x53;
    private const ushort VkSpace = 0x20;
    private const ushort VkQ = 0x51;
    private const ushort VkE = 0x45;
    private const ushort VkH = 0x48;
    private const ushort VkC = 0x43;
    private const ushort VkR = 0x52;

    private readonly OutputConfig _config;
    private readonly HashSet<ushort> _down = new();
    private bool _disposed;
    private string _status = "WEB SAFE keyboard output active (virtual Xbox disconnected)";

    public WebKeyboardOutput(OutputConfig config)
    {
        _config = config;
    }

    public bool IsConnected => !_disposed;
    public string Status => _status;

    public void Apply(ControllerState state)
    {
        if (_disposed) return;

        var steering = Math.Clamp(state.Steering, -1f, 1f);
        if (_config.InvertSteering) steering = -steering;
        steering *= Math.Clamp(_config.SteeringSensitivity, 0.1f, 3f);

        // A small minimum threshold avoids key chatter around wheel center while still
        // respecting a larger user-configured deadzone.
        var steeringThreshold = Math.Max(0.04f, Math.Clamp(_config.SteeringDeadzone, 0f, 0.5f));
        var pedalThreshold = Math.Max(0.02f, Math.Clamp(_config.PedalDeadzone, 0f, 0.5f));

        SetKey(VkA, steering < -steeringThreshold);
        SetKey(VkD, steering > steeringThreshold);
        SetKey(VkW, state.Throttle > pedalThreshold);
        SetKey(VkS, state.Brake > pedalThreshold);
        SetKey(VkSpace, state.Handbrake >= Math.Clamp(_config.HandbrakeButtonThreshold, 0f, 1f));

        // Keep the phone's existing button semantics useful in browser games.
        SetKey(VkE, IsBitSet(state.Buttons, _config.ShiftUpBit));
        SetKey(VkQ, IsBitSet(state.Buttons, _config.ShiftDownBit));
        SetKey(VkH, IsBitSet(state.Buttons, _config.HornBit));
        SetKey(VkC, IsBitSet(state.Buttons, _config.CameraBit));
        SetKey(VkR, IsBitSet(state.Buttons, _config.ResetBit));
    }

    private void SetKey(ushort virtualKey, bool shouldBeDown)
    {
        var isDown = _down.Contains(virtualKey);
        if (isDown == shouldBeDown) return;

        if (SendKey(virtualKey, keyUp: !shouldBeDown))
        {
            if (shouldBeDown)
                _down.Add(virtualKey);
            else
                _down.Remove(virtualKey);
        }
        else
        {
            _status = $"WEB SAFE keyboard output failed (Win32 {Marshal.GetLastWin32Error()})";
        }
    }

    private static bool SendKey(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KeyeventfKeyup : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero,
                },
            },
        };

        return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
    }

    private static bool IsBitSet(uint mask, int bit) =>
        bit is >= 0 and < 32 && (mask & (1u << bit)) != 0;

    private void ReleaseAllKeys()
    {
        foreach (var key in _down.ToArray())
        {
            SendKey(key, keyUp: true);
        }
        _down.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        ReleaseAllKeys();
        _disposed = true;
        _status = "Disconnected";
    }

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
