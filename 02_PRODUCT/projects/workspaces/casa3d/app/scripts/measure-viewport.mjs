// Măsoară viewport-ul 3D pe aplicația reală (plan A1, rândul 5): cadre randate în repaus și timpul de cadru în timpul orbitării.
// Numărătoarea nu depinde de motor: un cadru = un apel requestAnimationFrame în care s-a desenat ceva în WebGL.
//   node scripts/measure-viewport.mjs http://127.0.0.1:3214 --profile desktop --tier desktop --out D:/w/m/base.json
// --profile desktop|laptop|phone; --tier auto|desktop|laptop|phone (nivelul din viewport); --gpu hw (GPU real, fără plafonul de 60 Hz)|sw (SwiftShader,
// aproximează un GPU slab); ieșirea nu conține numele GPU-ului, doar clasa lui (hardware/software).
// --cdp PORT măsoară un Chrome care rulează deja, fără emulare (telefon real: adb reverse pentru server, adb forward pentru
// chrome_devtools_remote); tragerea devine atingere. Numele și seria dispozitivului nu se înregistrează. --viewport 1600x1000
// randează pe același GPU o fereastră de laptop, ca să încarce GPU-ul cât un ecran mare.
import { writeFileSync } from 'node:fs';
import { attach, launch, page, sleep } from './lib/cdp.mjs';

const argv = process.argv.slice(2), BASE = (argv.find(a => a.startsWith('http')) || 'http://127.0.0.1:3214').replace(/\/$/, '');
const opt = (k, d) => { const i = argv.indexOf(`--${k}`); return i >= 0 ? argv[i + 1] : d; };
const PROFILES = { desktop: { width: 1600, height: 1000, deviceScaleFactor: 1, mobile: false }, laptop: { width: 1440, height: 900, deviceScaleFactor: 2, mobile: false }, phone: { width: 390, height: 844, deviceScaleFactor: 3, mobile: true } };
const name = opt('profile', 'desktop'), prof = PROFILES[name], tier = opt('tier', 'auto'), gpu = opt('gpu', 'hw'), settle = Number(opt('settle', 10)), idleS = Number(opt('idle', 5)), orbitS = Number(opt('orbit', 6)), out = opt('out', '');
if (!prof){ console.error('Profil necunoscut:', name); process.exit(2); }
const cdpPort = opt('cdp', ''), real = !!cdpPort, vp = opt('viewport', '').split('x').map(Number);
const GPU_ARGS = { hw: ['--enable-gpu', '--ignore-gpu-blocklist', '--use-angle=default', '--disable-gpu-vsync', '--disable-frame-rate-limit'], sw: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-gpu-vsync', '--disable-frame-rate-limit'] };

const INSTRUMENT = `(() => { const T = window.__v3m = { frames: [], cur: 0 };
  for (const C of [window.WebGL2RenderingContext, window.WebGLRenderingContext]) { if (!C) continue;
    for (const k of ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced', 'drawRangeElements']) { const f = C.prototype[k]; if (!f) continue; C.prototype[k] = function(...a){ T.cur++; return f.apply(this, a); }; } }
  const raf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = cb => raf(t => { const s = performance.now(), c0 = T.cur; cb(t); if (T.cur > c0) T.frames.push({ at: s, cpu: performance.now() - s, draws: T.cur - c0 }); }); })();`;
const pct = (a, p) => { if (!a.length) return null; const s = [...a].sort((x, y) => x - y); return +s[Math.min(s.length - 1, Math.floor(s.length * p))].toFixed(1); };
function summarize(frames){ const dt = frames.slice(1).map((f, i) => f.at - frames[i].at);
  return { frames: frames.length, fps: dt.length ? +(1000 / (dt.reduce((a, b) => a + b, 0) / dt.length)).toFixed(1) : 0, p50ms: pct(dt, .5), p95ms: pct(dt, .95), cpuP95ms: pct(frames.map(f => f.cpu), .95), drawsP50: pct(frames.map(f => f.draws), .5) }; }

const cdp = real ? await attach(Number(cdpPort)) : await launch(1, GPU_ARGS[gpu]); let failed = false, tab = null; const res = real ? { profile: 'real', tier } : { profile: name, tier, gpu, viewport: prof };
try {
  const p = await page(cdp); tab = p.targetId;
  if (!real) await p.s('Emulation.setDeviceMetricsOverride', prof);
  else if (vp.length === 2) await p.s('Emulation.setDeviceMetricsOverride', { width: vp[0], height: vp[1], deviceScaleFactor: 1, mobile: false });
  if (!real && prof.mobile) await p.s('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
  await p.s('Page.addScriptToEvaluateOnNewDocument', { source: INSTRUMENT });
  await p.go(`${BASE}/`); await sleep(1500);
  await p.evaluate(t => { localStorage.setItem('casa3d.viewerTier', t); }, tier);
  const id = await p.evaluate(async () => { await fetch('/api/projects'); const r = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name: 'Măsurare viewport', template: 'demo', lang: 'ro' }) }); return (await r.json()).id; });
  await p.go(`${BASE}/p/${id}`);
  await p.evaluate(async () => { for (let i = 0; i < 300; i++){ const b = [...document.querySelectorAll('button')].find(x => x.classList.contains('v-3d') || x.textContent.trim() === '3D'); if (b){ b.click(); return; } await new Promise(r => setTimeout(r, 100)); } throw new Error('fără butonul 3D'); });
  await p.evaluate(async () => { for (let i = 0; i < 600; i++){ if (document.querySelector('.view3d canvas') && window.__casa3dViewer) return; await new Promise(r => setTimeout(r, 100)); } throw new Error('fără viewer 3D (doar în dev)'); });
  res.renderer = await p.evaluate(() => { const c = document.createElement('canvas').getContext('webgl2'), e = c && c.getExtension('WEBGL_debug_renderer_info'), s = e ? c.getParameter(e.UNMASKED_RENDERER_WEBGL) : ''; return /swiftshader|llvmpipe|software/i.test(s) ? 'software' : s ? 'hardware' : 'necunoscut'; });
  await sleep(500); res.tierAtStart = await p.evaluate(() => window.__casa3dViewer.tierNow?.() ?? null);
  const box = await p.evaluate(() => { const r = document.querySelector('.view3d canvas').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height, dw: document.querySelector('.view3d canvas').width, dh: document.querySelector('.view3d canvas').height }; });
  res.canvas = box;
  const window_ = async fn => { await p.evaluate(() => { window.__v3m.frames = []; }); await fn(); return p.evaluate(() => window.__v3m.frames); };
  const touchDrag = async s => { const at = (x, y) => [{ x, y, id: 1 }]; await p.s('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: at(box.x, box.y) }); const t0 = Date.now(), r = Math.min(box.w, box.h) * .3; while (Date.now() - t0 < s * 1000){ const a = (Date.now() - t0) / 1000 * 2; await p.s('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: at(box.x + Math.cos(a) * r, box.y + Math.sin(a) * r * .4) }); await sleep(16); } await p.s('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] }); };
  const drag = real ? touchDrag : async s => { const mouse = (type, x, y, extra = {}) => p.s('Input.dispatchMouseEvent', { type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, ...extra });
    await mouse('mousePressed', box.x, box.y, { clickCount: 1 }); const t0 = Date.now(), r = Math.min(box.w, box.h) * .3; while (Date.now() - t0 < s * 1000){ const a = (Date.now() - t0) / 1000 * 2; await mouse('mouseMoved', box.x + Math.cos(a) * r, box.y + Math.sin(a) * r * .4); await sleep(8); } await mouse('mouseReleased', box.x, box.y, { clickCount: 1 }); };
  await sleep(settle * 1000);
  const idle = await window_(() => sleep(idleS * 1000));
  res.houseIdle = { frames: idle.length, perSecond: +(idle.length / idleS).toFixed(1) };
  const orbitFrames = await window_(async () => { await drag(orbitS); await sleep(2000); }); res.houseOrbit = summarize(orbitFrames.slice(0, -1)); res.houseRestFrameDraws = orbitFrames.at(-1)?.draws ?? 0;   // ultimul cadru: imaginea din repaus
  await sleep(2500);
  const after = await window_(() => sleep(idleS * 1000)); res.houseIdleAfterOrbit = { frames: after.length, perSecond: +(after.length / idleS).toFixed(1) };
  res.statsHouse = await p.evaluate(() => { const s = window.__casa3dViewer.getStats(); return { calls: s.calls, triangles: s.triangles, callsWithShadow: s.callsWithShadow, meshes: s.meshes, materials: s.materials, pointLights: s.pointLights, textures: s.textures, programs: s.programs }; });
  const entered = await p.evaluate(() => { const s = document.querySelector('.v3bar select'); const o = s && [...s.options].find(x => x.value); if (!o) return false; s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); return true; });
  if (entered){ await sleep(3000); const w = await window_(() => sleep(idleS * 1000)); res.walkIdle = { frames: w.length, perSecond: +(w.length / idleS).toFixed(1) };
    // în tur tragerea mișcă imaginea doar la ritmul mouse-ului: mersul cu tasta ține bucla pornită și dă timpul real de cadru
    res.walkMove = summarize(await window_(async () => { const key = type => p.s('Input.dispatchKeyEvent', { type, key: 'w', code: 'KeyW', windowsVirtualKeyCode: 87 }); await key('keyDown'); await sleep(orbitS * 1000); await key('keyUp'); }));
    res.statsWalk = await p.evaluate(() => { const s = window.__casa3dViewer.getStats(); return { calls: s.calls, triangles: s.triangles, callsWithShadow: s.callsWithShadow, pointLights: s.pointLights }; }); }
  res.tierAtEnd = await p.evaluate(() => window.__casa3dViewer.tierNow?.() ?? null);
  await p.evaluate(id => fetch(`/api/projects/${id}`, { method: 'DELETE' }).then(() => {}), id).catch(() => {});   // proiectul de măsurare nu rămâne în listă
  console.log(JSON.stringify(res, null, 1));
  if (out) writeFileSync(out, JSON.stringify(res, null, 1));
} catch (e){ console.error(e); failed = true; }
finally { if (real && tab) await cdp.send('Target.closeTarget', { targetId: tab }).catch(() => {}); await cdp.close(); }
process.exit(failed ? 1 : 0);
