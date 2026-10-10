import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import { newSnapshot, autoLayout, addPlacement } from '../core/project';
import { validatePlacement, validateFloor, severityOf } from '../core/validate';
import { History } from '../core/history';
import { resolve } from '../core/catalog';
import type { Catalog, Snapshot } from '../core/types';
const cat = catalogV1 as unknown as Catalog;
const demo = () => newSnapshot(cat, 'Test', 'demo');
const byGroup = (s: Snapshot, g: string) => s.placements.find(p => p.group === g)!;

describe('amenajare automată în noul model', () => {
  test('proiect demo: 19 piese, toate fără erori', () => {
    const s = demo(); assert.equal(s.placements.length, 19);
    for (const p of s.placements) assert.notEqual(severityOf(validatePlacement(s, cat, p)), 'ERROR', `${p.group}: ${JSON.stringify(validatePlacement(s, cat, p))}`);
  });
  test('LACUNA 5 reparată: motorul nu mai schimbă selecția utilizatorului', () => {
    const s = demo(); assert.equal(s.selections.masa, 'masa-0');
    assert.notEqual(byGroup(s, 'masa').variantId, 'masa-0', 'varianta care încape e salvată pe plasare');
  });
  test('LACUNA 3 reparată: ID-urile pieselor sunt stabile (UUID salvat), nu depind de ordine', () => {
    const s = demo(), ids = s.placements.map(p => p.id); assert.ok(ids.every(i => /^[0-9a-f-]{36}$/.test(i)));
    const again: Snapshot = JSON.parse(JSON.stringify(s)); assert.deepEqual(again.placements.map(p => p.id), ids);
  });
  test('re-amenajarea unei singure camere nu atinge celelalte camere', () => {
    const s = demo(), other = s.placements.filter(p => p.roomId !== 'living').map(p => p.id);
    const r = autoLayout(s, cat, { roomId: 'living' }).snapshot; assert.deepEqual(r.placements.filter(p => p.roomId !== 'living').map(p => p.id), other);
  });
  test('adăugare mobilier: găsește loc valid', () => {
    const s = demo(), r = addPlacement(s, cat, 'hol', 'biblioteca-0'); assert.ok(r);
    const p = r!.placements.at(-1)!; assert.equal(severityOf(validatePlacement(r!, cat, p)), 'PASS');
  });
});
describe('validare (ERROR / WARNING)', () => {
  test('LACUNA 1 reparată: dulap în fața ferestrei → WARNING', () => {
    const s = demo(), d = byGroup(s, 'dulap'); Object.assign(d, { x: 8.8 - .25 - .01, z: 5.4, rotation: -Math.PI / 2 });
    const iss = validatePlacement(s, cat, d); assert.ok(iss.some(i => i.code === 'WINDOW_BLOCKED' && i.severity === 'WARNING'), JSON.stringify(iss));
  });
  test('LACUNA 2 reparată: măsuța lipită de canapea → WARNING de circulație', () => {
    const s = demo(), m = byGroup(s, 'masuta'), sofa = byGroup(s, 'canapea'), rs = resolve(cat, sofa.variantId)!;
    Object.assign(m, { x: sofa.x, z: sofa.z - rs.d / 2 - .39 - .02, rotation: 0 });
    assert.ok(validatePlacement(s, cat, m).some(i => i.code === 'CLEARANCE'));
  });
  test('dulap peste ușă → ERROR', () => { const s = demo(), d = byGroup(s, 'dulap'); Object.assign(d, { x: 3.2 + .6, z: 4.4, rotation: Math.PI / 2 }); assert.equal(severityOf(validatePlacement(s, cat, d)), 'ERROR'); });
  test('piesă în afara camerei → ERROR', () => { const s = demo(), m = byGroup(s, 'masuta'); Object.assign(m, { x: -1, z: -1 }); assert.ok(validatePlacement(s, cat, m).some(i => i.code === 'OUT_OF_ROOM')); });
  test('suprapunere → ERROR, dar scaunul sub birou e permis', () => {
    const s = demo(), m = byGroup(s, 'masuta'), sofa = byGroup(s, 'canapea'); Object.assign(m, { x: sofa.x, z: sofa.z });
    assert.ok(validatePlacement(s, cat, m).some(i => i.code === 'OVERLAP'));
    assert.ok(!validatePlacement(s, cat, byGroup(s, 'scaunBirou')).some(i => i.code === 'OVERLAP'));
  });
  test('canapea prea mare (cu șezlong) în hol → ERROR', () => {
    const s = demo(); const p = { ...byGroup(s, 'canapea'), id: 'x', roomId: 'hol', variantId: 'canapea-4', x: 1.6, z: 3.8, rotation: 0 }; s.placements.push(p);
    assert.equal(severityOf(validatePlacement(s, cat, p)), 'ERROR');
  });
  test('varianta per piesă: o noptieră poate avea altă variantă decât cealaltă (LACUNA 4)', () => {
    const s = demo(), n = s.placements.filter(p => p.group === 'noptiera'); n[1].variantId = 'noptiera-0'; n[0].variantId = 'noptiera-0';
    n[1].variantId = 'noptiera-2'; assert.notEqual(n[0].variantId, n[1].variantId); assert.notEqual(severityOf(validatePlacement(s, cat, n[1])), undefined);
  });
  test('perete: gol care iese din perete → ERROR; perete prea scurt → ERROR', () => {
    const s = demo(); s.floor.walls[0].openings[0].offset = 50; assert.ok(validateFloor(s.floor).some(i => i.code === 'OPENING_OUTSIDE_WALL'));
    s.floor.walls[1].b = [8.8, .1]; assert.ok(validateFloor(s.floor).some(i => i.code === 'WALL_TOO_SHORT'));
  });
});
describe('undo / redo', () => {
  test('mutare → undo → redo', () => {
    const h = new History<Snapshot>(); let s = demo(); const x0 = byGroup(s, 'masuta').x;
    h.push(s); s = JSON.parse(JSON.stringify(s)); byGroup(s, 'masuta').x = x0 + .5;
    s = h.undo(s)!; assert.equal(byGroup(s, 'masuta').x, x0); s = h.redo(s)!; assert.equal(byGroup(s, 'masuta').x, x0 + .5); assert.equal(h.canRedo, false);
  });
  test('o acțiune nouă golește redo', () => { const h = new History<number>(); h.push(1); h.undo(2); assert.equal(h.canRedo, true); h.push(3); assert.equal(h.canRedo, false); });
});
