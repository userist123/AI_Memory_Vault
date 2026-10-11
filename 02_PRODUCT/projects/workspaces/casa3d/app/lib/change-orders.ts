// Modificările cu preț: designerul le scrie (ciornă), le trimite, clientul le decide pe orice link activ al proiectului.
// O modificare trimisă nu se mai poate edita (clientul a văzut-o); se poate retrage doar cât nu e decisă.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { isShareToken } from './share';
import { checkChange, checkDecision, ChangeError, CHANGE_LIMITS, type ChangeOrder } from '../core/change-orders';

const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const iso = (d: any) => d ? new Date(d).toISOString() : null;
const row = (r: any): ChangeOrder => ({ id: r.id, number: r.number, title: r.title, body: r.body, amount: Number(r.amount), days: r.days, status: r.status,
  createdAt: iso(r.created_at)!, sentAt: iso(r.sent_at), decidedAt: iso(r.decided_at), decidedBy: r.decided_by ?? null, clientNote: r.client_note ?? null });
const v = <T>(fn: () => T) => { try { return fn(); } catch (e: any){ if (e instanceof ChangeError) throw new HttpError(400, e.message); throw e; } };
const notFound = () => new HttpError(404, 'Modificare inexistentă.');

export async function listChanges(owner: string, projectId: string): Promise<ChangeOrder[]> {
  await getProject(owner, projectId); const { q } = await getDb();
  return (await q('select * from change_orders where project_id=$1 order by number', [projectId])).rows.map(row);
}
export async function addChange(owner: string, projectId: string, input: unknown): Promise<ChangeOrder> {
  const x = v(() => checkChange(input)); await getProject(owner, projectId);
  const { tx } = await getDb();
  return tx(async q => {
    await q('select id from projects where id=$1 for update', [projectId]);   // numărul următor fără cursă între cereri
    const { rows: [c] } = await q('select count(*)::int as n, coalesce(max(number), 0)::int as m from change_orders where project_id=$1', [projectId]);
    if (c.n >= CHANGE_LIMITS.perProject) throw new HttpError(429, 'Prea multe modificări în proiect.');
    const { rows } = await q('insert into change_orders(project_id, number, title, body, amount, days) values($1,$2,$3,$4,$5,$6) returning *', [projectId, c.m + 1, x.title, x.body ?? '', x.amount, x.days ?? 0]);
    return row(rows[0]); });
}
/** patch: câmpuri (doar pe ciornă) sau { action: 'send' | 'withdraw' }. */
export async function updateChange(owner: string, projectId: string, id: unknown, patch: unknown): Promise<ChangeOrder> {
  await getProject(owner, projectId); if (typeof id !== 'string' || !ID_RE.test(id)) throw notFound();
  const { q } = await getDb(), action = (patch as any)?.action;
  if (action === 'send' || action === 'withdraw'){
    const [from, to, sent] = action === 'send' ? ['draft', 'sent', "date_trunc('milliseconds', now())"] : ['sent', 'draft', 'null'];   // ms: aceeași precizie ca data trimisă clientului
    const { rows } = await q(`update change_orders set status=$3, sent_at=${sent} where id=$1 and project_id=$2 and status=$4 returning *`, [id, projectId, to, from]);
    if (!rows[0]) throw new HttpError(409, action === 'send' ? 'Se poate trimite doar o ciornă.' : 'Se poate retrage doar o modificare trimisă și nedecisă.');
    return row(rows[0]); }
  const x = v(() => checkChange(patch, true)), vals: unknown[] = [id, projectId], sets: string[] = [];
  for (const [k, val] of Object.entries(x)){ vals.push(val); sets.push(`${k}=$${vals.length}`); }
  if (!sets.length) throw new HttpError(400, 'Nimic de modificat.');
  const { rows } = await q(`update change_orders set ${sets.join(', ')} where id=$1 and project_id=$2 and status='draft' returning *`, vals);
  if (!rows[0]) throw new HttpError(409, 'Doar o ciornă se poate modifica; clientul a văzut deja varianta trimisă.');
  return row(rows[0]);
}
export async function deleteChange(owner: string, projectId: string, id: unknown){
  await getProject(owner, projectId); if (typeof id !== 'string' || !ID_RE.test(id)) throw notFound();
  const { q } = await getDb(), { rows } = await q(`delete from change_orders where id=$1 and project_id=$2 and status='draft' returning id`, [id, projectId]);
  if (!rows[0]) throw new HttpError(409, 'Doar o ciornă se poate șterge.'); return { ok: true };
}

async function shareProject(token: unknown): Promise<string> {
  if (!isShareToken(token)) throw new HttpError(404, 'Link inexistent.');
  const { q } = await getDb(), { rows } = await q('select project_id from shares where token=$1 and revoked_at is null', [token]);
  if (!rows[0]) throw new HttpError(404, 'Link inexistent.'); return rows[0].project_id;
}
/** Ce vede clientul: doar modificările trimise sau decise (ciornele rămân la designer). */
export async function listChangesForShare(token: unknown): Promise<ChangeOrder[]> {
  const pid = await shareProject(token), { q } = await getDb();
  return (await q(`select * from change_orders where project_id=$1 and status <> 'draft' order by number`, [pid])).rows.map(row);
}
export async function decideChange(token: unknown, id: unknown, input: unknown): Promise<ChangeOrder> {
  const pid = await shareProject(token); if (typeof id !== 'string' || !ID_RE.test(id)) throw notFound();
  const d = v(() => checkDecision(input)), { q } = await getDb();
  // doar o modificare trimisă și nedecisă; o a doua decizie (sau una simultană) nu o suprascrie pe prima
  // doar varianta pe care a văzut-o clientul (retrasă și retrimisă = altă variantă, altă dată de trimitere)
  const { rows } = await q(`update change_orders set status=$3, decided_at=now(), decided_by=$4, client_note=$5 where id=$1 and project_id=$2 and status='sent' and sent_at=$6::timestamptz returning *`, [id, pid, d.status, d.author, d.note, d.seen]);
  if (!rows[0]){ const { rows: [e] } = await q(`select status from change_orders where id=$1 and project_id=$2 and status <> 'draft'`, [id, pid]);
    if (!e) throw notFound(); throw new HttpError(409, e.status === 'sent' ? 'Modificarea s-a schimbat între timp; reîncărcați pagina și decideți din nou.' : 'Modificarea a fost deja decisă.'); }
  return row(rows[0]);
}
