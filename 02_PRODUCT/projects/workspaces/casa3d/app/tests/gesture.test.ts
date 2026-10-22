// Ciupirea pe plan: punctul dintre degete rămâne pe loc, zoom-ul are limite, deplasarea cu două degete mută planul.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { pinchViewBox, ZOOM_LIMITS } from '../core/gesture';
const vb = { x: -1, y: -1, w: 12, h: 10 }, ctm = { a: 30, d: 30, e: 45, f: 40 };   // 30 px pe metru
const toWorldAt = (v0: typeof vb, c0: typeof ctm, v1: typeof vb, p: { x: number; y: number }) => {
  // matricea pentru v1, cu același element SVG: scara crește invers cu lățimea, centrarea (offset) rămâne aceeași
  const r = v1.w / v0.w, a = c0.a / r, d = c0.d / r, offX = c0.e + v0.x * c0.a, offY = c0.f + v0.y * c0.d;
  return { x: (p.x - offX) / a + v1.x, y: (p.y - offY) / d + v1.y };
};
test('fără mișcare viewBox-ul rămâne la fel', () => {
  const m = { x: 200, y: 150 }; const r = pinchViewBox(vb, ctm, m, 100, m, 100);
  for (const k of ['x', 'y', 'w', 'h'] as const) assert.ok(Math.abs(r[k] - vb[k]) < 1e-9, k);
});
test('degetele se depărtează de două ori: zoom 2x în jurul punctului dintre ele', () => {
  const m = { x: 200, y: 150 }, before = toWorldAt(vb, ctm, vb, m), r = pinchViewBox(vb, ctm, m, 100, m, 200);
  assert.ok(Math.abs(r.w - 6) < 1e-9 && Math.abs(r.h - 5) < 1e-9);
  const after = toWorldAt(vb, ctm, r, m); assert.ok(Math.abs(after.x - before.x) < 1e-9 && Math.abs(after.y - before.y) < 1e-9, 'punctul de sub degete rămâne pe loc');
});
test('deplasare cu două degete fără ciupire: planul urmează degetele', () => {
  const r = pinchViewBox(vb, ctm, { x: 200, y: 150 }, 100, { x: 260, y: 120 }, 100);
  assert.ok(Math.abs(r.x - (vb.x - 60 / 30)) < 1e-9 && Math.abs(r.y - (vb.y + 30 / 30)) < 1e-9 && r.w === vb.w);
});
test('zoom-ul are limite', () => {
  const m = { x: 200, y: 150 };
  assert.equal(pinchViewBox(vb, ctm, m, 100, m, 100000).w, ZOOM_LIMITS.minW);
  assert.equal(pinchViewBox(vb, ctm, m, 100000, m, 1).w, ZOOM_LIMITS.maxW);
});
