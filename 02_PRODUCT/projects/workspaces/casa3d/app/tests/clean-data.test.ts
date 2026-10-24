// UI/UX P1-1 (raport „Casa3D UI UX ultimă generație”): date curate pe link și în documente — nume de camere în limba
// celui care citește (doar numele implicite, după tipul camerei), culori cu nume, nu coduri hex brute.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ } from '../core/boq';
import { localizeRoomNames, localizeSnapshotRoomNames } from '../core/templates';
import type { Catalog, MaterialsCatalog, Snapshot, Floor } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const HEX = /#[0-9a-f]{6}\b/i;

test('etichetele BOQ (deviz, proformă, comenzi) nu conțin coduri hex; vopseaua are numele culorii', () => {
  for (const lang of ['ro', 'en'] as const){
    const snap = newSnapshot(cat, 'Curat', 'demo', lang, { materials: mc }), { items } = computeBOQ(snap, cat, mc);
    const bad = items.map(i => i.label).filter(l => HEX.test(l));
    assert.deepEqual(bad, [], `${lang}: etichete cu hex`);
    const paint = items.map(i => i.label).filter(l => /^Vopsea nuanță /.test(l));
    for (const l of paint) assert.match(l, /^Vopsea nuanță [a-zăâîșț]+( [a-zăâîșț]+){0,2} · /, l);
    assert.deepEqual(items.map(i => i.label).filter(l => /perete [NSEW]/.test(l)), [], `${lang}: litere de perete`);
  }
});
test('numele implicite ale camerelor se traduc din orice limbă în limba cititorului; cele personalizate rămân', () => {
  const f = { rooms: [{ type: 'bucatarie', name: 'Kitchen' }, { type: 'dormitor', name: 'Bedroom 2' }, { type: 'baie', name: 'Baie' }, { type: 'living', name: 'Camera lui Andrei' }, { type: 'hol', name: 'Hall' }] } as unknown as Floor;
  assert.deepEqual(localizeRoomNames(f, 'ro').rooms.map(r => r.name), ['Bucătărie', 'Dormitor 2', 'Baie', 'Camera lui Andrei', 'Hol']);
  assert.deepEqual(localizeRoomNames(f, 'en').rooms.map(r => r.name), ['Kitchen', 'Bedroom 2', 'Bathroom', 'Camera lui Andrei', 'Hall']);
  const ro = { rooms: [{ type: 'baie', name: 'Baie' }] } as unknown as Floor; assert.equal(localizeRoomNames(ro, 'ro'), ro, 'nimic de schimbat → același obiect');
});
test('snapshot: parterul și etajele se traduc; fără schimbări se întoarce același obiect (fără salvări inutile)', () => {
  const en = newSnapshot(cat, 'Casa', 'demo', 'en', { materials: mc });
  const lvl = structuredClone(en.floor); lvl.id = 'etaj-1'; lvl.rooms = lvl.rooms.map(r => ({ ...r, id: r.id + '-e1' }));
  const two: Snapshot = { ...en, levels: [lvl] }, ro = localizeSnapshotRoomNames(two, 'ro');
  assert.ok(ro.floor.rooms.some(r => r.name === 'Bucătărie') && ro.levels![0]!.rooms.some(r => r.name === 'Bucătărie'));
  assert.ok(!JSON.stringify(ro.floor.rooms).includes('Kitchen'));
  assert.equal(localizeSnapshotRoomNames(ro, 'ro'), ro);
  assert.ok(two.floor.rooms.some(r => r.name === 'Kitchen'), 'originalul nu se modifică');
});
