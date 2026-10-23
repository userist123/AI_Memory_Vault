// Corpurile de iluminat plasate: poziții legate de mobilier, înălțimi, buget, lux, plan/3D și validare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { fixturePoints, fixtureCount, fixtureIssues, sanitizeFixtures, FIXTURE_RULES } from '../core/fixtures';
import { lightReport } from '../core/light-design';
import { footprintOf } from '../core/validate';
import { viewerInput } from '../lib/viewer-input';
import { finishSchedule } from '../core/finish-schedule';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes, LightFixture, Material } from '../core/types';

const cat = catalogV1 as unknown as Catalog, base = materials as unknown as MaterialsCatalog;
const prod = (id: string, category: string, price: number, specs: object = {}): Material => ({ id, category, name: id, supplier: 'Test', unit: 'buc', unitPrice: price, sourceUrl: null, verificationType: 'product_page', confidence: 'HIGH', specs, verifiedAt: '2026-10-11' } as any);
const mc: MaterialsCatalog = { ...base, materials: [...base.materials, prod('pendul', 'pendant', 200, { lumens: 800, cctK: 2700 }), prod('aplica', 'wall_light', 100, { lumens: 400, ip: 'IP20' })] };
const demo = (): Snapshot => newSnapshot(cat, 'Lumini', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const set = (s: Snapshot, id: string, fixtures: LightFixture[]) => { s.finishes = { ...(s.finishes || {}), [id]: { ...finishesOf(s, room(s, id)), fixtures } as RoomFinishes }; return s; };
const fpOf = (s: Snapshot, g: string) => footprintOf(cat, s.placements.find(p => p.group === g)!)!;

test('pendulul stă deasupra mesei, la 75 cm peste blatul ei, și se mută cu masa', () => {
  const s = demo(), [pt] = fixturePoints(s, cat, room(s, 'living'), { kind: 'pendant', anchor: 'table' }), fp = fpOf(s, 'masa');
  assert.ok(pt!.x > fp.x0 && pt!.x < fp.x1 && pt!.z > fp.z0 && pt!.z < fp.z1); assert.equal(pt!.y, FIXTURE_RULES.tableTopM + FIXTURE_RULES.pendantAboveTableM);
  const m = s.placements.find(p => p.group === 'masa')!; m.x += .5; const [moved] = fixturePoints(s, cat, room(s, 'living'), { kind: 'pendant', anchor: 'table' });
  assert.ok(Math.abs(moved!.x - pt!.x - .5) < 1e-9 && Math.abs(moved!.z - pt!.z) < 1e-9);
});
test('aplicele de pat: două, pe peretele de la capul patului, de o parte și de alta, la 1,1 m', () => {
  const s = demo(), pts = fixturePoints(s, cat, room(s, 'dormitor'), { kind: 'sconce', anchor: 'bed' }), fp = fpOf(s, 'pat'), r = room(s, 'dormitor').rect;
  assert.equal(pts.length, 2); assert.ok(pts.every(p => p.y === FIXTURE_RULES.bedSconceM));
  assert.ok(pts.every(p => [r.x0, r.x1, r.z0, r.z1].some(e => { const d = Math.abs((p.nx ? p.x : p.z) - e); return d > .05 && d < .15; })), 'pe fața unui perete');
  const along = pts[0]!.z === pts[1]!.z; const [a, b] = along ? [pts[0]!.x, pts[1]!.x] : [pts[0]!.z, pts[1]!.z], [lo, hi] = along ? [fp.x0, fp.x1] : [fp.z0, fp.z1];
  assert.ok(Math.min(a, b) < lo && Math.max(a, b) > hi, 'de o parte și de alta a patului');
});
test('fără piesa-ancoră nu se pune nimic și apare avertismentul; în centru sub 2,1 m e prea jos', () => {
  const s = demo(); assert.deepEqual(fixturePoints(s, cat, room(s, 'hol'), { kind: 'pendant', anchor: 'table' }), []);
  const f: RoomFinishes = { ...finishesOf(s, room(s, 'hol')), fixtures: [{ kind: 'pendant', anchor: 'table', material: 'pendul' }, { kind: 'pendant', anchor: 'center', heightM: 1.9, material: 'pendul' }] };
  assert.deepEqual(fixtureIssues(s, cat, mc, room(s, 'hol'), f).map(i => i.key), ['fix.noAnchor', 'fix.headroom']);
  const b: RoomFinishes = { ...finishesOf(s, room(s, 'baie')), fixtures: [{ kind: 'sconce', anchor: 'vanity', material: 'aplica' }] };
  assert.deepEqual(fixtureIssues(s, cat, mc, room(s, 'baie'), b).map(i => i.key), ['fix.ip']);
});
test('buget, fișă și lux: produsul ales se numără, aplicele cer punct electric, fără produs e necunoscut', () => {
  const s = set(demo(), 'living', [{ kind: 'pendant', anchor: 'table', material: 'pendul', count: 2 }, { kind: 'sconce', anchor: 'sofa' }]), b = computeBOQ(s, cat, mc);
  const it = b.items.find(i => i.key === 'living:fixture:0')!; assert.equal(it.orderedQty, 2); assert.equal(it.category, 'lighting');
  assert.ok(b.unknown.some(u => /Aplică ×2 \(produs neales\)/.test(u)) && b.unknown.some(u => /Punct electric/.test(u)));
  assert.ok(finishSchedule(s, cat, mc).some(r => r.element === 'fixture' && r.orderedQty === 2));
  const before = lightReport(mc, s.floor, room(s, 'living'), finishesOf(demo(), room(s, 'living'))).lumens, after = lightReport(mc, s.floor, room(s, 'living'), finishesOf(s, room(s, 'living'))).lumens;
  assert.equal(after - before, 1600);
  assert.equal(fixtureCount({ kind: 'pendant', anchor: 'counter' }), 3);
});
test('plan/3D primesc corpurile; validare', () => {
  const s = set(demo(), 'living', [{ kind: 'track', anchor: 'center', lengthM: 1.5 }]), v = viewerInput(s, cat, undefined, { mc });
  assert.equal((v.plan as any).lumini.length, 1); assert.equal((v.plan as any).lumini[0].len, 1.5);
  assert.equal(sanitizeFixtures(undefined), null); assert.equal(sanitizeFixtures([{ kind: 'pendant', anchor: 'table', count: 3, color: '#000000' }]), null);
  for (const bad of [{}, [{ kind: 'lamp', anchor: 'table' }], [{ kind: 'pendant', anchor: 'roof' }], [{ kind: 'pendant', anchor: 'table', count: 50 }], [{ kind: 'sconce', anchor: 'wall', side: 'X' }], [{ kind: 'track', anchor: 'center', lengthM: 20 }]])
    assert.equal(sanitizeFixtures(bad), 'Corpurile de iluminat sunt invalide.', JSON.stringify(bad));
});

// regresii din review: peretele din spatele patului vine din rotație, nu din cel mai apropiat perete; aplicele rămân în cameră
test('pat împins în colț (rotație 0, tăblia la N): aplicele stau pe peretele N, în cameră', () => {
  const s = demo(), bed = s.placements.find(p => p.group === 'pat')!, r = room(s, 'dormitor').rect; bed.rotation = 0;
  const fp0 = footprintOf(cat, bed)!, w = fp0.x1 - fp0.x0, d = fp0.z1 - fp0.z0; bed.x = r.x0 + w / 2 + .03; bed.z = r.z0 + d / 2 + .1;
  const pts = fixturePoints(s, cat, room(s, 'dormitor'), { kind: 'sconce', anchor: 'bed' });
  assert.ok(pts.every(p => p.nz === 1 && p.nx === 0), 'peretele N'); assert.ok(pts.every(p => p.x >= r.x0 && p.x <= r.x1 && Math.abs(p.z - r.z0) < .15), JSON.stringify(pts));
});
test('șina și pendulele stau sub tavanul fals; pendulele de lângă pat sunt cel mult 2; pendule peste blat cu suspendate = avertisment', () => {
  const s = set(demo(), 'living', [{ kind: 'track', anchor: 'center' }]); s.finishes!.living!.ceiling = { type: 'cove', dropCm: 10, coveCm: 30 };
  const [tr] = fixturePoints(s, cat, room(s, 'living'), { kind: 'track', anchor: 'center' }); assert.ok(tr!.y < s.floor.ceilingHeight - .1 && tr!.top === s.floor.ceilingHeight - .1);
  assert.equal(fixtureCount({ kind: 'pendant', anchor: 'bed', count: 3 }), 2);
  const b = set(demo(), 'dormitor', [{ kind: 'pendant', anchor: 'bed', count: 3, material: 'pendul' }]); assert.equal(computeBOQ(b, cat, mc).items.find(i => i.key === 'dormitor:fixture:0')!.orderedQty, 2);
  const k = set(demo(), 'bucatarie', [{ kind: 'pendant', anchor: 'counter', material: 'pendul' }]);
  assert.ok(fixtureIssues(k, cat, mc, room(k, 'bucatarie'), finishesOf(k, room(k, 'bucatarie'))).some(i => i.key === 'fix.counterUpper'));
});
