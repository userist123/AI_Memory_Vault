// Partajare read-only a unei revizii, prin token imposibil de ghicit. Fără cookie de proprietar la citire, fără editare.
import { randomBytes } from 'node:crypto';
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import type { Snapshot } from '../core/types';

export const TOKEN_BYTES = 32;
const TOKEN_RE = /^[A-Za-z0-9_-]{43}$/;

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
  const { rows } = await q(`select p.name, r.number, r.note, r.created_at, r.snapshot from shares s join projects p on p.id = s.project_id
    join revisions r on r.project_id = s.project_id and r.number = s.revision_number where s.token=$1 and s.revoked_at is null`, [token]);
  if (!rows[0]) throw new HttpError(404, 'Link inexistent.');
  return { projectName: rows[0].name, revisionNumber: rows[0].number, note: rows[0].note, createdAt: rows[0].created_at, snapshot: rows[0].snapshot as Snapshot, readOnly: true };
}
