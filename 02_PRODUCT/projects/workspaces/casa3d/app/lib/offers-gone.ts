// Ofertele scoase din vânzare (pagina produsului a dispărut sau trimite la o categorie), găsite de scripts/recheck-prices.mjs.
import gone from '../data/offers-gone.json';
/** `replacement`: varianta echivalentă din catalog (verificată pe pagina ei), dacă există; `replacementWhy`: ce diferă. */
export interface OfferGone { id: string; checkedAt: string; url: string; why: string; replacement?: string; replacementWhy?: string }
const DATE = /^\d{4}-\d{2}-\d{2}$/;
export const OFFERS_GONE_LIST: OfferGone[] = ((gone as { items?: OfferGone[] }).items ?? []).filter(g => typeof g.id === 'string' && DATE.test(g.checkedAt));
/** id ofertă → data verificării */
export const OFFERS_GONE = new Map(OFFERS_GONE_LIST.map(g => [g.id, g.checkedAt]));
/** id ofertă scoasă din vânzare → înlocuitorul ei */
export const OFFER_REPLACEMENTS = new Map(OFFERS_GONE_LIST.filter(g => g.replacement).map(g => [g.id, { variantId: g.replacement!, why: g.replacementWhy ?? '' }]));
