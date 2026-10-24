// A1-3: lățimile panourilor, citirea/scrierea stării, încadrarea în fereastră, modul de aranjare și tastatura separatorului.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { DEFAULT_WS, LIMITS, clampWidth, fitToViewport, layoutDefault, parseWorkspace, resolveMode, serializeWorkspace, stepWidth } from '../lib/workspace-layout';

test('implicit: 260 / 380, outliner deschis', () => {
  assert.deepEqual(DEFAULT_WS, { v: 1, left: 260, right: 380, outliner: true });
  assert.deepEqual(parseWorkspace(null), DEFAULT_WS);
  assert.notEqual(parseWorkspace(null), DEFAULT_WS, 'copie proaspătă');
});
test('clampWidth: limite, rotunjire, valori nefinite', () => {
  assert.equal(clampWidth('left', 100), 200); assert.equal(clampWidth('left', 999), 420); assert.equal(clampWidth('left', 300), 300);
  assert.equal(clampWidth('right', 100), 320); assert.equal(clampWidth('right', 9999), 760); assert.equal(clampWidth('right', 500), 500);
  assert.equal(clampWidth('left', 300.6), 301); assert.equal(clampWidth('right', 400.4), 400);
  assert.equal(clampWidth('left', NaN), 260); assert.equal(clampWidth('right', Infinity), 380); assert.equal(clampWidth('right', -Infinity), 380);
});
test('parseWorkspace: JSON stricat, non-obiect, versiune greșită -> implicit', () => {
  for (const raw of ['{nope', '', '42', 'null', '"x"', '[1]', '{"v":2,"left":300}', '{"left":300}'])
    assert.deepEqual(parseWorkspace(raw), DEFAULT_WS, raw);
});
test('parseWorkspace: tipuri greșite pe câmpuri și valori în afara limitelor', () => {
  assert.deepEqual(parseWorkspace('{"v":1,"left":"300","right":null,"outliner":"yes"}'), DEFAULT_WS);
  assert.deepEqual(parseWorkspace('{"v":1,"left":5000,"right":1,"outliner":false}'), { v: 1, left: 420, right: 320, outliner: false });
  assert.deepEqual(parseWorkspace('{"v":1,"left":300.7}'), { v: 1, left: 301, right: 380, outliner: true });
});
test('serializeWorkspace: dus-întors', () => {
  const s = { v: 1 as const, left: 300, right: 500, outliner: false };
  assert.deepEqual(parseWorkspace(serializeWorkspace(s)), s);
  assert.deepEqual(parseWorkspace(serializeWorkspace(DEFAULT_WS)), DEFAULT_WS);
});
test('fitToViewport: încape neschimbat; strânge întâi dreapta, apoi stânga; fără outliner ignoră stânga; imposibil -> minime', () => {
  const fixed = LIMITS.rail + LIMITS.canvasMin; // 424
  assert.deepEqual(fitToViewport({ left: 260, right: 380 }, 1400, true), { left: 260, right: 380 });
  assert.deepEqual(fitToViewport({ left: 260, right: 380 }, fixed + 260 + 380, true), { left: 260, right: 380 }, 'exact');
  assert.deepEqual(fitToViewport({ left: 260, right: 500 }, fixed + 260 + 400, true), { left: 260, right: 400 });
  // dreapta ajunge la minim, restul se ia din stânga
  assert.deepEqual(fitToViewport({ left: 400, right: 500 }, fixed + 300 + 320, true), { left: 300, right: 320 });
  assert.deepEqual(fitToViewport({ left: 400, right: 500 }, 500, true), { left: 200, right: 320 });
  // outliner ascuns: stânga nu contează și nu se atinge
  assert.deepEqual(fitToViewport({ left: 400, right: 500 }, fixed + 450, false), { left: 400, right: 450 });
  assert.deepEqual(fitToViewport({ left: 400, right: 500 }, 300, false), { left: 400, right: 320 });
});
test('layoutDefault / resolveMode', () => {
  assert.equal(layoutDefault('classic'), 'classic'); assert.equal(layoutDefault('  Classic '), 'classic');
  assert.equal(layoutDefault(undefined), 'studio'); assert.equal(layoutDefault('garbage'), 'studio'); assert.equal(layoutDefault(''), 'studio');
  assert.equal(resolveMode('studio', 'classic'), 'studio'); assert.equal(resolveMode('classic', undefined), 'classic');
  assert.equal(resolveMode(null, 'classic'), 'classic'); assert.equal(resolveMode('garbage', 'classic'), 'classic');
  assert.equal(resolveMode('garbage', 'x'), 'studio'); assert.equal(resolveMode(null, undefined), 'studio');
});
test('stepWidth: panoul stâng (dreapta lărgește) și drept (stânga lărgește), Shift, Home/End, limite, alte taste', () => {
  assert.equal(stepWidth('left', 260, 'ArrowRight', false), 276); assert.equal(stepWidth('left', 260, 'ArrowLeft', false), 244);
  assert.equal(stepWidth('left', 260, 'ArrowRight', true), 324); assert.equal(stepWidth('left', 260, 'ArrowLeft', true), 200);
  assert.equal(stepWidth('right', 380, 'ArrowLeft', false), 396); assert.equal(stepWidth('right', 380, 'ArrowRight', false), 364);
  assert.equal(stepWidth('right', 380, 'ArrowLeft', true), 444); assert.equal(stepWidth('right', 380, 'ArrowRight', true), 320);
  assert.equal(stepWidth('left', 300, 'Home', false), 200); assert.equal(stepWidth('left', 300, 'End', false), 420);
  assert.equal(stepWidth('right', 500, 'Home', true), 320); assert.equal(stepWidth('right', 500, 'End', false), 760);
  assert.equal(stepWidth('left', 415, 'ArrowRight', false), 420); assert.equal(stepWidth('right', 325, 'ArrowRight', false), 320);
  assert.equal(stepWidth('left', 300, 'a', false), null); assert.equal(stepWidth('right', 300, 'ArrowUp', true), null);
});
