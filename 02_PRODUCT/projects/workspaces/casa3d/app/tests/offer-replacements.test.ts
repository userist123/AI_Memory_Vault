// Înlocuitorii produselor IKEA scoase din vânzare (2026-11-18): fiecare legătură duce la o variantă din catalog, disponibilă,
// cu pagina de produs, prețul și dimensiunile din dovadă; motorul 3D și planul o pot desena; editorul o oferă ca înlocuitor.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { resolve, groupOf, groups, toEngineCatalog } from '../core/catalog';
import { isProductPage } from '../core/commercial';
import { OFFERS_GONE_LIST, OFFER_REPLACEMENTS } from '../lib/offers-gone';
import evidence from '../data/catalog-evidence.2026-11-18.json';
beforeAll(() => resetDbForTests());

test('fiecare produs scos din vânzare are înlocuitor (cu excepțiile notate) din aceeași grupă, disponibil', async () => {
  const cat = await repo.getCatalog();
  assert.ok(OFFERS_GONE_LIST.length >= 19);
  const without = OFFERS_GONE_LIST.filter(g => !g.replacement).map(g => g.id);
  assert.deepEqual(without, ['offer-masuta-3'], 'LISTERBY: niciun echivalent de 135–145 × 55–65 cm cu dimensiuni pe pagină');
  for (const g of OFFERS_GONE_LIST.filter(x => x.replacement)){
    const old = cat.offers.find(o => o.id === g.id)!, nv = resolve(cat, g.replacement!)!;
    assert.ok(nv, g.replacement!); assert.equal(groupOf(nv.variant.id), groupOf(old.variantId), g.id);
    assert.equal(old.availability, 'OUT_OF_STOCK', g.id); assert.notEqual(nv.offer!.availability, 'OUT_OF_STOCK', g.replacement!);
    assert.ok(isProductPage(nv.offer!.provenance.sourceUrl!), nv.offer!.provenance.sourceUrl!);
    assert.ok((g.replacementWhy ?? '').length > 10, g.id);
    assert.equal(OFFER_REPLACEMENTS.get(g.id)?.variantId, g.replacement);
  }
});

test('variantele noi: dovada are același URL, preț și dimensiuni; desenabile în 3D', async () => {
  const cat = await repo.getCatalog(), eng = toEngineCatalog(cat), G = groups(cat);
  const items = (evidence as { items: { url: string; price: number; dimensionsCm: { w: number; d: number; h: number }; replaces: string }[] }).items;
  assert.equal(items.length, 17); assert.equal(new Set(items.map(i => i.url)).size, items.length);
  for (const e of items){
    const o = cat.offers.find(x => x.provenance.sourceUrl === e.url)!; assert.ok(o, e.url);
    const v = cat.variants.find(x => x.id === o.variantId)!;
    assert.equal(o.price, e.price, e.url); assert.equal(o.provenance.verifiedAt, '2026-11-18'); assert.deepEqual(v.dimensionsCm, e.dimensionsCm, v.id);
    const g = groupOf(v.id); assert.ok(eng[g]?.model, g); assert.ok(G[g]!.variants.some(x => x.id === v.id), v.id);
    assert.equal(OFFERS_GONE_LIST.find(x => x.id === e.replaces)?.replacement, v.id, e.replaces);
  }
  // seturile cu 2 scaune (TONSTAD) își declară scaunele: planul nu desenează 4
  for (const id of ['masa-20', 'masa-21']) assert.equal(cat.variants.find(v => v.id === id)?.chairs, 2, id);
});
