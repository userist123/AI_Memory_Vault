// Uneltele de plan din lotul 1: gol de trecere, ghenă / casetare, nișă, cote și texte persistente.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { doorOpenings } from '../core/doors';
import { doorZones, validateFloor, issueTarget } from '../core/validate';
import { checkSnapshot } from '../lib/repo';
import { computeBOQ, roomGeometry, shaftsIn, defaultFinishes } from '../core/boq';
import { floorToPlan, planToFloor } from '../features/migration/legacy.js';
import { viewerInput } from '../lib/viewer-input';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { planToDxf } from '../core/dxf';
import { nicheGeometry, nicheRoom, nicheSide, nicheTileArea } from '../core/niches';
import { addLevel } from '../core/levels';
import { selectionBox } from '../core/frame';
import { planItems } from '../core/plan-keys';
import type { Catalog, MaterialsCatalog, Niche, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
// camera 5 × 4 m cu pereți exteriori (wall-1 = latura de nord, de la [0,0] la [5,0], fereastră la 1,8–3,2 m) și ușa de intrare pe wall-4
const blank = (): Snapshot => newSnapshot(cat, 'Unelte', 'blank');
const desc = (s: Snapshot) => describePlan(s, cat, { t: translator('ro'), units: 'metric', lang: 'ro' }).flatMap(g => g.items);
const ents = (dxf: string, type: string, layer: string) => dxf.split(`0\n${type}\n8\n${layer}\n`).length - 1;

describe('gol de trecere', () => {
  const withPassage = () => { const s = blank(); s.floor.walls[0]!.openings.push({ id: 'tr-1', kind: 'door', offset: .3, width: .9, passage: true }); return s; };
  test('rămâne ușă pentru suprafață (se scade ca ușa), dar nu cere produs de ușă și nu e fereastră', () => {
    const s = withPassage(), g = roomGeometry(s, s.floor.rooms[0]!), g0 = roomGeometry(blank(), blank().floor.rooms[0]!);
    assert.equal(g.windowWidth, g0.windowWidth); assert.ok(Math.abs(g.doorWidth - g0.doorWidth - .9) < 1e-9);
    assert.ok(!doorOpenings(s).some(d => d.id === 'tr-1'));
    s.doors = { 'tr-1': { product: mc.materials.find(m => m.category === 'door')!.id } };
    assert.ok(!computeBOQ(s, cat, mc).items.some(i => i.key === 'door:tr-1'));
  });
  test('zona din fața golului e de 60 cm (circulație), nu de 95 cm (arcul foii)', () => {
    const s = withPassage(), z = doorZones(s.floor, s.floor.rooms[0]!).find(r => r.x0 < .7 && r.x1 > .7 && r.z0 === 0)!;
    assert.ok(Math.abs(z.z1 - .6) < 1e-9);
  });
  test('3D: `trecere` ajunge la motor; adaptorul legacy face drumul dus-întors; proiectul vechi nu primește câmpul', () => {
    const s = withPassage(), pl = floorToPlan(s.floor, 'x');
    assert.equal((pl.pereti[0]!.goluri.find(g => g.la === .3) as any).trecere, true);
    assert.equal(planToFloor(pl).walls[0]!.openings.find(o => o.offset === .3)!.passage, true);
    assert.ok(!JSON.stringify(floorToPlan(blank().floor, 'x')).includes('trecere'));
    assert.ok(viewerInput(s, cat).plan.pereti[0].goluri.some((g: any) => g.trecere));
  });
  test('descriere, DXF: fără arc și fără foaie, doar tocurile', () => {
    const s = withPassage(); assert.match(desc(s).find(i => i.id === 'tr-1')!.label, /^Gol de trecere · 90/);
    const arcs = (x: Snapshot) => ents(planToDxf(x, cat, { lang: 'ro' }), 'ARC', 'DOORS'), lines = (x: Snapshot) => ents(planToDxf(x, cat, { lang: 'ro' }), 'LINE', 'DOORS');
    assert.equal(arcs(s), arcs(blank())); assert.equal(lines(s), lines(blank()) + 2);
  });
});

describe('ghenă / casetare', () => {
  const withShaft = (o: Partial<import('../core/types').Column> = {}) => { const s = blank(); s.floor.rooms[0]!.type = 'baie'; s.finishes = { [s.floor.rooms[0]!.id]: { ...defaultFinishes(s.floor.rooms[0]!) } };
    s.floor.columns = [{ id: 'gh-1', x: 4.85, z: 2, w: .3, d: .3, shape: 'rect', role: 'shaft', ...o }]; return s; };
  test('deviz: gips-carton pe fețele libere × înălțime, faianță în baie; stâlpul structural fără cost', () => {
    const s = withShaft(), [sh] = shaftsIn(s.floor, s.floor.rooms[0]!);
    // lipită de peretele de est (x = 5): fețe libere = 0,3 + 0,3 + 0,3 = 0,9 m, înălțimea tavanului
    assert.equal(sh!.freePerimeter, .9); assert.equal(sh!.area, Math.round(.9 * s.floor.ceilingHeight * 100) / 100);
    const b = computeBOQ(s, cat, mc); assert.ok(b.items.some(i => i.key.endsWith(':shaft:gh-1') && i.netQty === sh!.area));
    assert.ok(b.items.some(i => i.key.endsWith(':shaft-tile:gh-1'))); assert.ok(b.unknown.some(u => u.startsWith('Manoperă casetare')));
    const low = withShaft({ role: 'boxing', height: 1.1 }); assert.equal(shaftsIn(low.floor, low.floor.rooms[0]!)[0]!.area, Math.round(.9 * 1.1 * 100) / 100);
    const st = withShaft({ role: undefined }); assert.deepEqual(shaftsIn(st.floor, st.floor.rooms[0]!), []); assert.ok(!computeBOQ(st, cat, mc).items.some(i => i.key.includes('shaft')));
  });
  test('validări: în deschiderea ușii și în afara camerei → avertismente; ținta e ghena', () => {
    assert.deepEqual(validateFloor(withShaft().floor).filter(i => i.code.startsWith('SHAFT')), []);
    const out = withShaft({ x: 6 }), iss = validateFloor(out.floor).find(i => i.code === 'SHAFT_OUTSIDE_ROOM')!;
    assert.equal(iss.severity, 'WARNING'); assert.deepEqual(issueTarget(iss, out.floor), { kind: 'column', id: 'gh-1' });
    const door = blank().floor.walls[3]!.openings[0]!, s = withShaft({ x: .3, z: 4 - door.offset - door.width / 2 });
    assert.ok(validateFloor(s.floor).some(i => i.code === 'SHAFT_DOOR' && i.severity === 'WARNING'));
  });
  test('3D, descriere, DXF, server: înălțimea ajunge la motor, rolul în listă și pe stratul pereților; rol necunoscut respins', () => {
    const s = withShaft({ height: 1.1 }); assert.equal((viewerInput(s, cat).plan.stalpi[0] as any).height, 1.1);
    assert.match(desc(s).find(i => i.id === 'gh-1')!.label, /^Ghenă · 30/);
    assert.equal(ents(planToDxf(s, cat, { lang: 'ro' }), 'LINE', 'WALLS'), ents(planToDxf(blank(), cat, { lang: 'ro' }), 'LINE', 'WALLS') + 2);
    const bad: any = withShaft(); bad.floor.columns[0].role = 'chimney'; assert.throws(() => checkSnapshot(bad), (e: any) => e.status === 400);
    const old = withShaft({ role: undefined }); assert.ok(!('role' in (checkSnapshot(structuredClone(old)) as any).floor.columns[0]));
  });
});

describe('nișă', () => {
  // pe peretele de est (wall-2, de la [5,0] la [5,4]): normala (-uz, ux) = (-1, 0) dă spre cameră, deci side = +1
  const withNiche = (o: Partial<Niche> = {}, type = 'baie') => { const s = blank(); s.floor.rooms[0]!.type = type;
    s.floor.niches = [{ id: 'nisa-1', wallId: 'wall-2', offset: 1, width: .6, height: .4, depth: .1, elevation: 1.1, side: 1, ...o }]; return s; };
  test('model: fața dinspre clic, scobitura în grosimea peretelui, camera în care dă', () => {
    const s = withNiche(), w = s.floor.walls[1]!; assert.equal(nicheSide(w, [4.5, 1]), 1); assert.equal(nicheSide(w, [5.5, 1]), -1);
    const g = nicheGeometry(s.floor, s.floor.niches![0]!)!; assert.ok(g.quad.every(q => q[0] >= 5 - .125 - 1e-9 && q[0] <= 5));
    assert.equal(nicheRoom(s.floor, s.floor.niches![0]!)?.id, s.floor.rooms[0]!.id);
    assert.equal(nicheRoom(s.floor, { ...s.floor.niches![0]!, side: -1 }), null);
  });
  test('validări: în perete și sub tavan, adâncimea cu 2 cm rămași, fără suprapunere cu un gol', () => {
    const codes = (s: Snapshot) => validateFloor(s.floor).filter(i => i.code.startsWith('NICHE')).map(i => `${i.code}:${i.severity}`);
    assert.deepEqual(codes(withNiche()), []);
    assert.deepEqual(codes(withNiche({ offset: 3.8 })), ['NICHE_OUTSIDE_WALL:ERROR']);
    assert.deepEqual(codes(withNiche({ elevation: 2.4 })), ['NICHE_OUTSIDE_WALL:ERROR']);
    assert.deepEqual(codes(withNiche({ depth: .24 })), ['NICHE_TOO_DEEP:ERROR']);   // perete exterior
    const inner = withNiche({ depth: .24 }); inner.floor.walls[1]!.exterior = false; assert.deepEqual(codes(inner), ['NICHE_TOO_DEEP:WARNING']);
    const op = withNiche(); op.floor.walls[1]!.openings.push({ id: 'f', kind: 'window', offset: 1.2, width: 1, sill: 1 }); assert.deepEqual(codes(op), ['NICHE_OVER_OPENING:ERROR']);
    const low = withNiche(); low.floor.walls[1]!.openings.push({ id: 'f', kind: 'window', offset: 1.2, width: 1, sill: 1.6 }); assert.deepEqual(codes(low), []);
    const iss = validateFloor(withNiche({ offset: 3.8 }).floor).find(i => i.code === 'NICHE_OUTSIDE_WALL')!; assert.deepEqual(issueTarget(iss, withNiche().floor), { kind: 'niche', id: 'nisa-1' });
  });
  test('deviz: în baie faianța pe fund și fețe (w·h + 2·(w+h)·adâncime), manopera pe bucată necunoscută; în living doar manopera', () => {
    const b = computeBOQ(withNiche(), cat, mc), line = b.items.find(i => i.key.endsWith(':niche:nisa-1'))!;
    assert.equal(line.netQty, Math.round((.6 * .4 + 2 * (.6 + .4) * .1) * 100) / 100); assert.equal(nicheTileArea({ width: .6, height: .4, depth: .1 }), .6 * .4 + 2 * 1 * .1);
    assert.ok(b.unknown.some(u => u.startsWith('Manoperă nișă'))); assert.ok(b.unknown.some(u => u.startsWith('Profil de colț nișă')));
    const lv = computeBOQ(withNiche({}, 'living'), cat, mc); assert.ok(!lv.items.some(i => i.key.includes(':niche:'))); assert.ok(lv.unknown.some(u => u.startsWith('Manoperă nișă')));
  });
  test('niveluri: nișa urcă cu peretele ei (id nou, perete remapat); cotele și textele nu se copiază', () => {
    const s = withNiche(); s.floor.annotations = [{ id: 't1', kind: 'text', at: [1, 1], text: 'x' }];
    const up = addLevel(s, { name: 'Etaj' }).levels![0]!, n = up.niches![0]!;
    assert.notEqual(n.id, 'nisa-1'); assert.ok(up.walls.some(w => w.id === n.wallId)); assert.ok(!up.walls.some(w => w.id === 'wall-2')); assert.equal(up.annotations, undefined);
  });
  test('3D, descriere, încadrare, tastatură, DXF, server', () => {
    const s = withNiche(); assert.deepEqual(viewerInput(s, cat).plan.nise[0], { id: 'nisa-1', wi: 1, la: 1, l: .6, y: 1.1, h: .4, ad: .1, side: 1 });
    assert.match(desc(s).find(i => i.id === 'nisa-1')!.label, /^Nișă · 60 cm × 40 cm, adâncime 10 cm · Living$/);
    const box = selectionBox(s, cat, { kind: 'niche', id: 'nisa-1' })!; assert.ok(box.z0 >= 1 - 1e-9 && box.z1 <= 1.6 + 1e-9);
    assert.ok(planItems(s).some(i => i.kind === 'niche' && i.id === 'nisa-1'));
    assert.equal(ents(planToDxf(s, cat, { lang: 'ro' }), 'POLYLINE', 'WALLS'), ents(planToDxf(blank(), cat, { lang: 'ro' }), 'POLYLINE', 'WALLS') + 1);
    const bad: any = withNiche(); bad.floor.niches[0].depth = 2; assert.throws(() => checkSnapshot(bad), (e: any) => e.status === 400);
  });
});
