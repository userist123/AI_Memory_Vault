// Graficul de execuție al proiectului (doar proprietarul): etapele se salvează în project_schedule; termenul vine din fișa
// proiectului (meta.dueDate), ca să nu existe două date de predare.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { checkSchedule, ScheduleError, type Schedule } from '../core/schedule';

export async function getSchedule(owner: string, id: string): Promise<Schedule & { due: string | null; start: string | null }> {
  const p = await getProject(owner, id), { q } = await getDb(), { rows } = await q('select data from project_schedule where project_id=$1', [id]);
  let phases: Schedule['phases'] = []; try { if (rows[0]) phases = checkSchedule(rows[0].data).phases; } catch { phases = []; }
  return { phases, due: p.meta?.dueDate ?? null, start: p.meta?.startDate ?? null };
}
export async function saveSchedule(owner: string, id: string, input: unknown){
  await getProject(owner, id); let s: Schedule; try { s = checkSchedule(input); } catch (e: any){ if (e instanceof ScheduleError) throw new HttpError(400, e.message); throw e; }
  const { q } = await getDb();
  await q('insert into project_schedule(project_id, data) values($1,$2) on conflict(project_id) do update set data=excluded.data, updated_at=now()', [id, JSON.stringify(s)]);
  return getSchedule(owner, id);
}
