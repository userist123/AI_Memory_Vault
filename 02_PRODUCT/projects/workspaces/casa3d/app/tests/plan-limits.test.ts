import { test } from 'vitest';
import assert from 'node:assert/strict';
import { checkSnapshot } from '../lib/repo';
import { newSnapshot } from '../core/project';
import catalogJson from '../data/catalog.v1.json';
import type { Catalog } from '../core/types';
const cat = catalogJson as unknown as Catalog;
import { PLAN_LIMITS, sanitizeFloorElements, cleanPlanText } from '../core/plan-limits';

const base = () => { const s: any = structuredClone(newSnapshot(cat, 'Limite', 'demo')); s.floor.walls = [{ id: 'w1', a: [0, 0], b: [4, 0], thickness: .15, exterior: false, openings: [] }]; return s; };
const rejects = (fn: () => unknown) => assert.throws(fn, (e: any) => e.status === 400);

test('server: stâlpii, grinzile și pereții de sticlă trec doar cu numere finite și limite rezonabile', () => {
  const s = base(); s.floor.columns = [{ id: 'col-1', x: 1, z: 1, w: .35, d: .35, shape: 'rect', evil: '<x>' }]; s.floor.beams = [{ id: 'bm-1', a: [0, 0], b: [2, 0], width: .25, depth: .35 }];
  s.floor.glassWalls = [{ id: 'gw-1', a: [0, 1], b: [2, 1], thickness: .08, mullions: 2, frameColor: '#23262B' }];
  const r: any = checkSnapshot(s); assert.ok(!('evil' in r.floor.columns[0])); assert.equal(r.floor.beams.length, 1); assert.equal(r.floor.glassWalls[0].frameColor, '#23262B');
  for (const bad of [{ x: NaN }, { w: Infinity }, { w: '1' }, { shape: 'hex' }, { role: 'x' }, { height: 50 }, { id: '<script>' }]){ const t = base(); t.floor.columns = [{ id: 'col-1', x: 1, z: 1, w: .35, d: .35, shape: 'rect', ...bad }]; rejects(() => checkSnapshot(t)); }
  const b = base(); b.floor.beams = [{ id: 'bm-1', a: [0, NaN], b: [2, 0], width: .25, depth: .35 }]; rejects(() => checkSnapshot(b));
  const g = base(); g.floor.glassWalls = [{ id: 'gw-1', a: [0, 1], b: [2, 1], thickness: NaN }]; rejects(() => checkSnapshot(g));
  const many = base(); many.floor.columns = Array.from({ length: PLAN_LIMITS.column.max + 1 }, (_, i) => ({ id: `c${i}`, x: 1, z: 1, w: .3, d: .3, shape: 'rect' })); rejects(() => checkSnapshot(many));
  const empty = base(); empty.floor.columns = []; assert.ok(!('columns' in (checkSnapshot(empty) as any).floor));
});
test('server: nișele au perete existent, dimensiuni plauzibile și cel mult 100 pe nivel', () => {
  const s = base(); s.floor.niches = [{ id: 'n1', wallId: 'w1', offset: 1, width: .6, height: .4, depth: .1, elevation: 1.1, side: -1 }, { id: 'n2', wallId: 'nu-exista', offset: 1, width: .6, height: .4, depth: .1, elevation: 1.1 }];
  const r: any = checkSnapshot(s); assert.deepEqual(r.floor.niches.map((n: any) => n.id), ['n1']); assert.equal(r.floor.niches[0].side, -1);
  for (const bad of [{ depth: .9 }, { width: .01 }, { side: 2 }, { finish: 'gold' }, { elevation: -1 }]){ const t = base(); t.floor.niches = [{ id: 'n1', wallId: 'w1', offset: 1, width: .6, height: .4, depth: .1, elevation: 1.1, ...bad }]; rejects(() => checkSnapshot(t)); }
});
test('server: adnotările (cote, texte) curățate, textul fără < > și caractere de control, cel mult 300', () => {
  const s = base(); s.floor.annotations = [{ id: 'd1', kind: 'dim', a: [0, 0], b: [3, 1], offset: .3, mode: 'aligned' }, { id: 't1', kind: 'text', at: [1, 1], text: '<script>alert(1)</script>\u0007 h = 2,45', size: 'l' }, { id: 't2', kind: 'text', at: [1, 1], text: '   ' }];
  const r: any = checkSnapshot(s); assert.equal(r.floor.annotations.length, 2); const t = r.floor.annotations[1]; assert.ok(!/[<>\u0000-\u001f]/.test(t.text)); assert.match(t.text, /h = 2,45/);
  for (const bad of [{ id: 'd', kind: 'dim', a: [0, 0], b: [0.001, 0], offset: .3 }, { id: 'd', kind: 'dim', a: [0, 0], b: [2, 0], offset: Infinity }, { id: 'x', kind: 'arrow', at: [0, 0] }, { id: 't', kind: 'text', at: [5000, 0], text: 'x' }]){
    const t2 = base(); t2.floor.annotations = [bad]; rejects(() => checkSnapshot(t2)); }
  const many = base(); many.floor.annotations = Array.from({ length: 301 }, (_, i) => ({ id: `t${i}`, kind: 'text', at: [0, 0], text: 'x' })); rejects(() => checkSnapshot(many));
  assert.equal(cleanPlanText('ab\u202Ecd\u2066e'), 'ab cd e');
  assert.equal(cleanPlanText('a'.repeat(500)).length, PLAN_LIMITS.annotation.textMax);
});
test('server: golul de trecere doar `true` și doar pe ușă; proiectul vechi fără câmpuri noi rămâne neschimbat', () => {
  const s = base(); s.floor.walls[0].openings = [{ id: 'o1', kind: 'door', offset: 1, width: .9, passage: true }]; assert.equal((checkSnapshot(s) as any).floor.walls[0].openings[0].passage, true);
  for (const bad of [{ kind: 'door', passage: 'x' }, { kind: 'window', passage: true }, { kind: 'door', passage: true, width: 9 }]){ const t = base(); t.floor.walls[0].openings = [{ id: 'o1', offset: 0, width: .9, ...bad }]; rejects(() => checkSnapshot(t)); }
  const old = structuredClone(newSnapshot(cat, 'Vechi', 'demo')), before = JSON.stringify(old.floor); assert.equal(JSON.stringify(checkSnapshot(structuredClone(old)).floor), before);
  const f: any = { id: 'f', name: 'x', ceilingHeight: 2.6, rooms: [], walls: [] }; sanitizeFloorElements(f); assert.deepEqual(Object.keys(f).sort(), ['ceilingHeight', 'id', 'name', 'rooms', 'walls']);
});

test('server: valorile vechi peste limită (salvate de interfața de dinainte) se aduc la interval, nu blochează salvarea', () => {
  const s = base(); s.floor.columns = [{ id: 'col-1', x: 1, z: 1, w: 4, d: 1e300, shape: 'rect' }]; s.floor.beams = [{ id: 'bm-1', a: [0, 0], b: [2, 0], width: 3, depth: .01 }];
  s.floor.glassWalls = [{ id: 'gw-1', a: [0, 1], b: [2, 1], thickness: 5, mullions: 25 }];
  const r: any = checkSnapshot(s), L = PLAN_LIMITS;
  assert.equal(r.floor.columns[0].w, L.column.maxM); assert.equal(r.floor.columns[0].d, L.column.maxM);
  assert.equal(r.floor.beams[0].width, L.beam.maxM); assert.equal(r.floor.beams[0].depth, L.beam.minM);
  assert.equal(r.floor.glassWalls[0].thickness, L.glassWall.thickMaxM); assert.equal(r.floor.glassWalls[0].mullions, L.glassWall.mullionsMax);
  const neg = base(); neg.floor.glassWalls = [{ id: 'gw-1', a: [0, 1], b: [2, 1], thickness: .08, mullions: -1 }]; rejects(() => checkSnapshot(neg));
});
