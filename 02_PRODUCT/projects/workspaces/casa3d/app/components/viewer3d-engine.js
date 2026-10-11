// components/viewer3d.js — scena 3D portată din prototip (tur la persoana întâi + machetă).
// Modelele de mobilier, materialele și construcția pereților sunt copiate din prototip.
import * as THREE from 'three';
import { floorTexture, wallTexture } from './finish-textures.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { SSAOPass } from 'three/examples/jsm/postprocessing/SSAOPass.js';
import { ShaderPass } from 'three/examples/jsm/postprocessing/ShaderPass.js';
import { SLAB } from '../core/levels';

// Pasul final al calității înalte: în r128 tone mapping-ul (ACES) se aplică și la randarea în texturi
// (WebGLPrograms: toneMapping nu depinde de țintă), dar ieșirea în textură rămâne liniară; aici facem doar sRGB.
const FinishShader = { uniforms: { tDiffuse: { value: null } }, vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `uniform sampler2D tDiffuse; varying vec2 vUv;
    vec3 srgb(vec3 c){ return mix(pow(c, vec3(0.41666)) * 1.055 - vec3(0.055), c * 12.92, vec3(lessThanEqual(c, vec3(0.0031308)))); }
    void main(){ vec4 t = texture2D(tDiffuse, vUv); gl_FragColor = vec4(srgb(clamp(t.rgb, 0.0, 1.0)), t.a); }` };

export function createViewer(canvas, { onPick } = {}){
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const R = new THREE.WebGLRenderer({ canvas, antialias: true });
  R.setPixelRatio(Math.min(devicePixelRatio, 2)); R.outputEncoding = THREE.sRGBEncoding; R.toneMapping = THREE.ACESFilmicToneMapping; R.toneMappingExposure = .78;
  R.shadowMap.enabled = true; R.shadowMap.type = THREE.PCFSoftShadowMap;
  const scene = new THREE.Scene(); scene.background = new THREE.Color('#e9ebe7'); scene.fog = new THREE.Fog('#e9ebe7', 25, 60);
  const cam = new THREE.PerspectiveCamera(68, 1, .05, 200);
  // calitate înaltă: ocluzie ambientală (umbre de contact în colțuri și sub mobilă). Distanțele SSAO sunt în adâncime
  // normalizată la (far - near) = ~200 m: 0.0015 ≈ 0,3 m, 0.0001 ≈ 2 cm (sub atât, suprafețele plane s-ar umbri singure); raza nucleului e în metri.
  let quality = 'normal', composer = null, ssao = null, finish = null;
  function setupComposer(){ const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1; composer = new EffectComposer(R); composer.setPixelRatio(Math.min(devicePixelRatio, 1.5)); composer.setSize(w, h);
    ssao = new SSAOPass(scene, cam, w, h); ssao.kernelRadius = .3; ssao.minDistance = .0001; ssao.maxDistance = .0015; composer.addPass(ssao);
    finish = new ShaderPass(FinishShader); composer.addPass(finish); }
  // SSAOPass (r128) copiază proiecția camerei doar la creare și la setSize; o sincronizăm la fiecare cadru,
  // altfel umbrele de contact folosesc un aspect/fov vechi (după redimensionare sau în imaginile de export)
  const syncSsao = () => { const u = ssao.ssaoMaterial.uniforms; u.cameraProjectionMatrix.value.copy(cam.projectionMatrix); u.cameraInverseProjectionMatrix.value.copy(cam.projectionMatrixInverse);
    u.cameraNear.value = cam.near; u.cameraFar.value = cam.far; ssao.depthRenderMaterial.uniforms.cameraNear.value = cam.near; ssao.depthRenderMaterial.uniforms.cameraFar.value = cam.far; };
  const draw = () => { if (quality === 'high' && composer){ syncSsao(); composer.render(); } else R.render(scene, cam); };
  const pm = new THREE.PMREMGenerator(R); scene.environment = pm.fromScene(new RoomEnvironment(), .04).texture;
  const hemi = new THREE.HemisphereLight(0xffffff, 0xcfc8bc, .22); scene.add(hemi);
  let light = null; const interiorLights = []; // parametrii de iluminare primiți din core/lighting.ts
  const sun = new THREE.DirectionalLight(0xfff3e2, 1.1); sun.castShadow = true; sun.shadow.mapSize.set(2048, 2048); Object.assign(sun.shadow.camera, { left: -12, right: 12, top: 12, bottom: -12, near: 1, far: 50 }); sun.shadow.bias = -.0005; scene.add(sun, sun.target);
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(120, 120), new THREE.MeshStandardMaterial({ color: '#dcdfd9', roughness: 1 })); ground.rotation.x = -Math.PI / 2; ground.position.y = -.02; ground.receiveShadow = true; scene.add(ground);
  const lin = c => new THREE.Color(c).convertSRGBToLinear();
  const M = (c, o) => new THREE.MeshStandardMaterial(Object.assign({ color: lin(c), roughness: .75 }, o || {}));
  const tex = (w, h, draw) => { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d'), w, h); const t = new THREE.CanvasTexture(cv); t.encoding = THREE.sRGBEncoding; t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 8; return t; };
  const parquet = tex(512, 512, (g, w, h) => { const cols = ['#b88a5a', '#a97c4f', '#c29665', '#9f7248', '#b5855a'];
    for (let y = 0; y < h; y += 32) for (let x = -((y / 32) % 3) * 64; x < w; x += 192){ g.fillStyle = cols[(x / 64 + y / 32 * 7 & 7) % 5]; g.fillRect(x, y, 190, 30);
      for (let k = 0; k < 6; k++){ g.strokeStyle = 'rgba(70,40,20,.10)'; g.beginPath(); g.moveTo(x, y + 4 + k * 4.5); g.bezierCurveTo(x + 60, y + 2 + k * 4.5, x + 120, y + 8 + k * 4.5, x + 190, y + 4 + k * 4.5); g.stroke(); } } });
  const tiles = tex(512, 512, (g, w, h) => { g.fillStyle = '#b9bab5'; g.fillRect(0, 0, w, h); for (let y = 0; y < h; y += 128) for (let x = 0; x < w; x += 128){ const v = 222 + (Math.random() * 12 | 0); g.fillStyle = `rgb(${v},${v - 1},${v - 4})`; g.fillRect(x + 2, y + 2, 124, 124); } });
  const wallMat = M('#f4f3ef', { roughness: .92 }), extMat = M('#ecebe6', { roughness: .95 }), glass = new THREE.MeshPhysicalMaterial({ color: lin('#cfe3ee'), transparent: true, opacity: .28, roughness: .05, metalness: 0 });
  const frameMat = M('#ffffff', { roughness: .5 }), ceilMat = M('#fbfbf9', { roughness: 1 });
  const skirtMat = M('#f7f6f2', { roughness: .45 });
  // materiale pe culoare, cu cache: fețe de perete, tavane, rame (culorile vin din core/appearance.ts prin plan)
  const lookCache = {}; const lookMat = (kind, hex, rough) => lookCache[kind + hex] ||= M(hex, { roughness: rough });
  const faceMat = (hex, ext) => hex ? lookMat('wall', hex, ext ? .95 : .92) : (ext ? extMat : wallMat);

/* ---------- materiale realiste (texturi generate, fără imagini externe) ---------- */
const gray = (w, h, draw) => { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d'), w, h); const t = new THREE.CanvasTexture(cv); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 8; return t; };
const weave = gray(256, 256, (g, w, h) => { const id = g.createImageData(w, h); for (let y = 0; y < h; y++) for (let x = 0; x < w; x++){ const i = (y * w + x) * 4, v = 205 + ((x % 4 < 2) ^ (y % 4 < 2) ? 18 : 0) + Math.random() * 30; id.data[i] = id.data[i + 1] = id.data[i + 2] = Math.min(255, v); id.data[i + 3] = 255; } g.putImageData(id, 0, 0); });
weave.repeat.set(6, 6);
const grain = gray(512, 512, (g, w, h) => { g.fillStyle = '#d8d8d8'; g.fillRect(0, 0, w, h); for (let i = 0; i < 260; i++){ const y = Math.random() * h, a = .05 + Math.random() * .12; g.strokeStyle = `rgba(0,0,0,${a})`; g.lineWidth = .6 + Math.random() * 1.8; g.beginPath(); g.moveTo(0, y); for (let x = 0; x <= w; x += 32) g.lineTo(x, y + Math.sin(x / 70 + i) * 3); g.stroke(); } });
const rattan = gray(128, 128, (g, w, h) => { g.fillStyle = '#b8b8b8'; g.fillRect(0, 0, w, h); g.strokeStyle = '#6d6d6d'; g.lineWidth = 2; for (let i = -w; i < w * 2; i += 10){ g.beginPath(); g.moveTo(i, 0); g.lineTo(i + h, h); g.stroke(); g.beginPath(); g.moveTo(i + h, 0); g.lineTo(i, h); g.stroke(); } });
rattan.repeat.set(3, 3);
const matCache = {};
const lumOf = hex => { const n = parseInt(String(hex).slice(1), 16); return ((n >> 16 & 255) * .2126 + (n >> 8 & 255) * .7152 + (n & 255) * .0722) / 255; };
function MAT(kind, col){ const k = kind + col; if (matCache[k]) return matCache[k]; const c = lin(col); let m;
  switch (kind){
    case 'fabric': m = new THREE.MeshStandardMaterial({ color: c, map: weave, bumpMap: weave, bumpScale: .003, roughness: .97 }); break;
    case 'velvet': m = new THREE.MeshPhysicalMaterial({ color: c, map: weave, roughness: .85, sheen: lin('#9fc3c2') }); break;
    case 'leather': m = new THREE.MeshPhysicalMaterial({ color: c, roughness: .42, clearcoat: .35, clearcoatRoughness: .5, bumpMap: weave, bumpScale: .001 }); break;
    case 'wood': m = new THREE.MeshStandardMaterial({ color: c, map: grain, roughness: .58 }); break;
    case 'paint': m = new THREE.MeshStandardMaterial({ color: c, roughness: .48 }); break;
    case 'metal': m = new THREE.MeshStandardMaterial({ color: c, metalness: .9, roughness: .32 }); break;
    case 'chrome': m = new THREE.MeshStandardMaterial({ color: c, metalness: 1, roughness: .12 }); break;
    case 'glass': m = new THREE.MeshPhysicalMaterial({ color: c, transparent: true, opacity: .22, roughness: .04, metalness: 0, depthWrite: false }); break;
    case 'frost': m = new THREE.MeshPhysicalMaterial({ color: c, transparent: true, opacity: .55, roughness: .6, depthWrite: false }); break;
    case 'mirror': m = new THREE.MeshStandardMaterial({ color: c, metalness: 1, roughness: .03 }); break;
    case 'ceramic': m = new THREE.MeshPhysicalMaterial({ color: c, roughness: .12, clearcoat: .8 }); break;
    case 'rattan': m = new THREE.MeshStandardMaterial({ color: c, map: rattan, roughness: .8 }); break;
    case 'screen': m = new THREE.MeshStandardMaterial({ color: c, roughness: .2, metalness: .2 }); break;
    default: m = new THREE.MeshStandardMaterial({ color: c, roughness: .8 }); }
  return matCache[k] = m; }
// cutie cu muchii rotunjite (perne, tapițerie)
function rgeo(w, h, d, r){ r = Math.max(.002, Math.min(r, w / 2 - .002, h / 2 - .002, d / 2 - .002)); const s = new THREE.Shape(), x = -(w - 2 * r) / 2, y = -(h - 2 * r) / 2, W2 = w - 2 * r, H2 = h - 2 * r, q = Math.min(W2, H2) * .08;
  s.moveTo(x + q, y); s.lineTo(x + W2 - q, y); s.quadraticCurveTo(x + W2, y, x + W2, y + q); s.lineTo(x + W2, y + H2 - q); s.quadraticCurveTo(x + W2, y + H2, x + W2 - q, y + H2); s.lineTo(x + q, y + H2); s.quadraticCurveTo(x, y + H2, x, y + H2 - q); s.lineTo(x, y + q); s.quadraticCurveTo(x, y, x + q, y);
  const g = new THREE.ExtrudeGeometry(s, { depth: Math.max(.001, d - 2 * r), bevelEnabled: true, bevelSize: r, bevelThickness: r, bevelSegments: 4, curveSegments: 6 }); g.translate(0, 0, -(d - 2 * r) / 2); g.userData.rbox = [w, h, d, r]; return g; }

/* ---------- modele 3D pe variante, la dimensiunile oficiale ---------- */
function model(it){
  const s = it.s || {}, g = new THREE.Group(), w = it.w / 100, d = it.d / 100, h = it.h / 100;
  const B = (bw, bh, bd, x, y, z, m) => { const o = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, bd), m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const RB = (bw, bh, bd, r, x, y, z, m) => { const o = new THREE.Mesh(rgeo(bw, bh, bd, r), m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const CY = (rt, rb, hh, x, y, z, m, seg) => { const o = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, hh, seg || 24), m); o.position.set(x, y, z); o.castShadow = true; g.add(o); return o; };
  const body = (c) => s.wood ? MAT('wood', c) : MAT('paint', c);
  switch (it.model){
    case 'sofa': { const up = MAT(s.mat || 'fabric', s.col), dark = MAT('paint', '#1b1b1b'), ad = Math.min(.95, d), arm = w < 1.1 ? .16 : .24; const sw = s.chaise ? w - .02 : w;
      [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => CY(.02, .018, .06, a * (sw / 2 - .08), .03, b * (ad / 2 - .08), dark, 10));
      RB(sw, .22, ad, .03, 0, .17, 0, up);                          // bază
      RB(.24, .36, ad, .06, -sw / 2 + arm / 2, .46, 0, up);         // cotiere joase și late (specific KIVIK)
      if (!s.chaise) RB(.24, .36, ad, .06, sw / 2 - arm / 2, .46, 0, up);
      const seatW = (sw - arm * (s.chaise ? 1 : 2)), n = s.seats || (seatW < .9 ? 1 : seatW < 1.5 ? 2 : 3); const x0 = -sw / 2 + arm;
      for (let i = 0; i < n; i++) RB(seatW / n - .01, .17, ad - .22, .07, x0 + seatW / n * (i + .5), .365, .08, up); // perne șezut
      for (let i = 0; i < n; i++) RB(seatW / n - .02, .44, .2, .09, x0 + seatW / n * (i + .5), .62, -ad / 2 + .15, up); // perne spătar
      RB(sw - .02, .38, .16, .03, 0, .45, -ad / 2 + .08, up);          // spătar
      if (s.chaise){ const cd = d, cx = sw / 2 - .45; RB(.9, .22, cd, .03, cx, .17, (cd - ad) / 2, up); RB(.86, .17, cd - .24, .07, cx, .365, (cd - ad) / 2 + .1, up); }
      g.children.forEach(o => { o.position.z -= (d - ad) / 2 * (s.chaise ? 1 : 0); }); if (s.chaise) g.children.forEach(o => { o.position.z += 0; });
      if (s.chaise) g.position.z = 0; break; }
    case 'coffee': { const c = s.col;
      if (s.type === 'lack'){ const m = MAT('paint', c); B(w, .05, d, 0, h - .025, 0, m); B(w - .1, .02, d - .1, 0, .16, 0, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.05, h - .05, .05, a * (w / 2 - .035), (h - .05) / 2, b * (d / 2 - .035), m)); }
      else if (s.type === 'vittsjo'){ const m = MAT('metal', c); CY(w / 2, w / 2, .008, 0, h - .004, 0, MAT('glass', '#dfeaee'), 48); CY(w / 2 - .02, w / 2 - .02, .01, 0, .12, 0, MAT('paint', '#1f1c1a'), 48); for (let i = 0; i < 4; i++){ const a = i * Math.PI / 2 + Math.PI / 4; CY(.008, .008, h - .01, Math.cos(a) * (w / 2 - .03), (h - .01) / 2, Math.sin(a) * (w / 2 - .03), m, 8); } { const r = new THREE.Mesh(new THREE.TorusGeometry(w / 2 - .03, .007, 6, 48), m); r.rotation.x = Math.PI / 2; r.position.y = h - .02; g.add(r); } }
      else if (s.type === 'hemnes'){ const m = MAT('wood', c); B(w, .04, d, 0, h - .02, 0, m); B(w - .06, .07, d - .06, 0, h - .075, 0, m); B(w - .1, .02, d - .1, 0, .13, 0, MAT('rattan', '#8b6a4a')); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.06, h - .04, .06, a * (w / 2 - .04), (h - .04) / 2, b * (d / 2 - .04), m)); }
      else { const m = MAT('wood', c); RB(w, .035, d, .008, 0, h - .018, 0, m); for (let i = 0; i < 9; i++) B(w - .16, .015, .045, 0, .12, -d / 2 + .08 + i * (d - .16) / 8, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.05, h - .035, .05, a * (w / 2 - .06), (h - .035) / 2, b * (d / 2 - .06), m)); }
      const deco = MAT('paint', '#2f5d50'); CY(.06, .05, .12, w * .18, h + .06, 0, deco, 16); B(.22, .03, .16, -w * .2, h + .015, 0, MAT('paint', '#c8b89a')); break; }
    case 'tv': { const m = body(s.col); if (s.open){ B(w, .02, d, 0, h - .01, 0, m); B(w, .02, d, 0, .01, 0, m); for (let i = 0; i <= 3; i++) B(.02, h, d, -w / 2 + .01 + i * (w - .02) / 3, h / 2, 0, m); B(w, h, .01, 0, h / 2, -d / 2 + .005, m); }
      else { B(w, h - .01, d, 0, h / 2 + .005, 0, m); const n = s.fronts || 3; for (let i = 0; i < n; i++){ B(w / n - .006, h - .03, .016, -w / 2 + w / n * (i + .5), h / 2 + .005, d / 2 + .008, body(s.col)); } }
      B(1.45, .84, .03, 0, h + .02 + .5, -.06, MAT('screen', '#0b0c0e')); B(.32, .02, .2, 0, h + .01, -.06, MAT('metal', '#222')); B(.04, .09, .04, 0, h + .065, -.06, MAT('metal', '#222')); break; }
    case 'shelf': { const m = body(s.col);
      if (s.type === 'kallax'){ const t = .035; B(w, t, d, 0, t / 2, 0, m); B(w, t, d, 0, h - t / 2, 0, m); B(t, h, d, -w / 2 + t / 2, h / 2, 0, m); B(t, h, d, w / 2 - t / 2, h / 2, 0, m); B(.015, h - t * 2, d, 0, h / 2, 0, m);
        for (let i = 1; i < 4; i++) B(w - t * 2, .015, d, 0, t + i * (h - 2 * t) / 4, 0, m);
        for (let r = 0; r < 4; r++) for (let c = 0; c < 2; c++){ const cx = (c ? 1 : -1) * (w / 4 - .005), cy = t + (r + .5) * (h - 2 * t) / 4; if (s.doors && (r + c) % 2 === 0) B(w / 2 - .05, (h - 2 * t) / 4 - .03, .016, cx, cy, d / 2, m); else if ((r * 2 + c) % 3 !== 1) B(.28, .26, .3, cx, cy - .03, 0, MAT('fabric', ['#c9bda8', '#6c7b6f', '#b88f6a'][(r + c) % 3])); } }
      else { B(.018, h, d, -w / 2 + .009, h / 2, 0, m); B(.018, h, d, w / 2 - .009, h / 2, 0, m); B(w, h, .005, 0, h / 2, -d / 2 + .003, m); B(w, .06, d, 0, .03, 0, m);
        for (let i = 0; i < 6; i++){ const y = .06 + i * (h - .08) / 5; B(w - .036, .016, d - .01, 0, y, 0, m); if (i < 5){ let x = -w / 2 + .03; while (x < w / 2 - .06){ const bw = .02 + Math.random() * .03, bh = .19 + Math.random() * .1; B(bw, bh, d - .07, x + bw / 2, y + .008 + bh / 2, .01, MAT('paint', ['#7d3a2e', '#2d4a66', '#c9b27c', '#3c5f4a', '#d8d4cc', '#1f2327'][Math.random() * 6 | 0])); x += bw + .003; } } } }
      break; }
    case 'dining': { const top = s.top, tm = top === '#efefec' ? MAT('paint', top) : MAT('wood', top);
      B(w, .03, d, 0, h - .015, 0, tm); B(w - .08, .06, d - .08, 0, h - .06, 0, tm); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.05, h - .03, .05, a * (w / 2 - .05), (h - .03) / 2, b * (d / 2 - .05), tm));
      const pos = (it.chairs || 4) === 2 ? [[0, -1], [0, 1]] : [[-w * .24, -1], [w * .24, -1], [-w * .24, 1], [w * .24, 1]];
      pos.forEach(([x, sd]) => { const z = sd * (d / 2 + .1), cg = new THREE.Group(); cg.position.set(x, 0, z); cg.rotation.y = sd > 0 ? Math.PI : 0; g.add(cg);
        const add = (bw, bh, bd, px, py, pz, m) => { const o = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, bd), m); o.position.set(px, py, pz); o.castShadow = true; cg.add(o); };
        const fr = s.chair === 'ingolf' || s.chair === 'orrstaWhite' ? MAT('paint', '#f1f1ee') : s.chair === 'rattan' ? MAT('paint', '#1c1c1c') : MAT('wood', '#3f2d22');
        [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => add(.03, .45, .03, a * .19, .225, b * .19, fr));
        if (s.chair === 'rattan'){ add(.42, .03, .42, 0, .46, 0, MAT('rattan', '#b08858')); add(.42, .4, .02, 0, .7, -.2, MAT('rattan', '#b08858')); add(.03, .45, .03, -.19, .7, -.2, fr); add(.03, .45, .03, .19, .7, -.2, fr); }
        else if (s.chair === 'ingolf'){ add(.42, .04, .42, 0, .46, 0, fr); for (let k = -2; k <= 2; k++) add(.018, .42, .018, k * .08, .7, -.2, fr); add(.44, .05, .03, 0, .92, -.2, fr); add(.03, .5, .03, -.2, .7, -.2, fr); add(.03, .5, .03, .2, .7, -.2, fr); }
        else { const cu = MAT('fabric', s.chair === 'orrstaWhite' ? '#d9d9d6' : '#9a9c98'); const o = new THREE.Mesh(rgeo(.43, .06, .43, .02), cu); o.position.set(0, .48, 0); o.castShadow = true; cg.add(o); add(.42, .08, .025, 0, .88, -.2, fr); add(.42, .02, .02, 0, .66, -.2, fr); add(.03, .45, .03, -.19, .7, -.2, fr); add(.03, .45, .03, .19, .7, -.2, fr); } });
      break; }
    case 'bed': { const fr = s.mat ? MAT(s.mat, s.col) : body(s.col), hb = s.mat ? MAT(s.mat, s.col) : s.wood ? MAT('wood', s.col) : MAT('paint', s.col);
      B(w, .3, .02, 0, .2, d / 2 - .01, fr); B(.02, .3, d - .1, -w / 2 + .01, .2, .04, fr); B(.02, .3, d - .1, w / 2 - .01, .2, .04, fr); B(w, h, .05, 0, h / 2, -d / 2 + .025, hb);
      [[-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .05, .03, a * (w / 2 - .03), .025, b * (d / 2 - .03), MAT('paint', '#222')));
      if (s.boxes){ for (let i = 0; i < 2; i++) [-1, 1].forEach(sd => B(.02, .16, .9, sd * (w / 2 + .012), .12, -.35 + i * .95, fr)); }
      RB(w - .1, .2, d - .14, .05, 0, .44, .05, MAT('fabric', '#f7f7f5'));                   // saltea
      RB(w - .08, .07, d * .6, .03, 0, .565, d * .18, MAT('fabric', '#8a9fb1'));               // pilotă
      RB(w - .08, .04, .25, .02, 0, .545, d * .18 - d * .3 + .1, MAT('fabric', '#8a9fb1'));
      [-1, 1].forEach(a => RB(.62, .13, .38, .06, a * (w / 4 - .02), .61, -d / 2 + .3, MAT('fabric', '#ffffff'))); break; }
    case 'night': { const m = body(s.col);
      if (s.type === 'hemnes'){ B(w, .03, d, 0, h - .015, 0, MAT('wood', '#b98f63')); B(w - .02, .22, d - .02, 0, h - .14, 0, m); B(w - .04, .02, d - .04, 0, .2, 0, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.04, h - .03, .04, a * (w / 2 - .025), (h - .03) / 2, b * (d / 2 - .025), m)); CY(.012, .012, .02, 0, h - .14, d / 2, MAT('metal', '#8a7a64'), 10); }
      else if (s.type === 'songesand'){ B(w, h - .06, d, 0, (h - .06) / 2 + .06, 0, m); [0, 1].forEach(i => B(w - .03, (h - .1) / 2 - .01, .015, 0, .08 + (h - .1) / 4 + i * (h - .1) / 2, d / 2 + .008, m)); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .06, .03, a * (w / 2 - .03), .03, b * (d / 2 - .03), m)); }
      else { B(w, h, d, 0, h / 2, 0, m); B(w - .03, .14, .01, 0, h - .1, d / 2 + .005, MAT('frost', '#e9ecec')); B(w - .04, .015, d - .04, 0, h * .4, 0, m); }
      CY(.075, .08, .02, 0, h + .01, 0, MAT('metal', '#2b2b2b')); CY(.01, .01, .3, 0, h + .16, 0, MAT('metal', '#2b2b2b'), 8);
      { const sh = new THREE.Mesh(new THREE.CylinderGeometry(.09, .14, .18, 28, 1, true), new THREE.MeshStandardMaterial({ color: lin('#efe6d6'), side: THREE.DoubleSide, roughness: .9, emissive: lin('#ffd9a0'), emissiveIntensity: .35 })); sh.position.set(0, h + .37, 0); g.add(sh); } break; }
    case 'wardrobe': { const m = body(s.col);
      if (s.type === 'kleppstad'){ B(w, h, d - .04, 0, h / 2, -.02, m); B(w / 2 + .02, h - .06, .02, -w / 4 + .01, h / 2, d / 2 - .03, m); B(w / 2 + .02, h - .06, .02, w / 4 - .01, h / 2, d / 2 - .005, m); B(.012, h - .2, .02, -.06, h / 2, d / 2 - .01, MAT('metal', '#9a9a9a')); B(.012, h - .2, .02, .06, h / 2, d / 2 + .01, MAT('metal', '#9a9a9a')); }
      else { B(w, h - .05, d, 0, (h - .05) / 2 + .05, 0, m); B(w - .04, .05, d - .04, 0, .025, 0, m);
        for (let i = -1; i <= 1; i++){ B(w / 3 - .006, h - .08, .018, i * w / 3, h / 2 + .02, d / 2 + .009, i === 0 ? MAT('mirror', '#dfe6ea') : body(s.col)); if (i !== 0) CY(.012, .012, .025, i * w / 3 + (i < 0 ? .15 : -.15), h * .52, d / 2 + .03, MAT('metal', '#8f8f8f'), 12).rotation.x = Math.PI / 2; } }
      break; }
    case 'desk': { const m = MAT('paint', '#f2f2ef');
      if (s.type === 'torald'){ B(w, .02, d, 0, h - .01, 0, m); [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => CY(.012, .012, h - .02, a * (w / 2 - .03), (h - .02) / 2, b * (d / 2 - .03), MAT('metal', '#e8e8e6'), 10)); }
      else { B(w, .03, d, 0, h - .015, 0, m); B(.02, h - .03, d, -w / 2 + .01, (h - .03) / 2, 0, m); B(.35, h - .03, d, w / 2 - .175, (h - .03) / 2, 0, m); for (let i = 0; i < 2; i++) B(.33, .2, .01, w / 2 - .175, h - .15 - i * .25, d / 2 + .005, m); B(w - .37, .3, .01, -.17, h - .3, -d / 2 + .01, m); }
      B(.54, .32, .02, -w * .12, h + .23, -d / 2 + .12, MAT('screen', '#0c0d0f')); B(.05, .12, .05, -w * .12, h + .06, -d / 2 + .12, MAT('metal', '#333')); B(.42, .015, .14, -w * .12, h + .008, .06, MAT('paint', '#2b2d31')); break; }
    case 'ochair': { const fab = MAT('fabric', '#3b3d42'), mesh = MAT('paint', '#1d1e21');
      for (let i = 0; i < 5; i++){ const a = i / 5 * Math.PI * 2, leg = B(.3, .03, .04, Math.cos(a) * .15, .07, Math.sin(a) * .15, mesh); leg.rotation.y = -a; CY(.025, .025, .04, Math.cos(a) * .3, .025, Math.sin(a) * .3, mesh, 10); }
      CY(.028, .028, .38, 0, .28, 0, MAT('chrome', '#bbb')); RB(.5, .09, .5, .03, 0, .5, .02, fab); RB(.5, h - .75, .06, .02, 0, .55 + (h - .75) / 2 + .1, -.23, mesh); RB(.3, .12, .06, .02, 0, h - .08, -.24, fab);
      [-1, 1].forEach(a => B(.04, .2, .25, a * .27, .64, 0, mesh)); break; }
    case 'kitchen': { const k = s.k || null, frame = MAT('metal', s.frame || '#34363a'), wd = .6;
      // sistemul de bucătărie (core/kitchen.ts): finisajul fronturilor, mânerele, blatul, suspendatele, placarea, LED-ul
      const fcol = k ? k.frontColor : (s.fronts || '#f4f4f1'), front = k && k.frontFinish === 'gloss' ? MAT('ceramic', fcol) : k && k.frontFinish === 'wood' ? MAT('wood', fcol) : MAT('paint', fcol);
      const hmat = MAT('metal', k ? k.handleColor : '#222'), htype = k ? k.handle : 'bar', topM = k ? k.topM : .038, topMat = k ? (k.topWood ? MAT('wood', k.topColor) : MAT('ceramic', k.topColor)) : MAT('wood', '#cdb592');
      const handle = (x, y, z, vertical) => { if (htype === 'none') return; if (htype === 'knob') { CY(.012, .012, .025, x, y, z + .01, hmat, 12).rotation.x = Math.PI / 2; return; }
        if (htype === 'profile') B(vertical ? .015 : .5, vertical ? .5 : .015, .02, x, y, z, hmat); else B(vertical ? .012 : .18, vertical ? .18 : .012, .015, x, y, z, hmat); };
      B(w, .08, wd - .05, 0, .04, -d / 2 + wd / 2, MAT('paint', '#2b2c2f')); B(w, .78, wd - .02, 0, .47, -d / 2 + wd / 2, MAT('paint', '#2d2f33'));
      const n = Math.round(w / .6); for (let i = 0; i < n; i++){ const cx = -w / 2 + w / n * (i + .5); B(w / n - .006, .74, .018, cx, .46, -d / 2 + wd + .009, front); handle(cx, htype === 'profile' ? .82 : .78, -d / 2 + wd + .025, false); }
      const topY = .86 + topM / 2; B(w + .01, topM, d, 0, topY, 0, topMat); // blat
      const sinkMat = k ? MAT(lumOf(k.sinkColor) > .55 ? 'chrome' : 'paint', k.sinkColor) : MAT('chrome', '#c6c9cc'), tapMat = MAT('chrome', k ? k.tapColor : '#c6c9cc');
      B(.46, .015, .4, -w / 2 + .45, .86 + topM + .002, 0, sinkMat); CY(.015, .015, .25, -w / 2 + .45, .86 + topM + .14, -d / 2 + .08, tapMat, 10);
      B(.58, .006, .5, w / 2 - .55, .86 + topM + .003, 0, MAT('screen', '#0d0d0e')); B(.56, .56, .012, w / 2 - .55, .46, -d / 2 + wd + .02, MAT('screen', '#141517'));
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
      // walk-in: fără cădiță și fără ușă, doar un panou fix de sticlă cu profil sus și o bară de rigidizare
      if (bm && bm.walkin){ const p = new THREE.Mesh(new THREE.BoxGeometry(.008, h - .06, d * .8), gl); p.position.set(w / 2 - .004, h / 2 + .01, -d * .1); g.add(p); B(.02, .02, d * .8, w / 2 - .004, h - .02, -d * .1, pm);
        B(w * .6, .004, .06, w * .1, .003, d / 2 - .05, MAT('metal', '#444')); CY(.012, .012, h - .4, -w / 2 + .12, h / 2 + .1, -d / 2 + .04, fit, 10); CY(.12, .12, .012, -w / 2 + .25, h - .08, -d / 2 + .25, fit, 24); break; }
      B(w, .05, d, 0, .025, 0, MAT('ceramic', '#fafafa'));
      if (s.round){ const arc = new THREE.Mesh(new THREE.CylinderGeometry(w - .02, w - .02, h - .06, 32, 1, true, 0, Math.PI / 2), gl); arc.position.set(-w / 2 + .01, h / 2 + .03, -d / 2 + .01); g.add(arc); }
      else { const t = s.thick ? .03 : .02; [[w, .008, 0, d / 2 - .004], [.008, d, w / 2 - .004, 0]].forEach(([a, b, x, z]) => { const p = new THREE.Mesh(new THREE.BoxGeometry(a, h - .06, b), gl); p.position.set(x, h / 2 + .03, z); g.add(p); });
        B(t, h - .05, t, w / 2 - t / 2, h / 2, d / 2 - t / 2, pm); B(w, t, t, 0, h - t / 2, d / 2 - t / 2, pm); B(t, t, d, w / 2 - t / 2, h - t / 2, 0, pm); }
      CY(.012, .012, h - .4, -w / 2 + .12, h / 2 + .1, -d / 2 + .04, fit, 10); CY(.1, .1, .015, -w / 2 + .2, h - .1, -d / 2 + .2, fit, 24); break; }
    case 'vanity': { const m = body(s.col), y0 = s.hang ? .3 : 0; if (s.legs) [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .15, .03, a * (w / 2 - .04), .075, b * (d / 2 - .04), MAT('metal', '#222')));
      const hb = (s.hang ? h - .15 : h - .15) - (s.legs ? .15 : 0); B(w, hb, d, 0, y0 + (s.legs ? .15 : 0) + hb / 2, 0, m); B(w / 2 - .01, hb - .03, .015, -w / 4, y0 + (s.legs ? .15 : 0) + hb / 2, d / 2 + .008, m); B(w / 2 - .01, hb - .03, .015, w / 4, y0 + (s.legs ? .15 : 0) + hb / 2, d / 2 + .008, m);
      const ty = y0 + (s.legs ? .15 : 0) + hb; RB(w + .01, .14, d + .06, .03, 0, ty + .07, .03, MAT('ceramic', '#fdfdfd')); CY(.012, .012, .15, 0, ty + .2, -d / 2 + .05, MAT('chrome', s.b ? s.b.metal : '#cfcfcf'), 10);
      B(w * .85, .6, .02, 0, 1.55, -d / 2 + .01, MAT('mirror', '#dde6ea')); if (s.legs) B(w * .6, .04, .06, 0, 1.9, -d / 2 + .04, MAT('paint', '#f4f4f1')); break; }
    case 'wc': { const cer = MAT('ceramic', '#fdfdfd'), hung = s.b && s.b.wallHung, lift = hung ? .1 : 0;
      // WC suspendat: vasul ridicat de la pardoseală, fără picior; clapeta în culoarea armăturilor
      if (s.square || hung){ RB(.36, .3, .52, .06, 0, .27 + lift, .05, cer); } else { const b = new THREE.Mesh(new THREE.CylinderGeometry(.18, .13, .32, 32), cer); b.scale.z = 1.4; b.position.set(0, .24, .06); b.castShadow = true; g.add(b); }
      RB(.37, .03, .5, .015, 0, .42 + lift, .06, cer); B(.42, .9, .12, 0, .75, -d / 2 + .06, MAT('paint', '#efefec')); B(.24, .16, .01, 0, 1.0, -d / 2 + .125, MAT('chrome', s.b ? s.b.metal : '#d4d4d4')); break; }
    case 'shoe': { const m = body(s.col); B(w, h - .04, d, 0, (h - .04) / 2 + .04, 0, m); B(w + .02, .025, d + .02, 0, h - .012, 0, m); [[-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .04, .03, a * (w / 2 - .03), .02, b * (d / 2 - .03), m));
      const rows = s.two ? 2 : 2, cols = s.two ? 1 : 2; for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++){ const fw = w / cols - .01, fh = (h - .1) / rows - .015, x = -w / 2 + w / cols * (c + .5), y = .06 + (h - .1) / rows * (r + .5); B(fw, fh, .018, x, y, d / 2 + .009, m); CY(.012, .012, .02, x, y + fh / 2 - .05, d / 2 + .025, MAT('metal', '#7c6a55'), 10).rotation.x = Math.PI / 2; }
      break; }
    case 'mirror': { const m = MAT('paint', '#f3f3f0'), y0 = s.wall ? .95 : 0; B(w, h, d, 0, y0 + h / 2, 0, m); B(w - .06, h - .08, .01, 0, y0 + h / 2, d / 2 + .006, MAT('mirror', '#dfe6ea')); break; }
  }
  return g;
}


  // ---------- casa (reconstruită la fiecare schimbare de plan) ----------
  let house = new THREE.Group(), ceilings = new THREE.Group(), walls = new THREE.Group(), furniture = new THREE.Group(); scene.add(house, furniture);
  let colliders = [], blockers = [], structBlockers = [], plan = null, W = 1, D = 1, C = new THREE.Vector3(), pickables = [];
  // la reconstruire: geometria și texturile clonate pentru finisaje (fiecare clonă e o încărcare separată pe GPU)
  const disposeGroup = g => g.traverse(o => { if (o.geometry) o.geometry.dispose(); const m = o.material; if (m && m.map && m.map.userData && m.map.userData.owned){ m.map.dispose(); m.dispose(); } else if (m && m.userData && m.userData.ownedMat) m.dispose(); });
  const owned = m => { m.userData = { ...(m.userData || {}), ownedMat: true }; return m; };
  function box(w, h, d, mat, x, y, z, rotY, parent){ const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat); m.position.set(x, y, z); m.rotation.y = rotY || 0; m.castShadow = m.receiveShadow = true; parent.add(m); return m; }
  function buildHouse(p){
    disposeGroup(house); scene.remove(house); interiorLights.length = 0; house = new THREE.Group(); ceilings = new THREE.Group(); walls = new THREE.Group(); house.add(ceilings, walls); scene.add(house); colliders = [];
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
        else { t = (wet ? tiles : parquet).clone(); t.needsUpdate = true; t.repeat.set(w / sc, d / sc); t.offset.set((q.x0 - r.x0) / sc, (r.z1 - q.z1) / sc); }
        const fmat = new THREE.MeshStandardMaterial({ map: t, roughness: tileFloor ? .35 : .6, color: lin(r.podea || '#ffffff') });
        if (ft) fmat.userData = { kind: ff.kind === 'tile' ? 'tile' : 'parquet', pat: { ...ff, rect: [r.x0, r.z0, r.x1, r.z1] } };
        const f = new THREE.Mesh(new THREE.PlaneGeometry(w, d), fmat); f.rotation.x = -Math.PI / 2; f.position.set((q.x0 + q.x1) / 2, 0, (q.z0 + q.z1) / 2); f.receiveShadow = true; house.add(f); }
      // covorul camerei, centrat, cu latura lungă după cameră (sau rotit)
      if (r.fin && r.fin.rug){ const rg = r.fin.rug, rm = MAT('fabric', rg.color); const o = box(rg.w, .012, rg.d, rm, (r.x0 + r.x1) / 2, .006, (r.z0 + r.z1) / 2, 0, house); o.castShadow = false; }
      // tavan: drept la H; fals coborât cu `drop`; cu scafă: banda de lângă pereți rămâne sus, panoul din mijloc coboară, cu bandă LED pe margine
      const cv = r.fin && r.fin.ceiling, cmat = r.tavan ? lookMat('ceil', r.tavan, 1) : ceilMat, ctop = cv && cv.type === 'drop' ? H - cv.drop : H;
      const ceilPlane = (rect, y) => { for (const q of rectsMinus(rect, stairs.map(a => a.rect))){ const w = q.x1 - q.x0, d = q.z1 - q.z0;
        const c = new THREE.Mesh(new THREE.PlaneGeometry(w, d), cmat); c.rotation.x = Math.PI / 2; c.position.set((q.x0 + q.x1) / 2, y, (q.z0 + q.z1) / 2); ceilings.add(c); } };
      ceilPlane(r, ctop);
      const low = cv && cv.type !== 'flat' ? H - cv.drop : H;
      if (cv && cv.type === 'cove'){ const k = cv.cove, inner = { x0: r.x0 + k, z0: r.z0 + k, x1: r.x1 - k, z1: r.z1 - k };
        if (inner.x1 - inner.x0 > .3 && inner.z1 - inner.z0 > .3){ ceilPlane(inner, low); const iw = inner.x1 - inner.x0, id = inner.z1 - inner.z0, cx = (inner.x0 + inner.x1) / 2, cz = (inner.z0 + inner.z1) / 2;
          [[iw, .012, cx, inner.z0], [iw, .012, cx, inner.z1], [.012, id, inner.x0, cz], [.012, id, inner.x1, cz]].forEach(([a, b, x, z]) => { const m = box(a, cv.drop, b, cmat, x, H - cv.drop / 2, z, 0, ceilings); m.castShadow = false; });
          if (cv.led){ const lm = new THREE.MeshStandardMaterial({ color: lin(cv.led), emissive: lin(cv.led), emissiveIntensity: 1.6 });
            [[iw, .015, cx, inner.z0 - .02], [iw, .015, cx, inner.z1 + .02], [.015, id, inner.x0 - .02, cz], [.015, id, inner.x1 + .02, cz]].forEach(([a, b, x, z]) => { const m = box(a, .012, b, lm, x, low + .01, z, 0, ceilings); m.castShadow = false; });
            const glow = new THREE.MeshStandardMaterial({ color: lin('#ffffff'), emissive: lin(cv.led), emissiveIntensity: .35, roughness: 1 });
            [[r.x1 - r.x0, k, (r.x0 + r.x1) / 2, r.z0 + k / 2], [r.x1 - r.x0, k, (r.x0 + r.x1) / 2, r.z1 - k / 2], [k, id, r.x0 + k / 2, cz], [k, id, r.x1 - k / 2, cz]].forEach(([a, b, x, z]) => {
              const g2 = new THREE.Mesh(new THREE.PlaneGeometry(a, b), glow); g2.rotation.x = Math.PI / 2; g2.position.set(x, H - .002, z); ceilings.add(g2); }); } } }
      if (cv && cv.cornice){ const [cd, ch] = cv.cornice, e = .075 + cd / 2, y = (cv.type === 'drop' ? H - cv.drop : H) - ch / 2, cm2 = lookMat('cornice', '#f6f5f1', .7);
        [[r.x1 - r.x0, cd, (r.x0 + r.x1) / 2, r.z0 + e], [r.x1 - r.x0, cd, (r.x0 + r.x1) / 2, r.z1 - e], [cd, r.z1 - r.z0, r.x0 + e, (r.z0 + r.z1) / 2], [cd, r.z1 - r.z0, r.x1 - e, (r.z0 + r.z1) / 2]]
          .forEach(([a, b, x, z]) => { box(a, ch, b, cm2, x, y, z, 0, ceilings).castShadow = false; }); }
      if (cv && cv.spots > 0){ const n = cv.spots, rw2 = r.x1 - r.x0, rd2 = r.z1 - r.z0, cols = Math.max(1, Math.round(Math.sqrt(n * rw2 / rd2))), rows = Math.ceil(n / cols), sm = new THREE.MeshStandardMaterial({ color: lin('#fff6e6'), emissive: lin('#ffd9a8'), emissiveIntensity: 1.4 });
        for (let i = 0; i < n; i++){ const cx2 = r.x0 + rw2 * ((i % cols) + .5) / cols, cz2 = r.z0 + rd2 * (Math.floor(i / cols) + .5) / rows, s2 = new THREE.Mesh(new THREE.CylinderGeometry(.04, .04, .008, 20), sm); s2.position.set(cx2, (cv.type === 'cove' ? low : ctop) - .004, cz2); ceilings.add(s2); } }
      const w = r.x1 - r.x0, d = r.z1 - r.z0;
      const pl = new THREE.PointLight(0xfff0dc, light ? light.interior : .35, 7, 2); interiorLights.push(pl); pl.position.set((r.x0 + r.x1) / 2, H - .3, (r.z0 + r.z1) / 2); ceilings.add(pl); });
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
    const leafMat = MAT('paint', '#f4f2ed'), entranceMat = MAT('wood', '#4a3324'), handleMat = MAT('chrome', '#d8d8d8');
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
      const piece = (s0, s1, y0, y1) => { if (s1 - s0 < .005 || y1 - y0 < .005) return; const m = (s0 + s1) / 2; box(s1 - s0, y1 - y0, th, matAt(m), ax + ux * m, (y0 + y1) / 2, az + uz * m, rot, walls); if (y0 === 0) skirting(s0, s1, m); };
      const seg = (s0, s1, y0, y1) => { let q0 = s0; for (const t of breaks){ if (t > q0 + .005 && t < s1 - .005){ piece(q0, t, y0, y1); q0 = t; } } piece(q0, s1, y0, y1); };
      gs.forEach(g => { seg(q, g.la, 0, H);
        if (g.tip === 'usa'){ const dh = Math.min(H - .05, g.h || 2.1), fm = g.culoare ? lookMat('frame', g.culoare, .5) : frameMat; seg(g.la, g.la + g.l, dh, H); const m = g.la + g.l / 2; [g.la, g.la + g.l].forEach(s => box(.05, dh, th + .02, fm, ax + ux * s, dh / 2, az + uz * s, rot, walls)); box(g.l, .05, th + .02, fm, ax + ux * m, dh, az + uz * m, rot, walls);
          // foaia ușii, deschisă ~75° spre interior (opusul normalei exterioare, ca arcul din DXF), balamaua la începutul golului
          const cmx = (pMinX + pMaxX) / 2, cmz = (pMinZ + pMaxZ) / 2, mx = ax + ux * m, mz = az + uz * m, sIn = ((mx - cmx) * nx + (mz - cmz) * nz) > 0 ? -1 : 1;
          const R = rot - sIn * 1.31, dx = Math.cos(R), dz = -Math.sin(R), lw = Math.max(.3, g.l - .06), hx = ax + ux * (g.la + .03), hz = az + uz * (g.la + .03), lh = dh - .015;
          // ușa aleasă din catalog: culoarea foii, geam mat sau panouri în relief, culoarea mânerului
          const us = g.usa, lm = us && us.color ? (us.wood ? MAT('wood', us.color) : MAT('paint', us.color)) : (g.intrare ? entranceMat : leafMat), hm = us && us.handle ? MAT('chrome', us.handle) : handleMat;
          const cxL = hx + dx * lw / 2, czL = hz + dz * lw / 2, px = -dz, pz = dx;
          box(lw, lh, .04, lm, cxL, lh / 2, czL, R, walls);
          if (us && us.style === 'glass') [1, -1].forEach(sg => { box(lw * .55, lh * .55, .006, MAT('frost', '#e9ecec'), cxL + px * sg * .022, lh * .58, czL + pz * sg * .022, R, walls).castShadow = false; });
          if (us && us.style === 'panel') [1, -1].forEach(sg => { [lh * .72, lh * .3].forEach((y, i) => box(lw * .62, lh * (i ? .36 : .3), .008, lm, cxL + px * sg * .024, y, czL + pz * sg * .024, R, walls).castShadow = false); });
          const kx = hx + dx * (lw - .07), kz = hz + dz * (lw - .07);
          [1, -1].forEach(sg => box(.12, .02, .02, hm, kx + px * sg * .045, 1.02, kz + pz * sg * .045, R, walls).castShadow = false); }
        else { const sl = g.sill != null ? g.sill : .9, top = Math.min(H - .05, sl + (g.h || 1.3)), wh = top - sl, cy = (sl + top) / 2, fm = g.culoare ? lookMat('frame', g.culoare, .5) : frameMat;
          seg(g.la, g.la + g.l, 0, sl); seg(g.la, g.la + g.l, top, H); const m = g.la + g.l / 2; box(g.l, wh, .02, glass, ax + ux * m, cy, az + uz * m, rot, walls).castShadow = false;
          box(g.l, .05, th + .04, fm, ax + ux * m, sl, az + uz * m, rot, walls); box(g.l, .05, th + .02, fm, ax + ux * m, top, az + uz * m, rot, walls); box(.04, wh, th + .02, fm, ax + ux * m, cy, az + uz * m, rot, walls);
          if (g.trat){ const tr = g.trat, sg = tr.inward, at = (s, d, y, w2, h2, dp, mat) => { const px = ax + ux * s + nx * sg * d, pz = az + uz * s + nz * sg * d; const o = box(w2, h2, dp, mat, px, y, pz, rot, walls); o.castShadow = false; return o; };
            // stor în golul ferestrei, pe partea camerei: rulou (coborât o treime), roman (pliuri orizontale) sau jaluzea venețiană (lamele)
            if (tr.blind){ const bm = lookMat('blind', tr.blind.color, .85), bw = g.l - .04, bh = wh * .38, d0 = th / 2 + .02;
              at(m, d0, top - .04, bw + .04, .07, .07, bm);
              if (tr.blind.kind === 'venetian'){ for (let yv = top - .1; yv > top - wh + .02; yv -= .025) at(m, d0, yv, bw, .004, .025, bm); }
              else { at(m, d0, top - .07 - bh / 2, bw, bh, .006, bm); if (tr.blind.kind === 'roman') for (let k = 1; k <= 3; k++) at(m, d0 + .006, top - .07 - bh * k / 3.2, bw, .012, .012, bm); } }
            // bara deasupra golului, depășind fereastra cu 20 cm pe fiecare parte; perdeaua trasă pe toată lățimea, draperiile strânse la capete
            if (tr.curtain || tr.sheer){ const rw = g.l + .4, d1 = th / 2 + .13, y0 = .01, y1 = tr.rodH, hh = y1 - y0;
              at(m, d1, y1 + .01, rw + .06, .025, .025, lookMat('rod', '#2b2b2b', .4));
              if (tr.sheer){ const sm = new THREE.MeshStandardMaterial({ color: lin(tr.sheer.color), transparent: true, opacity: .45, roughness: .9, side: THREE.DoubleSide, depthWrite: false });
                for (let k = 0; k < Math.ceil(rw / .08); k++) at(m - rw / 2 + .04 + k * .08, d1 - .03 + (k % 2 ? .015 : 0), y0 + hh / 2, .085, hh, .004, sm); }
              if (tr.curtain){ const cm = MAT('fabric', tr.curtain.color), stack = .34;
                [-1, 1].forEach(e => { for (let k = 0; k < 6; k++) at(m + e * (rw / 2 - stack / 2) - stack / 2 + .03 + k * (stack - .06) / 5, d1 + .03 + (k % 2 ? .025 : 0), y0 + hh / 2, .07, hh, .012, cm); }); } } } }
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
    orbit.r = Math.max(W, D, 4) * 1.15; setMode(mode);
  }
  function buildFurniture(items){ disposeGroup(furniture); scene.remove(furniture); furniture = new THREE.Group(); scene.add(furniture); pickables = []; blockers = [];
    for (const it of items){ if (!it.variant || !it.variant.w) continue; const m = model(it.variant); m.position.set(it.x, 0, it.z); m.rotation.y = it.rotation; m.userData = { pid: it.id }; m.traverse(o => { o.userData.item = m; }); furniture.add(m); pickables.push(m);
      if (it.group !== 'scaunBirou' && it.fp) blockers.push(it.fp); } }

  // soarele stă pe direcția dată (azimut, înălțime), la ~14 m de centrul casei; implicit ca în prototip
  function placeSun(){ const dir = light && light.dir ? light.dir : null; if (dir) sun.position.set(C.x + dir[0] * 14, dir[1] * 14, C.z + dir[2] * 14); else sun.position.set(C.x + 6, 11, C.z - 8); }
  function setLighting(p){ light = p; const bg = new THREE.Color(p.background); scene.background = bg; scene.fog.color = bg;
    hemi.intensity = p.hemi; sun.color.set(p.sunColor); sun.intensity = p.sun; R.toneMappingExposure = p.exposure;
    interiorLights.forEach(l => { l.intensity = p.interior; }); placeSun(); }
  // captură PNG: randează cadrul curent și îl citește imediat (fără preserveDrawingBuffer)
  function capture(){ draw(); return canvas.toDataURL('image/png'); }
  // imagini fixe pentru export: ansamblu (machetă) sau o cameră văzută din colțul ei cel mai liber, la înălțimea ochilor
  function renderView(view, w = 1200, h = 800){ if (!plan) return null;
    const prevMode = mode, prevSize = new THREE.Vector2(); R.getSize(prevSize); const prevAspect = cam.aspect;
    R.setSize(w, h, false); if (composer) composer.setSize(w, h); cam.aspect = w / h; cam.updateProjectionMatrix();
    if (view && view.roomId){ const r = plan.camere.find(x => x.id === view.roomId); if (!r){ R.setSize(prevSize.x, prevSize.y, false); if (composer) composer.setSize(prevSize.x, prevSize.y); cam.aspect = prevAspect; cam.updateProjectionMatrix(); return null; }
      setMode('walk'); const inset = .35, corners = [[r.x0 + inset, r.z0 + inset], [r.x1 - inset, r.z0 + inset], [r.x1 - inset, r.z1 - inset], [r.x0 + inset, r.z1 - inset]];
      const blocked = ([x, z]) => blockers.some(b => x > b.x0 - .2 && x < b.x1 + .2 && z > b.z0 - .2 && z < b.z1 + .2);
      const pick = corners.find(c => !blocked(c)) || corners[0]; const opp = [r.x0 + r.x1 - pick[0], r.z0 + r.z1 - pick[1]];
      cam.fov = 75; cam.updateProjectionMatrix(); cam.position.set(pick[0], 1.65, pick[1]); cam.lookAt(opp[0], .85, opp[1]);
    } else { setMode('house'); const t = { th: -.7, ph: .85, r: Math.max(W, D, 4) * 1.25 };
      cam.position.set(C.x + Math.sin(t.th) * Math.sin(t.ph) * t.r, Math.cos(t.ph) * t.r, C.z + Math.cos(t.th) * Math.sin(t.ph) * t.r); cam.lookAt(C); }
    draw(); const url = canvas.toDataURL('image/png');
    cam.fov = 68; R.setSize(prevSize.x, prevSize.y, false); if (composer) composer.setSize(prevSize.x, prevSize.y); cam.aspect = prevAspect; cam.updateProjectionMatrix(); setMode(prevMode); return url; }

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
    try { cc.position.set((r.x0 + r.x1) / 2, 1.6, (r.z0 + r.z1) / 2); scene.add(cc); cc.update(R, scene);
      R.setPixelRatio(1); R.setSize(w, w / 2, false); R.render(qs, new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1)); return canvas.toDataURL('image/jpeg', .92); }
    finally { scene.remove(cc); quad.geometry.dispose(); mat.dispose(); rt.dispose(); R.setPixelRatio(prevRatio); R.setSize(prevSize.x, prevSize.y, false); if (composer) composer.setSize(prevSize.x, prevSize.y); setMode(prevMode); } }

  // ---------- navigare ----------
  let mode = 'house', yaw = 0, pitch = -.08; const EYE = 1.6, player = new THREE.Vector3(); const orbit = { th: -.7, ph: .95, r: 12 };
  function collide(x, z){ const r = .24;
    for (const c of colliders){ const px = x - c.ax, pz = z - c.az, s = Math.max(c.s0, Math.min(c.s1, px * c.ux + pz * c.uz)), cx = c.ax + c.ux * s, cz = c.az + c.uz * s, dx = x - cx, dz = z - cz, dd = Math.hypot(dx, dz);
      if (dd < r + .07 && dd > 1e-6){ const k = (r + .07 - dd) / dd; x += dx * k; z += dz * k; } }
    for (const b of [...structBlockers, ...blockers]){ if (x > b.x0 - r && x < b.x1 + r && z > b.z0 - r && z < b.z1 + r){ const dl = x - (b.x0 - r), dr = (b.x1 + r) - x, du = z - (b.z0 - r), dn = (b.z1 + r) - z, m = Math.min(dl, dr, du, dn);
      if (m === dl) x = b.x0 - r; else if (m === dr) x = b.x1 + r; else if (m === du) z = b.z0 - r; else z = b.z1 + r; } }
    return [x, z]; }
  // intri în cameră prin ușă, cu privirea spre centrul ei (ca în prototip)
  function goRoom(id){ if (!plan) return; const room = plan.camere.find(r => r.id === id); if (!room) return; setMode('walk');
    const cx = (room.x0 + room.x1) / 2, cz = (room.z0 + room.z1) / 2; let spot = null;
    for (const w of plan.pereti){ const [ax, az] = w.a, [bx, bz] = w.b, horiz = Math.abs(az - bz) < 1e-6, L = Math.hypot(bx - ax, bz - az); if (L < .01) continue;
      for (const g of w.goluri){ if (g.tip !== 'usa') continue; const m = g.la + g.l / 2, px = ax + (bx - ax) / L * m, pz = az + (bz - az) / L * m;
        if (horiz && Math.abs(pz - room.z0) < 1e-6 && px > room.x0 && px < room.x1) spot = [px, room.z0 + .55];
        else if (horiz && Math.abs(pz - room.z1) < 1e-6 && px > room.x0 && px < room.x1) spot = [px, room.z1 - .55];
        else if (!horiz && Math.abs(px - room.x0) < 1e-6 && pz > room.z0 && pz < room.z1) spot = [room.x0 + .55, pz];
        else if (!horiz && Math.abs(px - room.x1) < 1e-6 && pz > room.z0 && pz < room.z1) spot = [room.x1 - .55, pz];
        if (spot) break; } if (spot) break; }
    const [x, z] = collide(...(spot || [cx, cz])); player.set(x, 0, z); yaw = Math.atan2(-(cx - x), -(cz - z)); pitch = -.08; }
  // macheta taie pereții la 42% din înălțime; aceeași tăietură se aplică și mobilierului (dulapuri înalte, biblioteci),
  // altfel ar ieși deasupra pereților și ar părea în afara casei
  const cut = new THREE.Plane(new THREE.Vector3(0, -1, 0), 1.1);
  function setMode(m){ mode = m; walls.scale.y = m === 'walk' ? 1 : .42; ceilings.visible = m === 'walk'; cut.constant = (plan ? plan.inaltime : 2.6) * .42 + .01; R.clippingPlanes = m === 'walk' ? [] : [cut]; }
  const keys = {}; const joy = { f: 0, s: 0 }; // joystick virtual: axe în [-1, 1], însumate cu tastele
  const kd = e => { if (e.target.closest && e.target.closest('input,select,textarea')) return; keys[e.key.toLowerCase()] = true; }, ku = e => { keys[e.key.toLowerCase()] = false; };
  addEventListener('keydown', kd); addEventListener('keyup', ku);
  let drag = false, lx = 0, ly = 0, moved = 0; const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
  const pd = e => { drag = true; lx = e.clientX; ly = e.clientY; moved = 0; canvas.setPointerCapture(e.pointerId); };
  const pmv = e => { if (!drag) return; const dx = e.clientX - lx, dy = e.clientY - ly; lx = e.clientX; ly = e.clientY; moved += Math.abs(dx) + Math.abs(dy);
    if (mode === 'walk'){ yaw -= dx * .0045; pitch = Math.max(-1.2, Math.min(1.1, pitch - dy * .0035)); } else { orbit.th -= dx * .006; orbit.ph = Math.max(.12, Math.min(1.45, orbit.ph - dy * .005)); } };
  const pu = e => { drag = false; if (moved < 6 && onPick){ const r = canvas.getBoundingClientRect(); ndc.set((e.clientX - r.left) / r.width * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1); ray.setFromCamera(ndc, cam);
    const hit = ray.intersectObjects(pickables, true)[0]; onPick(hit ? hit.object.userData.item.userData.pid : null); } };
  const wh = e => { if (mode === 'house'){ orbit.r = Math.max(4, Math.min(40, orbit.r + e.deltaY * .01)); e.preventDefault(); } };
  canvas.addEventListener('pointerdown', pd); canvas.addEventListener('pointermove', pmv); canvas.addEventListener('pointerup', pu); canvas.addEventListener('wheel', wh, { passive: false });
  let raf = 0, last = performance.now(), alive = true;
  function resize(){ const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1; R.setSize(w, h, false); if (composer) composer.setSize(w, h); cam.aspect = w / h; cam.updateProjectionMatrix(); }
  const ro = new ResizeObserver(resize); ro.observe(canvas);
  (function loop(){ if (!alive) return; raf = requestAnimationFrame(loop); const now = performance.now(), dt = Math.min(.05, (now - last) / 1000); last = now;
    if (mode === 'walk'){ let f = 0, s = 0; if (keys.w || keys.arrowup) f += 1; if (keys.s || keys.arrowdown) f -= 1; if (keys.a || keys.arrowleft) s -= 1; if (keys.d || keys.arrowright) s += 1; f = Math.max(-1, Math.min(1, f + joy.f)); s = Math.max(-1, Math.min(1, s + joy.s));
      if (f || s){ const sp = 1.7 * dt, fx = -Math.sin(yaw), fz = -Math.cos(yaw), rx = Math.cos(yaw), rz = -Math.sin(yaw); const [x, z] = collide(player.x + (fx * f + rx * s) * sp, player.z + (fz * f + rz * s) * sp); player.x = x; player.z = z; }
      cam.position.set(player.x, EYE, player.z); cam.rotation.order = 'YXZ'; cam.rotation.set(pitch, yaw, 0);
    } else { const t = new THREE.Vector3(C.x + Math.sin(orbit.th) * Math.sin(orbit.ph) * orbit.r, Math.cos(orbit.ph) * orbit.r, C.z + Math.cos(orbit.th) * Math.sin(orbit.ph) * orbit.r); cam.position.lerp(t, reduce ? 1 : Math.min(1, dt * 5)); cam.lookAt(C); }
    draw(); })();
  // ---------- scena pentru randarea fotorealistă (tools/blender/casa3d_render.py) ----------
  // Aceleași primitive ca vederea 3D (cutii, cutii rotunjite, cilindri, plăci, tor), cu poziția în lume și materialul
  // lor: tipul (lemn, textil, sticlă...) se deduce din textura și parametrii PBR, iar valorile brute merg și ele.
  function exportScene(meta = {}){ if (!plan) return null; const prevMode = mode; setMode('walk'); scene.updateMatrixWorld(true);
    const r4 = v => Math.round(v * 1e4) / 1e4, hex = c => '#' + c.clone().convertLinearToSRGB().getHexString();
    const mats = [], matIx = new Map(), meshes = [], P = new THREE.Vector3(), Q = new THREE.Quaternion(), Sc = new THREE.Vector3();
    const kindOf = m => { if (m.userData && m.userData.kind) return m.userData.kind; const img = m.map && m.map.image;
      if (img && img === parquet.image) return 'parquet'; if (img && img === tiles.image) return 'tile';
      if (m.map === grain) return 'wood'; if (m.map === rattan) return 'rattan'; if (m.map === weave) return m.sheen ? 'velvet' : 'fabric'; if (m.bumpMap === weave) return 'leather';
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
      switch (g.type){
        case 'BoxGeometry': case 'BoxBufferGeometry': return { t: 'box', p: [p.width, p.height, p.depth].map(r4) };
        case 'PlaneGeometry': case 'PlaneBufferGeometry': return { t: 'plane', p: [p.width, p.height].map(r4) };
        case 'CylinderGeometry': case 'CylinderBufferGeometry': return { t: 'cyl', p: [p.radiusTop, p.radiusBottom, p.height, p.radialSegments, p.openEnded ? 1 : 0, p.thetaStart, p.thetaLength].map(r4) };
        case 'TorusGeometry': case 'TorusBufferGeometry': return { t: 'torus', p: [p.radius, p.tube, p.radialSegments, p.tubularSegments, p.arc].map(r4) };
        default: return null; } };
    for (const root of [house, furniture]) root.traverse(o => { if (!o.isMesh || !o.visible) return; for (let a = o.parent; a; a = a.parent) if (!a.visible) return;
      const g = geoOf(o.geometry); if (!g) return; o.matrixWorld.decompose(P, Q, Sc);
      const m = Array.isArray(o.material) ? o.material.map(matOf) : matOf(o.material);
      meshes.push({ g, m, p: [P.x, P.y, P.z].map(r4), q: [Q.x, Q.y, Q.z, Q.w].map(r4), s: [Sc.x, Sc.y, Sc.z].map(r4) }); });
    const rooms = plan.camere.map(r => ({ id: r.id, name: r.nume, type: r.tip, rect: [r.x0, r.z0, r.x1, r.z1] }));
    const out = { format: 'casa3d-scene', version: 1, units: 'm', up: 'Y', height: plan.inaltime, rooms, materials: mats, meshes,
      sun: { dir: light && light.dir ? light.dir : [.45, .75, -.48], time: meta.time || 'day', azimuthDeg: meta.azimuthDeg ?? null },
      lights: interiorLights.map(l => ({ p: [l.position.x, l.position.y, l.position.z].map(r4), i: r4(l.intensity) })), ...meta };
    setMode(prevMode); return out; }

  return {
    setState(p, items){ plan = p; buildHouse(p); buildFurniture(items); },
    setMode, goRoom, getMode: () => mode, setLighting, capture, renderView, renderPanorama, exportScene,
    setQuality(q){ quality = q === 'high' ? 'high' : 'normal'; if (quality === 'high' && !composer){ try { setupComposer(); } catch (e){ quality = 'normal'; composer = null; } } return quality; },
    setMove(forward, strafe){ joy.f = Number.isFinite(forward) ? Math.max(-1, Math.min(1, forward)) : 0; joy.s = Number.isFinite(strafe) ? Math.max(-1, Math.min(1, strafe)) : 0; },
    dispose(){ alive = false; cancelAnimationFrame(raf); ro.disconnect(); removeEventListener('keydown', kd); removeEventListener('keyup', ku); canvas.removeEventListener('pointerdown', pd); canvas.removeEventListener('pointermove', pmv); canvas.removeEventListener('pointerup', pu); canvas.removeEventListener('wheel', wh); if (composer){ ssao.dispose(); composer.renderTarget1.dispose(); composer.renderTarget2.dispose(); composer = null; } R.dispose(); }
  };
}
