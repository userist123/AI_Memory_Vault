// Dosarul de predare al unui proiect (doar pentru proprietar): din comenzi, fișa de finisaje și remedierile deschise.
import { getProject, getCatalog, getMaterials } from './repo';
import { projectRows } from './procurement';
import { listSnags } from './site';
import { finishSchedule } from '../core/finish-schedule';
import { handover } from '../core/handover';

export async function getHandover(owner: string, id: string, today = new Date().toISOString().slice(0, 10)){
  const p = await getProject(owner, id), [cat, mc] = await Promise.all([getCatalog(), getMaterials()]);
  const [rows, snags] = await Promise.all([projectRows(id, p.draft, today, p.meta.startDate), listSnags(owner, id)]);
  return { name: p.draft.name || p.name, meta: p.meta, ...handover(rows, finishSchedule(p.draft, cat, mc)), openSnags: snags.filter(s => s.status === 'open').map(s => ({ number: s.number, title: s.title, trade: s.trade })) };
}
