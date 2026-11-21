// Faza 5 din ghidul BIM (§3.7, §8 „Clash Detection”): teste pozitive, negative, la limită, reproductibilitate și referințe.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { detectClashes, segBoxDistance, segSegDistance, boxBoxOverlap, solidsOf, CLASH_RULES } from '../core/clash';
import { runCoordination } from '../core/coordination-review';
import type { Catalog, Floor, Snapshot } from '../core/types';

const cat: Catalog = { suppliers: [], products: [{ id: 'p', group: 'dulap', name: 'Dulap', brand: 'b', category: 'c', model3d: '' }],
  variants: [{ id: 'dulap-0', productId: 'p', name: 'Înalt', legacyIndex: 0, dimensionsCm: { w: 100, d: 60, h: 240 }, dimensionsConfidence: 'HIGH', style: {} }], offers: [] };
const floor = (): Floor => ({ id: 'f0', name: 'P', ceilingHeight: 2.6,
  rooms: [{ id: 'r1', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 6, z1: 4 } }],
  walls: [{ id: 'w1', a: [0, 0], b: [6, 0], thickness: .2, exterior: true, openings: [{ id: 'o1', kind: 'door', offset: 2, width: 1, height: 2.1 }] }],
  columns: [{ id: 'c1', x: 3, z: 2, w: .4, d: .4, shape: 'rect' }],
  beams: [{ id: 'b1', a: [0, 3], b: [6, 3], width: .3, depth: .4 }] });
const snap = (f: Floor, placements: Snapshot['placements'] = []): Snapshot => ({ name: 'T', floor: f, placements, selections: {}, picked: [] });
const ids = (s: Snapshot) => detectClashes(s, cat).map(c => `${c.type}:${c.rule}:${[...c.refs].sort().join('+')}`).sort();

test('geometrie: distanța segment–cutie și segment–segment, intersecția a două cutii rotite doar în plan', () => {
  const box = { cx: 0, cy: 0, cz: 0, hx: 1, hy: 1, hz: 1, yaw: 0 };
  assert.ok(segBoxDistance([-5, 0, 0], [5, 0, 0], box) < 1e-9, 'trece prin cutie');
  assert.ok(Math.abs(segBoxDistance([-5, 3, 0], [5, 3, 0], box) - 2) < 1e-6, 'trece la 2 m deasupra');
  assert.ok(Math.abs(segSegDistance([0, 0, 0], [1, 0, 0], [0.5, 1, -1], [0.5, 1, 1]) - 1) < 1e-9);
  const rot = { cx: 1.6, cy: 0, cz: 0, hx: 1, hy: 1, hz: .1, yaw: Math.PI / 4 };
  assert.ok(boxBoxOverlap(box, rot) > 0, 'cutia rotită la 45° atinge cutia');
  assert.equal(boxBoxOverlap(box, { ...rot, cx: 3.5 }), 0);
});
test('pozitiv: traseul prin stâlp și prin grindă = interferență hard cu structura; traseul prin perete = străpungere (informare)', () => {
  const f = floor(); f.mepRuns = [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[3.1, 1, -1], [3.1, 1, 3.5]] }];
  // m1 (x=3,1, la 1 m) trece prin peretele de lângă ușă (ușa e la x 2–3) și prin stâlp; grinda e mai sus (y 2,2–2,6)
  const got = ids(snap(f));
  assert.ok(got.includes('hard:casa3d.clash.mep-structure:c1+m1'), got.join('\n'));
  assert.ok(got.includes('penetration:casa3d.clash.mep-wall:m1+w1'));
  assert.equal(got.some(x => x.includes('b1')), false, 'grinda e deasupra traseului');
  const up = floor(); up.mepRuns = [{ id: 'm2', system: 'hvac_supply', diameterMm: 160, points: [[1, 2.45, 1], [1, 2.45, 3.6]] }];
  assert.ok(ids(snap(up)).includes('hard:casa3d.clash.mep-structure:b1+m2'));
});
test('negativ: traseul prin golul ușii nu străpunge peretele; traseul la distanță peste toleranță nu e raportat', () => {
  const f = floor(); f.mepRuns = [{ id: 'm1', system: 'electric', diameterMm: 20, points: [[2.5, 1, -1], [2.5, 1, 1]] }];
  assert.deepEqual(ids(snap(f)), []);
  const g = floor(); g.mepRuns = [{ id: 'm2', system: 'water_cold', diameterMm: 20, points: [[3.22, 1, 1.5], [3.22, 1, 2.5]] }];
  assert.deepEqual(ids(snap(g)), [], 'la 1 cm de fața stâlpului (raza 1 cm, stâlpul până la x=3,2): fără atingere');
});
test('la limită: toleranța decide atingerea; clearance doar dacă proiectul definește distanța minimă', () => {
  const f = floor(); f.mepRuns = [{ id: 'a', system: 'water_hot', diameterMm: 20, points: [[1, 1, 1], [5, 1, 1]] }, { id: 'b', system: 'electric', diameterMm: 20, points: [[1, 1.05, 1.5], [1, 1.05, .5]] }];
  assert.deepEqual(ids(snap(f)).filter(x => x.includes('a+b')), [], 'distanța dintre axe 5 cm, raze 1+1 cm: liber');
  const c = detectClashes(snap(f), cat, { clearances: [{ a: 'water_hot', b: 'electric', minM: .1 }] }).filter(x => x.type === 'clearance');
  assert.equal(c.length, 1); assert.deepEqual([...c[0]!.refs].sort(), ['a', 'b']); assert.ok(Math.abs(c[0]!.distanceM! - .03) < 1e-6, `${c[0]!.distanceM}`);
  // toleranța = suprapunerea ignorată: două trasee care se pătrund 3 mm nu sunt raportate la 5 mm, sunt la 1 mm
  const g = floor(); g.mepRuns = [{ id: 'a', system: 'water_hot', diameterMm: 20, points: [[1, 1, 1], [5, 1, 1]] }, { id: 'b', system: 'electric', diameterMm: 20, points: [[1, 1.017, 1.5], [1, 1.017, .5]] }];
  const hard = (tol: number) => detectClashes(snap(g), cat, { toleranceM: tol }).filter(x => x.type === 'hard' && x.refs.includes('a') && x.refs.includes('b'));
  assert.equal(hard(.005).length, 0); assert.equal(hard(.001).length, 1); assert.ok(Math.abs(hard(.001)[0]!.depthM! - .003) < 1e-6);
});
test('trasee legate în același punct nu sunt interferență; încrucișarea a două trasee la aceeași cotă este', () => {
  const f = floor(); f.mepRuns = [
    { id: 'a', system: 'water_cold', diameterMm: 20, points: [[1, 1, 1], [2, 1, 1]] }, { id: 'b', system: 'water_cold', diameterMm: 20, points: [[2, 1, 1], [2, 1, 2]] },
    { id: 'c', system: 'drain', diameterMm: 50, points: [[4, .5, 1], [4, .5, 3]] }, { id: 'd', system: 'water_hot', diameterMm: 20, points: [[3.5, .5, 1.5], [4.5, .5, 1.5]] }];
  const got = ids(snap(f));
  assert.equal(got.some(x => x.includes('a+b')), false);
  assert.ok(got.includes('hard:casa3d.clash.mep-mep:c+d'));
});
test('mobilierul înalt sub grindă și traseul prin mobilier; mobilierul fără înălțime e exclus și raportat ca necunoscut', () => {
  const f = floor();
  const pl = (id: string, x: number, z: number, variantId = 'dulap-0') => ({ id, roomId: 'r1', group: 'dulap', variantId, x, z, rotation: 0, source: 'manual' as const });
  f.mepRuns = [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[5, 1, 1], [5, 1, 2]] }];
  const s = snap(f, [pl('d1', 1, 3), pl('d2', 5, 1.5), pl('d3', 1, 1.2, 'necunoscut-0')]);
  const got = ids(s);
  assert.ok(got.includes('hard:casa3d.clash.structure-furniture:b1+d1'), 'dulapul de 2,4 m intră în grinda care coboară la 2,2 m');
  assert.ok(got.includes('hard:casa3d.clash.mep-furniture:d2+m1'));
  const solids = solidsOf(s, cat);
  assert.deepEqual(solids.skipped, [{ id: 'd3', reason: 'unknown-size' }]);
});
test('reproductibil: aceleași intrări dau aceleași constatări, în aceeași ordine, cu id stabil și referințe către elemente', () => {
  const f = floor(); f.mepRuns = [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[3.1, 1, -1], [3.1, 1, 3.5]] }];
  const a = detectClashes(snap(f), cat), b = detectClashes(snap(structuredClone(f)), cat);
  assert.deepEqual(a, b);
  for (const c of a) { assert.match(c.id, /^cf-[0-9a-f]{8}$/); assert.equal(c.refs.length, 2); assert.equal(c.disciplines.length, 2); assert.equal(c.levelId, 'f0'); assert.equal(c.point.length, 3); }
});
test('coordonarea include regulile de interferență și spune explicit că clearance-ul nu a rulat fără distanțe definite', () => {
  const f = floor(); f.mepRuns = [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[3.1, 1, -1], [3.1, 1, 3.5]] }];
  const r = runCoordination(snap(f), cat, { revision: 3 });
  for (const rule of CLASH_RULES) assert.ok(r.rulesChecked.includes(rule), rule);
  assert.ok(r.rulesNotChecked.includes('casa3d.clash.clearance'));
  assert.ok(r.findings.some(x => x.code === 'CLASH_HARD' && x.refs.includes('c1')));
  assert.ok(r.findings.some(x => x.code === 'WALL_PENETRATION' && x.severity === 'INFO'));
  const withC = runCoordination(snap(f), cat, { revision: 3, clash: { clearances: [{ a: 'water_cold', b: 'electric', minM: .05 }] } });
  assert.ok(withC.rulesChecked.includes('casa3d.clash.clearance'));
});

// ---------- revizia independentă (fazele 2–5) ----------
import { planToDxf } from '../core/dxf';
import { parseDxf, wallCandidates } from '../core/dxf-import';
import { SLAB } from '../core/levels';
test('revizie: două trasee suprapuse care pornesc din același punct sunt raportate; îmbinarea simplă nu', () => {
  const f = floor(); f.mepRuns = [{ id: 'a', system: 'water_cold', diameterMm: 50, points: [[1, 1, 1], [4, 1, 1]] }, { id: 'b', system: 'drain', diameterMm: 110, points: [[1, 1, 1], [4, 1, 1]] }];
  assert.ok(ids(snap(f)).includes('hard:casa3d.clash.mep-mep:a+b'));
  const g = floor(); g.mepRuns = [{ id: 'a', system: 'water_cold', diameterMm: 50, points: [[1, 1, 1], [2, 1, 1]] }, { id: 'b', system: 'water_cold', diameterMm: 50, points: [[2, 1, 1], [2, 1, 2]] }];
  assert.equal(ids(snap(g)).some(x => x.includes('a+b')), false);
});
test('revizie: fundația e sub placă (−SLAB … −SLAB−adâncime), ca în vederea 3D', () => {
  const f = floor(); f.footings = [{ id: 'fd', x: 5, z: 1, w: 1, d: 1, depth: .4 }];
  f.mepRuns = [{ id: 'inSlab', system: 'drain', diameterMm: 20, points: [[4.5, -.08, 1], [5.5, -.08, 1]] }, { id: 'inFooting', system: 'drain', diameterMm: 20, points: [[4.5, -SLAB - .2, 1], [5.5, -SLAB - .2, 1]] }];
  const got = ids(snap(f));
  assert.equal(got.some(x => x.includes('inSlab')), false); assert.ok(got.includes('hard:casa3d.clash.mep-structure:fd+inFooting'));
});
test('revizie: DXF exportat și reimportat păstrează orientarea (fără oglindire)', () => {
  const s = snap({ id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [{ id: 'r1', name: 'X', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 2 } }],
    walls: [{ id: 'w1', a: [0, 0], b: [4, 0], thickness: .2, exterior: true, openings: [] }, { id: 'w2', a: [0, 0], b: [0, 2], thickness: .2, exterior: true, openings: [] }] });
  const c = wallCandidates(parseDxf(planToDxf(s, cat, { lang: 'ro' })), { layers: ['WALLS'] });
  const mid = (w: { a: [number, number]; b: [number, number] }) => [(w.a[0] + w.b[0]) / 2, (w.a[1] + w.b[1]) / 2];
  const along = c.find(w => Math.abs(w.a[1] - w.b[1]) < 1e-6 && Math.abs(Math.abs(w.a[0] - w.b[0]) - 4) < .3)!, side = c.find(w => Math.abs(w.a[0] - w.b[0]) < 1e-6 && Math.abs(Math.abs(w.a[1] - w.b[1]) - 2) < .3)!;
  assert.ok(along && side, JSON.stringify(c));
  // peretele lung e la z≈0, iar cel scurt se întinde spre z pozitiv (în jos pe ecran), ca în original
  assert.ok(Math.abs(mid(along)[1]!) < .2, `${mid(along)}`); assert.ok(mid(side)[1]! > .5, `${mid(side)}`);
});
