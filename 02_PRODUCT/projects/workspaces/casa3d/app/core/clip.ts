// „Produs din link”: din pagina unui magazin se citesc numele, prețul, poza, codul și marca, din datele structurate
// (JSON-LD schema.org/Product) sau, în lipsă, din etichetele OpenGraph. Pagina e doar text: nu se execută nimic din ea.
export const ALLOWED_SHOPS = ['dedeman.ro', 'ikea.com', 'emag.ro', 'mathaus.ro', 'hornbach.ro', 'leroymerlin.ro', 'jysk.ro', 'mobexpert.ro', 'vivre.ro', 'bricodepot.ro', 'englishhome.ro', 'kika.ro', 'altex.ro', 'mobila-dalin.ro', 'casaidea.ro', 'westwing.ro', 'h-m.com', 'zarahome.com'];
export const SHOP_NAMES: Record<string, string> = { 'dedeman.ro': 'Dedeman', 'ikea.com': 'IKEA', 'emag.ro': 'eMAG', 'mathaus.ro': 'Mathaus', 'hornbach.ro': 'Hornbach', 'leroymerlin.ro': 'Leroy Merlin', 'jysk.ro': 'JYSK', 'mobexpert.ro': 'Mobexpert', 'vivre.ro': 'Vivre', 'bricodepot.ro': 'Brico Depôt', 'englishhome.ro': 'English Home', 'kika.ro': 'kika', 'altex.ro': 'Altex', 'mobila-dalin.ro': 'Mobila Dalin', 'casaidea.ro': 'Casaidea', 'westwing.ro': 'Westwing', 'h-m.com': 'H&M Home', 'zarahome.com': 'Zara Home' };
export interface ClippedProduct { name: string; price: number | null; currency: string | null; image: string | null; sku: string | null; brand: string | null; supplier: string; url: string }

/** Magazinul din adresă, dacă e unul permis (domeniul exact sau un subdomeniu al lui); doar https, fără utilizator/port ciudat. */
export function shopOf(raw: unknown): string | null {
  if (typeof raw !== 'string' || raw.length > 2000) return null; let u: URL; try { u = new URL(raw.trim()); } catch { return null; }
  if (u.protocol !== 'https:' || u.username || u.password || (u.port && u.port !== '443')) return null;
  const h = u.hostname.toLowerCase().replace(/\.$/, ''); return ALLOWED_SHOPS.find(d => h === d || h.endsWith('.' + d)) ?? null;
}
/** IPv6 desfăcut în 8 grupe de 16 biți (cu IPv4 încorporat la final); null dacă nu e o adresă validă. */
function ipv6Words(ip: string): number[] | null {
  let x = ip.toLowerCase().replace(/^\[|\]$/g, '').replace(/%.*$/, ''), tail: number[] = [];
  const v4 = x.match(/(\d+)\.(\d+)\.(\d+)\.(\d+)$/);
  if (v4){ const b = v4.slice(1).map(Number); if (b.some(n => n > 255)) return null; tail = [(b[0]! << 8) | b[1]!, (b[2]! << 8) | b[3]!]; x = x.slice(0, -v4[0].length).replace(/:$/, ':0').replace(/::0$/, '::'); if (x.endsWith(':') && !x.endsWith('::')) x = x.slice(0, -1); }
  const parts = x.split('::'); if (parts.length > 2) return null;
  const head = parts[0] ? parts[0].split(':') : [], rest = parts.length === 2 && parts[1] ? parts[1].split(':') : [];
  const words = (a: string[]) => a.map(h => /^[0-9a-f]{1,4}$/.test(h) ? parseInt(h, 16) : NaN);
  const h = words(head), r = words(rest), fill = 8 - tail.length - h.length - r.length;
  if (h.some(Number.isNaN) || r.some(Number.isNaN) || fill < 0 || (parts.length === 1 && fill !== 0)) return null;
  return [...h, ...Array(fill).fill(0), ...r, ...tail];
}
function privateV4(a: number, b: number): boolean {
  return a === 0 || a === 10 || a === 127 || (a === 100 && b >= 64 && b <= 127) || (a === 169 && b === 254) || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 192 && b === 0) || (a === 198 && (b === 18 || b === 19)) || a >= 224;
}
/** Adrese IP care nu sunt pe internet (rețea locală, loopback, link-local, metadata cloud, multicast, rezervate), în orice
 *  scriere: IPv4, IPv6, IPv4 mapat/compatibil/traducere (::ffff:, ::, ::ffff:0:, 64:ff9b::), 6to4 (2002::). Ce nu se poate
 *  interpreta se tratează ca privat (se refuză). */
export function isPrivateIp(ip: string): boolean {
  if (/^\d+\.\d+\.\d+\.\d+$/.test(ip)){ const b = ip.split('.').map(Number); return b.some(n => n > 255) || privateV4(b[0]!, b[1]!); }
  const w = ipv6Words(ip); if (!w) return true;
  const v4 = (hi: number, lo: number) => privateV4(hi >> 8, hi & 255) || (hi === 0 && lo === 0);
  if (w.every(x => x === 0)) return true;                                                         // ::
  if (w.slice(0, 7).every(x => x === 0) && w[7] === 1) return true;                               // ::1
  if (w.slice(0, 5).every(x => x === 0) && (w[5] === 0xffff || w[5] === 0)) return v4(w[6]!, w[7]!); // ::ffff:a.b.c.d, ::a.b.c.d
  if (w.slice(0, 4).every(x => x === 0) && w[4] === 0xffff && w[5] === 0) return v4(w[6]!, w[7]!);  // ::ffff:0:a.b.c.d
  if (w[0] === 0x64 && w[1] === 0xff9b) return true;                                              // NAT64: traducere spre IPv4, oricare
  if (w[0] === 0x2002) return v4(w[1]!, w[2]!);                                                   // 6to4
  const f = w[0]!; return (f & 0xfe00) === 0xfc00 || (f & 0xffc0) === 0xfe80 || (f & 0xffc0) === 0xfec0 || (f & 0xff00) === 0xff00 || f === 0x2001 && w[1] === 0xdb8;
}
const decode = (s: string) => s.replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(Number(n))).replace(/&#x([0-9a-f]+);/gi, (_, n) => String.fromCodePoint(parseInt(n, 16))).replace(/&amp;/g, '&');
const text = (v: unknown, max = 300) => typeof v === 'string' ? decode(v).replace(/\s+/g, ' ').replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, '').trim().slice(0, max) || null : null;
const num = (v: unknown) => { if (typeof v === 'number' && Number.isFinite(v)) return v; if (typeof v !== 'string') return null; const s = v.replace(/\s/g, ''); const n = Number(/,\d{1,2}$/.test(s) ? s.replace(/\./g, '').replace(',', '.') : s.replace(/,/g, '')); return Number.isFinite(n) && n > 0 && n < 1e7 ? Math.round(n * 100) / 100 : null; };
const httpsUrl = (v: unknown, base: string) => { if (typeof v !== 'string') return null; try { const u = new URL(decode(v), base); return u.protocol === 'https:' ? u.href.slice(0, 1000) : null; } catch { return null; } };

function findProduct(node: any, depth = 0): any { if (!node || typeof node !== 'object' || depth > 6) return null;
  if (Array.isArray(node)){ for (const n of node){ const p = findProduct(n, depth + 1); if (p) return p; } return null; }
  const t = node['@type']; if (t === 'Product' || (Array.isArray(t) && t.includes('Product'))) return node;
  for (const k of ['@graph', 'mainEntity', 'itemListElement', 'item']) if (node[k]){ const p = findProduct(node[k], depth + 1); if (p) return p; } return null; }
const meta = (html: string, prop: string) => { const re = new RegExp(`<meta[^>]+(?:property|name)=["']${prop.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}["'][^>]*>`, 'i'), m = html.match(re)?.[0]; return m?.match(/content=["']([^"']*)["']/i)?.[1] ?? null; };

/** Produsul din pagină; null dacă pagina nu are nici măcar un nume de produs. */
export function parseProductPage(html: string, url: string, shop: string): ClippedProduct | null {
  let p: any = null;
  for (const m of html.matchAll(/<script[^>]+type=["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi)){ try { p = findProduct(JSON.parse(m[1]!.trim())); } catch { p = null; } if (p) break; }
  const offer = p ? (Array.isArray(p.offers) ? p.offers[0] : p.offers?.offers ? (Array.isArray(p.offers.offers) ? p.offers.offers[0] : p.offers.offers) : p.offers) : null;
  const name = text(p?.name) ?? text(meta(html, 'og:title')) ?? text(html.match(/<title>([^<]*)<\/title>/i)?.[1]);
  if (!name) return null;
  const image = httpsUrl(Array.isArray(p?.image) ? (typeof p.image[0] === 'string' ? p.image[0] : p.image[0]?.url) : typeof p?.image === 'object' ? p?.image?.url : p?.image, url) ?? httpsUrl(meta(html, 'og:image'), url);
  const price = num(offer?.price ?? offer?.lowPrice) ?? num(meta(html, 'product:price:amount')) ?? num(meta(html, 'og:price:amount'));
  const currency = text(offer?.priceCurrency, 3) ?? text(meta(html, 'product:price:currency'), 3);
  return { name, price, currency: currency ? currency.toUpperCase() : null, image, sku: text(p?.sku ?? p?.mpn ?? p?.gtin13 ?? p?.productID, 60), brand: text(typeof p?.brand === 'object' ? p?.brand?.name : p?.brand, 80), supplier: SHOP_NAMES[shop] ?? shop, url };
}

// Produsul adăugat de designer în comenzi (preluat din link sau scris de mână): validat aici, pe server.
export interface CustomItemInput { name: string; supplier: string; url: string | null; image: string | null; sku: string | null; price: number | null; currency: string | null; qty: number; unit: string; roomId: string | null;
  /** ce plătește studioul (prețul de achiziție); `price` e prețul de listă / către client */
  costPrice: number | null; leadDays: number | null; dimension: string | null }
export class ClipError extends Error {}
const field = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max) : '';
const optUrl = (v: unknown, allowHttp = false) => { if (v == null || v === '') return null; if (typeof v !== 'string' || v.length > 1000) throw new ClipError('Adresa nu e validă.');
  let u: URL; try { u = new URL(v); } catch { throw new ClipError('Adresa nu e validă.'); } if (!(u.protocol === 'https:' || allowHttp && u.protocol === 'http:') || u.username || u.password) throw new ClipError(allowHttp ? 'Adresa trebuie să fie http sau https.' : 'Adresa trebuie să fie https.'); return u.href; };
const money = (p: unknown, what: string) => { if (p == null || p === '') return null; if (typeof p === 'number' && Number.isFinite(p) && p >= 0 && p < 1e7) return Math.round(p * 100) / 100; throw new ClipError(what + ' nu e valid.'); };
/** Câmpurile trimise de interfață; la modificare (partial) doar cele prezente. */
export function checkCustomItem(x: unknown, partial = false): Partial<CustomItemInput> {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new ClipError('Produsul nu e valid.');
  const out: Partial<CustomItemInput> = {}, has = (k: string) => !partial || k in o;
  if (has('name')){ const n = field(o.name, 200); if (!n) throw new ClipError('Scrie numele produsului.'); out.name = n; }
  if (has('supplier')){ const s = field(o.supplier, 80); if (!s) throw new ClipError('Scrie furnizorul.'); out.supplier = s; }
  if (has('url')) out.url = optUrl(o.url, true);
  if (has('image')) out.image = optUrl(o.image);
  if (has('sku')) out.sku = field(o.sku, 60) || null;
  if (has('price')) out.price = money(o.price, 'Prețul');
  if (has('costPrice')) out.costPrice = money(o.costPrice, 'Prețul de achiziție');
  if (has('leadDays')){ const d = o.leadDays; if (d == null || d === '') out.leadDays = null; else if (typeof d === 'number' && Number.isInteger(d) && d >= 0 && d <= 365) out.leadDays = d; else throw new ClipError('Termenul de livrare nu e valid (0–365 de zile).'); }
  if (has('dimension')) out.dimension = field(o.dimension, 80) || null;
  if (has('currency')){ const c = o.currency; if (c == null || c === '') out.currency = null; else if (typeof c === 'string' && /^[A-Za-z]{3}$/.test(c)) out.currency = c.toUpperCase(); else throw new ClipError('Moneda nu e validă.'); }
  if (has('qty')){ const q = o.qty ?? 1; if (typeof q !== 'number' || !Number.isFinite(q) || q <= 0 || q > 100000) throw new ClipError('Cantitatea nu e validă.'); out.qty = Math.round(q * 100) / 100; }
  if (has('unit')) out.unit = field(o.unit, 12) || 'buc';
  if (has('roomId')){ const r = o.roomId; if (r == null || r === '') out.roomId = null; else if (typeof r === 'string' && r.length <= 64) out.roomId = r; else throw new ClipError('Camera nu e validă.'); }
  return out;
}
