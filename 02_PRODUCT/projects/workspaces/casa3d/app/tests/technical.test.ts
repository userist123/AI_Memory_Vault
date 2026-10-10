import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { suggestTechPoints, techCounts, nearestWallPoint, MIN_OUTLETS } from '../core/technical';
import type { Catalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog, demo = (): Snapshot => newSnapshot(cat, 'T', 'demo');
const onEdge = (s: Snapshot, roomId: string, x: number, z: number) => { const r = s.floor.rooms.find(q => q.id === roomId)!.rect, e = 0.03;
  return x >= r.x0 - 1e-9 && x <= r.x1 + 1e-9 && z >= r.z0 - 1e-9 && z <= r.z1 + 1e-9 && (x - r.x0 < e || r.x1 - x < e || z - r.z0 < e || r.z1 - z < e); };

test('sugestiile sunt deterministe și au id-uri unice', () => {
  const a = suggestTechPoints(demo()), b = suggestTechPoints(demo());
  assert.deepEqual(a.map(p => [p.id, p.x, p.z]), b.map(p => [p.id, p.x, p.z]));
  assert.equal(new Set(a.map(p => p.id)).size, a.length);
});
test('fiecare cameră atinge minimul uzual de prize, iar punctele de perete stau pe pereții camerei', () => {
  const s = demo(), pts = suggestTechPoints(s);
  for (const r of s.floor.rooms){ const n = pts.filter(p => p.roomId === r.id && (p.kind === 'outlet' || p.kind === 'outlet_double')).length; assert.ok(n >= (MIN_OUTLETS[r.type] ?? 1), `${r.name}: ${n} prize`); }
  for (const p of pts.filter(p => p.kind !== 'light_point')) assert.ok(onEdge(s, p.roomId, p.x, p.z), `${p.id} la (${p.x}, ${p.z}) nu e pe perete`);
});
test('întrerupător la fiecare ușă, puncte de lumină câte corpuri are camera', () => {
  const s = demo(), pts = suggestTechPoints(s), c = techCounts(pts);
  for (const r of s.floor.rooms) assert.ok((c.byRoom[r.id]?.switch ?? 0) >= 1, `${r.name} are întrerupător`);
  assert.ok((c.byRoom.living?.light_point ?? 0) >= 2, 'livingul de 21 m² are cel puțin două puncte de lumină');
});
test('apă și scurgere la fiecare obiect sanitar și la bucătărie', () => {
  const s = demo(), pts = suggestTechPoints(s), c = techCounts(pts);
  const sanitary = s.placements.filter(p => ['lavoar', 'dus', 'wc'].includes(p.group));
  assert.ok((c.total.drain ?? 0) >= sanitary.length + (s.placements.some(p => p.group === 'bucatarie') ? 1 : 0));
  assert.ok((c.byRoom.baie?.water_cold ?? 0) >= 3 && (c.byRoom.baie?.water_hot ?? 0) >= 2);
  assert.ok((c.byRoom.bucatarie?.cooker ?? 0) === 1);
});
test('punctele nu cad în uși sau ferestre', () => {
  const s = demo(), pts = suggestTechPoints(s).filter(p => p.kind !== 'light_point');
  for (const w of s.floor.walls){ const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    for (const o of w.openings) for (const p of pts){ const t = (p.x - w.a[0]) * ux + (p.z - w.a[1]) * uz, perp = Math.abs((p.x - w.a[0]) * -uz + (p.z - w.a[1]) * ux);
      assert.ok(!(perp < w.thickness / 2 + 0.1 && t > o.offset + 0.01 && t < o.offset + o.width - 0.01), `${p.id} cade în golul ${o.id}`); } }
});
test('cel mai apropiat punct de perete', () => {
  const room = { id: 'r', name: 'R', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 3 } };
  const q = nearestWallPoint(room, 1, 0.4); assert.ok(Math.abs(q.z - 0.02) < 1e-9 && q.x === 1);
  const q2 = nearestWallPoint(room, 3.9, 1.5); assert.ok(Math.abs(q2.x - 3.98) < 1e-9);
});

import { placeTechPoint, sanitizeTech } from '../core/technical';
import { checkSnapshot } from '../lib/repo';
import { adviseProject } from '../core/advisor';

test('punctul adăugat manual se pune pe peretele cel mai apropiat al camerei; în afara camerelor nu se pune', () => {
  const s = demo(), r = s.floor.rooms.find(x => x.id === 'living')!.rect;
  const p = placeTechPoint(s, 'outlet', r.x0 + 0.3, (r.z0 + r.z1) / 2)!; assert.equal(p.roomId, 'living'); assert.ok(Math.abs(p.x - (r.x0 + 0.02)) < 1e-9);
  s.tech = [p]; const q = placeTechPoint(s, 'outlet', r.x0 + 0.3, (r.z0 + r.z1) / 2)!; assert.notEqual(q.id, p.id, 'id unic');
  assert.equal(placeTechPoint(s, 'outlet', -50, -50), null);
  const l = placeTechPoint(s, 'light_point', (r.x0 + r.x1) / 2, (r.z0 + r.z1) / 2)!; assert.equal(l.height, s.floor.ceilingHeight);
});
test('serverul păstrează doar puncte tehnice valide', () => {
  const s = demo(), good = suggestTechPoints(s).slice(0, 3);
  s.tech = [...good, { ...good[0]!, id: good[0]!.id }, { ...good[1]!, id: 'x1', kind: 'nuclear' as any }, { ...good[1]!, id: 'x2', roomId: 'nu-exista' }, { ...good[1]!, id: 'x3', height: 9 }, { ...good[1]!, id: 'x4', x: 999 }, { ...good[1]!, id: '<script>' }];
  assert.deepEqual(checkSnapshot(structuredClone(s)).tech!.map(p => p.id), good.map(p => p.id));
  assert.equal(sanitizeTech('nu', s), undefined);
});
test('consilierul: prize puține, lipsă întrerupător, sanitar fără scurgere — doar când există stratul tehnic', () => {
  const s = demo(), codes = (x: Snapshot) => adviseProject(x, cat).map(a => a.code);
  assert.ok(!codes(s).some(c => c.startsWith('TECH_')), 'fără strat tehnic, nicio observație de instalații');
  s.tech = suggestTechPoints(s); assert.ok(!codes(s).some(c => c.startsWith('TECH_')), 'sugestiile complete nu produc observații');
  s.tech = s.tech.filter(p => !(p.roomId === 'baie' && p.kind === 'drain') && !(p.roomId === 'living' && (p.kind === 'switch' || p.kind.startsWith('outlet'))));
  const a = adviseProject(s, cat);
  assert.ok(a.some(x => x.code === 'TECH_NO_DRAIN' && x.severity === 'WARNING'));
  assert.ok(a.some(x => x.code === 'TECH_NO_SWITCH' && x.refs.roomId === 'living'));
  assert.ok(a.some(x => x.code === 'TECH_FEW_OUTLETS' && x.refs.roomId === 'living' && x.vars.count === 0));
});
test('cel mult două întrerupătoare pe cameră, întâi la intrare', () => {
  const s = demo(), c = techCounts(suggestTechPoints(s));
  for (const r of s.floor.rooms) assert.ok((c.byRoom[r.id]?.switch ?? 0) <= 2, `${r.name}: ${c.byRoom[r.id]?.switch}`);
});
