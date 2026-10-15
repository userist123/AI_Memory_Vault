// Partajare read-only a unei revizii, prin token imposibil de ghicit. Fără cookie de proprietar la citire, fără editare.
import { randomBytes } from 'node:crypto';
import { getDb } from './db';
import { HttpError, getProject, createRevision } from './repo';
import type { Snapshot } from '../core/types';

export const TOKEN_BYTES = 32;
const TOKEN_RE = /^[A-Za-z0-9_-]{43}$/;
export const isShareToken = (t: unknown): t is string => typeof t === 'string' && TOKEN_RE.test(t);

export async function createShare(owner: string, id: string, revisionNumber: unknown){
  const n = Number(revisionNumber); if (!Number.isInteger(n) || n < 1) throw new HttpError(400, 'Revizie invalidă.');
  await getProject(owner, id); const { q } = await getDb();
  const rev = await q('select number from revisions where project_id=$1 and number=$2', [id, n]); if (!rev.rows[0]) throw new HttpError(404, 'Revizie inexistentă.');
  const token = randomBytes(TOKEN_BYTES).toString('base64url');
  const { rows } = await q('insert into shares(token, project_id, revision_number) values($1,$2,$3) returning created_at', [token, id, n]);
  return { token, revisionNumber: n, createdAt: rows[0].created_at, path: `/share/${token}` };
}
export async function listShares(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb();
  const { rows } = await q('select token, revision_number, created_at, revoked_at from shares where project_id=$1 order by created_at desc', [id]);
  return rows.map(r => ({ token: r.token, revisionNumber: r.revision_number, createdAt: r.created_at, revokedAt: r.revoked_at, path: `/share/${r.token}` }));
}
export async function revokeShare(owner: string, id: string, token: unknown){
  await getProject(owner, id); if (typeof token !== 'string' || !TOKEN_RE.test(token)) throw new HttpError(404, 'Link inexistent.');
  const { q } = await getDb(); const { rows } = await q('update shares set revoked_at = coalesce(revoked_at, now()) where token=$1 and project_id=$2 returning revoked_at', [token, id]);
  if (!rows[0]) throw new HttpError(404, 'Link inexistent.'); return { ok: true, revokedAt: rows[0].revoked_at };
}
export interface SharedView { projectName: string; revisionNumber: number; note: string; createdAt: string; snapshot: Snapshot; readOnly: true }
/** Public: un token necunoscut, revocat sau fără revizie dă același 404, fără alte detalii. */
export async function resolveShare(token: unknown): Promise<SharedView> {
  if (typeof token !== 'string' || !TOKEN_RE.test(token)) throw new HttpError(404, 'Link inexistent.');
  const { q } = await getDb();
  const { rows } = await q(`select r.number, r.note, r.created_at, r.snapshot from shares s
    join revisions r on r.project_id = s.project_id and r.number = s.revision_number where s.token=$1 and s.revoked_at is null`, [token]);
  if (!rows[0]) throw new HttpError(404, 'Link inexistent.');
  // Numele vine din revizia partajată: o redenumire ulterioară a proiectului nu apare pe linkurile vechi.
  return { projectName: String(rows[0].snapshot?.name ?? ''), revisionNumber: rows[0].number, note: rows[0].note, createdAt: rows[0].created_at, snapshot: rows[0].snapshot as Snapshot, readOnly: true };
}

/** Adresa publică a aplicației (CASA3D_PUBLIC_URL, ex. tunelul spre acest calculator); fără ea linkul folosește adresa din browser. */
export function publicBase(){ const u = process.env.CASA3D_PUBLIC_URL; if (!u) return null;
  // doar o origine curată: http(s), fără utilizator, cale, interogare sau fragment
  try { const x = new URL(u); return /^https?:$/.test(x.protocol) && !x.username && !x.password && (x.pathname === '/' || x.pathname === '') && !x.search && !x.hash && !/[?#]$/.test(u) ? x.origin : null; } catch { return null; } }
/** Un singur buton pentru designer: dacă proiectul s-a schimbat de la ultima revizie, se salvează una nouă („Predat clientului”);
 *  linkul activ spre ultima revizie se refolosește, altfel se creează unul. */
export async function clientLink(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb();
  const last = await q('select r.number, (r.snapshot = p.draft) as same from revisions r join projects p on p.id = r.project_id where r.project_id=$1 order by r.number desc limit 1', [id]);
  const same = !!last.rows[0]?.same;
  const number = same ? last.rows[0].number as number : (await createRevision(owner, id, 'Predat clientului')).number;
  const old = await q('select token from shares where project_id=$1 and revision_number=$2 and revoked_at is null order by created_at desc limit 1', [id, number]);
  const s = old.rows[0] ? { token: old.rows[0].token as string, path: `/share/${old.rows[0].token}` } : await createShare(owner, id, number);
  const base = publicBase();
  // publicUrl: false = linkul folosește adresa din browser (pe localhost merge doar pe acest calculator); revisionNote: ce s-a salvat
  return { token: s.token, path: s.path, revisionNumber: number, newRevision: !same, url: base ? base + s.path : null, publicUrl: !!base,
    revisionNote: same ? null : `Revizia ${number} — „Predat clientului”` };
}
