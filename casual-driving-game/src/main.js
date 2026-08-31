import * as THREE from 'three';
import './style.css';

const canvas = document.querySelector('#game');
const speedEl = document.querySelector('#speed');
const gearEl = document.querySelector('#gear');
const padDot = document.querySelector('#padDot');
const padText = document.querySelector('#padText');
const cameraText = document.querySelector('#cameraText');
const startScreen = document.querySelector('#startScreen');
const startButton = document.querySelector('#startButton');
const minimap = document.querySelector('#minimap');
const mapCtx = minimap.getContext('2d');

const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, 1.6));
renderer.setSize(innerWidth, innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.05;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x88a9c4);
scene.fog = new THREE.Fog(0x88a9c4, 150, 600);

const camera = new THREE.PerspectiveCamera(62, innerWidth / innerHeight, 0.1, 1200);

const hemi = new THREE.HemisphereLight(0xcbe5ff, 0x34412f, 2.2);
scene.add(hemi);

const sun = new THREE.DirectionalLight(0xfff2d7, 3.0);
sun.position.set(120, 180, 80);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
sun.shadow.camera.left = -180;
sun.shadow.camera.right = 180;
sun.shadow.camera.top = 180;
sun.shadow.camera.bottom = -180;
sun.shadow.camera.far = 500;
scene.add(sun);

const WORLD_HALF = 440;
const ROAD_STEP = 80;
const ROAD_WIDTH = 24;
const BUILDING_MARGIN = 9;
const collisionBoxes = [];
const traffic = [];

function seeded(seed) {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}
const rand = seeded(20260831);

function mat(color, roughness = 0.8, metalness = 0.0) {
  return new THREE.MeshStandardMaterial({ color, roughness, metalness });
}

const ground = new THREE.Mesh(
  new THREE.PlaneGeometry(WORLD_HALF * 2 + 140, WORLD_HALF * 2 + 140),
  mat(0x58724a, 1)
);
ground.rotation.x = -Math.PI / 2;
ground.receiveShadow = true;
scene.add(ground);

const roadMat = mat(0x252b31, 0.96);
const sidewalkMat = mat(0x8b8e8d, 1);
const laneMat = new THREE.MeshStandardMaterial({ color: 0xd9d4b7, roughness: 0.9 });

function addRoadStrip(x, z, w, d) {
  const sidewalk = new THREE.Mesh(new THREE.BoxGeometry(w + 4, 0.22, d + 4), sidewalkMat);
  sidewalk.position.set(x, 0.11, z);
  sidewalk.receiveShadow = true;
  scene.add(sidewalk);

  const road = new THREE.Mesh(new THREE.BoxGeometry(w, 0.24, d), roadMat);
  road.position.set(x, 0.24, z);
  road.receiveShadow = true;
  scene.add(road);
}

for (let p = -400; p <= 400; p += ROAD_STEP) {
  addRoadStrip(p, 0, ROAD_WIDTH, WORLD_HALF * 2);
  addRoadStrip(0, p, WORLD_HALF * 2, ROAD_WIDTH);
}

const dashGeoX = new THREE.BoxGeometry(8, 0.035, 0.22);
const dashGeoZ = new THREE.BoxGeometry(0.22, 0.035, 8);
for (let p = -400; p <= 400; p += ROAD_STEP) {
  for (let q = -420; q <= 420; q += 18) {
    if (Math.abs(((q + ROAD_STEP / 2) % ROAD_STEP) - ROAD_STEP / 2) < ROAD_WIDTH / 2 + 8) continue;
    const dx = new THREE.Mesh(dashGeoX, laneMat);
    dx.position.set(q, 0.39, p);
    scene.add(dx);
    const dz = new THREE.Mesh(dashGeoZ, laneMat);
    dz.position.set(p, 0.39, q);
    scene.add(dz);
  }
}

const buildingColors = [0x8c969f, 0xb0a18f, 0x8793a6, 0xa98576, 0x6f7d85, 0xa1a59a, 0x7f7380];
const roofMat = mat(0x4b5056, 0.85);

function addBuilding(x, z, w, d, h, color) {
  const group = new THREE.Group();
  const body = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat(color, 0.82));
  body.position.y = h / 2 + 0.28;
  body.castShadow = true;
  body.receiveShadow = true;
  group.add(body);

  const roof = new THREE.Mesh(new THREE.BoxGeometry(w * 0.72, 0.7, d * 0.72), roofMat);
  roof.position.y = h + 0.63;
  roof.castShadow = true;
  group.add(roof);

  const glass = new THREE.MeshStandardMaterial({ color: 0x223746, emissive: 0x182b39, emissiveIntensity: 0.15, roughness: 0.35, metalness: 0.15 });
  const rows = Math.max(1, Math.min(5, Math.floor(h / 8)));
  for (let i = 0; i < rows; i++) {
    const win = new THREE.Mesh(new THREE.BoxGeometry(w * 0.68, 1.1, 0.08), glass);
    win.position.set(0, 4 + i * Math.max(4.8, h / (rows + 1)), d / 2 + 0.045);
    group.add(win);
  }

  group.position.set(x, 0, z);
  scene.add(group);
  collisionBoxes.push({ minX: x - w / 2, maxX: x + w / 2, minZ: z - d / 2, maxZ: z + d / 2 });
}

for (let ix = -5; ix < 5; ix++) {
  for (let iz = -5; iz < 5; iz++) {
    const cx = ix * ROAD_STEP + ROAD_STEP / 2;
    const cz = iz * ROAD_STEP + ROAD_STEP / 2;
    const blockSize = ROAD_STEP - ROAD_WIDTH - BUILDING_MARGIN * 2;
    const split = rand() > 0.45;
    if (split) {
      const w = blockSize * 0.42;
      const d = blockSize * (0.62 + rand() * 0.2);
      const h1 = 12 + rand() * 42;
      const h2 = 10 + rand() * 36;
      addBuilding(cx - blockSize * 0.25, cz, w, d, h1, buildingColors[Math.floor(rand() * buildingColors.length)]);
      addBuilding(cx + blockSize * 0.25, cz, w, d, h2, buildingColors[Math.floor(rand() * buildingColors.length)]);
    } else {
      const w = blockSize * (0.7 + rand() * 0.16);
      const d = blockSize * (0.7 + rand() * 0.16);
      addBuilding(cx, cz, w, d, 14 + rand() * 48, buildingColors[Math.floor(rand() * buildingColors.length)]);
    }
  }
}

const trunkMat = mat(0x5e4937, 1);
const leavesMat = mat(0x3d6f43, 0.95);
function addTree(x, z, scale = 1) {
  const trunk = new THREE.Mesh(new THREE.CylinderGeometry(0.35 * scale, 0.5 * scale, 3.2 * scale, 7), trunkMat);
  trunk.position.set(x, 1.6 * scale, z);
  trunk.castShadow = true;
  scene.add(trunk);
  const crown = new THREE.Mesh(new THREE.IcosahedronGeometry(1.9 * scale, 1), leavesMat);
  crown.position.set(x, 4.1 * scale, z);
  crown.castShadow = true;
  scene.add(crown);
}

for (let p = -360; p <= 360; p += ROAD_STEP) {
  for (let q = -350; q <= 350; q += 70) {
    addTree(p + ROAD_WIDTH / 2 + 5, q + 13, 0.85 + rand() * 0.3);
  }
}

const poleMat = mat(0x33393e, 0.65, 0.35);
const lampMat = new THREE.MeshStandardMaterial({ color: 0xffe8b0, emissive: 0xffce78, emissiveIntensity: 1.3 });
function addLamp(x, z) {
  const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.12, 5.5, 8), poleMat);
  pole.position.set(x, 2.75, z);
  scene.add(pole);
  const head = new THREE.Mesh(new THREE.BoxGeometry(0.55, 0.2, 0.35), lampMat);
  head.position.set(x, 5.45, z);
  scene.add(head);
}
for (let p = -320; p <= 320; p += ROAD_STEP) {
  for (let q = -300; q <= 300; q += 80) addLamp(p - ROAD_WIDTH / 2 - 2.5, q + 28);
}

function createCar(color = 0x4fa7ff, npc = false) {
  const car = new THREE.Group();
  const bodyMat = mat(color, 0.34, 0.35);
  const dark = mat(0x12161b, 0.5, 0.5);
  const glass = new THREE.MeshStandardMaterial({ color: 0x152b39, roughness: 0.24, metalness: 0.2 });

  const lower = new THREE.Mesh(new THREE.BoxGeometry(2.15, 0.62, 4.3), bodyMat);
  lower.position.y = 0.72;
  lower.castShadow = true;
  car.add(lower);

  const hood = new THREE.Mesh(new THREE.BoxGeometry(1.95, 0.3, 1.2), bodyMat);
  hood.position.set(0, 1.04, 1.25);
  hood.castShadow = true;
  car.add(hood);

  const cabin = new THREE.Mesh(new THREE.BoxGeometry(1.72, 0.84, 1.85), glass);
  cabin.position.set(0, 1.42, -0.2);
  cabin.castShadow = true;
  car.add(cabin);

  const bumper = new THREE.Mesh(new THREE.BoxGeometry(2.02, 0.16, 0.18), dark);
  bumper.position.set(0, 0.55, 2.2);
  car.add(bumper);

  const wheels = [];
  for (const x of [-1.08, 1.08]) {
    for (const z of [-1.45, 1.42]) {
      const pivot = new THREE.Group();
      pivot.position.set(x, 0.52, z);
      const wheel = new THREE.Mesh(new THREE.CylinderGeometry(0.42, 0.42, 0.28, 16), dark);
      wheel.rotation.z = Math.PI / 2;
      wheel.castShadow = true;
      pivot.add(wheel);
      car.add(pivot);
      wheels.push({ pivot, wheel, front: z > 0 });
    }
  }
  car.userData.wheels = wheels;
  car.userData.npc = npc;
  scene.add(car);
  return car;
}

const player = createCar(0x4d9eff);
player.position.set(0, 0.28, 18);

const trafficColors = [0xe55b52, 0xf1c84a, 0xe8e8e8, 0x3d4854, 0x55b67a, 0xd28adb];
for (let i = 0; i < 12; i++) {
  const axis = i % 2 === 0 ? 'x' : 'z';
  const laneIndex = Math.floor(rand() * 7) - 3;
  const laneCenter = laneIndex * ROAD_STEP + (rand() > 0.5 ? 4.5 : -4.5);
  const npc = createCar(trafficColors[i % trafficColors.length], true);
  const dir = rand() > 0.5 ? 1 : -1;
  if (axis === 'x') {
    npc.position.set((rand() * 2 - 1) * WORLD_HALF, 0.28, laneCenter);
    npc.rotation.y = dir > 0 ? Math.PI / 2 : -Math.PI / 2;
  } else {
    npc.position.set(laneCenter, 0.28, (rand() * 2 - 1) * WORLD_HALF);
    npc.rotation.y = dir > 0 ? 0 : Math.PI;
  }
  traffic.push({ mesh: npc, axis, dir, speed: 8 + rand() * 7 });
}

const state = {
  speed: 0,
  heading: 0,
  steerVisual: 0,
  cameraMode: 0,
  started: false,
  lastTime: performance.now()
};

const keys = new Set();
let previousButtons = [];
let audioCtx = null;
let engineOsc = null;
let engineGain = null;

function startAudio() {
  if (audioCtx) return;
  audioCtx = new AudioContext();
  engineOsc = audioCtx.createOscillator();
  engineGain = audioCtx.createGain();
  engineOsc.type = 'sawtooth';
  engineOsc.frequency.value = 60;
  engineGain.gain.value = 0.012;
  engineOsc.connect(engineGain).connect(audioCtx.destination);
  engineOsc.start();
}

function horn() {
  if (!audioCtx) return;
  const now = audioCtx.currentTime;
  for (const [freq, level] of [[330, 0.07], [440, 0.045]]) {
    const o = audioCtx.createOscillator();
    const g = audioCtx.createGain();
    o.type = 'square';
    o.frequency.value = freq;
    g.gain.setValueAtTime(level, now);
    g.gain.exponentialRampToValueAtTime(0.0001, now + 0.22);
    o.connect(g).connect(audioCtx.destination);
    o.start(now);
    o.stop(now + 0.23);
  }
}

addEventListener('keydown', (e) => {
  keys.add(e.code);
  if (['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Space'].includes(e.code)) e.preventDefault();
});
addEventListener('keyup', (e) => keys.delete(e.code));

startButton.addEventListener('click', () => {
  state.started = true;
  startScreen.style.display = 'none';
  startAudio();
});

addEventListener('gamepadconnected', () => updatePadStatus());
addEventListener('gamepaddisconnected', () => updatePadStatus());

function deadzone(v, dz = 0.08) {
  if (Math.abs(v) <= dz) return 0;
  return Math.sign(v) * (Math.abs(v) - dz) / (1 - dz);
}

function activePad() {
  const pads = navigator.getGamepads ? navigator.getGamepads() : [];
  for (const p of pads) if (p && (p.mapping === 'standard' || /xbox|xinput|controller/i.test(p.id))) return p;
  for (const p of pads) if (p) return p;
  return null;
}

function buttonValue(pad, index) {
  const b = pad?.buttons?.[index];
  return b ? (typeof b.value === 'number' ? b.value : (b.pressed ? 1 : 0)) : 0;
}

function buttonPressedEdge(pad, index) {
  const down = !!pad?.buttons?.[index]?.pressed;
  const was = !!previousButtons[index];
  return down && !was;
}

function updatePadStatus() {
  const pad = activePad();
  if (pad) {
    padDot.classList.add('connected');
    padText.textContent = 'XBOX / GAMEPAD';
  } else {
    padDot.classList.remove('connected');
    padText.textContent = 'KEYBOARD';
  }
}

function getInput() {
  const pad = activePad();
  let steer = 0;
  let throttle = 0;
  let brake = 0;
  let handbrake = false;
  let reset = false;
  let cam = false;
  let doHorn = false;

  if (pad) {
    steer = deadzone(pad.axes?.[0] ?? 0);
    throttle = buttonValue(pad, 7);
    brake = buttonValue(pad, 6);
    handbrake = !!pad.buttons?.[0]?.pressed;
    reset = buttonPressedEdge(pad, 2);
    cam = buttonPressedEdge(pad, 3);
    doHorn = buttonPressedEdge(pad, 1);
    previousButtons = pad.buttons.map((b) => !!b.pressed);
  } else {
    previousButtons = [];
  }

  const left = keys.has('KeyA') || keys.has('ArrowLeft');
  const right = keys.has('KeyD') || keys.has('ArrowRight');
  const up = keys.has('KeyW') || keys.has('ArrowUp');
  const down = keys.has('KeyS') || keys.has('ArrowDown');

  if (left || right) steer = (right ? 1 : 0) - (left ? 1 : 0);
  if (up) throttle = 1;
  if (down) brake = 1;
  if (keys.has('Space')) handbrake = true;

  return { steer, throttle, brake, handbrake, reset, cam, doHorn, hasPad: !!pad };
}

let prevR = false, prevC = false, prevH = false;
function keyboardEdges(input) {
  const r = keys.has('KeyR');
  const c = keys.has('KeyC');
  const h = keys.has('KeyH');
  input.reset ||= r && !prevR;
  input.cam ||= c && !prevC;
  input.doHorn ||= h && !prevH;
  prevR = r; prevC = c; prevH = h;
}

function resetCar() {
  player.position.set(0, 0.28, 18);
  state.speed = 0;
  state.heading = 0;
  player.rotation.set(0, 0, 0);
}

function collides(x, z) {
  const r = 1.35;
  for (const b of collisionBoxes) {
    if (x + r > b.minX && x - r < b.maxX && z + r > b.minZ && z - r < b.maxZ) return true;
  }
  return false;
}

function distToRoadLine(v) {
  const nearest = Math.round(v / ROAD_STEP) * ROAD_STEP;
  return Math.abs(v - nearest);
}

function isOnRoad(x, z) {
  return distToRoadLine(x) < ROAD_WIDTH / 2 || distToRoadLine(z) < ROAD_WIDTH / 2;
}

function updatePhysics(dt, input) {
  if (input.reset) resetCar();
  if (input.cam) {
    state.cameraMode = (state.cameraMode + 1) % 3;
    cameraText.textContent = ['CHASE CAM', 'HOOD CAM', 'TOP CAM'][state.cameraMode];
  }
  if (input.doHorn) horn();

  const onRoad = isOnRoad(player.position.x, player.position.z);
  const maxForward = onRoad ? 36 : 19;
  const maxReverse = -12;
  const accel = onRoad ? 15.5 : 8.5;
  const reverseAccel = 9;

  if (input.throttle > 0.01) {
    if (state.speed < -0.5) state.speed += 22 * input.throttle * dt;
    else state.speed += accel * input.throttle * dt;
  }

  if (input.brake > 0.01) {
    if (state.speed > 0.8) state.speed -= 25 * input.brake * dt;
    else state.speed -= reverseAccel * input.brake * dt;
  }

  const naturalDrag = onRoad ? 0.52 : 1.75;
  state.speed *= Math.exp(-naturalDrag * dt);
  if (input.handbrake) state.speed *= Math.exp(-2.8 * dt);
  state.speed = THREE.MathUtils.clamp(state.speed, maxReverse, maxForward);
  if (Math.abs(state.speed) < 0.025 && input.throttle < 0.02 && input.brake < 0.02) state.speed = 0;

  const targetSteer = THREE.MathUtils.clamp(input.steer, -1, 1);
  state.steerVisual = THREE.MathUtils.damp(state.steerVisual, targetSteer, 12, dt);
  const speedFactor = THREE.MathUtils.clamp(Math.abs(state.speed) / 7, 0, 1);
  const steeringAuthority = input.handbrake ? 0.074 : 0.052;
  state.heading += state.steerVisual * state.speed * steeringAuthority * (0.25 + 0.75 * speedFactor) * dt;

  const nx = player.position.x + Math.sin(state.heading) * state.speed * dt;
  const nz = player.position.z + Math.cos(state.heading) * state.speed * dt;
  if (!collides(nx, nz)) {
    player.position.x = THREE.MathUtils.clamp(nx, -WORLD_HALF, WORLD_HALF);
    player.position.z = THREE.MathUtils.clamp(nz, -WORLD_HALF, WORLD_HALF);
  } else {
    state.speed *= -0.18;
  }

  player.rotation.y = state.heading;
  for (const w of player.userData.wheels) {
    w.wheel.rotation.x -= state.speed * dt / 0.42;
    if (w.front) w.pivot.rotation.y = state.steerVisual * 0.42;
  }

  const kph = Math.abs(state.speed) * 3.6;
  speedEl.textContent = String(Math.round(kph));
  gearEl.textContent = state.speed > 0.35 ? 'D' : state.speed < -0.35 ? 'R' : 'N';
  updatePadStatus();

  if (engineOsc && engineGain) {
    const rpm = 54 + Math.abs(state.speed) * 4.2 + input.throttle * 22;
    engineOsc.frequency.setTargetAtTime(rpm, audioCtx.currentTime, 0.04);
    engineGain.gain.setTargetAtTime(0.008 + Math.min(kph / 220, 1) * 0.012 + input.throttle * 0.01, audioCtx.currentTime, 0.05);
  }
}

function updateTraffic(dt) {
  for (const t of traffic) {
    if (t.axis === 'x') {
      t.mesh.position.x += t.dir * t.speed * dt;
      if (t.mesh.position.x > WORLD_HALF + 20) t.mesh.position.x = -WORLD_HALF - 20;
      if (t.mesh.position.x < -WORLD_HALF - 20) t.mesh.position.x = WORLD_HALF + 20;
      t.mesh.rotation.y = t.dir > 0 ? Math.PI / 2 : -Math.PI / 2;
    } else {
      t.mesh.position.z += t.dir * t.speed * dt;
      if (t.mesh.position.z > WORLD_HALF + 20) t.mesh.position.z = -WORLD_HALF - 20;
      if (t.mesh.position.z < -WORLD_HALF - 20) t.mesh.position.z = WORLD_HALF + 20;
      t.mesh.rotation.y = t.dir > 0 ? 0 : Math.PI;
    }
    for (const w of t.mesh.userData.wheels) w.wheel.rotation.x -= t.speed * t.dir * dt / 0.42;
  }
}

const cameraPos = new THREE.Vector3();
const lookPos = new THREE.Vector3();
function updateCamera(dt) {
  const forward = new THREE.Vector3(Math.sin(state.heading), 0, Math.cos(state.heading));
  const right = new THREE.Vector3(forward.z, 0, -forward.x);
  if (state.cameraMode === 0) {
    cameraPos.copy(player.position).addScaledVector(forward, -8.8).add(new THREE.Vector3(0, 4.7, 0)).addScaledVector(right, 0.1);
    lookPos.copy(player.position).addScaledVector(forward, 5.5).add(new THREE.Vector3(0, 1.2, 0));
  } else if (state.cameraMode === 1) {
    cameraPos.copy(player.position).addScaledVector(forward, 1.35).add(new THREE.Vector3(0, 1.62, 0));
    lookPos.copy(player.position).addScaledVector(forward, 20).add(new THREE.Vector3(0, 1.2, 0));
  } else {
    cameraPos.copy(player.position).addScaledVector(forward, -4).add(new THREE.Vector3(0, 21, 0));
    lookPos.copy(player.position).addScaledVector(forward, 7);
  }
  camera.position.lerp(cameraPos, 1 - Math.exp(-7 * dt));
  const targetQuat = new THREE.Quaternion();
  const temp = new THREE.Object3D();
  temp.position.copy(camera.position);
  temp.lookAt(lookPos);
  targetQuat.copy(temp.quaternion);
  camera.quaternion.slerp(targetQuat, 1 - Math.exp(-9 * dt));
}

function drawMinimap() {
  const c = mapCtx;
  const w = minimap.width;
  c.clearRect(0, 0, w, w);
  c.fillStyle = '#11171d';
  c.fillRect(0, 0, w, w);
  const scale = w / (WORLD_HALF * 2);
  c.strokeStyle = 'rgba(210,220,228,.22)';
  c.lineWidth = Math.max(2, ROAD_WIDTH * scale);
  for (let p = -400; p <= 400; p += ROAD_STEP) {
    const s = (p + WORLD_HALF) * scale;
    c.beginPath(); c.moveTo(s, 0); c.lineTo(s, w); c.stroke();
    c.beginPath(); c.moveTo(0, w - s); c.lineTo(w, w - s); c.stroke();
  }
  const px = (player.position.x + WORLD_HALF) * scale;
  const py = w - (player.position.z + WORLD_HALF) * scale;
  c.save();
  c.translate(px, py);
  c.rotate(state.heading);
  c.fillStyle = '#7fb9ff';
  c.beginPath();
  c.moveTo(0, -7);
  c.lineTo(5, 6);
  c.lineTo(0, 4);
  c.lineTo(-5, 6);
  c.closePath();
  c.fill();
  c.restore();
}

function animate(now) {
  requestAnimationFrame(animate);
  const dt = Math.min((now - state.lastTime) / 1000, 0.033);
  state.lastTime = now;

  const input = getInput();
  keyboardEdges(input);
  if (state.started) updatePhysics(dt, input);
  updateTraffic(dt);
  updateCamera(dt);
  drawMinimap();
  renderer.render(scene, camera);
}

camera.position.set(0, 6, 8);
updateCamera(1);
updatePadStatus();
requestAnimationFrame(animate);

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.6));
});
