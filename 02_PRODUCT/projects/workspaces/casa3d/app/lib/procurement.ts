// Comenzile unui proiect: liniile se calculează din plan (core/procurement.ts), starea fiecăreia stă în tabela procurement.
import { getDb } from './db';
import { HttpError, getProject, getCatalog, getMaterials } from './repo';
import { procurementLines, procurementRows, bySupplier, procurementSummary, checkOrderState, isLineKey, type OrderState } from '../core/procurement';

async function states(projectId: string): Promise<Record<string, OrderState>> {
  const { q } = await getDb(), { rows } = await q('select key, state from procurement where project_id=$1', [projectId]);
  return Object.fromEntries(rows.map(r => [r.key, r.state as OrderState]));
}
export async function getOrders(owner: string, id: string, today = new Date().toISOString().slice(0, 10)){
  const p = await getProject(owner, id), [cat, mc] = await Promise.all([getCatalog(), getMaterials()]);
  const rows = procurementRows(procurementLines(p.draft, cat, mc), await states(id), today);
  return { rows, groups: bySupplier(rows), summary: procurementSummary(rows) };
}
export async function setOrderState(owner: string, id: string, key: unknown, state: unknown){
  const p = await getProject(owner, id); if (!isLineKey(key)) throw new HttpError(400, 'Linie necunoscută.');
  // doar liniile care există acum în proiect (tabela nu crește cu chei inventate)
  const [cat, mc] = await Promise.all([getCatalog(), getMaterials()]); if (!procurementLines(p.draft, cat, mc).some(l => l.key === key)) throw new HttpError(400, 'Linie necunoscută.');
  let s: OrderState; try { s = checkOrderState(state); } catch (e: any){ throw new HttpError(400, e.message); }
  const { q } = await getDb();
  await q(`insert into procurement(project_id, key, state) values($1,$2,$3) on conflict(project_id, key) do update set state=excluded.state, updated_at=now()`, [id, key, JSON.stringify(s)]);
  return s;
}
