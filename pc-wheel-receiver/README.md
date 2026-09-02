# PC Wheel Receiver

Windows companion app for the Android **PC Wheel** controller.

It receives the Android controller state over low-latency UDP and exposes it to games as a virtual Xbox 360 controller.

## Current data path

```text
Android phone
  Motion / Tilt / Touch steering
  Throttle / Brake / Clutch / Handbrake
        |
        | UDP, target ~100 Hz
        v
PC Wheel Receiver
  binary packet parser
  packet-rate/loss diagnostics
  12-byte ping echo
        |
        v
ViGEm virtual Xbox 360 controller
        |
        v
Game
```

## Default 36-byte Android packet profile

The receiver currently assumes the field set described by the Android implementation summary:

| Offset | Size | Type | Field | Range |
|---:|---:|---|---|---|
| 0 | 4 | uint32 | sequence | monotonically increasing |
| 4 | 8 | int64 | timestamp | Android timestamp |
| 12 | 4 | float32 | steering | -1..1 |
| 16 | 4 | float32 | throttle | 0..1 |
| 20 | 4 | float32 | brake | 0..1 |
| 24 | 4 | float32 | clutch | 0..1 |
| 28 | 4 | float32 | handbrake | 0..1 |
| 32 | 4 | uint32 | buttons | bit mask |

Total: **36 bytes**.

The parser can automatically distinguish little-endian vs big-endian data from valid controller ranges. The default preference is `big` because Kotlin/Java `ByteBuffer` is big-endian unless the Android app explicitly changes byte order.

If the actual Android source uses different offsets or types, edit `protocol.json`. Recompilation is not required.

### Default button bits

- bit 0: Shift Up -> Xbox RB
- bit 1: Shift Down -> Xbox LB
- bit 2: Horn -> Xbox B
- bit 3: Camera -> Xbox Y
- bit 4: Reset -> Xbox X
- Handbrake analog >= 50% -> Xbox A

Xbox has only two native trigger axes, so the default mapping is:

- Steering -> Left Stick X
- Throttle -> Right Trigger
- Brake -> Left Trigger
- Clutch -> Right Stick Y (positive half-axis)

A future wheel/joystick backend can implement dedicated steering, throttle, brake and clutch axes without changing the UDP receiver.

## 12-byte ping packets

Any UDP datagram whose size equals `pingPacketSize` (default: 12 bytes) is echoed byte-for-byte to the sender.

That preserves whatever request ID/timestamp format the Android application already uses and lets the Android app calculate RTT without the PC receiver needing to understand the ping payload.

## Requirements

- Windows 10/11 x64
- .NET 8 SDK only if building from source
- ViGEmBus for virtual Xbox output

`Nefarius.ViGEm.Client` 1.21.256 is used as the managed client. ViGEmBus/ViGEm.NET are retired upstream projects, but the bus remains a practical compatibility backend for Xbox-style game input. The receiver deliberately keeps the virtual-controller layer behind `IControllerOutput` so it can be replaced later.

If ViGEmBus is missing, the receiver still starts in **diagnostic mode**. UDP packets, parsing and telemetry continue to work, but games will not receive controller input until the driver is installed and the receiver is restarted.

Official upstream project:

https://github.com/nefarius/ViGEmBus

## Build

From PowerShell:

```powershell
cd pc-wheel-receiver
dotnet restore
dotnet build -c Release
```

Or publish a self-contained Windows folder:

```powershell
./publish.ps1
```

Output:

```text
pc-wheel-receiver/publish/win-x64/
```

## Connect the Android app

1. Put the PC and phone on the same Wi-Fi/LAN.
2. Start `PCWheelReceiver.exe`.
3. The receiver shows the local IPv4 address at the top.
4. In the Android app, set the PC destination IP to that IPv4 address.
5. Set the UDP destination port to **26760** unless you changed `protocol.json`.
6. Start controller streaming.
7. The receiver should show `PHONE CONNECTED` and packet rate close to 100 Hz.

### Windows Firewall

If packets do not arrive, allow the UDP port in an elevated PowerShell terminal:

```powershell
New-NetFirewallRule -DisplayName "PC Wheel Receiver UDP" -Direction Inbound -Protocol UDP -LocalPort 26760 -Action Allow
```

You can remove that rule later with:

```powershell
Remove-NetFirewallRule -DisplayName "PC Wheel Receiver UDP"
```

## What to verify first

Before testing a game, confirm in the receiver UI:

- Phone endpoint is shown.
- Packet rate is near 100 Hz.
- Steering changes from about -100% to +100%.
- Pedal percentages track the Android UI.
- `Detected endian` settles on `little` or `big` after non-zero controller input.
- Packet loss remains close to 0% on a stable LAN.
- Virtual controller says `Xbox 360 virtual controller connected`.

Then open Windows `joy.cpl` or a game's controller settings and verify the virtual Xbox controller reacts.

## Important Android compatibility check

The 36-byte layout above is the most likely layout because:

```text
sequence       4 bytes
 timestamp      8 bytes
5 x float32    20 bytes
buttons         4 bytes
----------------------
total          36 bytes
```

However, the exact Android `ByteBuffer.put...()` order is the source of truth. If it differs, only `protocol.json` should need to change.
