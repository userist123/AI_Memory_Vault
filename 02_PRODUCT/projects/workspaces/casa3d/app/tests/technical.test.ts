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
