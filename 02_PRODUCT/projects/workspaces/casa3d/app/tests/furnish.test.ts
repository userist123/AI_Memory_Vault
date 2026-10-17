// Amenajarea șabloanelor: a doua trecere peste motorul din prototip (core/furnish.ts) și decorul proiectelor noi (core/decor.ts).
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materialsV1 from '../data/materials.v1.json';
import { newSnapshot, autoLayout, defaultSelections } from '../core/project';
import { TEMPLATES, getTemplate } from '../core/templates';
import { footprintOf, validatePlacement } from '../core/validate';
import { resolve } from '../core/catalog';
import { rectHit, insideRect } from '../core/geometry';
import { blocksWindow, doorSwingZones, isSmallWc, isStudioLiving, windowOverlap, FURNISH_RULES } from '../core/furnish';
import { backSide } from '../core/fixtures';
import { DECOR, rugRect, rugUnderBed, rugUnderSofa } from '../core/decor';
import { rugSize, roomWindows, textileIssues } from '../core/textiles';
import { sanitizeFinishes, materialOf } from '../core/finishes';
import { checkSnapshot, projectErrors } from '../lib/repo';
import type { Catalog, Floor, FurniturePlacement, MaterialsCatalog, Room, Snapshot } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materialsV1 as unknown as MaterialsCatalog;
const project = (id: string) => newSnapshot(cat, 'Test', id, 'ro', { materials: mc });
const ALL = TEMPLATES.map(t => t.id);
const SNAPS = Object.fromEntries(ALL.map(id => [id, project(id)])) as Record<string, Snapshot>;
const inRoom = (s: Snapshot, r: Room) => s.placements.filter(p => p.roomId === r.id);
const groups = (s: Snapshot, r: Room) => inRoom(s, r).map(p => p.group).sort();
const fp = (p: FurniturePlacement) => footprintOf(cat, p)!;
const h = (p: FurniturePlacement) => resolve(cat, p.variantId)!.h;
const area = (r: Room) => (r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0);
const rooms = (s: Snapshot, type: string) => s.floor.rooms.filter(r => r.type === type);

describe('mobilierul șabloanelor', () => {
  test('bucătărie prezentă în fiecare șablon: front de bucătărie și frigider', () => {
    for (const id of ALL) for (const r of rooms(SNAPS[id]!, 'bucatarie')){ const g = groups(SNAPS[id]!, r);
      assert.ok(g.includes('bucatarie'), `${id}/${r.id}: lipsește frontul de bucătărie (${g})`); assert.ok(g.includes('frigider'), `${id}/${r.id}: lipsește frigiderul`); }
  });
  test('garsoniera are zona de dormit: pat în camera de zi, plus canapea și TV', () => {
    const s = SNAPS.studio!, liv = rooms(s, 'living')[0]!; assert.ok(isStudioLiving(s.floor, liv));
    const g = groups(s, liv); for (const x of ['pat', 'canapea', 'comodaTv', 'masuta']) assert.ok(g.includes(x), `studio: lipsește ${x} (${g})`);
    // un living obișnuit (există dormitor) nu primește pat
    for (const id of ALL.filter(x => x !== 'studio')) for (const r of rooms(SNAPS[id]!, 'living')) assert.ok(!groups(SNAPS[id]!, r).includes('pat'), id);
  });
  test('garsoniera mică: bucătăria primește o variantă mai scurtă, nu rămâne goală', () => {
    const k = SNAPS.studio!.placements.find(p => p.group === 'bucatarie')!; assert.notEqual(k.variantId, 'bucatarie-0');
    assert.ok(resolve(cat, k.variantId)!.w < resolve(cat, 'bucatarie-0')!.w);
  });
  test('fiecare dormitor are pat și dulap; biroul doar de la ~11 m²', () => {
    for (const id of ALL) for (const r of rooms(SNAPS[id]!, 'dormitor')){ const g = groups(SNAPS[id]!, r);
      assert.ok(g.includes('pat'), `${id}/${r.id}: fără pat`); assert.ok(g.includes('dulap'), `${id}/${r.id}: fără dulap (${g})`);
      if (area(r) < FURNISH_RULES.deskMinArea) assert.ok(!g.includes('birou'), `${id}/${r.id}: birou într-o cameră de ${area(r).toFixed(1)} m²`);
      else assert.ok(g.includes('birou'), `${id}/${r.id}: ${area(r).toFixed(1)} m² fără birou`); }
  });
  test('dormitoarele mici primesc o variantă mai mică de dulap (lanțul de rezervă)', () => {
    const s = SNAPS['two-bedroom']!; for (const r of rooms(s, 'dormitor')){ const d = inRoom(s, r).find(p => p.group === 'dulap')!; assert.notEqual(d.variantId, s.selections.dulap, r.id); }
  });
  test('nicio comodă TV și nicio piesă înaltă în fața unei ferestre', () => {
    for (const id of ALL){ const s = SNAPS[id]!; for (const p of s.placements){ const r = s.floor.rooms.find(x => x.id === p.roomId)!;
      assert.ok(!blocksWindow(s.floor, r, p.group, h(p), fp(p)), `${id}: ${p.variantId} în ${r.id} acoperă o fereastră`);
      assert.ok(!validatePlacement(s, cat, p).some(i => i.code === 'WINDOW_BLOCKED'), `${id}: ${p.variantId} WINDOW_BLOCKED`); } }
  });
  test('TV-ul nu stă în dreptul unei ferestre (pe peretele lui) și privește spre canapea', () => {
    for (const id of ALL){ const s = SNAPS[id]!; for (const r of rooms(s, 'living')){ const tv = inRoom(s, r).find(p => p.group === 'comodaTv'), sofa = inRoom(s, r).find(p => p.group === 'canapea');
      assert.ok(tv && sofa, `${id}: perechea canapea–TV lipsește`); const f = fp(tv!), side = backSide(tv!.rotation), horiz = side === 'N' || side === 'S';
      assert.ok(!windowOverlap(s.floor, r, side, horiz ? f.x0 : f.z0, horiz ? f.x1 : f.z1), `${id}: TV în dreptul ferestrei`);
      // pe pereți opuși
      const opp = { N: 'S', S: 'N', W: 'E', E: 'W' } as const; assert.equal(backSide(sofa!.rotation), opp[side], id); } }
  });
  test('patul nu intră niciodată în zona de deschidere a ușii', () => {
    for (const id of ALL){ const s = SNAPS[id]!; for (const p of s.placements.filter(p => p.group === 'pat')){ const r = s.floor.rooms.find(x => x.id === p.roomId)!;
      for (const z of doorSwingZones(s.floor, r)) assert.ok(!rectHit(z, fp(p)), `${id}: patul din ${r.id} e în bătaia ușii`); } }
  });
  test('WC-ul mic primește doar vas WC și lavoar, fără cabină de duș; baia mare își păstrează dușul', () => {
    const s = SNAPS['three-bedroom']!, wc = s.floor.rooms.find(r => r.id === 'wc')!, baie = s.floor.rooms.find(r => r.id === 'baie')!;
    assert.ok(isSmallWc(s.floor, wc)); assert.deepEqual(groups(s, wc), ['lavoar', 'wc']);
    assert.ok(!isSmallWc(s.floor, baie)); assert.ok(groups(s, baie).includes('dus'));
    // singura baie a casei nu pierde dușul, oricât de mică ar fi
    for (const id of ALL.filter(x => x !== 'three-bedroom')) for (const r of rooms(SNAPS[id]!, 'baie')){ assert.ok(!isSmallWc(SNAPS[id]!.floor, r)); assert.deepEqual(groups(SNAPS[id]!, r), ['dus', 'lavoar', 'wc'], id); }
  });
  test('holul: pantofar/consolă și oglindă, nimic înalt, nimic în fața intrării', () => {
    for (const id of ALL){ const s = SNAPS[id]!; for (const r of rooms(s, 'hol')){ const ps = inRoom(s, r), g = ps.map(p => p.group);
      assert.ok(g.includes('pantofar') && g.includes('oglinda'), `${id}: holul are ${g}`);
      for (const p of ps) assert.ok(h(p) <= FURNISH_RULES.hallMaxH + 1e-9, `${id}: ${p.variantId} de ${h(p)} m în hol`); } }
  });
  test('proiectele noi nu au erori și trec de verificarea serverului (checkSnapshot, sanitizeFinishes)', () => {
    for (const id of ALL){ const s = SNAPS[id]!; assert.deepEqual(projectErrors(s, cat), [], id); assert.equal(sanitizeFinishes(s.finishes), null, id);
      assert.doesNotThrow(() => checkSnapshot(structuredClone(s)), id);
      for (const p of s.placements) assert.ok(insideRect(s.floor.rooms.find(r => r.id === p.roomId)!.rect, fp(p)), `${id}: ${p.variantId}`); }
  });
  test('șabloanele mobilate nu au nici avertismente de circulație, ferestre sau uși (validatePlacement)', () => {
    for (const id of ALL){ const s = SNAPS[id]!; for (const p of s.placements) assert.deepEqual(validatePlacement(s, cat, p), [], `${id}: ${p.roomId} ${p.variantId}`); }
  });
  test('a doua trecere rulează și la „Amenajează automat” din editor (autoLayout), pe toată casa sau pe o cameră', () => {
    const s = SNAPS.studio!, again = autoLayout({ ...s, placements: [] }, cat).snapshot, liv = rooms(s, 'living')[0]!;
    assert.ok(groups(again, liv).includes('pat'));
    const one = autoLayout(s, cat, { roomId: liv.id }).snapshot; assert.ok(groups(one, liv).includes('pat'));
    assert.deepEqual(one.placements.filter(p => p.roomId !== liv.id).map(p => p.id), s.placements.filter(p => p.roomId !== liv.id).map(p => p.id));
  });
  test('o grupă aleasă explicit (picked) nu e înlocuită cu altă variantă', () => {
    const tpl = getTemplate('studio')!, snap: Snapshot = { name: 'P', floor: structuredClone(tpl.floor), placements: [], selections: defaultSelections(cat), picked: ['bucatarie'] };
    const out = autoLayout(snap, cat).snapshot; assert.ok(!out.placements.some(p => p.group === 'bucatarie' && p.variantId !== 'bucatarie-0'));
  });
  test('planul gol (o singură cameră de zi) nu primește pat', () => {
    const s = newSnapshot(cat, 'Gol', 'blank', 'ro', { materials: mc }); assert.ok(!s.placements.some(p => p.group === 'pat')); assert.equal(s.finishes, undefined);
  });
});

describe('decorul proiectelor noi', () => {
  const ids = (cats: string[]) => new Set(mc.materials.filter(m => cats.includes(m.category)).map(m => m.id));
  test('produsele decorului există în catalogul de materiale, în categoria potrivită', () => {
    const want: Record<string, string> = { curtain: 'curtain', sheer: 'sheer', tablePendant: 'pendant', bedSconce: 'wall_light', underLed: 'led_strip', ledMirror: 'led_mirror', warmPaint: 'paint', accentPaint: 'paint' };
    for (const [k, c] of Object.entries(want)) assert.equal(materialOf(mc, (DECOR as Record<string, string>)[k])?.category, c, k);
  });
  test('livingul are covor sub picioarele din față ale canapelei, draperii și perdele la ferestre', () => {
    const rugs = ids(['rug']);
    for (const id of ALL){ const s = SNAPS[id]!; for (const r of rooms(s, 'living')){ const f = s.finishes?.[r.id]!; assert.ok(f?.rug && rugs.has(f.rug.material), `${id}: livingul fără covor`);
      const sofa = inRoom(s, r).find(p => p.group === 'canapea')!, rug = rugRect(r, rugSize(r, materialOf(mc, f.rug!.material), f.rug!.rotate)!);
      assert.ok(rugUnderSofa({ fp: fp(sofa), rotation: sofa.rotation }, rug), `${id}: covorul nu ajunge sub canapea`);
      assert.deepEqual(f.windows!.map(w => w.openingId).sort(), roomWindows(s.floor, r).map(w => w.openingId).sort(), id);
      for (const w of f.windows!){ assert.equal(w.curtain, DECOR.curtain); assert.equal(w.sheer, DECOR.sheer); }
      assert.deepEqual(textileIssues(mc, s.floor, r, f).filter(i => i.key !== 'tex.curtainShort'), [], id); } }
  });
  test('pendul deasupra mesei, oriunde e masa', () => {
    let n = 0; for (const id of ALL){ const s = SNAPS[id]!; for (const p of s.placements.filter(p => p.group === 'masa')){ n++;
      assert.ok(s.finishes?.[p.roomId]?.fixtures?.some(x => x.kind === 'pendant' && x.anchor === 'table' && x.material === DECOR.tablePendant), `${id}: masa fără pendul`); } }
    assert.ok(n >= 3, `doar ${n} mese în șabloane`);
  });
  test('dormitoarele: covor sub cele două treimi de jos ale patului, draperii, aplice lângă pat, perete de accent în spatele patului', () => {
    for (const id of ALL){ const s = SNAPS[id]!; for (const r of rooms(s, 'dormitor')){ const f = s.finishes?.[r.id]!, bed = inRoom(s, r).find(p => p.group === 'pat')!;
      assert.ok(f?.rug, `${id}/${r.id}: fără covor`); const rug = rugRect(r, rugSize(r, materialOf(mc, f.rug!.material), f.rug!.rotate)!);
      assert.ok(rugUnderBed({ fp: fp(bed), rotation: bed.rotation }, rug), `${id}/${r.id}: covorul nu e sub partea de jos a patului ${JSON.stringify(rug)} ${JSON.stringify(fp(bed))}`);
      assert.ok(f.fixtures?.some(x => x.kind === 'sconce' && x.anchor === 'bed'), `${id}/${r.id}: fără aplice`);
      assert.ok(f.windows?.length && f.windows.every(w => w.curtain === DECOR.curtain), `${id}/${r.id}: fără draperii`);
      assert.ok(f.wallFeatures?.some(w => w.kind === 'paint' && w.material === DECOR.accentPaint), `${id}/${r.id}: fără perete de accent`); } }
  });
  test('bucătăria primește LED sub suspendate, baia oglinda cu LED', () => {
    for (const id of ALL){ const s = SNAPS[id]!;
      for (const r of rooms(s, 'bucatarie')) assert.equal(s.finishes?.[r.id]?.kitchen?.underLed, DECOR.underLed, id);
      for (const r of rooms(s, 'baie')) assert.equal(s.finishes?.[r.id]?.bath?.mirror, DECOR.ledMirror, id); }
  });
  test('toate id-urile de material din decor sunt reale; finisajele de bază rămân cele implicite', () => {
    const all = new Set(mc.materials.map(m => m.id));
    for (const id of ALL){ const s = SNAPS[id]!; for (const [rid, f] of Object.entries(s.finishes || {})){ const ref = [f.floor, f.wallPaint, f.light, f.rug?.material, f.kitchen?.underLed, f.bath?.mirror,
        ...(f.windows || []).flatMap(w => [w.curtain, w.sheer, w.blind]), ...(f.fixtures || []).map(x => x.material), ...(f.wallFeatures || []).map(w => w.material)].filter((x): x is string => !!x);
      for (const m of ref) assert.ok(all.has(m), `${id}/${rid}: ${m} nu există`); } }
  });
  test('fără catalogul de materiale (sau pe planul gol) nu se adaugă decor', () => {
    assert.equal(newSnapshot(cat, 'X', 'studio').finishes, undefined);
  });
});

// geometria folosită de reguli, pe un plan sintetic
describe('reguli pe plan sintetic', () => {
  const room = (id: string, type: string, x0: number, z0: number, x1: number, z1: number): Room => ({ id, name: id, type, rect: { x0, z0, x1, z1 } });
  test('WC mic: sub 1,6 m lățime sau sub 4 m², numai dacă mai există o baie', () => {
    const big = room('b', 'baie', 0, 0, 2.2, 3), wc = room('w', 'baie', 2.2, 0, 3.6, 3), fl: Floor = { id: 'f', name: 'E', ceilingHeight: 2.6, rooms: [big, wc], walls: [] };
    assert.ok(isSmallWc(fl, wc)); assert.ok(!isSmallWc(fl, big)); assert.ok(!isSmallWc({ ...fl, rooms: [wc] }, wc));
  });
});

describe('amenajare automată scalată pentru camere mari', () => {
  test('livingul mare (31.9 m²) primește fotoliu și comodă pe lângă piesele de bază', () => {
    const floor: Floor = {
      id: 'f-large', name: 'Parter', ceilingHeight: 2.6,
      rooms: [
        { id: 'liv', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 8.4, z1: 3.8 } },
        { id: 'k', name: 'Bucătărie', type: 'bucatarie', rect: { x0: 8.4, z0: 0, x1: 12.0, z1: 3.8 } }
      ],
      walls: [
        { id: 'w1', a: [0, 0], b: [8.4, 0], thickness: 0.25, exterior: true, openings: [{ id: 'wnd', kind: 'window', offset: 3, width: 2 }] },
        { id: 'w2', a: [8.4, 0], b: [8.4, 3.8], thickness: 0.15, exterior: false, openings: [{ id: 'dr', kind: 'door', offset: 1.5, width: 0.9 }] },
        { id: 'w3', a: [8.4, 3.8], b: [0, 3.8], thickness: 0.25, exterior: true, openings: [] },
        { id: 'w4', a: [0, 3.8], b: [0, 0], thickness: 0.25, exterior: true, openings: [] },
      ]
    };
    const snap: Snapshot = { name: 'Casa Mare', floor, placements: [], selections: defaultSelections(cat), picked: [] };
    const res = autoLayout(snap, cat).snapshot;
    const livPlacements = res.placements.filter(p => p.roomId === 'liv');
    const g = livPlacements.map(p => p.group);
    assert.ok(g.includes('canapea'), 'trebuie să aibă canapea');
    assert.ok(g.includes('comodaTv'), 'trebuie să aibă comodaTv');
    assert.ok(g.includes('fotoliu'), 'livingul de 31.9 m² trebuie să primească fotoliu');
    assert.ok(g.includes('comoda'), 'livingul de 31.9 m² trebuie să primească comodă');
    for (const p of livPlacements) {
      assert.deepEqual(validatePlacement(res, cat, p), [], `piesa ${p.group} (${p.variantId}) nu trebuie să aibă erori`);
    }
  });

  test('dormitorul mare (26.9 m²) primește comodă și fotoliu / dulap adițional', () => {
    const floor: Floor = {
      id: 'f-bed', name: 'Parter', ceilingHeight: 2.6,
      rooms: [
        { id: 'bed', name: 'Dormitor', type: 'dormitor', rect: { x0: 0, z0: 0, x1: 8.4, z1: 3.2 } },
        { id: 'h', name: 'Hol', type: 'hol', rect: { x0: 0, z0: 3.2, x1: 8.4, z1: 5.0 } }
      ],
      walls: [
        { id: 'wb1', a: [0, 0], b: [8.4, 0], thickness: 0.25, exterior: true, openings: [{ id: 'wnd1', kind: 'window', offset: 3, width: 1.5 }] },
        { id: 'wb2', a: [8.4, 0], b: [8.4, 3.2], thickness: 0.25, exterior: true, openings: [] },
        { id: 'wb3', a: [8.4, 3.2], b: [0, 3.2], thickness: 0.15, exterior: false, openings: [{ id: 'dr1', kind: 'door', offset: 7.0, width: 0.9 }] },
        { id: 'wb4', a: [0, 3.2], b: [0, 0], thickness: 0.25, exterior: true, openings: [] },
      ]
    };
    const snap: Snapshot = { name: 'Dormitor Mare', floor, placements: [], selections: defaultSelections(cat), picked: [] };
    const res = autoLayout(snap, cat).snapshot;
    const bedPlacements = res.placements.filter(p => p.roomId === 'bed');
    const g = bedPlacements.map(p => p.group);
    assert.ok(g.includes('pat'), 'trebuie să aibă pat');
    assert.ok(g.includes('dulap'), 'trebuie să aibă dulap');
    assert.ok(g.includes('comoda'), 'dormitorul mare trebuie să primească comodă');
    assert.ok(g.includes('fotoliu') || g.filter(x => x === 'dulap').length >= 2, 'dormitorul mare trebuie să primească fotoliu sau al doilea dulap');
    for (const p of bedPlacements) {
      assert.deepEqual(validatePlacement(res, cat, p), [], `piesa ${p.group} (${p.variantId}) nu trebuie să aibă erori`);
    }
  });

  test('baia mare (>7 m²) primește caloriferBaie și coloanaBaie', () => {
    const floor: Floor = {
      id: 'f-bath', name: 'Parter', ceilingHeight: 2.6,
      rooms: [
        { id: 'baie', name: 'Baie Mare', type: 'baie', rect: { x0: 0, z0: 0, x1: 3.0, z1: 2.8 } },
        { id: 'hol', name: 'Hol', type: 'hol', rect: { x0: 0, z0: 2.8, x1: 3.0, z1: 4.8 } }
      ],
      walls: [
        { id: 'wba1', a: [0, 0], b: [3.0, 0], thickness: 0.25, exterior: true, openings: [{ id: 'wndb', kind: 'window', offset: 1.0, width: 0.8 }] },
        { id: 'wba2', a: [3.0, 0], b: [3.0, 2.8], thickness: 0.25, exterior: true, openings: [] },
        { id: 'wba3', a: [3.0, 2.8], b: [0, 2.8], thickness: 0.15, exterior: false, openings: [{ id: 'drb', kind: 'door', offset: 0.3, width: 0.8 }] },
        { id: 'wba4', a: [0, 2.8], b: [0, 0], thickness: 0.25, exterior: true, openings: [] },
      ]
    };
    const snap: Snapshot = { name: 'Baie', floor, placements: [], selections: defaultSelections(cat), picked: [] };
    const res = autoLayout(snap, cat).snapshot;
    const bathPlacements = res.placements.filter(p => p.roomId === 'baie');
    const g = bathPlacements.map(p => p.group);
    assert.ok(g.includes('dus') || g.includes('cada'), 'trebuie să aibă duș sau cadă');
    assert.ok(g.includes('wc'), 'trebuie să aibă wc');
    assert.ok(g.includes('lavoar'), 'trebuie să aibă lavoar');
    assert.ok(g.includes('caloriferBaie'), 'baia mare primește calorifer de baie');
    for (const p of bathPlacements) {
      assert.deepEqual(validatePlacement(res, cat, p), [], `piesa ${p.group} (${p.variantId}) nu trebuie să aibă erori`);
    }
  });

  test('bucătăria mare (>=12 m²) primește comodă/bufet adițional pe lângă front, frigider și masă', () => {
    const floor: Floor = {
      id: 'f-kitch', name: 'Parter', ceilingHeight: 2.6,
      rooms: [
        { id: 'k', name: 'Bucătărie Mare', type: 'bucatarie', rect: { x0: 0, z0: 0, x1: 4.0, z1: 3.5 } },
        { id: 'liv', name: 'Living', type: 'living', rect: { x0: 4.0, z0: 0, x1: 8.0, z1: 3.5 } }
      ],
      walls: [
        { id: 'wk1', a: [0, 0], b: [4.0, 0], thickness: 0.25, exterior: true, openings: [{ id: 'wndk', kind: 'window', offset: 1.5, width: 1.2 }] },
        { id: 'wk2', a: [4.0, 0], b: [4.0, 3.5], thickness: 0.15, exterior: false, openings: [{ id: 'drk', kind: 'door', offset: 1.5, width: 0.9 }] },
        { id: 'wk3', a: [4.0, 3.5], b: [0, 3.5], thickness: 0.25, exterior: true, openings: [] },
        { id: 'wk4', a: [0, 3.5], b: [0, 0], thickness: 0.25, exterior: true, openings: [] },
      ]
    };
    const snap: Snapshot = { name: 'Bucătărie', floor, placements: [], selections: defaultSelections(cat), picked: [] };
    const res = autoLayout(snap, cat).snapshot;
    const kPlacements = res.placements.filter(p => p.roomId === 'k');
    const g = kPlacements.map(p => p.group);
    assert.ok(g.includes('bucatarie'), 'trebuie să aibă front bucătărie');
    assert.ok(g.includes('frigider'), 'trebuie să aibă frigider');
    assert.ok(g.includes('comoda'), 'bucătăria mare trebuie să primească comodă/bufet');
    for (const p of kPlacements) {
      assert.deepEqual(validatePlacement(res, cat, p), [], `piesa ${p.group} (${p.variantId}) nu trebuie să aibă erori`);
    }
  });

  test('holul mare (>=7.5 m²) primește comodă/consolă pe lângă pantofar și oglindă', () => {
    const floor: Floor = {
      id: 'f-hall', name: 'Parter', ceilingHeight: 2.6,
      rooms: [
        { id: 'h', name: 'Hol Mare', type: 'hol', rect: { x0: 0, z0: 0, x1: 3.0, z1: 3.0 } },
        { id: 'liv', name: 'Living', type: 'living', rect: { x0: 3.0, z0: 0, x1: 7.0, z1: 3.0 } }
      ],
      walls: [
        { id: 'wh1', a: [0, 0], b: [3.0, 0], thickness: 0.25, exterior: true, openings: [{ id: 'ent', kind: 'door', offset: 1.0, width: 1.0, entrance: true }] },
        { id: 'wh2', a: [3.0, 0], b: [3.0, 3.0], thickness: 0.15, exterior: false, openings: [{ id: 'drh', kind: 'door', offset: 1.0, width: 0.9 }] },
        { id: 'wh3', a: [3.0, 3.0], b: [0, 3.0], thickness: 0.25, exterior: true, openings: [] },
        { id: 'wh4', a: [0, 3.0], b: [0, 0], thickness: 0.25, exterior: true, openings: [] },
      ]
    };
    const snap: Snapshot = { name: 'Hol', floor, placements: [], selections: defaultSelections(cat), picked: [] };
    const res = autoLayout(snap, cat).snapshot;
    const hPlacements = res.placements.filter(p => p.roomId === 'h');
    const g = hPlacements.map(p => p.group);
    assert.ok(g.includes('pantofar'), 'trebuie să aibă pantofar');
    assert.ok(g.includes('oglinda'), 'trebuie să aibă oglindă');
    assert.ok(g.includes('comoda'), 'holul mare trebuie să primească comodă/consolă');
    for (const p of hPlacements) {
      assert.deepEqual(validatePlacement(res, cat, p), [], `piesa ${p.group} (${p.variantId}) nu trebuie să aibă erori`);
    }
  });
});


