// Obiecte pe perete (Floor.wallItems): tablou, oglindă, raft, TV — geometria pe fața peretelui, regula de montaj,
// validările, serverul, nivelurile, 3D-ul, devizul, elevațiile, lista pentru cititorul de ecran și tastatura.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { validateFloor, issueTarget } from '../core/validate';
import { checkSnapshot } from '../lib/repo';
import { computeBOQ, finishesOf } from '../core/boq';
import { viewerInput } from '../lib/viewer-input';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { addLevel } from '../core/levels';
import { selectionBox } from '../core/frame';
import { planItems, planKeyAction } from '../core/plan-keys';
import { wallElevations } from '../core/drawings';
import { wallItemGeometry, wallItemRoom, wallItemFace, wallItemDefaults, furnitureBelow, wallItemFurnitureIssues, wallItemsOnSide } from '../core/wall-items';
import { sanitizeFloorElements, PlanLimitError, PLAN_LIMITS } from '../core/plan-limits';
import { railSlots, TOOL_GROUPS } from '../lib/tool-groups';
import type { Catalog, MaterialsCatalog, Snapshot, WallItem } from '../core/types';
const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
// camera 5 × 4 m (blank): wall-1 = latura de nord, de la [0,0] la [5,0], cu fereastră la 1,8–3,2 m; fața +1 dă în cameră
const blank = (): Snapshot => { const s = newSnapshot(cat, 'Pe perete', 'blank'); s.placements = []; return s; };
const art = (o: Partial<WallItem> = {}): WallItem => ({ id: 'tab-1', wallId: 'wall-1', side: 1, kind: 'art', offset: 1, elevation: 1.45, width: .7, height: .5, ...o });
const withItem = (o: Partial<WallItem> = {}) => { const s = blank(); s.floor.wallItems = [art(o)]; return s; };
const codes = (s: Snapshot) => validateFloor(s.floor).filter(i => i.code === 'WALL_ITEM').map(i => `${i.key}:${i.severity}`);
// canapea de 228 × 95 × 83 cm lipită de peretele de nord, cu centrul la x = 1
const sofa = (s: Snapshot, x = 1) => { const th = s.floor.walls[0]!.thickness; s.placements.push({ id: 'sofa', roomId: s.floor.rooms[0]!.id, group: 'canapea', variantId: 'canapea-0', x, z: th / 2 + .475 + .02, rotation: 0, source: 'manual' }); return s; };

describe('obiecte pe perete: model și plasare', () => {
  test('geometria pe fața peretelui, camera, latura și poziția pe ea', () => {
    const s = withItem(), it = s.floor.wallItems![0]!, g = wallItemGeometry(s.floor, it)!;
    assert.deepEqual([g.s0, g.s1].map(v => Math.round(v * 100) / 100), [.65, 1.35]); assert.ok(g.nz > .99, 'fața +1 a peretelui de nord privește spre cameră');
    assert.equal(wallItemRoom(s.floor, it)?.id, s.floor.rooms[0]!.id); assert.equal(wallItemRoom(s.floor, { ...it, side: -1 }), null);
    const f = wallItemFace(s.floor, it)!; assert.equal(f.side, 'N'); assert.ok(Math.abs(f.u - 1) < 1e-9);
  });
  test('valorile implicite: tablou la 1,45 m; deasupra canapelei marginea de jos la 20 cm peste ea; oglindă, raft, TV', () => {
    assert.deepEqual(wallItemDefaults('art'), { width: .7, height: .5, depth: .03, elevation: 1.45 });
    assert.equal(wallItemDefaults('mirror').elevation, 1.55); assert.equal(wallItemDefaults('shelf').elevation, 1.6); assert.deepEqual([wallItemDefaults('tv').width, wallItemDefaults('tv').elevation], [1.23, 1.1]);
    const s = sofa(blank()), below = furnitureBelow(s, cat, s.floor, art())!;
    assert.equal(below.id, 'sofa'); assert.equal(below.top, .83); assert.equal(wallItemDefaults('art', below).elevation, .83 + .2 + .25);
    assert.equal(furnitureBelow(s, cat, s.floor, art({ offset: 4.2 })), null, 'nu e deasupra canapelei');
  });
});

describe('obiecte pe perete: validări', () => {
  test('iese din perete, sub pardoseală sau peste tavan (ERROR)', () => {
    assert.deepEqual(codes(withItem()), []);
    assert.deepEqual(codes(withItem({ offset: .2 })), ['issue.WALL_ITEM.outside:ERROR']);
    assert.deepEqual(codes(withItem({ elevation: 2.5 })), ['issue.WALL_ITEM.outside:ERROR']);
  });
  test('acoperă fereastra sau o nișă de pe aceeași față (ERROR); deasupra ferestrei sau pe cealaltă față, nu', () => {
    assert.deepEqual(codes(withItem({ offset: 2.5 })), ['issue.WALL_ITEM.overOpening:ERROR']);
    const s = withItem({ offset: 4.2 }); s.floor.niches = [{ id: 'n1', wallId: 'wall-1', offset: 3.9, width: .6, height: .4, depth: .1, elevation: 1.3 }];
    assert.deepEqual(codes(s), ['issue.WALL_ITEM.overOpening:ERROR']); s.floor.niches[0]!.side = -1; assert.deepEqual(codes(s), []);
    const iss = validateFloor(withItem({ offset: 2.5 }).floor).find(i => i.code === 'WALL_ITEM')!; assert.deepEqual(issueTarget(iss, withItem().floor), { kind: 'wall_item', id: 'tab-1' });
  });
  test('pe zona în care culisează o ușă glisantă (WARNING), doar pe fața șinei', () => {
    const s = blank(), w = s.floor.walls[1]!, L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]);
    w.openings.push({ id: 'g1', kind: 'door', offset: L - 1.2, width: .8, doorType: 'sliding', hinge: 'start', swing: 1 });
    s.floor.wallItems = [art({ wallId: w.id, offset: L - 1.8, elevation: 1.45 })];
    assert.deepEqual(codes(s), ['issue.WALL_ITEM.slideZone:WARNING']);
    s.floor.wallItems[0]!.side = -1; assert.deepEqual(codes(s), []);
  });
  test('o piesă mai înaltă decât marginea de jos, lipită de aceeași față, acoperă obiectul (WARNING)', () => {
    const low = sofa(withItem({ elevation: 1 })), ok = sofa(withItem());
    assert.deepEqual(wallItemFurnitureIssues(low, cat, low.floor).map(i => i.key), ['issue.WALL_ITEM.behindTall']);
    assert.deepEqual(wallItemFurnitureIssues(ok, cat, ok.floor), []);
  });
});

describe('obiecte pe perete: server și niveluri', () => {
  test('limite: număr, dimensiuni, tip, față, id unic; poziția și cota se aduc pe perete; textul se curăță', () => {
    const clean = (items: any[]) => { const f: any = structuredClone(blank().floor); f.wallItems = items; sanitizeFloorElements(f); return f.wallItems as WallItem[] | undefined; };
    const got = clean([{ ...art(), offset: 99, elevation: 9, label: '<b>Tablou</b>‮', color: '#AABBCC', extra: 1 }])![0]!;
    assert.equal(got.offset, 5); assert.equal(got.elevation, blank().floor.ceilingHeight); assert.equal(got.label, 'b Tablou /b'); assert.equal(got.color, '#aabbcc'); assert.ok(!('extra' in got));
    assert.equal(clean([{ ...art(), wallId: 'nu-exista' }]), undefined, 'pe un perete care nu există se elimină, ca nișa');
    for (const bad of [{ width: 5 }, { height: 0 }, { kind: 'vaza' }, { side: 0 }, { depth: 1 }, { id: 'a b' }, { color: 'red' }, { offset: 'x' }])
      assert.throws(() => clean([{ ...art(), ...bad }]), PlanLimitError, JSON.stringify(bad));
    assert.throws(() => clean([art(), art()]), PlanLimitError, 'id dublat');
    assert.throws(() => clean(Array.from({ length: PLAN_LIMITS.wallItem.max + 1 }, (_, i) => art({ id: `t${i}` }))), PlanLimitError);
    const s: any = withItem(); s.floor.wallItems[0].width = 9; assert.throws(() => checkSnapshot(s), (e: any) => e.status === 400);
  });
  test('un nivel nou nu copiază obiectele de pe perete', () => {
    const up = addLevel(withItem(), { name: 'Etaj' }).levels![0]!; assert.equal(up.wallItems, undefined);
  });
});

describe('obiecte pe perete: 3D, deviz, planșe, listă, tastatură, bară', () => {
  test('3D: peretele, centrul, cota, fața, camera și latura; în fața riflajului', () => {
    const s = withItem(), room = s.floor.rooms[0]!; s.finishes = { [room.id]: { ...finishesOf(s, room), wallFeatures: [{ side: 'N', kind: 'slats', material: 'riflaj-mdf-unic-alb', alongFromM: .5, alongToM: 1.5 }] } };
    const o = viewerInput(s, cat).plan.obiectePerete[0]!; assert.deepEqual({ ...o, clad: undefined }, { id: 'tab-1', wi: 0, la: 1, l: .7, y: 1.45, h: .5, ad: .03, side: 1, kind: 'art', color: null, room: room.id, fata: 'N', clad: undefined });
    assert.equal(o.clad, .021); assert.equal(viewerInput(withItem({ offset: 3.5 }), cat).plan.obiectePerete[0]!.clad, 0);
  });
  test('deviz: fără produs nicio linie (obiectul clientului), doar montajul necunoscut; cu produs, 1 buc', () => {
    const b = computeBOQ(withItem(), cat, mc); assert.ok(!b.items.some(i => i.key.includes(':wallitem:'))); assert.ok(b.unknown.some(u => /^Montaj tablou \(1 buc\)/.test(u)));
    const mc2: MaterialsCatalog = { ...mc, materials: [...mc.materials, { id: 'rama-test', category: 'wall_art', name: 'Ramă test', supplier: 'Test', unit: 'buc', unitPrice: 50, sourceUrl: null, verificationType: 'test', confidence: 'LOW' }] };
    const line = computeBOQ(withItem({ product: 'rama-test' }), cat, mc2).items.find(i => i.key.endsWith(':wallitem:tab-1'))!; assert.equal(line.orderedQty, 1); assert.match(line.label, /^Tablou · .*perete nord/);
    assert.ok(computeBOQ(withItem({ product: 'nu-exista' }), cat, mc).unknown.some(u => /produsul ales nu are preț verificat/.test(u)));
  });
  test('elevația arată obiectul cu centrul lui; latura S se citește de la dreapta', () => {
    const s = withItem(), room = s.floor.rooms[0]!, n = wallElevations(s, mc, cat, s.floor, room).find(e => e.side === 'N')!;
    assert.deepEqual(n.items, [{ id: 'tab-1', kind: 'art', u: 1, y: 1.45, w: .7, h: .5 }]);
    const south = s.floor.walls.find(w => Math.abs(w.a[1] - 4) < 1e-6 && Math.abs(w.b[1] - 4) < 1e-6)!, sw = Math.hypot(south.b[0] - south.a[0], south.b[1] - south.a[1]);
    const nx = -(south.b[1] - south.a[1]) / sw, nz = (south.b[0] - south.a[0]) / sw, side: 1 | -1 = nz < 0 ? 1 : -1; void nx;
    const at = south.a[0] < south.b[0] ? 1 : sw - 1; s.floor.wallItems!.push(art({ id: 'tab-2', wallId: south.id, side, offset: at }));
    assert.deepEqual(wallItemsOnSide(s.floor, room, 'S').map(x => x.u), [4]);
  });
  test('lista pentru cititorul de ecran, încadrarea, Tab și PageUp / PageDown', () => {
    const s = withItem(), items = describePlan(s, cat, { t: translator('ro'), units: 'metric', lang: 'ro' }).flatMap(g => g.items);
    assert.match(items.find(i => i.id === 'tab-1')!.label, /^Tablou pe perete · 70 cm × 50 cm, centrul la 1[.,]45 m$/);
    const box = selectionBox(s, cat, { kind: 'wall_item', id: 'tab-1' })!; assert.ok(box.x0 >= .65 - 1e-9 && box.x1 <= 1.35 + 1e-9);
    assert.ok(planItems(s).some(i => i.kind === 'wall_item' && i.id === 'tab-1'));
    assert.deepEqual(planKeyAction({ key: 'PageUp' }, planItems(s), { kind: 'wall_item', id: 'tab-1' }), { type: 'lift', dyCm: 5 });
    assert.deepEqual(planKeyAction({ key: 'PageDown', shiftKey: true }, planItems(s), { kind: 'wall_item', id: 'tab-1' }), { type: 'lift', dyCm: -50 });
  });
  test('bara: „Placare” și „Pe perete” stau în grupa Decor (cu zona de pardoseală, lotul 3, și tavanul fals pe zonă, lotul 4); etapele cu puține unelte rămân plate', () => {
    assert.deepEqual(TOOL_GROUPS.find(g => g.id === 'decor')!.tools, ['clad', 'wall_item', 'floor_zone', 'ceiling_zone']);
    assert.deepEqual(railSlots(['select', 'clad', 'wall_item', 'measure'], true).map(s => 'tool' in s ? s.tool : s.group), ['select', 'clad', 'wall_item', 'measure']);
    const many = ['select', 'niche', 'column', 'glass_wall', 'wall_item', 'clad', 'measure', 'dim', 'text'] as const;
    assert.deepEqual(railSlots([...many], true).map(s => 'tool' in s ? s.tool : s.group), ['select', 'niche', 'structure', 'decor', 'notes']);
  });
});
