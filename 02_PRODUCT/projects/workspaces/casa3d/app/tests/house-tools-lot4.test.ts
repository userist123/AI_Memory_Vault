// Lotul 4 de unelte „prin casă”: tavanul fals pe zonă, balustrada și tipul spațiului (balcon, terasă), boiseria (casete din
// baghetă), covorul cu poziție și al doilea covor. Pentru fiecare: serverul, validările, devizul, 3D-ul, DXF-ul, lista pentru
// cititorul de ecran, tastatura și bara de unelte. Proiectele vechi dau aceleași ieșiri: tests/lot4-legacy-golden.test.ts.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { validateFloor, issueTarget } from '../core/validate';
import { checkSnapshot, HttpError } from '../lib/repo';
import { computeBOQ, finishesOf } from '../core/boq';
import { viewerInput } from '../lib/viewer-input';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { planToDxf } from '../core/dxf';
import { addLevel } from '../core/levels';
import { planItems } from '../core/plan-keys';
import { selectionBox } from '../core/frame';
import { railSlots, TOOL_GROUPS } from '../lib/tool-groups';
import { matchCommand, toolShortcut, formatShortcut } from '../lib/commands';
import { ceilingZoneIssues, ceilingZoneQty, removeRoomCeilingZones, openingUnder } from '../core/ceiling-zones';
import { railingIssues, railingVisual, spaceKindOf } from '../core/railings';
import { sanitizeCeilingZone, sanitizeRailing, PLAN_LIMITS, PlanLimitError } from '../core/plan-limits';
import { frameRects, frameMoulding, autoFrameCount, bandsOverlap, sanitizeFinishes, roomVisual, insertBand } from '../core/finishes';
import { wallElevations } from '../core/drawings';
import { textileIssues, textileVisual, roomRugs, rugRectAt } from '../core/textiles';
import { rugHints } from '../core/decor';
import { finishSchedule } from '../core/finish-schedule';
import type { Catalog, CeilingZone, MaterialsCatalog, Railing, RoomFinishes, Snapshot, WallFeature } from '../core/types';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const t = translator('ro');
// camera 5 × 4 m (blank, tavan 2,6 m, pereți exteriori de 25 cm): fereastra op-2 pe nord (x 1,8–3,2, buiandrug 2,2 m),
// ușa de intrare op-1 pe vest (z 1,55–2,45, 2,1 m)
const blank = (): Snapshot => { const s = newSnapshot(cat, 'Lot 4', 'blank'); s.placements = []; return s; };
const cz = (o: Partial<CeilingZone> = {}): CeilingZone => ({ id: 'tavan-1', roomId: 'camera-1', rect: { x0: 2, z0: 1.5, x1: 3.5, z1: 2.7 }, dropCm: 15, ...o });
const rail = (o: Partial<Railing> = {}): Railing => ({ id: 'bal-1', a: [0, 4.5], b: [3, 4.5], ...o });
const bad400 = (fn: () => unknown) => assert.throws(fn, (e: unknown) => e instanceof HttpError && e.status === 400);
const withFin = (s: Snapshot, p: Partial<RoomFinishes>) => { const room = s.floor.rooms[0]!; s.finishes = { ...(s.finishes || {}), [room.id]: { ...finishesOf(s, room), ...p } }; return s; };
const LED = 'banda-led-hoff-3000k', MOULD = 'dedeman-bagheta-polistiren-decosa-a80-alb-200cm';

describe('tavan fals pe zonă: server și validări', () => {
  test('serverul: coborârea și scafa se aduc în interval, NaN / Infinity / șir se resping; scafa 0 și LED-ul gol dispar', () => {
    assert.equal(sanitizeCeilingZone(cz({ dropCm: 200 })).dropCm, PLAN_LIMITS.ceilingZone.dropMaxCm);
    assert.equal(sanitizeCeilingZone(cz({ dropCm: 1 })).dropCm, PLAN_LIMITS.ceilingZone.dropMinCm);
    for (const v of [NaN, Infinity, '20' as any]) assert.throws(() => sanitizeCeilingZone(cz({ dropCm: v })), PlanLimitError);
    assert.throws(() => sanitizeCeilingZone(cz({ coveCm: NaN })), PlanLimitError);
    const z = sanitizeCeilingZone({ ...cz({ coveCm: 0, led: '' as any, label: ' Masă <b> ' }), extra: 1 });
    assert.deepEqual(z, { id: 'tavan-1', roomId: 'camera-1', rect: cz().rect, dropCm: 15, label: 'Masă b' });
    assert.equal(sanitizeCeilingZone(cz({ coveCm: 90, led: LED })).coveCm, 60);
    assert.throws(() => sanitizeCeilingZone(cz({ rect: { x0: 0, z0: 0, x1: .2, z1: 1 } })), PlanLimitError);
    assert.throws(() => sanitizeCeilingZone(cz({ led: 42 as any })), PlanLimitError);
  });
  test('checkSnapshot: zona unei camere șterse dispare, id dublu și peste plafon = 400', () => {
    const s = blank(); s.floor.ceilingZones = [cz(), cz({ id: 'tavan-2', roomId: 'nu-exista' })];
    assert.deepEqual(checkSnapshot(structuredClone(s)).floor.ceilingZones!.map(z => z.id), ['tavan-1']);
    s.floor.ceilingZones = [cz(), cz()]; bad400(() => checkSnapshot(structuredClone(s)));
    s.floor.ceilingZones = Array.from({ length: PLAN_LIMITS.ceilingZone.max + 1 }, (_, i) => cz({ id: `t${i}` })); bad400(() => checkSnapshot(structuredClone(s)));
    s.floor.ceilingZones = [cz({ dropCm: 'x' as any })]; bad400(() => checkSnapshot(structuredClone(s)));
    s.floor.ceilingZones = []; assert.equal('ceilingZones' in checkSnapshot(structuredClone(s)).floor, false);
  });
  test('validări: iese din cameră, suprapunere, coborâre sub ușa din dreptul ei + 5 cm (doar lângă gol)', () => {
    const s = blank(); const codes = (zs: CeilingZone[]) => ceilingZoneIssues({ ...s.floor, ceilingZones: zs }).map(i => i.code);
    assert.deepEqual(codes([cz()]), []);
    assert.deepEqual(codes([cz({ rect: { x0: 4, z0: 1, x1: 6, z1: 2 } })]), ['CEILING_ZONE_OUTSIDE_ROOM']);
    assert.deepEqual(codes([cz(), cz({ id: 'tavan-2', rect: { x0: 3, z0: 2, x1: 4, z1: 3 } })]), ['CEILING_ZONE_OVERLAP']);
    // lângă ușa de intrare (vest): la 50 cm partea de jos ajunge la 2,10 m < 2,10 + 0,05 → avertisment; la 40 cm (2,20 m) nu
    const nearDoor = cz({ rect: { x0: .2, z0: 1, x1: 1.5, z1: 3 }, dropCm: 50 });
    assert.deepEqual(openingUnder(s.floor, nearDoor), { top: 2.1, kind: 'door' });
    const low = ceilingZoneIssues({ ...s.floor, ceilingZones: [nearDoor] });
    assert.deepEqual(low.map(i => [i.code, i.severity, i.key]), [['CEILING_ZONE_LOW', 'WARNING', 'issue.CEILING_ZONE_LOW.door']]);
    assert.deepEqual(codes([{ ...nearDoor, dropCm: 40 }]), []);
    assert.deepEqual(codes([cz({ dropCm: 50 })]), [], 'în mijlocul camerei nu e niciun gol dedesubt');
    // sub fereastra de pe nord (buiandrug 2,2 m): 40 cm → 2,20 < 2,25
    assert.deepEqual(ceilingZoneIssues({ ...s.floor, ceilingZones: [cz({ rect: { x0: 1.5, z0: .1, x1: 3.5, z1: 1 }, dropCm: 40 })] }).map(i => i.key), ['issue.CEILING_ZONE_LOW.window']);
    // în validateFloor, cu ținta pentru „Sănătate”
    s.floor.ceilingZones = [nearDoor]; const iss = validateFloor(s.floor).find(i => i.code === 'CEILING_ZONE_LOW')!;
    assert.deepEqual(issueTarget(iss, s.floor), { kind: 'ceiling_zone', id: 'tavan-1' });
    assert.match(iss.message, /coboară la 210 cm/);
  });
  test('camera ștearsă își ia tavanele false pe zonă', () => {
    const s = blank(); s.floor.ceilingZones = [cz(), cz({ id: 'b', roomId: 'alta' })]; removeRoomCeilingZones(s.floor, 'camera-1');
    assert.deepEqual(s.floor.ceilingZones.map(z => z.id), ['b']); removeRoomCeilingZones(s.floor, 'alta'); assert.equal('ceilingZones' in s.floor, false);
  });
});

describe('tavan fals pe zonă: deviz, 3D, plan, DXF', () => {
  test('cantitățile: placa, banda laterală, scafa și LED-ul pe contur; tavanul fals al camerei micșorează banda', () => {
    const s = blank(), q = ceilingZoneQty(s.floor, cz({ led: LED, coveCm: 10 }))!;
    assert.deepEqual(q, { w: 1.5, d: 1.2, areaM2: 1.8, perimeterM: 5.4, bandM: .15, bandM2: .81, coveM2: .54, boardM2: 3.15, ledM: 5.4 });
    assert.equal(ceilingZoneQty(s.floor, cz(), 10)!.bandM2, .27);
    assert.equal(ceilingZoneQty(s.floor, cz(), 20)!.bandM2, 0);
  });
  test('devizul: gips-carton cu manoperă de tavan fals, LED în ml, manopera scafei; LED fără preț verificat = necunoscut', () => {
    const s = blank(); s.floor.ceilingZones = [cz({ led: LED, coveCm: 10, label: 'Masă' })]; const b = computeBOQ(s, cat, mc);
    const board = b.items.find(i => i.key === 'camera-1:ceiling:czone:tavan-1')!, led = b.items.find(i => i.key === 'camera-1:led:czone:tavan-1')!;
    assert.equal(board.netQty, 3.15); assert.match(board.label, /^Tavan fals pe zonă „Masă” gips-carton · .*placă 1\.8 m² \+ bandă laterală 0\.81 m² \+ scafă 0\.54 m²/);
    assert.equal(led.netQty, 5.4); assert.equal(led.unit, 'ml');
    assert.deepEqual(b.labor.filter(l => l.key.endsWith(':czone:tavan-1')).map(l => [l.rateId, l.qty]), [['manopera-rigips-tavan', 3.15], ['manopera-scafa', 5.4]]);
    // vopseaua camerei crește cu banda laterală și scafa (fețele casetei se vopsesc)
    const paint = (x: Snapshot) => computeBOQ(x, cat, mc).items.find(i => i.key === 'camera-1:paint')!.label;
    assert.notEqual(paint(s), paint(blank()));
    s.floor.ceilingZones = [cz({ led: 'nu-exista' })];
    assert.ok(computeBOQ(s, cat, mc).unknown.some(u => /^Bandă LED tavan fals pe zonă 5\.4 ml .*fără preț verificat|produsul ales nu are preț verificat/.test(u)));
    // schedule: placa ca element de tavan, LED-ul ca LED
    s.floor.ceilingZones = [cz({ led: LED })]; const els = finishSchedule(s, cat, mc).map(r => r.element);
    assert.ok(els.includes('ceiling') && els.includes('led'));
  });
  test('3D: caseta decupată la cameră, sus sub tavanul camerei, jos la coborâre; fără zone, nicio listă nouă', () => {
    const s = blank(); assert.equal('tavaneZona' in (viewerInput(s, cat, undefined, { mc }).plan as any), false);
    s.floor.ceilingZones = [cz({ led: LED, coveCm: 10, rect: { x0: 4, z0: 1, x1: 6, z1: 2 } })];
    const z = (viewerInput(s, cat, undefined, { mc }).plan as any).tavaneZona[0];
    assert.deepEqual([z.x0, z.x1, z.z0, z.z1, z.top, z.drop, z.cove, z.led], [4, 5, 1, 2, 0, .15, .1, '#ffd29a']);
    withFin(s, { ceiling: { type: 'drop', dropCm: 10 } });
    assert.equal((viewerInput(s, cat, undefined, { mc }).plan as any).tavaneZona[0].top, .1);
  });
  test('Tab, încadrare, cititor de ecran, DXF pe stratul lui', () => {
    const s = blank(); s.floor.ceilingZones = [cz({ label: 'Masă' })];
    assert.deepEqual(planItems(s).map(i => i.kind).slice(0, 2), ['room', 'ceiling_zone']);
    assert.deepEqual(selectionBox(s, cat, { kind: 'ceiling_zone', id: 'tavan-1' }), cz().rect);
    const items = describePlan(s, cat, { t, units: 'metric', lang: 'ro' })[0]!.items;
    assert.equal(items.find(i => i.kind === 'ceiling_zone')!.label, 'Masă · 1,50 m × 1,20 m · coborât 15 cm');
    const dxf = planToDxf(s, cat, { lang: 'ro' }); assert.match(dxf, /2\nCEILING_ZONES/); assert.match(dxf, /TF -15 cm/);
  });
});

describe('balustradă și tipul spațiului', () => {
  test('serverul: înălțimea în interval, NaN / șir respinse, tipul din enumerare, id dublu = 400', () => {
    assert.equal(sanitizeRailing(rail({ height: 5 })).height, 2); assert.equal(sanitizeRailing(rail({ height: .1 })).height, .3);
    assert.throws(() => sanitizeRailing(rail({ height: NaN })), PlanLimitError); assert.throws(() => sanitizeRailing(rail({ height: '1' as any })), PlanLimitError);
    assert.throws(() => sanitizeRailing(rail({ kind: 'wood' as any })), PlanLimitError);
    assert.deepEqual(sanitizeRailing({ ...rail({ kind: 'glass' }), extra: true }), { id: 'bal-1', a: [0, 4.5], b: [3, 4.5], kind: 'glass' });
    assert.throws(() => sanitizeRailing(rail({ a: [NaN, 0] as any })), PlanLimitError);
    const s = blank(); s.floor.railings = [rail(), rail()]; bad400(() => checkSnapshot(structuredClone(s)));
    s.floor.railings = Array.from({ length: PLAN_LIMITS.railing.max + 1 }, (_, i) => rail({ id: `r${i}` })); bad400(() => checkSnapshot(structuredClone(s)));
  });
  test('tipul spațiului: valorile cunoscute rămân, una necunoscută dispare (camera rămâne interior)', () => {
    const s = blank(); s.floor.rooms[0]!.spaceKind = 'open_balcony';
    assert.equal(checkSnapshot(structuredClone(s)).floor.rooms[0]!.spaceKind, 'open_balcony');
    (s.floor.rooms[0] as any).spaceKind = 'garaj'; const out = checkSnapshot(structuredClone(s)).floor.rooms[0]!;
    assert.equal('spaceKind' in out, false); assert.equal(spaceKindOf(out), 'interior');
    s.floor.rooms[0]!.spaceKind = 'terrace'; assert.match(describePlan(s, cat, { t, units: 'metric', lang: 'ro' })[0]!.items[0]!.label, / · Terasă$/);
  });
  test('avertisment sub 90 cm; implicit 1 m fără avertisment', () => {
    assert.deepEqual(railingIssues({ railings: [rail(), rail({ id: 'b', height: .9 })] }), []);
    const iss = railingIssues({ railings: [rail({ height: .8 })] });
    assert.deepEqual(iss.map(i => [i.code, i.severity]), [['RAILING_LOW', 'WARNING']]);
    const s = blank(); s.floor.railings = [rail({ height: .8 })];
    assert.deepEqual(issueTarget(validateFloor(s.floor).find(i => i.code === 'RAILING_LOW')!, s.floor), { kind: 'railing', id: 'bal-1' });
  });
  test('deviz: în ml, pe tip și înălțime, fără preț verificat', () => {
    const s = blank(); s.floor.railings = [rail(), rail({ id: 'b', kind: 'glass', height: 1.1, a: [0, 0], b: [0, 2] })];
    const u = computeBOQ(s, cat, mc).unknown.filter(x => x.startsWith('Balustradă'));
    assert.deepEqual(u, ['Balustradă metal 3 ml, h 100 cm (material și montaj): fără preț verificat', 'Balustradă sticlă 2 ml, h 110 cm (material și montaj): fără preț verificat']);
  });
  test('3D: sticla refolosește pereții de sticlă (cu înălțimea ei), metalul are lista lui; pereții de sticlă vechi rămân la fel', () => {
    const s = blank(); s.floor.glassWalls = [{ id: 'gw-1', a: [1, 1], b: [1, 3], thickness: .08, mullions: 2 }];
    const before = (viewerInput(s, cat).plan as any).peretiSticla; assert.equal(before, s.floor.glassWalls);
    s.floor.railings = [rail({ kind: 'glass', height: 1.1 }), rail({ id: 'm', a: [0, 5], b: [2.5, 5] })];
    const p = viewerInput(s, cat).plan as any;
    assert.deepEqual(p.peretiSticla.map((g: any) => [g.id, g.h ?? null]), [['gw-1', null], ['bal-1', 1.1]]);
    assert.deepEqual(p.balustrade, [{ id: 'm', a: [0, 5], b: [2.5, 5], h: 1, posts: 3 }]);
    assert.deepEqual(railingVisual({ railings: [rail({ a: [0, 0], b: [0, 0] })] }), { glass: [], metal: [] });
  });
  test('niveluri, Tab, încadrare, cititor de ecran, DXF', () => {
    const s = blank(); s.floor.railings = [rail()]; s.floor.ceilingZones = [cz()];
    const up = addLevel(s, { name: 'Etaj 1' }).levels![0]!;
    assert.equal(up.railings!.length, 1); assert.notEqual(up.railings![0]!.id, 'bal-1'); assert.equal('ceilingZones' in up, false, 'tavanele false țin de camerele nivelului');
    assert.ok(planItems(s).some(i => i.kind === 'railing')); assert.deepEqual(selectionBox(s, cat, { kind: 'railing', id: 'bal-1' }), { x0: -.025, x1: 3.025, z0: 4.475, z1: 4.525 });
    assert.ok(describePlan(s, cat, { t, units: 'metric', lang: 'ro' }).flatMap(g => g.items).some(i => i.kind === 'railing' && i.label === 'Balustradă · Metal · 3,00 m · h 1,00 m'));
    assert.match(planToDxf(s, cat, { lang: 'ro' }), /2\nRAILINGS/);
  });
});

describe('boiserie (casete din baghetă)', () => {
  const frames = (o: Partial<WallFeature> = {}): WallFeature => ({ side: 'S', kind: 'frames', material: MOULD, ...o });
  test('numărul de casete se calculează din lungime; casetele egale umplu banda minus margini; nu trec peste goluri', () => {
    assert.equal(autoFrameCount(3.7), 4); assert.equal(autoFrameCount(.5), 1); assert.equal(autoFrameCount(100), 12);
    const s = blank(), room = s.floor.rooms[0]!;
    const r = frameRects(s.floor, room, frames({ fromM: .1, heightM: 1 }));
    assert.equal(r.length, 5); assert.deepEqual(r[0], [.15, .25, 1.01, .85]);
    assert.equal(frameRects(s.floor, room, frames({ frames: { count: 2, marginM: .2, gapM: .2 } })).length, 2);
    // pe vest e ușa de intrare: caseta din dreptul ei (pe toată înălțimea) nu se pune
    const west = frameRects(s.floor, room, frames({ side: 'W', frames: { count: 4 } }));
    assert.equal(west.length, 2); assert.ok(west.every(([u0, , u1]) => u1 <= 1.55 + .02 || u0 >= 2.45 - .02));
    // sub fereastra de pe nord (parapet 0,9 m) un brâu de casete 0,1–0,8 m rămâne întreg
    assert.equal(frameRects(s.floor, room, frames({ side: 'N', fromM: .1, heightM: .8 })).length, autoFrameCount(5 - .3));
  });
  test('bagheta: ml = perimetrele casetelor; bucăți întregi de 2 m, resturile refolosite', () => {
    assert.deepEqual(frameMoulding([[0, 0, 1, .5]]), { ml: 3, pieces: null });
    assert.deepEqual(frameMoulding([[0, 0, 1, .5]], 2), { ml: 3, pieces: 2 });
    assert.equal(frameMoulding([[0, 0, .8, 2.3]], 2).pieces, 4, 'o latură de 2,3 m cere două bucăți');
  });
  test('deviz: bagheta în bucăți, manopera în ml; vopseaua peretelui rămâne aceeași', () => {
    const s = withFin(blank(), { wallFeatures: [frames({ fromM: .1, heightM: 1 })] }), b = computeBOQ(s, cat, mc);
    const line = b.items.find(i => i.key === 'camera-1:wall:S:0')!, rects = frameRects(s.floor, s.floor.rooms[0]!, frames({ fromM: .1, heightM: 1 })), mo = frameMoulding(rects, 2);
    assert.equal(line.unit, 'buc'); assert.equal(line.orderedQty, mo.pieces); assert.match(line.label, /^Boiserie baghetă · Living, perete sud \(5 casete, 0\.1–1 m\): 14\.6 ml, 9 bucăți de 2 m$/);
    assert.deepEqual(b.labor.filter(l => l.key.includes('frames')).map(l => [l.rateId, l.qty]), [['manopera-bagheta', 14.6]]);
    const paint = (x: Snapshot) => computeBOQ(x, cat, mc).items.find(i => i.key === 'camera-1:paint')!.netQty;
    assert.equal(paint(s), paint(blank()));
    const s2 = withFin(blank(), { wallFeatures: [frames({ material: 'nu-exista' })] });
    assert.ok(computeBOQ(s2, cat, mc).unknown.some(u => /^Boiserie baghetă .* ml · .*produsul ales nu are preț verificat$/.test(u)));
  });
  test('serverul: casetele doar la boiserie, numărul 1–12, marginea și distanța 0–50 cm; peste vopsea nu e suprapunere', () => {
    const ok = (wf: WallFeature[]) => sanitizeFinishes({ r: { floor: 'x', wallPaint: 'y', light: 'z', wallFeatures: wf } });
    assert.equal(ok([frames({ frames: { count: 3, marginM: .1, gapM: .05 } })]), null);
    for (const fr of [{ count: 13 }, { count: 1.5 }, { count: 0 }, { marginM: .6 }, { gapM: -1 }, { gapM: NaN }, { other: 1 } as any])
      assert.equal(ok([frames({ frames: fr })]), 'Placările de pe pereți sunt invalide.', JSON.stringify(fr));
    assert.equal(ok([{ side: 'S', kind: 'paint', material: 'v', frames: { count: 2 } }]), 'Placările de pe pereți sunt invalide.');
    const both: WallFeature[] = [{ side: 'S', kind: 'paint', material: 'v', color: '#c0b0a0' }, frames()];
    assert.equal(bandsOverlap(both, 2.6), false); assert.equal(ok(both), null);
    assert.equal(insertBand([{ side: 'S', kind: 'paint', material: 'v' }], frames(), 5, 2.6).length, 2, 'vopseaua de sub boiserie nu se taie');
  });
  test('3D: casetele gata calculate în vizualul peretelui; elevația desenează chenarele', () => {
    const s = withFin(blank(), { wallFeatures: [frames({ fromM: .1, heightM: 1 })] }), room = s.floor.rooms[0]!;
    const wv = roomVisual(mc, finishesOf(s, room), room.type, room, s.floor).walls.find(w => w.kind === 'frames')!;
    assert.equal(wv.frames!.length, 5); assert.equal(wv.profile, .08);
    assert.equal((viewerInput(s, cat, undefined, { mc }).plan as any).camere[0].fin.walls[0].frames.length, 5);
    const south = wallElevations(s, mc, cat, s.floor, room).find(e => e.side === 'S')!;
    assert.equal(south.bands[0]!.frames!.length, 5); assert.deepEqual(south.bands[0]!.frames![0], [3.99, .25, 4.85, .85], 'latura de sud se citește de la x1');
  });
});

describe('covor cu poziție și al doilea covor', () => {
  const RUG = 'covor-morum-80x200';
  test('serverul: poziția e un număr finit în plan, cel mult două covoare în plus', () => {
    const fin = (p: Partial<RoomFinishes>) => sanitizeFinishes({ r: { floor: 'x', wallPaint: 'y', light: 'z', ...p } });
    assert.equal(fin({ rug: { material: RUG, x: 2, z: 1 }, rugs: [{ material: RUG, x: 1, z: 3 }] }), null);
    assert.equal(fin({ rug: { material: RUG, x: NaN } }), 'Covorul este invalid.');
    assert.equal(fin({ rug: { material: RUG, z: 1e6 } }), 'Covorul este invalid.');
    assert.equal(fin({ rugs: [{ material: RUG }, { material: RUG }, { material: RUG }] }), 'Covoarele în plus sunt invalide.');
    assert.equal(fin({ rugs: [{ material: '' }] }), 'Covoarele în plus sunt invalide.');
    assert.equal(fin({ rugs: {} as any }), 'Covoarele în plus sunt invalide.');
  });
  test('3D: covorul centrat rămâne ca înainte; cel mutat are x / z; covoarele în plus au lista lor', () => {
    const s = blank(), room = s.floor.rooms[0]!;
    assert.deepEqual(textileVisual(mc, s.floor, room, { ...finishesOf(s, room), rug: { material: RUG } }).rug, { w: 2, d: .8, color: '#3e4d3b' });
    withFin(s, { rug: { material: RUG, x: 1.5, z: 1 }, rugs: [{ material: RUG, rotate: true }] });
    const fin = (viewerInput(s, cat, undefined, { mc }).plan as any).camere[0].fin;
    assert.deepEqual([fin.rug.x, fin.rug.z], [1.5, 1]); assert.deepEqual(fin.rugs, [{ w: .8, d: 2, color: '#3e4d3b', x: 2.5, z: 2 }]);
  });
  test('deviz: o linie pe covor; validări: în afara camerei și suprapunere, doar pentru covoarele mutate sau în plus', () => {
    const s = withFin(blank(), { rug: { material: RUG, x: 1.2, z: .6 }, rugs: [{ material: RUG, x: 1.4, z: .7 }] }), room = s.floor.rooms[0]!;
    const keys = computeBOQ(s, cat, mc).items.filter(i => i.key.startsWith('camera-1:rug')).map(i => [i.key, i.label]);
    assert.deepEqual(keys, [['camera-1:rug', 'Covor · Living'], ['camera-1:rug:2', 'Covor 2 · Living']]);
    assert.deepEqual(textileIssues(mc, s.floor, room, finishesOf(s, room)).map(i => i.key), ['tex.rugOverlap']);
    const out = withFin(blank(), { rug: { material: RUG, x: .5, z: 2 } });
    assert.deepEqual(textileIssues(mc, out.floor, room, finishesOf(out, room)).map(i => [i.key, i.vars?.n]), [['tex.rugOutside', 1]]);
    assert.deepEqual(roomRugs({ rug: null, rugs: [{ material: RUG }] }).length, 1);
    assert.deepEqual(rugRectAt(room, { w: 2, d: .8 }), { x0: 1.5, x1: 3.5, z0: 1.6, z1: 2.4 });
  });
  test('regula canapelei pe poziția reală: covorul mutat departe de canapea primește sfatul; cel centrat nu', () => {
    const s = newSnapshot(cat, 'Demo', 'demo', 'ro', { materials: mc }), fl = s.floor;
    const room = fl.rooms.find(r => r.type === 'living' && s.finishes?.[r.id]?.rug && s.placements.some(p => p.roomId === r.id && p.group === 'canapea'))!;
    const f = finishesOf(s, room); assert.deepEqual(rugHints(s, cat, mc, room, f), []);
    const far = { ...f, rug: { ...f.rug!, x: room.rect.x0 + .1, z: room.rect.z0 + .1 } };
    assert.deepEqual(rugHints(s, cat, mc, room, far).map(h => h.key), ['tex.rugSofa']);
  });
});

describe('bara de unelte și tastele', () => {
  const survey = ['select', 'wall', 'room', 'door', 'window', 'passage', 'niche', 'column', 'shaft', 'beam', 'glass_wall', 'railing', 'demolish', 'stair', 'measure', 'dim', 'text'] as const;
  test('Balustrada intră în Structură (Releveul rămâne cu 7 locuri); Tavanul fals pe zonă în Decor (Finisaje rămâne cu 3)', () => {
    assert.ok(TOOL_GROUPS.find(g => g.id === 'structure')!.tools.includes('railing')); assert.equal(railSlots([...survey], true).length, 7);
    assert.deepEqual(railSlots(['select', 'clad', 'wall_item', 'floor_zone', 'ceiling_zone', 'measure'], true).map(s => 'tool' in s ? s.tool : s.group), ['select', 'decor', 'measure']);
  });
  test('Shift+Z = tavan fals pe zonă, Shift+Y = balustradă; Z și Y rămân zonă de pardoseală și demolare', () => {
    const ev = (key: string, shiftKey = false) => ({ key, shiftKey, ctrlKey: false, metaKey: false, altKey: false });
    assert.equal(matchCommand(ev('Z', true)), 'tool.ceiling_zone'); assert.equal(matchCommand(ev('Y', true)), 'tool.railing');
    assert.equal(matchCommand(ev('z')), 'tool.floor_zone'); assert.equal(matchCommand(ev('y')), 'tool.demolish');
    assert.equal(formatShortcut(toolShortcut('ceiling_zone'), { mac: false }), 'Shift+Z'); assert.equal(formatShortcut(toolShortcut('railing'), { mac: true }), '⇧Y');
  });
});
