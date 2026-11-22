// Coloanele verticale pe trasee (core/mep-riser.ts): urcare/coborâre într-un punct, cu restul traseului la cota nouă;
// coborâre la un capăt; limite; rezultatul trece prin sanitizeMepRun și lungimea include segmentul vertical.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { addRiser, addEndDrop, risers } from '../core/mep-riser';
import { sanitizeMepRun, runLength, MEP_LIMITS } from '../core/mep';
import type { MepRun } from '../core/types';

const run = (): MepRun => ({ id: 'mep-1', system: 'water_cold', points: [[0, .3, 0], [2, .3, 0], [2, .3, 3]] });
test('urcare la punctul 1: segment vertical inserat, restul traseului la cota nouă; trece prin sanitizator', () => {
  const r = addRiser(run(), 1, 2.4, 2.6); assert.equal(r.ok, true); if (!r.ok) return;
  assert.deepEqual(r.run.points, [[0, .3, 0], [2, .3, 0], [2, 2.4, 0], [2, 2.4, 3]]);
  assert.deepEqual(risers(r.run), [{ from: 1, to: 2, dh: 2.1 }]);
  assert.deepEqual(sanitizeMepRun(r.run, 2.6), r.run);
  assert.ok(Math.abs(runLength(r.run) - (2 + 2.1 + 3)) < 1e-9, 'lungimea include coloana');
});
test('la ultimul punct urcarea e doar segmentul vertical; la cota identică nu se schimbă nimic', () => {
  const r = addRiser(run(), 2, 1, 2.6); assert.equal(r.ok, true); if (r.ok) assert.deepEqual(r.run.points.slice(-2), [[2, .3, 3], [2, 1, 3]]);
  const same = addRiser(run(), 1, .3, 2.6); assert.equal(same.ok, false); if (!same.ok) assert.equal(same.key, 'mepRiser.same');
});
test('coborâre la capete (racord lavoar, sifon de pardoseală)', () => {
  const e = addEndDrop(run(), 'end', .55, 2.6); assert.equal(e.ok, true); if (e.ok) assert.deepEqual(e.run.points.at(-1), [2, .55, 3]);
  const s = addEndDrop(run(), 'start', 0, 2.6); assert.equal(s.ok, true); if (s.ok) assert.deepEqual(s.run.points[0], [0, 0, 0]);
});
test('coloanele de mai departe își păstrează forma: urcarea mută restul traseului cu aceeași diferență', () => {
  const r0: MepRun = { id: 'm', system: 'water_cold', points: [[0, 1, 0], [1, 1, 0], [2, 1, 0], [2, .55, 0]] };
  const r = addRiser(r0, 0, .2, 2.6); assert.equal(r.ok, true); if (!r.ok) return;
  assert.deepEqual(r.run.points, [[0, 1, 0], [0, .2, 0], [1, .2, 0], [2, .2, 0], [2, -.25, 0]], 'coborârea de 45 cm de la capăt rămâne de 45 cm');
  const out = addRiser(r0, 0, -.7, 2.6); assert.equal(out.ok, false, 'capătul ar ajunge la −1,15 m, sub −1 m'); if (!out.ok) assert.equal(out.key, 'mepRiser.badHeight');
});
test('limite: index, cota între −1 m și tavan + 1 m, numărul maxim de puncte', () => {
  for (const [i, h, k] of [[-1, 1, 'mepRiser.badIndex'], [3, 1, 'mepRiser.badIndex'], [1, 3.7, 'mepRiser.badHeight'], [1, -1.5, 'mepRiser.badHeight'], [1, NaN, 'mepRiser.badHeight']] as const){
    const r = addRiser(run(), i, h, 2.6); assert.equal(r.ok, false); if (!r.ok) assert.equal(r.key, k); }
  const full: MepRun = { id: 'm', system: 'drain', points: Array.from({ length: MEP_LIMITS.maxPoints }, (_, i) => [i * .1, .3, 0] as [number, number, number]) };
  const r = addRiser(full, 3, 1, 2.6); assert.equal(r.ok, false); if (!r.ok) assert.equal(r.key, 'mepRiser.tooMany');
});
