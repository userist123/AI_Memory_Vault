import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { duplicatePlacement, nudgePlacement, measure } from '../core/edit-ops';
import { validatePlacement, severityOf } from '../core/validate';
import type { Catalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog;
const demo = (): Snapshot => newSnapshot(cat, 'Test', 'demo');

test('duplicarea găsește un loc liber, păstrează varianta, rotația, dimensiunea și aspectul', () => {
  const s = demo(), src = s.placements[0]!; src.size = { w: 80, d: 50, h: 45 }; s.appearance = { items: { [src.id]: { color: '#c94f3d', material: 'wood' } } };
  const r = duplicatePlacement(s, cat, src.id, 'copie-1'); assert.ok(r.ok); if (!r.ok) return;
  const c = r.snapshot.placements.find(p => p.id === 'copie-1')!;
  assert.equal(r.snapshot.placements.length, s.placements.length + 1); assert.notEqual(c.id, src.id);
  assert.equal(c.variantId, src.variantId); assert.equal(c.rotation, src.rotation); assert.deepEqual(c.size, src.size);
  assert.deepEqual(r.snapshot.appearance!.items!['copie-1'], { color: '#c94f3d', material: 'wood' });
  assert.notEqual(severityOf(validatePlacement(r.snapshot, cat, c)), 'ERROR');
  assert.ok(c.x !== src.x || c.z !== src.z); assert.equal(s.placements.length, demo().placements.length, 'originalul nu e modificat');
});
test('duplicarea refuză când piesa e încadrată și nu mai e loc', () => {
  const s = demo(), src = s.placements[0]!, room = s.floor.rooms.find(r => r.id === src.roomId)!;
  // camera se strânge în jurul piesei: nicio deplasare nu mai trece de validare
  room.rect = { x0: src.x - .6, x1: src.x + .6, z0: src.z - .6, z1: src.z + .6 }; s.placements = [src];
  src.size = { w: 120, d: 120, h: 45 };
  const r = duplicatePlacement(s, cat, src.id, 'x'); assert.equal(r.ok, false);
});
test('mutarea fină respectă validarea: în interior merge, în afara camerei e refuzată', () => {
  const s = demo(), src = s.placements[0]!, room = s.floor.rooms.find(r => r.id === src.roomId)!;
  s.placements = [src]; src.size = { w: 60, d: 60, h: 45 }; room.rect = { x0: src.x - 1, x1: src.x + 1, z0: src.z - 1, z1: src.z + 1 };
  const ok = nudgePlacement(s, cat, src.id, 5, 0); assert.ok(ok.ok); if (ok.ok) assert.equal(ok.snapshot.placements[0]!.x, Math.round((src.x + .05) * 1000) / 1000);
  const bad = nudgePlacement(s, cat, src.id, 150, 0); assert.equal(bad.ok, false); if (!bad.ok) assert.match(bad.message, /Poziție refuzată/);
  assert.equal(nudgePlacement(s, cat, 'nu-exista', 5, 0).ok, false);
});
test('măsurarea întoarce distanța în metri', () => {
  assert.equal(measure([0, 0], [3, 4]), 5); assert.equal(measure([1, 1], [1, 1]), 0); assert.equal(measure([0, 0], [0.123, 0]), 0.123);
});
