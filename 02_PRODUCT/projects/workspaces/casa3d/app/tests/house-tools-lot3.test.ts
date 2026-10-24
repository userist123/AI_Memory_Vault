// Lotul 3 de unelte „prin casă”: starea peretelui (existent / nou / de demolat) cu unealta Demolare, caloriferul cu corp,
// aerul condiționat în 3D și în deviz, zona de pardoseală. Pentru fiecare: modelul, planul, 3D-ul, devizul, validările,
// serverul, lista pentru cititorul de ecran și bara de unelte; la final, un proiect vechi rămâne identic.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { validateFloor, validatePlacement, openingsOnSide, issueTarget } from '../core/validate';
import { checkSnapshot, HttpError } from '../lib/repo';
import { computeBOQ } from '../core/boq';
import { viewerInput } from '../lib/viewer-input';
import { floorToPlan } from '../features/migration/legacy.js';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { planToDxf } from '../core/dxf';
import { doorOpenings } from '../core/doors';
import { wallWorks, renovationIssues } from '../core/renovation';
import { placeTechPoint, sanitizeTech, techIssues, techBodyFields, techBodyCount } from '../core/technical';
import { techFrame, radiatorRect, radiatorFront, isSizedRadiator } from '../core/tech-geometry';
import { zoneIssues, zonesAreaIn, zoneAreaIn, defaultZoneMaterial, removeRoomZones } from '../core/floor-zones';
import { sanitizeFloorElements, sanitizeZone, sanitizeWallStatus, PlanLimitError, PLAN_LIMITS } from '../core/plan-limits';
import { planItems } from '../core/plan-keys';
import { selectionBox } from '../core/frame';
import { addLevel } from '../core/levels';
import { railSlots, TOOL_GROUPS } from '../lib/tool-groups';
import type { Catalog, FloorZone, MaterialsCatalog, Snapshot, Wall } from '../core/types';
import type { TechPoint } from '../core/technical';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const t = translator('ro');
// camera 5 × 4 m (blank, pereți exteriori de 25 cm): wall-1 = nord [0,0]→[5,0] cu fereastra op-2, wall-4 = vest cu ușa de intrare
const blank = (): Snapshot => { const s = newSnapshot(cat, 'Lot 3', 'blank'); s.placements = []; return s; };
// un perete interior de 10 cm pe x = 2,5 (de la nord la sud), cu o ușă
const inner = (o: Partial<Wall> = {}): Wall => ({ id: 'wall-in', a: [2.5, 0], b: [2.5, 4], thickness: .1, exterior: false, openings: [{ id: 'op-in', kind: 'door', offset: 1, width: .8 }], ...o });
const unknownOf = (s: Snapshot) => computeBOQ(s, cat, mc).unknown;
const bad400 = (fn: () => unknown) => assert.throws(fn, (e: unknown) => e instanceof HttpError && e.status === 400);

describe('starea peretelui: model, server, validări', () => {
  test('serverul: lipsă / existent = fără câmp; nou și de demolat se păstrează; altceva (șir, număr, NaN) se respinge', () => {
    const s = blank(); s.floor.walls.push(inner({ status: 'new' })); (s.floor.walls[0] as any).status = 'existing'; s.floor.walls[1]!.status = 'demolish';
    const out = checkSnapshot(structuredClone(s));
    assert.equal('status' in out.floor.walls[0]!, false); assert.equal(out.floor.walls[1]!.status, 'demolish'); assert.equal(out.floor.walls[4]!.status, 'new');
    for (const v of ['ruined', 1, NaN, Infinity, '']) { const b = blank(); (b.floor.walls[0] as any).status = v; bad400(() => checkSnapshot(b)); }
    const w: any = { status: null }; sanitizeWallStatus(w); assert.equal('status' in w, false);
  });
  test('avertisment doar la demolarea unui perete exterior; peretele interior se demolează fără avertisment', () => {
    const s = blank(); s.floor.walls.push(inner({ status: 'demolish' }));
    assert.deepEqual(validateFloor(s.floor).filter(i => i.code === 'WALL_DEMOLISH'), []);
    s.floor.walls[0]!.status = 'demolish'; const w = validateFloor(s.floor).filter(i => i.code === 'WALL_DEMOLISH');
    assert.equal(w.length, 1); assert.equal(w[0]!.severity, 'WARNING'); assert.equal(w[0]!.vars?.id, 'wall-1'); assert.match(t(w[0]!.key!, w[0]!.vars as any), /inginer/);
    assert.deepEqual(issueTarget(w[0]!, s.floor), { kind: 'wall', id: 'wall-1' }); assert.equal(renovationIssues(s.floor).length, 1);
  });
  test('golurile peretelui de demolat nu mai contează: nici în calcule, nici în validări, nici la uși de cumpărat', () => {
    const s = blank(); s.floor.walls.push(inner({ openings: [{ id: 'op-in', kind: 'door', offset: 3.6, width: .8 }] }));   // ușa iese din perete (3,6 + 0,8 > 4)
    assert.ok(validateFloor(s.floor).some(i => i.code === 'OPENING_OUTSIDE_WALL' && i.vars?.op === 'op-in'));
    assert.ok(doorOpenings(s).some(d => d.id === 'op-in'));
    s.floor.walls[4]!.status = 'demolish';
    assert.ok(!validateFloor(s.floor).some(i => i.vars?.op === 'op-in')); assert.ok(!doorOpenings(s).some(d => d.id === 'op-in'));
    const room = s.floor.rooms[0]!; s.floor.walls[0]!.status = 'demolish';
    assert.deepEqual(openingsOnSide(s.floor, room, 'N'), [], 'fereastra din peretele de nord demolat nu se mai scade');
  });
});

describe('starea peretelui: plan, 3D, deviz, DXF, cititor de ecran, niveluri', () => {
  test('deviz: demolarea în m² cu evacuarea molozului (necunoscută), gips-carton nou cu preț de placă, zidăria necunoscută', () => {
    const s = blank(); s.floor.walls.push(inner({ status: 'demolish' }));
    const ww = wallWorks(s.floor)[0]!; assert.equal(ww.status, 'demolish'); assert.equal(ww.areaM2, Math.round((4 * 2.6 - .8 * 2.1) * 100) / 100); assert.equal(ww.system, 'drywall');
    assert.ok(unknownOf(s).some(u => u.startsWith(`Demolare ${ww.areaM2} m² cu evacuarea molozului`)));
    s.floor.walls[4]!.status = 'new'; const boq = computeBOQ(s, cat, mc), line = boq.items.find(i => i.key === 'wall-new:wall-in');
    assert.ok(line, 'placa de gips-carton are linie cu preț'); assert.equal(line!.netQty, Math.round(ww.areaM2 * 2 * 100) / 100); assert.equal(line!.roomId, null);
    assert.ok(boq.unknown.some(u => u.startsWith('Manoperă perete nou gips-carton')));
    s.floor.walls[4]!.thickness = .25; assert.ok(unknownOf(s).some(u => u.startsWith('Zidărie nouă')));
    assert.ok(!(mc.materials as any[]).some(m => /demol|moloz|zid/i.test(m.id)), 'niciun preț inventat în catalog');
  });
  test('3D: peretele de demolat rămâne în listă (indicii nu se mută), marcat, fără goluri; cel nou e marcat', () => {
    const s = blank(); s.floor.walls[0]!.status = 'demolish'; s.floor.walls.push(inner({ status: 'new' }));
    const p = floorToPlan(s.floor, 'x'); assert.equal(p.pereti.length, 5); assert.equal(p.pereti[0].demolat, true); assert.deepEqual(p.pereti[0].goluri, []); assert.equal(p.pereti[4].nou, true);
    const vi = viewerInput(s, cat); assert.equal(vi.plan.pereti[0].demolat, true); assert.equal(vi.plan.pereti[1].demolat, undefined);
  });
  test('DXF: straturi separate WALLS_NEW / WALLS_DEMOLISH doar când există; peretele demolat fără goluri', () => {
    const old = planToDxf(blank(), cat, { lang: 'ro' }); assert.ok(!/WALLS_NEW|WALLS_DEMOLISH|FLOOR_ZONES/.test(old));
    const s = blank(); s.floor.walls[0]!.status = 'demolish'; s.floor.walls.push(inner({ status: 'new' }));
    const d = planToDxf(s, cat, { lang: 'ro' }); assert.match(d, /2\nWALLS_NEW/); assert.match(d, /2\nWALLS_DEMOLISH/); assert.ok((d.match(/8\nWALLS_NEW/g) ?? []).length > 3, 'hașura peretelui nou');
    assert.equal((d.match(/8\nWINDOWS/g) ?? []).length, 0, 'fereastra peretelui demolat nu mai apare');
  });
  test('cititorul de ecran: starea apare în descrierea peretelui; peretele existent rămâne ca înainte', () => {
    const s = blank(); const before = describePlan(s, cat, { t, units: 'metric', lang: 'ro' }).find(g => g.id === 'structure')!.items.find(i => i.id === 'wall-1')!.label;
    s.floor.walls[0]!.status = 'demolish'; s.floor.walls[1]!.status = 'new';
    const items = describePlan(s, cat, { t, units: 'metric', lang: 'ro' }).find(g => g.id === 'structure')!.items;
    assert.equal(items.find(i => i.id === 'wall-1')!.label, `${before} · De demolat`); assert.match(items.find(i => i.id === 'wall-2')!.label, / · Nou \(de construit\)$/);
    assert.ok(!/De demolat|Nou \(de construit\)/.test(items.find(i => i.id === 'wall-3')!.label), 'peretele existent fără sufix');
    const en = describePlan(s, cat, { t: translator('en'), units: 'metric', lang: 'en' }).find(g => g.id === 'structure')!.items; assert.match(en.find(i => i.id === 'wall-1')!.label, /To demolish$/);
  });
  test('nivelul nou copiază starea pereților (aceleași lucrări pe verticală)', () => {
    const s = blank(); s.floor.walls[1]!.status = 'new'; const h = addLevel(s, { name: 'Etaj' }); assert.equal(h.levels![0]!.walls[1]!.status, 'new');
  });
});

describe('caloriferul (TechPoint radiator cu mărimi)', () => {
  test('plasare: lângă perete, centrat sub fereastra cea mai apropiată de pe acel perete, 80 × 60 cm la 15 cm', () => {
    const s = blank(), win = s.floor.walls[0]!.openings.find(o => o.kind === 'window')!, mid = win.offset + win.width / 2;
    const p = placeTechPoint(s, 'radiator', mid + 1.2, .3)!; assert.ok(isSizedRadiator(p)); assert.equal(p.x, Math.round(mid * 1000) / 1000); assert.equal(p.widthM, .8); assert.equal(p.heightM, .6); assert.equal(p.height, .15); assert.equal(p.model, 'panel');
    const q = placeTechPoint(s, 'radiator', 4.8, 2)!; assert.equal(q.z, 2, 'pe un perete fără fereastră rămâne unde s-a dat clic'); assert.equal(techFrame(s.floor, q)!.side, 'E');
    const o = placeTechPoint(s, 'outlet', 1, .3)!; assert.equal('widthM' in o, false, 'celelalte puncte rămân fără corp');
  });
  test('plan: dreptunghiul pe fața peretelui (după grosime), cu zona de 30 cm din față', () => {
    const s = blank(), p = placeTechPoint(s, 'radiator', 2.5, .3)!, r = radiatorRect(s.floor, p)!;
    assert.ok(Math.abs(r.x1 - r.x0 - .8) < 1e-9); assert.ok(Math.abs(r.z0 - (.125 + .03)) < 1e-9 && Math.abs(r.z1 - (.125 + .13)) < 1e-9);
    assert.ok(Math.abs(radiatorFront(s.floor, p)!.z1 - (.125 + .43)) < 1e-9);
    assert.equal(radiatorRect(s.floor, { ...p, widthM: undefined }), null, 'punctul vechi de racord rămâne simbol');
  });
  test('server: mărimile se aduc în interval, NaN / Infinity / șiruri / tip necunoscut se resping; plafonul pe proiect', () => {
    const s = blank(), p = placeTechPoint(s, 'radiator', 2.5, .3)!;
    assert.deepEqual(techBodyFields({ ...p, widthM: 9, heightM: .01 }), { widthM: PLAN_LIMITS.radiator.widthMaxM, heightM: PLAN_LIMITS.radiator.heightMinM, model: 'panel' });
    for (const v of [NaN, Infinity, '80']) assert.throws(() => techBodyFields({ ...p, widthM: v }), PlanLimitError);
    assert.throws(() => techBodyFields({ ...p, model: 'cast-iron' }), PlanLimitError);
    assert.deepEqual(techBodyFields({ kind: 'hvac_split', widthM: 3 }), { widthM: PLAN_LIMITS.hvac.widthMaxM });
    assert.deepEqual(sanitizeTech([{ ...p, widthM: 2 }], s)![0]!.widthM, 2);
    const b = blank(); b.tech = [{ ...p, widthM: NaN as any }]; bad400(() => checkSnapshot(b));
    const many = blank(); many.tech = Array.from({ length: PLAN_LIMITS.radiator.max + 1 }, (_, i) => ({ ...p, id: `radiator:camera-1:m${i + 1}` })); bad400(() => checkSnapshot(many));
    assert.equal(techBodyCount(many.tech, 'radiator'), PLAN_LIMITS.radiator.max + 1);
    const ok = checkSnapshot(structuredClone({ ...blank(), tech: [p] })); assert.deepEqual(ok.tech![0], p);
  });
  test('3D: cutie cu nervuri pe fața peretelui (doar caloriferul cu corp); deviz pe bucată fără preț', () => {
    const s = blank(), p = placeTechPoint(s, 'radiator', 2.5, .3)!; s.tech = [p, { ...p, id: 'radiator:camera-1:vechi', widthM: undefined, heightM: undefined, model: undefined }];
    const te = (viewerInput(s, cat).plan as any).tehnice; assert.equal(te.length, 1); assert.equal(te[0].kind, 'radiator'); assert.ok(Math.abs(te[0].z - .125) < 1e-9); assert.deepEqual([te[0].nx, te[0].nz], [0, 1]);
    const u = unknownOf(s).filter(x => x.startsWith('Calorifer')); assert.equal(u.length, 1); assert.match(u[0]!, /panou 80×60 cm .*fără preț verificat/);
  });
  test('avertisment când mobilierul înalt stă în fața lui (ca la fereastră); piesa joasă și punctul vechi nu', () => {
    const s = blank(), p = placeTechPoint(s, 'radiator', 4.8, 2)!; s.tech = [p];
    const tall = { id: 'dulap', roomId: 'camera-1', group: 'dulap', variantId: 'dulap-0', x: 4.6, z: 2, rotation: -Math.PI / 2, source: 'manual' as const };
    s.placements = [tall]; assert.ok(validatePlacement(s, cat, tall).some(i => i.code === 'RADIATOR_BLOCKED' && i.severity === 'WARNING'));
    s.tech = [{ ...p, widthM: undefined }]; assert.ok(!validatePlacement(s, cat, tall).some(i => i.code === 'RADIATOR_BLOCKED'));
  });
});

describe('aerul condiționat (hvac_split) în 3D și în deviz', () => {
  test('3D: unitate interioară albă de 80 × 29 × 20 cm pe fața peretelui, la înălțimea punctului', () => {
    const s = blank(), p = placeTechPoint(s, 'hvac_split', 4.8, 2)!; s.tech = [p];
    const te = (viewerInput(s, cat).plan as any).tehnice[0]; assert.equal(te.kind, 'ac'); assert.deepEqual([te.w, te.h, te.d], [.8, .29, .2]); assert.equal(te.y, 2.2); assert.ok(Math.abs(te.x - (5 - .125)) < 1e-9); assert.deepEqual([te.nx, te.nz], [-1, 0]);
    s.floor.ceilingHeight = 2.4; assert.ok(Math.abs((viewerInput(s, cat).plan as any).tehnice[0].y - (2.4 - .29 - .02)) < 1e-9, 'sub tavanul coborât');
  });
  test('deviz: „Unitate AC interioară + traseu” necunoscută, pe bucată', () => {
    const s = blank(); s.tech = [placeTechPoint(s, 'hvac_split', 4.8, 2)!]; assert.ok(unknownOf(s).some(u => u.startsWith('Unitate AC interioară + traseu (1 buc, 80 cm) · ')));
  });
  test('avertismente: deasupra patului și în zona unei uși glisante', () => {
    const s = blank(); s.tech = [placeTechPoint(s, 'hvac_split', 2.5, 3.9)!];   // peretele de sud
    s.placements = [{ id: 'pat', roomId: 'camera-1', group: 'pat', variantId: 'pat-0', x: 2.5, z: 3, rotation: Math.PI, source: 'manual' }];
    assert.deepEqual(techIssues(s, cat, s.floor).map(i => i.key), ['issue.HVAC.overBed']);
    s.placements = []; s.floor.walls[2]!.openings.push({ id: 'op-sl', kind: 'door', offset: 1.8, width: .8, doorType: 'sliding' });
    assert.deepEqual(techIssues(s, cat, s.floor).map(i => i.key), ['issue.HVAC.slide']);
  });
});

describe('zona de pardoseală (Floor.zones)', () => {
  const zone = (o: Partial<FloorZone> = {}): FloorZone => ({ id: 'zona-1', roomId: 'camera-1', rect: { x0: 0, z0: 0, x1: 2, z1: 1.5 }, floor: 'gresie-mckinley', ...o });
  test('validări: ZONE_OUTSIDE_ROOM și ZONE_OVERLAP sunt erori', () => {
    const s = blank(); s.floor.zones = [zone()]; assert.deepEqual(zoneIssues(s.floor), []);
    s.floor.zones = [zone({ rect: { x0: 4, z0: 0, x1: 6, z1: 1 } })]; const o = validateFloor(s.floor).filter(i => i.code === 'ZONE_OUTSIDE_ROOM');
    assert.equal(o.length, 1); assert.equal(o[0]!.severity, 'ERROR'); assert.deepEqual(issueTarget(o[0]!, s.floor), { kind: 'floor_zone', id: 'zona-1' });
    s.floor.zones = [zone(), zone({ id: 'zona-2', rect: { x0: 1, z0: 1, x1: 3, z1: 3 } })]; assert.deepEqual(validateFloor(s.floor).filter(i => i.code === 'ZONE_OVERLAP').map(i => i.severity), ['ERROR']);
    s.floor.zones = [zone(), zone({ id: 'zona-2', rect: { x0: 2, z0: 0, x1: 3, z1: 1 } })]; assert.deepEqual(zoneIssues(s.floor), [], 'zonele lipite nu se suprapun');
  });
  test('deviz: pardoseala camerei minus zonele, plus o linie proprie pentru zonă (aceeași suprafață totală)', () => {
    const s = blank(); const before = computeBOQ(s, cat, mc).items.find(i => i.key === 'camera-1:floor')!;
    s.floor.zones = [zone()]; const boq = computeBOQ(s, cat, mc), room = boq.items.find(i => i.key === 'camera-1:floor')!, z = boq.items.find(i => i.key === 'camera-1:zone:zona-1')!;
    assert.equal(room.netQty, Math.round((20 - 3) * 100) / 100); assert.equal(z.netQty, 3); assert.equal(room.netQty + z.netQty, before.netQty);
    assert.equal(z.refId, 'gresie-mckinley'); assert.ok(boq.labor.some(l => l.key === 'camera-1:manopera-gresie:zone:zona-1'));
    assert.ok(boq.items.some(i => i.key === 'adhesive'), 'gresia zonei cere adeziv');
    assert.equal(zonesAreaIn(s.floor, 'camera-1'), 3); assert.equal(zoneAreaIn(s.floor, zone({ rect: { x0: 4, z0: 0, x1: 6, z1: 1 } })), 1, 'partea din afara camerei nu se plătește');
    s.floor.zones = [zone({ floor: 'nu-exista' })]; assert.ok(unknownOf(s).some(u => u.startsWith('Pardoseală zonă · Living')));
  });
  test('materialul implicit contrastează cu al camerei (gresie peste parchet, parchet peste gresie)', () => {
    const tile = defaultZoneMaterial(mc, 'parchet-egger-h2099')!, wood = defaultZoneMaterial(mc, 'gresie-mckinley')!;
    assert.equal(mc.materials.find(m => m.id === tile)!.category, 'floor_tile'); assert.equal(mc.materials.find(m => m.id === wood)!.category, 'parquet'); assert.equal(defaultZoneMaterial(null, 'x'), null);
  });
  test('3D: zona la +1 mm, decupată la cameră, cu pardoseala ei', () => {
    const s = blank(); s.floor.zones = [zone({ rect: { x0: 4, z0: 0, x1: 6, z1: 1 } })];
    const zp = (viewerInput(s, cat, undefined, { mc }).plan as any).zonePardoseala; assert.equal(zp.length, 1); assert.deepEqual([zp[0].x0, zp[0].x1], [4, 5]); assert.equal(zp[0].fin.kind, 'tile');
  });
  test('server: câmpurile modelului, latura minimă, NaN / șiruri respinse, zona camerei care nu mai există se elimină', () => {
    const z = sanitizeZone({ ...zone(), extra: 1, label: ' <b>Intrare</b> ', layout: { pattern: 'diagonal', groutMm: 99, angle: 90 } });
    assert.deepEqual(z, { id: 'zona-1', roomId: 'camera-1', rect: { x0: 0, z0: 0, x1: 2, z1: 1.5 }, floor: 'gresie-mckinley', layout: { pattern: 'diagonal', angle: 90, groutMm: PLAN_LIMITS.zone.groutMaxMm }, label: 'b Intrare /b' });
    for (const r of [{ x0: 0, z0: 0, x1: .05, z1: 1 }, { x0: NaN, z0: 0, x1: 1, z1: 1 }, { x0: '0', z0: 0, x1: 1, z1: 1 }, { x0: 0, z0: 0, x1: 1e9, z1: 1 }]) assert.throws(() => sanitizeZone(zone({ rect: r as any })), PlanLimitError);
    assert.throws(() => sanitizeZone(zone({ floor: 5 as any })), PlanLimitError); assert.throws(() => sanitizeZone({ ...zone(), layout: { pattern: 'zigzag' } }), PlanLimitError);
    const f: any = structuredClone(blank().floor); f.zones = [zone(), zone({ id: 'zona-2', roomId: 'camera-x' })]; sanitizeFloorElements(f); assert.deepEqual(f.zones.map((x: any) => x.id), ['zona-1']);
    const dup: any = structuredClone(blank().floor); dup.zones = [zone(), zone()]; assert.throws(() => sanitizeFloorElements(dup), PlanLimitError);
    const many: any = structuredClone(blank().floor); many.zones = Array.from({ length: PLAN_LIMITS.zone.max + 1 }, (_, i) => zone({ id: `z-${i}` })); assert.throws(() => sanitizeFloorElements(many), PlanLimitError);
    const s = blank(); s.floor.zones = [zone()]; assert.deepEqual(checkSnapshot(structuredClone(s)).floor.zones, [zone()]);
  });
  test('ștergerea camerei șterge și zonele ei (lista goală dispare)', () => {
    const s = blank(); s.floor.zones = [zone()]; removeRoomZones(s.floor, 'camera-1'); assert.equal('zones' in s.floor, false);
    s.floor.zones = [zone(), zone({ id: 'z2', roomId: 'alta' })]; removeRoomZones(s.floor, 'camera-1'); assert.deepEqual(s.floor.zones.map(z => z.id), ['z2']);
  });
  test('Tab, încadrare, cititor de ecran, DXF', () => {
    const s = blank(); s.floor.zones = [zone({ label: 'Intrare' })];
    assert.deepEqual(planItems(s).slice(0, 2).map(i => i.kind), ['room', 'floor_zone']); assert.deepEqual(selectionBox(s, cat, { kind: 'floor_zone', id: 'zona-1' }), zone().rect);
    const items = describePlan(s, cat, { t, units: 'metric', lang: 'ro' })[0]!.items; assert.match(items.find(i => i.kind === 'floor_zone')!.label, /^Intrare · 3,00? m²/);
    assert.match(planToDxf(s, cat, { lang: 'ro' }), /2\nFLOOR_ZONES/);
  });
});

describe('bara de unelte și plafoanele', () => {
  const survey = ['select', 'wall', 'room', 'door', 'window', 'passage', 'shaft', 'niche', 'column', 'beam', 'glass_wall', 'demolish', 'stair', 'measure', 'dim', 'text'] as const;
  test('Demolare intră în grupa Structură: Releveul rămâne cu 7 locuri pe ecran lat', () => {
    assert.ok(TOOL_GROUPS.find(g => g.id === 'structure')!.tools.includes('demolish')); assert.equal(railSlots([...survey], true).length, 7);
  });
  test('Zonă pardoseală intră în grupa Decor: Finisaje are acum 3 locuri (era 4), grupele de doi rămân desfăcute', () => {
    assert.deepEqual(railSlots(['select', 'clad', 'wall_item', 'floor_zone', 'measure'], true).map(s => 'tool' in s ? s.tool : s.group), ['select', 'decor', 'measure']);
    assert.deepEqual(railSlots(['select', 'wall_item', 'measure'], true).map(s => 'tool' in s ? s.tool : s.group), ['select', 'wall_item', 'measure']);
  });
  test('plafoanele sunt în core/plan-limits.ts, comune cu serverul', () => {
    assert.deepEqual([PLAN_LIMITS.zone.max, PLAN_LIMITS.radiator.max, PLAN_LIMITS.hvac.max], [100, 100, 40]);
  });
});

describe('proiectele vechi se comportă identic', () => {
  const templates = ['blank', 'demo'];
  test('fără câmpurile noi: serverul nu adaugă nimic, devizul nu are linii noi, 3D fără liste noi, DXF fără straturi noi', () => {
    for (const tpl of templates){ const s = newSnapshot(cat, 'Vechi', tpl, 'ro', { materials: mc });
      const out = checkSnapshot(structuredClone(s));
      for (const f of [out.floor, ...(out.levels ?? [])]){ assert.equal('zones' in f, false); assert.ok(f.walls.every(w => !('status' in w))); }
      assert.deepEqual(out.floor.walls, s.floor.walls, 'pereții trec neschimbați');
      const boq = computeBOQ(s, cat, mc);
      assert.ok(!boq.unknown.some(u => /Demolare|Zidărie|perete nou|Calorifer|Unitate AC|zonă/i.test(u)), tpl);
      assert.ok(!boq.items.some(i => /wall-new|:zone:/.test(i.key)), tpl);
      assert.ok(boq.items.filter(i => i.key.endsWith(':floor')).every(i => !/fără zonele/.test(i.label)));
      const vi = viewerInput(s, cat, undefined, { mc }).plan as any; assert.equal('tehnice' in vi, false); assert.equal('zonePardoseala' in vi, false); assert.ok(vi.pereti.every((p: any) => !p.demolat && !p.nou));
      assert.ok(!/WALLS_NEW|WALLS_DEMOLISH|FLOOR_ZONES/.test(planToDxf(s, cat, { lang: 'ro' })));
      assert.ok(!validateFloor(s.floor).some(i => ['WALL_DEMOLISH', 'ZONE_OUTSIDE_ROOM', 'ZONE_OVERLAP'].includes(i.code)));
      assert.ok(!describePlan(s, cat, { t, units: 'metric', lang: 'ro' }).some(g => g.items.some(i => /De demolat|Nou \(de construit\)/.test(i.label)))); }
  });
  test('punctul vechi de calorifer (fără mărimi) rămâne simbol: fără 3D, fără deviz, fără avertismente', () => {
    const s = blank(); const old: TechPoint = { id: 'radiator:camera-1:1', kind: 'radiator', roomId: 'camera-1', x: 2.5, z: .02, height: .15, reason: 'manual' }; s.tech = [old];
    assert.equal('tehnice' in (viewerInput(s, cat).plan as any), false); assert.ok(!unknownOf(s).some(u => u.startsWith('Calorifer')));
    assert.deepEqual(checkSnapshot(structuredClone(s)).tech, [old]); assert.deepEqual(techIssues(s, cat, s.floor), []);
  });
});
