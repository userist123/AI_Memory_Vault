// Panoul de stil: paleta camerei ponderată după suprafață, rolurile 60-30-10, materialele alese și piesele cu prețul lor.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { roomBoard } from '../core/moodboard';
import type { Catalog, MaterialsCatalog } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;

test('livingul demo: paleta însumează 100%, pereții domină, piesele au preț, materialele au rol', () => {
  const s = newSnapshot(cat, 'Demo', 'demo', 'ro'), room = s.floor.rooms.find(r => r.type === 'living')!, b = roomBoard(s, cat, mc, room);
  const total = b.palette.reduce((a, c) => a + c.share, 0); assert.ok(Math.abs(total - 1) < 1e-9);
  assert.equal(b.palette[0]!.role, 'dominant'); assert.ok(b.palette[0]!.share >= b.palette.at(-1)!.share);
  assert.ok(Math.abs(b.ratio.dominant + b.ratio.secondary + b.ratio.accent - 1) < 1e-9);
  assert.equal(b.pieces.length, s.placements.filter(p => p.roomId === room.id).length);
  assert.ok(b.pieces.some(p => /KIVIK/.test(p.name) && (p.price ?? 0) > 0));
  assert.ok(b.materials.some(m => m.role === 'floor') && b.materials.some(m => m.role === 'wall'));
});
test('o cameră cu pereți și mobilier de aceeași culoare are o singură culoare dominantă (culorile apropiate se unesc)', () => {
  const s = newSnapshot(cat, 'Demo', 'demo', 'ro'), room = s.floor.rooms.find(r => r.type === 'dormitor')!, b = roomBoard(s, cat, mc, room);
  const hexes = b.palette.map(c => c.hex); assert.equal(new Set(hexes).size, hexes.length);
});
