// Alegerea clientului între variantele unei camere (A/B/C), pe linkul de prezentare. O singură alegere pe cameră și pe link
// (clientul se poate răzgândi: ultima alegere o înlocuiește pe cea dinainte). Varianta trebuie să existe în revizia partajată;
// eticheta o pune serverul. Designerul vede alegerile tuturor linkurilor și aplică varianta din editor.
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { resolveShare } from './share';
import { floors } from '../core/levels';

export const CHOICE_LIMITS = { author: 60, note: 500 };
export interface RoomChoice { roomId: string; optionId: string; label: string; author: string; note: string; chosenAt: string }
const clean = (v: unknown) => typeof v === 'string' ? v.replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f​-‏‪-‮⁠-⁩﻿]/g, '').trim() : null;
const row = (r: any): RoomChoice => ({ roomId: r.room_id, optionId: r.option_id, label: r.label, author: r.author, note: r.note, chosenAt: new Date(r.chosen_at).toISOString() });

export async function chooseOption(token: unknown, input: unknown): Promise<RoomChoice> {
  const view = await resolveShare(token), x = input as any;
  if (!x || typeof x !== 'object' || typeof x.roomId !== 'string' || typeof x.optionId !== 'string') throw new HttpError(400, 'Alegerea nu e validă.');
  const author = clean(x.author), note = clean(x.note ?? '');
  if (!author || author.length > CHOICE_LIMITS.author) throw new HttpError(400, 'Scrie-ți numele (cel mult 60 de caractere).');
  if (note == null || note.length > CHOICE_LIMITS.note) throw new HttpError(400, 'Nota e prea lungă.');
  if (!floors(view.snapshot).some(f => f.rooms.some(r => r.id === x.roomId))) throw new HttpError(400, 'Camera nu există în prezentare.');
  const opt = view.snapshot.roomOptions?.[x.roomId]?.find(o => o.id === x.optionId);
  if (!opt) throw new HttpError(400, 'Varianta nu există în prezentare.');
  const { q } = await getDb(), { rows } = await q(`insert into room_choices(token, project_id, room_id, option_id, label, author, note)
      select s.token, s.project_id, $2, $3, $4, $5, $6 from shares s where s.token=$1 and s.revoked_at is null
      on conflict (token, room_id) do update set option_id=excluded.option_id, label=excluded.label, author=excluded.author, note=excluded.note, chosen_at=now() returning *`,
    [token, x.roomId, opt.id, opt.label, author, note]);
  if (!rows[0]) throw new HttpError(404, 'Link inexistent.');
  return row(rows[0]);
}
export async function listChoicesForShare(token: unknown): Promise<RoomChoice[]> {
  await resolveShare(token); const { q } = await getDb();
  return (await q('select * from room_choices where token=$1 order by chosen_at', [token])).rows.map(row);
}
/** Pentru designer: cea mai nouă alegere pe fiecare cameră, de pe orice link al proiectului (și revocat), cu revizia. */
export async function listChoices(owner: string, projectId: string): Promise<(RoomChoice & { revisionNumber: number })[]> {
  await getProject(owner, projectId); const { q } = await getDb();
  const { rows } = await q(`select distinct on (c.room_id) c.*, s.revision_number from room_choices c join shares s on s.token = c.token
    where c.project_id=$1 order by c.room_id, c.chosen_at desc`, [projectId]);
  return rows.map(r => ({ ...row(r), revisionNumber: r.revision_number }));
}
