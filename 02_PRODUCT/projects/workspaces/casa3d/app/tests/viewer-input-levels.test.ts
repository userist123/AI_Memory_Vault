import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { stairGeometry, SLAB } from '../core/levels';
import { viewerInput } from '../lib/viewer-input';
import type { Catalog, Snapshot, Stair } from '../core/types';

const cat = catalogJson as unknown as Catalog;
const base = (): Snapshot => newSnapshot(cat, 'Casa', 'two-bedroom', 'en');

test('viewerInput: scara devine scari = stairGeometry + id/x/z/w/l/rot', () => {
  const s = base(), st: Stair = { id: 's1', x: 2, z: 3, width: .9, length: 3, rotation: Math.PI / 2 };
  s.floor = { ...s.floor, stairs: [st] };
  const { plan } = viewerInput(s, cat);
  assert.deepEqual(plan.scari, [{ id: 's1', x: 2, z: 3, w: .9, l: 3, rot: Math.PI / 2, ...stairGeometry(st, s.floor.ceilingHeight + SLAB) }]);
  assert.deepEqual(plan.goluriPlaca, []);
});
test('viewerInput: golurile de placă trec neschimbate', () => {
  const voids = [{ x0: 1, z0: 1, x1: 2, z1: 4, dir: [0, -1] as [number, number] }];
  assert.deepEqual(viewerInput(base(), cat, undefined, { voids }).plan.goluriPlaca, voids);
});
test('viewerInput: fără scări și goluri, restul planului e neschimbat', () => {
  const s = base(), { plan, items } = viewerInput(s, cat);
  assert.deepEqual(plan.scari, []); assert.deepEqual(plan.goluriPlaca, []);
  const { scari: _a, goluriPlaca: _b, ...rest } = plan;
  const withEmpty = { ...s, floor: { ...s.floor, stairs: [] } };
  const o = viewerInput(withEmpty, cat); const { scari: _c, goluriPlaca: _d, ...rest2 } = o.plan;
  assert.deepEqual(rest2, rest); assert.deepEqual(o.items, items);
});
