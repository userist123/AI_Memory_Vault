// Reverificarea prețurilor (decizia proprietarului, 2026-11-17: fără acorduri cu magazinele, doar linkul paginii și prețul observat
// acolo, cu data citirii). Rezultatele stau în data/price-rechecks.json, separat de fișierele de catalog datate (care rămân istoria
// fiecărui import). La fiecare pornire se aplică peste baza de date: doar dacă verificarea e mai nouă decât cea din bază, deci o
// corectură manuală mai nouă (updatePrice, lib/outbound.ts) nu e suprascrisă, iar a doua pornire nu mai schimbă nimic.
import rechecks from '../data/price-rechecks.json';
import { OFFERS_GONE_LIST, type OfferGone } from './offers-gone';
type Query = (sql: string, params?: unknown[]) => Promise<{ rows: any[] }>;
/** `per`: la un material vândut la pachet, prețul de pe pagină e pe unitate (m², ml, L: „unit”) sau pe pachet / bucată („pack”). */
export interface PriceRecheck { kind: 'm' | 'o'; id: string; price: number; seenAt: string; url: string; evidence: string; per?: 'unit' | 'pack' }
const DATE = /^\d{4}-\d{2}-\d{2}$/;
export const PRICE_RECHECKS = ((rechecks as { items?: PriceRecheck[] }).items ?? []).filter(r => (r.kind === 'm' || r.kind === 'o') && r.price > 0 && DATE.test(r.seenAt));

export async function applyPriceRechecks(q: Query, items: PriceRecheck[] = PRICE_RECHECKS){
  let changed = 0;
  for (const r of items){
    if (r.kind === 'o'){
      // linkul se actualizează odată cu prețul: unele oferte din primul import duceau la o categorie sau la alt produs
      const u = await q(`update offers set price=$1, verified_at=$2, source_url=$4, availability=case when availability='OUT_OF_STOCK' then 'UNKNOWN' else availability end where id=$3 and (verified_at is null or verified_at < $2::date) returning id`, [r.price, r.seenAt, r.id, r.url]);
      if (!u.rows[0]) continue;
    } else {
      // materialul cu preț pe pachet (găleată, bucată de plintă): prețul observat e al pachetului, ca în updatePrice
      // comparația datelor se face în SQL (o dată citită în JavaScript devine miezul nopții local și se mută cu o zi în UTC)
      const m = (await q('select pack, coalesce(verified_at >= $2::date, false) as newer from materials where id=$1', [r.id, r.seenAt])).rows[0]; if (!m || m.newer) continue;
      // pe unitate: prețul pachetului din import nu mai e cel observat, deci se scoate (devizul înmulțește cantitatea comandată în
      // pachete întregi cu prețul pe unitate); nu se calculează un preț de pachet pe care pagina nu l-a arătat
      if (m.pack && r.per === 'unit'){ const { price: _old, ...pack } = m.pack; void _old; await q('update materials set pack=$1, unit_price=$2, verified_at=$3 where id=$4', [JSON.stringify(pack), r.price, r.seenAt, r.id]); }
      else if (m.pack?.price != null) await q('update materials set pack=$1, unit_price=$2, verified_at=$3 where id=$4', [JSON.stringify({ ...m.pack, price: r.price }), Math.round(r.price / m.pack.size * 100) / 100, r.seenAt, r.id]);
      else await q('update materials set unit_price=$1, verified_at=$2 where id=$3', [r.price, r.seenAt, r.id]);
    }
    await q('insert into price_history(target_kind, target_id, price, verified_at) values($1,$2,$3,$4)', [r.kind, r.id, r.price, r.seenAt]); changed++;
  }
  return changed;
}

/** Oferta a cărei pagină a dispărut devine indisponibilă, doar dacă prețul ei nu a fost citit (de mână sau automat) în ziua
 *  verificării sau după; o citire nouă a prețului o face din nou „necunoscută” (pagina există). */
export async function applyOffersGone(q: Query, items: OfferGone[] = OFFERS_GONE_LIST){
  let n = 0;
  for (const g of items) n += (await q(`update offers set availability='OUT_OF_STOCK' where id=$1 and availability <> 'OUT_OF_STOCK' and (verified_at is null or verified_at < $2::date) returning id`, [g.id, g.checkedAt])).rows.length;
  return n;
}

/** Catalogul de materiale din fișier cu reverificările aplicate (aceleași reguli ca applyPriceRechecks, fără bază de date). */
export function materialsWithRechecks<T extends { materials: { id: string; unitPrice: number; verifiedAt?: string; pack?: { size: number; price?: number | null; label?: string } | null }[] }>(mc: T, items: PriceRecheck[] = PRICE_RECHECKS): T {
  const by = new Map(items.filter(r => r.kind === 'm').map(r => [r.id, r]));
  return { ...mc, materials: mc.materials.map(m => { const r = by.get(m.id); if (!r || (m.verifiedAt && m.verifiedAt >= r.seenAt)) return m;
    if (m.pack && r.per === 'unit'){ const { price: _p, ...pack } = m.pack; void _p; return { ...m, pack, unitPrice: r.price, verifiedAt: r.seenAt }; }
    if (m.pack?.price != null) return { ...m, pack: { ...m.pack, price: r.price }, unitPrice: Math.round(r.price / m.pack.size * 100) / 100, verifiedAt: r.seenAt };
    return { ...m, unitPrice: r.price, verifiedAt: r.seenAt }; }) };
}
