// Releveul de pe șantier, pe camere (doar proprietarul). Camera trebuie să existe în proiect; o măsurătoare nouă o înlocuiește.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { floors } from '../core/levels';
import { checkSurvey, SurveyError, type RoomSurvey } from '../core/survey';

const row = (r: any): RoomSurvey => ({ ...(r.data as object), roomId: r.room_id, measuredAt: new Date(r.measured_at).toISOString() } as RoomSurvey);
export async function listSurveys(owner: string, id: string): Promise<RoomSurvey[]> {
  await getProject(owner, id); const { q } = await getDb(); return (await q('select * from surveys where project_id=$1', [id])).rows.map(row);
}
export async function saveSurvey(owner: string, id: string, roomId: unknown, input: unknown): Promise<RoomSurvey> {
  const p = await getProject(owner, id);
  if (typeof roomId !== 'string' || !floors(p.draft).some(f => f.rooms.some(r => r.id === roomId))) throw new HttpError(400, 'Camera nu există în proiect.');
  let m; try { m = checkSurvey(input); } catch (e: any){ if (e instanceof SurveyError) throw new HttpError(400, e.message); throw e; }
  const { q } = await getDb();
  const { rows } = await q(`insert into surveys(project_id, room_id, data) values($1,$2,$3) on conflict(project_id, room_id) do update set data=excluded.data, measured_at=now() returning *`, [id, roomId, JSON.stringify(m)]);
  return row(rows[0]);
}
export async function deleteSurvey(owner: string, id: string, roomId: unknown){
  await getProject(owner, id); if (typeof roomId !== 'string' || roomId.length > 64) throw new HttpError(404, 'Releveu inexistent.');
  const { q } = await getDb(), { rows } = await q('delete from surveys where project_id=$1 and room_id=$2 returning room_id', [id, roomId]);
  if (!rows[0]) throw new HttpError(404, 'Releveu inexistent.'); return { ok: true };
}
