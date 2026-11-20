// Precizia randării 3D: fiecare variantă din catalog, desenată de motor (components/viewer3d-engine.js, model()), are cotele
// din fișa produsului (±2 cm), centrată pe amprenta din plan. Accesoriile de punere în scenă (userData.prop: televizorul de
// pe comodă, veioza, oglinda peste lavoar...) și piesele de set (userData.setPiece: scaunele unei mese) nu intră în măsură.
// Motorul rulează fără placă video: randerul WebGL și browserul sunt înlocuite cu obiecte goale; model() doar construiește
// geometria (THREE), nu desenează nimic. Același audit, în browser: scripts/audit-model-dims.js.
import { test, vi } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { toEngineCatalog } from '../core/catalog';
import type { Catalog } from '../core/types';

// un obiect „orice”: orice proprietate, apel sau construcție dă tot un astfel de obiect; ca număr valorează 0
const any = (): any => new Proxy(function(){}, {
  get: (_t, k) => k === Symbol.toPrimitive ? () => 0 : k === 'then' ? undefined : k === Symbol.iterator ? function*(){} : any(),
  apply: () => any(), construct: () => any(),
});
vi.mock('three', async orig => { const T: any = await orig(); return { ...T, WebGLRenderer: function(){ return any(); }, WebGLRenderTarget: function(){ return any(); } }; });
for (const p of ['three/examples/jsm/postprocessing/EffectComposer.js', 'three/examples/jsm/postprocessing/SSAOPass.js', 'three/examples/jsm/postprocessing/OutputPass.js', 'three/examples/jsm/postprocessing/RenderPass.js'])
  vi.doMock(p, () => new Proxy({}, { get: () => function(){ return any(); } }));

const SKIP = new Set(['mirror', 'plant', 'shower']), TOL = 2.05;
test('fiecare produs din catalog se desenează la cotele din fișă (±2 cm), centrat pe amprentă', async () => {
  const g = globalThis as any;
  for (const k of ['window', 'document', 'navigator', 'matchMedia', 'requestAnimationFrame', 'cancelAnimationFrame', 'ResizeObserver', 'IntersectionObserver', 'performance', 'addEventListener', 'removeEventListener', 'devicePixelRatio', 'innerWidth', 'innerHeight', 'getComputedStyle', 'HTMLCanvasElement', 'Image', 'OffscreenCanvas'])
    if (!(k in g) || k === 'navigator') { try { Object.defineProperty(g, k, { value: any(), configurable: true, writable: true }); } catch {} }
  const m: any = await import('../components/viewer3d-engine.js');
  const v = m.createViewer(any());
  assert.equal(typeof v.measureModel, 'function');
  const cat = toEngineCatalog(catalogJson as unknown as Catalog), bad: string[] = []; let n = 0;
  for (const [grp, gr] of Object.entries(cat) as [string, any][]){
    if (!gr.model || SKIP.has(gr.model)) continue;
    gr.v.forEach((it: any, i: number) => { if (!it.w) return; n++;
      const r = v.measureModel({ ...it, model: gr.model, s: it.s || {} });
      const err = Math.max(Math.abs(r.w - it.w), Math.abs(r.d - it.d), Math.abs(r.h - it.h)), off = Math.max(Math.abs((r.x0 + r.x1) / 2), Math.abs((r.z0 + r.z1) / 2));
      if (err > TOL || off > TOL) bad.push(`${grp}-${i} ${it.nume}: ${it.w}x${it.d}x${it.h} -> ${r.w.toFixed(1)}x${r.d.toFixed(1)}x${r.h.toFixed(1)} (centru ${off.toFixed(1)})`); }); }
  assert.ok(n > 250, `prea puține variante măsurate: ${n}`);
  assert.deepEqual(bad, []);
});
