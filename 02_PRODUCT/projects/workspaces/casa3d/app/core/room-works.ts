// Ce se face într-o cameră: finisaje/materiale, manoperă și mobilier, cu totaluri pe monedă (prețurile lipsă se numără, nu devin 0).
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { computeBOQ } from './boq';
import { resolve, pricedOffer } from './catalog';

export interface WorkMaterial { key: string; label: string; qty: number; unit: string; cost: number | null }
export interface WorkLabor { key: string; label: string; qty: number; unit: string; low: number; expected: number; high: number }
export interface WorkFurniture { id: string; name: string; retailer: string | null; price: number | null; currency: string | null; offerId: string | null }
export interface RoomCurrencyTotal { known: number; unknown: number; laborExpected: number }
export interface RoomWorks { materials: WorkMaterial[]; labor: WorkLabor[]; furniture: WorkFurniture[]; totals: Record<string, RoomCurrencyTotal> }

const finite = (n: unknown): n is number => typeof n === 'number' && Number.isFinite(n);

export function roomWorks(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, roomId: string, currency?: string): RoomWorks {
  const boq = computeBOQ(snap, cat, mc), cur = currency ?? cat.offers[0]?.currency ?? 'RON';
  const items = boq.items.filter(i => i.roomId === roomId && i.category !== 'furniture');
  const materials: WorkMaterial[] = items.map(i => ({ key: i.key, label: i.label, qty: i.orderedQty, unit: i.unit, cost: finite(i.total) ? i.total : null }));
  const labor: WorkLabor[] = boq.labor.filter(l => l.roomId === roomId).map(l => ({ key: l.key, label: l.label, qty: l.qty, unit: l.unit, low: l.low, expected: l.expected, high: l.high }));
  // piesa pe comandă nu se cumpără din catalog: fără magazin, fără link, preț necunoscut (pricedOffer)
  const furniture: WorkFurniture[] = snap.placements.filter(p => p.roomId === roomId).map(p => { const rv = resolve(cat, p.variantId), o = pricedOffer(cat, p);
    const name = rv?.product.name ?? p.group;
    return { id: p.id, name: p.size ? `${name} — pe comandă ${p.size.w}×${p.size.d}×${p.size.h} cm` : name, retailer: p.size ? null : rv?.offer?.provenance.source ?? null, price: o ? o.price : null, currency: rv?.offer?.currency ?? null, offerId: p.size ? null : rv?.offer?.id ?? null }; });
  const totals: Record<string, RoomCurrencyTotal> = {};
  const slot = (c: string) => (totals[c] ||= { known: 0, unknown: 0, laborExpected: 0 });
  const placementIds = new Set(furniture.map(f => f.id));
  // sanitarele/electrocasnicele plasate apar și în mobilier: se numără o singură dată (la mobilier)
  for (const m of materials){ if (placementIds.has(m.key)) continue; if (m.cost != null) slot(cur).known += m.cost; else slot(cur).unknown++; }
  for (const f of furniture){ const t = slot(f.currency ?? cur); if (f.price != null) t.known += f.price; else t.unknown++; }
  for (const l of labor) slot(cur).laborExpected += l.expected;
  for (const t of Object.values(totals)){ t.known = Math.round(t.known * 100) / 100; t.laborExpected = Math.round(t.laborExpected * 100) / 100; }
  return { materials, labor, furniture, totals };
}
