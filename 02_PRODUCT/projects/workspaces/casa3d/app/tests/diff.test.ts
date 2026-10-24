import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { diffSnapshots } from '../core/diff';
import { groupOf } from '../core/catalog';
import type { Catalog, Snapshot, FurniturePlacement } from '../core/types';
beforeAll(() => resetDbForTests());
const A = '11111111-1111-4111-8111-111111111111', B = '22222222-2222-4222-8222-222222222222';

const prov = { source: 's', sourceUrl: null, verifiedAt: null, verificationType: 't', confidence: 'HIGH' as const };
const cat: Catalog = {
  suppliers: [],
  products: [{ id: 'p-sofa', group: 'sofa', name: 'Canapea', brand: 'b', category: 'c', model3d: '' }, { id: 'p-masa', group: 'masa', name: 'Masă', brand: 'b', category: 'c', model3d: '' }],
  variants: [
    { id: 'sofa-0', productId: 'p-sofa', name: 'Gri', legacyIndex: 0, dimensionsCm: null, dimensionsConfidence: 'HIGH', style: {} },
    { id: 'sofa-1', productId: 'p-sofa', name: 'Bej', legacyIndex: 1, dimensionsCm: null, dimensionsConfidence: 'HIGH', style: {} },
    { id: 'masa-0', productId: 'p-masa', name: 'Masă mică', legacyIndex: 0, dimensionsCm: null, dimensionsConfidence: 'HIGH', style: {} },
  ],
  offers: [
    { id: 'o1', variantId: 'sofa-0', supplierId: 's', price: 1000, currency: 'RON', availability: 'UNKNOWN', affiliateUrl: null, provenance: prov },
    { id: 'o2', variantId: 'sofa-1', supplierId: 's', price: 1500, currency: 'RON', availability: 'UNKNOWN', affiliateUrl: null, provenance: prov },
    // masa-0 nu are ofertă: preț necunoscut
  ],
};
const pl = (id: string, variantId: string, x = 1, z = 1, rotation = 0, roomId = 'r1'): FurniturePlacement => ({ id, roomId, group: groupOf(variantId), variantId, x, z, rotation, source: 'manual' });
const snap = (placements: FurniturePlacement[], rect = { x0: 0, z0: 0, x1: 4, z1: 3 }, name = 'Living'): Snapshot => ({
  name: 'T', floor: { id: 'f', name: 'f', ceilingHeight: 2.5, rooms: [{ id: 'r1', name, type: 'living', rect }], walls: [{ id: 'w1', a: [0, 0], b: [4, 0], thickness: .2, exterior: true, openings: [] }] },
  placements, selections: {}, picked: [] });

test('identic: diferența e goală', () => {
  const d = diffSnapshots(snap([pl('a', 'sofa-0')]), snap([pl('a', 'sofa-0')]), cat);
  assert.equal(d.isEmpty, true); assert.equal(d.cost['RON']!.delta, 0);
});
test('mobilier adăugat, eliminat, mutat (>1 cm sau rotit) și variantă schimbată', () => {
  const before = snap([pl('a', 'sofa-0'), pl('b', 'masa-0'), pl('c', 'sofa-0', 2, 2), pl('d', 'sofa-0', 3, 3)]);
  const after = snap([pl('a', 'sofa-0', 1.005), pl('c', 'sofa-0', 2.5, 2), pl('d', 'sofa-1', 3, 3, 90), pl('e', 'masa-0')]);
  const d = diffSnapshots(before, after, cat);
  assert.deepEqual(d.furniture.added.map(x => [x.id, x.name, x.roomName]), [['e', 'Masă', 'Living']]);
  assert.deepEqual(d.furniture.removed.map(x => x.id), ['b']);
  assert.deepEqual(d.furniture.moved.map(x => x.id), ['c', 'd'], '0,5 cm nu contează; 50 cm și rotirea contează');
  assert.deepEqual(d.furniture.swapped.map(x => [x.id, x.detail]), [['d', 'Gri → Bej']]);
  assert.equal(d.isEmpty, false);
});
test('camere: redenumire, redimensionare cu cm vechi/noi, adăugare, eliminare; pereți', () => {
  const before = snap([]), after = snap([], { x0: 0, z0: 0, x1: 5, z1: 3 }, 'Salon');
  after.floor.rooms.push({ id: 'r2', name: 'Baie', type: 'bath', rect: { x0: 4, z0: 0, x1: 6, z1: 2 } });
  after.floor.walls[0]!.thickness = .3; after.floor.walls.push({ id: 'w2', a: [0, 0], b: [0, 3], thickness: .2, exterior: false, openings: [] });
  const d = diffSnapshots(before, after, cat);
  assert.deepEqual(d.rooms.added, [{ id: 'r2', name: 'Baie' }]);
  assert.equal(d.rooms.changed.length, 1);
  assert.ok(d.rooms.changed[0]!.changes.some(c => c.includes('„Living” → „Salon”')));
  assert.ok(d.rooms.changed[0]!.changes.some(c => c.includes('400×300 cm → 500×300 cm')));
  assert.deepEqual(d.walls, { added: 1, removed: 0, changed: 1 });
  assert.deepEqual(diffSnapshots(after, before, cat).rooms.removed, [{ id: 'r2', name: 'Baie' }]);
});
test('cost: delta pe monedă; prețurile necunoscute sunt numărate, nu 0', () => {
  const d = diffSnapshots(snap([pl('a', 'sofa-0')]), snap([pl('a', 'sofa-1'), pl('b', 'masa-0')]), cat);
  assert.deepEqual(d.cost['RON'], { before: 1000, after: 1500, delta: 500, unknownBefore: 0, unknownAfter: 1 });
  assert.equal(d.isEmpty, false);
  const onlyUnknown = diffSnapshots(snap([]), snap([pl('b', 'masa-0')]), cat);
  assert.equal(onlyUnknown.cost['RON']!.delta, 0); assert.equal(onlyUnknown.cost['RON']!.unknownAfter, 1); assert.equal(onlyUnknown.isEmpty, false);
});
test('cost: monede diferite rămân separate', () => {
  const c2: Catalog = { ...cat, offers: [...cat.offers.slice(0, 1), { ...cat.offers[1]!, currency: 'EUR' as any, price: 300 }] };
  const d = diffSnapshots(snap([pl('a', 'sofa-0')]), snap([pl('a', 'sofa-0'), pl('b', 'sofa-1')]), c2);
  assert.equal(d.cost['RON']!.delta, 0); assert.equal(d.cost['EUR']!.delta, 300);
});

test('prin repo: revizia 1 → 2 și 1 → draft, 404 și alt proprietar', async () => {
  const id = await repo.createProject(A, 'Diff', 'demo'), p = await repo.getProject(A, id), cat = await repo.getCatalog();
  await repo.createRevision(A, id, 'inițial');
  const s = p.draft, names = (xs: { id: string }[]) => xs.map(x => x.id).sort();
  const mv = s.placements[0]!, rm = s.placements[1]!;
  const sw = s.placements.slice(2).find(x => cat.variants.some(v => groupOf(v.id) === groupOf(x.variantId) && v.id !== x.variantId))!;
  const alt = cat.variants.find(v => groupOf(v.id) === groupOf(sw.variantId) && v.id !== sw.variantId)!;
  const oldPos = { x: mv.x, z: mv.z };
  s.placements = s.placements.filter(x => x.id !== rm.id); mv.x = Math.round((mv.x + 0.05) * 1000) / 1000; sw.variantId = alt.id;
  await repo.saveDraft(A, id, s);
  // draftul modificat poate avea erori de plasare; revizia 2 se salvează doar dacă proiectul e valid
  const d0 = await repo.diffRevisions(A, id, 1, 'draft');
  assert.deepEqual([d0.from, d0.to], [1, 'draft']);
  assert.deepEqual(names(d0.furniture.moved), [mv.id].sort()); assert.deepEqual(names(d0.furniture.removed), [rm.id]); assert.deepEqual(names(d0.furniture.swapped), [sw.id]);
  assert.ok(Math.abs(mv.x - oldPos.x - 0.05) < 1e-6);
  await repo.createRevision(A, id, 'modificat');
  const d = await repo.diffRevisions(A, id, 1, 2);
  assert.deepEqual([d.from, d.to], [1, 2]);
  assert.deepEqual(names(d.furniture.moved), [mv.id]); assert.deepEqual(names(d.furniture.removed), [rm.id]); assert.deepEqual(names(d.furniture.swapped), [sw.id]);
  assert.equal(d.furniture.added.length, 0); assert.equal(d.isEmpty, false);
  assert.equal((await repo.diffRevisions(A, id, 2, 'draft')).isEmpty, true);
  await assert.rejects(repo.diffRevisions(A, id, 1, 9), (e: any) => e.status === 404);
  await assert.rejects(repo.diffRevisions(A, id, 0, 1), (e: any) => e.status === 400);
  await assert.rejects(repo.diffRevisions(A, id, NaN, 'draft'), (e: any) => e.status === 400);
  await assert.rejects(repo.diffRevisions(B, id, 1, 2), (e: any) => e.status === 404);
});
