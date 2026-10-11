// Șantierul unui proiect: remedieri și pozele lor, doar pentru proprietar. Pozele stau în baza de date (bytea), cu tipul
// verificat după conținut, mărime și număr limitate; se servesc doar proprietarului, fără ghicirea tipului de browser.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { floors } from '../core/levels';
import { checkSnag, sniffImage, isUuid, SITE_LIMITS, SiteError, type Snag } from '../core/site';

const roomsOf = async (owner: string, id: string) => new Set(floors((await getProject(owner, id)).draft).flatMap(f => f.rooms).map(r => r.id));
const row = (r: any): Snag => ({ id: r.id, number: r.number, title: r.title, body: r.body, roomId: r.room_id, trade: r.trade, status: r.status, photos: r.photos,
  createdAt: new Date(r.created_at).toISOString(), updatedAt: new Date(r.updated_at).toISOString(), resolvedAt: r.resolved_at ? new Date(r.resolved_at).toISOString() : null });
const v = <T>(fn: () => T) => { try { return fn(); } catch (e: any){ if (e instanceof SiteError) throw new HttpError(400, e.message); throw e; } };

async function ownPhotos(projectId: string, ids: string[]){ if (!ids.length) return;
  const { q } = await getDb(), { rows } = await q('select id from site_photos where project_id=$1 and id = any($2::uuid[])', [projectId, ids]);
  if (rows.length !== ids.length) throw new HttpError(400, 'O poză nu aparține proiectului.'); }

export async function listSnags(owner: string, id: string): Promise<Snag[]> {
  await getProject(owner, id); const { q } = await getDb(); return (await q('select * from snags where project_id=$1 order by number', [id])).rows.map(row);
}
export async function addSnag(owner: string, id: string, input: unknown): Promise<Snag> {
  const rooms = await roomsOf(owner, id), s = v(() => checkSnag(input, rooms)); await ownPhotos(id, s.photos ?? []);
  const { tx } = await getDb();
  return tx(async q => {
    // contorul proiectului (rândul blocat de update): numerele nu se refolosesc nici după ștergerea ultimei remedieri
    const { rows: [c] } = await q('select count(*)::int as n from snags where project_id=$1', [id]);
    if (c.n >= SITE_LIMITS.snagsPerProject) throw new HttpError(429, 'Prea multe remedieri în proiect.');
    const { rows: [seq] } = await q('update projects set snag_seq = greatest(snag_seq, (select coalesce(max(number),0) from snags where project_id=$1)) + 1 where id=$1 returning snag_seq', [id]);
    const { rows } = await q(`insert into snags(project_id, number, title, body, room_id, trade, status, photos, resolved_at) values($1,$2,$3,$4,$5,$6,$7,$8, case when $7 = 'open' then null else now() end) returning *`,
      [id, seq.snag_seq, s.title, s.body ?? '', s.roomId ?? null, s.trade, s.status ?? 'open', JSON.stringify(s.photos ?? [])]);
    return row(rows[0]); });
}
export async function updateSnag(owner: string, id: string, snagId: unknown, patch: unknown): Promise<Snag> {
  if (!isUuid(snagId)) throw new HttpError(404, 'Remediere inexistentă.');
  const rooms = await roomsOf(owner, id), s = v(() => checkSnag(patch, rooms, true)); if (s.photos) await ownPhotos(id, s.photos);
  const { q } = await getDb(), sets: string[] = [], vals: unknown[] = [snagId, id];
  const put = (col: string, val: unknown) => { vals.push(val); sets.push(`${col}=$${vals.length}`); };
  if (s.title !== undefined) put('title', s.title); if (s.body !== undefined) put('body', s.body); if (s.roomId !== undefined) put('room_id', s.roomId);
  if (s.trade !== undefined) put('trade', s.trade); if (s.photos !== undefined) put('photos', JSON.stringify(s.photos));
  // data rezolvării: se pune la ieșirea din „deschis”, se șterge la redeschidere, rămâne la „rezolvat” → „verificat”
  if (s.status !== undefined){ put('status', s.status); sets.push(`resolved_at = case when $${vals.length} = 'open' then null when status = 'open' then now() else resolved_at end`); }
  if (!sets.length) throw new HttpError(400, 'Nimic de modificat.');
  // resolved_at se calculează față de starea veche (status din partea dreaptă e valoarea de dinainte de update)
  const { rows } = await q(`update snags set ${sets.join(', ')}, updated_at=now() where id=$1 and project_id=$2 returning *`, vals);
  if (!rows[0]) throw new HttpError(404, 'Remediere inexistentă.'); if (s.photos) await dropOrphans(id);
  return row(rows[0]);
}
/** Pozele care nu mai aparțin niciunei remedieri: scoase dintr-o remediere imediat, cele neatașate după o oră (formular abandonat). */
async function dropOrphans(projectId: string, olderThanMinutes = 0){
  const { q } = await getDb();
  await q(`delete from site_photos p where p.project_id=$1 and p.created_at < now() - ($2 || ' minutes')::interval and not exists (select 1 from snags s where s.project_id=$1 and s.photos ? p.id::text)`, [projectId, String(olderThanMinutes)]);
}
export async function deleteSnag(owner: string, id: string, snagId: unknown){
  if (!isUuid(snagId)) throw new HttpError(404, 'Remediere inexistentă.'); await getProject(owner, id);
  const { q } = await getDb(), { rows } = await q('delete from snags where id=$1 and project_id=$2 returning photos', [snagId, id]);
  if (!rows[0]) throw new HttpError(404, 'Remediere inexistentă.');
  const ph: string[] = rows[0].photos ?? []; if (ph.length) await q(`delete from site_photos p where p.project_id=$1 and p.id = any($2::uuid[]) and not exists (select 1 from snags s where s.project_id=$1 and s.photos ? p.id::text)`, [id, ph]);
  return { ok: true };
}
export async function addPhoto(owner: string, id: string, bytes: Uint8Array){
  await getProject(owner, id);
  if (bytes.length > SITE_LIMITS.photoBytes) throw new HttpError(413, 'Poza e prea mare.');
  const mime = sniffImage(bytes); if (!mime) throw new HttpError(415, 'Doar poze JPEG, PNG sau WebP.');
  await dropOrphans(id, 60);
  const { tx } = await getDb();
  return tx(async q => { await q('select id from projects where id=$1 for update', [id]);   // limita nu poate fi depășită de încărcări simultane
    const { rows: [c] } = await q('select count(*)::int as n from site_photos where project_id=$1', [id]);
    if (c.n >= SITE_LIMITS.photosPerProject) throw new HttpError(429, 'Prea multe poze în proiect.');
    const { rows } = await q('insert into site_photos(project_id, mime, data, bytes) values($1,$2,$3,$4) returning id', [id, mime, Buffer.from(bytes), bytes.length]);
    return { id: rows[0].id as string, mime }; });
}
export async function readPhoto(owner: string, id: string, photoId: unknown){
  if (!isUuid(photoId)) throw new HttpError(404, 'Poză inexistentă.'); await getProject(owner, id);
  const { q } = await getDb(), { rows } = await q('select mime, data from site_photos where id=$1 and project_id=$2', [photoId, id]);
  if (!rows[0]) throw new HttpError(404, 'Poză inexistentă.'); return { mime: rows[0].mime as string, data: new Uint8Array(rows[0].data) };
}
