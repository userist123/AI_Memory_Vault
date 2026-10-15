// Pontajul pe proiect: orele lucrate, pe etape (aceleași ca în editor, plus întâlniri, drumuri, administrativ). Față de
// onorariu se vede tariful efectiv pe oră și, la onorariu pe oră, cât s-a acumulat — ca designerul să știe unde pierde timp.
export const TIME_KINDS = ['survey', 'concept', 'finishes', 'ffe', 'mep', 'docs', 'quote', 'present', 'orders', 'site', 'meeting', 'travel', 'admin'] as const;
export type TimeKind = typeof TIME_KINDS[number];
export interface TimeEntry { id: string; projectId: string; date: string; minutes: number; kind: TimeKind; note: string; createdAt: string }
export class TimeError extends Error {}
const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const validDate = (v: string) => DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
/** O înregistrare: data, durata (minute, 5 min – 24 h), etapa, nota. */
export function checkTime(x: unknown): Pick<TimeEntry, 'date' | 'minutes' | 'kind' | 'note'> {
  const o = x as any; if (!o || typeof o !== 'object') throw new TimeError('Înregistrarea nu e validă.');
  if (typeof o.date !== 'string' || !validDate(o.date)) throw new TimeError('Data nu e validă (AAAA-LL-ZZ).');
  if (!Number.isInteger(o.minutes) || o.minutes < 5 || o.minutes > 24 * 60) throw new TimeError('Durata e între 5 minute și 24 de ore.');
  if (!(TIME_KINDS as readonly string[]).includes(o.kind)) throw new TimeError('Etapă necunoscută.');
  const note = typeof o.note === 'string' ? o.note.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').trim().slice(0, 300) : '';
  return { date: o.date, minutes: o.minutes, kind: o.kind, note };
}
/** „1,5” / „1:30” / „90m” → minute; null dacă nu se înțelege. */
export function parseDuration(s: string): number | null {
  const t = s.trim().toLowerCase().replace(',', '.'); let m: RegExpMatchArray | null;
  if ((m = t.match(/^(\d{1,2}):([0-5]\d)$/))) return Number(m[1]) * 60 + Number(m[2]);
  if ((m = t.match(/^(\d{1,4})\s*m(in)?$/))) return Number(m[1]);
  if ((m = t.match(/^(\d{1,2}(\.\d+)?)\s*h?$/))) return Math.round(Number(m[1]) * 60);
  return null;
}
/** Totalul, pe etape, și tariful efectiv (onorariul fix sau pe m² împărțit la ore; la onorariu pe oră: suma acumulată). */
export function timeSummary(entries: Pick<TimeEntry, 'minutes' | 'kind'>[], fee: { kind: 'fixed' | 'per_m2' | 'hourly' | 'percent'; amount: number } | undefined, feeTotal: number | null){
  const minutes = entries.reduce((a, e) => a + e.minutes, 0), hours = Math.round(minutes / 60 * 100) / 100, byKind: Partial<Record<TimeKind, number>> = {};
  for (const e of entries) byKind[e.kind] = (byKind[e.kind] ?? 0) + e.minutes;
  const effectiveRate = fee && fee.kind !== 'hourly' && feeTotal != null && hours > 0 ? Math.round(feeTotal / hours * 100) / 100 : null;
  const billable = fee?.kind === 'hourly' ? Math.round(fee.amount * hours * 100) / 100 : null;
  return { minutes, hours, byKind, effectiveRate, billable };
}
