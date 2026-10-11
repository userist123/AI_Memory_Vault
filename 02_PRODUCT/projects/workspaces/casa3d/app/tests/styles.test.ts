// Pachetele de stil: aplicate pe toată casa, valide, cu produse existente, fără să atingă mobilierul.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { STYLE_PACKAGES, applyStyle, styleCosts, wallBehind } from '../core/styles';
import { sanitizeFinishes, materialOf, bandsOverlap } from '../core/finishes';
import { sanitizeDoors } from '../core/doors';
import { sanitizeAppearance } from '../core/appearance';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Stil', 'demo');

test('fiecare stil: valid, doar produse din catalog, mobilierul neatins', () => {
  for (const pkg of STYLE_PACKAGES){ const before = demo(), s = applyStyle(before, cat, mc, pkg);
    assert.equal(sanitizeFinishes(s.finishes), null, pkg.id); assert.equal(sanitizeDoors(s.doors, s).error, undefined, pkg.id);
    assert.deepEqual(sanitizeAppearance(s.appearance, s), s.appearance, pkg.id); assert.deepEqual(s.placements, before.placements, pkg.id);
    for (const [rid, f] of Object.entries(s.finishes!)){
      const ids = [f.floor, f.wallPaint, f.wallTile, f.baseboard, f.light, f.rug?.material, f.ceiling?.led, f.ceiling?.spot, f.ceiling?.cornice, f.kitchen?.underLed,
        ...(f.wallFeatures || []).map(w => w.material), ...(f.windows || []).flatMap(w => [w.curtain, w.sheer, w.blind])].filter((x): x is string => !!x);
      for (const id of ids) assert.ok(materialOf(mc, id), `${pkg.id}/${rid}: ${id}`);
      assert.equal(bandsOverlap(f.wallFeatures || [], 10), false, `${pkg.id}/${rid}`); }
    const b = computeBOQ(s, cat, mc); assert.ok(b.items.some(i => i.key.startsWith('door:')), `${pkg.id}: ușile`); }
});
test('accentul stă pe peretele din spatele canapelei și al patului; stilurile chiar diferă', () => {
  const s0 = demo(), liv = s0.floor.rooms.find(r => r.id === 'living')!, side = wallBehind(s0, cat, liv, 'canapea');
  const s = applyStyle(s0, cat, mc, STYLE_PACKAGES.find(p => p.id === 'industrial')!); assert.equal(finishesOf(s, liv).wallFeatures![0]!.side, side);
  assert.equal(finishesOf(s, liv).wallFeatures![0]!.kind, 'brick');
  const classic = applyStyle(s0, cat, mc, STYLE_PACKAGES.find(p => p.id === 'classic')!), cw = finishesOf(classic, liv).wallFeatures!;
  assert.deepEqual(cw.map(w => [w.kind, w.fromM ?? 0, w.heightM ?? null]), [['panel', 0, 1], ['paint', 1, null]], 'lambriu până la 1 m, vopsea deasupra');
  const costs = styleCosts(s0, cat, mc); assert.equal(new Set(costs.map(c => c.materials)).size, STYLE_PACKAGES.length); assert.ok(costs.every(c => c.materials > 0));
});
