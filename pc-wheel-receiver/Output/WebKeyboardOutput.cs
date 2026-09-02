using System.Runtime.InteropServices;
using PCWheelReceiver.Models;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Output;

/// <summary>
/// Browser-safe keyboard fallback. It deliberately does not create a virtual gamepad,
/// so desktop controller-to-mouse mappings have nothing to intercept. For broad browser
/// racing compatibility it emits both WASD and arrow-key scan codes for driving input.
/// </summary>
public sealed class WebKeyboardOutput : IControllerOutput
{
    private readonly OutputConfig _config;
    private readonly HashSet<int> _down = new();
    private bool _disposed;
    private string _status = "WEB SAFE keyboard output active (WASD + arrows, virtual gamepad disconnected)";

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

        var steeringThreshold = Math.Max(0.04f, Math.Clamp(_config.SteeringDeadzone, 0f, 0.5f));
        var pedalThreshold = Math.Max(0.02f, Math.Clamp(_config.PedalDeadzone, 0f, 0.5f));

        var left = steering < -steeringThreshold;
        var right = steering > steeringThreshold;
        var throttle = state.Throttle > pedalThreshold;
        var brake = state.Brake > pedalThreshold;

        // Emit both common browser-racing layouts. Using scan codes makes these look like
        // physical key transitions to applications that care about hardware positions.
        SetScanKey(ScanA, extended: false, left);
        SetScanKey(ScanLeft, extended: true, left);
        SetScanKey(ScanD, extended: false, right);
        SetScanKey(ScanRight, extended: true, right);
        SetScanKey(ScanW, extended: false, throttle);
        SetScanKey(ScanUp, extended: true, throttle);
        SetScanKey(ScanS, extended: false, brake);
        SetScanKey(ScanDown, extended: true, brake);

        SetScanKey(ScanSpace, extended: false,
            state.Handbrake >= Math.Clamp(_config.HandbrakeButtonThreshold, 0f, 1f));

        SetScanKey(ScanE, extended: false, IsBitSet(state.Buttons, _config.ShiftUpBit));
        SetScanKey(ScanQ, extended: false, IsBitSet(state.Buttons, _config.ShiftDownBit));
        SetScanKey(ScanH, extended: false, IsBitSet(state.Buttons, _config.HornBit));
        SetScanKey(ScanC, extended: false, IsBitSet(state.Buttons, _config.CameraBit));
        SetScanKey(ScanR, extended: false, IsBitSet(state.Buttons, _config.ResetBit));
    }

    private void SetScanKey(ushort scanCode, bool extended, bool shouldBeDown)
    {
        var id = scanCode | (extended ? 0x10000 : 0);
        var isDown = _down.Contains(id);
        if (isDown == shouldBeDown) return;

        if (SendScanKey(scanCode, extended, keyUp: !shouldBeDown))
        {
            if (shouldBeDown)
                _down.Add(id);
            else
                _down.Remove(id);
        }
        else
        {
            _status = $"WEB SAFE keyboard output failed (Win32 {Marshal.GetLastWin32Error()})";
        }
    }

    private static bool SendScanKey(ushort scanCode, bool extended, bool keyUp)
    {
        var flags = KeyeventfScancode;
        if (extended) flags |= KeyeventfExtendedkey;
        if (keyUp) flags |= KeyeventfKeyup;

        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scanCode,
                    dwFlags = flags,
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
        foreach (var id in _down.ToArray())
        {
            var extended = (id & 0x10000) != 0;
            var scanCode = (ushort)(id & 0xFFFF);
            SendScanKey(scanCode, extended, keyUp: true);
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

    // Set 1 keyboard scan codes used by Windows SendInput.
    private const ushort ScanQ = 0x10;
    private const ushort ScanW = 0x11;
    private const ushort ScanE = 0x12;
    private const ushort ScanR = 0x13;
    private const ushort ScanA = 0x1E;
    private const ushort ScanS = 0x1F;
    private const ushort ScanD = 0x20;
    private const ushort ScanH = 0x23;
    private const ushort ScanC = 0x2E;
    private const ushort ScanSpace = 0x39;
    private const ushort ScanUp = 0x48;
    private const ushort ScanLeft = 0x4B;
    private const ushort ScanRight = 0x4D;
    private const ushort ScanDown = 0x50;

    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;

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
