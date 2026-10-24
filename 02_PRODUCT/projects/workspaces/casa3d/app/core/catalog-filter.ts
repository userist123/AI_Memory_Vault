// Căutare și filtre peste catalog (pură, fără I/O). Dimensiuni în cm; prețurile rămân în moneda ofertei.
import type { Catalog, Confidence } from './types';
import { groupOf } from './catalog';

export interface CatalogFilter {
  q?: string; retailers?: string[]; markets?: string[];
  /** ISO 4217. Limitele de preț se aplică doar în această monedă; dacă lipsește și catalogul are o singură monedă, aceea. */
  currency?: string; minPrice?: number; maxPrice?: number; maxW?: number; maxD?: number; group?: string; sort?: 'price' | 'name';
}
export interface CatalogRow { variantId: string; group: string; groupLabel: string; productName: string; variantName: string; brand: string; retailer: string; country: string | null; offerId: string | null; price: number | null; currency: string | null; dims: { w: number; d: number; h: number } | null; confidence: Confidence }

/** minuscule, fără diacritice (ă â î ș ț și variantele cu sedilă), spații normalizate */
export const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();
const bound = (n: number | undefined): n is number => typeof n === 'number' && Number.isFinite(n);

/** Monedele ofertelor din catalog (sortate). Mai mult de una => filtrul de preț cere o monedă explicită. */
export const catalogCurrencies = (cat: Catalog) => [...new Set(cat.offers.map(o => o.currency))].sort();
/** Țările furnizorilor (sortate). */
export const catalogMarkets = (cat: Catalog) => [...new Set(cat.suppliers.map(s => s.country))].sort();

export function searchCatalog(cat: Catalog, f: CatalogFilter = {}): CatalogRow[] {
  const products = new Map(cat.products.map(p => [p.id, p])), suppliers = new Map(cat.suppliers.map(s => [s.id, s]));
  const offers = new Map<string, (typeof cat.offers)[number]>(); for (const o of cat.offers) if (!offers.has(o.variantId)) offers.set(o.variantId, o);
  const labels = new Map<string, string>(); for (const v of cat.variants){ const g = groupOf(v.id); const p = products.get(v.productId); if (p && !labels.has(g)) labels.set(g, p.category); }
  const words = fold(f.q ?? '').split(' ').filter(Boolean), want = (f.retailers ?? []).map(fold), markets = (f.markets ?? []).map(fold);
  const priceBound = bound(f.minPrice) || bound(f.maxPrice);
  const currs = catalogCurrencies(cat), cur = f.currency ?? (currs.length === 1 ? currs[0] : undefined);
  if (priceBound && !cur) return [];   // monede multiple fără alegere: nu comparăm sume în monede diferite
  const rows: CatalogRow[] = [];
  for (const v of cat.variants){
    const p = products.get(v.productId); if (!p) continue;
    const group = groupOf(v.id); if (f.group && group !== f.group) continue;
    const o = offers.get(v.id) ?? null, sup = o ? suppliers.get(o.supplierId) : undefined, retailer = sup?.name ?? '';
    if (want.length && !(sup && want.some(w => w === fold(sup.name) || w === fold(sup.id)))) continue;
    if (markets.length && !(sup && markets.includes(fold(sup.country)))) continue;
    const price = o && Number.isFinite(o.price) ? o.price : null, currency = o ? o.currency : null;
    if (f.currency && price !== null && currency !== f.currency) continue;
    if (priceBound){
      if (price === null || currency !== cur) continue;
      if ((bound(f.minPrice) && price < f.minPrice) || (bound(f.maxPrice) && price > f.maxPrice)) continue;
    }
    const dm = v.dimensionsCm, dims = dm ? { w: dm.w, d: dm.d, h: dm.h } : null;
    if ((bound(f.maxW) || bound(f.maxD)) && !dims) continue;
    if (dims && ((bound(f.maxW) && dims.w > f.maxW) || (bound(f.maxD) && dims.d > f.maxD))) continue;
    const groupLabel = labels.get(group) ?? p.category;
    if (words.length){ const hay = fold([p.name, p.brand, p.category, v.name, groupLabel].join(' ')); if (!words.every(w => hay.includes(w))) continue; }
    rows.push({ variantId: v.id, group, groupLabel, productName: p.name, variantName: v.name, brand: p.brand, retailer, country: sup?.country ?? null, offerId: o?.id ?? null, price, currency, dims, confidence: v.dimensionsConfidence });
  }
  const byName = (a: CatalogRow, b: CatalogRow) => fold(a.productName + ' ' + a.variantName).localeCompare(fold(b.productName + ' ' + b.variantName), 'ro') || a.variantId.localeCompare(b.variantId);
  const byPrice = (a: CatalogRow, b: CatalogRow) => (a.price === null ? 1 : 0) - (b.price === null ? 1 : 0) || (a.price !== null && b.price !== null ? (a.currency ?? '').localeCompare(b.currency ?? '') || a.price - b.price : 0);
  return rows.sort(f.sort === 'name' ? byName : (a, b) => byPrice(a, b) || byName(a, b));
}
