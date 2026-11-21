// Repetarea în rând a punctelor tehnice (core/tech-array.ts): spoturi libere în cameră, puncte de perete doar de-a lungul
// peretelui, totul sau nimic, fără calorifere/aer condiționat, id-uri unice, trec prin sanitizeTech neschimbate.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { arrayTech } from '../core/tech-array';
import { sanitizeTech } from '../core/technical';
import type { Snapshot } from '../core/types';
import type { TechPoint } from '../core/technical';

const pt = (id: string, kind: TechPoint['kind'], x: number, z: number, height: number, roomId = 'liv'): TechPoint => ({ id, kind, roomId, x, z, height, reason: 'manual' });
const snap = (tech: TechPoint[]): Pick<Snapshot, 'floor' | 'tech'> => ({ floor: { id: 'f', name: 'P', ceilingHeight: 2.6, walls: [], rooms: [
  { id: 'liv', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 6, z1: 4 } }, { id: 'buc', name: 'Bucătărie', type: 'bucatarie', rect: { x0: 6, z0: 0, x1: 9, z1: 4 } }] }, tech });

test('spoturi: 3 copii la 120 cm pe x, id-uri noi unice, aceeași înălțime; trec prin sanitizeTech', () => {
  const s = snap([pt('light_point:liv:m1', 'light_point', 1, 2, 2.6)]), r = arrayTech(s, ['light_point:liv:m1'], 3, 120, 0);
  assert.equal(r.ok, true); if (!r.ok) return;
  assert.deepEqual(r.ids, ['light_point:liv:m2', 'light_point:liv:m3', 'light_point:liv:m4']);
  assert.deepEqual(r.tech.slice(1).map(p => [p.x, p.z, p.height, p.roomId]), [[2.2, 2, 2.6, 'liv'], [3.4, 2, 2.6, 'liv'], [4.6, 2, 2.6, 'liv']]);
  assert.deepEqual(sanitizeTech(r.tech, s as Snapshot), r.tech, 'datele rezultate sunt valide la salvare');
  assert.equal(s.tech!.length, 1, 'intrarea nu se schimbă');
});
test('un spot care trece în camera vecină primește camera aceea; în afara oricărei camere, refuz întreg', () => {
  const r = arrayTech(snap([pt('light_point:liv:m1', 'light_point', 5, 2, 2.6)]), ['light_point:liv:m1'], 2, 150, 0);
  assert.equal(r.ok, true); if (r.ok){ assert.deepEqual(r.tech.slice(1).map(p => [p.x, p.roomId]), [[6.5, 'buc'], [8, 'buc']]); assert.deepEqual(r.ids, ['light_point:buc:m1', 'light_point:buc:m2']); }
  const out = arrayTech(snap([pt('light_point:liv:m1', 'light_point', 5, 2, 2.6)]), ['light_point:liv:m1'], 3, 150, 0); assert.equal(out.ok, false); if (!out.ok) assert.equal(out.key, 'tech.arrayOutside');
});
test('prize: de-a lungul peretelui da; spre mijlocul camerei nu (refuz cu motiv)', () => {
  const s = snap([pt('outlet:liv:m1', 'outlet', 1, 0, .3)]);
  const along = arrayTech(s, ['outlet:liv:m1'], 2, 100, 0); assert.equal(along.ok, true);
  const inward = arrayTech(s, ['outlet:liv:m1'], 1, 0, 100); assert.equal(inward.ok, false); if (!inward.ok) assert.equal(inward.key, 'tech.arrayOffWall');
});
test('caloriferele și unitățile AC nu se repetă; limitele de număr și pas; punct inexistent', () => {
  const r = arrayTech(snap([pt('radiator:liv:m1', 'radiator', 3, 0, .15)]), ['radiator:liv:m1'], 2, 100, 0); assert.equal(r.ok, false); if (!r.ok) assert.equal(r.key, 'tech.arrayKind');
  const s = snap([pt('light_point:liv:m1', 'light_point', 1, 2, 2.6)]);
  for (const [n, x] of [[0, 100], [31, 10], [2, 0], [2, Infinity]] as const){ const b = arrayTech(s, ['light_point:liv:m1'], n, x, 0); assert.equal(b.ok, false); if (!b.ok) assert.equal(b.key, 'edit.arrayBad'); }
  const none = arrayTech(s, ['nu'], 2, 100, 0); assert.equal(none.ok, false); if (!none.ok) assert.equal(none.key, 'edit.noPiece');
});
test('plafonul de puncte se numără pe toată casa, nu doar pe nivelul afișat', () => {
  const s = snap([pt('light_point:liv:m1', 'light_point', 1, 2, 2.6)]);
  const r = arrayTech(s, ['light_point:liv:m1'], 6, 50, 0, 597); assert.equal(r.ok, false); if (!r.ok) assert.equal(r.key, 'tech.arrayTooMany');
  assert.equal(arrayTech(s, ['light_point:liv:m1'], 3, 50, 0, 597).ok, true, '597 + 3 = 600 încape');
});
test('o priză repetată de-a lungul peretelui nu poate cădea în golul unei uși', () => {
  const s = snap([pt('outlet:liv:m1', 'outlet', .5, 0, .3)]);
  s.floor.walls = [{ id: 'w1', a: [0, 0], b: [6, 0], thickness: .2, exterior: false, openings: [{ id: 'd1', kind: 'door', offset: 2.5, width: 1 }] }];
  const r = arrayTech(s, ['outlet:liv:m1'], 4, 75, 0); assert.equal(r.ok, false); if (!r.ok) assert.equal(r.key, 'tech.arrayInOpening');
  assert.equal(arrayTech(s, ['outlet:liv:m1'], 1, 75, 0).ok, true, 'înainte de ușă e în regulă');
});
