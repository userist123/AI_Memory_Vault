import type { Catalog, ProductVariant, Offer, Product } from './types';
import type { EngineGroup } from './layout';

export interface ResolvedVariant { variant: ProductVariant; product: Product; offer: Offer | null; w: number; d: number; h: number }
export const groupOf = (variantId: string) => variantId.slice(0, variantId.lastIndexOf('-'));
export const indexOf = (variantId: string) => Number(variantId.slice(variantId.lastIndexOf('-') + 1));

export function resolve(cat: Catalog, variantId: string): ResolvedVariant | null {
  const variant = cat.variants.find(v => v.id === variantId); if (!variant) return null;
  const product = cat.products.find(p => p.id === variant.productId)!;
  const offer = cat.offers.find(o => o.variantId === variantId) || null;
  const dm = variant.dimensionsCm;
  return { variant, product, offer, w: dm ? dm.w / 100 : 0, d: dm ? dm.d / 100 : 0, h: dm ? dm.h / 100 : 0 };
}
export function groups(cat: Catalog){
  const out: Record<string, { label: string; model: string; includedWith?: string; variants: ProductVariant[] }> = {};
  for (const v of [...cat.variants].sort((a, b) => a.legacyIndex - b.legacyIndex)){
    const p = cat.products.find(x => x.id === v.productId)!; const g = groupOf(v.id);
    (out[g] ||= { label: p.category, model: p.model3d, includedWith: v.includedWith, variants: [] }).variants.push(v);
  }
  return out;
}
// Catalogul din baza de date → formatul pe care îl înțelege motorul extras din prototip (OPTS).
export function toEngineCatalog(cat: Catalog): Record<string, EngineGroup> {
  const out: Record<string, EngineGroup> = {};
  for (const [g, grp] of Object.entries(groups(cat))){
    out[g] = { eticheta: grp.label, ...(grp.model && grp.model !== 'none' ? { model: grp.model } : {}), ...(grp.includedWith ? { inclus: grp.includedWith } : {}),
      v: grp.variants.map(v => { const o = cat.offers.find(x => x.variantId === v.id); const dm = v.dimensionsCm;
        return { nume: v.name, mag: o?.provenance.source, pret: o?.price, url: o?.provenance.sourceUrl || undefined, ...(dm ? { w: dm.w, d: dm.d, h: dm.h } : {}), ...(v.dimensionsConfidence === 'MEDIUM' && dm ? { aprox: true } : {}), ...(Object.keys(v.style || {}).length ? { s: v.style } : {}), ...(v.chairs ? { chairs: v.chairs } : {}) }; }) };
  }
  return out;
}

/** Totalul mobilierului la prețurile cunoscute din catalogul curent; piesele fără preț se numără separat, nu ca 0. */
export function furnitureTotal(cat: Catalog, placements: { variantId: string }[]): { known: number; unknown: number } {
  let known = 0, unknown = 0;
  for (const p of placements){ const price = resolve(cat, p.variantId)?.offer?.price; if (typeof price === 'number' && Number.isFinite(price)) known += price; else unknown++; }
  return { known, unknown };
}
