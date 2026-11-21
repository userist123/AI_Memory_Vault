// Faza 6 din ghidul BIM (§4.2, §6): livrabile — DXF cu straturi pe sistem, citit înapoi de importul propriu (dus-întors).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { planToDxf } from '../core/dxf';
import { parseDxf } from '../core/dxf-import';
import type { Catalog, Snapshot } from '../core/types';

const cat: Catalog = { suppliers: [], products: [], variants: [], offers: [] };
const snap = (): Snapshot => ({ name: 'T', floor: { id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [{ id: 'r1', name: 'Baie', type: 'baie', rect: { x0: 0, z0: 0, x1: 3, z1: 2 } }],
  walls: [{ id: 'w1', a: [0, 0], b: [3, 0], thickness: .25, exterior: true, openings: [], structural: true }],
  mepRuns: [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[0.5, 1, 0.2], [0.5, 2.4, 0.2], [2.5, 2.4, 0.2]] }, { id: 'm2', system: 'drain', points: [[1, .1, 1], [2, .05, 1]] }],
  footings: [{ id: 'fd1', x: 1, z: 1, w: .8, d: .8, depth: .6 }] }, placements: [], selections: {}, picked: [] });

test('DXF: traseele pe straturi de sistem, fundațiile și pereții structurali; segmentele verticale nu apar în plan', () => {
  const p = parseDxf(planToDxf(snap(), cat, { lang: 'ro' }));
  for (const l of ['MEP_WATER_COLD', 'MEP_DRAIN', 'STRUCT_FOOTINGS', 'STRUCT_WALLS']) assert.ok(p.layers.includes(l), l);
  assert.equal(p.units, 'mm');
  const cold = p.segments.filter(s => s.layer === 'MEP_WATER_COLD');
  assert.equal(cold.length, 1, 'urcarea verticală (aceeași poziție în plan) nu e o linie în plan');
  assert.deepEqual(cold[0], { layer: 'MEP_WATER_COLD', a: [500, -200], b: [2500, -200] });
  assert.equal(p.segments.filter(s => s.layer === 'STRUCT_FOOTINGS').length, 4);
});
test('DXF: diametrul nedeclarat apare ca „D?”, nu ca o valoare', () => {
  const d = planToDxf(snap(), cat, { lang: 'ro' });
  assert.match(d, /m1 D20/); assert.match(d, /m2 D\?/);
});
test('DXF: un proiect fără câmpurile BIM nu primește straturi noi', () => {
  const s = snap(); delete s.floor.mepRuns; delete s.floor.footings; delete s.floor.walls[0]!.structural;
  const d = planToDxf(s, cat, { lang: 'ro' });
  assert.equal(/MEP_|STRUCT_/.test(d), false);
});

import { readFileSync } from 'node:fs';
test('vederea 3D pe disciplină: exporturile ies din modelul întreg; starea vederii e declarată înainte de prima folosire', () => {
  const src = readFileSync(new URL('../components/viewer3d-engine.js', import.meta.url), 'utf8');
  assert.match(src, /exportScene: fullView\(solid\(exportScene\)\), exportGLB: fullViewAsync\(solid\(exportGLB\)\)/);
  assert.match(src, /setElementView\(v\)\{/);
  const decl = src.indexOf('let elView ='), firstUse = src.indexOf('applyElementView();');
  assert.ok(decl > 0 && decl < firstUse, 'let elView trebuie să preceadă orice apel (altfel TDZ la reconstruirea casei)');
  assert.match(src, /const wn0 = walls\.children\.length;/, 'tocurile și foile primesc id-ul peretelui');
});
