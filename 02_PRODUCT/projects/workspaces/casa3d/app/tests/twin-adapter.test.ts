import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { snapshotToTwin, placementsFromTwin, twinCatalogItems, toRotation, toRadians } from '../lib/twin';
import { validate, fingerprint } from '@casa3d/twin-core';
import { validatePlacement, severityOf } from '../core/validate';
beforeAll(() => resetDbForTests());
const A = '33333333-3333-4333-8333-333333333333';

test('rotațiile se convertesc în ambele sensuri', () => {
  assert.equal(toRotation(0), 0); assert.equal(toRotation(Math.PI / 2), 90); assert.equal(toRotation(Math.PI), 180); assert.equal(toRotation(-Math.PI / 2), 270); assert.equal(toRotation(3 * Math.PI / 2), 270);
  for (const r of [0, 90, 180, 270] as const) assert.equal(toRotation(toRadians(r)), r);
});
test('proiectul demo devine un Digital Twin valid, iar piesele revin identic', async () => {
  const cat = await repo.getCatalog(), id = await repo.createProject(A, 'Twin demo', 'demo'), p = await repo.getProject(A, id), snap = p.draft;
  const twin = snapshotToTwin(snap, cat);
  assert.equal(twin.rooms.length, 5); assert.equal(twin.walls.length, snap.floor.walls.length);
  assert.ok(twin.walls.every(w => w.roomIds.length >= 1), 'fiecare perete e legat de cel puțin o cameră');
  assert.ok(twin.walls.some(w => w.roomIds.length === 2), 'pereții interiori sunt comuni la două camere');
  assert.equal(twin.openings.length, snap.floor.walls.reduce((a, w) => a + w.openings.length, 0));
  assert.equal(twin.placements.length, snap.placements.length, 'toate cele 19 piese demo au dimensiuni în catalog');
  const v = validate(twin); assert.equal(v.issues.filter(i => i.severity === 'ERROR').length, 0, JSON.stringify(v.issues.filter(i => i.severity === 'ERROR')));
  const back = placementsFromTwin(snap, cat, twin);
  assert.equal(back.length, snap.placements.length);
  for (const b of back){ const o = snap.placements.find(q => q.id === b.id)!; assert.ok(Math.abs(b.x - o.x) < 0.0015 && Math.abs(b.z - o.z) < 0.0015, `${o.group}: centrul se păstrează`); assert.equal(b.variantId, o.variantId); assert.equal(b.source, o.source); assert.equal(Math.round(b.rotation / (Math.PI / 2)) % 4, ((Math.round(o.rotation / (Math.PI / 2)) % 4) + 4) % 4); }
  assert.equal(fingerprint(twin), fingerprint(snapshotToTwin({ ...snap, placements: back }, cat)), 'dus-întors nu schimbă amprenta');
  assert.ok(snap.placements.every(pl => severityOf(validatePlacement({ ...snap, placements: back }, cat, pl)) !== 'ERROR'));
});
test('catalogul twin păstrează prețurile cunoscute, provenienţa și UNKNOWN', async () => {
  const cat = await repo.getCatalog(), items = twinCatalogItems(cat);
  assert.ok(items.length >= 60 && items.length <= cat.variants.length);
  const sofa = items.find(i => i.id === 'canapea-0')!;
  assert.deepEqual(sofa.price, { amount: 2299, currency: 'RON' }); assert.equal(sofa.retailer, 'IKEA'); assert.equal(sofa.role, 'canapea'); assert.equal(sofa.w, 2.28);
  assert.equal(sofa.provenance?.verifiedAt, '2026-09-30');
  const noDims = cat.variants.filter(v => !v.dimensionsCm).length; assert.equal(items.length, cat.variants.length - noDims);
});
