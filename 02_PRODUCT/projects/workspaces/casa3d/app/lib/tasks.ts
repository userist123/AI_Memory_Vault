// Sarcinile proprietarului (pe toate proiectele lui). Un proiect se poate atașa doar dacă e al lui.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { checkTask, TaskError, TASK_LIMITS, type Task } from '../core/tasks';

const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const day = (d: any) => d == null ? null : typeof d === 'string' ? d.slice(0, 10) : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
const row = (r: any): Task => ({ id: r.id, projectId: r.project_id, title: r.title, due: day(r.due), doneAt: r.done_at ? new Date(r.done_at).toISOString() : null, createdAt: new Date(r.created_at).toISOString() });
const v = <T>(fn: () => T) => { try { return fn(); } catch (e: any){ if (e instanceof TaskError) throw new HttpError(400, e.message); throw e; } };

export async function listTasks(owner: string, projectId?: string | null): Promise<(Task & { projectName: string | null })[]> {
  const { q } = await getDb();
  if (projectId){ await getProject(owner, projectId); }
  const { rows } = await q(`select t.*, p.name as project_name from tasks t left join projects p on p.id = t.project_id where t.owner=$1 and (t.project_id is null or p.deleted_at is null) ${projectId ? 'and t.project_id=$2' : ''}
    and (t.done_at is null or t.done_at > now() - interval '14 days') order by t.done_at nulls first, t.due nulls last, t.created_at`, projectId ? [owner, projectId] : [owner]);
  return rows.map(r => ({ ...row(r), projectName: r.project_name ?? null }));
}
export async function addTask(owner: string, input: unknown): Promise<Task> {
  const s = v(() => checkTask(input)), pid = (input as any)?.projectId ?? null;
  if (pid !== null){ if (typeof pid !== 'string' || !ID_RE.test(pid)) throw new HttpError(400, 'Proiect invalid.'); await getProject(owner, pid); }
  // numărătoarea și inserarea sub același blocaj pe proprietar: cererile simultane nu trec de limită
  const { tx } = await getDb();
  return tx(async q => {
    await q('select pg_advisory_xact_lock(hashtext($1))', [owner]);
    const { rows: [c] } = await q('select count(*)::int as n from tasks where owner=$1 and done_at is null', [owner]);
    if (c.n >= TASK_LIMITS.perOwner) throw new HttpError(429, 'Prea multe sarcini deschise.');
    const { rows } = await q('insert into tasks(owner, project_id, title, due) values($1,$2,$3,$4) returning *', [owner, pid, s.title, s.due ?? null]);
    return row(rows[0]); });
}
export async function updateTask(owner: string, id: unknown, patch: unknown): Promise<Task> {
  if (typeof id !== 'string' || !ID_RE.test(id)) throw new HttpError(404, 'Sarcină inexistentă.');
  const s = v(() => checkTask(patch, true)), { q } = await getDb(), sets: string[] = [], vals: unknown[] = [id, owner];
  const put = (col: string, val: unknown) => { vals.push(val); sets.push(`${col}=$${vals.length}`); };
  if (s.title !== undefined) put('title', s.title); if (s.due !== undefined) put('due', s.due);
  if (s.done !== undefined) sets.push(s.done ? 'done_at = coalesce(done_at, now())' : 'done_at = null');
  if (!sets.length) throw new HttpError(400, 'Nimic de modificat.');
  const { rows } = await q(`update tasks set ${sets.join(', ')} where id=$1 and owner=$2 returning *`, vals);
  if (!rows[0]) throw new HttpError(404, 'Sarcină inexistentă.'); return row(rows[0]);
}
export async function deleteTask(owner: string, id: unknown){
  if (typeof id !== 'string' || !ID_RE.test(id)) throw new HttpError(404, 'Sarcină inexistentă.');
  const { q } = await getDb(), { rows } = await q('delete from tasks where id=$1 and owner=$2 returning id', [id, owner]);
  if (!rows[0]) throw new HttpError(404, 'Sarcină inexistentă.'); return { ok: true };
}
