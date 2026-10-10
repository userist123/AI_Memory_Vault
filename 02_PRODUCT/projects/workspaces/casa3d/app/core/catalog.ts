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
/** Oferta care dă prețul unei piese din plan. `null` pentru piesele pe comandă (cu `size`: nu se cumpără din catalog)
 *  și pentru ofertele fără preț valid. Toate totalurile (buget, diff, export) trec pe aici, ca regula să fie una singură. */
export function pricedOffer(cat: Catalog, p: { variantId: string; size?: unknown }): Offer | null {
  if (p.size) return null; const o = resolve(cat, p.variantId)?.offer;
  return o && typeof o.price === 'number' && Number.isFinite(o.price) ? o : null;
}
export function furnitureTotal(cat: Catalog, placements: { variantId: string; size?: unknown }[]): { known: number; unknown: number } {
  let known = 0, unknown = 0;
  for (const p of placements){ const price = pricedOffer(cat, p)?.price; if (typeof price === 'number' && Number.isFinite(price)) known += price; else unknown++; }
  return { known, unknown };
}

/** Categoriile bifate implicit în brief, după tipul camerei; doar grupe care există în catalog. */
export const ROOM_DEFAULT_WANTS: Record<string, string[]> = {
  dormitor: ['pat', 'noptiera', 'dulap'], living: ['canapea', 'masuta', 'comodaTv'], bucatarie: ['bucatarie', 'frigider', 'masa'],
  baie: ['lavoar', 'wc', 'dus'], hol: ['pantofar', 'oglinda'], birou: ['birou', 'scaunBirou', 'biblioteca'],
};
export function defaultWants(roomType: string | undefined, groupKeys: string[]): string[] {
  return (ROOM_DEFAULT_WANTS[roomType ?? ''] ?? []).filter(k => groupKeys.includes(k));
}
