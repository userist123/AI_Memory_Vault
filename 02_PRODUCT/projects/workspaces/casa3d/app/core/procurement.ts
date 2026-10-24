// Comenzile proiectului: din fișa de finisaje și din mobilierul plasat rezultă liniile de cumpărat (un produs = o linie,
// cu toate camerele în care apare), grupate pe furnizor. Pe fiecare linie designerul ține starea comenzii, numărul ei,
// data comenzii și data promisă de livrare; o livrare promisă și trecută fără „livrat” e întârziere.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { finishSchedule } from './finish-schedule';
import { resolve, pricedOffer } from './catalog';
import { floors } from './levels';

export const ORDER_STATUSES = ['todo', 'quoted', 'ordered', 'delivered', 'installed', 'cancelled'] as const;
export type OrderStatus = typeof ORDER_STATUSES[number];
export interface OrderState { status: OrderStatus; ref?: string; orderedAt?: string; eta?: string; deliveredAt?: string; note?: string;
  /** data la care produsul trebuie să fie pe șantier și zilele de livrare (implicit pe categorie, editabil) */
  neededBy?: string; leadDays?: number;
  /** garanția în luni, de pe certificat (lipsă = implicită pe tipul produsului) */
  warrantyMonths?: number }
export interface ProcurementLine {
  key: string; kind: 'finish' | 'furniture' | 'custom'; product: string; supplier: string; code: string | null; url: string | null; image?: string | null; itemId?: string;
  qty: number; unit: string; packs: string | null; unitPrice: number | null; total: number | null; rooms: string[];
  /** doar produsele proprii: prețul de achiziție (intern, nu ajunge la client), termenul și dimensiunea */
  costPrice?: number | null; costTotal?: number | null; leadDays?: number | null; dimension?: string | null;
}
export interface ProcurementRow extends ProcurementLine { state: OrderState; overdue: boolean;
  /** zilele de livrare folosite (ale designerului sau implicite) și data până la care trebuie comandat/decis */
  leadDays: number; orderBy: string | null; orderUrgency: 'late' | 'soon' | null }
/** Zile de livrare implicite, pe tipul produsului: valori de pornire, nu promisiuni ale furnizorilor (se schimbă pe linie). */
export function defaultLeadDays(l: Pick<ProcurementLine, 'kind' | 'product'>): number {
  const n = l.product.toLowerCase();
  if (/bucătări|kitchen|metod|enhet|canapea|sofa|kivik|la comandă|custom/.test(n)) return 42;
  if (/gresie|faianț|tile|parchet|laminat|ușă|door|cabin|duș|shower|vas wc|lavoar/.test(n)) return 21;
  if (l.kind === 'finish') return 10; return 21;
}
export const ORDER_SOON_DAYS = 7;
const addDays = (d: string, n: number) => new Date(Date.parse(d + 'T00:00:00Z') + n * 864e5).toISOString().slice(0, 10);

const round2 = (v: number) => Math.round(v * 100) / 100;
/** Ambalajele a două camere adunate („5 × pachet” + „12 × pachet” = „17 × pachet”); etichete diferite → necunoscut. */
export function addPacks(a: string | null, b: string | null): string | null {
  const ma = a?.match(/^(\d+) × (.+)$/), mb = b?.match(/^(\d+) × (.+)$/); return ma && mb && ma[2] === mb[2] ? `${Number(ma[1]) + Number(mb[1])} × ${ma[2]}` : null; }
/** „BESTĂ comodă” + „BESTĂ comodă TV cu uși, alb” → doar varianta (care repetă deja numele produsului). */
export function productLabel(product: string, variant: string | null | undefined){
  const v = (variant ?? '').trim(); if (!v) return product; return v.toLowerCase().startsWith(product.trim().toLowerCase()) ? v : `${product} · ${v}`; }
const keyOf = (...parts: (string | null | undefined)[]) => parts.map(p => (p ?? '').trim().toLowerCase()).join('|').slice(0, 300);

/** Liniile de cumpărat, stabile între salvări (cheia nu depinde de ordinea camerelor). */
/** Produs adăugat de designer în afara catalogului (din linkul unui magazin sau de mână). */
export interface CustomLineItem { id: string; name: string; supplier: string; url: string | null; image: string | null; sku: string | null; price: number | null; qty: number; unit: string; roomId: string | null; costPrice?: number | null; leadDays?: number | null; dimension?: string | null }
export function procurementLines(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, custom: CustomLineItem[] = []): ProcurementLine[] {
  const out = new Map<string, ProcurementLine>();
  for (const r of finishSchedule(snap, cat, mc)){ if (!r.product || r.element === 'adhesive' && !r.supplier) continue;
    const key = 'm|' + keyOf(r.supplier, r.code ?? r.product, r.unit), cur = out.get(key);
    if (cur){ cur.qty = round2(cur.qty + r.orderedQty); cur.packs = addPacks(cur.packs, r.packs); cur.total = cur.total != null && r.total != null ? round2(cur.total + r.total) : null; if (!cur.rooms.includes(r.room)) cur.rooms.push(r.room); continue; }
    out.set(key, { key, kind: 'finish', product: r.product, supplier: r.supplier || '—', code: r.code, url: r.url, qty: round2(r.orderedQty), unit: r.unit, packs: r.packs, unitPrice: r.unitPrice, total: r.total, rooms: [r.room] }); }
  const roomName = new Map(floors(snap).flatMap(f => f.rooms).map(r => [r.id, r.name]));
  for (const p of snap.placements){ const rv = resolve(cat, p.variantId); if (!rv) continue;
    const o = pricedOffer(cat, p), sup = cat.suppliers.find(s => s.id === (o ?? rv.offer)?.supplierId)?.name ?? '—';
    const key = 'f|' + keyOf(p.variantId, p.size ? JSON.stringify(p.size) : ''), room = roomName.get(p.roomId ?? '') ?? '', cur = out.get(key);
    if (cur){ cur.qty += 1; cur.total = cur.total != null && o ? round2(cur.total + o.price) : null; if (room && !cur.rooms.includes(room)) cur.rooms.push(room); continue; }
    out.set(key, { key, kind: 'furniture', product: productLabel(rv.product.name, rv.variant.name), supplier: sup, code: null,
      url: (o ?? rv.offer)?.affiliateUrl ?? (o ?? rv.offer)?.provenance?.sourceUrl ?? null, qty: 1, unit: 'buc', packs: null, unitPrice: o?.price ?? null, total: o ? round2(o.price) : null, rooms: room ? [room] : [] }); }
  for (const c of custom){ const room = roomName.get(c.roomId ?? '') ?? '', key = 'c|' + c.id;
    out.set(key, { key, kind: 'custom', itemId: c.id, product: c.name, supplier: c.supplier, code: c.sku, url: c.url, image: c.image, qty: c.qty, unit: c.unit, packs: null, unitPrice: c.price, total: c.price != null ? round2(c.price * c.qty) : null, rooms: room ? [room] : [],
      costPrice: c.costPrice ?? null, costTotal: c.costPrice != null ? round2(c.costPrice * c.qty) : null, leadDays: c.leadDays ?? null, dimension: c.dimension ?? null }); }
  return [...out.values()].sort((a, b) => a.supplier.localeCompare(b.supplier) || a.kind.localeCompare(b.kind) || a.product.localeCompare(b.product));
}
/** Liniile cu starea lor; o linie fără stare salvată e „de comandat”. */
/** defaultNeededBy: data de început a lucrărilor din fișa proiectului, pentru liniile fără dată proprie. */
export function procurementRows(lines: ProcurementLine[], states: Record<string, OrderState>, today: string, defaultNeededBy?: string): ProcurementRow[] {
  return lines.map(l => { const state = states[l.key] ?? { status: 'todo' as const };
    const leadDays = state.leadDays ?? l.leadDays ?? defaultLeadDays(l), needed = state.neededBy ?? defaultNeededBy, orderBy = needed ? addDays(needed, -leadDays) : null, open = state.status === 'todo' || state.status === 'quoted';
    const orderUrgency = !open || !orderBy ? null : orderBy < today ? 'late' : orderBy <= addDays(today, ORDER_SOON_DAYS) ? 'soon' : null;
    return { ...l, state, overdue: state.status === 'ordered' && !!state.eta && state.eta < today, leadDays, orderBy, orderUrgency }; });
}
export interface SupplierGroup { supplier: string; rows: ProcurementRow[]; total: number | null; open: number; overdue: number }
export function bySupplier(rows: ProcurementRow[]): SupplierGroup[] {
  const m = new Map<string, ProcurementRow[]>(); for (const r of rows) m.set(r.supplier, [...(m.get(r.supplier) ?? []), r]);
  return [...m.entries()].map(([supplier, rs]) => { const act = rs.filter(r => r.state.status !== 'cancelled');
    return { supplier, rows: rs, total: act.every(r => r.total != null) ? round2(act.reduce((a, r) => a + (r.total ?? 0), 0)) : null,
      open: act.filter(r => r.state.status === 'todo' || r.state.status === 'quoted').length, overdue: rs.filter(r => r.overdue).length }; });
}
/**
 * Starea nouă a unei linii, aplicată imediat în interfață (înainte de răspunsul serverului): starea, întârzierea și urgența
 * comenzii se recalculează ca în procurementRows, deci contoarele de sus se schimbă la clic. Celelalte linii rămân neatinse.
 */
export function withOrderState(rows: ProcurementRow[], key: string, state: OrderState, today: string): ProcurementRow[] {
  return rows.map(r => { if (r.key !== key) return r;
    const leadDays = state.leadDays ?? r.leadDays, orderBy = state.neededBy ? addDays(state.neededBy, -leadDays) : r.orderBy ? addDays(r.orderBy, r.leadDays - leadDays) : null, open = state.status === 'todo' || state.status === 'quoted';
    const orderUrgency = !open || !orderBy ? null : orderBy < today ? 'late' as const : orderBy <= addDays(today, ORDER_SOON_DAYS) ? 'soon' as const : null;
    return { ...r, state, leadDays, orderBy, orderUrgency, overdue: state.status === 'ordered' && !!state.eta && state.eta < today }; });
}
export function procurementSummary(rows: ProcurementRow[]){
  const act = rows.filter(r => r.state.status !== 'cancelled'), n = (s: OrderStatus) => act.filter(r => r.state.status === s).length;
  return { lines: act.length, todo: n('todo') + n('quoted'), ordered: n('ordered'), delivered: n('delivered') + n('installed'), installed: n('installed'), overdue: rows.filter(r => r.overdue).length, orderLate: rows.filter(r => r.orderUrgency === 'late').length, orderSoon: rows.filter(r => r.orderUrgency === 'soon').length,
    ordersValue: round2(act.filter(r => ['ordered', 'delivered', 'installed'].includes(r.state.status)).reduce((a, r) => a + (r.total ?? 0), 0)) };
}

const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const validDate = (v: string) => DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').trim().slice(0, max) : '';
/** Starea trimisă de interfață, validată; câmpurile goale dispar. */
export function checkOrderState(x: unknown): OrderState {
  const o = x as any; if (!o || typeof o !== 'object' || !(ORDER_STATUSES as readonly string[]).includes(o.status)) throw new Error('Starea comenzii e invalidă.');
  const s: OrderState = { status: o.status }, ref = clean(o.ref, 80), note = clean(o.note, 500);
  if (ref) s.ref = ref; if (note) s.note = note;
  if (o.leadDays != null && o.leadDays !== ''){ if (!Number.isInteger(o.leadDays) || o.leadDays < 0 || o.leadDays > 365) throw new Error('Zilele de livrare nu sunt valide (0–365).'); s.leadDays = o.leadDays; }
  if (o.warrantyMonths != null && o.warrantyMonths !== ''){ if (!Number.isInteger(o.warrantyMonths) || o.warrantyMonths < 0 || o.warrantyMonths > 240) throw new Error('Garanția nu e validă (0–240 de luni).'); s.warrantyMonths = o.warrantyMonths; }
  for (const k of ['orderedAt', 'eta', 'deliveredAt', 'neededBy'] as const){ const v = o[k]; if (v == null || v === '') continue; if (typeof v !== 'string' || !validDate(v)) throw new Error('Data nu e validă (AAAA-LL-ZZ).'); s[k] = v; }
  return s;
}
export const isLineKey = (k: unknown): k is string => typeof k === 'string' && k.length <= 300 && /^[mfc]\|/.test(k);
