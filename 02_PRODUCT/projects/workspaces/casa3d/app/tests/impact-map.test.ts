import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
import { computeDesignImpact } from '../core/impact-map';

const cat = catalogJson as unknown as Catalog;
const mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Demo Project', 'demo');

test('computeDesignImpact detects no changes on identical snapshots', () => {
  const a = demo();
  const b = structuredClone(a);
  const impact = computeDesignImpact(a, b, cat, mc);

  assert.equal(impact.hasChanges, false);
  assert.equal(impact.geometry.roomsResized.length, 0);
  assert.equal(impact.furniture.added.length, 0);
  assert.equal(impact.furniture.removed.length, 0);
  assert.equal(impact.drawingsStale.length, 0);
  assert.equal(impact.riskLevel, 'LOW');
});

test('computeDesignImpact tracks room resize and downstream effects', () => {
  const a = demo();
  const b = structuredClone(a);

  // Resize bedroom
  const bedRoom = b.floor.rooms.find(r => r.type === 'dormitor')!;
  bedRoom.rect.z1 -= 0.8; // shrink by 80cm

  const impact = computeDesignImpact(a, b, cat, mc);

  assert.equal(impact.hasChanges, true);
  assert.equal(impact.geometry.roomsResized.length, 1);
  assert.equal(impact.geometry.roomsResized[0]!.id, bedRoom.id);
  assert.ok(impact.geometry.roomsResized[0]!.deltaArea < 0);
  assert.ok(impact.drawingsStale.includes('floor_plan'));
  assert.ok(impact.drawingsStale.includes('finishes'));
  assert.ok(['MEDIUM', 'HIGH'].includes(impact.riskLevel));
});
