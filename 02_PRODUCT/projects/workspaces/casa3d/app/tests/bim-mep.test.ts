// Faza 4 din ghidul BIM (§3.4–3.5): trasee de instalații cu sistem, geometrie 3D și proprietăți declarate sau necunoscute;
// fundații și pereți structurali ca reprezentare geometrică, fără dimensionare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { proposeRun, runLength, sanitizeMepRun, sanitizeFooting, mepIssues, MEP_SYSTEMS } from '../core/mep';
import { sanitizeFloorElements } from '../core/plan-limits';
import { elementIndex } from '../core/elements';
import { diffSnapshots } from '../core/diff';
import type { Floor, Snapshot, Catalog } from '../core/types';

const cat: Catalog = { suppliers: [], products: [], variants: [], offers: [] };
const floor = (): Floor => ({ id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [{ id: 'r1', name: 'Baie', type: 'baie', rect: { x0: 0, z0: 0, x1: 3, z1: 2 } }],
  walls: [{ id: 'w1', a: [0, 0], b: [3, 0], thickness: .25, exterior: true, openings: [] }] });

test('proposeRun: traseu ortogonal între două puncte, la cota aleasă, fără puncte coliniare sau duble', () => {
  const pts = proposeRun({ x: 0.5, z: 0.2, height: 0.6 }, { x: 2.5, z: 1.8, height: 0.3 }, 2.4);
  assert.deepEqual(pts, [[0.5, 0.6, 0.2], [0.5, 2.4, 0.2], [2.5, 2.4, 0.2], [2.5, 2.4, 1.8], [2.5, 0.3, 1.8]]);
  assert.deepEqual(proposeRun({ x: 0, z: 0, height: 0.3 }, { x: 2, z: 0, height: 0.3 }, 0.3), [[0, 0.3, 0], [2, 0.3, 0]], 'la aceeași cotă și pe aceeași axă: o linie');
  assert.equal(runLength({ points: [[0, 0, 0], [0, 1, 0], [2, 1, 0]] }), 3);
});
test('sanitizeMepRun: păstrează câmpurile valide, lasă necunoscut ce nu e declarat, respinge geometria greșită', () => {
  const r = sanitizeMepRun({ id: 'm1', system: 'drain', points: [[0, .1, 0], [2, .05, 0]], diameterMm: 110, material: '  PP  ', slopePct: 2, fromId: 't1', extra: 1 }, 2.6);
  assert.deepEqual(r, { id: 'm1', system: 'drain', points: [[0, .1, 0], [2, .05, 0]], diameterMm: 110, material: 'PP', slopePct: 2, fromId: 't1' });
  const bare = sanitizeMepRun({ id: 'm2', system: 'water_cold', points: [[0, 1, 0], [1, 1, 0]] }, 2.6);
  assert.equal('diameterMm' in bare, false); assert.equal('material' in bare, false);
  assert.throws(() => sanitizeMepRun({ id: 'm', system: 'gaz', points: [[0, 0, 0], [1, 0, 0]] }, 2.6), /sistem/i);
  assert.throws(() => sanitizeMepRun({ id: 'm', system: 'drain', points: [[0, 0, 0]] }, 2.6), /puncte/i);
  assert.throws(() => sanitizeMepRun({ id: 'm', system: 'drain', points: [[0, 9, 0], [1, 0, 0]] }, 2.6), /cot/i);
  assert.throws(() => sanitizeMepRun({ id: 'm', system: 'drain', points: [[0, 0, 0], [1, 0, 0]], diameterMm: 5000 }, 2.6), /diametr/i);
});
test('sanitizeFooting: fundație cu dimensiuni în limite', () => {
  assert.deepEqual(sanitizeFooting({ id: 'fd1', x: 1, z: 1, w: .8, d: .8, depth: .6, kind: 'pad', x2: 1 }), { id: 'fd1', x: 1, z: 1, w: .8, d: .8, depth: .6, kind: 'pad' });
  assert.throws(() => sanitizeFooting({ id: 'fd', x: 0, z: 0, w: 0, d: 1, depth: .5 }), /fundați/i);
});
test('sanitizeFloorElements: traseele, fundațiile și peretele structural trec prin validarea nivelului', () => {
  const f: any = floor();
  f.mepRuns = [{ id: 'm1', system: 'water_hot', points: [[0, 1, 0], [1, 1, 0]] }];
  f.footings = [{ id: 'fd1', x: 1, z: 1, w: .8, d: .8, depth: .6 }];
  f.walls[0].structural = true;
  sanitizeFloorElements(f);
  assert.equal(f.mepRuns.length, 1); assert.equal(f.footings.length, 1); assert.equal(f.walls[0].structural, true);
  f.walls[0].structural = 'da'; assert.throws(() => sanitizeFloorElements(f), /structural/i);
  const g: any = floor(); g.mepRuns = [{ id: 'm1', system: 'drain', points: [[0, 0, 0], [1, 0, 0]] }, { id: 'm1', system: 'drain', points: [[0, 0, 0], [1, 0, 0]] }];
  assert.throws(() => sanitizeFloorElements(g), /același id/);
  const e: any = floor(); e.mepRuns = []; e.footings = []; e.walls[0].structural = false; sanitizeFloorElements(e);
  assert.equal('mepRuns' in e, false); assert.equal('footings' in e, false); assert.equal('structural' in e.walls[0], false);
});
test('mepIssues: diametru nedeclarat = necunoscut, canalizare fără pantă sau cu pantă inversă = avertisment', () => {
  const f = floor();
  f.mepRuns = [
    { id: 'a', system: 'water_cold', points: [[0, 1, 0], [1, 1, 0]] },
    { id: 'b', system: 'drain', points: [[0, .1, 0], [2, .1, 0]], diameterMm: 50 },
    { id: 'c', system: 'drain', points: [[0, .05, 0], [2, .09, 0]], diameterMm: 50, slopePct: 2 },
    { id: 'd', system: 'drain', points: [[0, .09, 0], [2, .05, 0]], diameterMm: 50, slopePct: 2 },
  ];
  const by = (id: string) => mepIssues(f).filter(i => i.refs.includes(id)).map(i => i.code).sort();
  assert.deepEqual(by('a'), ['MEP_DIAMETER_UNKNOWN']);
  assert.deepEqual(by('b'), ['MEP_DRAIN_SLOPE_UNDECLARED']);
  assert.deepEqual(by('c'), ['MEP_DRAIN_SLOPE_REVERSED'], 'punctele sunt în sensul curgerii: aici canalizarea urcă');
  assert.deepEqual(by('d'), []);
});
test('index și diff: traseele au disciplina sistemului, fundațiile și peretele structural sunt structură', () => {
  const f = floor(); f.mepRuns = [{ id: 'm1', system: 'hvac_supply', points: [[0, 2.4, 0], [1, 2.4, 0]] }, { id: 'm2', system: 'electric', points: [[0, 2.4, 0], [1, 2.4, 0]] }];
  f.footings = [{ id: 'fd1', x: 1, z: 1, w: .8, d: .8, depth: .6 }]; f.walls[0]!.structural = true;
  const s: Snapshot = { name: 'T', floor: f, placements: [], selections: {}, picked: [] };
  const by = new Map(elementIndex(s).map(e => [e.elementId, e]));
  assert.equal(by.get('m1')!.discipline, MEP_SYSTEMS.hvac_supply.discipline); assert.equal(by.get('m1')!.discipline, 'mechanical');
  assert.equal(by.get('m2')!.discipline, 'electrical'); assert.equal(by.get('m1')!.elementType, 'run');
  assert.equal(by.get('fd1')!.discipline, 'structure'); assert.equal(by.get('w1')!.discipline, 'structure'); assert.equal(by.get('w1')!.elementType, 'structural_wall');
  const t = structuredClone(s); t.floor.mepRuns![0]!.diameterMm = 160; t.floor.footings = [];
  const d = diffSnapshots(s, t, cat);
  assert.deepEqual(d.mep, { added: 0, removed: 0, changed: 1 }); assert.equal(d.structure.removed, 1); assert.equal(d.isEmpty, false);
});

// ---------- revizia independentă ----------
import { checkSnapshot } from '../lib/repo';
import { readFileSync } from 'node:fs';
test('revizie: id-urile traseelor și fundațiilor au format sigur și sunt unice pe toată casa', () => {
  assert.throws(() => sanitizeMepRun({ id: '<img src=x>', system: 'drain', points: [[0, 0, 0], [1, 0, 0]] }, 2.6), /id/);
  assert.throws(() => sanitizeFooting({ id: 'a b', x: 0, z: 0, w: 1, d: 1, depth: .5 }), /fundați/i);
  const s: Snapshot = { name: 'T', floor: floor(), placements: [], selections: {}, picked: [] };
  s.floor.mepRuns = [{ id: 'w1', system: 'drain', points: [[0, .1, 0], [1, .1, 0]] }];
  assert.throws(() => checkSnapshot(structuredClone(s)), /w1/);
  s.floor.mepRuns = [{ id: 'mep-1', system: 'drain', points: [[0, .1, 0], [1, .1, 0]] }];
  assert.equal(checkSnapshot(structuredClone(s)).floor.mepRuns!.length, 1);
});
test('revizie: fundația nu are dimensiuni precompletate în panou (art. 4)', () => {
  const src = readFileSync(new URL('../components/MepPanel.tsx', import.meta.url), 'utf8');
  assert.match(src, /useState\(\{ x: '', z: '', w: '', d: '', depth: '' \}\)/);
});

import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
test('descrierea planului (arbore, cititor de ecran): fundația la structură, traseele în grupul lor, peretele structural marcat', () => {
  const f = floor(); f.walls[0]!.structural = true; f.footings = [{ id: 'fd1', x: 1, z: 1, w: .8, d: .8, depth: .6 }];
  f.mepRuns = [{ id: 'm1', system: 'water_hot', diameterMm: 20, points: [[0, 1, 0], [2, 1, 0]] }, { id: 'm2', system: 'electric', points: [[0, 2, 0], [1, 2, 0]] }];
  for (const lang of ['ro', 'en'] as const) {
    const g = describePlan({ floor: f, placements: [] }, cat, { t: translator(lang), units: 'metric', lang });
    const struct = g.find(x => x.id === 'structure')!, svc = g.find(x => x.id === 'services')!;
    assert.match(struct.items.find(i => i.id === 'w1')!.label, /structural/);
    assert.ok(struct.items.some(i => i.kind === 'footing' && i.id === 'fd1'));
    assert.deepEqual(svc.items.map(i => i.kind), ['mep_run', 'mep_run']);
    assert.match(svc.items[0]!.label, lang === 'ro' ? /Apă caldă · 2,00 m · Ø20 mm/ : /Hot water · 2\.00 m · Ø20 mm/);
    assert.match(svc.items[1]!.label, lang === 'ro' ? /diametru nedeclarat/ : /diameter not declared/);
  }
});
