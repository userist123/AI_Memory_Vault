// Strat de linkuri externe (Faza 4, mod testare). UI-ul folosește doar /go/o/{offerId} sau /go/m/{materialId};
// destinația reală (directă sau de afiliere) se decide pe server, iar marcajul rel/informarea derivă din tipul linkului.
export type LinkType = 'direct' | 'affiliate';
export interface OfferLink { id: number; target_kind: 'o' | 'm'; target_id: string; retailer: string; type: LinkType; network: string | null; url: string; active_from: string | Date; active_to: string | Date | null }
export interface Retailer { slug: string; name: string; website: string; affiliate_status: string; network: string | null; default_utm: Record<string, string>; disclosure_required: boolean }
export const DEFAULT_UTM = { utm_source: 'casamea3d', utm_medium: 'referral', utm_campaign: 'shopping_list' };
export const STALE_DAYS = 14;
// domenii permise pentru linkurile de afiliere (rețelele cercetate) — protecție împotriva open-redirect
export const AFFILIATE_HOSTS: Record<string, string[]> = { '2performant': ['event.2performant.ro', 'event.2performant.com'], profitshare: ['l.profitshare.ro', 'profitshare.ro'] };
export const RETAILER_HOSTS: Record<string, string[]> = { 'ikea-ro': ['www.ikea.com', 'ikea.com'], dedeman: ['www.dedeman.ro', 'dedeman.ro'],
  'jysk-ro': ['jysk.ro', 'www.jysk.ro'], mobexpert: ['mobexpert.ro', 'www.mobexpert.ro'] };

export function activeLink(links: OfferLink[], now = new Date()): OfferLink | null {
  const live = links.filter(l => new Date(l.active_from) <= now && (!l.active_to || new Date(l.active_to) > now));
  return live.find(l => l.type === 'affiliate') || live.find(l => l.type === 'direct') || null;   // afilierea activă are prioritate; expirată → revine la link direct
}
export function destination(link: OfferLink, retailer?: Retailer): string {
  if (link.type === 'affiliate') return link.url;                                                 // linkurile de afiliere nu se modifică (regulile rețelelor)
  const u = new URL(link.url); for (const [k, v] of Object.entries({ ...DEFAULT_UTM, ...(retailer?.default_utm || {}) })) if (!u.searchParams.has(k)) u.searchParams.set(k, v); return u.toString();
}
export const relFor = (t: LinkType | null) => t === 'affiliate' ? 'sponsored nofollow noopener noreferrer' : 'nofollow noopener noreferrer';
export function isAllowedUrl(url: string, type: LinkType, network: string | null, retailer: string): boolean {
  let u: URL; try { u = new URL(url); } catch { return false; } if (u.protocol !== 'https:') return false;
  const hosts = type === 'affiliate' ? (network ? AFFILIATE_HOSTS[network] : undefined) : RETAILER_HOSTS[retailer]; return !!hosts && hosts.includes(u.hostname);
}
export function deviceBucket(ua: string | null): 'mobil' | 'tabletă' | 'desktop' | 'bot' | 'necunoscut' {
  if (!ua) return 'necunoscut'; if (/bot|crawl|spider|preview|curl|wget/i.test(ua)) return 'bot'; if (/ipad|tablet/i.test(ua)) return 'tabletă'; if (/mobi|android|iphone/i.test(ua)) return 'mobil'; return 'desktop';
}
export const hourOf = (d = new Date()) => { const x = new Date(d); x.setUTCMinutes(0, 0, 0); return x; };
export function freshness(verifiedAt: string | null, now = new Date()){ if (!verifiedAt) return { days: null, stale: true }; const days = Math.floor((now.getTime() - new Date(verifiedAt + 'T00:00:00Z').getTime()) / 864e5); return { days, stale: days > STALE_DAYS }; }
