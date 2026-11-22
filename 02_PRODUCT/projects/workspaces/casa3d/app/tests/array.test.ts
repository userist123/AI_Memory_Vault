// Repetarea în rând (core/arrange.ts arrayGroup): copii ale grupului la pas egal, validate împreună, aspect copiat;
// refuz întreg când o copie iese din cameră sau peste altă piesă; limite pentru număr și pas.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { arrayGroup, stepToFill, ARRAY_LIMITS, type GroupResult } from '../core/arrange';
import type { Catalog, FurniturePlacement, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog;
const put = (id: string, variantId: string, x: number, z: number): FurniturePlacement => ({ id, roomId: 'cam', group: variantId.slice(0, variantId.lastIndexOf('-')), variantId, x, z, rotation: 0, source: 'auto' });
const fixture = (extra: FurniturePlacement[] = []): Snapshot => ({ name: 'Rând', floor: { id: 'f', name: 'P', ceilingHeight: 2.6, walls: [], rooms: [{ id: 'cam', name: 'Sala', type: 'living', rect: { x0: 0, z0: 0, x1: 12, z1: 10 } }] },
  placements: [put('masuta', 'masuta-0', 1.5, 2), ...extra], selections: {}, picked: [], appearance: { items: { masuta: { color: '#224466' } } } });
const ids = (pref: string) => (i: number) => `${pref}${i + 1}`;
const good = (r: GroupResult) => { assert.equal(r.ok, true, r.ok ? '' : r.key); if (!r.ok) throw new Error('refuzat'); assert.equal(r.changed, true); return r; };

test('o piesă repetată de 4 ori la 150 cm pe x: copii la pas egal, id-uri noi, aspect copiat, originalul neatins', () => {
  const s = fixture(), r = good(arrayGroup(s, cat, ['masuta'], 4, 150, 0, ids('c')));
  assert.deepEqual(r.ids, ['c1', 'c2', 'c3', 'c4']);
  const xs = r.ids.map(id => r.snapshot.placements.find(p => p.id === id)!.x); assert.deepEqual(xs, [3, 4.5, 6, 7.5]);
  assert.ok(r.ids.every(id => r.snapshot.placements.find(p => p.id === id)!.z === 2 && r.snapshot.appearance!.items![id]!.color === '#224466'));
  assert.deepEqual(r.snapshot.placements.find(p => p.id === 'masuta'), s.placements[0]); assert.equal(s.placements.length, 1, 'intrarea nu se schimbă');
});
test('un grup de două piese repetat pe diagonală păstrează distanța dintre ele în fiecare copie', () => {
  const s = fixture([put('birou', 'birou-0', 1.5, 4)]), r = good(arrayGroup(s, cat, ['masuta', 'birou'], 2, 300, 100, ids('k')));
  const at = (id: string) => r.snapshot.placements.find(p => p.id === id)!;
  assert.deepEqual(r.ids, ['k1', 'k2', 'k3', 'k4']);
  assert.deepEqual([at('k1').x, at('k1').z, at('k2').x, at('k2').z], [4.5, 3, 4.5, 5]);
  assert.deepEqual([at('k3').x, at('k3').z, at('k4').x, at('k4').z], [7.5, 4, 7.5, 6]);
});
test('refuz întreg: copia care iese din cameră sau peste altă piesă oprește toată repetarea, cu motivul ei', () => {
  const out = arrayGroup(fixture(), cat, ['masuta'], 10, 150, 0, ids('c')); assert.equal(out.ok, false); if (!out.ok){ assert.equal(out.key, 'edit.refused'); assert.equal(out.cause?.severity, 'ERROR'); }
  const blocked = arrayGroup(fixture([put('canapea', 'canapea-0', 4.5, 2)]), cat, ['masuta'], 3, 150, 0, ids('c')); assert.equal(blocked.ok, false);
});
test('limite: număr între 1 și 30, pas finit și nenul, cel mult 20 m', () => {
  const bad = (n: number, x: number, z: number) => { const r = arrayGroup(fixture(), cat, ['masuta'], n, x, z, ids('c')); assert.equal(r.ok, false); if (!r.ok) assert.equal(r.key, 'edit.arrayBad'); };
  bad(0, 100, 0); bad(ARRAY_LIMITS.countMax + 1, 10, 0); bad(2.5, 100, 0); bad(2, 0, 0); bad(2, NaN, 0); bad(2, 2001, 0);
  const none = arrayGroup(fixture(), cat, ['nu-exista'], 2, 100, 0, ids('c')); assert.equal(none.ok, false); if (!none.ok) assert.equal(none.key, 'edit.noPiece');
});
test('stepToFill: pasul pentru ca ultima copie să ajungă exact la distanța dată', () => {
  assert.equal(stepToFill(300, 3), 100); assert.equal(stepToFill(100, 3), 33.3); assert.equal(stepToFill(300, 0), 0);
});
