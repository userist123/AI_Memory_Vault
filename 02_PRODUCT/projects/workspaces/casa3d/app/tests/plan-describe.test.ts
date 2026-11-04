// UI/UX P3-16: echivalentul text al planului: grupe pe camere în ordinea citirii, descrieri în limba interfeței,
// pereții cu camerele vecine, golurile după perete, structura la final.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { describePlan, describeSelection, roomsAlongWall, roomsAtOpening } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import type { Catalog, MaterialsCatalog } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const snap = newSnapshot(cat, 'Descriere', 'demo', 'ro', { materials: mc });

test('grupe: fiecare cameră cu piesele ei, apoi structura; toate obiectele apar o singură dată', () => {
  const g = describePlan(snap, cat, { t: translator('ro'), units: 'metric', lang: 'ro' });
  assert.deepEqual(g.slice(0, -1).map(x => x.id).sort(), snap.floor.rooms.map(r => r.id).sort());
  assert.equal(g.at(-1)!.id, 'structure');
  const pieces = g.flatMap(x => x.items).filter(i => i.kind === 'placement').map(i => i.id).sort();
  assert.deepEqual(pieces, snap.placements.map(p => p.id).sort());
  const walls = g.at(-1)!.items.filter(i => i.kind === 'wall'); assert.equal(walls.length, snap.floor.walls.length);
  const ops = g.at(-1)!.items.filter(i => i.kind === 'opening'); assert.equal(ops.length, snap.floor.walls.reduce((a, w) => a + w.openings.length, 0));
  assert.ok(ops.every(o => o.wallId), 'golul își știe peretele (pentru selecție)');
  // ordinea citirii: camerele de sus în jos
  const z = g.slice(0, -1).map(x => snap.floor.rooms.find(r => r.id === x.id)!.rect.z0); assert.deepEqual(z, [...z].sort((a, b) => a - b));
});
test('descrieri: cameră cu suprafață și laturi, perete cu lungime și camere vecine, în ambele limbi', () => {
  const ro = describePlan(snap, cat, { t: translator('ro'), units: 'metric', lang: 'ro' }), en = describePlan(snap, cat, { t: translator('en'), units: 'imperial', lang: 'en' });
  const room = ro[0]!.items[0]!.label; assert.match(room, /^Cameră .+ · [\d,]+ m² · [\d,]+ m × [\d,]+ m$/, room);
  const wall = ro.at(-1)!.items.find(i => i.kind === 'wall')!.label; assert.match(wall, /^Perete (interior|exterior) · [\d,]+ m · .+$/, wall);
  assert.match(en[0]!.items[0]!.label, /^Room .+ · .+ ft² · /, en[0]!.items[0]!.label);
  assert.ok(ro.flatMap(x => x.items).every(i => i.label && !/\{|\}|undefined|NaN/.test(i.label)), 'fără variabile neînlocuite');
});
test('peretele dintre două camere le numește pe amândouă; selecția își găsește descrierea', () => {
  const inner = snap.floor.walls.find(w => !w.exterior && roomsAlongWall(snap, w).length === 2);
  assert.ok(inner, 'demo-ul are un perete interior între două camere');
  const g = describePlan(snap, cat, { t: translator('ro'), units: 'metric', lang: 'ro' });
  assert.match(describeSelection(g, { kind: 'wall', id: inner!.id })!, /, /);
  assert.equal(describeSelection(g, { kind: 'room', id: 'nu-exista' }), null); assert.equal(describeSelection(g, null), null);
});
test('golurile numesc doar camerele din dreptul lor, nu tot peretele', () => {
  const long = snap.floor.walls.find(w => w.openings.length && roomsAlongWall(snap, w).length > 2)!;
  assert.ok(long, 'demo-ul are un perete lung, cu goluri, între mai mult de două camere');
  for (const o of long.openings){ const at = roomsAtOpening(snap, long, o); assert.ok(at.length >= 1 && at.length <= 2, `${o.id}: ${at.join(', ')}`); }
});
