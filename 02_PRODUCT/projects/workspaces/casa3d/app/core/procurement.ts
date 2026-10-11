// Comenzile proiectului: din fișa de finisaje și din mobilierul plasat rezultă liniile de cumpărat (un produs = o linie,
// cu toate camerele în care apare), grupate pe furnizor. Pe fiecare linie designerul ține starea comenzii, numărul ei,
// data comenzii și data promisă de livrare; o livrare promisă și trecută fără „livrat” e întârziere.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { finishSchedule } from './finish-schedule';
import { resolve, pricedOffer } from './catalog';
import { floors } from './levels';

export const ORDER_STATUSES = ['todo', 'quoted', 'ordered', 'delivered', 'installed', 'cancelled'] as const;
export type OrderStatus = typeof ORDER_STATUSES[number];
export interface OrderState { status: OrderStatus; ref?: string; orderedAt?: string; eta?: string; deliveredAt?: string; note?: string }
export interface ProcurementLine {
  key: string; kind: 'finish' | 'furniture' | 'custom'; product: string; supplier: string; code: string | null; url: string | null; image?: string | null; itemId?: string;
  qty: number; unit: string; packs: string | null; unitPrice: number | null; total: number | null; rooms: string[];
}
export interface ProcurementRow extends ProcurementLine { state: OrderState; overdue: boolean }

const round2 = (v: number) => Math.round(v * 100) / 100;
/** Ambalajele a două camere adunate („5 × pachet” + „12 × pachet” = „17 × pachet”); etichete diferite → necunoscut. */
export function addPacks(a: string | null, b: string | null): string | null {
  const ma = a?.match(/^(\d+) × (.+)$/), mb = b?.match(/^(\d+) × (.+)$/); return ma && mb && ma[2] === mb[2] ? `${Number(ma[1]) + Number(mb[1])} × ${ma[2]}` : null; }
const keyOf = (...parts: (string | null | undefined)[]) => parts.map(p => (p ?? '').trim().toLowerCase()).join('|').slice(0, 300);

/** Liniile de cumpărat, stabile între salvări (cheia nu depinde de ordinea camerelor). */
/** Produs adăugat de designer în afara catalogului (din linkul unui magazin sau de mână). */
export interface CustomLineItem { id: string; name: string; supplier: string; url: string | null; image: string | null; sku: string | null; price: number | null; qty: number; unit: string; roomId: string | null }
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
    out.set(key, { key, kind: 'furniture', product: `${rv.product.name}${rv.variant.name ? ' · ' + rv.variant.name : ''}`, supplier: sup, code: null,
      url: (o ?? rv.offer)?.affiliateUrl ?? (o ?? rv.offer)?.provenance?.sourceUrl ?? null, qty: 1, unit: 'buc', packs: null, unitPrice: o?.price ?? null, total: o ? round2(o.price) : null, rooms: room ? [room] : [] }); }
  for (const c of custom){ const room = roomName.get(c.roomId ?? '') ?? '', key = 'c|' + c.id;
    out.set(key, { key, kind: 'custom', itemId: c.id, product: c.name, supplier: c.supplier, code: c.sku, url: c.url, image: c.image, qty: c.qty, unit: c.unit, packs: null, unitPrice: c.price, total: c.price != null ? round2(c.price * c.qty) : null, rooms: room ? [room] : [] }); }
  return [...out.values()].sort((a, b) => a.supplier.localeCompare(b.supplier) || a.kind.localeCompare(b.kind) || a.product.localeCompare(b.product));
}
/** Liniile cu starea lor; o linie fără stare salvată e „de comandat”. */
export function procurementRows(lines: ProcurementLine[], states: Record<string, OrderState>, today: string): ProcurementRow[] {
  return lines.map(l => { const state = states[l.key] ?? { status: 'todo' as const };
    return { ...l, state, overdue: state.status === 'ordered' && !!state.eta && state.eta < today }; });
}
export interface SupplierGroup { supplier: string; rows: ProcurementRow[]; total: number | null; open: number; overdue: number }
export function bySupplier(rows: ProcurementRow[]): SupplierGroup[] {
  const m = new Map<string, ProcurementRow[]>(); for (const r of rows) m.set(r.supplier, [...(m.get(r.supplier) ?? []), r]);
  return [...m.entries()].map(([supplier, rs]) => { const act = rs.filter(r => r.state.status !== 'cancelled');
    return { supplier, rows: rs, total: act.every(r => r.total != null) ? round2(act.reduce((a, r) => a + (r.total ?? 0), 0)) : null,
      open: act.filter(r => r.state.status === 'todo' || r.state.status === 'quoted').length, overdue: rs.filter(r => r.overdue).length }; });
}
export function procurementSummary(rows: ProcurementRow[]){
  const act = rows.filter(r => r.state.status !== 'cancelled'), n = (s: OrderStatus) => act.filter(r => r.state.status === s).length;
  return { lines: act.length, todo: n('todo') + n('quoted'), ordered: n('ordered'), delivered: n('delivered') + n('installed'), installed: n('installed'), overdue: rows.filter(r => r.overdue).length,
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
  for (const k of ['orderedAt', 'eta', 'deliveredAt'] as const){ const v = o[k]; if (v == null || v === '') continue; if (typeof v !== 'string' || !validDate(v)) throw new Error('Data nu e validă (AAAA-LL-ZZ).'); s[k] = v; }
  return s;
}
export const isLineKey = (k: unknown): k is string => typeof k === 'string' && k.length <= 300 && /^[mfc]\|/.test(k);
