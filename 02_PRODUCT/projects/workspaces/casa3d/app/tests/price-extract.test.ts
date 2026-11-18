// Scriptul de reverificare (scripts/recheck-prices.mjs): prețul se citește doar din date legate de produs, iar fișierul de
// reverificări primește numai citirile sigure. Fără rețea: pagini HTML minime.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import * as px from '../scripts/lib/price-extract.mjs';
// modulul e JavaScript: tipurile deduse sunt prea înguste pentru verificările de mai jos
const { priceFromHtml, isProductUrl, classify, staleItems, mergeRechecks } = px as any;
import { isProductPage } from '../core/commercial';
import materials from '../data/materials.v1.json';
import catalog from '../data/catalog.v1.json';
import rechecks from '../data/price-rechecks.json';

const ld = (o: unknown) => `<script type="application/ld+json">${JSON.stringify(o)}</script>`;

test('JSON-LD: Product.offers.price în RON; fără Product sau în altă monedă → null', () => {
  const p = priceFromHtml(`<html>${ld({ '@type': 'BreadcrumbList' })}${ld({ '@type': 'Product', name: 'BILLY', offers: { price: '399.00', priceCurrency: 'RON' } })}</html>`, 'https://www.ikea.com/ro/ro/p/billy-x-1/');
  assert.equal(p.price, 399); assert.equal(p.how, 'json-ld');
  assert.equal(priceFromHtml(ld({ '@type': 'Product', offers: { price: 10, priceCurrency: 'EUR' } }), 'https://x.ro/p/1/'), null);
  assert.equal(priceFromHtml('<p>Preț: 129,90 lei</p>', 'https://x.ro/p/1/'), null, 'textul paginii nu e sursă');
  // IKEA: listă de prețuri, cu prețul vechi tăiat la promoții → doar prețul curent
  const k = priceFromHtml(ld({ '@type': 'Product', offers: { '@type': 'Offer', priceSpecification: [{ price: '1699', priceCurrency: 'RON' }, { price: '2299', priceCurrency: 'RON', priceType: 'https://schema.org/StrikethroughPrice' }] } }), 'https://www.ikea.com/ro/ro/p/kivik-s49440597/');
  assert.equal(k.price, 1699); assert.match(k.evidence, /promoție/);
  assert.equal(priceFromHtml(ld({ '@type': 'Product', offers: { priceSpecification: [{ price: '1', priceCurrency: 'RON' }, { price: '2', priceCurrency: 'RON' }] } }), 'https://x.ro/p/1/'), null, 'două prețuri curente → ambiguu');
  assert.equal(priceFromHtml(`${ld({ '@graph': [{ '@type': 'Product', offers: [{ lowPrice: '1.299,50', priceCurrency: 'RON' }] }] })}`, 'https://x.ro/p/1/').price, 1299.5);
});

test('Dedeman: doar prețul final al codului din adresă, nu al produselor recomandate', () => {
  const box = (code: string, price: string) => `<div data-price-box="product-id-${code}"><span data-price-amount="${price}" data-price-type="finalPrice"></span><meta data-type="finalPrice" data-product-id="${code}"><span class="price-sale-unit"> / mp</span></div>`;
  const p = priceFromHtml(box('111', '9.99') + box('4029081', '38.9'), 'https://www.dedeman.ro/ro/parchet/p/4029081');
  assert.equal(p.price, 38.9); assert.equal(p.unit, 'mp');
  assert.equal(priceFromHtml(box('111', '9.99'), 'https://www.dedeman.ro/ro/parchet/p/4029081'), null, 'codul lipsește → nimic');
});

test('regula „pagină de produs” e aceeași ca în aplicație', () => {
  for (const u of ['https://www.ikea.com/ro/ro/p/billy-50263838/', 'https://www.ikea.com/ro/ro/cat/familia-billy-28102/', 'https://www.dedeman.ro/ro/mobilier-baie/c/65',
    'https://www.dedeman.ro/ro/x/p/4027179', 'https://jysk.ro/dormitor/saltele/saltea-x', 'http://www.ikea.com/ro/ro/p/x/', 'nu e url', 'https://www.emag.ro/x/pd/DABC/'])
    assert.equal(isProductUrl(u), isProductPage(u), u);
});

test('clasificare: pachet cu preț pe m² pe pagină → per=unit; altfel se compară cu pachetul', () => {
  assert.deepEqual(classify({ pack: 77.6, old: 38.9, unit: 'm2' }, { price: 38.9, unit: 'm²', how: 'dedeman-finalPrice', evidence: 'e' }).per, 'unit');
  const c = classify({ pack: 77.6, old: 38.9, unit: 'm2' }, { price: 80, unit: null, how: 'json-ld', evidence: 'e' });
  assert.equal(c.per, 'pack'); assert.equal(c.status, 'changed'); assert.equal(c.compared, 77.6);
  assert.equal(classify({ pack: null, old: 5, unit: 'buc' }, { price: 5, how: 'json-ld' }).status, 'same');
  assert.equal(classify({ pack: null, old: 5 }, null).status, 'manual');
});

test('ce e de reverificat: data efectivă ține cont de reverificările salvate', () => {
  const all = staleItems(materials, catalog, rechecks, '2099-01-01');
  assert.ok(all.length > 400, String(all.length));
  const recent = staleItems(materials, catalog, rechecks, '2026-11-18');
  const rk = new Set((rechecks.items as { kind: string; id: string; seenAt: string }[]).filter(r => r.seenAt >= '2026-11-18').map(r => `${r.kind}:${r.id}`));
  assert.ok(recent.every((i: { kind: string; id: string }) => !rk.has(`${i.kind}:${i.id}`)), 'cele citite pe 18 nu mai apar');
  assert.ok(recent.length < all.length);
  // linkul folosit e cel relegat, nu cel vechi de categorie
  const billy = all.find((i: { url: string }) => /billy/.test(i.url) && i.url.includes('/p/'));
  assert.ok(!all.some((i: { url: string }) => /\/cat\/familia-billy/.test(i.url)), billy?.url);
});

test('fișierul de reverificări primește doar citirile sigure; o citire nouă o înlocuiește pe cea veche', () => {
  const base = { note: 'n', items: [{ kind: 'o', id: 'a', price: 1, seenAt: '2026-11-18', url: 'https://x/p/a', evidence: 'e' }], notVerified: [{ kind: 'offer', id: 'b', url: 'u', status: 'manual', why: 'w' }] };
  const r = mergeRechecks(base, [
    { kind: 'o', id: 'a', url: 'https://x/p/a', status: 'changed', price: 2, how: 'json-ld', evidence: 'e2' },
    { kind: 'o', id: 'b', url: 'https://x/p/b', status: 'same', price: 3, how: 'dedeman-finalPrice', evidence: 'e3' },
    { kind: 'm', id: 'c', url: 'https://x/p/c', status: 'manual', why: 'răspuns 403' },
  ], '2026-12-18');
  assert.equal(r.applied, 2); assert.equal(r.items.length, 2);
  assert.deepEqual(r.items.find((i: { id: string }) => i.id === 'a'), { kind: 'o', id: 'a', price: 2, seenAt: '2026-12-18', url: 'https://x/p/a', evidence: 'e2' });
  assert.deepEqual(r.notVerified.map((n: { id: string }) => n.id), ['c'], 'b a fost citit, iese din neverificate');
  assert.equal(r.note, 'n');
});

test('ofertele dispărute: intră în lista separată, nu în „neverificate”; o ofertă citită din nou iese din listă', async () => {
  const { mergeGone } = await import('../scripts/lib/price-extract.mjs');
  const res = [{ kind: 'o', id: 'g', url: 'https://x/p/g', status: 'gone', why: 'pagina nu mai există' }, { kind: 'o', id: 'back', url: 'https://x/p/b', status: 'same', price: 1, how: 'json-ld', evidence: 'e' }];
  const g = mergeGone({ note: 'n', items: [{ id: 'back', checkedAt: '2026-11-01', url: 'u', why: 'w' }, { id: 'old', checkedAt: '2026-11-01', url: 'u', why: 'w' }] }, res, '2026-12-18');
  assert.deepEqual(g.items.map((i: { id: string }) => i.id), ['old', 'g']); assert.equal(g.items[1].checkedAt, '2026-12-18');
  assert.deepEqual(mergeRechecks({ items: [], notVerified: [] }, res, '2026-12-18').notVerified, [], 'dispărută ≠ de verificat de mână');
});

test('Dedeman fără codul în cutia de preț: produsul din formularul „adaugă în coș”, doar dacă adresa canonică e a codului', () => {
  const rec = '<div data-price-box="product-id-111"><span id="product-price-111" data-price-amount="9.99" data-price-type="finalPrice"></span></div>';
  const page = (canon: string) => `<link rel="canonical" href="${canon}" />${rec}<form id="product_addtocart_form" action="/checkout/cart/add/"><input type="hidden" name="product" value="185945"></form>
    <div data-price-box="product-id-185945"><span data-price-type="oldPrice" data-price-amount="548.99"></span><span id="product-price-185945" data-price-amount="469" data-price-type="finalPrice" class="price-wrapper"></span><span class="price-wrapper price-sale-unit">/bucată</span></div>`;
  const url = 'https://www.dedeman.ro/ro/oglinda/p/3043896', p = priceFromHtml(page(url), url);
  assert.equal(p.price, 469); assert.equal(p.unit, 'bucată'); assert.match(p.evidence, /produsul 185945 din formularul paginii/); assert.match(p.evidence, /promoție/);
  assert.equal(priceFromHtml(page('https://www.dedeman.ro/ro/alt-produs/p/999'), url), null, 'canonica altui produs → nimic');
  assert.equal(priceFromHtml(rec, url), null, 'fără formular → nimic (nu se ia prețul unui produs recomandat)');
});

test('eMAG: JSON-LD cu linii noi nescăpate în descriere se citește; vânzătorul fără entități HTML', () => {
  const raw = '<script type="application/ld+json">{"@type": "Product", "description": "Nota:\n\tdescriere", "offers": {"@type": "Offer", "price": "399.3", "priceCurrency": "RON", "seller": {"name": "ELECTRICS SHOP &amp; BATTERY"}}}</script>';
  assert.throws(() => JSON.parse(raw.slice(raw.indexOf('{'), raw.lastIndexOf('}') + 1)), 'JSON-ul paginii e invalid (ca pe eMAG)');
  const p = priceFromHtml(raw, 'https://www.emag.ro/x/pd/DC8JP1BBM/');
  assert.equal(p.price, 399.3); assert.match(p.evidence, /ELECTRICS SHOP & BATTERY/);
});
