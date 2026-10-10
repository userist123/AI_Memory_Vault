// Căutare și filtre în catalog: diacritice, cuvinte multiple, magazin/piață, preț (cu necunoscute și monede), dimensiuni, sortare.
import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import { searchCatalog, fold, catalogCurrencies } from '../core/catalog-filter';
import type { Catalog } from '../core/types';

const cat = catalogV1 as unknown as Catalog;
const clone = () => structuredClone(cat);
const ids = (rows: { variantId: string }[]) => rows.map(r => r.variantId);

describe('catalog-filter', () => {
  test('fără filtre: toate variantele, fiecare cu rând complet', () => {
    const rows = searchCatalog(cat); assert.equal(rows.length, cat.variants.length);
    for (const r of rows){ assert.ok(r.productName && r.variantName && r.groupLabel && r.group); }
  });
  test('diacritice și majuscule: canapea~Canapea, noptiera~noptieră', () => {
    assert.equal(fold('Noptieră ȘțĂ'), 'noptiera sta');
    for (const q of ['canapea', 'CANAPEA', 'Canapea']){ const r = searchCatalog(cat, { q }); assert.ok(r.length > 0); assert.ok(r.every(x => x.group === 'canapea' || fold(`${x.productName} ${x.variantName} ${x.brand} ${x.groupLabel}`).includes('canapea'))); }
    const a = searchCatalog(cat, { q: 'noptiera' }), b = searchCatalog(cat, { q: 'noptieră' });
    assert.ok(a.length > 0); assert.deepEqual(ids(a), ids(b));
    assert.ok(a.every(r => r.group === 'noptiera'));
  });
  test('interogare cu mai multe cuvinte: toate trebuie să se potrivească', () => {
    const one = searchCatalog(cat, { q: 'canapea' }), two = searchCatalog(cat, { q: 'canapea kivik' });
    assert.ok(two.length > 0 && two.length <= one.length);
    assert.ok(two.every(r => fold(`${r.productName} ${r.variantName}`).includes('kivik')));
    assert.equal(searchCatalog(cat, { q: 'canapea zzznimic' }).length, 0);
    assert.deepEqual(ids(searchCatalog(cat, { q: 'kivik canapea' })), ids(two));
  });
  test('filtru de magazin (după id sau nume) și de piață', () => {
    const ikea = searchCatalog(cat, { retailers: ['ikea-ro'] }), ded = searchCatalog(cat, { retailers: ['Dedeman'] });
    assert.ok(ikea.length > 0 && ded.length > 0); assert.ok(ikea.every(r => r.retailer === 'IKEA')); assert.ok(ded.every(r => r.retailer === 'Dedeman'));
    assert.equal(searchCatalog(cat, { retailers: ['IKEA', 'Dedeman'] }).filter(r => r.offerId).length, ikea.length + ded.length);
    assert.equal(searchCatalog(cat, { markets: ['RO'] }).filter(r => r.offerId).length, ikea.length + ded.length);
    assert.equal(searchCatalog(cat, { markets: ['DE'] }).length, 0);
  });
  test('limite de preț: necunoscutele ies doar când e setată o limită', () => {
    const c = clone(); const [o1, o2] = c.offers; c.offers = c.offers.filter(o => o.id !== o1!.id);   // o variantă fără ofertă => preț necunoscut
    const unknownId = o1!.variantId;
    assert.ok(ids(searchCatalog(c)).includes(unknownId)); assert.equal(searchCatalog(c).find(r => r.variantId === unknownId)!.price, null);
    assert.ok(!ids(searchCatalog(c, { maxPrice: 1e9 })).includes(unknownId)); assert.ok(!ids(searchCatalog(c, { minPrice: 0 })).includes(unknownId));
    const mid = o2!.price, within = searchCatalog(c, { minPrice: mid, maxPrice: mid });
    assert.ok(within.length > 0 && within.every(r => r.price === mid));
    const lo = searchCatalog(c, { maxPrice: 500 }); assert.ok(lo.every(r => r.price !== null && r.price <= 500));
    const hi = searchCatalog(c, { minPrice: 500 }); assert.ok(hi.every(r => r.price! >= 500));
  });
  test('monede multiple: limita de preț cere moneda și nu compară sume diferite', () => {
    const c = clone(); const eur = c.offers.find(o => o.price > 0)!; (eur as any).currency = 'EUR'; (eur as any).price = 10;
    assert.deepEqual(catalogCurrencies(c), ['EUR', 'RON']);
    assert.equal(searchCatalog(c, { maxPrice: 20 }).length, 0);                       // ambiguu: nu ghicim moneda
    assert.deepEqual(ids(searchCatalog(c, { maxPrice: 20, currency: 'EUR' })), [eur.variantId]);
    assert.ok(!ids(searchCatalog(c, { maxPrice: 1e9, currency: 'RON' })).includes(eur.variantId));
    assert.equal(searchCatalog(c, { currency: 'EUR' }).filter(r => r.price !== null).length, 1);
  });
  test('limite de dimensiuni: necunoscutele ies doar când e setată o limită', () => {
    const c = clone(); const v = c.variants.find(x => x.dimensionsCm)!; const dm = v.dimensionsCm!; const unk = c.variants.find(x => x !== v)!; unk.dimensionsCm = null;
    assert.ok(ids(searchCatalog(c)).includes(unk.id));
    assert.ok(!ids(searchCatalog(c, { maxW: 1e9 })).includes(unk.id)); assert.ok(!ids(searchCatalog(c, { maxD: 1e9 })).includes(unk.id));
    const w = searchCatalog(c, { maxW: dm.w }); assert.ok(ids(w).includes(v.id)); assert.ok(w.every(r => r.dims && r.dims.w <= dm.w));
    const d = searchCatalog(c, { maxD: dm.d - 1 }); assert.ok(!ids(d).includes(v.id)); assert.ok(d.every(r => r.dims && r.dims.d <= dm.d - 1));
    assert.ok(searchCatalog(c, { maxW: dm.w, maxD: dm.d }).every(r => r.dims!.w <= dm.w && r.dims!.d <= dm.d));
  });
  test('sortare: preț crescător cu necunoscutele la sfârșit, apoi nume; sort=name', () => {
    const c = clone(); const drop = new Set(c.offers.slice(0, 5).map(o => o.id)); c.offers = c.offers.filter(o => !drop.has(o.id));
    const rows = searchCatalog(c); const firstNull = rows.findIndex(r => r.price === null);
    assert.ok(firstNull > 0); assert.ok(rows.slice(firstNull).every(r => r.price === null)); assert.equal(rows.length - firstNull, 5);
    const known = rows.slice(0, firstNull); for (let i = 1; i < known.length; i++) assert.ok(known[i - 1]!.price! <= known[i]!.price!);
    const byName = searchCatalog(c, { sort: 'name' }).map(r => fold(`${r.productName} ${r.variantName}`)); assert.deepEqual(byName, [...byName].sort((a, b) => a.localeCompare(b, 'ro')));
  });
  test('filtru de grupă', () => {
    const rows = searchCatalog(cat, { group: 'canapea' }); assert.ok(rows.length > 0); assert.ok(rows.every(r => r.group === 'canapea'));
    assert.equal(searchCatalog(cat, { group: 'inexistent' }).length, 0);
  });
  test('rândurile trimit la oferte existente cu aceeași variantă, magazin și monedă', () => {
    for (const r of searchCatalog(cat)){
      const o = cat.offers.find(x => x.id === r.offerId); assert.ok(o, r.variantId); assert.equal(o!.variantId, r.variantId);
      assert.equal(r.price, o!.price); assert.equal(r.currency, o!.currency);
      assert.equal(r.retailer, cat.suppliers.find(s => s.id === o!.supplierId)!.name);
    }
  });
});
