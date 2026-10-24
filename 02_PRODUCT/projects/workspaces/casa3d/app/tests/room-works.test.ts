import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ } from '../core/boq';
import { roomWorks } from '../core/room-works';
import type { Catalog, MaterialsCatalog } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const snap = newSnapshot(cat, 'Test', 'demo'), boq = computeBOQ(snap, cat, mc);

describe('roomWorks', () => {
  test('materialele pe camere = liniile BOQ ale camerelor, fără mobilier', () => {
    const rooms = snap.floor.rooms.map(r => roomWorks(snap, cat, mc, r.id));
    const expected = boq.items.filter(i => i.roomId != null && i.category !== 'furniture');
    assert.equal(rooms.reduce((a, w) => a + w.materials.length, 0), expected.length);
    const sum = (xs: (number | null)[]) => Math.round(xs.reduce<number>((a, v) => a + (v ?? 0), 0) * 100) / 100;
    assert.equal(sum(rooms.flatMap(w => w.materials.map(m => m.cost))), sum(expected.map(i => i.total)));
  });
  test('manopera pe cameră se potrivește cu BOQ', () => {
    for (const r of snap.floor.rooms){ const w = roomWorks(snap, cat, mc, r.id), exp = boq.labor.filter(l => l.roomId === r.id);
      assert.deepEqual(w.labor.map(l => [l.key, l.qty, l.low, l.expected, l.high]), exp.map(l => [l.key, l.qty, l.low, l.expected, l.high])); }
  });
  test('mobilierul pe cameră = plasările din cameră', () => {
    for (const r of snap.floor.rooms) assert.deepEqual(roomWorks(snap, cat, mc, r.id).furniture.map(f => f.id), snap.placements.filter(p => p.roomId === r.id).map(p => p.id));
    assert.ok(snap.placements.length > 0);
  });
  test('prețul necunoscut e numărat ca necunoscut, nu ca 0', () => {
    const victim = snap.placements[0]!, cat2: Catalog = { ...cat, offers: cat.offers.filter(o => o.variantId !== victim.variantId) };
    const before = roomWorks(snap, cat, mc, victim.roomId), after = roomWorks(snap, cat2, mc, victim.roomId);
    const f = after.furniture.find(x => x.id === victim.id)!;
    assert.equal(f.price, null); assert.equal(f.offerId, null);
    const sumU = (w: typeof after) => Object.values(w.totals).reduce((a, t) => a + t.unknown, 0);
    assert.equal(sumU(after), sumU(before) + 1);
  });
});
