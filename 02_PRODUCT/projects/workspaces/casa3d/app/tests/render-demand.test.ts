// Plan A1, rândul 5: randarea la cerere: ce ține bucla pornită, când se oprește și cum se reia fără salt.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { isNavKey, pressKey, releaseKey, stillMoving, settled, wakeDt, SETTLE_M, type Demand } from '../core/render-demand';

const rest: Demand = { keys: {}, joyF: 0, joyS: 0, doorsMoving: false, glide: false, camGap: 0, targetGap: 0 };

test('o scenă nemișcată nu cere niciun cadru', () => { assert.equal(stillMoving(rest), false); });
test('fiecare cauză de mișcare ține bucla pornită', () => {
  const causes: Partial<Demand>[] = [{ keys: { w: true } }, { keys: { arrowleft: true } }, { keys: { '+': true } }, { joyF: .4 }, { joyS: -1 }, { doorsMoving: true }, { glide: true }, { camGap: .5 }, { targetGap: .5 }];
  for (const s of causes)
    assert.equal(stillMoving({ ...rest, ...s }), true, JSON.stringify(s));
});
test('tastele eliberate sau fără rol în navigare nu țin bucla pornită', () => {
  assert.equal(stillMoving({ ...rest, keys: { w: false, arrowup: false } }), false);
  assert.equal(stillMoving({ ...rest, keys: { x: true, enter: true, shift: true } }), false);
});
test('tastele de navigare sunt cele citite de motor', () => {
  for (const k of ['w', 'a', 's', 'd', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright', '+', '=', '-', '_', '0']) assert.ok(isNavKey(k), k);
  for (const k of ['x', 'enter', 'escape', 'delete', 'tab']) assert.equal(isNavKey(k), false, k);
});
test('caracterul de la apăsare se eliberează oricare ar fi caracterul de la ridicare', () => {
  for (const [code, down, up] of [['Equal', '+', '='], ['Minus', '_', '-'], ['Digit0', '0', ')'], ['Numpad0', '0', 'insert'], ['Digit0', '=', '0'], ['BracketRight', '+', '*'], ['Digit0', '0', 'à']]){
    const m = { keys: {} as Record<string, boolean>, held: new Map<string, string>() }; pressKey(m, code, down); assert.equal(stillMoving({ ...rest, keys: m.keys }), true, down);
    releaseKey(m, code, up); assert.equal(stillMoving({ ...rest, keys: m.keys }), false, `${down} → ${up}`); assert.equal(m.held.size, 0); }
});
test('Shift apăsat cât tasta e ținută nu lasă în urmă caracterul vechi', () => {
  const m = { keys: {} as Record<string, boolean>, held: new Map<string, string>() }; pressKey(m, 'Digit0', '0'); pressKey(m, 'Digit0', ')'); assert.equal(m.keys['0'], false);
  releaseKey(m, 'Digit0', ')'); assert.equal(stillMoving({ ...rest, keys: m.keys }), false);
});
test('fără cod fizic se folosește caracterul', () => {
  const m = { keys: {} as Record<string, boolean>, held: new Map<string, string>() }; pressKey(m, '', 'w'); assert.equal(m.keys.w, true); releaseKey(m, '', 'w'); assert.equal(m.keys.w, false); assert.equal(m.held.size, 0);
});
test('camera se oprește sub 1 mm, inclusiv când abia a ajuns la prag', () => {
  assert.equal(settled(SETTLE_M - 1e-6, 0), true); assert.equal(settled(0, SETTLE_M - 1e-6), true);
  assert.equal(settled(SETTLE_M, 0), false); assert.equal(settled(0, SETTLE_M), false);
  assert.equal(stillMoving({ ...rest, camGap: SETTLE_M - 1e-6, targetGap: SETTLE_M - 1e-6 }), false);
});
test('o distanță necunoscută nu oprește bucla pe tăcute', () => { assert.equal(settled(Number.NaN, 0), false); assert.equal(settled(0, Number.POSITIVE_INFINITY), false); });
test('după repaus primul pas e de un cadru, nu cât a stat scena', () => {
  assert.equal(wakeDt(100000, 0, true), 1 / 60);
});
test('în mișcare pasul e timpul real, plafonat la 50 ms', () => {
  assert.equal(wakeDt(1020, 1000, false), .02); assert.equal(wakeDt(5000, 1000, false), .05); assert.equal(wakeDt(1000, 1000, false), 0);
});
