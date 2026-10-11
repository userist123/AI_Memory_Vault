// Părerile clientului pe linkul de prezentare: pe o cameră sau pe tot proiectul („îmi place”, „aș schimba”, „am o întrebare”).
// Scrierea e publică (doar cu tokenul unui link activ), deci totul e validat strict și limitat pe link; citirea completă
// și bifarea „rezolvat” sunt doar ale proprietarului proiectului.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { resolveShare } from './share';

export const FEEDBACK_VERDICTS = ['like', 'change', 'question'] as const;
export type FeedbackVerdict = typeof FEEDBACK_VERDICTS[number];
export const FEEDBACK_LIMITS = { author: 60, text: 2000, perShare: 200 };
export interface Feedback { id: string; roomId: string | null; verdict: FeedbackVerdict; author: string; text: string; createdAt: string; resolvedAt: string | null }

const ID_RE = /^[0-9a-f-]{36}$/;
// fără caractere de control (în afară de rând nou), spații de la capete tăiate
const clean = (v: unknown) => typeof v === 'string' ? v.replace(/[\u0000-\u0009\u000b-\u001f\u007f]/g, '').trim() : null;
const row = (r: any): Feedback => ({ id: r.id, roomId: r.room_id, verdict: r.verdict, author: r.author, text: r.body, createdAt: new Date(r.created_at).toISOString(), resolvedAt: r.resolved_at ? new Date(r.resolved_at).toISOString() : null });

export async function addFeedback(token: unknown, input: unknown): Promise<Feedback> {
  const view = await resolveShare(token);                                 // link necunoscut sau revocat → 404
  const x = input as any; if (!x || typeof x !== 'object') throw new HttpError(400, 'Părerea nu e validă.');
  const verdict = x.verdict, author = clean(x.author), text = clean(x.text ?? ''), roomId = x.roomId ?? null;
  if (!(FEEDBACK_VERDICTS as readonly string[]).includes(verdict)) throw new HttpError(400, 'Alege: îmi place, aș schimba sau am o întrebare.');
  if (!author || author.length > FEEDBACK_LIMITS.author) throw new HttpError(400, 'Scrie-ți numele (cel mult 60 de caractere).');
  if (text == null || text.length > FEEDBACK_LIMITS.text) throw new HttpError(400, 'Textul e prea lung.');
  if (verdict !== 'like' && !text) throw new HttpError(400, 'Scrie ce ai schimba sau ce vrei să întrebi.');
  if (roomId !== null && !(typeof roomId === 'string' && view.snapshot.floor.rooms.concat(...(view.snapshot.levels ?? []).map(l => l.rooms)).some(r => r.id === roomId))) throw new HttpError(400, 'Camera nu există în prezentare.');
  const { q } = await getDb(), t = token as string;
  const { rows: [c] } = await q('select count(*)::int as n from share_feedback where token=$1', [t]);
  if (c.n >= FEEDBACK_LIMITS.perShare) throw new HttpError(429, 'S-au trimis prea multe păreri pe acest link.');
  const { rows } = await q(`insert into share_feedback(token, project_id, room_id, verdict, author, body)
    select token, project_id, $2, $3, $4, $5 from shares where token=$1 and revoked_at is null returning *`, [t, roomId, verdict, author, text]);
  if (!rows[0]) throw new HttpError(404, 'Link inexistent.');
  return row(rows[0]);
}
/** Ce vede clientul pe link: părerile trimise pe acest link, cu starea lor. */
export async function listFeedbackForShare(token: unknown): Promise<Feedback[]> {
  await resolveShare(token); const { q } = await getDb();
  const { rows } = await q('select * from share_feedback where token=$1 order by created_at', [token]);
  return rows.map(row);
}
/** Ce vede designerul: toate părerile proiectului, de pe toate linkurile (și cele revocate), cu revizia la care se referă. */
export async function listFeedback(owner: string, projectId: string): Promise<(Feedback & { revisionNumber: number })[]> {
  await getProject(owner, projectId); const { q } = await getDb();
  const { rows } = await q(`select f.*, s.revision_number from share_feedback f join shares s on s.token = f.token where f.project_id=$1 order by f.created_at desc`, [projectId]);
  return rows.map(r => ({ ...row(r), revisionNumber: r.revision_number }));
}
export async function setFeedbackResolved(owner: string, projectId: string, id: unknown, resolved: unknown){
  await getProject(owner, projectId); if (typeof id !== 'string' || !ID_RE.test(id)) throw new HttpError(404, 'Părere inexistentă.');
  const { q } = await getDb();
  const { rows } = await q(`update share_feedback set resolved_at = case when $3 then coalesce(resolved_at, now()) else null end where id=$1 and project_id=$2 returning resolved_at`, [id, projectId, resolved === true]);
  if (!rows[0]) throw new HttpError(404, 'Părere inexistentă.');
  return { resolvedAt: rows[0].resolved_at ? new Date(rows[0].resolved_at).toISOString() : null };
}
