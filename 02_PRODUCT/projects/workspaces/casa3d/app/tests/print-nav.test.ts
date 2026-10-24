// Page navigator of the print package: current page while scrolling, previous / next, page labels.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { activePage, stepPage, pageTitle } from '../lib/print-nav';

const order = ['pg-cover', 'pg-1', 'pg-2', 'pg-3'];
test('pagina curentă e prima vizibilă în ordinea documentului', () => {
  assert.equal(activePage(order, new Set(['pg-2', 'pg-1']), null), 'pg-1');
  assert.equal(activePage(order, new Set(['pg-3']), 'pg-1'), 'pg-3');
});
test('între două pagini înalte (nimic în bandă) rămâne ultima cunoscută; la început, coperta', () => {
  assert.equal(activePage(order, new Set(), 'pg-2'), 'pg-2');
  assert.equal(activePage(order, new Set(), null), 'pg-cover');
  assert.equal(activePage(order, new Set(), 'pg-dispărută'), 'pg-cover');
  assert.equal(activePage([], new Set(), null), null);
});
test('anterioara și următoarea se opresc la capete', () => {
  assert.equal(stepPage(order, 'pg-1', 1), 'pg-2'); assert.equal(stepPage(order, 'pg-1', -1), 'pg-cover');
  assert.equal(stepPage(order, 'pg-cover', -1), null); assert.equal(stepPage(order, 'pg-3', 1), null);
  assert.equal(stepPage(order, null, 1), 'pg-cover');
});
test('titlul paginii: data-title, apoi titlul secțiunii, niciodată gol', () => {
  assert.equal(pageTitle('Plan cotat', 'altceva', 'Pagina 1'), 'Plan cotat');
  assert.equal(pageTitle(undefined, '  Elevații   pereți · Hol ', 'Pagina 2'), 'Elevații pereți · Hol');
  assert.equal(pageTitle(undefined, '   ', 'Pagina 3'), 'Pagina 3');
});
