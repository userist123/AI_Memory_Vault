import { test } from 'vitest';
import assert from 'node:assert/strict';
import { formatMoney, formatMoneyDelta, formatLength, formatArea, toMetres } from '../core/format';
const n = (s: string) => s.replace(/\s/g, ' ');
test('banii se afișează în moneda ofertei, iar prețul lipsă rămâne necunoscut', () => {
  assert.match(n(formatMoney(2299, 'RON', 'ro')), /2\.299 RON|2\.299 lei/);
  assert.match(formatMoney(2299, 'EUR', 'en'), /€2,299/);
  assert.match(formatMoney(1500, 'USD', 'en'), /US\$1,500|\$1,500/);
  assert.equal(formatMoney(null, 'EUR', 'en'), '—'); assert.equal(formatMoney(undefined, 'EUR', 'en', '?'), '?');
  assert.match(formatMoneyDelta(-200, 'EUR', 'en'), /^−€200$/); assert.match(formatMoneyDelta(50, 'EUR', 'en'), /^\+€50$/);
});
test('lungimi și suprafețe metrice sau imperiale', () => {
  assert.equal(formatLength(0.8), '80 cm'); assert.equal(formatLength(3.2, 'metric', 'ro'), '3,20 m'); assert.equal(formatLength(3.2, 'metric', 'en'), '3.20 m');
  assert.equal(formatLength(3.048, 'imperial'), '10′'); assert.equal(formatLength(3.2, 'imperial'), '10′ 6″'); assert.equal(formatLength(0.127, 'imperial'), '5″');
  assert.equal(formatArea(17.9, 'metric', 'ro'), '17,9 m²'); assert.equal(formatArea(10, 'imperial', 'en'), '107.6 ft²');
  assert.equal(toMetres(10, 'imperial'), 3.048); assert.equal(toMetres(3, 'metric'), 3);
});
import { cmToInput, inputToCm, lengthInputUnit, catalogCurrency } from '../core/format';
test('câmpurile în cm: metric rămân cm, imperial arată și acceptă inci', () => {
  assert.equal(cmToInput(260, 'metric'), 260); assert.equal(cmToInput(259.6, 'metric'), 260);
  assert.equal(cmToInput(254, 'imperial'), 100); assert.equal(cmToInput(260, 'imperial'), 102.4);
  assert.equal(inputToCm('100', 'imperial'), 254); assert.equal(inputToCm('100', 'metric'), 100);
  assert.equal(inputToCm('2,5', 'metric'), 2.5); assert.equal(inputToCm(10, 'imperial'), 25.4);
  assert.equal(inputToCm('', 'metric'), null); assert.equal(inputToCm('abc', 'imperial'), null);
  for (const cm of [60, 80, 120, 260]) assert.ok(Math.abs(inputToCm(cmToInput(cm, 'imperial'), 'imperial')! - cm) < 0.13);
  assert.equal(lengthInputUnit('metric'), 'cm'); assert.equal(lengthInputUnit('imperial'), 'in');
  assert.equal(catalogCurrency({ offers: [] }), 'RON'); assert.equal(catalogCurrency({ offers: [{ currency: 'EUR' }] }), 'EUR');
});
