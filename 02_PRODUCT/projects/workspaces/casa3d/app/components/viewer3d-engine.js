// components/viewer3d.js — scena 3D portată din prototip (tur la persoana întâi + machetă).
// Modelele de mobilier, materialele și construcția pereților sunt copiate din prototip.
import * as THREE from 'three';
import { floorTexture, wallTexture, onTexturesReady } from './finish-textures.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { RGBELoader } from 'three/examples/jsm/loaders/RGBELoader.js';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { SSAOPass } from 'three/examples/jsm/postprocessing/SSAOPass.js';
import { ShaderPass } from 'three/examples/jsm/postprocessing/ShaderPass.js';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { SLAB } from '../core/levels';

// Pasul final al calității înalte: în r128 tone mapping-ul (ACES) se aplică și la randarea în texturi
// (WebGLPrograms: toneMapping nu depinde de țintă), dar ieșirea în textură rămâne liniară; aici facem doar sRGB.
const FinishShader = { uniforms: { tDiffuse: { value: null } }, vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `uniform sampler2D tDiffuse; varying vec2 vUv;
    vec3 srgb(vec3 c){ return mix(pow(c, vec3(0.41666)) * 1.055 - vec3(0.055), c * 12.92, vec3(lessThanEqual(c, vec3(0.0031308)))); }
    void main(){ vec4 t = texture2D(tDiffuse, vUv); gl_FragColor = vec4(srgb(clamp(t.rgb, 0.0, 1.0)), t.a); }` };

// hărți de normale fotografice (CC0, public/tex): țesătura textilelor și tencuiala pereților
const texLoader = typeof document !== 'undefined' ? new THREE.TextureLoader() : null;
const normalTex = (src, rep) => { if (!texLoader) return null; const t = texLoader.load(src); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(rep, rep); t.anisotropy = 8; return t; };
const linenN = normalTex('/tex/rough_linen_nor_gl_web.jpg', 5), boucleN = normalTex('/tex/wool_boucle_nor_gl_web.jpg', 4), plasterN = normalTex('/tex/white_plaster_rough_02_nor_gl_web.jpg', 2);
export function createViewer(canvas, { onPick, onContextLost, onContextRestored } = {}){
  let raf = 0, last = performance.now(), alive = true, contextLost = false;
  const mql = typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null;
  let reduce = !!(mql && mql.matches);
  const R = new THREE.WebGLRenderer({ canvas, antialias: true });
  R.setPixelRatio(Math.min(devicePixelRatio, 2)); R.outputEncoding = THREE.sRGBEncoding; R.toneMapping = THREE.ACESFilmicToneMapping; R.toneMappingExposure = .78;
  // umbrele soarelui se recalculează doar când se schimbă ceva (planul, lumina, modul, o ușă), nu la fiecare cadru
  R.shadowMap.enabled = true; R.shadowMap.type = THREE.PCFSoftShadowMap; R.shadowMap.autoUpdate = false; R.shadowMap.needsUpdate = true;
  let mirrorsDirty = true; const dirty = () => { R.shadowMap.needsUpdate = true; mirrorsDirty = true; };
  const scene = new THREE.Scene(); scene.background = new THREE.Color('#d9d3c8'); scene.fog = new THREE.Fog('#d9d3c8', 25, 60);
  // regula comună a cadrelor (imaginile de prezentare și intrarea în tur): ochiul la 1,5 m, FOV vertical 52°,
  // aparatul ținut drept (verticalele rămân verticale), privirea coborâtă cu o deplasare a obiectivului (setViewOffset)
  const SHOT = { eye: 1.5, fov: 52, shift: .07, near: 1.0, nearObj: 1.5, maxCover: .15 };   // near: pereți; nearObj: piese, uși, perdele, ferestre
  const walkFov = () => (cam.aspect || 1.6) >= 1 ? 55 : 72;
  const cam = new THREE.PerspectiveCamera(55, 1, .05, 200);
  // calitate înaltă: ocluzie ambientală (umbre de contact în colțuri și sub mobilă). Distanțele SSAO sunt în adâncime
  // normalizată la (far - near) = ~200 m: 0.0015 ≈ 0,3 m, 0.0001 ≈ 2 cm (sub atât, suprafețele plane s-ar umbri singure); raza nucleului e în metri.
  let quality = 'normal', composer = null, ssao = null, finish = null;
  function setupComposer(){ const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1; composer = new EffectComposer(R); composer.setPixelRatio(Math.min(devicePixelRatio, 1.5)); composer.setSize(w, h);
    ssao = new SSAOPass(scene, cam, w, h); ssao.kernelRadius = .45; ssao.minDistance = .0001; ssao.maxDistance = .002; composer.addPass(ssao);
    finish = new ShaderPass(FinishShader); composer.addPass(finish); }
  // SSAOPass (r128) copiază proiecția camerei doar la creare și la setSize; o sincronizăm la fiecare cadru,
  // altfel umbrele de contact folosesc un aspect/fov vechi (după redimensionare sau în imaginile de export)
  const syncSsao = () => { const u = ssao.ssaoMaterial.uniforms; u.cameraProjectionMatrix.value.copy(cam.projectionMatrix); u.cameraInverseProjectionMatrix.value.copy(cam.projectionMatrixInverse);
    u.cameraNear.value = cam.near; u.cameraFar.value = cam.far; ssao.depthRenderMaterial.uniforms.cameraNear.value = cam.near; ssao.depthRenderMaterial.uniforms.cameraFar.value = cam.far; };
  const draw = () => { if (contextLost) return; if (quality === 'high' && composer){ syncSsao(); composer.render(); } else R.render(scene, cam); };
  const pm = new THREE.PMREMGenerator(R); scene.environment = pm.fromScene(new RoomEnvironment(), .04).texture;
  // lumina de mediu dintr-un interior real (HDRI CC0, public/tex): reflexii și umbre moi credibile; până se încarcă rămâne camera sintetică
  try { new RGBELoader().setDataType(THREE.UnsignedByteType).load('/tex/lebombo_1k.hdr', t => { if (!alive) { t.dispose(); return; } const env = pm.fromEquirectangular(t).texture; t.dispose(); const old = scene.environment; scene.environment = env; old?.dispose?.(); }, undefined, () => {}); } catch { /* fără HDRI: rămâne mediul sintetic */ }
  const hemi = new THREE.HemisphereLight(0xfff6ea, 0x8f8272, .3); scene.add(hemi);
  let light = null; const interiorLights = []; // parametrii de iluminare primiți din core/lighting.ts
  // luminile camerelor: una pe încăpere în plan (roomLights), dar cel mult MAX_PL PointLight-uri active în scenă, mutate la
  // camerele cele mai apropiate de jucător (în tur) sau la cele mai mari (machetă)
  const roomLights = [], MAX_PL = 4; let lightRoom = null;
  function assignLights(force, px = player.x, pz = player.z){ if (!interiorLights.length) return; const walk = mode === 'walk';
    const here = walk && plan ? (plan.camere.find(r => px > r.x0 && px < r.x1 && pz > r.z0 && pz < r.z1) || {}).id || null : '__house';
    if (!force && here === lightRoom) return; lightRoom = here;
    const order = [...roomLights].sort((a, b) => walk ? Math.hypot(a.x - px, a.z - pz) - Math.hypot(b.x - px, b.z - pz) : b.area - a.area);
    interiorLights.forEach((l, i) => { const r = order[i]; l.position.set(r.x, r.y, r.z); l.updateMatrixWorld(); }); }
  const sun = new THREE.DirectionalLight(0xfff0dc, 1.5); sun.castShadow = true; sun.shadow.mapSize.set(2048, 2048); Object.assign(sun.shadow.camera, { left: -12, right: 12, top: 12, bottom: -12, near: 1, far: 50 }); sun.shadow.bias = -.0004; sun.shadow.normalBias = .03; sun.shadow.radius = 3; scene.add(sun, sun.target);
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(240, 240), new THREE.MeshStandardMaterial({ color: new THREE.Color('#b9afa0').convertSRGBToLinear(), roughness: 1 })); ground.rotation.x = -Math.PI / 2; ground.position.y = -.02; ground.receiveShadow = true; scene.add(ground);
  const lin = c => new THREE.Color(c).convertSRGBToLinear();
  // exteriorul văzut prin ferestre în tur și în imaginile fixe: cer în degradeu (orizont deschis → zenit albastru) și teren,
  // estompat spre orizont; în machetă rămâne fundalul neutru
  const SKY = { day: ['#eef0ee', '#cfdde6', '#8fb3d6', '#7d8467'], evening: ['#f3d2b0', '#d9b9a6', '#7c86a8', '#5f5a48'], night: ['#273042', '#18202e', '#0b101a', '#1a1c1a'] };
  const skyGeo = new THREE.SphereGeometry(110, 32, 16), skyCol = new Float32Array(skyGeo.attributes.position.count * 3); skyGeo.setAttribute('color', new THREE.BufferAttribute(skyCol, 3));
  const sky = new THREE.Mesh(skyGeo, new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.BackSide, fog: false, depthWrite: false, toneMapped: false })); sky.visible = false; sky.renderOrder = -1; scene.add(sky);
  const groundHouse = ground.material.color.clone();
  let skyKey = 'day';
  function paintSky(key){ skyKey = SKY[key] ? key : 'day'; const [hz, lo, zen, gr] = SKY[skyKey].map(lin), p = skyGeo.attributes.position, c = new THREE.Color();
    for (let i = 0; i < p.count; i++){ const t = p.getY(i) / 110; if (t < 0) c.copy(hz).lerp(gr, Math.min(1, -t * 4)); else if (t < .25) c.copy(hz).lerp(lo, t / .25); else c.copy(lo).lerp(zen, Math.min(1, (t - .25) / .6)); skyCol.set([c.r, c.g, c.b], i * 3); }
    skyGeo.attributes.color.needsUpdate = true; }
  paintSky('day');
  const M = (c, o) => new THREE.MeshStandardMaterial(Object.assign({ color: lin(c), roughness: .75, envMapIntensity: .7 }, o || {}));
  const tex = (w, h, draw) => { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d'), w, h); const t = new THREE.CanvasTexture(cv); t.encoding = THREE.sRGBEncoding; t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 8; return t; };
  const parquet = tex(512, 512, (g, w, h) => { const cols = ['#b88a5a', '#a97c4f', '#c29665', '#9f7248', '#b5855a'];
    for (let y = 0; y < h; y += 32) for (let x = -((y / 32) % 3) * 64; x < w; x += 192){ g.fillStyle = cols[(x / 64 + y / 32 * 7 & 7) % 5]; g.fillRect(x, y, 190, 30);
      for (let k = 0; k < 6; k++){ g.strokeStyle = 'rgba(70,40,20,.10)'; g.beginPath(); g.moveTo(x, y + 4 + k * 4.5); g.bezierCurveTo(x + 60, y + 2 + k * 4.5, x + 120, y + 8 + k * 4.5, x + 190, y + 4 + k * 4.5); g.stroke(); } } });
  const tiles = tex(512, 512, (g, w, h) => { g.fillStyle = '#b9bab5'; g.fillRect(0, 0, w, h); for (let y = 0; y < h; y += 128) for (let x = 0; x < w; x += 128){ const v = 222 + (Math.random() * 12 | 0); g.fillStyle = `rgb(${v},${v - 1},${v - 4})`; g.fillRect(x + 2, y + 2, 124, 124); } });
  const plaster = plasterN ? { normalMap: plasterN, normalScale: new THREE.Vector2(.18, .18) } : {};
  const wallMat = M('#ddd3c3', { roughness: .92, ...plaster }), extMat = M('#cdc3b3', { roughness: .95 }), glass = new THREE.MeshPhysicalMaterial({ color: lin('#d4e6ee'), transparent: true, opacity: .12, roughness: .02, metalness: 0, envMapIntensity: 1.2, depthWrite: false });
  // rame și pervaze: decalaj de adâncime față de perete (fără „puncte” de z-fighting la îmbinări)
  const offs = { polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1 };
  const frameMat = M('#f5f2ea', { roughness: .5, ...offs });
  // tavanul: primește umbra (nu mai trece soarele prin el) și o mică lumină proprie în culoarea finisajului ales —
  // altfel îl luminează doar culoarea de jos a emisferei (bej) și iese gri-maroniu
  const ceilMats = [], ceilFill = { day: .32, evening: .2, night: .1 };
  const mkCeil = hex => { const m = M(hex, { roughness: 1, emissive: lin(hex), emissiveIntensity: ceilFill.day, envMapIntensity: .5 }); m.shadowSide = THREE.DoubleSide; m.userData = { ceil: true }; ceilMats.push(m); return m; };
  const ceilMat = mkCeil('#f3efe7');
  const skirtMat = M('#efe9de', { roughness: .45 });
  // materiale pe culoare, cu cache: fețe de perete, tavane, rame (culorile vin din core/appearance.ts prin plan)
  const lookCache = {}; const lookMat = (kind, hex, rough) => lookCache[kind + hex] ||= (kind === 'ceil' ? mkCeil(hex) : M(hex, { roughness: rough, ...(kind === 'wall' ? plaster : {}), ...(kind === 'frame' ? offs : {}) }));
  const faceMat = (hex, ext) => hex ? lookMat('wall', hex, ext ? .95 : .92) : (ext ? extMat : wallMat);

/* ---------- materiale realiste (texturi generate, fără imagini externe) ---------- */
const gray = (w, h, draw) => { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d'), w, h); const t = new THREE.CanvasTexture(cv); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 8; return t; };
const weave = gray(256, 256, (g, w, h) => { const id = g.createImageData(w, h); for (let y = 0; y < h; y++) for (let x = 0; x < w; x++){ const i = (y * w + x) * 4, v = 205 + ((x % 4 < 2) ^ (y % 4 < 2) ? 18 : 0) + Math.random() * 30; id.data[i] = id.data[i + 1] = id.data[i + 2] = Math.min(255, v); id.data[i + 3] = 255; } g.putImageData(id, 0, 0); });
weave.repeat.set(6, 6);
const grain = gray(512, 512, (g, w, h) => { g.fillStyle = '#d8d8d8'; g.fillRect(0, 0, w, h); for (let i = 0; i < 260; i++){ const y = Math.random() * h, a = .05 + Math.random() * .12; g.strokeStyle = `rgba(0,0,0,${a})`; g.lineWidth = .6 + Math.random() * 1.8; g.beginPath(); g.moveTo(0, y); for (let x = 0; x <= w; x += 32) g.lineTo(x, y + Math.sin(x / 70 + i) * 3); g.stroke(); } });
// fibra pe verticală, ca la foile de ușă furniruite (furnirul curge pe înălțimea ușii, nu de-a latul ei)
const grainV = grain.clone(); grainV.center.set(.5, .5); grainV.rotation = Math.PI / 2; grainV.needsUpdate = true;
const rattan = gray(128, 128, (g, w, h) => { g.fillStyle = '#b8b8b8'; g.fillRect(0, 0, w, h); g.strokeStyle = '#6d6d6d'; g.lineWidth = 2; for (let i = -w; i < w * 2; i += 10){ g.beginPath(); g.moveTo(i, 0); g.lineTo(i + h, h); g.stroke(); g.beginPath(); g.moveTo(i + h, 0); g.lineTo(i, h); g.stroke(); } });
rattan.repeat.set(3, 3);
// obiecte decorative (CC0, public/models): încărcate o dată, normalizate (bază la y=0, centrate, la înălțimea sau lățimea dorită)
const DECO = { plant: ['potted_plant_02', 1.05, 'h'], succulent: ['potted_plant_04', .2, 'h'], vase: ['ceramic_vase_01', .28, 'h'], jug: ['ceramic_vase_04', .22, 'h'], art: ['hanging_picture_frame_02', .95, 'w'], photo: ['standing_picture_frame_01', .2, 'h'] };
const decoReady = {}, decoWaiters = new Set();
function decoModel(key){ if (decoReady[key] !== undefined) return decoReady[key]; decoReady[key] = null; const [name, size, axis] = DECO[key];
  try { new GLTFLoader().load(`/models/${name}/${name}.gltf`, gl => { const o = gl.scene; let b = new THREE.Box3().setFromObject(o), sz = b.getSize(new THREE.Vector3());
      if (axis === 'w' && sz.x < sz.z) { o.rotation.y = Math.PI / 2; b = new THREE.Box3().setFromObject(o); sz = b.getSize(new THREE.Vector3()); }   // tabloul cu fața spre +z
      const k = size / (axis === 'w' ? sz.x : sz.y), wrap = new THREE.Group(); o.scale.multiplyScalar(k); b = new THREE.Box3().setFromObject(o); const c = b.getCenter(new THREE.Vector3());
      o.position.set(-c.x, -b.min.y, -c.z); o.traverse(m => { if (m.isMesh){ m.castShadow = true; m.receiveShadow = true; } }); wrap.add(o); decoReady[key] = wrap; decoWaiters.forEach(f => f()); },
    undefined, () => { decoReady[key] = false; }); } catch { decoReady[key] = false; }
  return null; }
const matCache = {};
const lumOf = hex => { const n = parseInt(String(hex).slice(1), 16); return ((n >> 16 & 255) * .2126 + (n >> 8 & 255) * .7152 + (n & 255) * .0722) / 255; };
function MAT(kind, col){ const k = kind + col; if (matCache[k]) return matCache[k]; const c = lin(col); let m;
  switch (kind){
    case 'fabric': m = new THREE.MeshStandardMaterial({ color: c, map: weave, roughness: .95, ...(linenN ? { normalMap: linenN, normalScale: new THREE.Vector2(.9, .9) } : { bumpMap: weave, bumpScale: .003 }) }); break;
    case 'velvet': m = new THREE.MeshPhysicalMaterial({ color: c, map: weave, roughness: .85, sheen: lin('#9fc3c2'), ...(boucleN ? { normalMap: boucleN, normalScale: new THREE.Vector2(.6, .6) } : {}) }); break;
    case 'leather': m = new THREE.MeshPhysicalMaterial({ color: c, roughness: .42, clearcoat: .35, clearcoatRoughness: .5, bumpMap: weave, bumpScale: .001 }); break;
    case 'wood': m = new THREE.MeshStandardMaterial({ color: c, map: grain, roughness: .58 }); break;
    case 'woodV': m = new THREE.MeshStandardMaterial({ color: c, map: grainV, roughness: .5 }); break;
    case 'paint': m = new THREE.MeshStandardMaterial({ color: c, roughness: .48 }); break;
    case 'metal': m = new THREE.MeshStandardMaterial({ color: c, metalness: .9, roughness: .32 }); break;
    case 'chrome': m = new THREE.MeshStandardMaterial({ color: c, metalness: 1, roughness: .12 }); break;
    case 'glass': m = new THREE.MeshPhysicalMaterial({ color: c, transparent: true, opacity: .14, roughness: .03, metalness: 0, envMapIntensity: 1.2, depthWrite: false }); break;
    case 'frost': m = new THREE.MeshPhysicalMaterial({ color: c, transparent: true, opacity: .55, roughness: .6, depthWrite: false }); break;
    case 'mirror': m = new THREE.MeshStandardMaterial({ color: c.clone().lerp(new THREE.Color(1, 1, 1), .55), metalness: 1, roughness: .03, envMapIntensity: 1.6 }); m.userData.mirror = true; break;   // reflexia camerei: sonda cub per cameră (updateMirrors)
    case 'cloth': m = new THREE.MeshStandardMaterial({ color: c, map: weave, roughness: .95, side: THREE.DoubleSide, ...(linenN ? { normalMap: linenN, normalScale: new THREE.Vector2(.9, .9) } : {}) }); break;   // pilote, pleduri, draperii (o singură suprafață)
    case 'rug': m = new THREE.MeshStandardMaterial({ color: c.clone().multiplyScalar(.7), map: weave, roughness: 1, ...(boucleN ? { normalMap: boucleN, normalScale: new THREE.Vector2(1.4, 1.4) } : { bumpMap: weave, bumpScale: .004 }) }); break;
    case 'emit': m = new THREE.MeshStandardMaterial({ color: c, emissive: c, emissiveIntensity: 1.4, roughness: .6 }); break;   // banda LED a oglinzilor
    case 'ceramic': m = new THREE.MeshPhysicalMaterial({ color: c, roughness: .12, clearcoat: .8 }); break;
    case 'rattan': m = new THREE.MeshStandardMaterial({ color: c, map: rattan, roughness: .8 }); break;
    case 'screen': m = new THREE.MeshStandardMaterial({ color: c, roughness: .2, metalness: .2 }); break;
    default: m = new THREE.MeshStandardMaterial({ color: c, roughness: .8 }); }
  m.userData._k = kind; m.userData._c = col;
  return matCache[k] = m; }
// fibra lemnului pe lungimea piesei: ușile de corp și dulapurile primesc fibra verticală
const vgrain = m => m && m.userData && m.userData._k === 'wood' ? MAT('woodV', m.userData._c) : m;
// geometrii comune (cache după dimensiuni, la mm): cutiile mobilierului au muchii ușor rotunjite (rază 5–6 mm, ca o
// muchie de PAL cu cant), plăcile foarte subțiri rămân cutii simple. Nu se eliberează la reconstruire (userData.shared).
const geoCache = new Map();
const k3 = (w, h, d) => `${Math.round(w * 1000)}|${Math.round(h * 1000)}|${Math.round(d * 1000)}`;
function boxGeo(w, h, d, bevel = true){ const key = (bevel ? 'r' : 'b') + k3(w, h, d); let g = geoCache.get(key); if (g) return g;
  const mn = Math.min(w, h, d), r = Math.min(.006, mn * .22);
  g = bevel && r >= .0025 ? new RoundedBoxGeometry(w, h, d, 1, r) : new THREE.BoxGeometry(w, h, d);
  g.userData = { shared: true, box: [w, h, d] }; geoCache.set(key, g); return g; }
function sharedGeo(key, make){ let g = geoCache.get(key); if (!g){ g = make(); g.userData = { ...(g.userData || {}), shared: true }; geoCache.set(key, g); } return g; }
// draperii și perdele: un singur panou cu pliuri (profil sinusoidal), nu fâșii separate
function pleatGeo(w, h, amp, period){ return sharedGeo(`pl${k3(w, h, amp)}|${period}`, () => { const n = Math.max(8, Math.ceil(w / period * 10)), g = new THREE.PlaneGeometry(w, h, n, 1), p = g.attributes.position;
  for (let i = 0; i < p.count; i++){ const x = p.getX(i) + w / 2; p.setZ(i, amp * Math.sin(x / period * Math.PI * 2)); } g.computeVertexNormals(); g.userData = { free: true, smooth: true }; return g; }); }
// textil așezat peste o formă (pilota peste saltea, pledul): suprafața de sus ușor bombată, marginile rotunjite care cad
// pe laterale (dropX) și la picioare (dropZ). Coordonate locale: x lățime, z adâncime (+z = picioare), y = 0 sus.
function drapeGeo(w, d, dropX, dropZ, puff, r = .05){ return sharedGeo(`dr${k3(w, d, dropX)}|${Math.round(dropZ * 1000)}|${Math.round(puff * 1000)}`, () => {
  const fold = (s, half, drop) => { const a = Math.abs(s), sg = Math.sign(s) || 1, arc = Math.PI * r / 2; if (a <= half) return [s, 0];
    const t = a - half; if (t < arc) return [sg * (half + r * Math.sin(t / r)), -r * (1 - Math.cos(t / r))]; const u = Math.min(t - arc, Math.max(0, drop - r)); return [sg * (half + r + u * .06), -r - u]; };
  const arc = Math.PI * r / 2, ex = dropX > 0 ? arc + Math.max(0, dropX - r) : 0, ez = dropZ > 0 ? arc + Math.max(0, dropZ - r) : 0, nx = 36, nz = 30;
  const g = new THREE.PlaneGeometry(w + 2 * ex, d + ez, nx, nz); g.rotateX(-Math.PI / 2); g.translate(0, 0, ez / 2); const p = g.attributes.position;
  for (let i = 0; i < p.count; i++){ const sx = p.getX(i), sz = p.getZ(i), [x, yx] = fold(sx, w / 2, dropX), [zz, yz] = sz > d / 2 ? fold(sz - d / 2, 0, dropZ) : [0, 0];
    const z = sz > d / 2 ? d / 2 + zz : sz, ix = Math.min(1, Math.max(0, 1 - Math.abs(sx) / (w / 2))), iz = Math.min(1, Math.max(0, 1 - Math.abs(sz) / (d / 2)));
    const bulge = puff * Math.pow(Math.sin(Math.PI / 2 * Math.min(1, ix * 3)), .8) * Math.pow(Math.sin(Math.PI / 2 * Math.min(1, iz * 3)), .8) + puff * .25 * Math.sin(sx * 9.1 + sz * 3.7) * ix * iz;
    p.setXYZ(i, x, yx + yz + bulge, z); }
  g.computeVertexNormals(); g.userData = { free: true, smooth: true }; return g; }); }
// cutie cu muchii rotunjite (perne, tapițerie)
function rgeo(w, h, d, r){ r = Math.max(.002, Math.min(r, w / 2 - .002, h / 2 - .002, d / 2 - .002)); const s = new THREE.Shape(), x = -(w - 2 * r) / 2, y = -(h - 2 * r) / 2, W2 = w - 2 * r, H2 = h - 2 * r, q = Math.min(W2, H2) * .08;
  s.moveTo(x + q, y); s.lineTo(x + W2 - q, y); s.quadraticCurveTo(x + W2, y, x + W2, y + q); s.lineTo(x + W2, y + H2 - q); s.quadraticCurveTo(x + W2, y + H2, x + W2 - q, y + H2); s.lineTo(x + q, y + H2); s.quadraticCurveTo(x, y + H2, x, y + H2 - q); s.lineTo(x, y + q); s.quadraticCurveTo(x, y, x + q, y);
  const g = new THREE.ExtrudeGeometry(s, { depth: Math.max(.001, d - 2 * r), bevelEnabled: true, bevelSize: r, bevelThickness: r, bevelSegments: 4, curveSegments: 6 }); g.translate(0, 0, -(d - 2 * r) / 2); g.userData.rbox = [w, h, d, r]; return g; }

// contururi în plan (x, z) pentru plăci decupate: blatul cu gaura chiuvetei, puntea ceramică a lavoarului
function rrectPath(p, cx, cz, w, d, r){ r = Math.max(.001, Math.min(r, w / 2 - .001, d / 2 - .001)); const x0 = cx - w / 2, x1 = cx + w / 2, y0 = -cz - d / 2, y1 = -cz + d / 2;
  p.moveTo(x0 + r, y0); p.lineTo(x1 - r, y0); p.quadraticCurveTo(x1, y0, x1, y0 + r); p.lineTo(x1, y1 - r); p.quadraticCurveTo(x1, y1, x1 - r, y1); p.lineTo(x0 + r, y1); p.quadraticCurveTo(x0, y1, x0, y1 - r); p.lineTo(x0, y0 + r); p.quadraticCurveTo(x0, y0, x0 + r, y0); return p; }
function ellipsePath(p, cx, cz, rx, rz){ p.absellipse(cx, -cz, rx, rz, 0, Math.PI * 2, true); return p; }
/** Placă orizontală de grosime `t` cu fața de sus la y = 0, din conturul `shape` (cu găuri). */
function slabGeo(shape, t){ const g = new THREE.ExtrudeGeometry(shape, { depth: t, bevelEnabled: false, curveSegments: 28 }); g.rotateX(-Math.PI / 2); g.translate(0, -t, 0); return g; }

/* ---------- modele 3D pe variante, la dimensiunile oficiale ---------- */
function model(it){
  const s = it.s || {}, g = new THREE.Group(), w = it.w / 100, d = it.d / 100, h = it.h / 100;
  const B = (bw, bh, bd, x, y, z, m) => { const o = new THREE.Mesh(boxGeo(bw, bh, bd), m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const RB = (bw, bh, bd, r, x, y, z, m) => { const o = new THREE.Mesh(rgeo(bw, bh, bd, r), m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const CY = (rt, rb, hh, x, y, z, m, seg) => { const o = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, hh, seg || 24), m); o.position.set(x, y, z); o.castShadow = true; g.add(o); return o; };
  const body = (c) => s.wood ? MAT('wood', c) : MAT('paint', c);
  const SLAB = (shape, t, y, m) => { const o = new THREE.Mesh(slabGeo(shape, t), m); o.position.y = y; o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const name = String(it.nume || '');
  const handleM = MAT('metal', s.col && lumOf(s.col) < .35 ? '#a9a9a9' : '#2b2b2d');
  // un front de corp: sertar (cu mâner orizontal), ușă (mâner vertical lângă margine), vitrină (sticlă în ramă)
  const front = (kind, x, y, fw, fh, z, m, side = 1) => { if (kind === 'door') m = vgrain(m); B(fw - .006, fh - .006, .018, x, y, z + .009, kind === 'glass' ? MAT('paint', s.col2 || s.col || '#f2f2ef') : m);
    if (kind === 'glass'){ B(fw - .07, fh - .07, .006, x, y, z + .02, MAT('frost', '#dfe6e8')); return; }
    if (kind === 'drawer') B(Math.min(.16, fw * .4), .012, .02, x, y + fh / 2 - Math.min(.06, fh * .3), z + .028, handleM);
    else B(.012, Math.min(.16, fh * .3), .02, x + side * (fw / 2 - .04), y + (fh > .9 ? 0 : fh / 2 - .1), z + .028, handleM); };
  switch (it.model){
    case 'sofa': if (w < 1.1 && /PO[ÄA]NG|OSKARSHAMN|DYVLINGE|rotativ|EKEN[ÄA]SET|urechi/i.test(name)){
      // fotolii cu formă proprie: POÄNG (cadru curbat din lemn, pernă continuă), OSKARSHAMN (cu urechi), DYVLINGE (cochilie rotativă), EKENÄSET (brațe și picioare din lemn)
      const up = MAT(s.mat || 'fabric', s.col), wood = MAT('wood', /negru|black/i.test(name) ? '#2b2522' : '#c9a77a');
      if (/PO[ÄA]NG/i.test(name)){ [-1, 1].forEach(a => { const runner = new THREE.Mesh(new THREE.TorusGeometry(d * .55, .018, 8, 32, Math.PI * .75), wood); runner.rotation.y = Math.PI / 2; runner.rotation.z = Math.PI * .62; runner.position.set(a * (w / 2 - .03), .55, .05); runner.castShadow = true; g.add(runner); B(.035, .035, d * .9, a * (w / 2 - .03), .03, 0, wood); });
        const seat = RB(w - .1, .1, d * .55, .05, 0, .42, .1, up); seat.rotation.x = .18; const back = RB(w - .1, .1, h * .62, .05, 0, .72, -d / 2 + .16, up); back.rotation.x = -1.15; }
      else if (/DYVLINGE|rotativ/i.test(name)){ CY(.24, .26, .02, 0, .01, 0, MAT('metal', '#2b2b2b'), 32); CY(.03, .03, .22, 0, .12, 0, MAT('metal', '#2b2b2b'), 12);
        const shell = new THREE.Mesh(new THREE.CylinderGeometry(w / 2, w / 2 - .04, h - .22, 40, 1, true, Math.PI * .25, Math.PI * 1.5), up); shell.material.side = THREE.DoubleSide; shell.position.set(0, .22 + (h - .22) / 2, 0); shell.rotation.y = Math.PI; shell.castShadow = true; g.add(shell);
        CY(w / 2 - .03, w / 2 - .03, .14, 0, .3, 0, up, 40); }
      else if (/OSKARSHAMN|urechi/i.test(name)){ [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, c]) => B(.04, .14, .04, a * (w / 2 - .07), .07, c * (d / 2 - .07), wood));
        RB(w - .08, .2, d - .1, .04, 0, .26, .02, up); RB(w - .2, .12, d - .25, .05, 0, .42, .08, up); RB(w - .08, h - .3, .16, .06, 0, .3 + (h - .3) / 2, -d / 2 + .1, up);
        [-1, 1].forEach(a => { RB(.12, .26, d - .2, .05, a * (w / 2 - .06), .5, .02, up); const wing = RB(.1, .42, .3, .05, a * (w / 2 - .07), h - .26, -d / 2 + .22, up); wing.rotation.y = a * .25; }); }
      else { [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, c]) => B(.04, .6, .04, a * (w / 2 - .03), .3, c * (d / 2 - .05), wood)); [-1, 1].forEach(a => B(.05, .03, d - .02, a * (w / 2 - .03), .6, 0, wood));
        RB(w - .12, .12, d - .1, .04, 0, .42, .02, up); const bk = RB(w - .12, h - .45, .1, .05, 0, .42 + (h - .45) / 2, -d / 2 + .1, up); bk.rotation.x = -.12; }
      break; }
    { const up = MAT(s.mat || 'fabric', s.col), dark = MAT('paint', '#1b1b1b'), ad = Math.min(.95, d), arm = w < 1.1 ? .16 : .24; const sw = s.chaise ? w - .02 : w;
      [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => CY(.02, .018, .06, a * (sw / 2 - .08), .03, b * (ad / 2 - .08), dark, 10));
      RB(sw, .22, ad, .03, 0, .17, 0, up);                          // bază
      RB(.24, .36, ad, .06, -sw / 2 + arm / 2, .46, 0, up);         // cotiere joase și late (specific KIVIK)
      if (!s.chaise) RB(.24, .36, ad, .06, sw / 2 - arm / 2, .46, 0, up);
      const seatW = (sw - arm * (s.chaise ? 1 : 2)), n = s.seats || (seatW < .9 ? 1 : seatW < 1.5 ? 2 : 3); const x0 = -sw / 2 + arm;
      for (let i = 0; i < n; i++) RB(seatW / n - .01, .17, ad - .22, .07, x0 + seatW / n * (i + .5), .365, .08, up); // perne șezut
      for (let i = 0; i < n; i++) RB(seatW / n - .02, .44, .2, .09, x0 + seatW / n * (i + .5), .62, -ad / 2 + .15, up); // perne spătar
      RB(sw - .02, .38, .16, .03, 0, .45, -ad / 2 + .08, up);          // spătar
      if (s.chaise){ const cd = d, cx = sw / 2 - .45; RB(.9, .22, cd, .03, cx, .17, (cd - ad) / 2, up); RB(.86, .17, cd - .24, .07, cx, .365, (cd - ad) / 2 + .1, up); }
      // perne decorative la capete, în culori care se potrivesc tapițeriei (deschisă → teracota și salvie, închisă → in)
      { const light = lumOf(s.col || '#cccccc') > .5, cols = light ? ['#a8714a', '#7d8a72'] : ['#d9cfc0', '#b9a58a'];
        [[-1, 0], [1, 1]].forEach(([a, i]) => { if (s.chaise && a > 0) return; const c = RB(.44, .42, .13, .07, a * (sw / 2 - arm - .26), .6, -ad / 2 + .32, MAT('velvet', cols[i])); c.rotation.x = -.28; c.rotation.z = a * -.12; }); }
      g.children.forEach(o => { o.position.z -= (d - ad) / 2 * (s.chaise ? 1 : 0); }); if (s.chaise) g.children.forEach(o => { o.position.z += 0; });
      if (s.chaise) g.position.z = 0; break; }
    case 'coffee': { const c = s.col;
      if (s.type === 'lack'){ const m = MAT('paint', c); B(w, .05, d, 0, h - .025, 0, m); B(w - .1, .02, d - .1, 0, .16, 0, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.05, h - .05, .05, a * (w / 2 - .035), (h - .05) / 2, b * (d / 2 - .035), m)); }
      else if (s.type === 'vittsjo'){ const m = MAT('metal', c); CY(w / 2, w / 2, .008, 0, h - .004, 0, MAT('glass', '#dfeaee'), 48); CY(w / 2 - .02, w / 2 - .02, .01, 0, .12, 0, MAT('paint', '#1f1c1a'), 48); for (let i = 0; i < 4; i++){ const a = i * Math.PI / 2 + Math.PI / 4; CY(.008, .008, h - .01, Math.cos(a) * (w / 2 - .03), (h - .01) / 2, Math.sin(a) * (w / 2 - .03), m, 8); } { const r = new THREE.Mesh(new THREE.TorusGeometry(w / 2 - .03, .007, 6, 48), m); r.rotation.x = Math.PI / 2; r.position.y = h - .02; g.add(r); } }
      else if (s.type === 'hemnes'){ const m = MAT('wood', c); B(w, .04, d, 0, h - .02, 0, m); B(w - .06, .07, d - .06, 0, h - .075, 0, m); B(w - .1, .02, d - .1, 0, .13, 0, MAT('rattan', '#8b6a4a')); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.06, h - .04, .06, a * (w / 2 - .04), (h - .04) / 2, b * (d / 2 - .04), m)); }
      else if (s.round){ const top = s.marble ? MAT('ceramic', '#efeeea') : /sticl|glass/i.test(name) ? MAT('glass', '#dfeaee') : body(c || '#efefec'), leg = MAT(/crom|aram|cupru/i.test(name) ? 'chrome' : 'metal', s.col2 || (/aram|cupru/i.test(name) ? '#b87333' : '#1e1e20'));
        // măsuță rotundă: blat (marmură / sticlă / lemn) pe picior central sau pe cadru metalic, cu raft jos dacă e set
        CY(w / 2, w / 2, .03, 0, h - .015, 0, top, 48); CY(w / 2 - .01, w / 2 - .01, .012, 0, .12, 0, top, 48);
        for (let i = 0; i < 3; i++){ const a = i * Math.PI * 2 / 3; CY(.012, .012, h - .03, Math.cos(a) * (w / 2 - .05), (h - .03) / 2, Math.sin(a) * (w / 2 - .05), leg, 10); }
        const ring = new THREE.Mesh(new THREE.TorusGeometry(w / 2 - .03, .008, 8, 48), leg); ring.rotation.x = Math.PI / 2; ring.position.y = h - .035; g.add(ring); }
      else { const m = MAT('wood', c); RB(w, .035, d, .008, 0, h - .018, 0, m); for (let i = 0; i < 9; i++) B(w - .16, .015, .045, 0, .12, -d / 2 + .08 + i * (d - .16) / 8, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.05, h - .035, .05, a * (w / 2 - .06), (h - .035) / 2, b * (d / 2 - .06), m)); }
      const deco = MAT('paint', '#2f5d50'); CY(.06, .05, .12, w * .18, h + .06, 0, deco, 16); B(.22, .03, .16, -w * .2, h + .015, 0, MAT('paint', '#c8b89a')); break; }
    case 'tv': { const m = body(s.col); if (s.open){ B(w, .02, d, 0, h - .01, 0, m); B(w, .02, d, 0, .01, 0, m); for (let i = 0; i <= 3; i++) B(.02, h, d, -w / 2 + .01 + i * (w - .02) / 3, h / 2, 0, m); B(w, h, .01, 0, h / 2, -d / 2 + .005, m); }
      else if (s.doors || s.drawers || s.glass){ const carc = s.col2 ? body(s.col2) : m, legs = h > .4 ? .08 : .03, fh = h - legs;
        B(w, fh, d - .02, 0, legs + fh / 2, -.01, carc); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.03, legs, .03, a * (w / 2 - .05), legs / 2, b * (d / 2 - .06), MAT('paint', '#2a2a2a')));
        // coloanele: vitrinele la capete, ușile, iar sertarele într-o coloană la mijloc
        const cols = []; for (let i = 0; i < (s.glass || 0); i++) cols.push('glass'); for (let i = 0; i < (s.doors || 0); i++) cols.push('door'); if (s.drawers) cols.splice(Math.floor(cols.length / 2), 0, 'drawers');
        const cw = w / cols.length; cols.forEach((k, i) => { const x = -w / 2 + cw * (i + .5);
          if (k === 'drawers'){ const nd = s.drawers, dh = (fh - .02) / nd; for (let j = 0; j < nd; j++) front('drawer', x, legs + .01 + dh * (j + .5), cw, dh, d / 2 - .02, m); }
          else front(k, x, legs + fh / 2, cw, fh - .02, d / 2 - .02, m, i < cols.length / 2 ? 1 : -1); });
        if (s.lights) B(w - .06, .012, .02, 0, h - .025, d / 2 + .004, MAT('emit', '#fff1d6')); }
      else { B(w, h - .01, d, 0, h / 2 + .005, 0, m); const n = s.fronts || 3; for (let i = 0; i < n; i++){ B(w / n - .006, h - .03, .016, -w / 2 + w / n * (i + .5), h / 2 + .005, d / 2 + .008, body(s.col)); } }
      { const tw = Math.min(1.45, Math.max(.8, w * .72)), th = tw * .5625; B(tw, th, .03, 0, h + .09 + th / 2, -.06, MAT('screen', '#0b0c0e')); } B(.32, .02, .2, 0, h + .01, -.06, MAT('metal', '#222')); B(.04, .09, .04, 0, h + .065, -.06, MAT('metal', '#222')); break; }
    case 'shelf': { const m = body(s.col);
      if (s.type === 'kallax'){ const t = .035; B(w, t, d, 0, t / 2, 0, m); B(w, t, d, 0, h - t / 2, 0, m); B(t, h, d, -w / 2 + t / 2, h / 2, 0, m); B(t, h, d, w / 2 - t / 2, h / 2, 0, m); B(.015, h - t * 2, d, 0, h / 2, 0, m);
        for (let i = 1; i < 4; i++) B(w - t * 2, .015, d, 0, t + i * (h - 2 * t) / 4, 0, m);
        for (let r = 0; r < 4; r++) for (let c = 0; c < 2; c++){ const cx = (c ? 1 : -1) * (w / 4 - .005), cy = t + (r + .5) * (h - 2 * t) / 4; if (s.doors && (r + c) % 2 === 0) B(w / 2 - .05, (h - 2 * t) / 4 - .03, .016, cx, cy, d / 2, m); else if ((r * 2 + c) % 3 !== 1) B(.28, .26, .3, cx, cy - .03, 0, MAT('fabric', ['#c9bda8', '#6c7b6f', '#b88f6a'][(r + c) % 3])); } }
      else { B(.018, h, d, -w / 2 + .009, h / 2, 0, m); B(.018, h, d, w / 2 - .009, h / 2, 0, m); B(w, h, .005, 0, h / 2, -d / 2 + .003, m); B(w, .06, d, 0, .03, 0, m);
        for (let i = 0; i < 6; i++){ const y = .06 + i * (h - .08) / 5; B(w - .036, .016, d - .01, 0, y, 0, m); if (i < 5){ let x = -w / 2 + .03; while (x < w / 2 - .06){ const bw = .02 + Math.random() * .03, bh = .19 + Math.random() * .1; B(bw, bh, d - .07, x + bw / 2, y + .008 + bh / 2, .01, MAT('paint', ['#7d3a2e', '#2d4a66', '#c9b27c', '#3c5f4a', '#d8d4cc', '#1f2327'][Math.random() * 6 | 0])); x += bw + .003; } } } }
      break; }
    case 'dining': { const top = s.top, tm = top === '#efefec' ? MAT('paint', top) : MAT('wood', top);
      const lm = s.col2 ? (lumOf(s.col2) < .3 ? MAT('metal', s.col2) : MAT('wood', s.col2)) : tm; B(w, .03, d, 0, h - .015, 0, tm); B(w - .08, .06, d - .08, 0, h - .06, 0, lm); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.05, h - .03, .05, a * (w / 2 - .05), (h - .03) / 2, b * (d / 2 - .05), lm));
      // scaunele: câte încap pe o latură lungă (cel puțin 47 cm fiecare), restul la capete; trase puțin sub blat, fără să se atingă
      const total = it.chairs ?? s.chairs ?? 4, cap = Math.max(1, Math.floor((w - .04) / .47)), perSide = Math.min(cap, Math.ceil(total / 2)), ends = Math.max(0, total - perSide * 2);
      const pos = []; for (const sd of [-1, 1]) for (let i = 0; i < Math.min(perSide, total - (sd > 0 ? perSide : 0)); i++) pos.push([-w / 2 + w / perSide * (i + .5), sd, 0]);
      for (let i = 0; i < Math.min(ends, 2); i++) pos.push([0, i ? 1 : -1, 1]);
      pos.forEach(([x, sd, end]) => { const cg = new THREE.Group(); if (end){ cg.position.set(sd * (w / 2 + .12), 0, 0); cg.rotation.y = sd > 0 ? -Math.PI / 2 : Math.PI / 2; }
        else { cg.position.set(x, 0, sd * (d / 2 + .12)); cg.rotation.y = sd > 0 ? Math.PI : 0; } g.add(cg);
        const add = (bw, bh, bd, px, py, pz, m) => { const o = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, bd), m); o.position.set(px, py, pz); o.castShadow = true; cg.add(o); };
        const fr = s.chair === 'ingolf' || s.chair === 'orrstaWhite' ? MAT('paint', '#f1f1ee') : s.chair === 'rattan' ? MAT('paint', '#1c1c1c') : MAT('wood', '#3f2d22');
        [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => add(.03, .45, .03, a * .19, .225, b * .19, fr));
        if (s.chair === 'rattan'){ add(.42, .03, .42, 0, .46, 0, MAT('rattan', '#b08858')); add(.42, .4, .02, 0, .7, -.2, MAT('rattan', '#b08858')); add(.03, .45, .03, -.19, .7, -.2, fr); add(.03, .45, .03, .19, .7, -.2, fr); }
        else if (s.chair === 'ingolf'){ add(.42, .04, .42, 0, .46, 0, fr); for (let k = -2; k <= 2; k++) add(.018, .42, .018, k * .08, .7, -.2, fr); add(.44, .05, .03, 0, .92, -.2, fr); add(.03, .5, .03, -.2, .7, -.2, fr); add(.03, .5, .03, .2, .7, -.2, fr); }
        else { const cu = MAT('fabric', s.chair === 'orrstaWhite' ? '#d9d9d6' : '#9a9c98'); const o = new THREE.Mesh(rgeo(.43, .06, .43, .02), cu); o.position.set(0, .48, 0); o.castShadow = true; cg.add(o); add(.42, .08, .025, 0, .88, -.2, fr); add(.42, .02, .02, 0, .66, -.2, fr); add(.03, .45, .03, -.19, .7, -.2, fr); add(.03, .45, .03, .19, .7, -.2, fr); } });
      break; }
    case 'bed': { const up = s.uph || /tapi[tț]at/i.test(name), fr = up || s.mat ? MAT(s.mat || 'fabric', s.col) : body(s.col), hb = up || s.mat ? MAT(s.mat || 'fabric', s.col) : s.wood ? MAT('wood', s.col) : MAT('paint', s.col);
      if (up){ RB(w + .04, .32, d + .02, .04, 0, .2, .01, fr); RB(w + .06, h - .02, .12, .05, 0, (h - .02) / 2 + .02, -d / 2 + .05, hb); for (let i = 1; i < 6; i++) B(.006, h - .2, .006, -w / 2 + (w + .06) * i / 6, h / 2 + .05, -d / 2 + .112, MAT(s.mat || 'fabric', s.col)); }   // pat tapițat: bază îmbrăcată și tăblie capitonată
      B(w, .3, .02, 0, .2, d / 2 - .01, fr); B(.02, .3, d - .1, -w / 2 + .01, .2, .04, fr); B(.02, .3, d - .1, w / 2 - .01, .2, .04, fr); B(w, h, .05, 0, h / 2, -d / 2 + .025, hb);
      [[-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .05, .03, a * (w / 2 - .03), .025, b * (d / 2 - .03), MAT('paint', '#222')));
      if (s.boxes){ for (let i = 0; i < 2; i++) [-1, 1].forEach(sd => B(.02, .16, .9, sd * (w / 2 + .012), .12, -.35 + i * .95, fr)); }
      RB(w - .1, .2, d - .14, .05, 0, .44, .05, MAT('fabric', '#f7f7f5'));                   // saltea
      // lenjeria: pilota groasă care cade peste margini, întoarsă la cap, pled la picioare, perne și perne decorative
      // lenjeria: pilota cu fața de sus bombată și marginile rotunjite care cad peste laterale și la picioare, cearșaful
      // întors la cap, pledul împăturit (două straturi) pe treimea de la picioare; o singură suprafață textilă fiecare
      const duvet = MAT('cloth', '#eeeae3'), throwC = MAT('cloth', s.throw || '#9a7b5c'), D2 = (geo, x, y, z, m) => { const o = new THREE.Mesh(geo, m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
      const wm = w - .08, z0 = -d * .2, z1 = d / 2 - .03, dd = z1 - z0, top = .556, drop = up ? .17 : .19;
      D2(drapeGeo(wm, dd, drop, drop, .03), 0, top, z0 + dd / 2, duvet);
      RB(w - .1, .05, .22, .025, 0, .6, z0 + .09, MAT('fabric', '#f7f5f0'));                                                     // cearșaful întors
      D2(drapeGeo(wm + .02, .46, drop - .02, 0, .006), 0, top + .036, z1 - .3, throwC); D2(drapeGeo(wm + .04, .2, .1, 0, .004), 0, top + .048, z1 - .2, throwC);   // pled împăturit
      [-1, 1].forEach(a => { const pl = RB(.6, .15, .4, .07, a * (w / 4 - .02), .66, -d / 2 + .3, MAT('fabric', '#ffffff')); pl.rotation.x = -.25; });
      [-1, 1].forEach((a, i) => { const c = RB(.42, .36, .12, .06, a * (w / 6), .76, -d / 2 + .5, MAT('velvet', i ? '#c9b79c' : (s.throw || '#9a7b5c'))); c.rotation.x = -.35; c.rotation.z = a * .06; });
      break; }
    case 'night': { const m = body(s.col);
      if (s.type === 'hemnes'){ B(w, .03, d, 0, h - .015, 0, MAT('wood', '#b98f63')); B(w - .02, .22, d - .02, 0, h - .14, 0, m); B(w - .04, .02, d - .04, 0, .2, 0, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.04, h - .03, .04, a * (w / 2 - .025), (h - .03) / 2, b * (d / 2 - .025), m)); CY(.012, .012, .02, 0, h - .14, d / 2, MAT('metal', '#8a7a64'), 10); }
      else if (s.type === 'songesand'){ B(w, h - .06, d, 0, (h - .06) / 2 + .06, 0, m); [0, 1].forEach(i => B(w - .03, (h - .1) / 2 - .01, .015, 0, .08 + (h - .1) / 4 + i * (h - .1) / 2, d / 2 + .008, m)); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .06, .03, a * (w / 2 - .03), .03, b * (d / 2 - .03), m)); }
      else if (s.drawers || s.shelf || s.doors){ const body2 = s.col2 ? body(s.col2) : m, foot = .06; B(w, h - foot, d - .02, 0, foot + (h - foot) / 2, -.01, body2); B(w - .04, foot, d - .06, 0, foot / 2, -.02, MAT('paint', '#2a2a2a'));
        // sertarele de sus în jos; un raft deschis jos dacă produsul are „raft”; ușile ocupă ce rămâne
        const usable = h - foot - .02, open = s.shelf ? usable * (s.drawers ? .4 : .55) : 0, nd = s.drawers || (s.doors ? 0 : 1), dh = nd ? Math.min(.22, (usable - open) / Math.max(1, nd + (s.doors ? 1 : 0))) : 0;
        let y = h - .01; for (let i = 0; i < nd; i++){ front('drawer', 0, y - dh / 2, w - .01, dh, d / 2 - .02, m); y -= dh; }
        if (s.doors){ const rest = y - foot - open, n = Math.max(1, Math.min(2, s.doors)); for (let i = 0; i < n; i++) front('door', -w / 2 + w / n * (i + .5), foot + open + rest / 2, w / n, rest, d / 2 - .02, m, n === 1 ? 1 : (i ? -1 : 1)); }
        if (open) B(w - .04, .015, d - .06, 0, foot + open, -.02, body2); }
      else { B(w, h, d, 0, h / 2, 0, m); B(w - .03, .14, .01, 0, h - .1, d / 2 + .005, MAT('frost', '#e9ecec')); B(w - .04, .015, d - .04, 0, h * .4, 0, m); }
      CY(.075, .08, .02, 0, h + .01, 0, MAT('metal', '#2b2b2b')); CY(.01, .01, .3, 0, h + .16, 0, MAT('metal', '#2b2b2b'), 8);
      { const sh = new THREE.Mesh(new THREE.CylinderGeometry(.09, .14, .18, 28, 1, true), new THREE.MeshStandardMaterial({ color: lin('#efe6d6'), side: THREE.DoubleSide, roughness: .9, emissive: lin('#ffd9a0'), emissiveIntensity: .35 })); sh.position.set(0, h + .37, 0); g.add(sh); } break; }
    case 'wardrobe': { const m = vgrain(body(s.col));
      if (s.type === 'kleppstad'){ B(w, h, d - .04, 0, h / 2, -.02, m); B(w / 2 + .02, h - .06, .02, -w / 4 + .01, h / 2, d / 2 - .03, m); B(w / 2 + .02, h - .06, .02, w / 4 - .01, h / 2, d / 2 - .005, m); B(.012, h - .2, .02, -.06, h / 2, d / 2 - .01, MAT('metal', '#9a9a9a')); B(.012, h - .2, .02, .06, h / 2, d / 2 + .01, MAT('metal', '#9a9a9a')); }
      else if (s.doors || s.sliding || s.drawers){ B(w, h - .05, d - .02, 0, (h - .05) / 2 + .05, -.01, s.col2 ? body(s.col2) : m); B(w - .04, .05, d - .04, 0, .025, 0, m);
        const mir = MAT('mirror', '#dfe6ea'), base = .05, drH = s.drawers ? .2 : 0, n = s.sliding ? (w > 1.6 ? 3 : 2) : Math.max(1, s.doors || 2), nm = Math.min(n, s.mirrors || 0);
        // oglinzile pe ușile din mijloc; sertarele într-un rând jos, sub uși
        const mirIdx = new Set(Array.from({ length: nm }, (_, k) => Math.floor((n - nm) / 2) + k)), dw = w / n, top = h - .02;
        for (let i = 0; i < n; i++){ const x = -w / 2 + dw * (i + .5), fh = top - base - drH, z = d / 2 - .02 + (s.sliding ? (i % 2) * .025 : 0);
          if (mirIdx.has(i)){ B(dw - .006, fh - .006, .018, x, base + drH + fh / 2, z + .009, m); B(dw - .07, fh - .08, .006, x, base + drH + fh / 2, z + .021, mir); }
          else front('door', x, base + drH + fh / 2, dw, fh, z, m, i < n / 2 ? -1 : 1); }
        if (drH){ const nd = Math.max(1, s.drawers); for (let j = 0; j < nd; j++) front('drawer', -w / 2 + w / nd * (j + .5), base + drH / 2, w / nd, drH, d / 2 - .02, m); } }
      else { B(w, h - .05, d, 0, (h - .05) / 2 + .05, 0, m); B(w - .04, .05, d - .04, 0, .025, 0, m);
        for (let i = -1; i <= 1; i++){ B(w / 3 - .006, h - .08, .018, i * w / 3, h / 2 + .02, d / 2 + .009, i === 0 ? MAT('mirror', '#dfe6ea') : body(s.col)); if (i !== 0) CY(.012, .012, .025, i * w / 3 + (i < 0 ? .15 : -.15), h * .52, d / 2 + .03, MAT('metal', '#8f8f8f'), 12).rotation.x = Math.PI / 2; } }
      break; }
    case 'desk': { const m = s.col ? body(s.col) : MAT('paint', '#f2f2ef');
      if (s.type === 'torald'){ B(w, .02, d, 0, h - .01, 0, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => CY(.012, .012, h - .02, a * (w / 2 - .03), (h - .02) / 2, b * (d / 2 - .03), MAT('metal', '#e8e8e6'), 10)); }
      else { B(w, .03, d, 0, h - .015, 0, m); B(.02, h - .03, d, -w / 2 + .01, (h - .03) / 2, 0, m); B(.35, h - .03, d, w / 2 - .175, (h - .03) / 2, 0, m); { const nd = s.drawers || (s.doors ? 1 : 2), ph = h - .05, dh = s.doors ? .16 : ph / nd; for (let i = 0; i < nd; i++) front('drawer', w / 2 - .175, h - .03 - dh * (i + .5), .35, dh, d / 2 - .02, m); if (s.doors) front('door', w / 2 - .175, (h - .03 - dh * nd) / 2, .35, h - .03 - dh * nd, d / 2 - .02, m, -1); } B(w - .37, .3, .01, -.17, h - .3, -d / 2 + .01, m); }
      B(.54, .32, .02, -w * .12, h + .23, -d / 2 + .12, MAT('screen', '#0c0d0f')); B(.05, .12, .05, -w * .12, h + .06, -d / 2 + .12, MAT('metal', '#333')); B(.42, .015, .14, -w * .12, h + .008, .06, MAT('paint', '#2b2d31')); break; }
    case 'ochair': { const fab = MAT('fabric', s.col || '#3b3d42'), mesh = MAT('paint', '#1d1e21');
      for (let i = 0; i < 5; i++){ const a = i / 5 * Math.PI * 2, leg = B(.3, .03, .04, Math.cos(a) * .15, .07, Math.sin(a) * .15, mesh); leg.rotation.y = -a; CY(.025, .025, .04, Math.cos(a) * .3, .025, Math.sin(a) * .3, mesh, 10); }
      CY(.028, .028, .38, 0, .28, 0, MAT('chrome', '#bbb')); RB(.5, .09, .5, .03, 0, .5, .02, fab);
      { const bh = Math.min(.62, h - .78), back = MAT('fabric', s.col ? s.col : '#2c2e33'); B(.05, .3, .03, 0, .66, -.26, mesh);                       // coloana spătarului
        { const bp = RB(.46, bh, .035, .03, 0, .8 + bh / 2, -.29, back); bp.rotation.x = .12; RB(.48, .04, .045, .015, 0, .8 + .02, -.27, mesh); } // spătar dintr-o bucată, ușor înclinat, cu buiandrug lombar
        if (h > 1.2) { const hr = RB(.28, .1, .05, .02, 0, h - .07, -.3, fab); hr.rotation.x = .15; B(.03, .12, .02, 0, h - .16, -.29, mesh); } }
      [-1, 1].forEach(a => B(.04, .2, .25, a * .27, .64, 0, mesh)); break; }
    case 'kitchen': { const k = s.k || null, frame = MAT('metal', s.frame || '#34363a'), wd = .6;
      // sistemul de bucătărie (core/kitchen.ts): finisajul fronturilor, mânerele, blatul, suspendatele, placarea, LED-ul
      const fcol = k ? k.frontColor : (s.fronts || '#f4f4f1'), front = k && k.frontFinish === 'gloss' ? MAT('ceramic', fcol) : k && k.frontFinish === 'wood' ? MAT('wood', fcol) : MAT('paint', fcol);
      const hmat = MAT('metal', k ? k.handleColor : '#222'), htype = k ? k.handle : 'bar', topM = k ? k.topM : .038, topMat = k ? (k.topWood ? MAT('wood', k.topColor) : MAT('ceramic', k.topColor)) : MAT('wood', '#cdb592');
      const handle = (x, y, z, vertical) => { if (htype === 'none') return; if (htype === 'knob') { CY(.012, .012, .025, x, y, z + .01, hmat, 12).rotation.x = Math.PI / 2; return; }
        if (htype === 'profile') B(vertical ? .015 : .5, vertical ? .5 : .015, .02, x, y, z, hmat); else B(vertical ? .012 : .18, vertical ? .18 : .012, .015, x, y, z, hmat); };
      B(w, .08, wd - .05, 0, .04, -d / 2 + wd / 2, MAT('paint', '#2b2c2f')); const carc = MAT('paint', '#2d2f33'), sxc = -w / 2 + .45; // corpul de sub chiuvetă e mai jos: cuva intră în el
      B(sxc - .3 + w / 2, .78, wd - .02, (-w / 2 + sxc - .3) / 2, .47, -d / 2 + wd / 2, carc); B(w / 2 - sxc - .3, .78, wd - .02, (sxc + .3 + w / 2) / 2, .47, -d / 2 + wd / 2, carc); B(.6, .56, wd - .02, sxc, .36, -d / 2 + wd / 2, carc);
      const n = Math.round(w / .6); for (let i = 0; i < n; i++){ const cx = -w / 2 + w / n * (i + .5); B(w / n - .006, .74, .018, cx, .46, -d / 2 + wd + .009, front); handle(cx, htype === 'profile' ? .82 : .78, -d / 2 + wd + .025, false); }
      // blatul cu decupajul chiuvetei (ca la montaj: gaura e mai mică decât rama chiuvetei)
      { const sh = rrectPath(new THREE.Shape(), 0, 0, w + .01, d, .003); sh.holes.push(rrectPath(new THREE.Path(), -w / 2 + .45, 0, .42, .36, .03)); SLAB(sh, topM, .86 + topM, topMat); }
      const sinkMat = k ? MAT(lumOf(k.sinkColor) > .55 ? 'chrome' : 'paint', k.sinkColor) : MAT('chrome', '#c6c9cc'), tapMat = MAT('chrome', k ? k.tapColor : '#c6c9cc');
      // chiuveta: ramă subțire și cuva (mai închisă, cu scurgere), bateria cu pipă curbată
      const ct = .86 + topM, sx = -w / 2 + .45;
      { // chiuveta încastrată: rama subțire pe blat, cuva adâncă de 18 cm cu pereți și fund, sita de scurgere
        const rim = rrectPath(new THREE.Shape(), sx, 0, .48, .42, .035); rim.holes.push(rrectPath(new THREE.Path(), sx, 0, .4, .34, .025)); SLAB(rim, .006, ct + .006, sinkMat);
        const bowlM = k ? MAT(lumOf(k.sinkColor) > .55 ? 'chrome' : 'paint', k.sinkColor) : MAT('chrome', '#b9bcbf'), dep = .18, by = ct - dep;
        B(.4, .006, .34, sx, by, 0, bowlM); B(.4, dep, .006, sx, by + dep / 2, -.17, bowlM); B(.4, dep, .006, sx, by + dep / 2, .17, bowlM); B(.006, dep, .34, sx - .2, by + dep / 2, 0, bowlM); B(.006, dep, .34, sx + .2, by + dep / 2, 0, bowlM);
        CY(.035, .035, .004, sx + .08, by + .005, .06, MAT('metal', '#6a6d71'), 24); CY(.012, .012, .005, sx + .08, by + .007, .06, MAT('paint', '#2a2b2d'), 12); }
      CY(.022, .026, .03, sx, ct + .015, -.2, tapMat, 16); CY(.012, .012, .3, sx, ct + .17, -.2, tapMat, 12);
      { const sp = new THREE.Mesh(new THREE.TorusGeometry(.09, .012, 8, 16, Math.PI), tapMat); sp.rotation.y = Math.PI / 2; sp.position.set(sx, ct + .32, -.11); sp.castShadow = true; g.add(sp); }
      B(.016, .05, .016, sx, ct + .26, -.02, tapMat); B(.012, .012, .1, sx + .03, ct + .27, -.2, tapMat);
      // plita: sticlă neagră cu cele patru zone de gătit marcate
      const hx = w / 2 - .55; B(.58, .006, .5, hx, ct + .003, 0, MAT('screen', '#0d0d0e'));
      for (const [dx, dz, r] of [[-.14, -.1, .09], [.14, -.1, .07], [-.14, .12, .07], [.14, .12, .09]]){ const z = new THREE.Mesh(new THREE.TorusGeometry(r, .003, 4, 32), MAT('paint', '#3c3d40')); z.rotation.x = Math.PI / 2; z.position.set(hx + dx, ct + .0065, dz); g.add(z); }
      // cuptorul: rama inox, ușa de sticlă cu geam fumuriu, mânerul bară și panoul de comandă
      const oz = -d / 2 + wd + .02; B(.58, .58, .012, hx, .46, oz, MAT('metal', '#9da0a3')); B(.54, .38, .006, hx, .4, oz + .008, MAT('screen', '#111214')); B(.5, .08, .006, hx, .68, oz + .008, MAT('screen', '#1a1b1d'));
      B(.46, .016, .016, hx, .62, oz + .035, MAT('metal', '#c4c7ca')); [-.18, .18].forEach(a => B(.012, .012, .03, hx + a, .62, oz + .02, MAT('metal', '#c4c7ca')));
      [-.2, -.14, .14, .2].forEach(a => CY(.014, .014, .015, hx + a, .68, oz + .015, MAT('metal', '#c4c7ca'), 16).rotation.x = Math.PI / 2);
      // pe blat, ca într-o casă locuită: tocătorul sprijinit de perete, ibricul, un bol cu fructe, sticlele lângă plită
      { const wood = MAT('wood', '#b38a5e'), bx = w > 2 ? -w / 2 + 1.05 : sx; const cb = B(.3, .4, .02, bx, ct + .2, -d / 2 + .06, wood); cb.rotation.x = -.12; B(.22, .3, .018, bx + .2, ct + .15, -d / 2 + .07, MAT('wood', '#8a6340')).rotation.x = -.12;
        if (w > 1.6){ const kx = hx - .45; CY(.075, .085, .2, kx, ct + .1, -.1, MAT('ceramic', '#ece8e0'), 24); CY(.05, .07, .02, kx, ct + .21, -.1, MAT('ceramic', '#ece8e0'), 24); B(.03, .14, .06, kx - .09, ct + .1, -.1, MAT('paint', '#2b2b2b'));
          const bowl = new THREE.Mesh(new THREE.CylinderGeometry(.14, .07, .07, 32, 1, true), MAT('ceramic', '#cfc5b4')); bowl.material.side = THREE.DoubleSide; bowl.position.set(kx - .35, ct + .04, .05); bowl.castShadow = true; g.add(bowl);
          [['#c9572f', -.04, .02], ['#e0a83a', .05, -.02], ['#8aa04a', 0, .06], ['#c9572f', .03, .05]].forEach(([c, a, b], i) => { const f = new THREE.Mesh(new THREE.SphereGeometry(.038, 16, 12), MAT('paint', c)); f.position.set(kx - .35 + a, ct + .07 + (i > 2 ? .03 : 0), .05 + b); f.castShadow = true; g.add(f); }); }
        [['#3d4a2a', .32, .03], ['#6b3f22', .24, .028]].forEach(([c, hh, r], i) => { CY(r, r, hh, hx + .36 + i * .07, ct + hh / 2, -d / 2 + .08, MAT('glass', c), 16); CY(.01, .01, .04, hx + .36 + i * .07, ct + hh + .02, -d / 2 + .08, MAT('wood', '#9c7a52'), 8); }); }
      const upper = k ? k.upper : 'open', top = h - .02, wallBottom = upper === 'none' ? 1.5 : top - .72;
      if (upper === 'open'){ // polițe ENHET cu cadru deschis + uși pe jumătate din module
        B(w, .02, .32, 0, top, -d / 2 + .16, frame); B(w, .02, .32, 0, top - .72, -d / 2 + .16, frame); for (let i = 0; i <= n; i++) B(.02, .72, .32, -w / 2 + .01 + i * (w - .02) / n, top - .36, -d / 2 + .16, frame);
        B(w, .015, .3, 0, top - .36, -d / 2 + .16, frame); for (let i = 0; i < n; i += 2){ const cx = -w / 2 + w / n * (i + .5); B(w / n - .01, .7, .015, cx, top - .36, -d / 2 + .32, front); handle(cx + w / n / 2 - .06, top - .62, -d / 2 + .335, true); }
        for (let i = 1; i < n; i += 2){ B(.1, .12, .1, -w / 2 + w / n * (i + .5) - .08, top - .64, -d / 2 + .15, MAT('ceramic', '#eae6de')); B(.14, .1, .14, -w / 2 + w / n * (i + .5) + .08, top - .3, -d / 2 + .15, MAT('ceramic', '#6a7c72')); } }
      else if (upper === 'closed'){ B(w, .72, .32, 0, top - .36, -d / 2 + .16, MAT('paint', '#2d2f33')); for (let i = 0; i < n; i++){ const cx = -w / 2 + w / n * (i + .5); B(w / n - .006, .7, .018, cx, top - .36, -d / 2 + .329, front); handle(cx + w / n / 2 - .06, top - .62, -d / 2 + .345, true); } }
      if (upper !== 'none') B(.6, .12, .46, w / 2 - .55, top - .9, -d / 2 + .23, MAT('metal', '#bfc2c5'));
      // placarea dintre blat și suspendate
      const bsH = Math.max(.2, wallBottom - (.86 + topM)), bsY = .86 + topM + bsH / 2;
      if (k && k.backsplash === 'tile' && k.backsplashTile){ const t = wallTexture({ kind: 'tile', color: k.backsplashColor, sizeCm: k.backsplashTile }, 'kit:' + k.backsplashColor), tc = t.clone(); tc.userData = { owned: true }; tc.needsUpdate = true; tc.repeat.set(w / t.userData.size, bsH / t.userData.size);
        const bm = new THREE.MeshStandardMaterial({ map: tc, roughness: .3 }); bm.userData = { kind: 'tile', col: k.backsplashColor, size: [k.backsplashTile[0] / 100, k.backsplashTile[1] / 100] }; B(w, bsH, .008, 0, bsY, -d / 2 + .004, bm); }
      else if (k) B(w, bsH, .008, 0, bsY, -d / 2 + .004, k.backsplash === 'glass' ? MAT('ceramic', k.backsplashColor) : k.backsplash === 'countertop' ? topMat : MAT('paint', k.backsplashColor));
      else B(w, .01, .02, 0, 1.25, -d / 2 + .01, MAT('ceramic', '#f1efea'));
      if (k && k.led && upper !== 'none'){ const lm = new THREE.MeshStandardMaterial({ color: lin(k.led), emissive: lin(k.led), emissiveIntensity: 1.4 }); B(w - .04, .008, .015, 0, wallBottom - .006, -d / 2 + .3, lm); }
      break; }
    case 'fridge': { const m = s.inox ? MAT('metal', s.col) : MAT('paint', s.col); RB(w, h, d, .02, 0, h / 2, 0, m); B(w - .01, .004, .01, 0, h * .64, d / 2 + .001, MAT('paint', '#bdbdbd')); B(.02, .4, .03, w / 2 - .05, h * .8, d / 2 + .015, MAT('metal', '#aaa')); B(.02, .3, .03, w / 2 - .05, h * .45, d / 2 + .015, MAT('metal', '#aaa')); break; }
    case 'shower': { const bm = s.b || null, pm = MAT('metal', bm ? bm.metal : s.prof), fit = MAT('chrome', bm ? bm.metal : '#cfcfcf'), gl = MAT(s.frost ? 'frost' : 'glass', '#dcecf2');
      // poliță de nișă în perete cu sticle (șampon, gel, ulei de corp)
      B(.3, .012, .1, -w / 2 + .45, 1.0, -d / 2 + .05, MAT('ceramic', '#f2f0eb')); [['#e9e3d8', .17], ['#5f6b5c', .2], ['#b9824f', .14]].forEach(([c, hh], i) => CY(.026, .026, hh, -w / 2 + .37 + i * .08, 1.006 + hh / 2, -d / 2 + .06, MAT('ceramic', c), 16));
      // walk-in: fără cădiță și fără ușă, doar un panou fix de sticlă cu profil sus și o bară de rigidizare
      if (s.walkin){ const p = new THREE.Mesh(new THREE.BoxGeometry(.008, h - .06, d * .8), gl); p.position.set(w / 2 - .004, h / 2 + .01, -d * .1); g.add(p); B(.02, .02, d * .8, w / 2 - .004, h - .02, -d * .1, pm);
        B(w * .6, .004, .06, w * .1, .003, d / 2 - .05, MAT('metal', '#444')); CY(.012, .012, h - .4, -w / 2 + .12, h / 2 + .1, -d / 2 + .04, fit, 10); CY(.12, .12, .012, -w / 2 + .25, h - .08, -d / 2 + .25, fit, 24); break; }
      B(w, .05, d, 0, .025, 0, MAT('ceramic', '#fafafa'));
      if (s.round){ const arc = new THREE.Mesh(new THREE.CylinderGeometry(w - .02, w - .02, h - .06, 32, 1, true, 0, Math.PI / 2), gl); arc.position.set(-w / 2 + .01, h / 2 + .03, -d / 2 + .01); g.add(arc); }
      else { const t = s.thick ? .03 : .02; [[w, .008, 0, d / 2 - .004], [.008, d, w / 2 - .004, 0]].forEach(([a, b, x, z]) => { const p = new THREE.Mesh(new THREE.BoxGeometry(a, h - .06, b), gl); p.position.set(x, h / 2 + .03, z); g.add(p); });
        B(t, h - .05, t, w / 2 - t / 2, h / 2, d / 2 - t / 2, pm); B(w, t, t, 0, h - t / 2, d / 2 - t / 2, pm); B(t, t, d, w / 2 - t / 2, h - t / 2, 0, pm); }
      CY(.012, .012, h - .4, -w / 2 + .12, h / 2 + .1, -d / 2 + .04, fit, 10); CY(.1, .1, .015, -w / 2 + .2, h - .1, -d / 2 + .2, fit, 24); break; }
    case 'vanity': { const m = body(s.col), top = s.hang ? .86 : h, bot = s.hang ? Math.max(.12, top - h) : (s.legs ? .15 : .02), lip = .1;
      // înălțimea din catalog e a ansamblului (corp + lavoar); suspendatul are muchia lavoarului la 86 cm, ca la montaj
      if (s.legs) [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .15, .03, a * (w / 2 - .04), .075, b * (d / 2 - .04), MAT('metal', '#222')));
      if (!s.hang && !s.legs) B(w - .04, bot, d - .06, 0, bot / 2, -.02, MAT('paint', '#d9d9d6'));                   // soclul
      const fh = top - lip - bot; B(w, fh - .08, d, 0, bot + (fh - .08) / 2, 0, m);                                  // corpul (sub cuvă rămâne loc)
      [-1, 1].forEach(a => B(.018, .08, d, a * (w / 2 - .009), top - lip - .04, 0, m)); B(w, .08, .018, 0, top - lip - .04, -d / 2 + .009, m);
      // fronturile după produs: sertare („cu sertare”), sertar + uși („sertare și uși”), altfel două uși
      const drawers = /sertar/i.test(name), doors = !drawers || /u[sș]i/i.test(name), fz = d / 2 + .009, fm = m, hm = MAT('metal', s.col && lumOf(s.col) < .3 ? '#9a9a9a' : '#3a3a3c');
      if (drawers && !doors){ const n2 = fh > .45 ? 2 : 1; for (let i = 0; i < n2; i++){ const hh = fh / n2 - .006, y = bot + fh / n2 * (i + .5); B(w - .006, hh, .018, 0, y, fz, fm); B(w * .4, .012, .02, 0, y + hh / 2 - .04, fz + .015, hm); } }
      else { let yTop = top - lip; if (drawers){ const hh = .16; B(w - .006, hh - .006, .018, 0, yTop - hh / 2, fz, fm); B(w * .4, .012, .02, 0, yTop - .05, fz + .015, hm); yTop -= hh; }
        const dh = yTop - bot - .006; [-1, 1].forEach(a => { B(w / 2 - .006, dh, .018, a * w / 4, bot + dh / 2 + .003, fz, fm); B(.012, .12, .02, a * .03, yTop - .1, fz + .015, hm); }); }
      // lavoarul ceramic: puntea cu decupaj oval (o singură bucată, fără goluri la colțuri), cuva rotunjită sub ea, scurgerea, bateria pe puntea din spate
      const cer = MAT('ceramic', '#fbfbfa'), bw = w + .01, bd = d + .02, bz = .01, ow = bw - .1, od = Math.min(bd - .15, ow * .75), oz = bz - bd / 2 + .1 + od / 2;
      { const sh = rrectPath(new THREE.Shape(), 0, bz, bw, bd, .03); sh.holes.push(ellipsePath(new THREE.Path(), 0, oz, ow / 2, od / 2)); SLAB(sh, lip, top, cer); }
      { const prof = [[0, -.15], [.22, -.148], [.38, -.125], [.46, -.08], [.495, -.03], [.5, 0]].map(([x, y]) => new THREE.Vector2(x, y)), bowl = new THREE.Mesh(new THREE.LatheGeometry(prof, 40), cer);
        bowl.scale.set(ow, 1, od); bowl.position.set(0, top - .004, oz); bowl.receiveShadow = true; g.add(bowl); CY(.018, .018, .004, 0, top - .152, oz, MAT('chrome', '#9a9a9a'), 16); }
      { const tm = MAT('chrome', s.b ? s.b.metal : '#cfcfcf'), tz = bz - bd / 2 + .05; CY(.024, .026, .04, 0, top + .02, tz, tm, 20); CY(.016, .016, .14, 0, top + .1, tz, tm, 16);
        B(.026, .02, .12, 0, top + .16, tz + .05, tm); B(.012, .045, .05, 0, top + .19, tz - .005, tm); }
      const ty = top - .14;
      B(w * .85, .6, .02, 0, 1.55, -d / 2 + .01, MAT('mirror', '#dde6ea')); if (s.legs) B(w * .6, .04, .06, 0, 1.9, -d / 2 + .04, MAT('paint', '#f4f4f1'));
      // ce stă pe un lavoar folosit: dozatorul de săpun și paharul, pe puntea din spate (fronturile rămân la vedere)
      const tt = ty + .14; CY(.032, .032, .13, w / 2 - .1, tt + .065, -d / 2 + .07, MAT('ceramic', '#7c8a7d'), 20); CY(.006, .006, .03, w / 2 - .1, tt + .145, -d / 2 + .07, MAT('chrome', '#bbb'), 8); B(.04, .006, .006, w / 2 - .085, tt + .158, -d / 2 + .07, MAT('chrome', '#bbb'));
      CY(.035, .03, .1, w / 2 - .19, tt + .05, -d / 2 + .07, MAT('glass', '#d8e2e0'), 20);
      break; }
    case 'wc': { const cer = MAT('ceramic', s.col || '#fbfbfa'),   // vasele negre mat (Dura Petra) rămân negre
      metal = MAT('chrome', s.b ? s.b.metal : '#d4d4d4'), nm = name.toLowerCase();
      // după produs: montaj (suspendat / pe pardoseală), rezervor (încastrat cu clapetă pe perete / ceramic pe vas), forma vasului
      const hung = s.mount ? s.mount === 'wall' : /suspendat/.test(nm) || !!(s.b && s.b.wallHung);
      const tank = s.cistern ? s.cistern === 'ceramic' : /cu rezervor|monobloc/.test(nm) && !/[îi]ncastrat/.test(nm);
      const square = s.shape ? s.shape === 'square' : /square|p[ăa]trat/.test(nm);
      const bw = Math.min(w, .4), back = -d / 2, tankD = tank ? .17 : 0, frameD = tank ? 0 : .12, z0 = back + tankD + frameD, L = Math.max(.4, Math.min(.6, d - tankD)), rim = .41, cz = z0 + L / 2;
      // vasul: oval (profil rotunjit spre pardoseală) sau pătrat cu colțuri rotunjite
      if (square){ RB(bw, .2, L, .05, 0, rim - .1, cz, cer); RB(bw * .82, hung ? .12 : rim - .2, L * .7, .04, 0, hung ? rim - .26 : (rim - .2) / 2, cz - L * .1, cer); }
      else { const prof = [[0, -1], [.3, -1], [.37, -.85], [.43, -.5], [.48, -.18], [.5, 0]].map(([x, y]) => new THREE.Vector2(x, y)), hb = hung ? .3 : .22;
        const bowl = new THREE.Mesh(new THREE.LatheGeometry(prof, 40), cer); bowl.scale.set(bw, hb, L); bowl.position.set(0, rim, cz); bowl.castShadow = bowl.receiveShadow = true; g.add(bowl);
        if (!hung){ const ft = CY(.5, .5, rim - hb + .03, 0, (rim - hb + .03) / 2, cz - L * .08, cer, 32); ft.scale.set(bw * .55, 1, L * .55); } }   // piciorul vasului pe pardoseală
      // colacul și capacul închis, cu aceeași formă ca vasul
      if (square){ RB(bw - .005, .025, L - .01, .02, 0, rim + .0125, cz + .005, cer); RB(bw - .02, .02, L - .03, .02, 0, rim + .035, cz + .01, MAT('ceramic', '#f6f6f4')); }
      else { const seat = CY(.5, .5, .022, 0, rim + .011, cz + .005, cer, 40); seat.scale.set(bw, 1, L); const lid = CY(.5, .49, .02, 0, rim + .032, cz + .01, MAT('ceramic', '#f6f6f4'), 40); lid.scale.set(bw * .97, 1, L * .95); }
      B(.06, .03, .05, 0, rim + .02, z0 + .025, cer);                                                                    // balamalele colacului
      if (tank){ RB(bw * .92, .38, tankD - .01, .03, 0, rim + .19, back + tankD / 2, cer); RB(bw * .95, .03, tankD + .005, .012, 0, rim + .395, back + tankD / 2, cer); CY(.022, .022, .01, 0, rim + .415, back + tankD / 2, metal, 20); }
      else { // rezervor încastrat: placarea instalației (până la 1,2 m) și clapeta de acționare pe ea
        const fz = back + frameD; B(Math.max(.5, bw + .14), 1.15, frameD, 0, .575, back + frameD / 2, MAT('paint', '#efefec')); B(.25, .165, .012, 0, 1.0, fz + .006, metal); B(.11, .14, .004, -.06, 1.0, fz + .014, MAT('chrome', s.b ? s.b.metal : '#e2e2e2')); B(.11, .14, .004, .06, 1.0, fz + .014, MAT('chrome', s.b ? s.b.metal : '#e2e2e2')); }
      // suportul de hârtie pe peretele lateral din dreapta vasului
      { const hm = MAT('chrome', s.b ? s.b.metal : '#cfcfcf'); B(.012, .05, .03, bw / 2 + .2, .72, back + .015, hm); B(.11, .008, .008, bw / 2 + .25, .72, back + .05, hm); const roll = CY(.055, .055, .1, bw / 2 + .26, .72, back + .06, MAT('paint', '#f7f6f2'), 24); roll.rotation.z = Math.PI / 2; } break; }
    case 'bathtub': { const cer = MAT('ceramic', '#fbfbfa'), metal = MAT('chrome', s.b ? s.b.metal : '#cfcfcf'), water = MAT('glass', '#cfe3e6');
      // cada după produs: încastrată cu mască (dreptunghiulară sau asimetrică pe colț) sau freestanding (ovală / dreptunghiulară, cu sau fără picioare)
      const rim = Math.min(h, .6), free = s.type === 'freestanding', corner = s.type === 'corner', lip = .06;
      if (free && s.shape === 'oval'){ const prof = [[0, 0], [.42, 0], [.47, .3], [.5, 1]].map(([x, y]) => new THREE.Vector2(x, y)); const shell = new THREE.Mesh(new THREE.LatheGeometry(prof, 48), cer); const lift = s.legs ? .1 : 0;
        shell.scale.set(w, rim - lift, d); shell.position.y = lift; shell.castShadow = shell.receiveShadow = true; g.add(shell);
        const inner = new THREE.Mesh(new THREE.CylinderGeometry(.5, .44, .02, 48), MAT('ceramic', '#f1f1ef')); inner.scale.set(w - .1, 1, d - .1); inner.position.y = rim - .02; g.add(inner);
        if (s.legs) [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => CY(.025, .018, .1, a * w * .32, .05, b * d * .3, metal, 12)); }
      else if (corner){ const sh = new THREE.Shape(); const hx = s.hand === 'left' ? -1 : 1; sh.moveTo(-w / 2, d / 2); sh.lineTo(-w / 2, -d / 2); sh.lineTo(w / 2, -d / 2); sh.lineTo(w / 2, d / 2 - .25); sh.quadraticCurveTo(w / 2 - .1, d / 2, w / 2 - .45, d / 2); sh.lineTo(-w / 2, d / 2);
        const body = new THREE.Mesh(slabGeo(sh, rim), cer); body.position.y = rim; body.scale.x = hx; body.castShadow = body.receiveShadow = true; g.add(body);
        const ish = new THREE.Shape(); ish.moveTo(-w / 2 + lip + .02, d / 2 - lip - .02); ish.lineTo(-w / 2 + lip + .02, -d / 2 + lip); ish.lineTo(w / 2 - lip - .15, -d / 2 + lip); ish.quadraticCurveTo(w / 2 - lip, -d / 2 + lip, w / 2 - lip - .1, d / 2 - .3); ish.lineTo(-w / 2 + lip + .02, d / 2 - lip - .02);
        const inr = new THREE.Mesh(slabGeo(ish, .01), MAT('ceramic', '#f1f1ef')); inr.position.y = rim + .002; inr.scale.x = hx; g.add(inr); }
      else { RB(w, rim, d, .02, 0, rim / 2, 0, free ? cer : MAT('ceramic', '#f6f6f4'));            // cuva cu masca frontală (sau corpul freestanding)
        RB(w - lip * 2, .012, d - lip * 2, .006, 0, rim + .001, 0, MAT('ceramic', '#efefed'));       // fundul cuvei văzut de sus
        { const r = rrectPath(new THREE.Shape(), 0, 0, w, d, .05); r.holes.push(rrectPath(new THREE.Path(), 0, 0, w - lip * 2, d - lip * 2, .18)); SLAB(r, .02, rim + .02, cer); } }
      // bateria pe perete, deasupra capătului cu scurgere, și para de duș pe suport
      { const tx = (corner && s.hand === 'left' ? 1 : -1) * (w / 2 - .25), tz = -d / 2 - .0; B(.16, .05, .05, tx, rim + .22, tz + .025, metal); CY(.016, .016, .12, tx, rim + .2, tz + .1, metal, 12).rotation.x = Math.PI / 2; B(.03, .14, .03, tx + .12, rim + .45, tz + .02, metal); }
      break; }
    case 'bidet': { const cer = MAT('ceramic', s.col || '#fbfbfa'), metal = MAT('chrome', s.b ? s.b.metal : '#cfcfcf'), rim = .4, L = Math.min(.56, d - .12), cz = -d / 2 + .12 + L / 2;
      B(Math.max(.5, w + .14), 1.15, .12, 0, .575, -d / 2 + .06, MAT('paint', '#efefec'));   // cadrul încastrat placat, ca la WC-ul suspendat
      if (s.shape === 'square') RB(Math.min(w, .38), .22, L, .05, 0, rim - .11, cz, cer);
      else { const prof = [[0, -1], [.3, -1], [.4, -.6], [.48, -.2], [.5, 0]].map(([x, y]) => new THREE.Vector2(x, y)), b = new THREE.Mesh(new THREE.LatheGeometry(prof, 40), cer); b.scale.set(Math.min(w, .38), .26, L); b.position.set(0, rim, cz); b.castShadow = true; g.add(b); }
      const bowl = CY(.5, .45, .012, 0, rim + .002, cz + .04, MAT('ceramic', '#efefed'), 32); bowl.scale.set(Math.min(w, .38) * .7, 1, L * .6);
      CY(.02, .022, .05, 0, rim + .025, cz - L / 2 + .06, metal, 16); B(.02, .02, .08, 0, rim + .06, cz - L / 2 + .1, metal); break; }
    case 'towelrad': { const m = s.metal ? MAT('chrome', s.col || '#cfcfcf') : MAT('paint', s.col || '#f4f4f2'), y0 = .15, tz = -d / 2 + .05;
      // calorifer port-prosop: doi montanți și barele (rotunde sau plate), la 15 cm de pardoseală
      [-1, 1].forEach(a => CY(.014, .014, h, a * (w / 2 - .015), y0 + h / 2, tz, m, 12));
      const n = Math.max(6, Math.round(h / .065)); for (let i = 0; i < n; i++){ const y = y0 + .04 + (h - .08) * i / (n - 1); if (s.flat) B(w - .03, .012, .03, 0, y, tz, m); else { const bar = CY(.009, .009, w - .03, 0, y, tz, m, 10); bar.rotation.z = Math.PI / 2; } }
      if (!s.metal) B(.07, .05, .04, w / 2 - .05, y0 - .04, tz, MAT('paint', '#e9e9e6')); break; }
    case 'column': { const m = body(s.col || '#f5f5f3'), y0 = s.hang ? .3 : 0, doors = s.doors || 1, hm = MAT('metal', '#9a9a9a');
      // dulap înalt de baie (suspendat sau pe sol); dulapul pentru mașina de spălat are golul mașinii jos
      if (s.washer){ B(w, h - .9, d, 0, .9 + (h - .9) / 2, 0, m); [-1, 1].forEach(a => B(.018, .9, d, a * (w / 2 - .009), .45, 0, m)); B(w, .02, d, 0, .89, 0, m);
        const wm = MAT('paint', '#f7f7f5'); B(.6, .85, .5, 0, .425, -d / 2 + .25 + .02, wm); const door = CY(.17, .17, .02, 0, .5, -d / 2 + .53, MAT('glass', '#9fb3ba'), 32); door.rotation.x = Math.PI / 2; B(.5, .08, .01, 0, .79, -d / 2 + .525, MAT('screen', '#2b2b2b'));
        for (let i = 0; i < doors; i++) B(w / doors - .006, h - .92, .018, -w / 2 + w / doors * (i + .5), .9 + (h - .9) / 2, d / 2 + .009, m); }
      else { const ch = s.hang ? h : h - .05; B(w, ch, d, 0, y0 + (s.hang ? 0 : .05) + ch / 2, 0, m);
        const dh = (ch - .006 * doors) / doors; for (let i = 0; i < doors; i++){ const y = y0 + (s.hang ? 0 : .05) + dh * (i + .5) + .003 * (2 * i + 1); B(w - .006, dh, .018, 0, y, d / 2 + .009, m); B(.012, .12, .02, w / 2 - .04, y + (i === 0 ? dh / 2 - .1 : -dh / 2 + .1), d / 2 + .025, hm); } }
      break; }
    case 'mirrorcab': { const top = 1.85, mir = MAT('mirror', '#dfe6ea');
      // oglinda / dulapul cu oglindă peste lavoar, cu muchia de sus la 1,85 m; rotundă cu LED în spate sau dulap cu 1–3 uși oglindă
      if (s.round){ const r = Math.min(w, h) / 2, cy = top - r; const disc = CY(r, r, .02, 0, cy, -d / 2 + .02, mir, 48); disc.rotation.x = Math.PI / 2;
        if (s.led){ const glow = CY(r * .97, r * .97, .01, 0, cy, -d / 2 + .008, MAT('emit', '#fff4e0'), 48); glow.rotation.x = Math.PI / 2; }
        if (s.shelf) B(w * .8, .02, .1, 0, cy - r + .02, -d / 2 + .07, MAT('glass', '#e8eef0')); }
      else { const m = body(s.col || '#f5f5f3'), doors = s.doors || 1, y = top - h / 2; B(w, h, d - .02, 0, y, -.01, m);
        for (let i = 0; i < doors; i++) B(w / doors - .006, h - .01, .012, -w / 2 + w / doors * (i + .5), y, d / 2 - .006, mir);
        if (s.led) B(w - .04, .02, .03, 0, top - h - .01, d / 2 - .03, MAT('emit', '#fff4e0')); }
      break; }
    case 'boiler': { const m = MAT('ceramic', '#f6f6f4'), top = 2.05, y = top - h / 2;
      // boiler electric pe perete: plat (Velis) sau cilindric, cu racordurile jos
      if (s.flat){ RB(w, h, d, .06, 0, y, 0, m); B(w * .5, .05, .005, 0, y + h * .3, d / 2 + .002, MAT('screen', '#3b3d40')); }
      else { const c = CY(w / 2, w / 2, h, 0, y, 0, m, 40); c.scale.z = d / w; }
      [-.08, .08].forEach(a => CY(.01, .01, .25, a, top - h - .125, 0, MAT('chrome', '#cfcfcf'), 10)); break; }
    case 'shoe': { const m = body(s.col); B(w, h - .04, d, 0, (h - .04) / 2 + .04, 0, m); B(w + .02, .025, d + .02, 0, h - .012, 0, m); [[-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .04, .03, a * (w / 2 - .03), .02, b * (d / 2 - .03), m));
      if (s.doors){ // pantofar înalt cu uși (2K), oglinda pe una dintre uși dacă produsul o are
        const n = s.doors, fh = h - .1; for (let i = 0; i < n; i++){ const x = -w / 2 + w / n * (i + .5), mir = s.mirrors && i === n - 1;
          if (mir){ B(w / n - .006, fh, .018, x, .06 + fh / 2, d / 2 + .009, m); B(w / n - .07, fh - .1, .006, x, .06 + fh / 2, d / 2 + .021, MAT('mirror', '#dfe6ea')); }
          else front('door', x, .06 + fh / 2, w / n, fh, d / 2, m, i ? -1 : 1); }
        break; }
      const rows = s.two ? 2 : 2, cols = s.two ? 1 : 2; for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++){ const fw = w / cols - .01, fh = (h - .1) / rows - .015, x = -w / 2 + w / cols * (c + .5), y = .06 + (h - .1) / rows * (r + .5); B(fw, fh, .018, x, y, d / 2 + .009, m); CY(.012, .012, .02, x, y + fh / 2 - .05, d / 2 + .025, MAT('metal', '#7c6a55'), 10).rotation.x = Math.PI / 2; }
      break; }
    case 'mirror': { const mir = MAT('mirror', '#dfe6ea');
      if (d < .08){ // oglindă decorativă: rama subțire în culoarea produsului (negru mat, auriu, stejar), rotundă sau dreptunghiulară; pe perete sau pe suport de podea
        const fc = s.col || '#1e1e20', fm = s.wood ? MAT('wood', fc) : MAT(/auriu|alam/i.test(name) ? 'chrome' : 'metal', fc), floor = /podea|suport/i.test(name) || !s.wall, y0 = floor ? .02 : Math.max(.9, 1.6 - h / 2), lean = floor ? .08 : 0;
        const grp = new THREE.Group(); grp.position.set(0, y0, floor ? -.04 : -d / 2 + .02); grp.rotation.x = -lean; g.add(grp);
        const add = (o) => { o.castShadow = true; grp.add(o); return o; };
        if (s.round){ const r = Math.min(w, h) / 2; const ring = add(new THREE.Mesh(new THREE.TorusGeometry(r - .012, .014, 10, 64), fm)); ring.position.y = r; const glass = add(new THREE.Mesh(new THREE.CylinderGeometry(r - .015, r - .015, .006, 64), mir)); glass.rotation.x = Math.PI / 2; glass.position.y = r; }
        else { const t = s.wood ? .04 : .018; [[w, t, 0, h - t / 2], [w, t, 0, t / 2], [t, h, -w / 2 + t / 2, h / 2], [t, h, w / 2 - t / 2, h / 2]].forEach(([bw, bh, x, y]) => { const o = add(new THREE.Mesh(new THREE.BoxGeometry(bw, bh, .025), fm)); o.position.set(x, y, 0); });
          const glass = add(new THREE.Mesh(new THREE.BoxGeometry(w - 2 * t, h - 2 * t, .006), mir)); glass.position.set(0, h / 2, 0); }
        if (floor) B(.02, h * .9, .02, 0, h * .45, -.25, fm);
        break; }
      // corp cu ușă-oglindă (HEMNES): corpul alb și oglinda pe ușă
      const m = MAT('paint', s.col || '#f3f3f0'), y0 = s.wall ? .95 : 0; B(w, h, d, 0, y0 + h / 2, 0, m); B(w - .06, h - .08, .01, 0, y0 + h / 2, d / 2 + .006, mir); break; }
  }
  return g;
}


  // ---------- casa (reconstruită la fiecare schimbare de plan) ----------
  let house = new THREE.Group(), ceilings = new THREE.Group(), walls = new THREE.Group(), furniture = new THREE.Group(); scene.add(house, furniture);
  let doorLeaves = [], doorsOpen = true; // ușile cu balamale: deschise/închise, animate în bucla de randare
  let colliders = [], blockers = [], structBlockers = [], plan = null, W = 1, D = 1, C = new THREE.Vector3(), pickables = [];
  // la reconstruire: geometria și texturile clonate pentru finisaje (fiecare clonă e o încărcare separată pe GPU)
  const disposeGroup = g => g.traverse(o => { if (o.geometry && !(o.geometry.userData && o.geometry.userData.shared)) o.geometry.dispose(); const m = o.material; if (m && m.map && m.map.userData && m.map.userData.owned){ m.map.dispose(); m.dispose(); } else if (m && m.userData && m.userData.ownedMat) m.dispose(); });
  const owned = m => { m.userData = { ...(m.userData || {}), ownedMat: true }; return m; };
  function box(w, h, d, mat, x, y, z, rotY, parent){ const m = new THREE.Mesh(boxGeo(w, h, d, false), mat); m.position.set(x, y, z); m.rotation.y = rotY || 0; m.castShadow = m.receiveShadow = true; parent.add(m); return m; }
  function buildHouse(p){
    disposeGroup(house); scene.remove(house); interiorLights.length = 0; roomLights.length = 0; doorLeaves = []; house = new THREE.Group(); ceilings = new THREE.Group(); walls = new THREE.Group(); house.add(ceilings, walls); scene.add(house); colliders = [];
    const H = p.inaltime;
    // scări (blocuri pline) și goluri de placă; deschiderile din podea/tavan se taie ca bucăți dreptunghiulare
    const stairs = p.scari || [], voids = p.goluriPlaca || []; structBlockers = [];
    const rectsMinus = (r, holes) => { let ps = [{ x0: r.x0, z0: r.z0, x1: r.x1, z1: r.z1 }];
      for (const h of holes){ const nx = []; for (const q of ps){ const hx0 = Math.max(h.x0, q.x0), hx1 = Math.min(h.x1, q.x1), hz0 = Math.max(h.z0, q.z0), hz1 = Math.min(h.z1, q.z1);
        if (hx0 >= hx1 - 1e-6 || hz0 >= hz1 - 1e-6){ nx.push(q); continue; }
        if (hz0 > q.z0) nx.push({ x0: q.x0, z0: q.z0, x1: q.x1, z1: hz0 }); if (hz1 < q.z1) nx.push({ x0: q.x0, z0: hz1, x1: q.x1, z1: q.z1 });
        if (hx0 > q.x0) nx.push({ x0: q.x0, z0: hz0, x1: hx0, z1: hz1 }); if (hx1 < q.x1) nx.push({ x0: hx1, z0: hz0, x1: q.x1, z1: hz1 }); } ps = nx; }
      return ps.filter(q => q.x1 - q.x0 > .01 && q.z1 - q.z0 > .01); };
    p.camere.forEach(r => { const wet = r.tip === 'baie' || r.tip === 'bucatarie', sc = wet ? .9 : 1.6;
      // pardoseala aleasă (modul de așezare, mărimea plăcii, rostul) e desenată o dată pe toată camera; altfel textura generică
      const ff = r.fin && r.fin.floor, rw = r.x1 - r.x0, rd = r.z1 - r.z0, ft = ff ? floorTexture(ff, rw, rd, r.id) : null, tileFloor = ff ? ff.kind === 'tile' : wet;
      for (const q of rectsMinus(r, voids)){ const w = q.x1 - q.x0, d = q.z1 - q.z0; let t;
        if (ft){ t = ft.clone(); t.userData = { owned: true }; t.needsUpdate = true; t.repeat.set(w / rw, d / rd); t.offset.set((q.x0 - r.x0) / rw, (r.z1 - q.z1) / rd); }
        else { t = (wet ? tiles : parquet).clone(); t.userData = { owned: true }; t.needsUpdate = true; t.repeat.set(w / sc, d / sc); t.offset.set((q.x0 - r.x0) / sc, (r.z1 - q.z1) / sc); }
        const fmat = owned(new THREE.MeshStandardMaterial({ map: t, roughness: tileFloor ? .35 : .6, color: lin(r.podea || '#ffffff') }));
        if (ft) fmat.userData = { ...fmat.userData, kind: ff.kind === 'tile' ? 'tile' : 'parquet', pat: { ...ff, rect: [r.x0, r.z0, r.x1, r.z1] } };
        const f = new THREE.Mesh(new THREE.PlaneGeometry(w, d), fmat); f.rotation.x = -Math.PI / 2; f.position.set((q.x0 + q.x1) / 2, 0, (q.z0 + q.z1) / 2); f.receiveShadow = true; house.add(f); }
      // covorul camerei, centrat, cu latura lungă după cameră (sau rotit)
      if (r.fin && r.fin.rug){ const rg = r.fin.rug, rm = MAT('rug', rg.color || '#c8b79a'); const o = new THREE.Mesh(sharedGeo('rug' + k3(rg.w, .016, rg.d), () => rgeo(rg.w, .016, rg.d, .006)), rm); o.position.set((r.x0 + r.x1) / 2, .008, (r.z0 + r.z1) / 2); o.receiveShadow = true; house.add(o); }   // covor cu grosime și margini rotunjite, textil cu fir (nu o placă albă)
      // tavan: drept la H; fals coborât cu `drop`; cu scafă: banda de lângă pereți rămâne sus, panoul din mijloc coboară, cu bandă LED pe margine
      const cv = r.fin && r.fin.ceiling, cmat = r.tavan ? lookMat('ceil', r.tavan, 1) : ceilMat, ctop = cv && cv.type === 'drop' ? H - cv.drop : H;
      const ceilPlane = (rect, y) => { for (const q of rectsMinus(rect, stairs.map(a => a.rect))){ const w = q.x1 - q.x0, d = q.z1 - q.z0;
        const c = new THREE.Mesh(new THREE.PlaneGeometry(w, d), cmat); c.rotation.x = Math.PI / 2; c.position.set((q.x0 + q.x1) / 2, y, (q.z0 + q.z1) / 2); c.castShadow = c.receiveShadow = true; ceilings.add(c); } };   // tavanul ține soarele afară
      ceilPlane(r, ctop);
      const low = cv && cv.type !== 'flat' ? H - cv.drop : H;
      if (cv && cv.type === 'cove'){ const k = cv.cove, inner = { x0: r.x0 + k, z0: r.z0 + k, x1: r.x1 - k, z1: r.z1 - k };
        if (inner.x1 - inner.x0 > .3 && inner.z1 - inner.z0 > .3){ ceilPlane(inner, low); const iw = inner.x1 - inner.x0, id = inner.z1 - inner.z0, cx = (inner.x0 + inner.x1) / 2, cz = (inner.z0 + inner.z1) / 2;
          [[iw, .012, cx, inner.z0], [iw, .012, cx, inner.z1], [.012, id, inner.x0, cz], [.012, id, inner.x1, cz]].forEach(([a, b, x, z]) => { const m = box(a, cv.drop, b, cmat, x, H - cv.drop / 2, z, 0, ceilings); m.castShadow = false; });
          if (cv.led){ const lm = owned(new THREE.MeshStandardMaterial({ color: lin(cv.led), emissive: lin(cv.led), emissiveIntensity: 1.6 }));
            [[iw, .015, cx, inner.z0 - .02], [iw, .015, cx, inner.z1 + .02], [.015, id, inner.x0 - .02, cz], [.015, id, inner.x1 + .02, cz]].forEach(([a, b, x, z]) => { const m = box(a, .012, b, lm, x, low + .01, z, 0, ceilings); m.castShadow = false; });
            const glow = owned(new THREE.MeshStandardMaterial({ color: lin('#ffffff'), emissive: lin(cv.led), emissiveIntensity: .35, roughness: 1 }));
            [[r.x1 - r.x0, k, (r.x0 + r.x1) / 2, r.z0 + k / 2], [r.x1 - r.x0, k, (r.x0 + r.x1) / 2, r.z1 - k / 2], [k, id, r.x0 + k / 2, cz], [k, id, r.x1 - k / 2, cz]].forEach(([a, b, x, z]) => {
              const g2 = new THREE.Mesh(new THREE.PlaneGeometry(a, b), glow); g2.rotation.x = Math.PI / 2; g2.position.set(x, H - .002, z); ceilings.add(g2); }); } } }
      if (cv && cv.cornice){ const [cd, ch] = cv.cornice, e = .075 + cd / 2, y = (cv.type === 'drop' ? H - cv.drop : H) - ch / 2, cm2 = lookMat('cornice', '#f6f5f1', .7);
        [[r.x1 - r.x0, cd, (r.x0 + r.x1) / 2, r.z0 + e], [r.x1 - r.x0, cd, (r.x0 + r.x1) / 2, r.z1 - e], [cd, r.z1 - r.z0, r.x0 + e, (r.z0 + r.z1) / 2], [cd, r.z1 - r.z0, r.x1 - e, (r.z0 + r.z1) / 2]]
          .forEach(([a, b, x, z]) => { box(a, ch, b, cm2, x, y, z, 0, ceilings).castShadow = false; }); }
      if (cv && cv.spots > 0){ const n = cv.spots, rw2 = r.x1 - r.x0, rd2 = r.z1 - r.z0, cols = Math.max(1, Math.round(Math.sqrt(n * rw2 / rd2))), rows = Math.ceil(n / cols), sm = owned(new THREE.MeshStandardMaterial({ color: lin('#fff6e6'), emissive: lin('#ffd9a8'), emissiveIntensity: 1.4 }));
        for (let i = 0; i < n; i++){ const cx2 = r.x0 + rw2 * ((i % cols) + .5) / cols, cz2 = r.z0 + rd2 * (Math.floor(i / cols) + .5) / rows, s2 = new THREE.Mesh(new THREE.CylinderGeometry(.04, .04, .008, 20), sm); s2.position.set(cx2, (cv.type === 'cove' ? low : ctop) - .004, cz2); ceilings.add(s2); } }
      const w = r.x1 - r.x0, d = r.z1 - r.z0;
      roomLights.push({ id: r.id, x: (r.x0 + r.x1) / 2, y: H - .3, z: (r.z0 + r.z1) / 2, area: w * d }); void w; void d; });
    // cel mult MAX_PL lumini punctuale active (numărul rămâne fix, deci shaderele nu se recompilează): cele mai apropiate de jucător în tur
    for (let i = 0; i < Math.min(MAX_PL, roomLights.length); i++){ const pl = new THREE.PointLight(0xfff0dc, light ? light.interior : .35, 7, 2); interiorLights.push(pl); ceilings.add(pl); } lightRoom = null; assignLights(true);
    // corpurile de iluminat plasate: pendul (cablu + abajur + bec), aplică (bază pe perete + abajur), șină cu spoturi
    const bulb = owned(new THREE.MeshStandardMaterial({ color: lin('#fff6e6'), emissive: lin('#ffd9a8'), emissiveIntensity: 1.6 })), cord = MAT('paint', '#222222');
    (p.lumini || []).forEach(f => { const sm = MAT('paint', f.color || '#1c1c1c'), shade = owned(sm.clone()); shade.side = THREE.DoubleSide;
      const T = f.top || H;
      if (f.kind === 'pendant'){ const drop = Math.max(.1, T - f.y - .22), c = new THREE.Mesh(new THREE.CylinderGeometry(.004, .004, drop, 6), cord); c.position.set(f.x, T - drop / 2, f.z); ceilings.add(c);
        const sh = new THREE.Mesh(new THREE.ConeGeometry(.17, .22, 28, 1, true), shade); sh.position.set(f.x, f.y + .11, f.z); sh.castShadow = true; ceilings.add(sh);
        const b = new THREE.Mesh(new THREE.SphereGeometry(.045, 16, 10), bulb); b.position.set(f.x, f.y + .05, f.z); ceilings.add(b);
        const cp = new THREE.Mesh(new THREE.CylinderGeometry(.05, .05, .02, 16), sm); cp.position.set(f.x, T - .01, f.z); ceilings.add(cp); }
      else if (f.kind === 'sconce'){ const x = f.x + f.nx * .02, z = f.z + f.nz * .02, rot = Math.atan2(f.nx, f.nz);
        box(.1, .14, .02, sm, x, f.y, z, rot, house); const sh = new THREE.Mesh(new THREE.CylinderGeometry(.06, .08, .14, 20, 1, true), shade); sh.position.set(f.x + f.nx * .12, f.y + .03, f.z + f.nz * .12); house.add(sh);
        const b = new THREE.Mesh(new THREE.SphereGeometry(.03, 12, 8), bulb); b.position.set(f.x + f.nx * .12, f.y, f.z + f.nz * .12); house.add(b); }
      else if (f.kind === 'track'){ const L = f.len || 2, bar = box(f.alongX ? L : .035, .035, f.alongX ? .035 : L, sm, f.x, T - .02, f.z, 0, ceilings); bar.castShadow = false;
        const n = Math.max(2, Math.round(L / .5)); for (let k = 0; k < n; k++){ const o = ((k + .5) / n - .5) * L, x = f.alongX ? f.x + o : f.x, z = f.alongX ? f.z : f.z + o;
          const sp = new THREE.Mesh(new THREE.CylinderGeometry(.035, .035, .11, 16), sm); sp.position.set(x, T - .1, z); sp.rotation.x = (k % 2 ? .35 : -.35); ceilings.add(sp);
          const b = new THREE.Mesh(new THREE.CircleGeometry(.03, 16), bulb); b.rotation.x = Math.PI / 2; b.position.set(x, T - .156, z); ceilings.add(b); } } });
    const leafMat = MAT('paint', '#f4f2ed'), entranceMat = MAT('woodV', '#4a3324'), handleMat = MAT('chrome', '#d8d8d8');
    const pMinX = Math.min(...p.camere.map(r => r.x0)), pMaxX = Math.max(...p.camere.map(r => r.x1)), pMinZ = Math.min(...p.camere.map(r => r.z0)), pMaxZ = Math.max(...p.camere.map(r => r.z1));
    p.pereti.forEach(wl => {
      const [ax, az] = wl.a, [bx, bz] = wl.b, L = Math.hypot(bx - ax, bz - az); if (L < .01) return; const ux = (bx - ax) / L, uz = (bz - az) / L, rot = -Math.atan2(uz, ux), th = wl.ext ? .25 : .15, edge = wl.ext ? extMat : wallMat;
      // fețele mari ale cutiei: +z local = normala (-uz, ux) = fața A, -z = fața B (vezi wallFaceRooms în core/appearance.ts).
      // Peretele se taie la granițele camerelor, ca fiecare bucată să ia culoarea camerei din dreptul ei (sau accentul).
      const nx = -uz, nz = ux, off = th / 2 + .1, roomAt = (x, z) => p.camere.find(r => x > r.x0 && x < r.x1 && z > r.z0 && z < r.z1);
      const faceHex = r => r ? ((wl.accente && wl.accente[r.id]) || r.pereti || null) : null;
      const breaks = [...new Set(p.camere.flatMap(r => [[r.x0, r.z0], [r.x1, r.z0], [r.x1, r.z1], [r.x0, r.z1]])
        .filter(([cx, cz]) => Math.abs((cx - ax) * nx + (cz - az) * nz) < th + .3).map(([cx, cz]) => +((cx - ax) * ux + (cz - az) * uz).toFixed(4)))].filter(t => t > .01 && t < L - .01).sort((a, b) => a - b);
      const matAt = m => { const px = ax + ux * m, pz = az + uz * m; return [edge, edge, edge, edge, faceMat(faceHex(roomAt(px + nx * off, pz + nz * off)), wl.ext), faceMat(faceHex(roomAt(px - nx * off, pz - nz * off)), wl.ext)]; };
      const gs = [...wl.goluri].sort((a, b) => a.la - b.la); let q = 0;
      // plintă de 7 cm pe fețele care dau într-o cameră uscată (baia și bucătăria au faianță/gresie)
      const skirting = (s0, s1, m) => { for (const sg of [1, -1]){ const px = ax + ux * m + nx * sg * off, pz = az + uz * m + nz * sg * off, r = roomAt(px, pz); if (!r || r.tip === 'baie' || r.tip === 'bucatarie') continue;
        const k = th / 2 + .006; box(s1 - s0, .07, .012, skirtMat, ax + ux * m + nx * sg * k, .035, az + uz * m + nz * sg * k, rot, walls).castShadow = false; } };
      const piece = (s0, s1, y0, y1) => { if (s1 - s0 < .005 || y1 - y0 < .005) return; const m = (s0 + s1) / 2; box(s1 - s0, y1 - y0, th, matAt(m), ax + ux * m, (y0 + y1) / 2, az + uz * m, rot, walls).userData.wall = true; if (y0 === 0) skirting(s0, s1, m); };
      const seg = (s0, s1, y0, y1) => { let q0 = s0; for (const t of breaks){ if (t > q0 + .005 && t < s1 - .005){ piece(q0, t, y0, y1); q0 = t; } } piece(q0, s1, y0, y1); };
      gs.forEach(g => { seg(q, g.la, 0, H);
        if (g.tip === 'usa'){ const dh = Math.min(H - .05, g.h || 2.1), fm = g.culoare ? lookMat('frame', g.culoare, .5) : frameMat; seg(g.la, g.la + g.l, dh, H); const m = g.la + g.l / 2; [g.la, g.la + g.l].forEach(s => { box(.05, dh, th + .02, fm, ax + ux * s, dh / 2, az + uz * s, rot, walls).castShadow = false; }); box(g.l - .002, .05, th + .016, fm, ax + ux * m, dh, az + uz * m, rot, walls).castShadow = false;
          // foaia ușii pe balamale reale: capătul și partea din plan (core/doors.ts doorSwing), deschisă sau închisă (clic pe ușă)
          const sw = g.sw || { hinge: 'start', swing: ((mx0, mz0) => { const cmx = (pMinX + pMaxX) / 2, cmz = (pMinZ + pMaxZ) / 2; return ((mx0 - cmx) * nx + (mz0 - cmz) * nz) > 0 ? -1 : 1; })(ax + ux * m, az + uz * m) };
          const atStart = sw.hinge !== 'end', hs = atStart ? g.la + .025 : g.la + g.l - .025, hx = ax + ux * hs, hz = az + uz * hs, lw = Math.max(.3, g.l - .05), lh = dh - .012;
          const cdx = atStart ? ux : -ux, cdz = atStart ? uz : -uz, odx = nx * sw.swing, odz = nz * sw.swing, faceOff = sw.swing * (th / 2 - .03);
          const us = g.usa, lm = us && us.color ? (us.wood ? MAT('woodV', us.color) : MAT('paint', us.color)) : (g.intrare ? entranceMat : leafMat), hm = us && us.handle ? MAT('chrome', us.handle) : handleMat;
          // pivotul: pe linia balamalelor, la fața peretelui dinspre care se deschide; x local = de-a lungul foii
          const pivot = new THREE.Group(); pivot.position.set(hx + nx * faceOff, 0, hz + nz * faceOff); walls.add(pivot);
          const P = (bw, bh, bd, x, y, z, mat, shadow = true) => { const o = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, bd), mat); o.position.set(x, y, z); o.castShadow = shadow; o.receiveShadow = true; pivot.add(o); return o; };
          const lt = g.intrare ? .07 : .04; P(lw, lh, lt, lw / 2, lh / 2 + .006, 0, lm);
          if (us && us.style === 'glass') [1, -1].forEach(sg => P(lw * .55, lh * .55, .006, lw / 2, lh * .58, sg * (lt / 2 + .002), MAT('frost', '#e9ecec'), false));
          if (us && us.style === 'panel' || g.intrare) [1, -1].forEach(sg => [lh * .72, lh * .3].forEach((y, i) => P(lw * .66, lh * (i ? .36 : .3), .006, lw / 2, y, sg * (lt / 2 + .003), lm, false)));
          // mâner cu rozetă pe ambele fețe, balamale vizibile, vizor la ușa de intrare
          [1, -1].forEach(sg => { const z0 = sg * (lt / 2 + .006); P(.05, .05, .008, lw - .07, 1.02, z0, hm, false); const lev = P(.13, .018, .018, lw - .07 - .05, 1.02, sg * (lt / 2 + .03), hm, false); lev.rotation.x = 0;
            P(.018, .03, .02, lw - .07, 1.02, sg * (lt / 2 + .016), hm, false); P(.035, .09, .008, lw - .07, .9, z0, hm, false); });
          [.25, lh - .25].forEach(y => P(.012, .1, lt + .008, .006, y, 0, MAT('metal', '#8f8a80'), false));
          if (g.intrare) [1, -1].forEach(sg => { const o = new THREE.Mesh(new THREE.CylinderGeometry(.008, .008, .01, 12), MAT('chrome', '#bdbdbd')); o.rotation.x = Math.PI / 2; o.position.set(lw / 2, 1.55, sg * (lt / 2 + .004)); pivot.add(o); });
          const closed = -Math.atan2(cdz, cdx), openDir = Math.atan2(odz, odx), sweep = -(openDir - Math.atan2(cdz, cdx));
          // unghiul de deschidere: de la direcția închisă spre partea de deschidere, cu cel mai scurt sens
          const dAng = Math.atan2(Math.sin(sweep), Math.cos(sweep)), maxOpen = Math.sign(dAng) * Math.PI * .48;
          pivot.rotation.y = closed + (doorsOpen ? maxOpen : 0); doorLeaves.push({ pivot, closed, maxOpen, w: lw, open: doorsOpen ? 1 : 0, target: doorsOpen ? 1 : 0 });
          pivot.traverse(o => { if (o.isMesh) o.userData.door = doorLeaves.length - 1; });
          // pervazuri (ancadramente) pe ambele fețe ale golului
          [1, -1].forEach(sg => { const k = th / 2 + .008, ex = ax + nx * sg * k, ez = az + nz * sg * k;
            [g.la - .035, g.la + g.l + .035].forEach(sv => box(.07, dh, .016, fm, ex + ux * sv, dh / 2, ez + uz * sv, rot, walls).castShadow = false);
            box(g.l + .14, .07, .018, fm, ex + nx * sg * .001 + ux * m, dh + .035, ez + nz * sg * .001 + uz * m, rot, walls).castShadow = false; });
          if (g.intrare) box(g.l, .02, th + .02, MAT('metal', '#7b766e'), ax + ux * m, .01, az + uz * m, rot, walls); }
        else { const sl = g.sill != null ? g.sill : .9, top = Math.min(H - .05, sl + (g.h || 1.3)), wh = top - sl, cy = (sl + top) / 2, fm = g.culoare ? lookMat('frame', g.culoare, .5) : frameMat;
          seg(g.la, g.la + g.l, 0, sl); seg(g.la, g.la + g.l, top, H); const m = g.la + g.l / 2; box(g.l, wh, .02, glass, ax + ux * m, cy, az + uz * m, rot, walls).castShadow = false;
          // fereastră reală: toc PVC de 6 cm, canaturi cu profil, montant între canaturi, mânere, glaf interior și pervaz exterior
          { const pf = .06, pd = .07, at = (sv, y, w2, h2, d2, mat, off = 0) => { const o = box(w2, h2, d2, mat, ax + ux * sv + nx * off, y, az + uz * sv + nz * off, rot, walls); o.castShadow = false; return o; };
            const sIn = ((ax + ux * m - (pMinX + pMaxX) / 2) * nx + (az + uz * m - (pMinZ + pMaxZ) / 2) * nz) > 0 ? -1 : 1;   // partea dinspre casă
            at(g.la + pf / 2, cy, pf, wh, pd, fm); at(g.la + g.l - pf / 2, cy, pf, wh, pd, fm); at(m, sl + pf / 2, g.l - 2 * pf + .002, pf, pd - .004, fm); at(m, top - pf / 2, g.l - 2 * pf + .002, pf, pd - .004, fm);
            const n2 = g.l > 1.1 ? 2 : 1, iw = (g.l - 2 * pf) / n2;
            for (let k = 0; k < n2; k++){ const c0 = g.la + pf + iw * (k + .5), sw2 = iw - .01, sh2 = wh - 2 * pf - .01, sy = cy;
              [[c0 - sw2 / 2 + .03, sy, .06, sh2], [c0 + sw2 / 2 - .03, sy, .06, sh2]].forEach(([sv, y, w2, h2]) => at(sv, y, w2, h2, .06, fm, sIn * .012));
              [sy - sh2 / 2 + .03, sy + sh2 / 2 - .03].forEach(y => at(c0, y, sw2 - .118, .06, .056, fm, sIn * .012));
              const hx = k === 0 && n2 === 2 ? c0 + sw2 / 2 - .03 : c0 - sw2 / 2 + .03; at(hx, sy, .02, .14, .02, handleMat, sIn * (.06)); at(hx, sy + .05, .035, .035, .012, handleMat, sIn * .05); }
            if (n2 === 2) at(m, cy, .02, wh - 2 * pf, pd, fm);
            at(m, sl - .01, g.l + .08, .025, .2, MAT('paint', '#f4f2ee'), sIn * (th / 2 + .06));               // glaf
            if (wl.ext) at(m, sl - .04, g.l + .06, .015, .14, MAT('metal', '#d8d6d0'), -sIn * (th / 2 + .05)); }   // pervaz
          if (g.trat){ const tr = g.trat, sg = tr.inward, at = (s, d, y, w2, h2, dp, mat) => { const px = ax + ux * s + nx * sg * d, pz = az + uz * s + nz * sg * d; const o = box(w2, h2, dp, mat, px, y, pz, rot, walls); o.castShadow = false; return o; };
            // stor în golul ferestrei, pe partea camerei: rulou (coborât o treime), roman (pliuri orizontale) sau jaluzea venețiană (lamele)
            if (tr.blind){ const bm = lookMat('blind', tr.blind.color, .85), bw = g.l - .04, bh = wh * .38, d0 = th / 2 + .02;
              at(m, d0, top - .04, bw + .04, .07, .07, bm);
              if (tr.blind.kind === 'venetian'){ for (let yv = top - .1; yv > top - wh + .02; yv -= .025) at(m, d0, yv, bw, .004, .025, bm); }
              else { at(m, d0, top - .07 - bh / 2, bw, bh, .006, bm); if (tr.blind.kind === 'roman') for (let k = 1; k <= 3; k++) at(m, d0 + .006, top - .07 - bh * k / 3.2, bw, .012, .012, bm); } }
            // bara deasupra golului, depășind fereastra cu 20 cm pe fiecare parte; perdeaua trasă pe toată lățimea, draperiile strânse la capete
            if (tr.curtain || tr.sheer){ const rw = g.l + .4, d1 = th / 2 + .13, y0 = .01, y1 = tr.rodH, hh = y1 - y0 - .02;
              // bara: cilindru de 3 cm cu capete, puțin în fața perdelelor; perdeaua și draperiile atârnă sub ea (fără fețe comune)
              { const rm = lookMat('rod', '#2b2b2b', .4), px = ax + ux * m + nx * sg * (d1 + .01), pz = az + uz * m + nz * sg * (d1 + .01);
                const rod = new THREE.Mesh(sharedGeo('rod' + k3(rw + .06, .015, 0), () => new THREE.CylinderGeometry(.015, .015, rw + .06, 16)), rm); rod.position.set(px, y1 + .012, pz); rod.rotation.set(0, rot, Math.PI / 2); walls.add(rod);   // culcată prin rotația piesei, nu a geometriei (exportul trimite cilindrul parametric)
                [-1, 1].forEach(e => { const f = new THREE.Mesh(sharedGeo('finial', () => new THREE.SphereGeometry(.025, 16, 10)), rm); f.position.set(px + ux * e * (rw / 2 + .04), y1 + .012, pz + uz * e * (rw / 2 + .04)); walls.add(f); }); }
              const panel = (s0, w2, d, amp, period, mat, shadow) => { const px = ax + ux * s0 + nx * sg * d, pz = az + uz * s0 + nz * sg * d, o = new THREE.Mesh(pleatGeo(w2, hh, amp, period), mat);
                o.position.set(px, y0 + hh / 2, pz); o.rotation.y = rot; o.castShadow = shadow; o.receiveShadow = true; walls.add(o); return o; };
              // perdeaua: un singur panou plisat (profil sinusoidal), translucid; draperiile: câte un panou plisat strâns la fiecare capăt
              if (tr.sheer){ const sm = lookMat('sheer' + tr.sheer.color, tr.sheer.color, .9); Object.assign(sm, { transparent: true, opacity: .3, side: THREE.DoubleSide, depthWrite: false }); sm.userData.kind = 'sheer'; panel(m, rw, d1 - .02, .018, .1, sm, false); }
              if (tr.curtain){ const cm = MAT('cloth', tr.curtain.color), stack = .34; [-1, 1].forEach(e => panel(m + e * (rw / 2 - stack / 2), stack, d1 + .03, .03, .085, cm, true)); } } } }
        colliders.push({ ax, az, ux, uz, s0: q, s1: g.la }); if (g.tip === 'fereastra') colliders.push({ ax, az, ux, uz, s0: g.la, s1: g.la + g.l }); q = g.la + g.l; });
      seg(q, L, 0, H); colliders.push({ ax, az, ux, uz, s0: q, s1: L }); });
    // placări pe pereți (tapet, riflaj, tencuială, cărămidă, piatră, faianță): un strat subțire pe fața dinspre cameră, tăiat în jurul ușilor și ferestrelor
    p.camere.forEach(r => { if (!r.fin || !r.fin.walls || !r.fin.walls.length) return;
      r.fin.walls.forEach((wv, wi) => { const horiz = wv.side === 'N' || wv.side === 'S', edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[wv.side], inward = wv.side === 'N' || wv.side === 'W' ? 1 : -1;
        const lo = (horiz ? r.x0 : r.z0) + .075, hi = (horiz ? r.x1 : r.z1) - .075, bottom = Math.max(0, Math.min(H, wv.fromM || 0)), top = wv.kind === 'rail' ? Math.min(H, bottom + .045) : Math.min(H, wv.heightM || H); if (hi - lo < .05 || top - bottom < .005) return;
        // pereții pe latura asta: grosimea (fața camerei) și golurile, în coordonata de-a lungul laturii
        let th = .15, found = false; const holes = [];
        p.pereti.forEach(wl => { const [ax, az] = wl.a, [bx, bz] = wl.b, h2 = Math.abs(az - bz) < 1e-6, v2 = Math.abs(ax - bx) < 1e-6;
          if (horiz ? !(h2 && Math.abs(az - edge) < 1e-6) : !(v2 && Math.abs(ax - edge) < 1e-6)) return;
          const s0 = horiz ? ax : az, dir = Math.sign(horiz ? bx - ax : bz - az), a0 = Math.min(s0, horiz ? bx : bz), a1 = Math.max(s0, horiz ? bx : bz); if (a1 < lo || a0 > hi) return;
          th = wl.ext ? .25 : .15; found = true;
          wl.goluri.forEach(g => { const p0 = s0 + dir * g.la, p1 = s0 + dir * (g.la + g.l), sl = g.tip === 'usa' ? 0 : (g.sill != null ? g.sill : .9), tp = g.tip === 'usa' ? Math.min(H - .05, g.h || 2.1) : Math.min(H - .05, sl + (g.h || 1.3));
            holes.push({ a: Math.min(p0, p1), b: Math.max(p0, p1), y0: sl, y1: tp }); }); });
        if (!found) return;   // latură fără perete (deschisă spre altă cameră): nu desenăm o placare în aer
        const kind = wv.kind, depth = kind === 'stone' ? .022 : kind === 'brick' ? .014 : kind === 'slats' ? .021 : kind === 'rail' ? .02 : kind === 'panel' ? .012 : kind === 'tile' ? .01 : .003, off = edge + inward * (th / 2 + depth / 2 + .001);
        const plainKind = kind === 'slats' || kind === 'paint' || kind === 'rail' || kind === 'panel', tex = plainKind ? null : wallTexture(wv, `${r.id}:${wi}`), size = tex ? tex.userData.size : 1;
        const mkMat = (len, hgt, s0, y0) => { if (!tex) return null; const t = tex.clone(); t.userData = { ...tex.userData, owned: true }; t.needsUpdate = true; t.repeat.set(len / size, hgt / size); t.offset.set(s0 / size, y0 / size);
          const m = new THREE.MeshStandardMaterial({ map: t, roughness: kind === 'tile' ? .3 : kind === 'wallpaper' ? .7 : .9 }); m.userData = { kind: kind === 'tile' ? 'tile' : kind, col: wv.color, size: wv.sizeCm ? [wv.sizeCm[0] / 100, wv.sizeCm[1] / 100] : null, marble: !!wv.marble }; return m; };
        const slatBack = lookMat('slatback', '#2e2c2a', .95), slatMat = MAT('wood', wv.color);
        const place = (s0, s1, y0, y1) => { const len = s1 - s0, hgt = y1 - y0; if (len < .01 || hgt < .01) return; const mid = (s0 + s1) / 2, x = horiz ? mid : off, z = horiz ? off : mid, rot = horiz ? 0 : Math.PI / 2;
          if (kind === 'paint' || kind === 'rail'){ const pm = lookMat(kind === 'rail' ? 'rail' : 'bandpaint', wv.color, kind === 'rail' ? .5 : .92); pm.userData = { kind: 'paint' }; box(len, hgt, depth, pm, x, (y0 + y1) / 2, z, rot, walls).castShadow = false; return; }
          if (kind === 'panel'){ const pm = lookMat('panel', wv.color, .55); box(len, hgt, depth, pm, x, (y0 + y1) / 2, z, rot, walls).castShadow = false;
            // lambriu cu ramă: panouri în relief de ~60 cm, cu o bordură sus
            const n = Math.max(1, Math.round(len / .6)), pw = len / n;
            for (let k = 0; k < n; k++){ const sm = s0 + pw * (k + .5), px = horiz ? sm : off + inward * .006, pz = horiz ? off + inward * .006 : sm; box(pw - .1, Math.max(.05, hgt - .16), .006, pm, px, (y0 + y1) / 2, pz, rot, walls).castShadow = false; }
            box(len, .03, depth + .012, pm, x, y1 - .015, z, rot, walls).castShadow = false; return; }
          if (kind === 'slats'){ box(len, hgt, .006, slatBack, horiz ? mid : edge + inward * (th / 2 + .004), (y0 + y1) / 2, horiz ? edge + inward * (th / 2 + .004) : mid, rot, walls).castShadow = false;
            for (let sx = s0 + .02; sx < s1 - .01; sx += .05){ const px = horiz ? sx : off, pz = horiz ? off : sx; box(.027, hgt, depth, slatMat, px, (y0 + y1) / 2, pz, rot, walls).castShadow = false; } return; }
          box(len, hgt, depth, mkMat(len, hgt, s0, y0), x, (y0 + y1) / 2, z, rot, walls).castShadow = false; };
        // benzi verticale între marginile golurilor; în fiecare bandă, intervalele de înălțime rămase după scăderea golurilor
        const cuts = [...new Set([lo, hi, ...holes.flatMap(h => [h.a, h.b]).filter(v => v > lo && v < hi)])].sort((a, b) => a - b);
        for (let i = 0; i + 1 < cuts.length; i++){ const s0 = cuts[i], s1 = cuts[i + 1], m = (s0 + s1) / 2; let spans = [[bottom, top]];
          for (const h of holes) if (h.a < m && h.b > m) spans = spans.flatMap(([y0, y1]) => { const out = []; if (h.y0 > y0) out.push([y0, Math.min(y1, h.y0)]); if (h.y1 < y1) out.push([Math.max(y0, h.y1), y1]); return out.filter(([a2, b2]) => b2 - a2 > .005); });
          spans.forEach(([y0, y1]) => place(s0, s1, y0, y1)); } }); });
    // scări: trepte pline din lemn (treapta k are înălțimea (k+1)·riser), mână curentă pe o parte
    const stairMat = MAT('wood', '#a8794d'), railMat = M('#5a4430', { roughness: .6 });
    stairs.forEach(st => { const [dx, dz] = st.dir, ry = Math.atan2(dx, dz), sx = -dz, sz = dx;
      for (let k = 0; k < st.steps; k++){ const hgt = (k + 1) * st.riser, m = (k + .5) * st.going; box(st.w, hgt, st.going, stairMat, st.bottom[0] + dx * m, hgt / 2, st.bottom[1] + dz * m, ry, walls); }
      // mâna curentă: o bară înclinată, la ~0.9 m deasupra treptelor, pe partea laterală a scării
      const topY = st.steps * st.riser, rise = topY - st.riser, len = Math.hypot(st.l, rise), off = st.w / 2 - .025, cx = st.x + sx * off, cz = st.z + sz * off;
      const rail = box(.05, .05, len, railMat, cx, (st.riser + topY) / 2 + .9, cz, ry, walls); rail.rotateX(-Math.atan2(rise, st.l)); rail.castShadow = false;
      [[st.bottom, st.riser, 1], [st.top, topY, -1]].forEach(([e, y, inw]) => box(.05, .9, .05, railMat, e[0] + sx * off + dx * .025 * inw, y + .45, e[1] + sz * off + dz * .025 * inw, ry, walls).castShadow = false);
      structBlockers.push({ x0: st.rect.x0, z0: st.rect.z0, x1: st.rect.x1, z1: st.rect.z1 }); });
    // goluri de placă: fund întunecat, balustradă de 1 m pe laturile fără marginea de sosire
    const darkMat = M('#1c1a18', { roughness: 1 }), postMat = M('#6a5a48', { roughness: .5 });
    voids.forEach(v => { const w = v.x1 - v.x0, d = v.z1 - v.z0, bot = new THREE.Mesh(new THREE.PlaneGeometry(w, d), darkMat); bot.rotation.x = -Math.PI / 2; bot.position.set((v.x0 + v.x1) / 2, -SLAB, (v.z0 + v.z1) / 2); bot.receiveShadow = true; house.add(bot);
      const ax = Math.abs(v.dir[0]) > Math.abs(v.dir[1]), arrival = ax ? (v.dir[0] > 0 ? 'x1' : 'x0') : (v.dir[1] > 0 ? 'z1' : 'z0'), cxm = (v.x0 + v.x1) / 2, czm = (v.z0 + v.z1) / 2;
      const side = (edge, x0, z0, x1, z1) => { if (edge === arrival) return; const L = Math.hypot(x1 - x0, z1 - z0), n = Math.max(1, Math.round(L / 1));
        for (let i = 0; i <= n; i++) box(.05, 1, .05, postMat, x0 + (x1 - x0) * i / n, .5, z0 + (z1 - z0) * i / n, 0, walls).castShadow = false;
        box(.06, .05, L, postMat, (x0 + x1) / 2, 1, (z0 + z1) / 2, Math.atan2(x1 - x0, z1 - z0), walls); box(.03, .03, L, postMat, (x0 + x1) / 2, .5, (z0 + z1) / 2, Math.atan2(x1 - x0, z1 - z0), walls).castShadow = false; };
      side('z0', v.x0, v.z0, v.x1, v.z0); side('z1', v.x0, v.z1, v.x1, v.z1); side('x0', v.x0, v.z0, v.x0, v.z1); side('x1', v.x1, v.z0, v.x1, v.z1);
      structBlockers.push({ x0: v.x0, z0: v.z0, x1: v.x1, z1: v.z1 }); });
    const xs = p.camere.flatMap(r => [r.x0, r.x1]), zs = p.camere.flatMap(r => [r.z0, r.z1]);
    const minX = Math.min(...xs, 0), maxX = Math.max(...xs, 1), minZ = Math.min(...zs, 0), maxZ = Math.max(...zs, 1);
    W = maxX - minX; D = maxZ - minZ; C.set((minX + maxX) / 2, 0, (minZ + maxZ) / 2); placeSun(); sun.target.position.copy(C);
    // camera de umbră a soarelui pe măsura casei (mai multă rezoluție pe metru decât un pătrat fix de 24 m)
    { const rr = Math.hypot(W, D) / 2 + 1.5; Object.assign(sun.shadow.camera, { left: -rr, right: rr, top: rr, bottom: -rr, near: .5, far: 14 + rr + 6 }); sun.shadow.camera.updateProjectionMatrix(); }
    orbit.r = Math.max(W, D, 4) * 1.15; freeze(house); setMode(mode); dirty();
  }
  // decorul: ce ar pune un stilist ca locuința să pară locuită — planta în colțul liber, tabloul deasupra canapelei și a patului,
  // vaze pe comoda TV și pe masă, o ramă pe noptieră. Determinist (aceleași poziții la fiecare reconstruire), fără coliziuni.
  let decor = new THREE.Group(), lastItems = [], decorOn = true;
  function buildDecor(items){ scene.remove(decor); decor = new THREE.Group(); scene.add(decor); dirty(); if (!decorOn || !plan) return;
    const put = (key, x, y, z, rotY) => { const m = decoModel(key); if (!m) return; const o = m.clone(); o.position.set(x, y, z); o.rotation.y = rotY || 0; o.userData.deco = DECO[key][0]; decor.add(o); };
    const back = r => [-Math.sin(r), -Math.cos(r)], fps = items.filter(i => i.fp).map(i => i.fp);
    for (const it of items){ const v = it.variant; if (!v || !v.w) continue; const h = v.h / 100, d = v.d / 100, room = plan.camere.find(c => it.x > c.x0 && it.x < c.x1 && it.z > c.z0 && it.z < c.z1); if (!room) continue;
      const [bx, bz] = back(it.rotation), side = Math.abs(bx) > .5 ? (bx > 0 ? 'E' : 'W') : (bz > 0 ? 'S' : 'N');
      // pe un perete placat (riflaj, cărămidă, piatră, lambriu) tabloul stă în fața placării
      const clad = room.fin && room.fin.walls && room.fin.walls.some(w => w.side === side && ['slats', 'brick', 'stone', 'panel', 'tile'].includes(w.kind)) ? .06 : 0, k = .02 + clad;
      const wallAt = () => Math.abs(bx) > .5 ? [bx > 0 ? room.x1 - k : room.x0 + k, it.z] : [it.x, bz > 0 ? room.z1 - k : room.z0 + k];
      if (it.group === 'canapea' || it.group === 'pat'){ const [wx, wz] = wallAt(); put('art', wx, it.group === 'pat' ? 1.25 : 1.32, wz, it.rotation); }
      else if (it.group === 'comodaTv') put('vase', it.x - Math.cos(it.rotation) * (v.w / 100) * .36, h, it.z + Math.sin(it.rotation) * (v.w / 100) * .36, it.rotation);
      else if (it.group === 'masa') put('jug', it.x, h, it.z, 0);
      else if (it.group === 'noptiera') put('photo', it.x + Math.cos(it.rotation) * .12, h, it.z - Math.sin(it.rotation) * .12, it.rotation + .35);
      else if (it.group === 'biblioteca' && h > .8) put('succulent', it.x, h, it.z, 0);
      void d; }
    // planta: în colțul liber al livingului și al holului (departe de mobilier și de uși)
    for (const room of plan.camere){ if (room.tip !== 'living' && room.tip !== 'hol') continue; const ins = .32, cs = [[room.x0 + ins, room.z0 + ins], [room.x1 - ins, room.z0 + ins], [room.x0 + ins, room.z1 - ins], [room.x1 - ins, room.z1 - ins]];
      const doorPts = plan.pereti.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1; return w.goluri.filter(g => g.tip === 'usa').map(g => { const t = g.la + g.l / 2; return [w.a[0] + (w.b[0] - w.a[0]) / L * t, w.a[1] + (w.b[1] - w.a[1]) / L * t]; }); });
      const free = ([x, z]) => !doorPts.some(([px, pz]) => Math.hypot(px - x, pz - z) < 1.15) && !fps.some(f => x > f.x0 - .3 && x < f.x1 + .3 && z > f.z0 - .3 && z < f.z1 + .3) && !blockers.some(f => x > f.x0 - .3 && x < f.x1 + .3 && z > f.z0 - .3 && z < f.z1 + .3) && !structBlockers.some(f => x > f.x0 - .2 && x < f.x1 + .2 && z > f.z0 - .2 && z < f.z1 + .2);
      const c = cs.find(free); if (c) put(room.tip === 'hol' ? 'succulent' : 'plant', c[0], 0, c[1], 0); } freeze(decor); dirty(); }
  decoWaiters.add(() => { if (alive) buildDecor(lastItems); });
  // piesele statice nu-și mai recalculează matricea la fiecare cadru (grupurile părinte — ușile, pereții tăiați — rămân dinamice)
  function freeze(g){ g.updateMatrixWorld(true); g.traverse(o => { if (o.isMesh) o.matrixAutoUpdate = false; }); }
  // umbre de contact care urmează forma piesei: pentru fiecare piesă așezată pe pardoseală se desenează, într-un atlas comun,
  // proiecția pe pardoseală a fiecărei părți (picioarele, corpul canapelei, blatul rotund al mesei). Ce atinge pardoseala dă o
  // umbră scurtă și densă, ce stă mai sus (blatul, șezutul, un WC suspendat) una largă și slabă. Atlasul e estompat o dată pe
  // piesă și pus pe câte un dreptunghi sub ea; toate dreptunghiurile sunt un singur ochi de plasă (un apel de desen).
  let contact = null;
  const CONTACT_CELL = 96, CONTACT_MARGIN = .28, CONTACT_LOW = .12, CONTACT_TOP = .95;
  // înfășurătoarea convexă a unor puncte [x, z] (lanțul monoton)
  function hull(pts){ const p = pts.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]); if (p.length < 3) return p;
    const cr = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]), lo = [], up = [];
    for (const q of p){ while (lo.length >= 2 && cr(lo[lo.length - 2], lo[lo.length - 1], q) <= 0) lo.pop(); lo.push(q); }
    for (let i = p.length - 1; i >= 0; i--){ const q = p[i]; while (up.length >= 2 && cr(up[up.length - 2], up[up.length - 1], q) <= 0) up.pop(); up.push(q); }
    return lo.slice(0, -1).concat(up.slice(0, -1)); }
  // părțile unei piese, în coordonatele ei (rotația piesei rămâne pe umbră): conturul proiectat, cât de sus începe, dacă e rotundă
  function contactParts(m){ const inv = new THREE.Matrix4().copy(m.matrixWorld).invert(), v = new THREE.Vector3(), up = new THREE.Vector3(), bb = new THREE.Box3(), parts = [];
    m.traverse(o => { if (!o.isMesh || !o.geometry || o.visible === false) return; const g = o.geometry; if (!g.boundingBox) g.computeBoundingBox(); const b = g.boundingBox; if (!b || b.isEmpty()) return;
      const pts = []; let y0 = Infinity;
      for (let k = 0; k < 8; k++){ v.set(k & 1 ? b.max.x : b.min.x, k & 2 ? b.max.y : b.min.y, k & 4 ? b.max.z : b.min.z).applyMatrix4(o.matrixWorld).applyMatrix4(inv); bb.expandByPoint(v); pts.push([v.x, v.z]); y0 = Math.min(y0, v.y); }
      // un cilindru, o sferă sau un tor în picioare: umbra e un oval, nu pătratul cutiei lui
      up.set(0, 1, 0).transformDirection(o.matrixWorld).transformDirection(inv);
      const round = /Cylinder|Sphere|Lathe|Torus|Circle/.test(g.type || '') && Math.abs(up.y) > .9;
      parts.push({ hull: hull(pts), y0, round }); });
    return { bb, parts }; }
  function buildContact(){ if (contact){ scene.remove(contact); contact.geometry.dispose(); if (contact.material.uniforms.map.value) contact.material.uniforms.map.value.dispose(); contact.material.dispose(); contact = null; }
    if (typeof document === 'undefined') return;
    const list = [];
    for (const m of furniture.children){ const { bb, parts } = contactParts(m);
      if (bb.isEmpty() || bb.max.y - bb.min.y < .06) continue;   // covoare, plăci: fără umbră
      const w = bb.max.x - bb.min.x, d = bb.max.z - bb.min.z; if (bb.min.y > .45 || w < .05 || d < .05) continue;   // piese suspendate sus (oglinzi, rafturi): fără
      list.push({ m, bb, parts: parts.filter(q => q.y0 < CONTACT_TOP), w, d }); }
    if (!list.length) return;
    const cols = Math.ceil(Math.sqrt(list.length)), N = cols * CONTACT_CELL, atlas = document.createElement('canvas'); atlas.width = atlas.height = N;
    const A = atlas.getContext('2d'), tmp = document.createElement('canvas'); tmp.width = tmp.height = CONTACT_CELL; const T = tmp.getContext('2d');
    const pos = [], uv = [], idx = [], v = new THREE.Vector3();
    list.forEach(({ m, bb, parts, w, d }, i) => { const mar = CONTACT_MARGIN, S = Math.max(w, d) + 2 * mar, s = CONTACT_CELL / S, ox = bb.min.x - mar, oz = bb.min.z - mar;
      const cx = (i % cols) * CONTACT_CELL, cy = Math.floor(i / cols) * CONTACT_CELL;
      const shape = q => { T.beginPath(); if (q.round){ let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity; for (const [x, z] of q.hull){ x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); }
          T.ellipse(((x0 + x1) / 2 - ox) * s, ((z0 + z1) / 2 - oz) * s, Math.max(.5, (x1 - x0) / 2 * s), Math.max(.5, (z1 - z0) / 2 * s), 0, 0, Math.PI * 2); }
        else q.hull.forEach(([x, z], k) => k ? T.lineTo((x - ox) * s, (z - oz) * s) : T.moveTo((x - ox) * s, (z - oz) * s));
        T.closePath(); T.fill(); };
      // două straturi: ce atinge pardoseala (estompare mică) și ce stă deasupra ei (estompare mare, tot mai slabă cu înălțimea)
      for (const low of [true, false]){ T.clearRect(0, 0, CONTACT_CELL, CONTACT_CELL); let any = false, hs = 0, hn = 0;
        for (const q of parts){ if ((q.y0 < CONTACT_LOW) !== low) continue; any = true;
          T.fillStyle = low ? 'rgba(0,0,0,.85)' : `rgba(0,0,0,${(.42 * Math.pow(1 - q.y0 / CONTACT_TOP, 1.5)).toFixed(3)})`; shape(q); if (!low){ hs += q.y0; hn++; } }
        if (!any) continue;
        const blur = (low ? .035 : .07 + .14 * (hs / hn)) * s;
        A.save(); A.beginPath(); A.rect(cx, cy, CONTACT_CELL, CONTACT_CELL); A.clip(); A.filter = `blur(${blur.toFixed(2)}px)`; A.drawImage(tmp, cx, cy); A.restore(); }
      // dreptunghiul de sub piesă acoperă doar partea folosită a celulei (S × S, cu piesa în colțul de sus)
      const x0 = ox, x1 = ox + w + 2 * mar, z0 = oz, z1 = oz + d + 2 * mar, base = pos.length / 3;
      [[x0, z0], [x1, z0], [x1, z1], [x0, z1]].forEach(([x, z]) => { v.set(x, 0, z).applyMatrix4(m.matrixWorld); pos.push(v.x, .004, v.z); uv.push((cx + (x - ox) * s) / N, 1 - (cy + (z - oz) * s) / N); });
      idx.push(base, base + 2, base + 1, base, base + 3, base + 2); });
    const tex = new THREE.CanvasTexture(atlas); tex.generateMipmaps = false; tex.minFilter = THREE.LinearFilter; tex.magFilter = THREE.LinearFilter;
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2)); g.setIndex(idx); g.computeBoundingSphere();
    const mat = new THREE.ShaderMaterial({ uniforms: { map: { value: tex }, strength: { value: .55 } }, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2,
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: 'uniform sampler2D map; uniform float strength; varying vec2 vUv; void main(){ gl_FragColor = vec4(0.0, 0.0, 0.0, texture2D(map, vUv).a * strength); }' });
    contact = new THREE.Mesh(g, mat); contact.name = 'contact'; contact.userData = { noExport: true }; contact.matrixAutoUpdate = false; contact.renderOrder = 1; scene.add(contact); }
  function buildFurniture(items){ disposeGroup(furniture); scene.remove(furniture); furniture = new THREE.Group(); scene.add(furniture); pickables = []; blockers = [];
    for (const it of items){ if (!it.variant || !it.variant.w) continue; const m = model(it.variant); m.position.set(it.x, 0, it.z); m.rotation.y = it.rotation; m.userData = { pid: it.id }; m.traverse(o => { o.userData.item = m; }); furniture.add(m); pickables.push(m);
      if (it.group !== 'scaunBirou' && it.fp) blockers.push(it.fp); }
    lastItems = items; freeze(furniture); buildContact(); buildDecor(items); dirty(); }

  // soarele stă pe direcția dată (azimut, înălțime), la ~14 m de centrul casei; implicit ca în prototip
  function placeSun(){ const dir = light && light.dir ? light.dir : null; if (dir) sun.position.set(C.x + dir[0] * 14, dir[1] * 14, C.z + dir[2] * 14); else sun.position.set(C.x + 6, 11, C.z - 8); }
  // momentul zilei, dedus din parametri (core/lighting.ts nu-l trimite): cerul, terenul și lumina proprie a tavanului
  const timeOf = p => p.sun < .3 ? 'night' : p.sun < 1 ? 'evening' : 'day';
  function setLighting(p){ light = p; const t = timeOf(p); paintSky(t); applyBackdrop();
    hemi.intensity = p.hemi; sun.color.set(p.sunColor); sun.intensity = p.sun; R.toneMappingExposure = p.exposure;
    ceilMats.forEach(m => { m.emissiveIntensity = ceilFill[t]; });
    interiorLights.forEach(l => { l.intensity = p.interior; }); placeSun(); dirty(); }
  // fundalul: în machetă culoarea neutră din lumină; în tur și în imaginile fixe cerul și terenul de afară
  function applyBackdrop(){ const walk = mode === 'walk', bg = new THREE.Color(light ? light.background : '#d9d3c8'), hz = lin(SKY[skyKey][0]);
    sky.visible = walk; scene.background = walk ? new THREE.Color(SKY[skyKey][0]) : bg; scene.fog.color.copy(walk ? hz : bg); scene.fog.near = walk ? 35 : 25; scene.fog.far = walk ? 105 : 60;
    ground.material.color.copy(walk ? lin(SKY[skyKey][3]) : groundHouse); }
  // captură PNG: randează cadrul curent și îl citește imediat (fără preserveDrawingBuffer)
  function capture(){ draw(); return canvas.toDataURL('image/png'); }
  // Cadrul unei camere (imaginile de prezentare și intrarea în tur, aceeași regulă — SHOT): ochiul la 1,5 m, FOV vertical 52°,
  // aparatul orizontal (verticale drepte) cu obiectivul deplasat în jos, privirea spre piesele principale. Candidați: colțurile
  // și pereții camerei, departe de uși (≥ 1 m de mijlocul golului) și de piese; câștigă poziția din care intră cele mai multe
  // piese principale (ponderate ca în tools/blender/casa3d_render.py:room_camera). Apoi testul de acoperire: raze prin tot
  // cadrul; o poziție în care un canat de ușă, o draperie, un perete sau o piesă înaltă aflată la sub 1 m ocupă peste 15 %
  // din imagine e respinsă.
  let lastShot = null;
  const SHOT_HERO = ['pat', 'canapea', 'bucatarie'];
  const SHOT_KEY = { bucatarie: 3, pat: 3, canapea: 3, wc: 2.5, lavoar: 2.5, dus: 2, masa: 1.5, dulap: 1.5, frigider: 1, birou: 1.2, comodaTv: 1, pantofar: 1, oglinda: .8, biblioteca: .6, masuta: .8, noptiera: .4 };
  const doorSpots = () => plan.pereti.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1; return w.goluri.filter(g => g.tip === 'usa').map(g => { const t = g.la + g.l / 2; return [w.a[0] + (w.b[0] - w.a[0]) / L * t, w.a[1] + (w.b[1] - w.a[1]) / L * t, g.l, !!g.intrare]; }); });
  const shotCam = new THREE.PerspectiveCamera(SHOT.fov, 1.5, .05, 50), shotRay = new THREE.Raycaster();
  function lensFor(c, asp){ const Wp = 1000, Hp = Math.max(1, Math.round(Wp / asp)); c.setViewOffset(Wp, Hp, 0, Math.round(SHOT.shift * Hp), Wp, Hp); }
  // obstacolele pentru raze: piesele vizibile (pereți, uși, perdele, mobilier, decor), cu sfera lor în lume (pentru o preselecție rapidă)
  function obstacles(){ const out = [], sp = new THREE.Sphere(); const vis = o => { for (let a = o; a; a = a.parent) if (!a.visible) return false; return true; };
    for (const root of [walls, furniture, decor]) root.traverse(o => { if (!o.isMesh || !vis(o)) return; const g = o.geometry; if (!g.boundingSphere) g.computeBoundingSphere(); sp.copy(g.boundingSphere).applyMatrix4(o.matrixWorld); out.push({ o, c: sp.center.clone(), r: sp.radius, lim: o.userData.wall ? SHOT.near : SHOT.nearObj }); });
    return out; }
  // ce vede obiectivul (grilă 12 × 8 de raze prin tot cadrul): cover = fracțiunea acoperită de ceva aflat la sub SHOT.near
  // metri (perete) sau SHOT.nearObj (piesă, ușă, draperie); fg = fracțiunea jumătății de jos a cadrului ocupată de o piesă la sub
  // 2,2 m (o masă sau o măsuță în prim-plan); depth = adâncimea medie a privirii pe orizontală (metri, până la 6)
  const FG_DIST = 2.2;   // wallNear: pragul pentru pereți (un coridor îngust are pereții aproape, pe laturi: acolo e mai mic)
  function probe(obs, x, z, tx, tz, asp, skip = null, fov = SHOT.fov, wallNear = SHOT.near){ shotCam.fov = fov; shotCam.aspect = asp; shotCam.position.set(x, SHOT.eye, z); shotCam.lookAt(tx, SHOT.eye, tz); lensFor(shotCam, asp); shotCam.updateMatrixWorld(true);
    const P = shotCam.position, near = obs.filter(q => q.c.distanceTo(P) - q.r < Math.max(q.lim, FG_DIST)).map(q => q.o);
    // un perete contează sub 1 m; o piesă, o ușă, o draperie sau o fereastră sub 1,5 m (prim-plan care domină imaginea)
    let hit = 0, fg = 0, n = 0, low = 0; const v = new THREE.Vector2();
    if (near.length){ shotRay.near = 0; shotRay.far = Math.max(SHOT.nearObj, FG_DIST);
      for (let j = 0; j < 8; j++) for (let i = 0; i < 12; i++){ v.set((i + .5) / 12 * 2 - 1, (j + .5) / 8 * 2 - 1); shotRay.setFromCamera(v, shotCam); n++; if (j < 4) low++;
        const h = shotRay.intersectObjects(near, false)[0]; if (!h) continue; const u = h.object.userData;
        if (h.distance < (u.wall ? wallNear : SHOT.nearObj)) hit++;
        if (j < 4 && u.item && h.distance < FG_DIST && !(skip && h.point.x > skip.x0 - .05 && h.point.x < skip.x1 + .05 && h.point.z > skip.z0 - .05 && h.point.z < skip.z1 + .05)) fg++; } }   // doar mobilierul (nu decorul), fără piesa principală (amprenta ei)
    else n = 96;
    // adâncimea: 9 raze pe orizontală, la înălțimea ochilor
    const all = obs.map(q => q.o); shotRay.near = 0; shotRay.far = 6; let dep = 0;
    for (let i = 0; i < 9; i++){ v.set((i + .5) / 9 * 2 - 1, -2 * SHOT.shift); shotRay.setFromCamera(v, shotCam); const h = shotRay.intersectObjects(all, false)[0]; dep += h ? h.distance : 6; }
    return { cover: hit / n, fg: low ? fg / low : 0, depth: dep / 9 }; }
  // fov: deschiderea verticală a cadrului care se va vedea (imaginile fixe: SHOT.fov; turul: walkFov(), puțin mai larg)
  function roomShot(r, asp = cam.aspect || 1.5, fov = SHOT.fov){ const cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, EYE_Y = SHOT.eye, FOV = fov;
    const half = Math.atan(Math.tan(FOV * Math.PI / 360) * asp), inRoom = (x, z, e = .05) => x > r.x0 - e && x < r.x1 + e && z > r.z0 - e && z < r.z1 + e;
    const its = (lastItems || []).filter(it => it.fp && inRoom((it.fp.x0 + it.fp.x1) / 2, (it.fp.z0 + it.fp.z1) / 2)), key = its.filter(it => SHOT_KEY[it.group] >= 1).map(it => ({ w: SHOT_KEY[it.group], f: it.fp, cx: (it.fp.x0 + it.fp.x1) / 2, cz: (it.fp.z0 + it.fp.z1) / 2, fx: Math.sin(it.rotation || 0), fz: Math.cos(it.rotation || 0), bed: it.group === 'pat', hero: SHOT_HERO.includes(it.group), top: Math.min(1.5, it.variant && it.variant.h ? it.variant.h / 100 : .8, it.group === 'wc' ? .42 : 9) }));
    const botTan = Math.tan(FOV * Math.PI / 360) * (1 + 2 * SHOT.shift);   // marginea de jos a cadrului (obiectivul deplasat în jos)
    const doors = doorSpots().filter(d => inRoom(d[0], d[1], .3));
    // ferestrele camerei (segmentele golurilor): obiectivul nu stă în perdele sau lipit de geam
    const wins = plan.pereti.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
      return w.goluri.filter(g => g.tip !== 'usa').map(g => [w.a[0] + ux * g.la, w.a[1] + uz * g.la, w.a[0] + ux * (g.la + g.l), w.a[1] + uz * (g.la + g.l)]); });
    const segD = (x, z, s) => { const dx = s[2] - s[0], dz = s[3] - s[1], t = Math.max(0, Math.min(1, ((x - s[0]) * dx + (z - s[1]) * dz) / (dx * dx + dz * dz || 1e-9))); return Math.hypot(x - s[0] - t * dx, z - s[1] - t * dz); };
    const cands = [], rw = r.x1 - r.x0, rd = r.z1 - r.z0;
    for (const k of [.35, .6]){ const xs = [r.x0 + k, cx, r.x1 - k], zs = [r.z0 + k, cz, r.z1 - k]; xs.forEach(x => zs.forEach(z => { if (x !== cx || z !== cz) cands.push([x, z]); })); }
    [.25, .75].forEach(f => { const x = r.x0 + rw * f, z = r.z0 + rd * f; cands.push([x, r.z0 + .35], [x, r.z1 - .35], [r.x0 + .35, z], [r.x1 - .35, z]); });
    // și de-a lungul pereților, din 0,5 în 0,5 m: fotograful se mută puțin ca să scoată o ușă sau o masă din cadru
    for (const k of [.35, .6]){ for (let x = r.x0 + .6; x < r.x1 - .55; x += .5) cands.push([x, r.z0 + k], [x, r.z1 - k]); for (let z = r.z0 + .6; z < r.z1 - .55; z += .5) cands.push([r.x0 + k, z], [r.x1 - k, z]); }
    const inside = cands.filter(([x, z]) => x > r.x0 + .2 && x < r.x1 - .2 && z > r.z0 + .2 && z < r.z1 - .2);
    const tooClose = ([x, z]) => its.some(it => x > it.fp.x0 - .3 && x < it.fp.x1 + .3 && z > it.fp.z0 - .3 && z < it.fp.z1 + .3) || doors.some(d => Math.hypot(d[0] - x, d[1] - z) < Math.max(1.0, d[2] * .5 + .6)) || structBlockers.some(b => x > b.x0 - .3 && x < b.x1 + .3 && z > b.z0 - .3 && z < b.z1 + .3) || wins.some(s => segD(x, z, s) < .5);
    let pool = inside.filter(c => !tooClose(c)); if (!pool.length) pool = inside.filter(c => !its.some(it => c[0] > it.fp.x0 && c[0] < it.fp.x1 && c[1] > it.fp.z0 && c[1] < it.fp.z1)); if (!pool.length) pool = [[cx, cz]];
    // fotograful stă și în prag: holul de la intrare (privind spre casă, cu ușa de la intrare în spate) și o încăpere mică
    // (baia) din ușa ei; acolo canatul și golul rămân în spatele obiectivului. În tur, cu ușile deschise, orice cameră se
    // poate privi și din pragul ușii ei (intri pe ușă): canatul deschis rămâne lângă obiectiv, nu în mijlocul cadrului
    const small = rw * rd < 6, entering = doorLeaves.some(q => q.target > .01), sill = []; for (const d of doors){ if (!d[3] && !small && !entering) continue; const dir = [[1, 0], [-1, 0], [0, 1], [0, -1]].find(([ux, uz]) => inRoom(d[0] + ux * .5, d[1] + uz * .5, -.05)); if (!dir) continue;
      const k = d[3] ? .7 : small ? .3 : .5, c = [d[0] + dir[0] * k, d[1] + dir[1] * k]; if (!its.some(it => c[0] > it.fp.x0 - .2 && c[0] < it.fp.x1 + .2 && c[1] > it.fp.z0 - .2 && c[1] < it.fp.z1 + .2)){ pool.push(c); sill.push(c); } }
    // zona din cadru pentru un punct: [înainte, unghi lateral] față de direcția privirii
    const frame = (c, lx, lz, px, pz) => { const n = Math.hypot(lx, lz) || 1, fx = lx / n, fz = lz / n, dx = px - c[0], dz = pz - c[1], fwd = dx * fx + dz * fz; return { fwd, side: Math.abs(Math.atan2(dx * fz - dz * fx, fwd)) }; };
    // regulile fotografiei de interior (aceleași în tools/blender/casa3d_render.py:room_camera): piesa principală (patul, canapeaua,
    // bucătăria) întreagă și spre mijlocul cadrului, privită din fața ei; fereastra într-o parte, nu drept în contra luminii; cablul
    // pendulului nu taie treimea din mijloc a cadrului și nici fereastra; ușa de la intrare (deschisă în tur) nu stă în cadru
    const hero = key.filter(m => m.hero).sort((a, b) => b.w * (b.f.x1 - b.f.x0) * (b.f.z1 - b.f.z0) - a.w * (a.f.x1 - a.f.x0) * (a.f.z1 - a.f.z0))[0] || null;
    const winIn = wins.filter(s => inRoom((s[0] + s[2]) / 2, (s[1] + s[3]) / 2, .35)).map(s => { const mx = (s[0] + s[2]) / 2, mz = (s[1] + s[3]) / 2, L = Math.hypot(s[2] - s[0], s[3] - s[1]) || 1; let nx = -(s[3] - s[1]) / L, nz = (s[2] - s[0]) / L;
      if (!inRoom(mx + nx * .4, mz + nz * .4, 0)){ nx = -nx; nz = -nz; } return { mx, mz, nx, nz, w: L }; });
    const pend = (plan.lumini || []).filter(f => f.kind === 'pendant' && inRoom(f.x, f.z, 0));
    // ușile deschise (în tur): canatul din cameră (balama, mijloc, capăt) și golurile ușilor ei nu stau în cadru; holul păstrează
    // golul din capăt (privirea prin ușa deschisă spre camera următoare îi dă adâncime)
    const openNow = doorLeaves.some(d => d.target > .01), isHall = doors.some(d => d[3]) || /^hol/.test(r.id);
    const leaves = openNow ? doorLeaves.filter(d => d.target > .01).map(d => { const a = d.closed + d.maxOpen * d.target, hx = d.pivot.position.x, hz = d.pivot.position.z, ex = Math.cos(a) * d.w, ez = -Math.sin(a) * d.w;
      return [[hx, hz], [hx + ex / 2, hz + ez / 2], [hx + ex, hz + ez]]; }).filter(L => inRoom(L[1][0], L[1][1], 0)) : [];
    const tanH = Math.tan(half), U = (c, lx, lz, px, pz) => { const f = frame(c, lx, lz, px, pz), n = Math.hypot(lx, lz) || 1, dx = px - c[0], dz = pz - c[1]; return f.fwd > .05 ? { fwd: f.fwd, u: (dx * lz - dz * lx) / n / f.fwd / tanH } : { fwd: f.fwd, u: 9 }; };
    function composition(c, lx, lz){ let s = 0; const n = Math.hypot(lx, lz) || 1, wu = [];
      if (hero){ const q = U(c, lx, lz, hero.cx, hero.cz); s += q.fwd > .3 && Math.abs(q.u) < .85 ? 3 * Math.max(0, 1 - Math.abs(q.u)) : -4; }
      for (const w of winIn){ const q = U(c, lx, lz, w.mx, w.mz); if (q.fwd <= .3 || Math.abs(q.u) > 1.2) continue; const facing = -(lx * w.nx + lz * w.nz) / n;
        s -= 2.5 * Math.max(0, facing - .35) / .65 * Math.max(0, 1 - Math.abs(q.u) / .75); const hw = w.w / 2 / q.fwd / tanH; wu.push([q.u - hw, q.u + hw]); }
      for (const f of pend){ const q = U(c, lx, lz, f.x, f.z); if (q.fwd <= .3 || Math.abs(q.u) > 1) continue; s -= 2.2 * Math.max(0, 1 - Math.abs(q.u) / .45); if (wu.some(([a, b]) => q.u > a - .03 && q.u < b + .03)) s -= 1.5; }
      // ușile: în tur canatul e deschis; un gol în treimea din mijloc a cadrului, la mai puțin de 5 m, strică imaginea
      for (const d of doors){ const q = U(c, lx, lz, d[0], d[1]); if (q.fwd > .2 && Math.abs(q.u) < 1.05){ if (d[3]) s -= 6; else if (q.fwd < 5 && !isHall) s -= 2.5 * Math.max(0, 1 - Math.abs(q.u) / .6); } }
      if (openNow){ for (const L of leaves){ let worst = 0; for (const p of L){ const q = U(c, lx, lz, p[0], p[1]); if (q.fwd > .15 && Math.abs(q.u) < 1.08) worst = Math.max(worst, .25 + .75 * Math.pow(Math.max(0, 1 - Math.abs(q.u)), .8)); } s -= 9 * worst; }
        if (!isHall) for (const d of doors){ if (d[3]) continue; const q = U(c, lx, lz, d[0], d[1]); if (q.fwd > .2 && Math.abs(q.u) < 1.02) s -= 4 * (1 - Math.abs(q.u) * .6); } }
      return s; }
    const aimsOf = c => { const out = [[cx + (cx - c[0]) * .35, cz + (cz - c[1]) * .35]]; if (key.length){ const ws = key.reduce((a, m) => a + m.w, 0); out.push([key.reduce((a, m) => a + m.w * m.cx, 0) / ws, key.reduce((a, m) => a + m.w * m.cz, 0) / ws]); key.forEach(m => out.push([m.cx, m.cz])); }
      // ținta trasă spre peretele din spatele piesei principale (tăblia patului): vedere în două puncte de fugă spre ea
      if (hero){ const dep = hero.bed ? .45 * (Math.abs(hero.fz) > .5 ? hero.f.z1 - hero.f.z0 : hero.f.x1 - hero.f.x0) : .25 * Math.min(hero.f.x1 - hero.f.x0, hero.f.z1 - hero.f.z0); out.push([hero.cx - hero.fx * dep, hero.cz - hero.fz * dep]); }
      // holul de la intrare: privirea dinspre ușa de la intrare spre casă
      // holul: și în lungul lui, spre capătul opus (perspectiva coridorului)
      if (isHall){ if (rw >= rd) out.push([c[0] < cx ? r.x1 + 1 : r.x0 - 1, cz]); else out.push([cx, c[1] < cz ? r.z1 + 1 : r.z0 - 1]); }
      for (const d of doors) if (d[3]){ const vx = c[0] - d[0], vz = c[1] - d[1], vn = Math.hypot(vx, vz); if (vn > .05) out.push([c[0] + vx / vn * 3, c[1] + vz / vn * 3]); }
      return out; };
    const ranked = [], seenShot = new Set();
    // fiecare țintă și ușor rotită (±12°, ±24°): încadrarea se ajustează ca să scoată o ușă sau o fereastră din cadru
    const turn = (c, t, a) => { const dx = t[0] - c[0], dz = t[1] - c[1], co = Math.cos(a), si = Math.sin(a); return [c[0] + dx * co - dz * si, c[1] + dx * si + dz * co]; };
    for (const c of pool) for (let t of aimsOf(c).flatMap(t0 => [0, .21, -.21, .42, -.42].map(a => a ? turn(c, t0, a) : t0))){ let lx = t[0] - c[0], lz = t[1] - c[1], d0 = Math.hypot(lx, lz); if (d0 < .05) continue;
      if (d0 < 1.1){ t = [c[0] + lx / d0 * 2, c[1] + lz / d0 * 2]; lx = t[0] - c[0]; lz = t[1] - c[1]; d0 = 2; }   // ținta prea aproape: aceeași direcție, mai departe
      let sc = Math.min(d0, 5) * .5;
      for (const m of key){ const pts = [[m.cx, m.cz], [m.f.x0, m.f.z0], [m.f.x1, m.f.z0], [m.f.x1, m.f.z1], [m.f.x0, m.f.z1]]; let seen = 0, near = false;
        // un colț văzut: în deschiderea orizontală și cu partea de sus a piesei deasupra marginii de jos a cadrului (nu tăiată)
        for (const q of pts){ const f = frame(c, lx, lz, q[0], q[1]); if (f.fwd > Math.max(.45, (EYE_Y - m.top) / botTan) && f.side < half * .92) seen++; if (f.fwd < 1.2 && f.fwd > -.2 && f.side < half) near = true; }
        sc += (isHall ? .3 : 1) * (m.w * seen / 5 - (near ? m.w * .8 : 0)) - (m.hero && seen < 5 ? 2.5 : 0);   // holul: perspectiva, nu pantofarul   // piesa principală: întreagă, nu tăiată
        // piesele mari se fotografiază din fața lor (patul dinspre picioare, canapeaua și bucătăria din față), nu din spate sau de la cap
        if (m.w >= 2){ const vx = c[0] - m.cx, vz = c[1] - m.cz, vn = Math.hypot(vx, vz) || 1, cf = (vx * m.fx + vz * m.fz) / vn;
          // patul: dinspre picioare/ușă, pe diagonală spre peretele tăbliei (18°–65° față de axa lui); de lângă tăblie sau din lateral, nu
          if (m.bed) sc += m.w * (1.2 * cf + (cf > .42 && cf < .95 ? 1.2 : 0)) - 12 * Math.max(0, .35 - cf); else sc += m.w * 1.0 * cf; } }
      sc += composition(c, lx, lz);
      // un gol de ușă în cadru, aproape: pierde (ușa închisă e doar un perete alb, dar golul deschis taie imaginea) (holul: ușile de pe laturi fac parte din perspectiva lui)
      if (!isHall) for (const d of doors){ const f = frame(c, lx, lz, d[0], d[1]); if (f.fwd > .3 && f.fwd < 4 && f.side < half) sc -= 1.0 * (1 - f.side / half) + (f.side < half * .8 && f.fwd < 2 ? 2 : 0); }
      // peretele din fața obiectivului: privirea să aibă adâncime (nu un perete lipit)
      { const ux = lx / d0, uz = lz / d0, tx = ux > 1e-6 ? (r.x1 - c[0]) / ux : ux < -1e-6 ? (r.x0 - c[0]) / ux : 1e9, tz = uz > 1e-6 ? (r.z1 - c[1]) / uz : uz < -1e-6 ? (r.z0 - c[1]) / uz : 1e9; sc += Math.min(Math.min(tx, tz), 5) * (isHall ? .9 : .15); }
      const id = `${c[0].toFixed(2)},${c[1].toFixed(2)},${t[0].toFixed(2)},${t[1].toFixed(2)}`; if (seenShot.has(id)) continue; seenShot.add(id);   // același cadru o singură dată
      ranked.push({ sc, c, t }); }
    ranked.sort((a, b) => b.sc - a.sc);
    if (!ranked.length){ const c = pool[0]; ranked.push({ sc: 0, c, t: [r.x0 + r.x1 - c[0] + 1e-3, r.z0 + r.z1 - c[1] + 1e-3] }); }
    // testul de acoperire pe cei mai buni candidați: prima poziție sub prag câștigă (scorul scade cu acoperirea); altfel cea mai liberă
    const obs = plan ? obstacles() : []; let best = null;
    for (const q of ranked.slice(0, 24)){ const pr = probe(obs, q.c[0], q.c[1], q.t[0], q.t[1], asp, hero && hero.f, FOV, isHall ? .5 : SHOT.near); q.cover = pr.cover; q.fg = pr.fg; q.depth = pr.depth;
      q.fin = q.sc - q.cover * 25 - q.fg * 16 + Math.min(q.depth, 5) * (isHall ? 1.6 : .6);   // holul: privirea în lungul lui (adâncimea) contează mai mult decât piesele
      if (q.cover <= SHOT.maxCover && (!best || best.cover > SHOT.maxCover || q.fin > best.fin)) best = q; else if (!best || (best.cover > SHOT.maxCover && q.cover < best.cover)) best = q; }
    // încăpere mică în care niciun cadru obișnuit nu trece: căutare mai largă (grilă de 30 cm × 12 direcții), câștigă
    // cadrul cel mai liber, apoi cel cu mai multe piese principale în el
    if (best.cover > SHOT.maxCover){ const grid = []; for (let x = r.x0 + .3; x <= r.x1 - .3 + 1e-6; x += .3) for (let z = r.z0 + .3; z <= r.z1 - .3 + 1e-6; z += .3) grid.push([x, z]);
      for (const c of grid.filter(c => !its.some(it => c[0] > it.fp.x0 - .15 && c[0] < it.fp.x1 + .15 && c[1] > it.fp.z0 - .15 && c[1] < it.fp.z1 + .15)).concat(sill)) for (let k = 0; k < 12; k++){
        const a = k * Math.PI / 6, t = [c[0] + Math.cos(a) * 2, c[1] + Math.sin(a) * 2], { cover, fg, depth } = probe(obs, c[0], c[1], t[0], t[1], asp, hero && hero.f, FOV, isHall ? .5 : SHOT.near);
        const seen = key.reduce((n, m) => { const f = frame(c, t[0] - c[0], t[1] - c[1], m.cx, m.cz); return n + (f.fwd > .3 && f.side < half * .9 ? m.w : 0); }, 0);
        // sub prag: contează piesele văzute; peste prag, fiecare procent de prim-plan costă mult (o baie mică nu are cadru perfect)
        const fin = seen - cover * 8 - Math.max(0, cover - SHOT.maxCover) * 25 - fg * 10 + Math.min(depth, 5) * .8 + composition(c, t[0] - c[0], t[1] - c[1]);
        if (best.fin2 == null) best.fin2 = -best.cover * 8 - Math.max(0, best.cover - SHOT.maxCover) * 25 - (best.fg || 0) * 10 + Math.min(best.depth || 0, 5) * .8 + composition(best.c, best.t[0] - best.c[0], best.t[1] - best.c[1]);
        if (fin > best.fin2) best = { c, t, sc: seen, cover, fg, depth, fin2: fin }; } }
    lastShot = { room: r.id, openNow, cands: cands.length, pool: pool.length, obs: obs.length, leaves: leaves.map(L => L.map(p => p.map(v => +v.toFixed(2)))), sill: sill.map(p => p.map(v => +v.toFixed(2))), pick: [best.c, best.t, +best.sc.toFixed(2), +best.cover.toFixed(3), +(best.fg || 0).toFixed(2), +(best.depth || 0).toFixed(2)], top: ranked.slice(0, 24).map(q => [q.c.map(v => +v.toFixed(2)), q.t.map(v => +v.toFixed(2)), +q.sc.toFixed(2), q.cover == null ? null : +q.cover.toFixed(3), q.fg == null ? null : +q.fg.toFixed(2), q.depth == null ? null : +q.depth.toFixed(2)]) };
    return { x: best.c[0], y: EYE_Y, z: best.c[1], tx: best.t[0], ty: EYE_Y, tz: best.t[1], fov: FOV, shift: SHOT.shift, cover: best.cover }; }
  // ușile, imediat (fără animație): închise pentru imaginile fixe, apoi starea de dinainte
  function doorsNow(open){ doorLeaves.forEach((d, i) => { const v = Array.isArray(open) ? open[i] : (open ? 1 : 0); d.open = d.target = v; d.pivot.rotation.y = d.closed + d.maxOpen * v * v * (3 - 2 * v); }); R.shadowMap.needsUpdate = true; }
  // imagini fixe pentru export: ansamblu (machetă) sau o cameră după regula SHOT, cu ușile închise
  function renderView(view, w = 1200, h = 800){ if (!plan) return null;
    const prevMode = mode, prevDoors = doorLeaves.map(d => d.target);
    const restore = () => { if (doorLeaves.length === prevDoors.length) doorsNow(prevDoors); resize(); setMode(prevMode); };   // resize: dimensiunile reale ale pânzei (aspectul de dinainte poate fi cel de la pornire)
    R.setSize(w, h, false); if (composer) composer.setSize(w, h); cam.aspect = w / h; cam.updateProjectionMatrix();
    if (view && view.roomId){ const r = plan.camere.find(x => x.id === view.roomId); if (!r){ restore(); return null; }
      setMode('walk'); doorsNow(false); scene.updateMatrixWorld(true); const sh = roomShot(r, w / h);
      cam.fov = sh.fov; cam.position.set(sh.x, sh.y, sh.z); cam.lookAt(sh.tx, sh.ty, sh.tz); lensFor(cam, w / h); assignLights(true, sh.x, sh.z);
      if (mirrorsDirty) updateMirrors();
    } else { setMode('house'); cam.fov = 68; cam.updateProjectionMatrix(); const t = { th: -.7, ph: .85, r: Math.max(W, D, 4) * 1.25 };
      cam.position.set(C.x + Math.sin(t.th) * Math.sin(t.ph) * t.r, Math.cos(t.ph) * t.r, C.z + Math.cos(t.th) * Math.sin(t.ph) * t.r); cam.lookAt(C); }
    let url = null; try { draw(); url = canvas.toDataURL('image/png'); } finally { restore(); } return url; }
  // oglinzile: o sondă cub de 128 px pe cameră (în fața oglinzilor, la înălțimea ochilor), randată o singură dată după fiecare
  // schimbare de plan/mobilier/lumină, folosită ca envMap pe o copie a materialului oglinzii. Fără sondă rămâne mediul HDRI
  // (oglinda nu mai iese neagră: culoare deschisă, envMapIntensity 1,6).
  const probes = [];
  function updateMirrors(){ if (!plan || mode !== 'walk') return; mirrorsDirty = false;
    furniture.traverse(o => { if (o.isMesh && o.material && o.material.userData && o.material.userData.mirrorOf) o.material = o.material.userData.mirrorOf; });
    probes.forEach(p => { p.rt.dispose(); p.mats.forEach(m => m.dispose()); }); probes.length = 0;
    const mirrors = []; furniture.traverse(o => { if (o.isMesh && o.material && o.material.userData && (o.material.userData.mirror || o.material.userData.mirrorOf)) mirrors.push(o); });
    if (!mirrors.length) return;
    const rooms = new Map(), P = new THREE.Vector3();
    for (const o of mirrors){ o.getWorldPosition(P); const r = plan.camere.find(c => P.x > c.x0 - .3 && P.x < c.x1 + .3 && P.z > c.z0 - .3 && P.z < c.z1 + .3); const k = r ? r.id : '_' + rooms.size;
      if (!rooms.has(k)) rooms.set(k, { r, list: [], sum: new THREE.Vector3() }); const e = rooms.get(k); e.list.push(o); e.sum.add(P); }
    const vis = mirrors.map(o => o.visible); mirrors.forEach(o => { o.visible = false; });
    try { for (const { r, list, sum } of rooms.values()){ const at = sum.divideScalar(list.length); if (r) at.lerp(new THREE.Vector3((r.x0 + r.x1) / 2, at.y, (r.z0 + r.z1) / 2), .45); at.y = SHOT.eye;
        const rt = new THREE.WebGLCubeRenderTarget(128, { encoding: THREE.sRGBEncoding, generateMipmaps: true, minFilter: THREE.LinearMipmapLinearFilter }), cc = new THREE.CubeCamera(.05, 80, rt);
        cc.position.copy(at); scene.add(cc); cc.update(R, scene); scene.remove(cc);
        const mats = new Map(); for (const o of list){ const base = o.material.userData.mirrorOf || o.material; let m = mats.get(base);
          if (!m){ m = base.clone(); m.userData = { ...base.userData, mirror: false, mirrorOf: base }; m.envMap = rt.texture; m.envMapIntensity = 1.5; mats.set(base, m); } o.material = m; }
        probes.push({ rt, mats: [...mats.values()] }); } }
    catch { /* fără sondă: oglinda folosește mediul scenei */ }
    finally { mirrors.forEach((o, i) => { o.visible = vis[i]; }); } }
  // panoramă 360° (echirectangulară 2:1) din mijlocul camerei, la înălțimea ochilor: cub de 6 vederi → proiecție
  // echirectangulară într-un shader. Cubul primește deja tone mapping + sRGB, deci pasul de proiecție copiază valorile.
  const EquiShader = { uniforms: { tCube: { value: null } }, vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }',
    fragmentShader: `uniform samplerCube tCube; varying vec2 vUv; const float PI = 3.141592653589793;
      void main(){ float lon = (vUv.x - 0.5) * 2.0 * PI, lat = (vUv.y - 0.5) * PI; vec3 dir = vec3(cos(lat) * sin(lon), sin(lat), -cos(lat) * cos(lon)); gl_FragColor = textureCube(tCube, dir); }` };
  function renderPanorama(roomId, w = 2048){ if (!plan) return null; const r = plan.camere.find(x => x.id === roomId); if (!r) return null;
    const prevMode = mode, prevSize = new THREE.Vector2(), prevRatio = R.getPixelRatio(); R.getSize(prevSize); setMode('walk');
    const rt = new THREE.WebGLCubeRenderTarget(Math.min(2048, w / 2), { encoding: THREE.sRGBEncoding, generateMipmaps: false }), cc = new THREE.CubeCamera(.05, 200, rt);
    const mat = new THREE.ShaderMaterial({ ...EquiShader, uniforms: { tCube: { value: rt.texture } }, depthTest: false, depthWrite: false }), quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), mat), qs = new THREE.Scene(); qs.add(quad);
    // orice excepție lasă randatorul cum era (mărime, densitate, mod) și eliberează resursele GPU ale panoramei
    try { cc.position.set((r.x0 + r.x1) / 2, SHOT.eye, (r.z0 + r.z1) / 2); scene.add(cc); cc.update(R, scene);
      R.setPixelRatio(1); R.setSize(w, w / 2, false); R.render(qs, new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1)); return canvas.toDataURL('image/jpeg', .92); }
    finally { scene.remove(cc); quad.geometry.dispose(); mat.dispose(); rt.dispose(); R.setPixelRatio(prevRatio); R.setSize(prevSize.x, prevSize.y, false); if (composer) composer.setSize(prevSize.x, prevSize.y); setMode(prevMode); } }

  // ---------- navigare ----------
  let mode = 'house', yaw = 0, pitch = 0; const EYE = SHOT.eye, player = new THREE.Vector3(); const orbit = { th: -.7, ph: .95, r: 12 };
  function collide(x, z){ const r = .24;
    for (const c of colliders){ const px = x - c.ax, pz = z - c.az, s = Math.max(c.s0, Math.min(c.s1, px * c.ux + pz * c.uz)), cx = c.ax + c.ux * s, cz = c.az + c.uz * s, dx = x - cx, dz = z - cz, dd = Math.hypot(dx, dz);
      if (dd < r + .07 && dd > 1e-6){ const k = (r + .07 - dd) / dd; x += dx * k; z += dz * k; } }
    for (const b of [...structBlockers, ...blockers]){ if (x > b.x0 - r && x < b.x1 + r && z > b.z0 - r && z < b.z1 + r){ const dl = x - (b.x0 - r), dr = (b.x1 + r) - x, du = z - (b.z0 - r), dn = (b.z1 + r) - z, m = Math.min(dl, dr, du, dn);
      if (m === dl) x = b.x0 - r; else if (m === dr) x = b.x1 + r; else if (m === du) z = b.z0 - r; else z = b.z1 + r; } }
    return [x, z]; }
  // intrarea în tur: același cadru ca imaginea de prezentare a camerei (regula SHOT: 1,5 m, orizontal, spre piesele principale)
  function goRoom(id){ if (!plan) return; const room = plan.camere.find(r => r.id === id); if (!room) return; resize(); setMode('walk'); scene.updateMatrixWorld(true);
    const sh = roomShot(room, cam.aspect || 1.5, walkFov()), [x, z] = collide(sh.x, sh.z); player.set(x, 0, z); yaw = Math.atan2(-(sh.tx - x), -(sh.tz - z)); pitch = 0; assignLights(true); }
  // macheta taie pereții la 42% din înălțime; aceeași tăietură se aplică și mobilierului (dulapuri înalte, biblioteci),
  // altfel ar ieși deasupra pereților și ar părea în afara casei
  const cut = new THREE.Plane(new THREE.Vector3(0, -1, 0), 1.1);
  function setMode(m){ const changed = m !== mode; mode = m; walls.scale.y = m === 'walk' ? 1 : .42; ceilings.visible = m === 'walk'; cut.constant = (plan ? plan.inaltime : 2.6) * .42 + .01; R.clippingPlanes = m === 'walk' ? [] : [cut];
    // turul: FOV-ul regulii SHOT și obiectivul deplasat (verticale drepte la privirea orizontală); macheta: fără deplasare
    if (m === 'walk'){ cam.fov = walkFov(); lensFor(cam, cam.aspect || 1.5); } else { cam.fov = 68; cam.clearViewOffset(); }
    cam.updateProjectionMatrix(); applyBackdrop(); if (changed){ R.shadowMap.needsUpdate = true; assignLights(true); } }
  const keys = {}; const joy = { f: 0, s: 0 }; // joystick virtual: axe în [-1, 1], însumate cu tastele
  const kd = e => { if (e.target.closest && e.target.closest('input,select,textarea')) return; keys[e.key.toLowerCase()] = true; }, ku = e => { keys[e.key.toLowerCase()] = false; };
  addEventListener('keydown', kd); addEventListener('keyup', ku);
  let drag = false, lx = 0, ly = 0, moved = 0; const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
  // două degete: ciupire = zoom (machetă) sau pas înainte/înapoi (tur); nu se rotește și nu se alege nimic în timpul gestului
  const ptrs = new Map(); let pinchD = 0;
  const pdist = () => { const [a, b] = [...ptrs.values()]; return Math.hypot(a.x - b.x, a.y - b.y); };
  const pd = e => { ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY }); canvas.setPointerCapture(e.pointerId);
    if (ptrs.size === 2){ pinchD = pdist(); drag = false; moved = 99; return; } drag = true; lx = e.clientX; ly = e.clientY; moved = 0; };
  const pmv = e => { if (ptrs.has(e.pointerId)) ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (ptrs.size >= 2 && pinchD > 0){ const d = pdist(), k = pinchD / Math.max(1, d); pinchD = d;
      if (mode === 'house') orbit.r = Math.max(4, Math.min(40, orbit.r * k)); else { const step = (1 - k) * 2.2, [x, z] = collide(player.x - Math.sin(yaw) * step, player.z - Math.cos(yaw) * step); player.x = x; player.z = z; } return; }
    if (!drag) return; const dx = e.clientX - lx, dy = e.clientY - ly; lx = e.clientX; ly = e.clientY; moved += Math.abs(dx) + Math.abs(dy);
    if (mode === 'walk'){ yaw -= dx * .0045; pitch = Math.max(-1.2, Math.min(1.1, pitch - dy * .0035)); } else { orbit.th -= dx * .006; orbit.ph = Math.max(.12, Math.min(1.45, orbit.ph - dy * .005)); } };
  const pu = e => { const wasPinch = ptrs.size >= 2 || pinchD > 0; ptrs.delete(e.pointerId); if (ptrs.size < 2) pinchD = 0; if (wasPinch){ drag = false; return; }
    drag = false; if (moved >= 6) return; const r = canvas.getBoundingClientRect(); ndc.set((e.clientX - r.left) / r.width * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1); ray.setFromCamera(ndc, cam);
    // clic pe o ușă: se deschide sau se închide (înaintea mobilierului, ca într-un tur real)
    const dh = ray.intersectObjects(doorLeaves.map(d => d.pivot), true)[0], fh = onPick ? ray.intersectObjects(pickables, true)[0] : null;
    if (dh && (!fh || dh.distance <= fh.distance)){ const d = doorLeaves[dh.object.userData.door]; if (d) d.target = d.target > .5 ? 0 : 1; return; }
    if (onPick) onPick(fh ? fh.object.userData.item.userData.pid : null); };
  const wh = e => { if (mode === 'house'){ orbit.r = Math.max(4, Math.min(40, orbit.r + e.deltaY * .01)); e.preventDefault(); } };
  const onCtxLost = e => { e.preventDefault(); contextLost = true; if (onContextLost) onContextLost(); };
  const onCtxRestored = () => { contextLost = false; R.shadowMap.needsUpdate = true; mirrorsDirty = true; if (plan) { buildHouse(plan); buildFurniture(lastItems); } if (onContextRestored) onContextRestored(); };
  const onMql = e => { reduce = !!e.matches; if (reduce) doorsNow(doorsOpen); };
  if (mql && mql.addEventListener) mql.addEventListener('change', onMql);
  canvas.addEventListener('webglcontextlost', onCtxLost); canvas.addEventListener('webglcontextrestored', onCtxRestored);
  canvas.addEventListener('pointerdown', pd); canvas.addEventListener('pointermove', pmv); canvas.addEventListener('pointerup', pu); canvas.addEventListener('pointercancel', pu); canvas.style.touchAction = 'none'; canvas.addEventListener('wheel', wh, { passive: false });
  // fibra fotografică a parchetului sosește după prima construire: reconstruim casa o singură dată
  const unTex = onTexturesReady(() => { if (alive && plan) buildHouse(plan); });
  function resize(){ const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1; R.setSize(w, h, false); if (composer) composer.setSize(w, h); cam.aspect = w / h; if (mode === 'walk'){ cam.fov = walkFov(); lensFor(cam, cam.aspect); } cam.updateProjectionMatrix(); }
  const ro = new ResizeObserver(resize); ro.observe(canvas);
  (function loop(){ if (!alive) return; raf = requestAnimationFrame(loop); const now = performance.now(), dt = Math.min(.05, (now - last) / 1000); last = now;
    for (const d of doorLeaves) if (d.open !== d.target){ d.open = reduce ? d.target : (d.target > d.open ? Math.min(d.target, d.open + dt * 1.4) : Math.max(d.target, d.open - dt * 1.4));
      const e = d.open * d.open * (3 - 2 * d.open); d.pivot.rotation.y = d.closed + d.maxOpen * e; R.shadowMap.needsUpdate = true; }
    if (mode === 'walk'){ let f = 0, s = 0; if (keys.w || keys.arrowup) f += 1; if (keys.s || keys.arrowdown) f -= 1; if (keys.a || keys.arrowleft) s -= 1; if (keys.d || keys.arrowright) s += 1; f = Math.max(-1, Math.min(1, f + joy.f)); s = Math.max(-1, Math.min(1, s + joy.s));
      if (f || s){ const sp = 1.7 * dt, fx = -Math.sin(yaw), fz = -Math.cos(yaw), rx = Math.cos(yaw), rz = -Math.sin(yaw); const [x, z] = collide(player.x + (fx * f + rx * s) * sp, player.z + (fz * f + rz * s) * sp); player.x = x; player.z = z; }
      cam.position.set(player.x, EYE, player.z); cam.rotation.order = 'YXZ'; cam.rotation.set(pitch, yaw, 0); assignLights(false);
      if (mirrorsDirty) updateMirrors();
    } else {
      if (keys.arrowleft || keys.a) orbit.th -= 1.4 * dt;
      if (keys.arrowright || keys.d) orbit.th += 1.4 * dt;
      if (keys.arrowup || keys.w) orbit.ph = Math.max(.12, orbit.ph - 1.1 * dt);
      if (keys.arrowdown || keys.s) orbit.ph = Math.min(1.45, orbit.ph + 1.1 * dt);
      if (keys['+'] || keys['=']) orbit.r = Math.max(4, orbit.r - 6 * dt);
      if (keys['-'] || keys['_']) orbit.r = Math.min(40, orbit.r + 6 * dt);
      if (keys['0']) { orbit.th = -.7; orbit.ph = .95; orbit.r = Math.max(W, D, 4) * 1.15; }
      const t = new THREE.Vector3(C.x + Math.sin(orbit.th) * Math.sin(orbit.ph) * orbit.r, Math.cos(orbit.ph) * orbit.r, C.z + Math.cos(orbit.th) * Math.sin(orbit.ph) * orbit.r); cam.position.lerp(t, reduce ? 1 : Math.min(1, dt * 5)); cam.lookAt(C);
    }
    draw(); })();
  // ---------- scena pentru randarea fotorealistă (tools/blender/casa3d_render.py) ----------
  // Aceleași primitive ca vederea 3D (cutii, cutii rotunjite, cilindri, plăci, tor), cu poziția în lume și materialul
  // lor: tipul (lemn, textil, sticlă...) se deduce din textura și parametrii PBR, iar valorile brute merg și ele.
  function exportScene(meta = {}){ if (!plan) return null; const prevMode = mode, prevDoors = doorLeaves.map(d => d.target); setMode('walk'); doorsNow(false); scene.updateMatrixWorld(true);   // ușile închise în imaginile fixe
    const r4 = v => Math.round(v * 1e4) / 1e4, hex = c => '#' + c.clone().convertLinearToSRGB().getHexString();
    const mats = [], matIx = new Map(), meshes = [], P = new THREE.Vector3(), Q = new THREE.Quaternion(), Sc = new THREE.Vector3();
    const kindOf = m => { if (m.userData && m.userData.kind) return m.userData.kind; if (m.userData && m.userData.ceil) return 'paint'; if (m.userData && m.userData.mirrorOf) return 'mirror'; const img = m.map && m.map.image;
      if (img && img === parquet.image) return 'parquet'; if (img && img === tiles.image) return 'tile';
      if (m.map === grain || m.map === grainV) return 'wood'; if (m.map === rattan) return 'rattan'; if (m.map === weave) return m.sheen ? 'velvet' : 'fabric'; if (m.bumpMap === weave) return 'leather';
      if (m.emissive && (m.emissive.r + m.emissive.g + m.emissive.b) > .05) return 'emit';
      if (m.transparent && m.opacity < .4) return 'glass'; if (m.transparent) return 'frost';
      if (m.metalness >= .95) return m.roughness < .06 ? 'mirror' : 'chrome'; if (m.metalness >= .8) return 'metal';
      if ((m.clearcoat || 0) > .5) return 'ceramic'; return 'paint'; };
    const matOf = m => { if (matIx.has(m)) return matIx.get(m);
      const d = { k: kindOf(m), c: hex(m.color), r: r4(m.roughness ?? .8), mt: r4(m.metalness ?? 0), o: m.transparent ? r4(m.opacity) : 1 };
      if (d.k === 'emit') { d.e = hex(m.emissive); d.ei = r4(m.emissiveIntensity ?? 1); }
      if (m.clearcoat) d.cc = r4(m.clearcoat);
      // finisajele de designer: culoarea reală (harta e albă la bază), mărimea plăcii, modul de așezare; spicul și chevronul pleacă și ca imagine
      if (m.userData && m.userData.col) d.c = m.userData.col;
      if (m.userData && m.userData.size) d.size = m.userData.size.map(r4);
      if (m.userData && m.userData.marble) d.marble = 1;
      if (m.userData && m.userData.pat){ d.pat = m.userData.pat; const img = m.map && m.map.image;
        if ((d.pat.pattern === 'herringbone' || d.pat.pattern === 'chevron') && img && img.toDataURL) d.img = img.toDataURL('image/jpeg', .9); }
      matIx.set(m, mats.length); mats.push(d); return mats.length - 1; };
    const geoOf = g => { const p = g.parameters || {};
      if (g.userData && g.userData.rbox) return { t: 'rbox', p: g.userData.rbox.map(r4) };
      if (g.userData && g.userData.box) return { t: 'box', p: g.userData.box.map(r4) };   // cutie cu muchii rotunjite: Blender o rotunjește cu nodul Bevel
      if (g.userData && g.userData.free){ const pos = g.attributes.position; if (pos.count > 30000) return null; return { t: 'mesh', p: [], v: Array.from(pos.array, r4), i: g.index ? Array.from(g.index.array) : null, sm: g.userData.smooth ? 1 : 0 }; }
      // o geometrie parametrică transformată după creare (rotateZ, translate) nu mai e forma din parametri: pleacă ca triunghiuri
      if (p && g.type !== 'LatheGeometry' && /^(Box|Plane|Cylinder|Torus|Sphere)/.test(g.type)){ if (!g.boundingBox) g.computeBoundingBox(); const bb = g.boundingBox, hy = g.type.startsWith('Plane') ? p.height / 2 : g.type.startsWith('Box') ? p.height / 2 : g.type.startsWith('Cylinder') ? p.height / 2 : null;
        if (bb && hy != null && (Math.abs(bb.max.y - hy) > 1e-3 || Math.abs(bb.min.y + hy) > 1e-3 || Math.abs(bb.max.x + bb.min.x) > 1e-3)){ const pos = g.attributes.position; if (!pos || pos.count > 30000) return null; return { t: 'mesh', p: [], v: Array.from(pos.array, r4), i: g.index ? Array.from(g.index.array) : null }; } }
      switch (g.type){
        case 'BoxGeometry': case 'BoxBufferGeometry': return { t: 'box', p: [p.width, p.height, p.depth].map(r4) };
        case 'PlaneGeometry': case 'PlaneBufferGeometry': return { t: 'plane', p: [p.width, p.height].map(r4) };
        case 'CylinderGeometry': case 'CylinderBufferGeometry': return { t: 'cyl', p: [p.radiusTop, p.radiusBottom, p.height, p.radialSegments, p.openEnded ? 1 : 0, p.thetaStart, p.thetaLength].map(r4) };
        case 'TorusGeometry': case 'TorusBufferGeometry': return { t: 'torus', p: [p.radius, p.tube, p.radialSegments, p.tubularSegments, p.arc].map(r4) };
        case 'SphereGeometry': case 'SphereBufferGeometry': return { t: 'sphere', p: [p.radius, p.widthSegments, p.heightSegments].map(r4) };
        case 'LatheGeometry': case 'LatheBufferGeometry': return { t: 'lathe', p: [p.segments, p.phiStart, p.phiLength].map(r4), pts: p.points.map(v => [r4(v.x), r4(v.y)]) };
        default: { // forme libere (plăci decupate, ExtrudeGeometry): triunghiurile, cu transformările deja aplicate geometriei
          const pos = g.attributes && g.attributes.position; if (!pos || pos.count > 30000) return null;
          return { t: 'mesh', p: [], v: Array.from(pos.array, r4), i: g.index ? Array.from(g.index.array) : null }; } } };
    for (const root of [house, furniture]) root.traverse(o => { if (!o.isMesh || !o.visible) return; for (let a = o.parent; a; a = a.parent) if (!a.visible) return;
      const g = geoOf(o.geometry); if (!g) return; o.matrixWorld.decompose(P, Q, Sc);
      const m = Array.isArray(o.material) ? o.material.map(matOf) : matOf(o.material);
      meshes.push({ g, m, p: [P.x, P.y, P.z].map(r4), q: [Q.x, Q.y, Q.z, Q.w].map(r4), s: [Sc.x, Sc.y, Sc.z].map(r4) }); });
    const rooms = plan.camere.map(r => ({ id: r.id, name: r.nume, type: r.tip, rect: [r.x0, r.z0, r.x1, r.z1] }));
    // golurile (uși și ferestre): mijlocul, capetele pe axa peretelui, lățimea, înălțimea, parapetul, normala peretelui și
    // deschiderea ușii (balamaua și partea) — camera foto din Blender nu stă în ele și nu le are în prim-plan
    const openings = plan.pereti.flatMap(wl => { const [ax, az] = wl.a, [bx, bz] = wl.b, L = Math.hypot(bx - ax, bz - az); if (L < .01) return []; const ux = (bx - ax) / L, uz = (bz - az) / L, th = wl.ext ? .25 : .15;
      return (wl.goluri || []).map(g => { const m = g.la + g.l / 2, door = g.tip === 'usa', sill = door ? 0 : (g.sill != null ? g.sill : .9), top = door ? Math.min(plan.inaltime - .05, g.h || 2.1) : Math.min(plan.inaltime - .05, sill + (g.h || 1.3));
        return { kind: door ? 'door' : 'window', p: [r4(ax + ux * m), r4(az + uz * m)], a: [r4(ax + ux * g.la), r4(az + uz * g.la)], b: [r4(ax + ux * (g.la + g.l)), r4(az + uz * (g.la + g.l))],
          w: r4(g.l), h: r4(top - sill), sill: r4(sill), n: [r4(-uz), r4(ux)], th, ext: !!wl.ext, entrance: !!g.intrare, swing: door ? (g.sw || null) : null, closed: door ? true : undefined }; }); });
    // decorul: trimitem doar modelul glTF și matricea lui (Blender îl importă din public/models), nu mii de triunghiuri
    decor.updateMatrixWorld(true); const decorRefs = decor.children.filter(o => o.userData.deco && o.children[0]).map(o => ({ model: o.userData.deco, m: o.children[0].matrixWorld.elements.map(r4) }));
    // piesele de mobilier (grupă, amprentă, înălțime): camera foto le încadrează pe cele principale și nu stă niciodată în ele
    const items = lastItems.filter(it => it.variant && it.variant.w && it.fp).map(it => ({ g: it.group, m: it.variant.model, fp: [it.fp.x0, it.fp.z0, it.fp.x1, it.fp.z1].map(r4), h: r4(it.variant.h / 100), r: r4(it.rotation || 0), c: [r4(it.x), r4(it.z)], n: String(it.variant.nume || '').slice(0, 120) }));
    const out = { format: 'casa3d-scene', version: 1, units: 'm', up: 'Y', height: plan.inaltime, rooms, items, materials: mats, meshes, decor: decorRefs,
      sun: { dir: light && light.dir ? light.dir : [.45, .75, -.48], time: meta.time || 'day', azimuthDeg: meta.azimuthDeg ?? null },
      lights: roomLights.map(l => ({ p: [l.x, l.y, l.z].map(r4), i: r4(interiorLights[0] ? interiorLights[0].intensity : (light ? light.interior : .35)) })), openings, camera: { eye: SHOT.eye, vfov: SHOT.fov, shift: SHOT.shift, maxCover: SHOT.maxCover, near: SHOT.near }, ...meta };
    doorsNow(prevDoors); setMode(prevMode); return out; }
  // modelul 3D ca fișier GLB (glTF binar), pentru vizualizatori (3ds Max, SketchUp, Blender, Twinmotion): aceeași geometrie și
  // materiale PBR ca în vedere, în metri, cu Y în sus; fără cer, umbre și lumina mediului.
  async function exportGLB(){ if (!plan) return null; const prevMode = mode; setMode('walk'); scene.updateMatrixWorld(true);
    const { GLTFExporter } = await import('three/examples/jsm/exporters/GLTFExporter.js');
    const roots = scene.children.filter(o => !o.isLight && !o.isCamera && o.visible && !(o.userData && o.userData.noExport));
    try { return await new Promise((res, rej) => { try { new GLTFExporter().parse(roots, r => res(r), { binary: true, onlyVisible: true, maxTextureSize: 2048 }); } catch (e){ rej(e); } }); }
    finally { setMode(prevMode); } }

  // contoare pentru diagnostic (ochiurile, materialele, apelurile de desen ale unui cadru, luminile punctuale active)
  function getStats(){ const ar = R.info.autoReset, count = () => { R.info.reset(); draw(); return { calls: R.info.render.calls, triangles: R.info.render.triangles }; };
    R.info.autoReset = false; const steady = count(); R.shadowMap.needsUpdate = true; const sh = count(); R.info.autoReset = ar; const mats = new Set(); let meshes = 0, pl = 0; const vis = o => { for (let a = o; a; a = a.parent) if (!a.visible) return false; return true; };
    scene.traverse(o => { if (o.isMesh && vis(o)){ meshes++; (Array.isArray(o.material) ? o.material : [o.material]).forEach(m => mats.add(m)); } if (o.isPointLight && vis(o)) pl++; });
    return { calls: steady.calls, triangles: steady.triangles, callsWithShadow: sh.calls, geometries: R.info.memory.geometries, textures: R.info.memory.textures, programs: R.info.programs ? R.info.programs.length : 0, meshes, materials: mats.size, pointLights: pl, mode, shot: lastShot }; }
  canvas.__casa3dStats = getStats;
  return {
    setState(p, items){ plan = p; buildHouse(p); buildFurniture(items); },
    getStats,
    setDecor(on){ decorOn = !!on; buildDecor(lastItems); },
    setDoors(open){ doorsOpen = !!open; doorsNow(doorsOpen); },
    setMode, goRoom, getMode: () => mode, setLighting, capture, renderView, renderPanorama, exportScene, exportGLB,
    setQuality(q){ quality = q === 'high' ? 'high' : 'normal'; if (quality === 'high' && !composer){ try { setupComposer(); } catch (e){ quality = 'normal'; composer = null; } } return quality; },
    setMove(forward, strafe){ joy.f = Number.isFinite(forward) ? Math.max(-1, Math.min(1, forward)) : 0; joy.s = Number.isFinite(strafe) ? Math.max(-1, Math.min(1, strafe)) : 0; },
    dispose(){ alive = false; unTex(); if (mql && mql.removeEventListener) mql.removeEventListener('change', onMql); cancelAnimationFrame(raf); ro.disconnect(); removeEventListener('keydown', kd); removeEventListener('keyup', ku); canvas.removeEventListener('webglcontextlost', onCtxLost); canvas.removeEventListener('webglcontextrestored', onCtxRestored); canvas.removeEventListener('pointerdown', pd); canvas.removeEventListener('pointermove', pmv); canvas.removeEventListener('pointerup', pu); canvas.removeEventListener('pointercancel', pu); canvas.removeEventListener('wheel', wh); disposeGroup(house); disposeGroup(furniture); if (contact){ contact.geometry.dispose(); if (contact.material.uniforms.map.value) contact.material.uniforms.map.value.dispose(); contact.material.dispose(); contact = null; } probes.forEach(p => { p.rt.dispose(); p.mats.forEach(m => m.dispose()); }); probes.length = 0; ground.geometry.dispose(); ground.material.dispose(); skyGeo.dispose(); sky.material.dispose(); parquet.dispose(); tiles.dispose(); wallMat.dispose(); extMat.dispose(); glass.dispose(); frameMat.dispose(); ceilMat.dispose(); skirtMat.dispose(); Object.values(lookCache).forEach(m => m.dispose()); scene.environment?.dispose?.(); pm.dispose(); if (composer){ ssao.dispose(); composer.renderTarget1.dispose(); composer.renderTarget2.dispose(); composer = null; } R.dispose(); }
  };
}
