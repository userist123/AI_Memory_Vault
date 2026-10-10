// components/viewer3d.js — scena 3D portată din prototip (tur la persoana întâi + machetă).
// Modelele de mobilier, materialele și construcția pereților sunt copiate din prototip.
import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { SSAOPass } from 'three/examples/jsm/postprocessing/SSAOPass.js';
import { ShaderPass } from 'three/examples/jsm/postprocessing/ShaderPass.js';

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
  const g = new THREE.ExtrudeGeometry(s, { depth: Math.max(.001, d - 2 * r), bevelEnabled: true, bevelSize: r, bevelThickness: r, bevelSegments: 4, curveSegments: 6 }); g.translate(0, 0, -(d - 2 * r) / 2); return g; }

/* ---------- modele 3D pe variante, la dimensiunile oficiale ---------- */
function model(it){
  const s = it.s || {}, g = new THREE.Group(), w = it.w / 100, d = it.d / 100, h = it.h / 100;
  const B = (bw, bh, bd, x, y, z, m) => { const o = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, bd), m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const RB = (bw, bh, bd, r, x, y, z, m) => { const o = new THREE.Mesh(rgeo(bw, bh, bd, r), m); o.position.set(x, y, z); o.castShadow = o.receiveShadow = true; g.add(o); return o; };
  const CY = (rt, rb, hh, x, y, z, m, seg) => { const o = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, hh, seg || 24), m); o.position.set(x, y, z); o.castShadow = true; g.add(o); return o; };
  const body = (c) => s.wood ? MAT('wood', c) : MAT('paint', c);
  switch (it.model){
    case 'sofa': { const up = MAT(s.mat || 'fabric', s.col), dark = MAT('paint', '#1b1b1b'), ad = .95, arm = .24; const sw = s.chaise ? w - .02 : w;
      [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => CY(.02, .018, .06, a * (sw / 2 - .08), .03, b * (ad / 2 - .08), dark, 10));
      RB(sw, .22, ad, .03, 0, .17, 0, up);                          // bază
      RB(.24, .36, ad, .06, -sw / 2 + arm / 2, .46, 0, up);         // cotiere joase și late (specific KIVIK)
      if (!s.chaise) RB(.24, .36, ad, .06, sw / 2 - arm / 2, .46, 0, up);
      const seatW = (sw - arm * (s.chaise ? 1 : 2)), n = 3; const x0 = -sw / 2 + arm;
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
    case 'bed': { const fr = body(s.col), hb = s.wood ? MAT('wood', s.col) : MAT('paint', s.col);
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
    case 'kitchen': { const frame = MAT('metal', s.frame || '#34363a'), front = MAT('paint', s.fronts || '#f4f4f1'), wd = .6;
      B(w, .08, wd - .05, 0, .04, -d / 2 + wd / 2, MAT('paint', '#2b2c2f')); B(w, .78, wd - .02, 0, .47, -d / 2 + wd / 2, MAT('paint', '#2d2f33'));
      const n = Math.round(w / .6); for (let i = 0; i < n; i++){ B(w / n - .006, .74, .018, -w / 2 + w / n * (i + .5), .46, -d / 2 + wd + .009, front); B(.18, .012, .015, -w / 2 + w / n * (i + .5), .78, -d / 2 + wd + .025, MAT('metal', '#222')); }
      B(w + .01, .038, d, 0, .88, 0, MAT('wood', '#cdb592')); // blat
      B(.46, .015, .4, -w / 2 + .45, .9, 0, MAT('chrome', '#c6c9cc')); CY(.015, .015, .25, -w / 2 + .45, 1.02, -d / 2 + .08, MAT('chrome', '#c6c9cc'), 10);
      B(.58, .006, .5, w / 2 - .55, .901, 0, MAT('screen', '#0d0d0e')); B(.56, .56, .012, w / 2 - .55, .46, -d / 2 + wd + .02, MAT('screen', '#141517'));
      // polițe ENHET cu cadru deschis (antracit) + dulapuri suspendate
      const top = h - .02; B(w, .02, .32, 0, top, -d / 2 + .16, frame); B(w, .02, .32, 0, top - .72, -d / 2 + .16, frame); for (let i = 0; i <= n; i++) B(.02, .72, .32, -w / 2 + .01 + i * (w - .02) / n, top - .36, -d / 2 + .16, frame);
      B(w, .015, .3, 0, top - .36, -d / 2 + .16, frame); for (let i = 0; i < n; i += 2) B(w / n - .01, .7, .015, -w / 2 + w / n * (i + .5), top - .36, -d / 2 + .32, front);
      for (let i = 1; i < n; i += 2){ B(.1, .12, .1, -w / 2 + w / n * (i + .5) - .08, top - .64, -d / 2 + .15, MAT('ceramic', '#eae6de')); B(.14, .1, .14, -w / 2 + w / n * (i + .5) + .08, top - .3, -d / 2 + .15, MAT('ceramic', '#6a7c72')); }
      B(.6, .12, .46, w / 2 - .55, top - .9, -d / 2 + .23, MAT('metal', '#bfc2c5')); B(w, .01, .02, 0, 1.25, -d / 2 + .01, MAT('ceramic', '#f1efea')); break; }
    case 'fridge': { const m = s.inox ? MAT('metal', s.col) : MAT('paint', s.col); RB(w, h, d, .02, 0, h / 2, 0, m); B(w - .01, .004, .01, 0, h * .64, d / 2 + .001, MAT('paint', '#bdbdbd')); B(.02, .4, .03, w / 2 - .05, h * .8, d / 2 + .015, MAT('metal', '#aaa')); B(.02, .3, .03, w / 2 - .05, h * .45, d / 2 + .015, MAT('metal', '#aaa')); break; }
    case 'shower': { const pm = MAT('metal', s.prof), gl = MAT(s.frost ? 'frost' : 'glass', '#dcecf2'); B(w, .05, d, 0, .025, 0, MAT('ceramic', '#fafafa'));
      if (s.round){ const arc = new THREE.Mesh(new THREE.CylinderGeometry(w - .02, w - .02, h - .06, 32, 1, true, 0, Math.PI / 2), gl); arc.position.set(-w / 2 + .01, h / 2 + .03, -d / 2 + .01); g.add(arc); }
      else { const t = s.thick ? .03 : .02; [[w, .008, 0, d / 2 - .004], [.008, d, w / 2 - .004, 0]].forEach(([a, b, x, z]) => { const p = new THREE.Mesh(new THREE.BoxGeometry(a, h - .06, b), gl); p.position.set(x, h / 2 + .03, z); g.add(p); });
        B(t, h - .05, t, w / 2 - t / 2, h / 2, d / 2 - t / 2, pm); B(w, t, t, 0, h - t / 2, d / 2 - t / 2, pm); B(t, t, d, w / 2 - t / 2, h - t / 2, 0, pm); }
      CY(.012, .012, h - .4, -w / 2 + .12, h / 2 + .1, -d / 2 + .04, MAT('chrome', '#cfcfcf'), 10); CY(.1, .1, .015, -w / 2 + .2, h - .1, -d / 2 + .2, MAT('chrome', '#cfcfcf'), 24); break; }
    case 'vanity': { const m = body(s.col), y0 = s.hang ? .3 : 0; if (s.legs) [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .15, .03, a * (w / 2 - .04), .075, b * (d / 2 - .04), MAT('metal', '#222')));
      const hb = (s.hang ? h - .15 : h - .15) - (s.legs ? .15 : 0); B(w, hb, d, 0, y0 + (s.legs ? .15 : 0) + hb / 2, 0, m); B(w / 2 - .01, hb - .03, .015, -w / 4, y0 + (s.legs ? .15 : 0) + hb / 2, d / 2 + .008, m); B(w / 2 - .01, hb - .03, .015, w / 4, y0 + (s.legs ? .15 : 0) + hb / 2, d / 2 + .008, m);
      const ty = y0 + (s.legs ? .15 : 0) + hb; RB(w + .01, .14, d + .06, .03, 0, ty + .07, .03, MAT('ceramic', '#fdfdfd')); CY(.012, .012, .15, 0, ty + .2, -d / 2 + .05, MAT('chrome', '#cfcfcf'), 10);
      B(w * .85, .6, .02, 0, 1.55, -d / 2 + .01, MAT('mirror', '#dde6ea')); if (s.legs) B(w * .6, .04, .06, 0, 1.9, -d / 2 + .04, MAT('paint', '#f4f4f1')); break; }
    case 'wc': { const cer = MAT('ceramic', '#fdfdfd'); if (s.square){ RB(.36, .3, .52, .06, 0, .27, .05, cer); } else { const b = new THREE.Mesh(new THREE.CylinderGeometry(.18, .13, .32, 32), cer); b.scale.z = 1.4; b.position.set(0, .24, .06); b.castShadow = true; g.add(b); }
      RB(.37, .03, .5, .015, 0, .42, .06, cer); B(.42, .9, .12, 0, .75, -d / 2 + .06, MAT('paint', '#efefec')); B(.24, .16, .01, 0, 1.0, -d / 2 + .125, MAT('chrome', '#d4d4d4')); break; }
    case 'shoe': { const m = body(s.col); B(w, h - .04, d, 0, (h - .04) / 2 + .04, 0, m); B(w + .02, .025, d + .02, 0, h - .012, 0, m); [[-1, 1], [1, 1]].forEach(([a, b]) => B(.03, .04, .03, a * (w / 2 - .03), .02, b * (d / 2 - .03), m));
      const rows = s.two ? 2 : 2, cols = s.two ? 1 : 2; for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++){ const fw = w / cols - .01, fh = (h - .1) / rows - .015, x = -w / 2 + w / cols * (c + .5), y = .06 + (h - .1) / rows * (r + .5); B(fw, fh, .018, x, y, d / 2 + .009, m); CY(.012, .012, .02, x, y + fh / 2 - .05, d / 2 + .025, MAT('metal', '#7c6a55'), 10).rotation.x = Math.PI / 2; }
      break; }
    case 'mirror': { const m = MAT('paint', '#f3f3f0'), y0 = s.wall ? .95 : 0; B(w, h, d, 0, y0 + h / 2, 0, m); B(w - .06, h - .08, .01, 0, y0 + h / 2, d / 2 + .006, MAT('mirror', '#dfe6ea')); break; }
  }
  return g;
}


  // ---------- casa (reconstruită la fiecare schimbare de plan) ----------
  let house = new THREE.Group(), ceilings = new THREE.Group(), walls = new THREE.Group(), furniture = new THREE.Group(); scene.add(house, furniture);
  let colliders = [], blockers = [], plan = null, W = 1, D = 1, C = new THREE.Vector3(), pickables = [];
  const disposeGroup = g => g.traverse(o => { if (o.geometry) o.geometry.dispose(); });
  function box(w, h, d, mat, x, y, z, rotY, parent){ const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat); m.position.set(x, y, z); m.rotation.y = rotY || 0; m.castShadow = m.receiveShadow = true; parent.add(m); return m; }
  function buildHouse(p){
    disposeGroup(house); scene.remove(house); interiorLights.length = 0; house = new THREE.Group(); ceilings = new THREE.Group(); walls = new THREE.Group(); house.add(ceilings, walls); scene.add(house); colliders = [];
    const H = p.inaltime;
    p.camere.forEach(r => { const wet = r.tip === 'baie' || r.tip === 'bucatarie', w = r.x1 - r.x0, d = r.z1 - r.z0, t = (wet ? tiles : parquet).clone(); t.needsUpdate = true; t.repeat.set(w / (wet ? .9 : 1.6), d / (wet ? .9 : 1.6));
      const f = new THREE.Mesh(new THREE.PlaneGeometry(w, d), new THREE.MeshStandardMaterial({ map: t, roughness: wet ? .35 : .6, color: lin(r.podea || '#ffffff') })); f.rotation.x = -Math.PI / 2; f.position.set((r.x0 + r.x1) / 2, 0, (r.z0 + r.z1) / 2); f.receiveShadow = true; house.add(f);
      const c = new THREE.Mesh(new THREE.PlaneGeometry(w, d), r.tavan ? lookMat('ceil', r.tavan, 1) : ceilMat); c.rotation.x = Math.PI / 2; c.position.set((r.x0 + r.x1) / 2, H, (r.z0 + r.z1) / 2); ceilings.add(c);
      const pl = new THREE.PointLight(0xfff0dc, light ? light.interior : .35, 7, 2); interiorLights.push(pl); pl.position.set((r.x0 + r.x1) / 2, H - .3, (r.z0 + r.z1) / 2); ceilings.add(pl); });
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
        if (g.tip === 'usa'){ const dh = Math.min(H - .05, g.h || 2.1), fm = g.culoare ? lookMat('frame', g.culoare, .5) : frameMat; seg(g.la, g.la + g.l, dh, H); const m = g.la + g.l / 2; [g.la, g.la + g.l].forEach(s => box(.05, dh, th + .02, fm, ax + ux * s, dh / 2, az + uz * s, rot, walls)); box(g.l, .05, th + .02, fm, ax + ux * m, dh, az + uz * m, rot, walls); }
        else { const sl = g.sill != null ? g.sill : .9, top = Math.min(H - .05, sl + (g.h || 1.3)), wh = top - sl, cy = (sl + top) / 2, fm = g.culoare ? lookMat('frame', g.culoare, .5) : frameMat;
          seg(g.la, g.la + g.l, 0, sl); seg(g.la, g.la + g.l, top, H); const m = g.la + g.l / 2; box(g.l, wh, .02, glass, ax + ux * m, cy, az + uz * m, rot, walls).castShadow = false;
          box(g.l, .05, th + .04, fm, ax + ux * m, sl, az + uz * m, rot, walls); box(g.l, .05, th + .02, fm, ax + ux * m, top, az + uz * m, rot, walls); box(.04, wh, th + .02, fm, ax + ux * m, cy, az + uz * m, rot, walls); }
        colliders.push({ ax, az, ux, uz, s0: q, s1: g.la }); if (g.tip === 'fereastra') colliders.push({ ax, az, ux, uz, s0: g.la, s1: g.la + g.l }); q = g.la + g.l; });
      seg(q, L, 0, H); colliders.push({ ax, az, ux, uz, s0: q, s1: L }); });
    const xs = p.camere.flatMap(r => [r.x0, r.x1]), zs = p.camere.flatMap(r => [r.z0, r.z1]);
    const minX = Math.min(...xs, 0), maxX = Math.max(...xs, 1), minZ = Math.min(...zs, 0), maxZ = Math.max(...zs, 1);
    W = maxX - minX; D = maxZ - minZ; C.set((minX + maxX) / 2, 0, (minZ + maxZ) / 2); placeSun(); sun.target.position.copy(C);
    orbit.r = Math.max(W, D, 4) * 1.15; walls.scale.y = mode === 'walk' ? 1 : .42; ceilings.visible = mode === 'walk';
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
    for (const b of blockers){ if (x > b.x0 - r && x < b.x1 + r && z > b.z0 - r && z < b.z1 + r){ const dl = x - (b.x0 - r), dr = (b.x1 + r) - x, du = z - (b.z0 - r), dn = (b.z1 + r) - z, m = Math.min(dl, dr, du, dn);
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
  function setMode(m){ mode = m; walls.scale.y = m === 'walk' ? 1 : .42; ceilings.visible = m === 'walk'; }
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
  return {
    setState(p, items){ plan = p; buildHouse(p); buildFurniture(items); },
    setMode, goRoom, getMode: () => mode, setLighting, capture, renderView, renderPanorama,
    setQuality(q){ quality = q === 'high' ? 'high' : 'normal'; if (quality === 'high' && !composer){ try { setupComposer(); } catch (e){ quality = 'normal'; composer = null; } } return quality; },
    setMove(forward, strafe){ joy.f = Number.isFinite(forward) ? Math.max(-1, Math.min(1, forward)) : 0; joy.s = Number.isFinite(strafe) ? Math.max(-1, Math.min(1, strafe)) : 0; },
    dispose(){ alive = false; cancelAnimationFrame(raf); ro.disconnect(); removeEventListener('keydown', kd); removeEventListener('keyup', ku); canvas.removeEventListener('pointerdown', pd); canvas.removeEventListener('pointermove', pmv); canvas.removeEventListener('pointerup', pu); canvas.removeEventListener('wheel', wh); if (composer){ ssao.dispose(); composer.renderTarget1.dispose(); composer.renderTarget2.dispose(); composer = null; } R.dispose(); }
  };
}
