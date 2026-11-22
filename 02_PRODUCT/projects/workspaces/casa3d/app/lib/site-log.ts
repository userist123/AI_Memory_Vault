// Jurnalul zilnic de șantier al unui proiect. Scrierea și lista completă sunt doar ale proprietarului; pe linkul clientului
// ajung doar intrările marcate vizibile, fără câmpurile interne, iar o poză se servește pe link doar dacă aparține unei
// intrări vizibile a proiectului acelui link. Pozele folosesc aceeași stocare ca remedierile (site_photos, lib/site.ts).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { isShareToken, shareActive } from './share';
import { ownPhotos, dropPhotosIfUnused } from './site';
import { isUuid } from '../core/site';
import { checkLogEntry, LOG_LIMITS, SiteLogError, type SiteLogEntry, type ClientLogEntry } from '../core/site-log';

const COLS = `id, to_char(date, 'YYYY-MM-DD') as day, text, weather, workers, trades, photos, client_visible, created_at`;
const row = (r: any): SiteLogEntry => ({ id: r.id, date: r.day, text: r.text, weather: r.weather, workers: r.workers ?? null, trades: r.trades ?? [], photos: r.photos ?? [],
  clientVisible: r.client_visible === true, createdAt: new Date(r.created_at).toISOString() });
const v = <T>(fn: () => T) => { try { return fn(); } catch (e: any){ if (e instanceof SiteLogError) throw new HttpError(400, e.message); throw e; } };
const missing = () => new HttpError(404, 'Intrare de jurnal inexistentă.');

export async function listSiteLog(owner: string, id: string): Promise<SiteLogEntry[]> {
  await getProject(owner, id); const { q } = await getDb();
  return (await q(`select ${COLS} from site_log where project_id=$1 order by date desc, created_at desc`, [id])).rows.map(row);
}
export async function addSiteLog(owner: string, id: string, input: unknown): Promise<SiteLogEntry> {
  await getProject(owner, id); const e = v(() => checkLogEntry(input)); await ownPhotos(id, e.photos ?? []);
  const { tx } = await getDb();
  return tx(async q => { await q('select id from projects where id=$1 for update', [id]);   // limita nu poate fi depășită de scrieri simultane
    const { rows: [c] } = await q('select count(*)::int as n from site_log where project_id=$1', [id]);
    if (c.n >= LOG_LIMITS.entriesPerProject) throw new HttpError(429, 'Prea multe intrări în jurnal.');
    const { rows } = await q(`insert into site_log(project_id, date, text, weather, workers, trades, photos, client_visible) values($1,$2,$3,$4,$5,$6,$7,$8) returning ${COLS}`,
      [id, e.date, e.text, e.weather ?? '', e.workers ?? null, JSON.stringify(e.trades ?? []), JSON.stringify(e.photos ?? []), e.clientVisible === true]);
    return row(rows[0]); });
}
/** Modificare parțială: se schimbă doar câmpurile trimise (comutatorul de vizibilitate trimite doar { clientVisible }). */
export async function updateSiteLog(owner: string, id: string, entryId: unknown, patch: unknown): Promise<SiteLogEntry> {
  if (!isUuid(entryId)) throw missing(); await getProject(owner, id);
  const e = v(() => checkLogEntry(patch, true)); if (e.photos) await ownPhotos(id, e.photos);
  const { q } = await getDb(), sets: string[] = [], vals: unknown[] = [entryId, id];
  const put = (col: string, val: unknown) => { vals.push(val); sets.push(`${col}=$${vals.length}`); };
  if (e.date !== undefined) put('date', e.date); if (e.text !== undefined) put('text', e.text); if (e.weather !== undefined) put('weather', e.weather);
  if (e.workers !== undefined) put('workers', e.workers); if (e.trades !== undefined) put('trades', JSON.stringify(e.trades));
  if (e.photos !== undefined) put('photos', JSON.stringify(e.photos)); if (e.clientVisible !== undefined) put('client_visible', e.clientVisible);
  if (!sets.length) throw new HttpError(400, 'Nimic de modificat.');
  const old = (await q('select photos from site_log where id=$1 and project_id=$2', [entryId, id])).rows[0]; if (!old) throw missing();
  const { rows } = await q(`update site_log set ${sets.join(', ')}, updated_at=now() where id=$1 and project_id=$2 returning ${COLS}`, vals);
  if (!rows[0]) throw missing();
  // pozele scoase din intrare se șterg, dacă nu le mai folosește nimic
  if (e.photos) await dropPhotosIfUnused(id, ((old.photos ?? []) as string[]).filter(p => !e.photos!.includes(p)));
  return row(rows[0]);
}
export async function deleteSiteLog(owner: string, id: string, entryId: unknown){
  if (!isUuid(entryId)) throw missing(); await getProject(owner, id);
  const { q } = await getDb(), { rows } = await q('delete from site_log where id=$1 and project_id=$2 returning photos', [entryId, id]);
  if (!rows[0]) throw missing(); await dropPhotosIfUnused(id, rows[0].photos ?? []);
  return { ok: true };
}

// ---------- linkul clientului ----------
/** Public, doar cu tokenul unui link activ (revocat, expirat sau necunoscut → 404): intrările vizibile, fără numărul de
 *  oameni, fără meserii și fără starea de vizibilitate. Câmpurile interne nu sunt nici măcar citite din bază. */
export async function siteLogForShare(token: unknown): Promise<ClientLogEntry[]> {
  if (!isShareToken(token)) throw new HttpError(404, 'Link inexistent.');
  const { q } = await getDb(), { rows: [s] } = await q(`select s.project_id from shares s join projects p on p.id = s.project_id where s.token=$1 and ${shareActive('s')} and p.deleted_at is null`, [token]);
  if (!s) throw new HttpError(404, 'Link inexistent.');
  const { rows } = await q(`select id, to_char(date, 'YYYY-MM-DD') as day, text, weather, photos from site_log where project_id=$1 and client_visible = true order by date desc, created_at desc`, [s.project_id]);
  return rows.map(r => ({ id: r.id as string, date: r.day as string, text: r.text as string, weather: r.weather as string, photos: (r.photos ?? []) as string[] }));
}
/** O poză pentru link: doar dacă linkul e activ și poza e atașată unei intrări VIZIBILE a proiectului acelui link.
 *  Orice altceva (poză de remediere, poză a unei intrări interne, poza altui proiect, link revocat) → null (404). */
export async function shareLogPhoto(token: unknown, photoId: unknown): Promise<{ mime: string; data: Uint8Array } | null> {
  if (!isShareToken(token) || !isUuid(photoId)) return null; const { q } = await getDb();
  const { rows } = await q(`select ph.mime, ph.data from shares s join projects p on p.id = s.project_id join site_photos ph on ph.project_id = s.project_id
    where s.token=$1 and ${shareActive('s')} and p.deleted_at is null and ph.id=$2
      and exists (select 1 from site_log l where l.project_id = s.project_id and l.client_visible = true and l.photos ? ph.id::text)`, [token, photoId]);
  return rows[0] ? { mime: String(rows[0].mime), data: new Uint8Array(rows[0].data) } : null;
}
