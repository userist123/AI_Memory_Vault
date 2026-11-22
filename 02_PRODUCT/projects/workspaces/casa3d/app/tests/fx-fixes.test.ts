// Reparații după recenzia cursului valutar: exportul CSV păstrează moneda, „lei” înseamnă RON, comentariile XML din BNR se ignoră.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { customItemsCsv, parseCustomCsv, TEMPLATE_HEADER } from '../core/custom-csv';
import { checkCustomItem } from '../core/clip';
import { isForeign, normCurrency, parseBnrXml } from '../core/fx';

test('exportul produselor proprii păstrează moneda și se reimportă cu ea (fără să devină lei)', () => {
  assert.equal(TEMPLATE_HEADER.at(-1), 'moneda');
  const csv = customItemsCsv([{ name: 'Canapea', supplier: 'Atelier', url: null, image: null, sku: null, price: 1200, currency: 'EUR', qty: 1, unit: 'buc', roomId: null, costPrice: 900, leadDays: null, dimension: null } as any]);
  const back = parseCustomCsv(csv), item = back.rows[0]?.item;
  assert.ok(item, JSON.stringify(back).slice(0, 300)); assert.equal(item.currency, 'EUR');
});
test('„lei”, „LEI”, „leu” înseamnă RON: nu devin monedă străină fără curs', () => {
  for (const c of ['lei', 'LEI', 'Leu', ' ron ']) { assert.equal(normCurrency(c), 'RON'); assert.equal(isForeign(c), false, c); }
  assert.equal(isForeign('EUR'), true); assert.equal((checkCustomItem({ currency: 'lei' }, true) as any).currency, 'RON');
});
test('parserul BNR ignoră cursurile din comentariile XML', () => {
  const xml = '<DataSet><Body><Cube date="2026-11-20"><!-- <Rate currency="EUR">9.9999</Rate> --><Rate currency="EUR">4.9771</Rate></Cube></Body></DataSet>';
  assert.equal(parseBnrXml(xml).rates.EUR, 4.9771);
});
