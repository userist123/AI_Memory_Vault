// Comenzile unui proiect: liniile se calculează din plan (core/procurement.ts), starea fiecăreia stă în tabela procurement.
import { getDb } from './db';
import type { Snapshot } from '../core/types';
import { HttpError, getProject, getCatalog, getMaterials } from './repo';
import { listCustomItems } from './custom-items';
import { procurementLines, procurementRows, bySupplier, procurementSummary, checkOrderState, isLineKey, type OrderState } from '../core/procurement';

async function states(projectId: string): Promise<Record<string, OrderState>> {
  const { q } = await getDb(), { rows } = await q('select key, state from procurement where project_id=$1', [projectId]);
  return Object.fromEntries(rows.map(r => [r.key, r.state as OrderState]));
}
/** Liniile cu stare ale unui proiect deja verificat (proprietar sau link de client valid). */
export async function projectRows(id: string, draft: Snapshot, today = new Date().toISOString().slice(0, 10), startDate?: string){
  const [cat, mc] = await Promise.all([getCatalog(), getMaterials()]);
  return procurementRows(procurementLines(draft, cat, mc, await listCustomItems(id)), await states(id), today, startDate);
}
/** Când s-a schimbat ultima dată starea unei comenzi din proiect. */
export async function ordersUpdatedAt(id: string): Promise<string | null> {
  const { q } = await getDb(), { rows } = await q('select max(updated_at) as t from procurement where project_id=$1', [id]);
  return rows[0]?.t ? new Date(rows[0].t).toISOString() : null;
}
export async function getOrders(owner: string, id: string, today = new Date().toISOString().slice(0, 10)){
  const p = await getProject(owner, id), rows = await projectRows(id, p.draft, today, p.meta.startDate);
  return { rows, groups: bySupplier(rows), summary: procurementSummary(rows) };
}
export async function setOrderState(owner: string, id: string, key: unknown, state: unknown){
  const p = await getProject(owner, id); if (!isLineKey(key)) throw new HttpError(400, 'Linie necunoscută.');
  // doar liniile care există acum în proiect (tabela nu crește cu chei inventate)
  const [cat, mc] = await Promise.all([getCatalog(), getMaterials()]); if (!procurementLines(p.draft, cat, mc, await listCustomItems(id)).some(l => l.key === key)) throw new HttpError(400, 'Linie necunoscută.');
  let s: OrderState; try { s = checkOrderState(state); } catch (e: any){ throw new HttpError(400, e.message); }
  const { q } = await getDb();
  await q(`insert into procurement(project_id, key, state) values($1,$2,$3) on conflict(project_id, key) do update set state=excluded.state, updated_at=now()`, [id, key, JSON.stringify(s)]);
  return s;
}
