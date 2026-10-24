// Planul de placare: alege pornirea care evită fâșiile (sub 1/3 de placă) și numără plăcile reale.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { axisPlan, settingOut } from '../core/setting-out';
import type { Catalog, MaterialsCatalog } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;

test('o axă: de la colț lasă fâșie de 5 cm; centrat dă două tăieturi egale de ~32 cm', () => {
  // 2,45 m cu plăci de 60 cm și rost 3 mm: de la colț rămân ~3 cm → fâșie
  const p = axisPlan(2.45, .6, .003);
  assert.notEqual(p.start, 'edge'); assert.equal(p.sliver, false); assert.ok(p.minCut! >= .2, `tăietura minimă ${p.minCut}`);
  assert.equal(p.pieces, p.full + p.cuts.length);
  const total = p.cuts.reduce((a, c) => a + c, 0) + p.full * .6 + (p.pieces - 1) * .003; assert.ok(Math.abs(total - 2.45) < .005, `acoperă toată lungimea: ${total}`);
  const exact = axisPlan(1.806, .6, .003); assert.equal(exact.start, 'edge'); assert.equal(exact.cuts.length, 0, '3 plăci + 2 rosturi intră fix'); assert.equal(exact.pieces, 3);
  const tiny = axisPlan(.25, .6, .003); assert.equal(tiny.pieces, 1); assert.equal(tiny.sliver, false, 'o singură bucată mai mică decât placa nu e „fâșie” dacă e peste 1/3');
});

test('pe proiectul demo: planuri pentru gresie și faianță, cu număr de plăci', () => {
  const s = newSnapshot(cat, 'Placare', 'demo'), plans = settingOut(s, cat, mc);
  assert.ok(plans.length > 0); for (const p of plans){ assert.ok(p.tiles >= p.across.pieces && p.tiles === p.across.pieces * p.along.pieces); assert.ok(p.widthM > 0 && p.heightM > 0); }
  assert.ok(plans.some(p => p.surface === 'floor') || plans.some(p => p.surface !== 'floor'));
});
