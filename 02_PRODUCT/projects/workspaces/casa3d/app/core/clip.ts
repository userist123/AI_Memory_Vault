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
/** Adrese IP care nu sunt pe internet (rețea locală, loopback, link-local, metadata cloud, multicast, rezervate). */
export function isPrivateIp(ip: string): boolean {
  const v4 = ip.replace(/^::ffff:/i, '');
  if (/^\d+\.\d+\.\d+\.\d+$/.test(v4)){ const [a, b] = v4.split('.').map(Number) as [number, number];
    return a === 0 || a === 10 || a === 127 || (a === 100 && b >= 64 && b <= 127) || (a === 169 && b === 254) || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 192 && b === 0) || (a === 198 && (b === 18 || b === 19)) || a >= 224; }
  const x = ip.toLowerCase(); return x === '::' || x === '::1' || x.startsWith('fc') || x.startsWith('fd') || x.startsWith('fe8') || x.startsWith('fe9') || x.startsWith('fea') || x.startsWith('feb') || x.startsWith('ff') || x.startsWith('64:ff9b');
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
export interface CustomItemInput { name: string; supplier: string; url: string | null; image: string | null; sku: string | null; price: number | null; currency: string | null; qty: number; unit: string; roomId: string | null }
export class ClipError extends Error {}
const field = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max) : '';
const optUrl = (v: unknown) => { if (v == null || v === '') return null; if (typeof v !== 'string' || v.length > 1000) throw new ClipError('Adresa nu e validă.');
  let u: URL; try { u = new URL(v); } catch { throw new ClipError('Adresa nu e validă.'); } if (u.protocol !== 'https:' || u.username || u.password) throw new ClipError('Adresa trebuie să fie https.'); return u.href; };
/** Câmpurile trimise de interfață; la modificare (partial) doar cele prezente. */
export function checkCustomItem(x: unknown, partial = false): Partial<CustomItemInput> {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new ClipError('Produsul nu e valid.');
  const out: Partial<CustomItemInput> = {}, has = (k: string) => !partial || k in o;
  if (has('name')){ const n = field(o.name, 200); if (!n) throw new ClipError('Scrie numele produsului.'); out.name = n; }
  if (has('supplier')){ const s = field(o.supplier, 80); if (!s) throw new ClipError('Scrie furnizorul.'); out.supplier = s; }
  if (has('url')) out.url = optUrl(o.url);
  if (has('image')) out.image = optUrl(o.image);
  if (has('sku')) out.sku = field(o.sku, 60) || null;
  if (has('price')){ const p = o.price; if (p == null || p === '') out.price = null; else if (typeof p === 'number' && Number.isFinite(p) && p >= 0 && p < 1e7) out.price = Math.round(p * 100) / 100; else throw new ClipError('Prețul nu e valid.'); }
  if (has('currency')){ const c = o.currency; if (c == null || c === '') out.currency = null; else if (typeof c === 'string' && /^[A-Za-z]{3}$/.test(c)) out.currency = c.toUpperCase(); else throw new ClipError('Moneda nu e validă.'); }
  if (has('qty')){ const q = o.qty ?? 1; if (typeof q !== 'number' || !Number.isFinite(q) || q <= 0 || q > 100000) throw new ClipError('Cantitatea nu e validă.'); out.qty = Math.round(q * 100) / 100; }
  if (has('unit')) out.unit = field(o.unit, 12) || 'buc';
  if (has('roomId')){ const r = o.roomId; if (r == null || r === '') out.roomId = null; else if (typeof r === 'string' && r.length <= 64) out.roomId = r; else throw new ClipError('Camera nu e validă.'); }
  return out;
}
