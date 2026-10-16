// Prezentarea pe telefonul clientului: rezoluția imaginilor 3D după ecran (cel mult 1600 px), fără ambient occlusion pe
// link/telefon, iar tipărirea primește rezoluția completă ca înainte. Plus garda pentru dublul click pe butoanele care creează.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { renderWidth, renderHeight, presentQuality, isMobileEnv, enginePixelRatio, yieldToBrowser, goodEnough, FULL_W } from '../lib/present-render';
import { singleFlight } from '../lib/single-flight';

test('rezoluția: min(dpr, 2) × lățimea CSS, cel mult 1600 px; motorul înmulțește cu densitatea', () => {
  const px = (css: number, dpr: number) => renderWidth({ cssWidth: css, dpr }) * enginePixelRatio(dpr);
  assert.equal(px(390, 3), 780, 'telefon 390 CSS px, dpr 3 → 780 px (nu 1600×1000×3)');
  assert.equal(px(390, 1), 390); assert.equal(px(1280 * .58, 1), Math.round(1280 * .58));
  assert.equal(px(1200, 2), 1600, 'plafon 1600');
  assert.equal(renderWidth({ cssWidth: 390, dpr: 3, print: true }), FULL_W, 'tipărirea: 1600 logic, ca înainte');
  assert.equal(renderWidth({ cssWidth: 0, dpr: NaN }), 800, 'fără măsură: o valoare rezonabilă');
  assert.equal(renderHeight(1600), 1000); assert.equal(renderHeight(1600, true), 900);
  assert.equal(goodEnough(780, FULL_W), false, 'o imagine de ecran se reface la tipărire'); assert.equal(goodEnough(1600, 780), true);
});

test('ambient occlusion doar pentru designer pe calculator', () => {
  assert.equal(presentQuality({ share: false, mobile: false }), 'high');
  assert.equal(presentQuality({ share: true, mobile: false }), 'normal'); assert.equal(presentQuality({ share: false, mobile: true }), 'normal');
  assert.equal(isMobileEnv({ matchMedia: q => ({ matches: q === '(pointer: coarse)' }), innerWidth: 1024 }), true);
  assert.equal(isMobileEnv({ matchMedia: () => ({ matches: false }), innerWidth: 390 }), true);
  assert.equal(isMobileEnv({ matchMedia: () => ({ matches: false }), innerWidth: 1440 }), false); assert.equal(isMobileEnv(undefined), false);
});

test('între două randări firul principal e cedat (requestIdleCallback, altfel setTimeout)', async () => {
  let idle = 0; await yieldToBrowser({ requestIdleCallback: (cb: () => void) => { idle++; cb(); }, setTimeout: () => assert.fail('nu trebuia') }); assert.equal(idle, 1);
  let to = 0; await yieldToBrowser({ setTimeout: (cb: () => void) => { to++; cb(); } }); assert.equal(to, 1);
});

test('PresentView: imaginile se fac leneș (IntersectionObserver), una câte una, iar planșele au un loc ușor până atunci', () => {
  const src = readFileSync(path.resolve(import.meta.dirname, '../components/PresentView.tsx'), 'utf8');
  assert.match(src, /new IntersectionObserver/); assert.match(src, /yieldToBrowser\(\)/); assert.match(src, /presentQuality\(\{ share: !!share, mobile \}\)/);
  assert.doesNotMatch(src, /setQuality\?\.\('high'\)/, 'nu mai forțează AO pentru toți'); assert.doesNotMatch(src, /renderView\([^)]*1600, 1000\)/, 'nicio randare fixă 1600×1000 la încărcare');
  assert.match(src, /ph-wait/); assert.match(src, /shoot\(k, FULL_W, true\)/, 'tipărirea cere rezoluția completă');
});

test('singleFlight: trei click-uri în același tick pornesc o singură cerere; după reușită cu hold, cheia rămâne ocupată', async () => {
  const f = singleFlight(); let calls = 0; const job = () => new Promise<boolean>(r => setTimeout(() => { calls++; r(true); }, 5));
  const out = await Promise.all([f.run('create', job), f.run('create', job), f.run('create', job)]);
  assert.equal(calls, 1); assert.deepEqual(out, [true, undefined, undefined]);
  await f.run('dup', job); await f.run('dup', job); assert.equal(calls, 3, 'secvențial: fiecare click după terminare trece');
  await f.run('nav', job, { hold: r => r === true }); assert.equal(await f.run('nav', job), undefined, 'pagina urmează să se schimbe'); assert.equal(f.has('nav'), true);
  await assert.rejects(f.run('err', () => Promise.reject(new Error('x')))); assert.equal(f.has('err'), false, 'eșecul eliberează cheia');
});

import { renderSize, PRINT_ROOM_ASPECT, PRINT_COVER_ASPECT } from '../lib/present-render';
test('imaginea camerei se randează în forma locului ei din planșă (nu 16:10 decupat)', () => {
  // planșa 16:9 de 1280 px: imaginea are 58 % × 720 px ≈ 742 × 720, aproape pătrată
  const s = renderSize({ cssWidth: 742, cssHeight: 720, dpr: 1 }); assert.equal(s.w, 742); assert.equal(s.h, 720);
  const ph = renderSize({ cssWidth: 390, cssHeight: 292.5, dpr: 3 }); assert.equal(ph.w, 780 / 2); assert.equal(ph.h, Math.round(390 * .75), 'telefon: 4:3');
  const pr = renderSize({ cssWidth: 390, cssHeight: 292, dpr: 3, print: true }); assert.equal(pr.w, FULL_W); assert.equal(pr.h, Math.round(FULL_W * PRINT_ROOM_ASPECT), 'tipărirea: forma din A4, nu cea de pe ecran');
  assert.equal(renderSize({ cssWidth: 100, dpr: 1, print: true, overview: true }).h, Math.round(FULL_W * PRINT_COVER_ASPECT));
  assert.equal(renderSize({ cssWidth: 800, dpr: 1 }).h, renderHeight(800), 'fără înălțime: formatul vechi');
  assert.equal(renderSize({ cssWidth: 800, cssHeight: 10000, dpr: 1 }).h, 2000, 'forme extreme limitate');
});
test('PresentView cere imaginile cu lățimea și înălțimea reale ale planșei', () => {
  const src = readFileSync(path.resolve(import.meta.dirname, '../components/PresentView.tsx'), 'utf8');
  assert.match(src, /renderSize\(/); assert.match(src, /renderView\(\{ roomId: key \}, size\.w, size\.h\)/); assert.doesNotMatch(src, /renderView\(\{ roomId: key \}, w, renderHeight\(w\)\)/);
});
