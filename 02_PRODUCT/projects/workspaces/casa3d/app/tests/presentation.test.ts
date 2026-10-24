// Prezentarea pentru client: o planșă pe cameră cu mostrele ei, moodboard-ul fără dubluri, bugetul egal cu cel din aplicație.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBudget } from '../core/boq';
import { presentation, roomSwatches } from '../core/presentation';
import { applyStyle, STYLE_PACKAGES } from '../core/styles';
import type { Catalog, MaterialsCatalog } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
test('o planșă pe cameră, mostrele în ordinea de citire, moodboard fără dubluri, bugetul aplicației', () => {
  const s = applyStyle(newSnapshot(cat, 'P', 'demo'), cat, mc, STYLE_PACKAGES.find(p => p.id === 'classic')!), p = presentation(s, cat, mc);
  assert.equal(p.rooms.length, s.floor.rooms.length); assert.equal(p.budget.total, computeBudget(s, cat, mc).chosen.total);
  const liv = p.rooms.find(r => r.id === 'living')!; assert.equal(liv.swatches[0]!.role, 'floor'); assert.ok(liv.swatches.some(w => w.role.startsWith('accent.')) && liv.swatches.some(w => w.role === 'curtain'));
  assert.ok(liv.products.length > 0);
  for (const g of p.board){ const keys = g.items.map(i => `${i.material?.id}|${i.color}`); assert.equal(new Set(keys).size, keys.length, g.role); }
  const bath = roomSwatches(s, mc, s.floor.rooms.find(r => r.id === 'baie')!); assert.ok(bath.some(w => w.role === 'bathTap') && bath.some(w => w.role === 'walltile'));
});
