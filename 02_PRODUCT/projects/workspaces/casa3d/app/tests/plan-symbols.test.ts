// Simbolurile de mobilier din plan: fiecare grupă are un desen, la dimensiunile ei, cu fața orientată corect.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import { planSymbol, symbolAngle } from '../core/plan-symbols';

const groups = [...new Set((catalogV1 as any).products.map((p: any) => p.group))] as string[];
test('fiecare grupă din catalog are simbol, fără NaN, cu numere rotunjite la mm', () => {
  for (const g of groups){ const s = planSymbol(g, 1.6, .9); assert.ok(s.outline || s.detail, g); assert.ok(!/NaN|Infinity/.test(s.outline + s.detail), g);
    assert.ok(!/\d\.\d{4,}/.test(s.outline + s.detail), `${g}: ${s.detail.slice(0, 80)}`); }
});
test('masa are scaunele cerute (jumătate pe fiecare latură lungă), patul dublu două perne, canapeaua de 3 m mai multe perne', () => {
  const count = (d: string) => (d.match(/M/g) || []).length;
  assert.equal(count(planSymbol('masa', 1.6, .9, 6).detail), 6); assert.equal(count(planSymbol('masa', 1.2, .8, 4).detail), 4);
  assert.ok(count(planSymbol('pat', 1.6, 2).detail) > count(planSymbol('pat', .9, 2).detail));
  assert.ok(count(planSymbol('canapea', 3, .95).detail) > count(planSymbol('canapea', 1.8, .95).detail));
});
test('rotația: fața locală (+y) ajunge spre +z la 0, spre +x la 90°, spre -z la 180°', () => {
  const face = (rot: number) => { const a = symbolAngle(rot) * Math.PI / 180; return [Math.round(-Math.sin(a)), Math.round(Math.cos(a))]; };
  assert.deepEqual(face(0), [0, 1]); assert.deepEqual(face(Math.PI / 2), [1, 0]); assert.deepEqual(face(Math.PI), [0, -1]);
});
