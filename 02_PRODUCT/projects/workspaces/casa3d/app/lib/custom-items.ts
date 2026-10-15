// Produsele adăugate de designer în comenzi, în afara catalogului (din linkul unui magazin sau scrise de mână).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { floors } from '../core/levels';
import { checkCustomItem, ClipError, type CustomItemInput } from '../core/clip';
import { parseCustomCsv, customItemsCsv, customBudget, normText, CSV_LIMITS } from '../core/custom-csv';

export interface CustomItem extends CustomItemInput { id: string; createdAt: string }
export const CUSTOM_LIMITS = { perProject: 1000 };
const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const n = (v: any) => v == null ? null : Number(v);
const row = (r: any): CustomItem => ({ id: r.id, name: r.name, supplier: r.supplier, url: r.url, image: r.image, sku: r.sku, price: n(r.price), currency: r.currency, qty: Number(r.qty), unit: r.unit, roomId: r.room_id, costPrice: n(r.cost_price), leadDays: r.lead_days == null ? null : Number(r.lead_days), dimension: r.dimension, createdAt: new Date(r.created_at).toISOString() });
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
    const { rows } = await q('insert into custom_items(project_id, name, supplier, url, image, sku, price, currency, qty, unit, room_id, cost_price, lead_days, dimension) values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14) returning *',
      [projectId, x.name, x.supplier, x.url, x.image, x.sku, x.price, x.currency, x.qty, x.unit, x.roomId, x.costPrice, x.leadDays, x.dimension]);
    return row(rows[0]); });
}
const COLS: Record<keyof CustomItemInput, string> = { name: 'name', supplier: 'supplier', url: 'url', image: 'image', sku: 'sku', price: 'price', currency: 'currency', qty: 'qty', unit: 'unit', roomId: 'room_id', costPrice: 'cost_price', leadDays: 'lead_days', dimension: 'dimension' };
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

const roomsOf = (draft: any) => floors(draft).flatMap(f => f.rooms.map(r => ({ id: r.id as string, name: r.name as string })));
const csvErr = (fn: () => any) => { try { return fn(); } catch (e: any){ if (e instanceof ClipError) throw new HttpError(e.message.includes('KB') ? 413 : 400, e.message); throw e; } };
/** Listă + totaluri pentru buget (proprietar). */
export async function getCustomItems(owner: string, projectId: string){ await getProject(owner, projectId); const items = await listCustomItems(projectId); return { items, budget: customBudget(items) }; }
/** Importul CSV: validarea e aici, pe server; cu `dryRun` doar previzualizare. Un rând greșit oprește tot importul (nimic nu se scrie pe jumătate). */
export async function importCustomItems(owner: string, projectId: string, body: unknown){
  const p = await getProject(owner, projectId), b = body as any;
  if (!b || typeof b !== 'object' || typeof b.csv !== 'string') throw new HttpError(400, 'Lipsește conținutul CSV.');
  const rooms = roomsOf(p.draft), byName = new Map(rooms.map(r => [normText(r.name), r.id])), ids = new Set(rooms.map(r => r.id));
  const parsed = csvErr(() => parseCustomCsv(b.csv, name => byName.get(normText(name)) ?? (ids.has(name) ? name : null)));
  const preview = parsed.rows.map((r: any) => ({ line: r.line, ok: !!r.item, errors: r.errors, item: r.item }));
  if (b.dryRun === true) return { dryRun: true, ok: parsed.ok, failed: parsed.failed, rows: preview };
  if (parsed.failed) throw new HttpError(422, `${parsed.failed} rânduri au erori; corectează-le și încearcă din nou.`, { rows: preview.filter((r: any) => !r.ok) });
  if (!parsed.ok) throw new HttpError(400, 'Niciun rând de importat.');
  const { tx } = await getDb();
  return tx(async q => {
    await q('select id from projects where id=$1 for update', [projectId]);
    const { rows: [c] } = await q('select count(*)::int as n from custom_items where project_id=$1', [projectId]);
    if (c.n + parsed.ok > CUSTOM_LIMITS.perProject) throw new HttpError(429, 'Prea multe produse adăugate în proiect.');
    for (const r of parsed.rows){ const x = r.item!;
      await q('insert into custom_items(project_id, name, supplier, url, image, sku, price, currency, qty, unit, room_id, cost_price, lead_days, dimension) values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14)',
        [projectId, x.name, x.supplier, x.url, x.image, x.sku, x.price, x.currency, x.qty, x.unit, x.roomId, x.costPrice, x.leadDays, x.dimension]); }
    return { dryRun: false, imported: parsed.ok };
  });
}
export async function exportCustomItemsCsv(owner: string, projectId: string): Promise<string> {
  const p = await getProject(owner, projectId), names = new Map(roomsOf(p.draft).map(r => [r.id, r.name]));
  return customItemsCsv((await listCustomItems(projectId)).map(i => ({ ...i, roomName: i.roomId ? names.get(i.roomId) ?? null : null })));
}
export { CSV_LIMITS };
