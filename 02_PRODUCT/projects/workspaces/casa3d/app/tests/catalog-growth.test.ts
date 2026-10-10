// Catalogul extins (produse verificate pe 2026-10-10): date valide, linkuri spre magazinul declarat, semănare incrementală în baze existente.
import { test } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogJson from '../data/catalog.v1.json';
import evidence from '../data/catalog-evidence.2026-10-10.json';
import { groups, toEngineCatalog } from '../core/catalog';
import { RETAILER_HOSTS, isAllowedUrl } from '../core/outbound';
import { FRONT_CLEARANCE } from '../core/rules';
import { getDb, resetDbForTests, reseedForTests } from '../lib/db';
import type { Catalog } from '../core/types';

const cat = catalogJson as unknown as Catalog;

test('fiecare variantă are dimensiuni plauzibile, o ofertă cu preț și un link https spre magazinul furnizorului', () => {
  assert.ok(cat.variants.length >= 200, `${cat.variants.length} variante`);
  const ids = new Set<string>();
  for (const v of cat.variants){ assert.ok(!ids.has(v.id), v.id); ids.add(v.id);
    assert.ok(cat.products.some(p => p.id === v.productId), v.id);
    if (v.dimensionsCm) for (const k of ['w', 'd', 'h'] as const) assert.ok(v.dimensionsCm[k] > 0 && v.dimensionsCm[k] < 400, `${v.id}.${k}`); }
  const urls = new Set<string>();
  for (const o of cat.offers){ assert.ok(ids.has(o.variantId), o.id); assert.ok(o.price > 0 && Number.isFinite(o.price), o.id);
    const u = o.provenance.sourceUrl; if (!u) continue;
    // fiecare pagină de produs verificată acum aparține unei singure oferte (cele vechi, ca bucătăria ENHET, acoperă și electrocasnicele incluse)
    if (o.provenance.verifiedAt === '2026-10-10'){ assert.ok(!urls.has(u), `URL repetat: ${u}`); urls.add(u); }
    assert.ok(RETAILER_HOSTS[o.supplierId]!.includes(new URL(u).hostname), `${o.id}: ${u} nu e pe ${o.supplierId}`);
    assert.ok(isAllowedUrl(u, 'direct', null, o.supplierId), o.id); }
  // fiecare ofertă verificată pe 2026-10-10 are dovada citită pe pagina produsului
  const ev = new Set(evidence.items.map(x => x.url));
  for (const o of cat.offers) if (o.provenance.verifiedAt === '2026-10-10') assert.ok(ev.has(o.provenance.sourceUrl!), o.id);
});
test('grupele noi (fotoliu, comodă) au model 3D, etichetă și spațiu liber în față; magazinele noi au furnizor', () => {
  const g = groups(cat), eng = toEngineCatalog(cat);
  for (const k of ['fotoliu', 'comoda']){ assert.ok(g[k]!.variants.length >= 5, k); assert.ok(eng[k]!.model, k); assert.ok(FRONT_CLEARANCE[k]! > 0, k); }
  for (const s of ['jysk-ro', 'mobexpert']) assert.ok(cat.suppliers.some(x => x.id === s) && cat.offers.some(o => o.supplierId === s), s);
  for (const [k, grp] of Object.entries(g)) grp.variants.forEach((v, i) => assert.equal(v.legacyIndex, i, `${k}: indexuri consecutive`));
});
test('repornirea pe o bază existentă adaugă produsele lipsă și prețurile mai noi, fără dubluri', async () => {
  resetDbForTests(); const { q } = await getDb();
  const count = async (t: string) => (await q(`select count(*)::int as n from ${t}`)).rows[0].n as number;
  const before = { offers: await count('offers'), links: await count('offer_links'), hist: await count('price_history'), ret: await count('retailers') };
  assert.equal(before.offers, cat.offers.length); assert.equal(before.ret, cat.suppliers.length);
  // o bază veche: fără ofertele noi și cu un preț mai vechi
  await q("delete from offers where verified_at = '2026-10-10' and id <> 'offer-canapea-0'");
  await q("update offers set price = 2299, verified_at = '2026-09-30' where id = 'offer-canapea-0'");
  await reseedForTests(); await reseedForTests();
  assert.equal(await count('offers'), before.offers); assert.equal(await count('offer_links'), before.links); assert.equal(await count('price_history'), before.hist);
  const o = (await q("select price, verified_at from offers where id = 'offer-canapea-0'")).rows[0];
  assert.equal(Number(o.price), cat.offers.find(x => x.id === 'offer-canapea-0')!.price);
  // un preț pus manual după ultima verificare din catalog rămâne
  await q("update offers set price = 1500, verified_at = '2026-10-11' where id = 'offer-canapea-0'"); await reseedForTests();
  assert.equal(Number((await q("select price from offers where id = 'offer-canapea-0'")).rows[0].price), 1500);
});
