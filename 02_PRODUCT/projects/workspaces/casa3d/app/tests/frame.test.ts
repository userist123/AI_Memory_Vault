// UI/UX P2-12: încadrarea obiectului selectat în plan (clic pe o problemă din „Sănătate”, tasta F).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { selectionBox, frameViewBox } from '../core/frame';
import type { Catalog, MaterialsCatalog } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const near = (a: number, b: number) => assert.ok(Math.abs(a - b) < 1e-9, `${a} ≈ ${b}`);

test('conturul: cameră, piesă, perete, gol de ușă (după perete); inexistent → null', () => {
  const s = newSnapshot(cat, 'Cadru', 'demo', 'ro', { materials: mc }), r = s.floor.rooms[0]!, w = s.floor.walls[0]!, p = s.placements[0]!;
  assert.deepEqual(selectionBox(s, cat, { kind: 'room', id: r.id }), r.rect);
  const pb = selectionBox(s, cat, { kind: 'placement', id: p.id })!; assert.ok(pb.x0 < p.x && p.x < pb.x1 && pb.z0 < p.z && p.z < pb.z1, 'piesa e în contur');
  const wb = selectionBox(s, cat, { kind: 'wall', id: w.id })!; near(wb.x0, Math.min(w.a[0], w.b[0]) - w.thickness / 2);
  assert.deepEqual(selectionBox(s, cat, { kind: 'opening', id: 'x', wallId: w.id }), wb);
  for (const sel of [null, { kind: 'room', id: 'nu' }, { kind: 'placement', id: 'nu' }, { kind: 'altceva', id: 'x' }]) assert.equal(selectionBox(s, cat, sel), null);
});
test('viewBox: obiectul în centru, cu margine, minim 3 m, la proporția ferestrei', () => {
  const v = frameViewBox({ x0: 2, z0: 1, x1: 6, z1: 3 }, 2);
  near(v.x + v.w / 2, 4); near(v.y + v.h / 2, 2); near(v.w / v.h, 2); assert.ok(v.w >= 8 && v.h >= 4, 'margine de jumătate din latura mare');
  const s = frameViewBox({ x0: 1, z0: 1, x1: 1.2, z1: 1.2 }, 1); near(s.w, 3); near(s.h, 3);
  const t = frameViewBox({ x0: 0, z0: 0, x1: 2, z1: 10 }, 1.5); near(t.w / t.h, 1.5); assert.ok(t.h >= 20, 'cameră înaltă: înălțimea decide');
  near(frameViewBox({ x0: 0, z0: 0, x1: 1, z1: 1 }, NaN).w, 3);
});
