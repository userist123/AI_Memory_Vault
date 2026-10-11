// Produsele adăugate de designer în comenzi, în afara catalogului (din linkul unui magazin sau scrise de mână).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { floors } from '../core/levels';
import { checkCustomItem, ClipError, type CustomItemInput } from '../core/clip';

export interface CustomItem extends CustomItemInput { id: string; createdAt: string }
export const CUSTOM_LIMITS = { perProject: 300 };
const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const n = (v: any) => v == null ? null : Number(v);
const row = (r: any): CustomItem => ({ id: r.id, name: r.name, supplier: r.supplier, url: r.url, image: r.image, sku: r.sku, price: n(r.price), currency: r.currency, qty: Number(r.qty), unit: r.unit, roomId: r.room_id, createdAt: new Date(r.created_at).toISOString() });
async function owned(owner: string, projectId: string, roomId: string | null | undefined){
  const p = await getProject(owner, projectId);
  if (roomId && !floors(p.draft).some(f => f.rooms.some(r => r.id === roomId))) throw new HttpError(400, 'Camera nu există în proiect.');
}
const v = <T>(fn: () => T) => { try { return fn(); } catch (e: any){ if (e instanceof ClipError) throw new HttpError(400, e.message); throw e; } };

export async function listCustomItems(projectId: string): Promise<CustomItem[]> {
  const { q } = await getDb(), { rows } = await q('select * from custom_items where project_id=$1 order by created_at', [projectId]); return rows.map(row);
}
export async function addCustomItem(owner: string, projectId: string, input: unknown): Promise<CustomItem> {
  const x = v(() => checkCustomItem(input)) as CustomItemInput; await owned(owner, projectId, x.roomId);
  const { tx } = await getDb();
  return tx(async q => {
    await q('select id from projects where id=$1 for update', [projectId]);       // numărătoarea și inserarea nu se intercalează
    const { rows: [c] } = await q('select count(*)::int as n from custom_items where project_id=$1', [projectId]);
    if (c.n >= CUSTOM_LIMITS.perProject) throw new HttpError(429, 'Prea multe produse adăugate în proiect.');
    const { rows } = await q('insert into custom_items(project_id, name, supplier, url, image, sku, price, currency, qty, unit, room_id) values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) returning *',
      [projectId, x.name, x.supplier, x.url, x.image, x.sku, x.price, x.currency, x.qty, x.unit, x.roomId]);
    return row(rows[0]); });
}
const COLS: Record<keyof CustomItemInput, string> = { name: 'name', supplier: 'supplier', url: 'url', image: 'image', sku: 'sku', price: 'price', currency: 'currency', qty: 'qty', unit: 'unit', roomId: 'room_id' };
export async function updateCustomItem(owner: string, projectId: string, id: unknown, patch: unknown): Promise<CustomItem> {
  if (typeof id !== 'string' || !ID_RE.test(id)) { await getProject(owner, projectId); throw new HttpError(404, 'Produs inexistent.'); }
  const x = v(() => checkCustomItem(patch, true)); await owned(owner, projectId, x.roomId); const vals: unknown[] = [id, projectId], sets: string[] = [];
  for (const [k, val] of Object.entries(x)){ vals.push(val); sets.push(`${COLS[k as keyof CustomItemInput]}=$${vals.length}`); }
  if (!sets.length) throw new HttpError(400, 'Nimic de modificat.');
  const { q } = await getDb(), { rows } = await q(`update custom_items set ${sets.join(', ')} where id=$1 and project_id=$2 returning *`, vals);
  if (!rows[0]) throw new HttpError(404, 'Produs inexistent.'); return row(rows[0]);
}
export async function deleteCustomItem(owner: string, projectId: string, id: unknown){
  await getProject(owner, projectId); if (typeof id !== 'string' || !ID_RE.test(id)) throw new HttpError(404, 'Produs inexistent.');
  const { tx } = await getDb();
  return tx(async q => {
    const { rows } = await q('delete from custom_items where id=$1 and project_id=$2 returning id', [id, projectId]);
    if (!rows[0]) throw new HttpError(404, 'Produs inexistent.');
    await q('delete from procurement where project_id=$1 and key=$2', [projectId, 'c|' + id]);   // starea comenzii pleacă odată cu produsul
    return { ok: true }; });
}
