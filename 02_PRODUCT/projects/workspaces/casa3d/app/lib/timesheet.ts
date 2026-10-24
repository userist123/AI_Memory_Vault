// Pontajul pe proiect (doar proprietarul): înregistrări, rezumat față de onorariu.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { checkTime, timeSummary, TimeError, type TimeEntry } from '../core/timesheet';
import { contractContext } from './contract';
import { effectiveFee, feeFor } from '../core/contract';

const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i, MAX_PER_PROJECT = 5000;
const day = (d: any) => typeof d === 'string' ? d.slice(0, 10) : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
const row = (r: any): TimeEntry => ({ id: r.id, projectId: r.project_id, date: day(r.date), minutes: r.minutes, kind: r.kind, note: r.note, createdAt: new Date(r.created_at).toISOString() });
export async function getTimesheet(owner: string, id: string){
  const p = await getProject(owner, id), { q } = await getDb(), entries = (await q('select * from time_entries where project_id=$1 order by date desc, created_at desc', [id])).rows.map(row);
  // onorariul din contract (sursa unică; migrează și fișa veche), aceeași sumă ca în contract, buget și proformă
  // (datele de contract invalide nu opresc pontajul: rămâne doar onorariul din fișă, fără estimare)
  const ctx = await contractContext(owner, id, undefined, undefined).catch(() => null), fee = ctx ? effectiveFee(ctx.meta, ctx.data) : p.meta.fee;
  return { entries, summary: timeSummary(entries, fee, fee && ctx ? feeFor(ctx).net : null) };
}
export async function addTime(owner: string, id: string, input: unknown): Promise<TimeEntry> {
  await getProject(owner, id); let x; try { x = checkTime(input); } catch (e: any){ if (e instanceof TimeError) throw new HttpError(400, e.message); throw e; }
  const { tx } = await getDb();
  return tx(async q => { await q('select id from projects where id=$1 for update', [id]);
    const { rows: [c] } = await q('select count(*)::int as n from time_entries where project_id=$1', [id]); if (c.n >= MAX_PER_PROJECT) throw new HttpError(429, 'Prea multe înregistrări.');
    const { rows } = await q('insert into time_entries(project_id, date, minutes, kind, note) values($1,$2,$3,$4,$5) returning *', [id, x.date, x.minutes, x.kind, x.note]); return row(rows[0]); });
}
export async function deleteTime(owner: string, id: string, entry: unknown){
  await getProject(owner, id); if (typeof entry !== 'string' || !ID_RE.test(entry)) throw new HttpError(404, 'Înregistrare inexistentă.');
  const { q } = await getDb(), { rows } = await q('delete from time_entries where id=$1 and project_id=$2 returning id', [entry, id]);
  if (!rows[0]) throw new HttpError(404, 'Înregistrare inexistentă.'); return { ok: true };
}
