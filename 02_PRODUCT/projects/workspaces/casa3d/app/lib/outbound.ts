// Faza 4 (mod testare): redirect, contorizare anonimă, linkuri de afiliere opționale, istoric de preț, verificare linkuri.
import { getDb } from './db';
import { HttpError } from './repo';
import { activeLink, destination, deviceBucket, hourOf, isAllowedUrl, type OfferLink, type Retailer, type LinkType } from '../core/outbound';

const kindOk = (k: string): k is 'o' | 'm' => k === 'o' || k === 'm';
const idOk = (id: string) => /^[a-z0-9-]{1,80}$/.test(id);
async function linksFor(kind: 'o' | 'm', id: string){ const { q } = await getDb(); return (await q('select * from offer_links where target_kind=$1 and target_id=$2 order by id', [kind, id])).rows as OfferLink[]; }
async function retailer(slug: string){ const { q } = await getDb(); return (await q('select * from retailers where slug=$1', [slug])).rows[0] as Retailer | undefined; }

// Rezolvă destinația și înregistrează un clic agregat: fără IP, fără cookie, fără user-agent brut.
export async function go(kind: string, id: string, meta: { ua: string | null; from: string | null }){
  if (!kindOk(kind) || !idOk(id)) throw new HttpError(404, 'Link inexistent.');
  const link = activeLink(await linksFor(kind, id)); if (!link) throw new HttpError(404, 'Produsul nu are încă o ofertă.');
  const url = destination(link, await retailer(link.retailer)), device = deviceBucket(meta.ua);
  const from = meta.from && /^\/p\/[0-9a-f-]{36}$/.test(meta.from) ? '/p/:id' : meta.from && meta.from.startsWith('/') ? meta.from.slice(0, 60) : null;
  if (device !== 'bot'){ const { q } = await getDb(); await q('insert into clicks(target_kind, target_id, retailer, link_type, hour, device, from_page) values($1,$2,$3,$4,$5,$6,$7)', [kind, id, link.retailer, link.type, hourOf(), device, from]); }
  return { url, type: link.type };
}
// Starea linkurilor pentru UI: ce țintă are ofertă, dacă e de afiliere (rel=sponsored + informare) și când a fost verificat prețul.
export async function outboundStatus(){
  const { q } = await getDb(), now = new Date();
  const rows = (await q('select * from offer_links')).rows as OfferLink[], by: Record<string, OfferLink[]> = {};
  for (const r of rows) (by[`${r.target_kind}:${r.target_id}`] ||= []).push(r);
  const targets: Record<string, LinkType> = {}; for (const [k, ls] of Object.entries(by)){ const a = activeLink(ls, now); if (a) targets[k] = a.type; }
  return { targets, disclosure: Object.values(targets).includes('affiliate') };
}
// ---------- administrare (doar cu ADMIN_TOKEN) ----------
export function assertAdmin(req: Request){ const t = process.env.ADMIN_TOKEN; if (!t || t.length < 16) throw new HttpError(404, 'Indisponibil.'); if (req.headers.get('x-admin-token') !== t) throw new HttpError(401, 'Neautorizat.'); }
export async function addLink(input: any){
  const kind = String(input.kind), id = String(input.targetId), type = input.type === 'affiliate' ? 'affiliate' : 'direct', network = type === 'affiliate' ? String(input.network || '') : null;
  if (!kindOk(kind) || !idOk(id)) throw new HttpError(400, 'Țintă invalidă.');
  const existing = await linksFor(kind, id); if (!existing.length) throw new HttpError(404, 'Ținta nu are ofertă directă în catalog.');
  const ret = existing[0].retailer; if (!isAllowedUrl(String(input.url), type, network, ret)) throw new HttpError(400, 'URL nepermis: linkurile de afiliere trebuie să fie de pe domeniul rețelei (2Performant/Profitshare), iar cele directe de pe site-ul retailerului, prin https.');
  const from = input.activeFrom ? new Date(input.activeFrom) : new Date(), to = input.activeTo ? new Date(input.activeTo) : null;
  if (isNaN(+from) || (to && (isNaN(+to) || to <= from))) throw new HttpError(400, 'Interval de valabilitate invalid.');
  const { q } = await getDb(); const { rows } = await q('insert into offer_links(target_kind, target_id, retailer, type, network, url, active_from, active_to) values($1,$2,$3,$4,$5,$6,$7,$8) returning id', [kind, id, ret, type, network, String(input.url), from, to]);
  if (type === 'affiliate') await q("update retailers set affiliate_status='active', network=$1, disclosure_required=true where slug=$2", [network, ret]);
  return { id: rows[0].id };
}
// Actualizare manuală de preț: păstrează istoricul și data verificării (nu există feed live în testare).
export async function updatePrice(input: any){
  const kind = String(input.kind), id = String(input.targetId), price = Number(input.price), verifiedAt = String(input.verifiedAt || '');
  if (!kindOk(kind) || !idOk(id) || !(price > 0) || !/^\d{4}-\d{2}-\d{2}$/.test(verifiedAt)) throw new HttpError(400, 'Date invalide.');
  const { q } = await getDb();
  if (kind === 'o'){ const r = await q("update offers set price=$1, verified_at=$2, verification_type='manual', confidence='HIGH' where id=$3 returning id", [price, verifiedAt, id]); if (!r.rows[0]) throw new HttpError(404, 'Ofertă inexistentă.'); }
  else { const m = (await q('select pack from materials where id=$1', [id])).rows[0]; if (!m) throw new HttpError(404, 'Material inexistent.');
    if (m.pack?.price != null){ const pack = { ...m.pack, price }; await q("update materials set pack=$1, unit_price=$2, verified_at=$3, verification_type='manual' where id=$4", [JSON.stringify(pack), Math.round(price / m.pack.size * 100) / 100, verifiedAt, id]); }
    else await q("update materials set unit_price=$1, verified_at=$2, verification_type='manual' where id=$3", [price, verifiedAt, id]); }
  await q('insert into price_history(target_kind, target_id, price, verified_at) values($1,$2,$3,$4)', [kind, id, price, verifiedAt]);
  return { ok: true };
}
export async function priceHistory(kind: string, id: string){ if (!kindOk(kind) || !idOk(id)) throw new HttpError(400, 'Țintă invalidă.'); const { q } = await getDb();
  return (await q('select price, verified_at, recorded_at from price_history where target_kind=$1 and target_id=$2 order by recorded_at', [kind, id])).rows.map(r => ({ price: Number(r.price), verifiedAt: new Date(r.verified_at).toISOString().slice(0, 10) })); }
export async function clickStats(){ const { q } = await getDb();
  const byRetailer = (await q('select retailer, link_type, count(*)::int as n from clicks group by retailer, link_type order by n desc')).rows;
  const top = (await q('select target_kind, target_id, count(*)::int as n from clicks group by target_kind, target_id order by n desc limit 20')).rows;
  return { byRetailer, top }; }
// Verificarea linkurilor directe (HEAD, fără descărcarea paginii). Nu face scraping.
export async function checkLinks(fetchImpl: typeof fetch = fetch, limit = 200){
  const { q } = await getDb(); const rows = (await q("select id, url from offer_links where type='direct' order by last_checked_at nulls first limit $1", [limit])).rows;
  let ok = 0, broken: string[] = [];
  for (const r of rows){ let status = 0; try { const res = await fetchImpl(r.url, { method: 'HEAD', redirect: 'follow' }); status = res.status; } catch { status = 0; }
    await q('update offer_links set last_status=$1, last_checked_at=now() where id=$2', [status, r.id]); if (status >= 200 && status < 400) ok++; else broken.push(r.url); }
  return { checked: rows.length, ok, broken };
}
