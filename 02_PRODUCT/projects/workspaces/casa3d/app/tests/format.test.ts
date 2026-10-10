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
  assert.equal(formatLength(3.048, 'imperial'), '10′'); assert.equal(formatLength(3.2, 'imperial'), '10′ 6″');
  assert.equal(formatArea(17.9, 'metric', 'ro'), '17,9 m²'); assert.equal(formatArea(10, 'imperial', 'en'), '107.6 ft²');
  assert.equal(toMetres(10, 'imperial'), 3.048); assert.equal(toMetres(3, 'metric'), 3);
});
