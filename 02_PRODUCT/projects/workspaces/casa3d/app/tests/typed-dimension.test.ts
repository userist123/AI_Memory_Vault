// UI/UX P1-6: cote tastate în timpul desenării (modelul SketchUp „desenezi aproximativ, tastezi exact”).
import { test } from 'vitest';
import assert from 'node:assert/strict';
// capătul segmentului tastat vine din core/snap.ts pointAtLength (testat în snap.test.ts)
import { parseTypedLength, parseTypedSize, typedRect, acceptsTypedKey } from '../core/typed-dimension';
const near = (a: number | null | undefined, b: number) => assert.ok(a != null && Math.abs(a - b) < 1e-9, `${a} ≈ ${b}`);

test('lungime: întreg = cm, zecimală = m, sufixele au prioritate; virgula e zecimală', () => {
  near(parseTypedLength('320', 'metric'), 3.2); near(parseTypedLength('3,20', 'metric'), 3.2); near(parseTypedLength('3.2', 'metric'), 3.2);
  near(parseTypedLength('4 m', 'metric'), 4); near(parseTypedLength('450cm', 'metric'), 4.5); near(parseTypedLength('2500 mm', 'metric'), 2.5); near(parseTypedLength(' 2,5M ', 'metric'), 2.5);
  near(parseTypedLength('120', 'imperial'), 120 * .0254); near(parseTypedLength("10'", 'imperial'), 3.048); near(parseTypedLength('10 ft', 'imperial'), 3.048); near(parseTypedLength('12"', 'imperial'), .3048); assert.equal(parseTypedLength('6"', 'imperial'), null, 'sub 20 cm');
  for (const bad of ['', 'abc', '0', '-3', '3,2,1', '5 kg', '19', '100001']) assert.equal(parseTypedLength(bad, 'metric'), null, bad);
});
test('dimensiunile camerei: „lățime;adâncime”, „x”, „×” sau spațiu', () => {
  for (const s of ['4,5;3,2', '450x320', '450 × 320', '4,5 3,2', '450*320', '4.5m;320']) { const r = parseTypedSize(s, 'metric'); near(r?.w, 4.5); near(r?.d, 3.2); }
  assert.equal(parseTypedSize('450', 'metric'), null); assert.equal(parseTypedSize('450;abc', 'metric'), null); assert.equal(parseTypedSize('1;2;3', 'metric'), null);
  assert.equal(parseTypedSize('50;300', 'metric'), null, 'sub 0,6 m, ca la camera trasă'); near(parseTypedSize('60;300', 'metric')?.w, .6);
});
test('camera: din colțul fixat, spre cadranul în care e mouse-ul', () => {
  assert.deepEqual(typedRect([2, 2], [5, 6], { w: 4, d: 3 }), { x0: 2, z0: 2, x1: 6, z1: 5 });
  assert.deepEqual(typedRect([2, 2], [0, 0], { w: 4, d: 3 }), { x0: -2, z0: -1, x1: 2, z1: 2 });
});
test('tastele acceptate în chip: cifre, separatori, unități; nu scurtăturile editorului', () => {
  for (const k of ['0', '9']) assert.equal(acceptsTypedKey(k, ''), true, k);
  for (const k of [',', '.', ';', 'x', ' ', 'm', 'c', "'", '"']) assert.equal(acceptsTypedKey(k, '3'), true, k);
  assert.equal(acceptsTypedKey('r', ''), false, 'R rotește'); assert.equal(acceptsTypedKey('m', ''), false, 'o unitate nu poate începe textul');
  assert.equal(acceptsTypedKey('Enter', '3'), false); assert.equal(acceptsTypedKey('x', ''), false, 'separatorul nu poate începe textul');
});
