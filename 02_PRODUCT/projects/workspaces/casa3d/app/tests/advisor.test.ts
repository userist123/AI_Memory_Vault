import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { adviseProject, type Advice } from '../core/advisor';
import type { Catalog, FurniturePlacement, Opening, Room, Snapshot, Wall } from '../core/types';

// Catalog sintetic: id = `${group}-${n}`, dimensiuni în cm.
const D: Record<string, [number, number, number, string?]> = {
  'pat-0': [160, 200, 45, '#ccccbb'], 'pat-1': [140, 200, 45, '#ccccbb'], 'dulap-0': [100, 50, 200, '#e0e0e0'], 'canapea-0': [200, 90, 80, '#8a7a66'], 'canapea-1': [450, 90, 80, '#8a7a66'],
  'masuta-0': [100, 50, 40, '#e0e0e0'], 'comodaTv-0': [150, 40, 40, '#e0e0e0'], 'birou-0': [120, 60, 75, '#e0e0e0'], 'masa-0': [300, 300, 75, '#e0e0e0'], 'noptiera-0': [40, 40, 50, '#e0e0e0'],
  'dulap-big': [300, 100, 90, '#e0e0e0'],
};
function mkCat(extra: Record<string, [number, number, number, string?]> = {}): Catalog {
  const all = { ...D, ...extra }, ids = Object.keys(all);
  return { suppliers: [], offers: [],
    products: ids.map(id => ({ id: 'p-' + id, group: id, name: 'Produs ' + id, brand: 'T', category: id, model3d: 'none' })),
    variants: ids.map((id, i) => ({ id, productId: 'p-' + id, name: 'Var ' + id, legacyIndex: i, dimensionsCm: { w: all[id][0], d: all[id][1], h: all[id][2] }, dimensionsConfidence: 'HIGH' as const, style: all[id][3] ? { col: all[id][3] } : {} })) };
}
const win = (offset = 0.5, width = 2): Opening => ({ id: `w${offset}`, kind: 'window', offset, width });
// O cameră W×D la origine; pereții exteriori; `windows` = pe peretele de nord (z=0).
function mkSnap(type: string, W: number, Dp: number, opts: { windows?: Opening[]; wallWindows?: Partial<Record<'S' | 'W' | 'E', Opening[]>>; doors?: Opening[]; placements?: Partial<FurniturePlacement>[]; lights?: number } = {}): Snapshot {
  const room: Room = { id: 'r1', name: 'Camera', type, rect: { x0: 0, z0: 0, x1: W, z1: Dp } };
  const walls: Wall[] = [
    { id: 'wN', a: [0, 0], b: [W, 0], thickness: .2, exterior: true, openings: opts.windows ?? [] },
    { id: 'wE', a: [W, 0], b: [W, Dp], thickness: .2, exterior: true, openings: opts.wallWindows?.E ?? [] },
    { id: 'wS', a: [W, Dp], b: [0, Dp], thickness: .2, exterior: true, openings: [...(opts.wallWindows?.S ?? []), ...(opts.doors ?? [])] },
    { id: 'wW', a: [0, Dp], b: [0, 0], thickness: .2, exterior: true, openings: opts.wallWindows?.W ?? [] } ];
  const placements = (opts.placements ?? []).map((p, i) => ({ id: p.id ?? `p${i}`, roomId: 'r1', group: p.variantId!.slice(0, p.variantId!.lastIndexOf('-')), x: 0, z: 0, rotation: 0, source: 'manual' as const, ...p } as FurniturePlacement));
  const snap: Snapshot = { name: 't', floor: { id: 'f', name: 'f', ceilingHeight: 2.6, rooms: [room], walls }, placements, selections: {}, picked: [] };
  if (opts.lights != null) snap.finishes = { r1: { floor: 'x', wallPaint: 'y', light: 'z', lights: opts.lights } };
  return snap;
}
const has = (a: Advice[], rule: string) => a.filter(x => x.id.startsWith(rule + ':'));
const cat = mkCat();
const BIG_WIN = [win(0.5, 3)]; // fereastră mare ca să nu apară reguli de lumină
const run = (s: Snapshot, o = {}) => adviseProject(s, cat, o);

describe('validare → plain Romanian', () => {
  test('OUT_OF_ROOM și DOOR_ZONE sunt BLOCKER, cu fix concret', () => {
    const s = mkSnap('living', 5, 4, { windows: BIG_WIN, doors: [{ id: 'd', kind: 'door', offset: 1, width: .9 }], placements: [{ variantId: 'dulap-0', x: 3.5, z: 3.7 }, { id: 'out', variantId: 'masuta-0', x: 4.9, z: 2 }] });
    const a = run(s);
    assert.equal(has(a, 'OUT_OF_ROOM')[0].severity, 'BLOCKER'); assert.equal(has(a, 'DOOR_ZONE')[0].severity, 'BLOCKER');
    assert.equal(has(a, 'DOOR_ZONE')[0].title, 'Ușa nu se poate deschide complet'); assert.match(has(a, 'DOOR_ZONE')[0].fix, /^Mută piesa cu cel puțin \d+ cm/);
  });
  test('OVERLAP raportat o singură dată pentru pereche', () => {
    const a = run(mkSnap('living', 5, 4, { windows: BIG_WIN, placements: [{ id: 'a', variantId: 'masuta-0', x: 2, z: 2 }, { id: 'b', variantId: 'masuta-0', x: 2.3, z: 2 }] }));
    assert.equal(has(a, 'OVERLAP').length, 1); assert.equal(has(a, 'OVERLAP')[0].severity, 'BLOCKER');
  });
  test('WINDOW_BLOCKED este WARNING', () => {
    const a = run(mkSnap('living', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 1.5, z: 0.25 }] }));
    assert.equal(has(a, 'WINDOW_BLOCKED')[0].severity, 'WARNING');
  });
  test('negativ: plasare validă nu produce reguli de validare', () => {
    const a = run(mkSnap('living', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'masuta-0', x: 2.5, z: 2 }] }));
    for (const r of ['OUT_OF_ROOM', 'DOOR_ZONE', 'OVERLAP', 'WINDOW_BLOCKED', 'CLEARANCE']) assert.equal(has(a, r).length, 0, r);
  });
  test('perete scurt / gol în afara peretelui → BLOCKER cu refs.wallIds', () => {
    const s = mkSnap('living', 5, 4, { windows: BIG_WIN }); s.floor.walls.push({ id: 'tiny', a: [0, 0], b: [0.1, 0], thickness: .1, exterior: false, openings: [] }); s.floor.walls[1].openings.push({ id: 'big', kind: 'window', offset: 3, width: 3 });
    const a = run(s);
    assert.deepEqual(has(a, 'WALL_TOO_SHORT')[0].refs.wallIds, ['tiny']); assert.deepEqual(has(a, 'OPENING_OUTSIDE_WALL')[0].refs.wallIds, ['wE']);
  });
});

describe('circulație', () => {
  test('dulap cu < 90 cm liberi în față → WARNING (clearance din validare nu se dublează)', () => {
    // rotație π: fața spre nord, perete la 0,3 m
    const a = run(mkSnap('dormitor', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 2, z: 0.55, rotation: Math.PI }] }));
    assert.ok(a.some(x => x.severity === 'WARNING' && x.category === 'circulatie' && x.refs.placementIds?.[0] === 'p0'));
    assert.equal(a.filter(x => x.refs.placementIds?.[0] === 'p0' && x.category === 'circulatie').length, 1);
  });
  test('dulap cu loc destul → fără sfat de circulație; cu accesibilitate (1,2 m) apare', () => {
    const s = mkSnap('dormitor', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 2, z: 0.25 }] }); // fața spre sud, 3,5 m liberi
    assert.equal(run(s).filter(x => x.category === 'circulatie').length, 0);
    const s4 = mkSnap('dormitor', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 2, z: 2.9 }] }); // 4-3.15 = 0,85 m liberi
    assert.ok(run(s4).some(x => x.category === 'circulatie' && x.severity === 'WARNING'));
    const s5 = mkSnap('dormitor', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 2, z: 2.3 }] }); // 1,45 m liber: ok normal, prea puțin pentru 1,2? nu — 1,45 > 1,2
    assert.equal(has(run(s5, { accessibility: true }), 'WARDROBE_FRONT').length, 0);
    const s6 = mkSnap('dormitor', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 2, z: 2.55 }] }); // 4-2.8 = 1,2 liber exact
    assert.equal(has(run(s6, { accessibility: true }), 'WARDROBE_FRONT').length, 0);
    const s7 = mkSnap('dormitor', 5, 4, { windows: BIG_WIN, placements: [{ variantId: 'dulap-0', x: 2, z: 2.65 }] }); // 4-2.9 = 1,1 liber
    assert.equal(has(run(s7, { accessibility: false }), 'WARDROBE_FRONT').length, 0);
    assert.equal(has(run(s7, { accessibility: true }), 'WARDROBE_FRONT').length, 1);
  });
  test('pat fără 60 cm liberi pe laturi → WARNING; cu loc pe o latură → nimic', () => {
    // pat 160 lat, într-o cameră de 1,9 m lățime: ambele laturi blocate
    const tight = run(mkSnap('dormitor', 1.8, 4, { windows: [win(0, 1.5)], placements: [{ variantId: 'pat-0', x: 0.9, z: 1.1 }] }));
    assert.equal(has(tight, 'BED_ACCESS')[0].severity, 'WARNING');
    const ok = run(mkSnap('dormitor', 3.5, 4, { windows: BIG_WIN, placements: [{ variantId: 'pat-0', x: 0.9, z: 1.1 }] }));
    assert.equal(has(ok, 'BED_ACCESS').length, 0);
  });
  test('sofa–măsuță: prea aproape / prea departe → TIP; în interval → nimic', () => {
    const mk = (z: number) => run(mkSnap('living', 5, 5, { windows: BIG_WIN, placements: [{ id: 's', variantId: 'canapea-0', x: 2.5, z: 3, rotation: Math.PI }, { id: 'm', variantId: 'masuta-0', x: 2.5, z }] }));
    // canapea z0 = 2.55; măsuța are d=0,5 → z1 = z+0.25
    assert.equal(has(mk(2.2), 'SOFA_TABLE')[0].severity, 'TIP');   // gap 0,1
    assert.equal(has(mk(1.5), 'SOFA_TABLE').length, 1);             // gap 0,8
    assert.equal(has(mk(1.85), 'SOFA_TABLE').length, 0);            // gap 0,45
  });
});

describe('proporții', () => {
  test('aglomerat >55% → WARNING; sub prag → nimic', () => {
    const crowd = run(mkSnap('birou', 3, 3, { windows: BIG_WIN, placements: [{ variantId: 'masa-0', x: 1.5, z: 1.5 }] })); // 9 m² / 9 m² dar masa e 3x3
    assert.equal(has(crowd, 'CROWDED')[0].severity, 'WARNING');
    assert.equal(has(run(mkSnap('birou', 6, 6, { windows: BIG_WIN, placements: [{ variantId: 'masuta-0', x: 3, z: 3 }] })), 'CROWDED').length, 0);
  });
  test('living gol <15% → TIP; living mobilat → nimic', () => {
    assert.equal(has(run(mkSnap('living', 5, 5, { windows: BIG_WIN })), 'EMPTY_LIVING')[0].severity, 'TIP');
    const full = run(mkSnap('living', 4, 4, { windows: BIG_WIN, placements: [{ variantId: 'canapea-0', x: 2, z: 3.3, rotation: Math.PI }, { variantId: 'masuta-0', x: 2, z: 2 }, { variantId: 'comodaTv-0', x: 2, z: 0.2 }] }));
    assert.equal(has(full, 'EMPTY_LIVING').length, 0);
  });
  test('pat 160+ în dormitor <9 m² → WARNING; pat 140 sau dormitor mare → nimic', () => {
    const sm = (v: string, W = 3, Dp = 2.9) => run(mkSnap('dormitor', W, Dp, { windows: BIG_WIN, placements: [{ variantId: v, x: 1.5, z: 1.5 }] }));
    assert.equal(has(sm('pat-0'), 'BIG_BED_SMALL_ROOM')[0].severity, 'WARNING');
    assert.equal(has(sm('pat-1'), 'BIG_BED_SMALL_ROOM').length, 0); assert.equal(has(sm('pat-0', 4, 4), 'BIG_BED_SMALL_ROOM').length, 0);
  });
  test('canapea > 2/3 din perete → TIP; canapea scurtă sau liberă → nimic', () => {
    const c = (v: string, z: number) => run(mkSnap('living', 6, 6, { windows: BIG_WIN, placements: [{ variantId: v, x: 3, z, rotation: Math.PI }] }));
    assert.equal(has(c('canapea-1', 5.5), 'SOFA_LONG')[0].severity, 'TIP'); // 450 > 4 m
    assert.equal(has(c('canapea-0', 5.5), 'SOFA_LONG').length, 0); assert.equal(has(c('canapea-1', 3), 'SOFA_LONG').length, 0);
  });
});

describe('lumină', () => {
  test('fără fereastră → WARNING (living/dormitor/bucătărie), baie nu', () => {
    for (const t of ['living', 'dormitor', 'bucatarie']) assert.equal(has(run(mkSnap(t, 4, 4)), 'NO_WINDOW')[0].severity, 'WARNING', t);
    assert.equal(has(run(mkSnap('baie', 4, 4)), 'NO_WINDOW').length, 0);
    assert.equal(has(run(mkSnap('living', 4, 4, { windows: BIG_WIN })), 'NO_WINDOW').length, 0);
  });
  test('fereastră sub 1/8 din podea → TIP', () => {
    assert.equal(has(run(mkSnap('living', 5, 5, { windows: [win(0.5, 1)] })), 'SMALL_WINDOW')[0].severity, 'TIP'); // 1,3/25
    assert.equal(has(run(mkSnap('living', 5, 5, { windows: [win(0.5, 3)] })), 'SMALL_WINDOW').length, 0);       // 3,9/25
  });
  test('prea puține lumini față de regula BOQ → TIP', () => {
    assert.equal(has(run(mkSnap('living', 5, 5, { windows: BIG_WIN, lights: 1 })), 'FEW_LIGHTS')[0].severity, 'TIP'); // 25 m² → 3
    assert.equal(has(run(mkSnap('living', 5, 5, { windows: BIG_WIN, lights: 3 })), 'FEW_LIGHTS').length, 0);
    assert.equal(has(run(mkSnap('living', 5, 5, { windows: BIG_WIN })), 'FEW_LIGHTS').length, 0);
  });
});

describe('ergonomie', () => {
  test('TV–canapea în afara 1,5–3,5 m → TIP', () => {
    const tv = (z: number) => run(mkSnap('living', 6, 6, { windows: BIG_WIN, placements: [{ id: 't', variantId: 'comodaTv-0', x: 3, z: 0.2 }, { id: 's', variantId: 'canapea-0', x: 3, z, rotation: Math.PI }] }));
    assert.equal(has(tv(1), 'TV_DISTANCE')[0].severity, 'TIP');  // 0,8 m
    assert.equal(has(tv(5.5), 'TV_DISTANCE').length, 1);          // 5,3 m
    assert.equal(has(tv(2.7), 'TV_DISTANCE').length, 0);          // 2,5 m
  });
  test('birou cu fereastra în spatele scaunului → TIP; cu fereastra în față → nimic', () => {
    // birou rotit π: fața (scaunul) spre nord; fereastra pe peretele de nord e în spatele celui care stă
    const d = (rot: number, z: number) => run(mkSnap('birou', 4, 4, { windows: BIG_WIN, placements: [{ variantId: 'birou-0', x: 1.5, z, rotation: rot }] }));
    assert.equal(has(d(Math.PI, 1), 'DESK_GLARE')[0].severity, 'TIP');
    assert.equal(has(d(0, 0.3), 'DESK_GLARE').length, 0);   // fața spre sud, fereastra în fața lui
    assert.equal(has(d(Math.PI, 3), 'DESK_GLARE').length, 0); // prea departe
  });
  test('tăblia patului pe perete cu fereastră → TIP; pe perete fără → nimic', () => {
    const b = (z: number, rot: number) => run(mkSnap('dormitor', 4, 4, { windows: BIG_WIN, placements: [{ variantId: 'pat-1', x: 1.5, z, rotation: rot }] }));
    assert.equal(has(b(1.1, 0), 'HEADBOARD_WINDOW')[0].severity, 'TIP'); // spatele la nord, lipit
    assert.equal(has(b(2.9, Math.PI), 'HEADBOARD_WINDOW').length, 0);     // spatele la sud (fără fereastră)
  });
});

describe('culori', () => {
  const colorsOf = (hexes: string[]) => () => hexes.map((hex, i) => ({ roomId: 'r1', kind: 'item' as const, refId: 'x' + i, hex }));
  const base = () => mkSnap('living', 5, 5, { windows: BIG_WIN });
  test('>4 nuanțe nenneutre → WARNING; 4 → nimic', () => {
    assert.equal(has(adviseProject(base(), cat, { colorsOf: colorsOf(['#cc2222', '#cc8822', '#22cc22', '#2288cc', '#8822cc']) }), 'TOO_MANY_HUES')[0].severity, 'WARNING');
    assert.equal(has(adviseProject(base(), cat, { colorsOf: colorsOf(['#cc2222', '#22cc22', '#2288cc', '#8822cc', '#ffffff', '#808080']) }), 'TOO_MANY_HUES').length, 0);
  });
  test('contrast piesă mare–podea < 1,3 → TIP; contrast bun → nimic', () => {
    const s = mkSnap('living', 5, 5, { windows: BIG_WIN, placements: [{ id: 'sofa', variantId: 'canapea-0', x: 2.5, z: 4, rotation: Math.PI }] });
    const mk = (item: string) => () => [{ roomId: 'r1', kind: 'floor' as const, refId: 'r1', hex: '#808080' }, { roomId: 'r1', kind: 'item' as const, refId: 'sofa', hex: item }];
    assert.equal(has(adviseProject(s, cat, { colorsOf: mk('#858585') }), 'LOW_CONTRAST')[0].severity, 'TIP');
    assert.equal(has(adviseProject(s, cat, { colorsOf: mk('#101010') }), 'LOW_CONTRAST').length, 0);
  });
  test('pereți închiși în cameră <10 m² → TIP; în cameră mare sau pereți deschiși → nimic', () => {
    const w = (hex: string) => () => [{ roomId: 'r1', kind: 'wall' as const, refId: 'r1', hex }];
    const small = mkSnap('living', 3, 3, { windows: BIG_WIN });
    assert.equal(has(adviseProject(small, cat, { colorsOf: w('#202830') }), 'DARK_WALLS')[0].severity, 'TIP');
    assert.equal(has(adviseProject(small, cat, { colorsOf: w('#f0f0f0') }), 'DARK_WALLS').length, 0);
    assert.equal(has(adviseProject(base(), cat, { colorsOf: w('#202830') }), 'DARK_WALLS').length, 0);
  });
  test('două culori vii complementare → TIP; analoage → nimic', () => {
    assert.equal(has(adviseProject(base(), cat, { colorsOf: colorsOf(['#ff0000', '#00ffff']) }), 'COLOR_CLASH')[0].severity, 'TIP');
    assert.match(has(adviseProject(base(), cat, { colorsOf: colorsOf(['#ff0000', '#00ffff']) }), 'COLOR_CLASH')[0].why, /contrast puternic/);
    assert.equal(has(adviseProject(base(), cat, { colorsOf: colorsOf(['#ff0000', '#ff8000']) }), 'COLOR_CLASH').length, 0);
  });
  test('culorile implicite vin din variant.style.col', () => {
    const s = mkSnap('living', 5, 5, { windows: BIG_WIN, placements: [{ id: 'sofa', variantId: 'canapea-0', x: 2.5, z: 4, rotation: Math.PI }] });
    assert.equal(has(adviseProject(s, cat), 'LOW_CONTRAST').length, 0);
  });
});

describe('buget', () => {
  const s = mkSnap('living', 5, 5, { windows: BIG_WIN });
  test('total peste țintă → WARNING cu suma depășirii; sub țintă → nimic', () => {
    const a = run(s, { budget: { total: 12000, target: 10000, unknown: 0 } });
    assert.equal(has(a, 'OVER_BUDGET')[0].severity, 'WARNING'); assert.match(has(a, 'OVER_BUDGET')[0].why, /2[\s. ]?000/);
    assert.equal(has(run(s, { budget: { total: 9000, target: 10000, unknown: 0 } }), 'OVER_BUDGET').length, 0);
    assert.equal(has(run(s, { budget: { total: 9000, target: null, unknown: 0 } }), 'OVER_BUDGET').length, 0);
  });
  test('prețuri necunoscute → TIP', () => {
    assert.equal(has(run(s, { budget: { total: 100, target: null, unknown: 2 } }), 'UNKNOWN_PRICES')[0].severity, 'TIP');
    assert.equal(has(run(s, { budget: { total: 100, target: null, unknown: 0 } }), 'UNKNOWN_PRICES').length, 0);
  });
});

describe('proprietăți generale', () => {
  const demo = newSnapshot(catalogV1 as unknown as Catalog, 'Demo', 'demo'), dcat = catalogV1 as unknown as Catalog;
  test('proiectul demo nu are BLOCKER', () => {
    const a = adviseProject(demo, dcat);
    assert.equal(a.filter(x => x.severity === 'BLOCKER').length, 0, JSON.stringify(a.filter(x => x.severity === 'BLOCKER')));
  });
  test('id-uri stabile între două rulări și unice; why/fix nevide', () => {
    const noisy = mkSnap('living', 3, 3, { placements: [{ id: 'a', variantId: 'masa-0', x: 1.5, z: 1.5 }, { id: 'b', variantId: 'canapea-1', x: 1.5, z: 1.5 }] });
    for (const [snap, c] of [[demo, dcat], [noisy, cat]] as const){
      const a1 = adviseProject(snap, c, { budget: { total: 5, target: 1, unknown: 1 } }), a2 = adviseProject(structuredClone(snap), c, { budget: { total: 5, target: 1, unknown: 1 } });
      assert.deepEqual(a1.map(x => x.id), a2.map(x => x.id)); assert.equal(new Set(a1.map(x => x.id)).size, a1.length);
      for (const x of a1){ assert.ok(x.why.trim().length > 0, x.id); assert.ok(x.fix.trim().length > 0, x.id); assert.ok(x.title.trim().length > 0, x.id); }
    }
  });
  test('sortare: BLOCKER, apoi WARNING, apoi TIP', () => {
    const noisy = mkSnap('living', 3, 3, { placements: [{ id: 'a', variantId: 'masa-0', x: 1.5, z: 1.5 }, { id: 'b', variantId: 'canapea-1', x: 1.5, z: 1.5 }] });
    const o = ['BLOCKER', 'WARNING', 'TIP'], a = run(noisy, { budget: { total: 5, target: 1, unknown: 1 } }).map(x => o.indexOf(x.severity));
    assert.deepEqual(a, [...a].sort((x, y) => x - y)); assert.ok(a.includes(0) && a.includes(1) && a.includes(2));
  });
});
