// Ce vede clientul pe link despre comenzi: pentru fiecare produs doar starea (de comandat / comandat / livrat / montat)
// și datele de livrare. Fără furnizor, preț de achiziție, nr. de comandă sau note interne: acelea rămân la designer.
import type { ProcurementRow } from './procurement';

export type ClientDeliveryStatus = 'todo' | 'ordered' | 'delivered' | 'installed';
export interface ClientDelivery { product: string; rooms: string[]; qty: number; unit: string; status: ClientDeliveryStatus; eta: string | null; deliveredAt: string | null; late: boolean }
export interface ClientProgress { items: ClientDelivery[]; counts: Record<ClientDeliveryStatus, number>; late: number; updatedAt: string | null }

const STATUS: Record<string, ClientDeliveryStatus | null> = { todo: 'todo', quoted: 'todo', ordered: 'ordered', delivered: 'delivered', installed: 'installed', cancelled: null };
const ORDER: ClientDeliveryStatus[] = ['ordered', 'todo', 'delivered', 'installed'];
/** Lista pentru client: întârziatele primele, apoi comandatele (după data de livrare), de comandat, livrate, montate. */
export function clientProgress(rows: ProcurementRow[], updatedAt: string | null): ClientProgress {
  const items: ClientDelivery[] = [];
  for (const r of rows){ const status = STATUS[r.state.status]; if (!status) continue;
    items.push({ product: r.product, rooms: r.rooms, qty: r.qty, unit: r.unit, status, eta: status === 'ordered' ? r.state.eta ?? null : null,
      deliveredAt: status === 'delivered' || status === 'installed' ? r.state.deliveredAt ?? null : null, late: r.overdue }); }
  items.sort((a, b) => Number(b.late) - Number(a.late) || ORDER.indexOf(a.status) - ORDER.indexOf(b.status) || (a.eta ?? '9').localeCompare(b.eta ?? '9') || a.product.localeCompare(b.product));
  const counts = { todo: 0, ordered: 0, delivered: 0, installed: 0 }; for (const i of items) counts[i.status]++;
  return { items, counts, late: items.filter(i => i.late).length, updatedAt };
}
