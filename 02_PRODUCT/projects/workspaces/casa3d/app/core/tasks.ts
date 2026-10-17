// Sarcinile studioului: ce e de făcut și până când, pe un proiect sau generale. Lista „Azi” = întârziate + azi + următoarele 7 zile.
export interface Task { id: string; projectId: string | null; title: string; due: string | null; doneAt: string | null; createdAt: string }
export const TASK_LIMITS = { title: 200, perOwner: 2000 };
const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
export const validDate = (v: string) => DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').trim().slice(0, max) : '';
export class TaskError extends Error {}
export function checkTask(x: unknown, partial = false): { title?: string; due?: string | null; done?: boolean } {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new TaskError('Sarcina e invalidă.');
  const out: { title?: string; due?: string | null; done?: boolean } = {};
  if (o.title !== undefined || !partial){ const t = clean(o.title, TASK_LIMITS.title); if (!t) throw new TaskError('Scrie ce e de făcut.'); out.title = t; }
  if (o.due !== undefined){ if (o.due === null || o.due === '') out.due = null; else if (typeof o.due === 'string' && validDate(o.due)) out.due = o.due; else throw new TaskError('Data nu e validă (AAAA-LL-ZZ).'); }
  if (o.done !== undefined){ if (typeof o.done !== 'boolean') throw new TaskError('Starea sarcinii e invalidă.'); out.done = o.done; }
  return out;
}
const addDays = (d: string, n: number) => new Date(Date.parse(d + 'T00:00:00Z') + n * 864e5).toISOString().slice(0, 10);
/** Împărțirea pentru lista „Azi”: întârziate, azi, săptămâna care vine, fără termen; cele făcute nu apar. */
export function agenda(tasks: Task[], today: string){
  const open = tasks.filter(t => !t.doneAt), week = addDays(today, 7), by = (a: Task, b: Task) => (a.due ?? '').localeCompare(b.due ?? '') || a.createdAt.localeCompare(b.createdAt);
  return { overdue: open.filter(t => t.due && t.due < today).sort(by), today: open.filter(t => t.due === today).sort(by),
    week: open.filter(t => t.due && t.due > today && t.due <= week).sort(by), later: open.filter(t => t.due && t.due > week).sort(by), noDate: open.filter(t => !t.due).sort(by) };
}
