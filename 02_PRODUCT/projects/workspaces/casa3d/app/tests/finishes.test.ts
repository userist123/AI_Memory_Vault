// Finisaje de designer: modul de așezare a pardoselii, placări pe pereți, tavane false, cantități și avertismente tehnice.
import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { wallpaperRolls, panelCount, floorWaste, sideGeometry, finishIssues, defaultLayout, materialOf, clearHeight, sanitizeFinishes } from '../core/finishes';
import { getDb, resetDbForTests, reseedForTests } from '../lib/db';
import * as repo from '../lib/repo';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Finisaje', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const set = (s: Snapshot, id: string, patch: Partial<RoomFinishes>) => { s.finishes = { ...(s.finishes || {}), [id]: { ...finishesOf(s, room(s, id)), ...patch } }; return s; };
const keys = (s: Snapshot, id: string) => finishIssues(s, mc, s.floor, room(s, id), finishesOf(s, room(s, id))).map(i => i.key);

describe('calcule de bază', () => {
  test('tapet: fâșii pe lungime, câte fâșii dintr-o rolă, role întregi', () => {
    assert.deepEqual(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 0 }), { strips: 7, perRoll: 3, rolls: 3 });
    // un raport de 64 cm lasă doar 2 fâșii pe rolă
    assert.deepEqual(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 64 }), { strips: 7, perRoll: 3, rolls: 3 });
    assert.deepEqual(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 80 }), { strips: 7, perRoll: 2, rolls: 4 });
  });
  test('riflaj: coloane × rânduri de panouri', () => { assert.equal(panelCount(3, 2.4, [60, 240]), 5); assert.equal(panelCount(3.1, 2.6, [60, 240]), 12); });
  test('pierderi după modul de așezare și format', () => {
    const egger = materialOf(mc, 'parchet-egger-h2099'), big = materialOf(mc, 'gresie-emarble-60x120'), small = materialOf(mc, 'gresie-mckinley');
    assert.equal(floorWaste({ pattern: 'third' }, egger), .10); assert.equal(floorWaste({ pattern: 'herringbone' }, egger), .15); assert.equal(floorWaste({ pattern: 'chevron' }, egger), .18);
    assert.equal(floorWaste({ pattern: 'straight' }, small), .10); assert.ok(Math.abs(floorWaste({ pattern: 'straight' }, big) - .15) < 1e-9, 'formatul mare adaugă 5 %');
    assert.deepEqual(defaultLayout(small), { pattern: 'straight', groutMm: 3, groutColor: '#bdb8ae' });
    assert.equal(defaultLayout(materialOf(mc, 'parchet-krono-herringbone-k450')).pattern, 'herringbone');
  });
  test('latura unei camere: lungime și suprafață netă fără uși și ferestre', () => {
    const s = demo(), g = sideGeometry(s.floor, room(s, 'living'), 'N');
    assert.equal(g.lengthM, 5.6); assert.ok(g.netM2 < 5.6 * 2.6 && g.netM2 > 0);
    assert.equal(sideGeometry(s.floor, room(s, 'living'), 'N', 1.2).heightM, 1.2);
  });
});

describe('cantități (BOQ)', () => {
  test('parchet în spic: 15 % pierderi și manopera de spic', () => {
    const s = set(demo(), 'living', { floor: 'parchet-krono-herringbone-k450', floorLayout: { pattern: 'herringbone' } }), b = computeBOQ(s, cat, mc);
    const fl = b.items.find(i => i.key === 'living:floor')!; assert.equal(fl.wastePct, .15); assert.match(fl.label, /spic/);
    assert.ok(b.labor.some(l => l.key === 'living:manopera-parchet-spic')); assert.ok(!b.labor.some(l => l.key === 'living:manopera-parchet'));
    assert.equal(fl.verifiedAt, '2026-10-10');
  });
  test('gresie 60×120: manopera de format mare și pierderi mai mari', () => {
    const b = computeBOQ(set(demo(), 'baie', { floor: 'gresie-emarble-60x120' }), cat, mc);
    assert.ok(b.labor.some(l => l.key === 'baie:manopera-gresie-mare')); assert.ok(Math.abs(b.items.find(i => i.key === 'baie:floor')!.wastePct - .15) < 1e-9);
  });
  test('tapet, riflaj, cărămidă și tencuială decorativă pe pereți diferiți; vopseaua scade cu suprafața placată', () => {
    const base = computeBOQ(demo(), cat, mc).items.find(i => i.key === 'living:paint')!.netQty;
    const s = set(demo(), 'living', { wallFeatures: [
      { side: 'N', kind: 'wallpaper', material: 'tapet-grandeco-marmor' }, { side: 'E', kind: 'slats', material: 'riflaj-mdf-unic-alb', heightM: 2.4 },
      { side: 'S', kind: 'brick', material: 'caramida-bronx-60', heightM: 1.2 }, { side: 'W', kind: 'plaster', material: 'tencuiala-kober-bob-orez' } ] });
    const b = computeBOQ(s, cat, mc), it = (k: string) => b.items.find(i => i.key.startsWith(`living:wall:${k}`))!;
    const n = sideGeometry(s.floor, room(s, 'living'), 'N'); assert.equal(it('N').orderedQty, wallpaperRolls(n.lengthM, n.heightM, { widthM: .53, lengthM: 10.05, repeatCm: 0 }).rolls);
    assert.equal(it('E').orderedQty, panelCount(3.8, 2.4, [60, 240])); assert.equal(it('E').unit, 'buc');
    assert.equal(it('S').unit, 'm2'); assert.ok(it('S').packs! >= 1); assert.equal(it('W').unit, 'kg');
    assert.ok(b.labor.some(l => l.key === 'living:manopera-tapet') && b.labor.some(l => l.key === 'living:manopera-tencuiala-decorativa'));
    // riflajul și cărămida nu au manoperă verificată: apar ca necunoscute, nu ca 0
    assert.ok(b.unknown.some(u => /riflaj/i.test(u)) && b.unknown.some(u => /cărămidă/i.test(u)));
    assert.ok(b.items.find(i => i.key === 'living:paint')!.netQty < base);
  });
  test('tavan fals cu scafă: plăci, bandă LED pe conturul interior, manoperă pe m² și pe ml; cornișă și spoturi', () => {
    const s = set(demo(), 'living', { ceiling: { type: 'cove', dropCm: 15, coveCm: 25, cornice: 'cornisa-nmc-nc109', spot: 'spot-mt143-9w', spots: 6 } }), b = computeBOQ(s, cat, mc);
    const inner = 2 * ((5.6 - .5) + (3.8 - .5));
    assert.ok(b.items.some(i => i.key === 'living:ceiling' && i.refId === 'rigips-rb-12-5'));
    const led = b.items.find(i => i.key === 'living:led')!; assert.ok(Math.abs(led.netQty - inner) < .02); assert.equal(led.category, 'lighting'); assert.equal(led.packs, Math.ceil(inner * 1.05 / 5));
    assert.ok(Math.abs(b.labor.find(l => l.key === 'living:manopera-scafa')!.qty - inner) < .02);
    assert.equal(b.items.find(i => i.key === 'living:cornice')!.packs, Math.ceil(18.8 * 1.1 / 2)); assert.equal(b.items.find(i => i.key === 'living:spots')!.orderedQty, 6);
    assert.equal(clearHeight(s.floor, finishesOf(s, room(s, 'living'))), 2.45);
  });
  test('un proiect fără finisaje noi dă exact aceleași linii ca înainte', () => {
    const b = computeBOQ(demo(), cat, mc); assert.ok(!b.items.some(i => /:wall:|:ceiling|:led|:cornice|:spots/.test(i.key)));
    assert.ok(b.labor.some(l => l.key === 'living:manopera-parchet') && b.labor.some(l => l.key === 'baie:manopera-gresie'));
  });
});

describe('avertismente tehnice', () => {
  test('baie: gresie fără clasă antiderapantă declarată; cu R10 nu mai apare', () => {
    assert.ok(keys(demo(), 'baie').includes('fin.slipUnknown'));
    assert.ok(!keys(set(demo(), 'baie', { floor: 'gresie-emarble-60x120' }), 'baie').some(k => k.startsWith('fin.slip')));
  });
  test('rost prea subțire, spot IP20 în baie, tavan prea jos, tapet și MDF în baie, spic cu parchet obișnuit', () => {
    assert.ok(keys(set(demo(), 'baie', { floorLayout: { pattern: 'straight', groutMm: 2 } }), 'baie').includes('fin.groutTooThin'));
    assert.ok(keys(set(demo(), 'baie', { ceiling: { type: 'drop', dropCm: 10, spot: 'spot-mt143-9w', spots: 4 } }), 'baie').includes('fin.ipLow'));
    assert.ok(keys(set(demo(), 'living', { ceiling: { type: 'drop', dropCm: 30 } }), 'living').includes('fin.ceilingLow'));
    assert.ok(keys(set(demo(), 'baie', { wallFeatures: [{ side: 'N', kind: 'wallpaper', material: 'tapet-grandeco-marmor' }] }), 'baie').includes('fin.notForWet'));
    assert.ok(keys(set(demo(), 'living', { floorLayout: { pattern: 'herringbone' } }), 'living').includes('fin.patternProduct'));
    assert.ok(keys(set(demo(), 'living', { floor: 'parchet-krono-herringbone-k450', floorLayout: { pattern: 'straight' } }), 'living').includes('fin.herringboneOnly'));
    assert.deepEqual(keys(demo(), 'living'), []);
  });
});

describe('salvare și bază de date', () => {
  test('finisaje invalide sunt respinse; cele valide se păstrează', async () => {
    assert.equal(sanitizeFinishes({ x: { floorLayout: { pattern: 'spirala' } } }), 'Modul de așezare a pardoselii este invalid.');
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'Q', kind: 'wallpaper', material: 'a' }] } }), 'Placările de pe pereți sunt invalide.');
    assert.equal(sanitizeFinishes({ x: { ceiling: { type: 'drop', dropCm: 500 } } }), 'Tavanul este invalid.');
    resetDbForTests(); const A = '99999999-9999-4999-8999-999999999999', id = await repo.createProject(A, 'F', 'demo'), s = demo();
    set(s, 'living', { floorLayout: { pattern: 'chevron', angle: 90 }, wallFeatures: [{ side: 'N', kind: 'stone', material: 'piatra-modulo-oslo-white', heightM: 1.5 }], ceiling: { type: 'cove', dropCm: 12, coveCm: 20 } });
    await repo.saveDraft(A, id, s); const got = (await repo.getProject(A, id)).draft.finishes!['living']!;
    assert.equal(got.floorLayout!.pattern, 'chevron'); assert.equal(got.wallFeatures![0]!.kind, 'stone'); assert.equal(got.ceiling!.type, 'cove');
    const bad = structuredClone(s); (bad.finishes!['living'] as any).ceiling = { type: 'dome' };
    await assert.rejects(repo.saveDraft(A, id, bad), (e: any) => e.status === 400);
  });
  test('materialele noi ajung și într-o bază existentă; prețul pus manual rămâne', async () => {
    resetDbForTests(); const { q } = await getDb();
    await q("delete from materials where id = 'tapet-grandeco-marmor'"); await q("update materials set unit_price = 1, specs = null where id = 'gresie-mckinley'");
    await reseedForTests(); const mc2 = await repo.getMaterials();
    assert.ok(mc2.materials.some(m => m.id === 'tapet-grandeco-marmor'));
    const g = mc2.materials.find(m => m.id === 'gresie-mckinley')!; assert.equal(g.unitPrice, 1); assert.deepEqual(g.specs, { sizeCm: [60, 60], rectified: true });
  });
});
