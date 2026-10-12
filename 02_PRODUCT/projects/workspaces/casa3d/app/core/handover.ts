// Dosarul de predare: ce s-a montat (furnizor, cod, nr. comandă, data livrării), garanția fiecărui produs și până
// când ține, codurile finisajelor (pentru retușuri și înlocuiri), sfaturi de îngrijire, remedierile rămase deschise și
// un model de proces-verbal. În România defectele vizibile nenotate în procesul-verbal se consideră acceptate și tot
// procesul-verbal pornește garanțiile, de aceea dosarul se dă la recepție.
import type { ProcurementRow } from './procurement';
import type { ScheduleRow } from './finish-schedule';

export const CARE_KINDS = ['wood', 'laminate', 'tile', 'paint', 'sanitary', 'kitchen', 'textile', 'lighting', 'furniture'] as const;
export type CareKind = typeof CARE_KINDS[number];
/** Tipul de îngrijire după numele produsului (sfaturi generale; instrucțiunile producătorului au întâietate). */
export function careKind(product: string, kind: ProcurementRow['kind']): CareKind | null {
  const n = product.toLowerCase();
  if (/adeziv|chit|amors|silicon|grund|plintă|plinta/.test(n)) return null;
  if (/parchet (masiv|stratificat)|lemn masiv|stejar masiv/.test(n)) return 'wood';
  if (/laminat|parchet/.test(n)) return 'laminate';
  if (/gresie|faianț|faiant|tile|porțelanat|portelanat|mozaic/.test(n)) return 'tile';
  if (/vopsea|lavabil|tencuial/.test(n)) return 'paint';
  if (/vas wc|lavoar|cabin|duș|dus|baterie|cadă|cada|rezervor|chiuvet/.test(n)) return 'sanitary';
  if (/bucătări|bucatari|blat|metod|enhet|kitchen/.test(n)) return 'kitchen';
  if (/draperi|perdea|covor|stor|textil/.test(n)) return 'textile';
  if (/lamp|plafonier|pendul|aplic|spot|led|corp de iluminat/.test(n)) return 'lighting';
  return kind === 'finish' ? null : 'furniture';
}
/** Garanția implicită în luni: 24 pentru produse (de verificat pe certificatul de garanție), niciuna pentru consumabile. */
export function defaultWarrantyMonths(product: string, kind: ProcurementRow['kind']): number | null {
  return /adeziv|chit|amors|silicon|grund|vopsea|lavabil/.test(product.toLowerCase()) ? null : 24;
}
const addMonths = (d: string, m: number) => { const x = new Date(d + 'T00:00:00Z'); x.setUTCMonth(x.getUTCMonth() + m); return x.toISOString().slice(0, 10); };

export interface HandoverItem { product: string; supplier: string; code: string | null; ref: string | null; rooms: string[]; qty: number; unit: string;
  status: 'delivered' | 'installed'; deliveredAt: string | null; warrantyMonths: number | null; warrantyUntil: string | null; care: CareKind | null }
export interface FinishCode { room: string; element: string; product: string; supplier: string; code: string | null }
export interface Handover { items: HandoverItem[]; notDelivered: { product: string; status: string }[]; finishes: FinishCode[]; care: CareKind[] }

const FINISH_ELEMENTS = new Set(['paint', 'wall', 'walltile', 'floor', 'ceiling', 'baseboard', 'countertop', 'cornice']);
export function handover(rows: ProcurementRow[], schedule: ScheduleRow[]): Handover {
  const items: HandoverItem[] = [], notDelivered: Handover['notDelivered'] = [];
  for (const r of rows){ const st = r.state.status;
    if (st === 'cancelled') continue;
    if (st !== 'delivered' && st !== 'installed'){ notDelivered.push({ product: r.product, status: st }); continue; }
    const months = r.state.warrantyMonths ?? defaultWarrantyMonths(r.product, r.kind), from = r.state.deliveredAt ?? null;
    items.push({ product: r.product, supplier: r.supplier, code: r.code, ref: r.state.ref ?? null, rooms: r.rooms, qty: r.qty, unit: r.unit, status: st, deliveredAt: from,
      warrantyMonths: months, warrantyUntil: months && from ? addMonths(from, months) : null, care: careKind(r.product, r.kind) }); }
  const seen = new Set<string>(), finishes: FinishCode[] = [];
  for (const s of schedule){ if (!FINISH_ELEMENTS.has(s.element)) continue; const k = `${s.room}|${s.element}|${s.product}`; if (seen.has(k)) continue; seen.add(k);
    finishes.push({ room: s.room, element: s.element, product: s.product, supplier: s.supplier, code: s.code }); }
  const care = CARE_KINDS.filter(k => items.some(i => i.care === k));
  items.sort((a, b) => (a.rooms[0] ?? '~').localeCompare(b.rooms[0] ?? '~') || a.product.localeCompare(b.product));
  return { items, notDelivered, finishes, care };
}
