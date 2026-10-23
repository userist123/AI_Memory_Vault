// Finisaje de designer: modul de așezare a pardoselii, placări pe pereți, tavane false, cantități și avertismente tehnice.
import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { wallpaperRolls, panelCount, floorWaste, sideGeometry, finishIssues, defaultLayout, materialOf, clearHeight, sanitizeFinishes, roomVisual } from '../core/finishes';
import { viewerInput } from '../lib/viewer-input';
import { lightReport, autoLightCount } from '../core/light-design';
import { getDb, resetDbForTests, reseedForTests } from '../lib/db';
import * as repo from '../lib/repo';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Finisaje', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const set = (s: Snapshot, id: string, patch: Partial<RoomFinishes>) => { s.finishes = { ...(s.finishes || {}), [id]: { ...finishesOf(s, room(s, id)), ...patch } }; return s; };
const keys = (s: Snapshot, id: string) => finishIssues(s, mc, s.floor, room(s, id), finishesOf(s, room(s, id))).map(i => i.key);

describe('calcule de bază', () => {
  test('tapet: fâșia rotunjită la multiplu de raport, potrivirea decalată, rezerva rotunjită la rolă', () => {
    // fără raport: fâșia = 2,6 + 5 cm; 7 fâșii, 3 pe rolă → 3 role + 10 % rezervă = 4
    assert.deepEqual(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 0 }), { strips: 7, perRoll: 3, rolls: 4, spare: 1, dropM: 2.65, match: 'free' });
    // raport de 64 cm: 2,65 m se rotunjește la 5 × 0,64 = 3,20 m (nu 2,6 + 0,64 + 0,05 = 3,29 m)
    assert.deepEqual(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 64 }), { strips: 7, perRoll: 3, rolls: 4, spare: 1, dropM: 3.2, match: 'straight' });
    // potrivire decalată: + jumătate de raport (3,52 m), doar 2 fâșii pe rolă, rezervă 15 %
    assert.deepEqual(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 64, match: 'offset' }), { strips: 7, perRoll: 2, rolls: 5, spare: 1, dropM: 3.52, match: 'offset' });
    assert.equal(wallpaperRolls(3.5, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 80 }).dropM, 3.2);
    assert.equal(wallpaperRolls(20, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 0 }).spare, 2, '38 fâșii → 13 role + 10 % = 15');
    assert.equal(wallpaperRolls(0, 2.6, { widthM: .53, lengthM: 10.05, repeatCm: 0 }).rolls, 0);
  });
  test('riflaj: coloane × rânduri de panouri', () => { assert.equal(panelCount(3, 2.4, [60, 240]), 5); assert.equal(panelCount(3.1, 2.6, [60, 240]), 7, 'restul de 20 cm se taie din panouri: 12 bucăți dintr-unul'); });
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
    assert.deepEqual(keys(demo(), 'living'), ['light.low', 'light.oneLayer'], 'implicit: 2 × 800 lm în 21 m² e prea puțin și e o singură sursă (doar plafoniera)');
    assert.ok(!keys(set(demo(), 'living', { fixtures: [{ kind: 'sconce', anchor: 'sofa' }] }), 'living').includes('light.oneLayer'), 'aplicele adaugă stratul de accent');
    assert.ok(keys(set(demo(), 'baie', { fixtures: [], bath: { mirror: null } }), 'baie').includes('light.noTaskBath'));
    assert.ok(!keys(set(demo(), 'baie', { bath: { mirror: 'ikea-faxalven-oglinda-iluminat-60x95' } }), 'baie').includes('light.noTaskBath'), 'oglinda cu LED e lumina de lucru a băii');
  });
});

describe('regresii din review (2026-10-10)', () => {
  test('faianța băii nu se numără de două ori când un perete are altă placare; vopseaua scade o singură dată', () => {
    const plain = computeBOQ(demo(), cat, mc), s = set(demo(), 'baie', { wallFeatures: [{ side: 'N', kind: 'tile', material: 'faianta-lane-blanco' }] }), b = computeBOQ(s, cat, mc);
    const n = sideGeometry(s.floor, room(s, 'baie'), 'N', 2.1).netM2, tile0 = plain.items.find(i => i.key === 'baie:walltile')!.netQty, tile1 = b.items.find(i => i.key === 'baie:walltile')!.netQty;
    assert.ok(Math.abs(tile0 - tile1 - n) < .02, `${tile0} - ${tile1} = ${n}`);
    const full = sideGeometry(s.floor, room(s, 'baie'), 'N').netM2, p0 = plain.items.find(i => i.key === 'baie:paint')!.netQty, p1 = b.items.find(i => i.key === 'baie:paint')!.netQty;
    assert.ok(p1 < p0 && p1 > 0, 'vopseaua scade, dar nu sub zero'); void full;
  });
  test('același perete nu poate avea două placări; tipuri și valori necunoscute dau 400, nu 500', () => {
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'wallpaper', material: 'a' }, { side: 'N', kind: 'tile', material: 'b' }] } }), 'Placările de pe pereți sunt invalide.');
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'toString', material: 'a' }] } }), 'Placările de pe pereți sunt invalide.');
    assert.equal(sanitizeFinishes({ x: { floorLayout: null } }), 'Modul de așezare a pardoselii este invalid.');
    assert.equal(sanitizeFinishes({ x: { ceiling: null } }), 'Tavanul este invalid.');
    assert.equal(sanitizeFinishes({ x: { floor: 'x'.repeat(500) } }), 'Finisajele sunt invalide.');
  });
  test('scafa fără bandă LED: nicio linie de LED', () => {
    const b = computeBOQ(set(demo(), 'living', { ceiling: { type: 'cove', dropCm: 15, coveCm: 25, led: null } }), cat, mc);
    assert.ok(!b.items.some(i => i.key === 'living:led')); assert.ok(b.labor.some(l => l.key === 'living:manopera-scafa'));
  });
  test('tavanul fals simplu scade vopseaua pereților; cel cu scafă adaugă doar fâșia scafei', () => {
    const paint = (s: Snapshot) => computeBOQ(s, cat, mc).items.find(i => i.key === 'living:paint')!.netQty, board = (s: Snapshot) => computeBOQ(s, cat, mc).items.find(i => i.key === 'living:ceiling')!.netQty;
    const flat = paint(demo()), drop = set(demo(), 'living', { ceiling: { type: 'drop', dropCm: 60 } }), cove = set(demo(), 'living', { ceiling: { type: 'cove', dropCm: 15, coveCm: 25 } });
    assert.ok(paint(drop) < flat, `${paint(drop)} < ${flat}`); assert.ok(Math.abs(board(drop) - 21.28) < .02, 'placa acoperă tavanul, fără margine');
    const iw = 5.6 - .5, id = 3.8 - .5; assert.ok(Math.abs(board(cove) - (iw * id + 2 * (iw + id) * .15)) < .02);
  });
  test('o latură fără perete (deschisă) nu are ce placa', () => {
    const s = demo(), r = room(s, 'living'); s.floor.walls = s.floor.walls.filter(w => !(Math.abs(w.a[1] - w.b[1]) < 1e-6 && Math.abs(w.a[1] - r.rect.z0) < 1e-6));
    assert.equal(sideGeometry(s.floor, r, 'N').netM2, 0);
  });
});

describe('iluminat (metoda fluxului)', () => {
  const rep = (s: Snapshot, id: string) => lightReport(mc, s.floor, room(s, id), finishesOf(s, room(s, id)));
  test('lux = lm × 0,5 × 0,8 / m²; spoturile și scafa se adună; câte spoturi lipsesc până la țintă', () => {
    const r0 = rep(demo(), 'living'); assert.equal(autoLightCount(21.28), 2); assert.equal(r0.lumens, 1600); assert.equal(r0.lux, Math.round(1600 * .4 / 21.28));
    assert.equal(r0.spotsForTarget, Math.ceil((100 * 21.28 / .4 - 1600) / 905));
    const s = set(demo(), 'living', { ceiling: { type: 'cove', dropCm: 15, coveCm: 25, spot: 'spot-mt143-9w', spots: 6 } }), r = rep(s, 'living'), inner = 2 * (5.1 + 3.3);
    assert.equal(r.lumens, Math.round(1600 + 6 * 905 + inner * 500 * .5)); assert.ok(r.lux >= 100);
    assert.ok(!keys(s, 'living').includes('light.low'));
  });
  test('temperatura de culoare: 2700 K în baie e prea caldă; amestecul 2700/3000 nu e avertizat (sub 500 K)', () => {
    assert.ok(keys(demo(), 'baie').includes('light.cct'));
    const s = set(demo(), 'living', { ceiling: { type: 'drop', dropCm: 10, spot: 'spot-mt143-9w', spots: 10 } }); assert.ok(!keys(s, 'living').includes('light.cctMixed'));
    assert.deepEqual(rep(s, 'living').ccts, [2700, 3000]);
  });
  test('un corp fără flux declarat: estimarea e minimă și nu se avertizează pe baza ei', () => {
    const s = set(demo(), 'living', { light: 'lampa-karrnocka' }), r = rep(s, 'living'); assert.ok(r.unknownLumens); assert.ok(!keys(s, 'living').includes('light.low'));
  });
});

describe('pereți în straturi (benzi)', () => {
  test('lambriu jos, baghetă, tapet sus și o vopsea colorată pe alt perete: suprafețe pe benzi, vopseaua principală scade', () => {
    const base = computeBOQ(demo(), cat, mc).items.find(i => i.key === 'living:paint')!.netQty;
    const s = set(demo(), 'living', { wallFeatures: [
      { side: 'E', kind: 'panel', material: 'riflaj-mdf-unic-alb', heightM: 1.1, color: '#e9e4da' }, { side: 'E', kind: 'rail', material: 'nu-exista', heightM: 1.1 },
      { side: 'E', kind: 'wallpaper', material: 'tapet-grandeco-marmor', fromM: 1.1 }, { side: 'W', kind: 'paint', material: 'vopsea-savana-super', color: '#5b7065' } ] });
    const b = computeBOQ(s, cat, mc), fl = s.floor, r = room(s, 'living');
    const wp = b.items.find(i => i.key === 'living:wall:E:2')!, full = sideGeometry(fl, r, 'E').netM2, low = sideGeometry(fl, r, 'E', 1.1).netM2;
    assert.match(wp.label, /1\.1–2\.6 m/); const rr = wallpaperRolls(3.8, 1.5, { widthM: .53, lengthM: 10.05, repeatCm: 0 }); assert.equal(wp.orderedQty, rr.rolls);
    const pan = b.items.find(i => i.key === 'living:wall:E:0')!; assert.equal(pan.orderedQty, panelCount(3.8, 1.1, [60, 240]));
    assert.ok(b.unknown.some(u => /baghetă/.test(u)), 'bagheta fără produs e necunoscută');
    const accent = b.items.find(i => i.key === 'living:wall:W:3')!; assert.match(accent.label, /nuanță gri verzui/); assert.match(accent.label, /perete vest/); assert.ok(!/#[0-9a-f]{6}/i.test(accent.label), 'fără hex în etichetă'); assert.equal(accent.color, '#5b7065', 'codul exact rămâne pe articol'); assert.equal(accent.refId, 'vopsea-savana-super');
    const main = b.items.find(i => i.key === 'living:paint')!.netQty, west = sideGeometry(fl, r, 'W').netM2;
    assert.ok(Math.abs((base - main) - (full + west) * 2 / 13) < .05, `${base} - ${main}`); void low;
  });
  test('benzile care se suprapun pe același perete sunt refuzate; cele alăturate trec', () => {
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'E', kind: 'panel', material: 'a', heightM: 1.2 }, { side: 'E', kind: 'wallpaper', material: 'b', fromM: 1.1 }] } }), 'Placările de pe pereți sunt invalide.');
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'E', kind: 'panel', material: 'a', heightM: 1.1 }, { side: 'E', kind: 'rail', material: 'r', heightM: 1.1 }, { side: 'E', kind: 'wallpaper', material: 'b', fromM: 1.1 }] } }), null);
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'E', kind: 'paint', material: 'a', fromM: 2, heightM: 1 }] } }), 'Placările de pe pereți sunt invalide.');
  });
  test('3D: banda pornește de la înălțimea ei', () => {
    const s = set(demo(), 'living', { wallFeatures: [{ side: 'E', kind: 'wallpaper', material: 'tapet-grandeco-marmor', fromM: 1.1 }] });
    assert.equal(roomVisual(mc, finishesOf(s, room(s, 'living')), 'living').walls[0]!.fromM, 1.1);
  });
});

describe('3D', () => {
  test('motorul primește pardoseala în metri, placările, tavanul și faianța băii până la 2,1 m', () => {
    const s = set(demo(), 'living', { floor: 'gresie-emarble-60x120', floorLayout: { pattern: 'brick', groutMm: 2, groutColor: '#333333' }, wallFeatures: [{ side: 'E', kind: 'brick', material: 'caramida-bronx-60' }],
      ceiling: { type: 'cove', dropCm: 12, coveCm: 30, cornice: 'cornisa-nmc-nc109', spot: 'spot-mt143-9w', spots: 4 } });
    const v = roomVisual(mc, finishesOf(s, room(s, 'living')), 'living');
    assert.deepEqual(v.floor, { kind: 'tile', pattern: 'brick', angle: 0, pieceL: 1.2, pieceW: .6, grout: .002, groutColor: '#333333', color: '#e0deda', marble: true });
    assert.deepEqual(v.walls.map(w => [w.side, w.kind, w.color]), [['E', 'brick', '#8a5a44']]);
    assert.deepEqual(v.ceiling, { type: 'cove', drop: .12, cove: .3, led: '#ffd29a', cornice: [.135, .1], spots: 4 });
    const bath = roomVisual(mc, finishesOf(s, room(s, 'baie')), 'baie'); assert.deepEqual(bath.walls.map(w => [w.side, w.kind, w.heightM]), [['N', 'tile', 2.1], ['E', 'tile', 2.1], ['S', 'tile', 2.1], ['W', 'tile', 2.1]]);
    const plan = viewerInput(s, cat, undefined, { mc }).plan; assert.equal(plan.camere.find((c: any) => c.id === 'living').fin.floor.pattern, 'brick');
    assert.equal(viewerInput(s, cat).plan.camere[0].fin, undefined, 'fără catalogul de materiale, 3D-ul rămâne ca înainte');
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
    const g = mc2.materials.find(m => m.id === 'gresie-mckinley')!; assert.equal(g.unitPrice, 1); assert.deepEqual(g.specs, { sizeCm: [60, 60], rectified: true, color: '#eeede9' });
  });
});

// ---------- P1.b1: placare pe o porțiune de perete ----------
import { bandGeometry, bandsOverlap, insertBand, cladTarget, featurePlanRect, featureMarks, sideSpan } from '../core/finishes';
import { wallElevations } from '../core/drawings';
import { finishSchedule } from '../core/finish-schedule';
describe('placare pe o porțiune (alongFromM / alongToM)', () => {
  test('geometrie: porțiunea scade golurile doar din intersecție; două porțiuni complementare dau toată latura', () => {
    const s = demo(), r = room(s, 'living'), full = sideGeometry(s.floor, r, 'N'), L = sideSpan(r, 'N');
    for (const cut of [1, 1.6, 2.8, 4.2]){ const a = sideGeometry(s.floor, r, 'N', undefined, [0, cut]), b = sideGeometry(s.floor, r, 'N', undefined, [cut, L]);
      assert.ok(Math.abs(a.netM2 + b.netM2 - full.netM2) < .03, `tăietura la ${cut} m`); assert.ok(Math.abs(a.lengthM + b.lengthM - full.lengthM) < .02); }
    const w = sideGeometry(s.floor, r, 'N', undefined, [0, L]); assert.equal(w.netM2, full.netM2);
    // un snapshot vechi (fără interval) = toată latura, ca înainte
    const old = bandGeometry(s.floor, r, { side: 'N', kind: 'wallpaper', material: 'x' }); assert.equal(old.netM2, full.netM2); assert.equal(old.along, null);
    const part = bandGeometry(s.floor, r, { side: 'N', kind: 'slats', material: 'x', alongFromM: 1.6, alongToM: 3.6 }); assert.equal(part.lengthM, 2); assert.deepEqual(part.along, [1.6, 3.6]);
  });
  test('suprapunerea e verificată în 2D: aceeași latură, înălțime și lungime', () => {
    const paint = { side: 'E' as const, kind: 'paint' as const, material: 'p', alongFromM: 0, alongToM: 1.6 }, slats = { side: 'E' as const, kind: 'slats' as const, material: 's', alongFromM: 1.6, alongToM: 3.6 };
    assert.equal(bandsOverlap([paint, slats], 2.6), false, 'alăturate pe lungime');
    assert.equal(bandsOverlap([paint, { ...slats, alongFromM: 1.5 }], 2.6), true);
    assert.equal(bandsOverlap([{ side: 'E', kind: 'paint', material: 'p' }, slats], 2.6), true, 'o bandă pe toată latura acoperă porțiunea');
    assert.equal(bandsOverlap([{ side: 'E', kind: 'paint', material: 'p', heightM: 1 }, { ...slats, fromM: 1 }], 2.6), false, 'una sub alta');
  });
  test('unealta Placare: porțiunea taie vopseaua de accent în stânga și dreapta, fără suprapunere', () => {
    const accent = { side: 'N' as const, kind: 'paint' as const, material: 'vopsea-savana-super', color: '#c8b49a' }, add = { side: 'N' as const, kind: 'slats' as const, material: 'riflaj-mdf-unic-alb', alongFromM: 1.6, alongToM: 2.4 };
    const out = insertBand([accent, { side: 'E', kind: 'wallpaper', material: 't' }], add, 4, 2.6);
    assert.deepEqual(out.map(w => [w.side, w.kind, w.alongFromM, w.alongToM]), [['N', 'paint', 0, 1.6], ['N', 'paint', 2.4, 4], ['E', 'wallpaper', undefined, undefined], ['N', 'slats', 1.6, 2.4]]);
    assert.equal(bandsOverlap(out, 2.6), false); assert.equal(out[0]!.color, '#c8b49a');
    // pe toată latura: vopseaua dispare; o bandă de altă înălțime rămâne neatinsă
    assert.deepEqual(insertBand([accent, { side: 'N', kind: 'panel', material: 'l', heightM: 1 }], { side: 'N', kind: 'wallpaper', material: 't', fromM: 1 }, 4, 2.6).map(w => w.kind), ['panel', 'wallpaper']);
  });
  test('clic pe fața peretelui: camera, latura și poziția (5 cm); dincolo de perete nu', () => {
    const s = demo(), r = room(s, 'living');
    const h = cladTarget(s.floor, [r.rect.x0 + 1.62, r.rect.z0 + .1])!; assert.equal(h.roomId, 'living'); assert.equal(h.side, 'N'); assert.equal(h.along, 1.6);
    assert.equal(cladTarget(s.floor, [r.rect.x0 + 1.6, r.rect.z0 - .5]), null);
  });
  test('deviz: riflaj pe o porțiune numără panourile porțiunii', () => {
    const s = set(demo(), 'living', { wallFeatures: [{ side: 'N', kind: 'slats', material: 'riflaj-mdf-unic-alb', heightM: 2.4, alongFromM: 0, alongToM: 2.4 }] });
    const t = set(demo(), 'living', { wallFeatures: [{ side: 'N', kind: 'slats', material: 'riflaj-mdf-unic-alb', heightM: 2.4, alongFromM: 0, alongToM: 4.8 }] });
    const a = computeBOQ(s, cat, mc).items.find(i => i.key === 'living:wall:N:0')!, b = computeBOQ(t, cat, mc).items.find(i => i.key === 'living:wall:N:0')!;
    assert.equal(a.orderedQty, panelCount(2.4, 2.4, [60, 240])); assert.equal(b.orderedQty, 2 * a.orderedQty, 'riflaj pe 2,4 m din 4,8 m = jumătate din panouri');
    assert.match(a.label, /porțiunea 0–2\.4 m pe lungime/);
  });
  test('plan, elevație, fișă, 3D și server', () => {
    const s = set(demo(), 'living', { wallFeatures: [{ side: 'S', kind: 'slats', material: 'riflaj-mdf-unic-alb', alongFromM: 1, alongToM: 2.5, vertical: false }, { side: 'N', kind: 'wallpaper', material: 'tapet-grandeco-marmor', alongFromM: .5, alongToM: 2 }] });
    const r = room(s, 'living'), L = sideSpan(r, 'S'), rc = featurePlanRect(s.floor, r, finishesOf(s, r).wallFeatures![0]!)!;
    assert.ok(Math.abs(rc.x0 - (r.rect.x0 + 1)) < 1e-9 && Math.abs(rc.x1 - (r.rect.x0 + 2.5)) < 1e-9 && rc.z1 <= r.rect.z1 && rc.z1 - rc.z0 > .039);
    assert.deepEqual(featureMarks(s, s.floor, finishesOf).map(m => [m.kind, m.n]), [['slats', 1], ['wallpaper', 1]]);
    const el = wallElevations(s, mc, cat, s.floor, r), south = el.find(e => e.side === 'S')!.bands[0]!, north = el.find(e => e.side === 'N')!.bands[0]!;
    assert.deepEqual([north.u0, north.u1], [.5, 2]); assert.deepEqual([south.u0, south.u1], [Math.round((L - 2.5) * 1000) / 1000, Math.round((L - 1) * 1000) / 1000], 'latura S se citește de la dreapta');
    assert.ok(finishSchedule(s, cat, mc).some(x => x.details.some(d => d.key === 'sched.along')));
    const v = roomVisual(mc, finishesOf(s, r), 'living').walls; assert.deepEqual(v[0]!.along, [1, 2.5]); assert.equal(v[0]!.vertical, false); assert.equal(v[1]!.vertical, true);
    const bad = (w: object) => sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'slats', material: 'a', ...w }] } });
    assert.equal(bad({ alongFromM: 1, alongToM: 2, vertical: false }), null);
    assert.equal(bad({ alongFromM: 2, alongToM: 1 }), 'Placările de pe pereți sunt invalide.');
    assert.equal(bad({ alongToM: 60 }), 'Placările de pe pereți sunt invalide.');
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'paint', material: 'a', vertical: true }] } }), 'Placările de pe pereți sunt invalide.', 'orientarea doar la riflaj');
  });
  test('server: porțiunea inversată dă 400 la salvare', async () => {
    resetDbForTests(); const A = '99999999-9999-4999-8999-999999999998', id = await repo.createProject(A, 'F', 'demo'), s = set(demo(), 'living', { wallFeatures: [{ side: 'N', kind: 'slats', material: 'riflaj-mdf-unic-alb', alongFromM: 3, alongToM: 1 }] });
    await assert.rejects(repo.saveDraft(A, id, s), (e: any) => e.status === 400);
  });
});

// ---------- P1.b3: tapet corect și vopsea decorativă cu efect ----------
import { featureLabor, effectOf, EFFECT_LABOR } from '../core/finishes';
describe('tapet și vopsea decorativă cu efect', () => {
  test('deviz: linia de tapet spune fâșiile, rezerva și lotul; rolele includ rezerva', () => {
    const s = set(demo(), 'living', { wallFeatures: [{ side: 'N', kind: 'wallpaper', material: 'tapet-grandeco-marmor' }] });
    const it = computeBOQ(s, cat, mc).items.find(i => i.key === 'living:wall:N:0')!, n = sideGeometry(s.floor, room(s, 'living'), 'N'), rr = wallpaperRolls(n.lengthM, n.heightM, { widthM: .53, lengthM: 10.05, repeatCm: 0 });
    assert.equal(it.orderedQty, rr.rolls); assert.ok(rr.spare >= 1);
    assert.match(it.label, /fâșii de 2\.65 m, 3 pe rolă, \+ 1 rolă rezervă; toate din același lot/);
  });
  test('efectul: manopera are tarif propriu; fără tarif și fără produs pentru efect, ambele apar fără preț (nu cu prețul tencuielii obișnuite)', () => {
    assert.equal(featureLabor({ kind: 'plaster' }), 'manopera-tencuiala-decorativa');
    assert.equal(featureLabor({ kind: 'plaster', effect: 'textured' }), 'manopera-tencuiala-decorativa');
    assert.equal(featureLabor({ kind: 'plaster', effect: 'venetian' }), EFFECT_LABOR.venetian); assert.notEqual(EFFECT_LABOR.venetian, 'manopera-tencuiala-decorativa');
    assert.equal(effectOf({ kind: 'paint', effect: 'silk' }), null, 'efectul doar pe tencuială');
    assert.equal(effectOf({ kind: 'plaster' }, { specs: { effect: 'concrete' } } as any), 'concrete', 'efectul declarat de produs are prioritate');
    const base = computeBOQ(demo(), cat, mc).items.find(i => i.key === 'living:paint')!.netQty;
    const s = set(demo(), 'living', { wallFeatures: [{ side: 'W', kind: 'plaster', material: 'tencuiala-kober-bob-orez', effect: 'venetian' }] }), b = computeBOQ(s, cat, mc);
    assert.ok(!b.items.some(i => i.key.startsWith('living:wall:W')), 'Kober nu e venețiană: fără linie cu prețul ei');
    assert.ok(b.unknown.some(u => /efect venețiană \(material/.test(u)) && b.unknown.some(u => /Manoperă vopsea decorativă efect venețiană/.test(u)));
    assert.ok(!b.labor.some(l => l.rateId === 'manopera-tencuiala-decorativa'), 'tariful tencuielii obișnuite nu se folosește la venețiană');
    assert.ok(b.items.find(i => i.key === 'living:paint')!.netQty < base, 'peretele cu efect nu se mai vopsește cu vopseaua camerei');
    const tx = computeBOQ(set(demo(), 'living', { wallFeatures: [{ side: 'W', kind: 'plaster', material: 'tencuiala-kober-bob-orez', effect: 'textured' }] }), cat, mc);
    assert.ok(tx.items.some(i => i.key === 'living:wall:W:0') && tx.labor.some(l => l.rateId === 'manopera-tencuiala-decorativa'));
  });
  test('avertismente: microciment în baie, efect fără produs; 3D primește efectul; serverul îl acceptă doar pe tencuială', () => {
    const s = set(demo(), 'baie', { wallFeatures: [{ side: 'N', kind: 'plaster', material: 'tencuiala-kober-bob-orez', effect: 'microcement' }] });
    const k = keys(s, 'baie'); assert.ok(k.includes('fin.microcementWet') && k.includes('fin.effectUnpriced'));
    assert.equal(roomVisual(mc, finishesOf(s, room(s, 'baie')), 'baie').walls[0]!.effect, 'microcement');
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'plaster', material: 'a', effect: 'venetian' }] } }), null);
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'plaster', material: 'a', effect: 'gold' }] } }), 'Placările de pe pereți sunt invalide.');
    assert.equal(sanitizeFinishes({ x: { wallFeatures: [{ side: 'N', kind: 'paint', material: 'a', effect: 'silk' }] } }), 'Placările de pe pereți sunt invalide.');
  });
});
