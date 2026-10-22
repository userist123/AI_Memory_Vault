import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
import { evaluateDesignOption, compareDesignOptions } from '../core/design-lab';

const cat = catalogJson as unknown as Catalog;
const mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Demo Project', 'demo');

test('evaluateDesignOption calculates real spatial metrics from snapshot', () => {
  const s = demo();
  const opt = evaluateDesignOption('Optiunea A - Baza', s, cat, mc);

  assert.equal(opt.title, 'Optiunea A - Baza');
  assert.ok(opt.metrics.storageMeters > 0);
  assert.ok(opt.metrics.freeSpacePct > 30 && opt.metrics.freeSpacePct < 95);
  assert.ok(opt.metrics.seatingCapacity >= 2);
  assert.equal(opt.metrics.itemCount, s.placements.length);
  assert.ok(opt.metrics.budgetKnown > 0);
  assert.ok(Array.isArray(opt.pros));
  assert.ok(Array.isArray(opt.cons));
});

test('compareDesignOptions compares baseline with alternative option', () => {
  const base = demo();
  const alt = structuredClone(base);

  // Add an extra wardrobe to living room
  const living = alt.floor.rooms.find(r => r.type === 'living')!;
  alt.placements.push({
    id: 'extra-wardrobe',
    roomId: living.id,
    variantId: 'dulap-2',
    group: 'dulap',
    x: living.rect.x0 + 0.5,
    z: living.rect.z0 + 0.5,
    rotation: 0,
    source: 'manual',
  });

  const comparison = compareDesignOptions(base, [{ id: 'opt-b', title: 'Optiunea B - Depozitare Maxima', snapshot: alt }], cat, mc);

  assert.equal(comparison.options.length, 2);
  const optB = comparison.options.find(o => o.id === 'opt-b')!;
  assert.ok(optB.metrics.storageMeters > comparison.base.metrics.storageMeters);
  assert.ok(optB.metrics.budgetDeltaKnown != null && optB.metrics.budgetDeltaKnown >= 0);
  assert.ok(optB.pros.some(p => p.ro.includes('depozitare') || p.ro.includes('ml')));
});
