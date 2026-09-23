# NYC AFTER DARK · v3

Standalone, stylized, single-player New York-inspired 3D browser game. The city map is fictional; this is not an official GTA product.

## Play
Open [index.html](./index.html) in a WebGL 2-capable browser or host this directory on a static website. No dependencies or build step.

## Controls
- WASD / arrows: drive or walk; Space: brake; E: enter/exit.
- C: camera; N: change day/night; P or Esc: pause; R: reset position.
- J: start/cancel taxi passenger job. Slow to under 4 in-game speed units in the pickup/drop-off marker to complete each stage.
- G: when parked near the starting garage, unlock/switch vehicles and repair damage.
- K: save progress; progress is also periodically saved in browser storage.
- Mobile: touch steering/throttle/brake, enter/exit, camera, taxi, garage, and day/night controls.

## Features
Procedural low-poly city inspired by Times Square, Central Park and Brooklyn Bridge; third-person driving and walking, pedestrian/traffic animation, night lighting, landmark challenges, two-stage taxi jobs, 3 vehicle types, collision damage, a simplified wanted/police pursuit mechanic, compass, minimap, and local save.

## Limitations
Prototype, not a geographically accurate NYC replica, GTA V clone, or a full open-world game. No real-map data, multiplayer, weapon/combat systems, or physically simulated traffic. Police and driving AI are simplified. Requires WebGL 2 and suitable graphics performance. Browser saves are local to the origin and device.

## Publishing
This directory is hosted on branch `feat/nyc-after-dark` of `hoonex/game` to keep the existing `main` application untouched. GitHub Pages is not configured by this commit; publishing it as a website requires separate static-hosting setup.