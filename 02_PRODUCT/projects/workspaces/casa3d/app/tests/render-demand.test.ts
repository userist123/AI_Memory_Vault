// Plan A1, rândul 5: randarea la cerere: ce ține bucla pornită, când se oprește și cum se reia fără salt.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { isNavKey, stillMoving, settled, wakeDt, SETTLE_M, type Demand } from '../core/render-demand';

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
