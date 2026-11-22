// Graficul de execuție al lucrării: etape cu meseria, datele de început și sfârșit și etapa după care încep. Aplicația propune
// doar ORDINEA uzuală a lucrărilor, din ce cere proiectul (demolări, pereți noi, instalații, finisaje, uși, mobilier); datele
// și duratele le introduce utilizatorul, nu se inventează (CONSTITUTION art. 4). Verificări: o etapă care începe înainte
// să se termine cea de care depinde, depășirea termenului proiectului, etape fără date. Funcții pure.
import type { Snapshot } from './types';
import { floors } from './levels';
import { TRADES, type Trade } from './site';
import { TECH_KINDS } from './technical';

export interface Phase { id: string; title: string; trade: Trade; start: string | null; end: string | null; after: string | null; done: boolean }
export interface Schedule { phases: Phase[] }
export class ScheduleError extends Error {}
export const SCHEDULE_LIMITS = { maxPhases: 80, titleMax: 80 } as const;

const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const validDate = (v: string) => DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const ID_RE = /^[\w-]{1,40}$/;
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max) : '';
const day = (s: string) => Date.parse(s + 'T00:00:00Z') / 86400000;

export function checkSchedule(x: unknown): Schedule {
  const o = x as any; if (!o || typeof o !== 'object' || !Array.isArray(o.phases)) throw new ScheduleError('Graficul nu e valid.');
  if (o.phases.length > SCHEDULE_LIMITS.maxPhases) throw new ScheduleError(`Cel mult ${SCHEDULE_LIMITS.maxPhases} etape.`);
  const phases: Phase[] = o.phases.map((p: any) => {
    if (!p || typeof p !== 'object' || typeof p.id !== 'string' || !ID_RE.test(p.id)) throw new ScheduleError('O etapă nu are un id valid.');
    const title = clean(p.title, SCHEDULE_LIMITS.titleMax); if (!title) throw new ScheduleError('Fiecare etapă are nevoie de un nume.');
    if (!(TRADES as readonly string[]).includes(p.trade)) throw new ScheduleError('Meserie necunoscută.');
    const d = (v: unknown) => { if (v == null || v === '') return null; if (typeof v !== 'string' || !validDate(v)) throw new ScheduleError('O dată nu e validă (AAAA-LL-ZZ).'); return v; };
    const start = d(p.start), end = d(p.end);
    if (start && end && day(end) < day(start)) throw new ScheduleError(`Etapa „${title}” se termină înainte să înceapă.`);
    return { id: p.id, title, trade: p.trade, start, end, after: typeof p.after === 'string' && p.after ? p.after : null, done: p.done === true };
  });
  const ids = new Set<string>(); for (const p of phases) { if (ids.has(p.id)) throw new ScheduleError('Două etape au același id.'); ids.add(p.id); }
  for (const p of phases) if (p.after && (!ids.has(p.after) || p.after === p.id)) throw new ScheduleError(`Etapa „${p.title}” depinde de o etapă inexistentă.`);
  // fără cicluri: urmând „după” din orice etapă, nu te întorci la ea
  const by = new Map(phases.map(p => [p.id, p]));
  for (const p of phases) { const seen = new Set<string>(); let q: Phase | undefined = p; while (q?.after) { if (seen.has(q.id)) throw new ScheduleError('Dependențele etapelor formează un cerc.'); seen.add(q.id); q = by.get(q.after); } }
  return { phases };
}

/** Ordinea uzuală a lucrărilor de renovare, cu etapele pe care proiectul le cere. `t` dă numele etapei în limba interfeței. */
export function proposeSchedule(s: Snapshot, t: (k: string) => string): Phase[] {
  const fl = floors(s), walls = fl.flatMap(f => f.walls), tech = s.tech ?? [], runs = fl.flatMap(f => f.mepRuns ?? []);
  const cat = (c: 'electric' | 'plumbing' | 'hvac') => tech.some(p => TECH_KINDS[p.kind]?.category === c);
  const runSys = (pred: (sys: string) => boolean) => runs.some(r => pred(r.system));
  const wet = fl.flatMap(f => f.rooms).some(r => /^(baie|bucatarie|wc|bath|kitchen)$/i.test(r.type));
  const steps: [string, Trade, boolean][] = [
    ['demolition', 'general', walls.some(w => w.status === 'demolish')],
    ['newWalls', 'general', walls.some(w => w.status === 'new')],
    ['electricRough', 'electric', cat('electric') || runSys(x => x === 'electric')],
    ['plumbingRough', 'plumbing', cat('plumbing') || runSys(x => x === 'water_cold' || x === 'water_hot' || x === 'drain')],
    ['hvac', 'hvac', cat('hvac') || runSys(x => x.startsWith('hvac') || x === 'refrigerant')],
    ['plaster', 'general', true],
    ['tiles', 'tiles', wet],
    ['paint', 'paint', true],
    ['flooring', 'flooring', true],
    ['doors', 'doors', walls.some(w => w.openings.some(o => o.kind === 'door'))],
    ['furniture', 'carpentry', s.placements.length > 0],
    ['electricFinish', 'electric', cat('electric')],
    ['cleaning', 'cleaning', true],
  ];
  const out: Phase[] = [];
  for (const [key, trade, needed] of steps) if (needed) out.push({ id: `ph-${key}`, title: t(`works.ph.${key}`), trade, start: null, end: null, after: out.length ? out[out.length - 1]!.id : null, done: false });
  return out;
}

export interface ScheduleIssue { code: 'BEFORE_PREDECESSOR' | 'PAST_DUE' | 'MISSING_DATES'; phase?: string; after?: string; count?: number }
/** Ce nu se potrivește: o etapă care începe înainte de sfârșitul celei după care trebuie să vină (aceeași zi e permisă),
 *  o etapă care se termină după termenul proiectului, etape fără date. */
export function scheduleIssues(phases: Phase[], due: string | null): ScheduleIssue[] {
  const by = new Map(phases.map(p => [p.id, p])), out: ScheduleIssue[] = [];
  for (const p of phases) {
    const prev = p.after ? by.get(p.after) : undefined;
    if (prev?.end && p.start && day(p.start) < day(prev.end)) out.push({ code: 'BEFORE_PREDECESSOR', phase: p.id, after: prev.id });
    if (due && p.end && day(p.end) > day(due)) out.push({ code: 'PAST_DUE', phase: p.id });
  }
  const missing = phases.filter(p => !p.start || !p.end).length; if (missing) out.push({ code: 'MISSING_DATES', count: missing });
  return out;
}

/** Intervalul desenat: de la primul început la ultimul sfârșit (și termenul, dacă e mai târziu); null fără nicio dată. */
export function scheduleRange(phases: Phase[], due: string | null): { from: string; to: string; days: number } | null {
  const dates = phases.flatMap(p => [p.start, p.end]).filter((d): d is string => !!d); if (!dates.length) return null;
  if (due) dates.push(due);
  const ds = dates.map(day), from = Math.min(...ds), to = Math.max(...ds), iso = (n: number) => new Date(n * 86400000).toISOString().slice(0, 10);
  return { from: iso(from), to: iso(to), days: to - from + 1 };
}
