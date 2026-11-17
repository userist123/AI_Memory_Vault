// Decizia proprietarului (2026-11-17): fără acorduri cu magazinele, clientul vede la fiecare produs linkul paginii și prețul
// observat acolo, cu data citirii — nu o ofertă a magazinului. Sursa trece prin deviz → profit → proformă, nu și în CSV.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getProforma } from '../lib/commercial';
import { proformaCsv } from '../core/commercial';
beforeAll(() => resetDbForTests());

test('proforma: produsele din catalog poartă magazinul, pagina produsului și data prețului observat; CSV-ul rămâne generic', async () => {
  const A = 'owner-price-source', id = await repo.createProject(A, 'Sursa prețului', 'demo');
  const { proforma } = await getProforma(A, id, '2026-11-17'), lines = proforma.groups.flatMap(g => g.lines);
  const withSrc = lines.filter(l => l.source); assert.ok(withSrc.length >= 5, `linii cu sursă: ${withSrc.length}`);
  for (const l of withSrc){
    assert.match(l.source!.url, /^https:\/\//, l.code); assert.match(l.source!.seenAt, /^\d{4}-\d{2}-\d{2}$/, l.code); assert.ok(l.source!.store.length > 0, l.code);
    // nicio informație internă (cost, discount, adaos, profit) nu ajunge la client prin sursă
    assert.deepEqual(Object.keys(l.source!).sort(), ['seenAt', 'store', 'url']); }
  // onorariul și manopera nu au sursă de magazin
  assert.ok(!proforma.groups.filter(g => g.category === 'fee' || g.category === 'labor').some(g => g.lines.some(l => l.source)));
  // CSV-ul pentru programul de facturare: aceleași coloane, fără linkuri
  const csv = proformaCsv(proforma); assert.ok(!/https?:\/\//.test(csv), 'fără URL în CSV');
});
