// Coșul de proiecte: ștergerea mută proiectul în coș pentru 30 de zile (linkurile clientului se opresc imediat), de unde se
// restaurează sau se șterge definitiv (cu numele tastat). După 30 de zile se șterge singur, cu tot cu randările de pe disc.
// Duplicarea copiază planul și designul, nu datele clientului, linkurile, comenzile sau randările.
import { getDb } from './db';
import { HttpError, checkSnapshot } from './repo';
import { removeRenders } from './renders';
import { demoBeforeCreate } from './demo';

export const TRASH_DAYS = 30;
const ID = /^[0-9a-f-]{36}$/i;
const purgeAt = (d: Date | string) => new Date(new Date(d).getTime() + TRASH_DAYS * 864e5).toISOString();

export async function moveToTrash(owner: string, id: string){
  if (!ID.test(id)) throw new HttpError(404, 'Proiect inexistent.');
  const { tx } = await getDb();
  return tx(async q => {
    const { rows } = await q('update projects set deleted_at=now() where id=$1 and owner=$2 and deleted_at is null returning deleted_at', [id, owner]);
    if (!rows[0]) throw new HttpError(404, 'Proiect inexistent.');
    const rv = await q('update shares set revoked_at=now() where project_id=$1 and revoked_at is null', [id]);   // clientul nu mai vede nimic
    return { ok: true, purgeAt: purgeAt(rows[0].deleted_at), revokedLinks: (rv as any).affectedRows ?? (rv as any).rowCount ?? 0 };
  });
}
export async function restoreFromTrash(owner: string, id: string){
  if (!ID.test(id)) throw new HttpError(404, 'Proiect inexistent.');
  const { q } = await getDb(), { rows } = await q('update projects set deleted_at=null, updated_at=now() where id=$1 and owner=$2 and deleted_at is not null returning id', [id, owner]);
  if (!rows[0]) throw new HttpError(404, 'Proiectul nu e în coș.'); return { ok: true };
}
/** Ștergerea definitivă: doar din coș și doar cu numele proiectului tastat exact. */
export async function purgeProject(owner: string, id: string, typedName: unknown){
  if (!ID.test(id)) throw new HttpError(404, 'Proiect inexistent.');
  const { q } = await getDb(), { rows } = await q('select name from projects where id=$1 and owner=$2 and deleted_at is not null', [id, owner]);
  if (!rows[0]) throw new HttpError(404, 'Proiectul nu e în coș.');
  if (typeof typedName !== 'string' || typedName.trim() !== String(rows[0].name).trim()) throw new HttpError(400, 'Scrie exact numele proiectului ca să-l ștergi definitiv.');
  await q('delete from projects where id=$1 and owner=$2', [id, owner]); await removeRenders(id); return { ok: true };
}
/** Golește ce a stat în coș mai mult de 30 de zile (rulat la fiecare deschidere a listei). */
export async function purgeExpired(owner: string, now = new Date()){
  const { q } = await getDb(), cutoff = new Date(now.getTime() - TRASH_DAYS * 864e5).toISOString();
  const { rows } = await q('delete from projects where owner=$1 and deleted_at is not null and deleted_at < $2 returning id', [owner, cutoff]);
  for (const r of rows) await removeRenders(r.id); return rows.length;
}
export async function listTrash(owner: string){
  await purgeExpired(owner); const { q } = await getDb();
  const { rows } = await q('select id, name, deleted_at from projects where owner=$1 and deleted_at is not null order by deleted_at desc', [owner]);
  return rows.map(r => ({ id: r.id as string, name: r.name as string, deletedAt: new Date(r.deleted_at).toISOString(), purgeAt: purgeAt(r.deleted_at) }));
}
/** Copie de lucru: planul, finisajele și mobilierul; fără client, onorariu, linkuri, revizii, comenzi sau randări. */
export async function duplicateProject(owner: string, id: string, name?: unknown){
  if (!ID.test(id)) throw new HttpError(404, 'Proiect inexistent.');
  const { q } = await getDb(), { rows } = await q('select name, draft from projects where id=$1 and owner=$2 and deleted_at is null', [id, owner]);
  if (!rows[0]) throw new HttpError(404, 'Proiect inexistent.');
  const n = (typeof name === 'string' && name.trim() ? name.trim() : `${rows[0].name} (copie)`).slice(0, 120);
  const snap = checkSnapshot({ ...(rows[0].draft as any), name: n }); delete (snap as any).brief;
  await demoBeforeCreate(q, owner);
  const ins = await q('insert into projects(owner, name, draft) values($1,$2,$3) returning id', [owner, n, JSON.stringify(snap)]);
  return { id: ins.rows[0].id as string };
}
