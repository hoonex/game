# Casual Drive

A small Windows 3D free-roam driving game built to test PC Wheel Receiver and its virtual Xbox 360 output.

## Controller mapping

- Left Stick X: steering
- RT: throttle
- LT: brake / reverse
- A: handbrake
- B: horn
- X: reset car
- Y: camera

This matches the default PCWheelReceiver mapping.

## Keyboard fallback

- W / Up: throttle
- S / Down: brake / reverse
- A/D or Left/Right: steering
- Space: handbrake
- R: reset
- C: camera
- H: horn

## Local development

```bash
npm install
npm run dev
```

## Windows portable build

```bash
npm install
npm run dist:win
```

The GitHub Actions workflow builds a portable Windows x64 executable automatically.
