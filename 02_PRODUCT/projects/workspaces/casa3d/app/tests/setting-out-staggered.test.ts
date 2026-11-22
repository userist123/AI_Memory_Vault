// Planul de placare pentru montajul decalat (core/setting-out.ts staggeredPlan) și o singură sursă pentru pierderi
// (procentele planului de placare = cele ale devizului, core/finishes.ts PATTERN_WASTE).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { staggeredPlan, axisPlan, customSurfaceSettingOut } from '../core/setting-out';
import { PATTERN_WASTE, PATTERNS as FLOOR_PATTERNS } from '../core/finishes';

const room = { id: 'r', name: 'Hol' };
test('cărămidă 60×30 pe 2,4 × 1,2 m: rânduri decalate, plăci numărate rând cu rând, fără fâșii sub o treime', () => {
  const p = staggeredPlan(room, 'Gresie', [60, 30], 3, 2.4, 1.2, 'brick');
  assert.equal(p.pattern, 'brick'); assert.equal(p.along.pieces, 4);
  const straight = axisPlan(2.4, .6, .003).pieces * 4;
  assert.ok(p.tiles >= straight, 'decalarea nu poate cere mai puține bucăți decât montajul drept');
  assert.ok(p.tiles <= straight + 4, 'cel mult o bucată în plus pe rând');
  assert.equal(p.sliver, false, 'pornirea aleasă evită fâșiile pe toate rândurile');
  assert.ok(p.fullTiles! > 0 && p.fullTiles! < p.tiles);
  assert.equal(p.wastePct, 10); assert.equal(p.netAreaM2, 2.88); assert.equal(p.grossAreaM2, 3.168);
});
test('o treime: ciclu de 3 rânduri; pe o lățime care e multiplu exact de pas, montajul drept nu are tăieturi, cel decalat are', () => {
  const W = 3 * .603 - .003, p = staggeredPlan(room, 'Gresie', [60, 30], 3, W, .9, 'third');
  assert.equal(axisPlan(W, .6, .003).cuts.length, 0);
  assert.ok(p.across.cuts.length > 0, 'rândurile decalate au tăieturi la capete');
  assert.equal(p.along.pieces, 3);
});
test('o singură sursă pentru pierderi: planul de placare folosește procentele devizului pentru fiecare model', () => {
  for (const pattern of FLOOR_PATTERNS){ const r = customSurfaceSettingOut({ roomId: 'r', roomName: 'R', surface: 'floor', material: 'M', widthM: 2, heightM: 2, tileCm: [60, 60], pattern });
    assert.equal(r.wastePct, Math.round(PATTERN_WASTE[pattern] * 100), pattern); }
});
