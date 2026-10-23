// Tipurile de ușă (lotul 2a): batantă, dublă, glisantă pe perete, în perete (casetă), pliantă, pivotantă și filomuro —
// modelul, validarea pe server, regulile de spațiu, simbolurile din plan, DXF, 3D, bugetul și textele.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { doorOpenings, doorIssues, sanitizeDoors, doorTypeOf, doorSwing, slideSide } from '../core/doors';
import { doorSymbol } from '../core/door-symbols';
import { doorZoneDepth, validateFloor, DOOR_ZONE_M, PASSAGE_ZONE_M } from '../core/validate';
import { doorSwingZones } from '../core/furnish';
import { sanitizeDoorType } from '../core/plan-limits';
import { DOOR_TYPES, DOOR_TYPE_RULES } from '../core/rules';
import { checkSnapshot } from '../lib/repo';
import { computeBOQ } from '../core/boq';
import { finishSchedule } from '../core/finish-schedule';
import { floorToPlan, planToFloor } from '../features/migration/legacy.js';
import { viewerInput } from '../lib/viewer-input';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { planToDxf } from '../core/dxf';
import { ro } from '../lib/locales/ro';
import { en } from '../lib/locales/en';
import type { Catalog, DoorType, Material, MaterialsCatalog, Opening, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const rejects = (fn: () => unknown) => assert.throws(fn, (e: any) => e.status === 400);
// camera 5 × 4 m (pereți exteriori) plus un perete interior de 15 cm pe z = 2, cu o ușă la 2,0–2,8 m
const withInner = (o: Partial<Opening> = {}, more: Opening[] = []): Snapshot => { const s = newSnapshot(cat, 'Tipuri', 'blank');
  s.floor.walls.push({ id: 'in-1', a: [0, 2], b: [5, 2], thickness: .15, exterior: false, openings: [{ id: 'd-1', kind: 'door', offset: 2, width: .8, ...o }, ...more] }); return s; };
const inner = (s: Snapshot) => ({ wi: s.floor.walls.findIndex(w => w.id === 'in-1'), oi: 0 });
const codes = (s: Snapshot) => validateFloor(s.floor).filter(i => i.code === 'DOOR_TYPE');

describe('model', () => {
  test('lipsa tipului = batanta de până acum; trecerea și fereastra nu au tip', () => {
    assert.equal(doorTypeOf({ kind: 'door' }), 'swing'); assert.equal(doorTypeOf({ kind: 'door', doorType: 'pocket' }), 'pocket');
    assert.equal(doorTypeOf({ kind: 'door', passage: true, doorType: 'pocket' }), null); assert.equal(doorTypeOf({ kind: 'window' }), null);
    const s = withInner({ doorType: 'sliding', flush: true }), d = doorOpenings(s).find(x => x.id === 'd-1')!; assert.equal(d.type, 'sliding'); assert.equal(d.flush, true);
  });
  test('regulile pe tip: lățimea standard stă în interval', () => {
    for (const k of DOOR_TYPES){ const r = DOOR_TYPE_RULES[k]; assert.ok(r.minM <= r.std && r.std <= r.maxM, k); }
  });
});

describe('server', () => {
  test('tipul și filomuro trec doar pe ușă, din enumerare, cu lățimea tipului', () => {
    const ok = withInner({ doorType: 'double', width: 1.2, flush: true }), r: any = checkSnapshot(structuredClone(ok)), o = r.floor.walls.find((w: any) => w.id === 'in-1').openings[0];
    assert.equal(o.doorType, 'double'); assert.equal(o.flush, true);
    for (const bad of [{ doorType: 'revolving' }, { doorType: 'double', width: .6 }, { doorType: 'swing', width: 1.6 }, { flush: 'da' }, { flush: false }]){
      const t: any = withInner(bad as any); rejects(() => checkSnapshot(t)); }
    const win: any = withInner(); win.floor.walls.find((w: any) => w.id === 'in-1').openings[0] = { id: 'f-1', kind: 'window', offset: 1, width: 1, doorType: 'sliding' }; rejects(() => checkSnapshot(win));
    const old = withInner(), r0: any = checkSnapshot(structuredClone(old)), o0 = r0.floor.walls.find((w: any) => w.id === 'in-1').openings[0]; assert.ok(!('doorType' in o0) && !('flush' in o0));
  });
  test('trecerea câștigă: tipul și filomuro se șterg, nu se resping (client vechi)', () => {
    const s: any = withInner({ passage: true, width: .9, doorType: 'pocket', flush: true }), o = checkSnapshot(s) as any, op = o.floor.walls.find((w: any) => w.id === 'in-1').openings[0];
    assert.equal(op.passage, true); assert.ok(!('doorType' in op) && !('flush' in op));
    const x: any = { kind: 'door', width: .8, doorType: null, flush: null }; sanitizeDoorType(x); assert.deepEqual(x, { kind: 'door', width: .8 });
  });
  test('ușile din catalog păstrează sistemul ales (id scurt); altceva → 400', () => {
    const s = withInner({ doorType: 'pocket' }), r = sanitizeDoors({ 'd-1': { product: 'p', system: 'sys-1' }, nu: { system: 'x' } }, s);
    assert.deepEqual(r.value, { 'd-1': { product: 'p', system: 'sys-1' } }); assert.ok(sanitizeDoors({ 'd-1': { system: 'x'.repeat(81) } }, s).error);
  });
});

describe('spațiu și reguli', () => {
  test('glisantele parchează spre capătul cu mai mult perete liber; altă ușă sau un perete care se prinde scurtează locul', () => {
    const s = withInner({ doorType: 'pocket' }), { wi, oi } = inner(s), f = slideSide(s.floor, wi, oi);
    assert.ok(Math.abs(f.start - 2) < 1e-9 && Math.abs(f.end - 2.2) < 1e-9); assert.equal(doorSwing(s.floor, wi, oi).hinge, 'end');
    const t = withInner({ doorType: 'pocket' }, [{ id: 'd-2', kind: 'door', offset: 3, width: .8 }]); assert.ok(Math.abs(slideSide(t.floor, wi, oi).end - .2) < 1e-9); assert.equal(doorSwing(t.floor, wi, oi).hinge, 'start');
    const u = withInner({ doorType: 'pocket' }); u.floor.walls.push({ id: 'in-2', a: [1, 2], b: [1, 4], thickness: .1, exterior: false, openings: [] }); assert.ok(Math.abs(slideSide(u.floor, wi, oi).start - .95) < 1e-9);
  });
  test('ușa în perete fără loc de casetă = eroare; glisanta = avertisment; balamaua aleasă contează', () => {
    assert.deepEqual(codes(withInner({ doorType: 'pocket' })), []);
    const p = codes(withInner({ doorType: 'pocket', hinge: 'end' }, [{ id: 'd-2', kind: 'door', offset: 3, width: .8 }]));
    assert.equal(p.length, 1); assert.equal(p[0]!.severity, 'ERROR'); assert.equal(p[0]!.key, 'issue.DOOR_TYPE.pocketSpace');
    const sl = codes(withInner({ doorType: 'sliding', hinge: 'end' }, [{ id: 'd-2', kind: 'door', offset: 3, width: .8 }])); assert.equal(sl[0]!.severity, 'WARNING'); assert.equal(sl[0]!.key, 'issue.DOOR_TYPE.slideSpace');
  });
  test('caseta pe perete exterior sau subțire și dubla îngustă: avertismente', () => {
    assert.ok(codes(withInner({ doorType: 'pocket' })).length === 0);
    const thin = withInner({ doorType: 'pocket' }); thin.floor.walls.find(w => w.id === 'in-1')!.thickness = .08; assert.ok(codes(thin).some(i => i.key === 'issue.DOOR_TYPE.pocketWall'));
    const ext = withInner({ doorType: 'pocket' }); ext.floor.walls.find(w => w.id === 'in-1')!.exterior = true; assert.ok(codes(ext).some(i => i.key === 'issue.DOOR_TYPE.pocketWall'));
    assert.ok(codes(withInner({ doorType: 'double', width: .9 })).some(i => i.key === 'issue.DOOR_TYPE.doubleNarrow'));
    assert.ok(!codes(withInner({ doorType: 'double', width: 1.2 })).length);
  });
  test('zona din fața ușii pe tip: glisantele doar circulație, dubla și pliantele jumătate, pivotanta trei sferturi', () => {
    assert.equal(doorZoneDepth({}, .8), DOOR_ZONE_M); assert.equal(doorZoneDepth({ passage: true, doorType: 'double' }, 2), PASSAGE_ZONE_M);
    assert.equal(doorZoneDepth({ doorType: 'pocket' }, .8), PASSAGE_ZONE_M); assert.equal(doorZoneDepth({ doorType: 'sliding' }, 1.2), PASSAGE_ZONE_M);
    assert.equal(doorZoneDepth({ doorType: 'double' }, 1.6), .8); assert.equal(doorZoneDepth({ doorType: 'folding' }, .8), PASSAGE_ZONE_M); assert.ok(Math.abs(doorZoneDepth({ doorType: 'pivot' }, 1.2) - .9) < 1e-9);
    const zs = (t?: DoorType) => { const x = withInner(t ? { doorType: t } : {}); return doorSwingZones(x.floor, x.floor.rooms[0]!); };
    const depth = (r: { z0: number; z1: number }[]) => Math.max(...r.map(q => q.z1 - q.z0));
    assert.ok(depth(zs('pocket')) <= depth(zs()) + 1e-9);
  });
});

describe('simboluri în plan', () => {
  const g = (type: DoorType | null, o: Partial<Parameters<typeof doorSymbol>[0]> = {}) => doorSymbol({ a: [0, 0], b: [.8, 0], thickness: .15, hinge: 'start', swing: 1, type, ...o });
  test('batanta: aceeași foaie și același arc ca înainte', () => {
    assert.deepEqual(g(null).paths, [{ d: 'M0 0L0 0.8A0.8 0.8 0 0 0 0.8 0' }]); assert.equal(g('swing').type, 'swing');
  });
  test('fiecare tip are desenul lui', () => {
    assert.equal(g('double').paths.length, 2); assert.ok(g('double').paths.every(p => p.d.includes('A0.4 0.4')));
    const sl = g('sliding').paths; assert.ok(sl.some(p => p.fill) && sl.some(p => p.dash));
    const pk = g('pocket').paths; assert.equal(pk.length, 2); assert.ok(pk.every(p => p.dash));
    const zig = (w: number) => (doorSymbol({ a: [0, 0], b: [w, 0], thickness: .15, hinge: 'start', swing: 1, type: 'folding' }).paths[0]!.d.match(/L/g) ?? []).length;
    assert.equal(zig(.8), 2); assert.equal(zig(1.2), 4);
    assert.equal(g('pivot').paths.length, 3);
    // în perete: totul în grosimea peretelui (|z| ≤ grosimea / 2), spre balama (x ≤ 0)
    for (const p of pk) for (const [, x, z] of p.d.matchAll(/(-?[\d.]+) (-?[\d.]+)/g)) { assert.ok(Math.abs(+z!) <= .075 + 1e-9); assert.ok(+x! <= 1e-9); }
  });
  test('filomuro: eticheta „F” pe fața opusă deschiderii', () => {
    assert.equal(g(null).label, undefined); const l = g(null, { flush: true }).label!; assert.equal(l.text, 'F'); assert.ok(l.z < 0);
  });
});

describe('integrare', () => {
  test('adaptorul legacy face drumul dus-întors; proiectul vechi nu primește câmpurile', () => {
    const s = withInner({ doorType: 'pivot', flush: true, width: .9 }), pl = floorToPlan(s.floor, 'x'), g = pl.pereti.at(-1)!.goluri[0] as any;
    assert.equal(g.tipUsa, 'pivot'); assert.equal(g.filomuro, true);
    const back = planToFloor(pl).walls.at(-1)!.openings[0]!; assert.equal(back.doorType, 'pivot'); assert.equal(back.flush, true);
    const plain = JSON.stringify(floorToPlan(newSnapshot(cat, 'x', 'blank').floor, 'x')); assert.ok(!plain.includes('tipUsa') && !plain.includes('filomuro'));
    assert.ok(viewerInput(s, cat).plan.pereti.some((w: any) => w.goluri.some((q: any) => q.tipUsa === 'pivot' && q.filomuro && q.sw)));
  });
  test('DXF: dubla are două arce, glisantele doar linii, filomuro nu schimbă nimic la arc', () => {
    const n = (s: Snapshot, type: string) => planToDxf(s, cat, { lang: 'ro' }).split(`0\n${type}\n8\nDOORS\n`).length - 1;
    const base = withInner(); assert.equal(n(withInner({ doorType: 'double', width: 1.2 }), 'ARC'), n(base, 'ARC') + 1);
    for (const t of ['sliding', 'pocket', 'folding'] as const){ const s = withInner({ doorType: t }); assert.equal(n(s, 'ARC'), n(base, 'ARC') - 1, t); assert.ok(n(s, 'LINE') > n(base, 'LINE'), t); }
  });
  test('descrierea planului numește tipul și filomuro', () => {
    const items = describePlan(withInner({ doorType: 'pocket', flush: true }), cat, { t: translator('ro'), units: 'metric', lang: 'ro' }).flatMap(g => g.items);
    assert.match(items.find(i => i.id === 'd-1')!.label, /Glisantă în perete \(casetă\), ascunsă \(filomuro\)$/);
  });
  test('produsul făcut pentru alt tip și priza pe zona de parcare: avertismente; sistemul intră în buget doar pe tipul lui', () => {
    const door = mc.materials.find(m => m.category === 'door')!, sys: Material = { id: 'sys-pocket', category: 'door_system', name: 'Casetă test', supplier: 'Test', unit: 'buc', unitPrice: 1000, sourceUrl: null, verificationType: 'test', confidence: 'LOW', specs: { system: 'pocket' } };
    const m2: MaterialsCatalog = { ...mc, materials: [...mc.materials.map(m => m.id === door.id ? { ...m, specs: { ...m.specs, doorTypes: ['swing'] as DoorType[] } } : m), sys] };
    const s = withInner({ doorType: 'pocket' }); s.doors = { 'd-1': { product: door.id, system: 'sys-pocket' } };
    s.tech = [{ id: 't1', kind: 'outlet', roomId: s.floor.rooms[0]!.id, x: 3.2, z: 2.05, reason: 'test' } as any];
    const keys = doorIssues(s, m2).filter(i => i.openingId === 'd-1').map(i => i.key); assert.ok(keys.includes('door.typeMismatch')); assert.ok(keys.includes('door.techInSlide'));
    s.tech = [{ id: 't1', kind: 'outlet', roomId: s.floor.rooms[0]!.id, x: 1, z: 2.05, reason: 'test' } as any]; assert.ok(!doorIssues(s, m2).some(i => i.key === 'door.techInSlide'));
    const b = computeBOQ(s, cat, m2); assert.equal(b.items.find(i => i.key === 'doorsys:d-1')?.total, 1000);
    assert.ok(finishSchedule(s, cat, m2).some(r => r.element === 'door_system'));
    const sw = withInner({ doorType: 'sliding' }); sw.doors = { 'd-1': { system: 'sys-pocket' } }; assert.ok(!computeBOQ(sw, cat, m2).items.some(i => i.key === 'doorsys:d-1'));
  });
  test('textele noi există în ambele limbi', () => {
    const keys = ['door.type', 'door.flush', 'door.flushShort', 'door.system', 'door.typeMismatch', 'door.techInSlide', 'door.flipSlide', 'door.flipFold', 'door.flipFace', 'door.slideAuto', 'sched.el.door_system',
      ...DOOR_TYPES.flatMap(k => [`door.type.${k}`, `door.typeHint.${k}`]), ...['pocketSpace', 'slideSpace', 'pocketWall', 'doubleNarrow'].map(k => `issue.DOOR_TYPE.${k}`)];
    for (const k of keys){ assert.ok((ro as any)[k], `ro ${k}`); assert.ok((en as any)[k], `en ${k}`); }
  });
});
