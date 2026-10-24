// Supplier feed import (F5 start): a feed is a file the supplier gives us, or a licensed API/affiliate export.
// Never scraped (Constitution art. 10). Every accepted row keeps its provenance; missing prices stay UNKNOWN.
import type { CatalogItem, Money, Provenance } from './catalog.js';

export interface FeedRow {
  id: string;
  name: string;
  width_m: number; depth_m: number; height_m: number;
  price?: number;
  currency?: 'RON' | 'EUR';
  role?: string;
  url?: string;
  verifiedAt?: string;   // ISO date on which the supplier states the price was valid
}

export interface SupplierFeed {
  supplier: string;
  /** How we are allowed to use it: supplier file, licensed API or an affiliate network export. */
  basis: 'supplier_file' | 'licensed_api' | 'affiliate_export';
  exportedAt: string;
  rows: unknown[];
}

export interface FeedRejection { index: number; reason: string }

export interface FeedImportResult {
  supplier: string;
  accepted: CatalogItem[];
  rejected: FeedRejection[];
  unknownPriceCount: number;
}

const isIsoDate = (s: unknown): s is string => typeof s === 'string' && /^\d{4}-\d{2}-\d{2}/.test(s);
const num = (v: unknown): number | undefined => (typeof v === 'number' && Number.isFinite(v) && v > 0 ? v : undefined);

export function parseFeed(raw: unknown): { feed?: SupplierFeed; error?: string } {
  if (!raw || typeof raw !== 'object') return { error: 'Feed-ul trebuie sa fie un obiect.' };
  const f = raw as Record<string, unknown>;
  if (typeof f['supplier'] !== 'string' || !f['supplier']) return { error: 'Lipseste supplier.' };
  if (!['supplier_file', 'licensed_api', 'affiliate_export'].includes(String(f['basis']))) return { error: 'basis trebuie sa fie supplier_file, licensed_api sau affiliate_export (fara scraping).' };
  if (!isIsoDate(f['exportedAt'])) return { error: 'Lipseste exportedAt (ISO).' };
  if (!Array.isArray(f['rows'])) return { error: 'rows trebuie sa fie o lista.' };
  return { feed: { supplier: f['supplier'], basis: f['basis'] as SupplierFeed['basis'], exportedAt: f['exportedAt'], rows: f['rows'] } };
}

export function importFeed(feed: SupplierFeed, opts: { maxRows?: number } = {}): FeedImportResult {
  const accepted: CatalogItem[] = [];
  const rejected: FeedRejection[] = [];
  const seen = new Set<string>();
  const max = opts.maxRows ?? 5000;
  let unknownPriceCount = 0;
  feed.rows.slice(0, max).forEach((row, index) => {
    if (!row || typeof row !== 'object') { rejected.push({ index, reason: 'rand invalid' }); return; }
    const r = row as Record<string, unknown>;
    const id = typeof r['id'] === 'string' && /^[A-Za-z0-9._-]{1,64}$/.test(r['id']) ? r['id'] : undefined;
    const name = typeof r['name'] === 'string' && r['name'].trim().length > 0 ? r['name'].trim().slice(0, 200) : undefined;
    const w = num(r['width_m']), d = num(r['depth_m']), h = num(r['height_m']);
    if (!id) { rejected.push({ index, reason: 'id lipsa sau invalid' }); return; }
    if (seen.has(id)) { rejected.push({ index, reason: `id duplicat: ${id}` }); return; }
    if (!name) { rejected.push({ index, reason: `${id}: nume lipsa` }); return; }
    if (!w || !d || !h || w > 20 || d > 20 || h > 10) { rejected.push({ index, reason: `${id}: dimensiuni lipsa sau neplauzibile` }); return; }
    let price: Money = 'UNKNOWN';
    const p = num(r['price']);
    if (p !== undefined && (r['currency'] === 'RON' || r['currency'] === 'EUR')) {
      if (!isIsoDate(r['verifiedAt'])) { rejected.push({ index, reason: `${id}: pret fara verifiedAt` }); return; }
      price = { amount: Math.round(p * 100) / 100, currency: r['currency'] };
    } else unknownPriceCount++;
    const provenance: Provenance = { source: `feed:${feed.supplier}`, verificationType: 'feed', confidence: price === 'UNKNOWN' ? 'low' : 'medium' };
    if (typeof r['url'] === 'string' && /^https:\/\//.test(r['url'])) provenance.sourceUrl = r['url'];
    if (isIsoDate(r['verifiedAt'])) provenance.verifiedAt = r['verifiedAt'];
    const item: CatalogItem = { id, name, w, d, h, price, retailer: feed.supplier, provenance };
    if (typeof r['role'] === 'string' && r['role']) item.role = r['role'];
    seen.add(id); accepted.push(item);
  });
  if (feed.rows.length > max) rejected.push({ index: max, reason: `feed trunchiat la ${max} randuri` });
  return { supplier: feed.supplier, accepted, rejected, unknownPriceCount };
}
