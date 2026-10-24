// O singură formatare pentru numere, cantități și date în interfață (audit UI F-20, F-35).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { formatNumber, formatQty, normalizeUnit, qtyDecimals, formatDate } from '../core/format';
const n = (s: string) => s.replace(/\s/g, ' ');

test('unitățile se scriu corect: m2 → m², mp → m², m3 → m³, buc și ml rămân', () => {
  assert.equal(normalizeUnit('m2'), 'm²'); assert.equal(normalizeUnit('mp'), 'm²'); assert.equal(normalizeUnit('M2'), 'm²');
  assert.equal(normalizeUnit('m3'), 'm³'); assert.equal(normalizeUnit('ml'), 'ml'); assert.equal(normalizeUnit('buc'), 'buc'); assert.equal(normalizeUnit(undefined), '');
});
test('numere cu virgulă zecimală în română și punct în engleză', () => {
  assert.equal(n(formatNumber(1234.5, 'ro', { decimals: 2 })), '1.234,50'); assert.equal(formatNumber(1234.5, 'en', { decimals: 2 }), '1,234.50');
  assert.equal(formatNumber(9.98, 'ro'), '9,98'); assert.equal(formatNumber(3, 'ro'), '3'); assert.equal(formatNumber(NaN, 'ro'), '—');
});
test('cantitățile au aceleași zecimale peste tot: m² și ml cu 2, bucățile întregi', () => {
  assert.equal(formatQty(9.98, 'm2', 'ro'), '9,98 m²'); assert.equal(formatQty(23.4, 'm2', 'ro'), '23,40 m²'); assert.equal(formatQty(2.43, 'ml', 'ro'), '2,43 ml');
  assert.equal(formatQty(3, 'buc', 'ro'), '3 buc'); assert.equal(formatQty(1.5, 'buc', 'ro'), '1,50 buc'); assert.equal(formatQty(23.4, 'm2', 'en'), '23.40 m²');
  assert.equal(formatQty(null, 'm2'), '—'); assert.equal(qtyDecimals('m²', 4), 2); assert.equal(qtyDecimals('buc', 4), 0);
  assert.doesNotMatch(formatQty(23.4, 'm2', 'ro'), /m2|\./, 'nici „m2”, nici punct zecimal în română');
});
test('datele: „15 oct. 2026” în interfață, „15.10.2026” în documente', () => {
  assert.equal(formatDate('2026-10-15', 'ro'), '15 oct. 2026'); assert.equal(formatDate('2026-10-15', 'ro', 'doc'), '15.10.2026');
  assert.equal(formatDate('2026-10-15', 'ro', 'long'), '15 octombrie 2026'); assert.equal(formatDate('nu-e-data', 'ro'), '—'); assert.equal(formatDate(null), '—');
});
