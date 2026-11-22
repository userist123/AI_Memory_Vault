// Jurnalul zilnic de șantier: ce s-a lucrat într-o zi (text, meteo, câți oameni, ce meserii, poze). Fiecare intrare e
// internă până când designerul o face vizibilă clientului; pe link clientul vede doar data, textul, meteo și pozele
// (numărul de oameni și meseriile rămân la designer). Doar date și validare; stocarea în lib/site-log.ts.
import { TRADES, clean, isUuid, type Trade } from './site';

export const LOG_LIMITS = { text: 2000, weather: 80, workers: 500, photos: 6, entriesPerProject: 2000 };
export interface SiteLogInput { date: string; text: string; weather?: string; workers?: number | null; trades?: Trade[]; photos?: string[]; clientVisible?: boolean }
export interface SiteLogEntry { id: string; date: string; text: string; weather: string; workers: number | null; trades: Trade[]; photos: string[]; clientVisible: boolean; createdAt: string }
/** Ce ajunge pe linkul clientului: fără numărul de oameni, fără meserii, fără starea de vizibilitate. */
export interface ClientLogEntry { id: string; date: string; text: string; weather: string; photos: string[] }
export class SiteLogError extends Error {}

/** O dată calendaristică reală, AAAA-LL-ZZ (2026-02-30 nu trece). */
export function isLogDate(v: unknown): v is string {
  if (typeof v !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(v)) return false;
  const [y, m, d] = v.split('-').map(Number) as [number, number, number], t = new Date(Date.UTC(y, m - 1, d));
  return y >= 2000 && y <= 2100 && t.getUTCFullYear() === y && t.getUTCMonth() === m - 1 && t.getUTCDate() === d;
}
/** Ziua de azi după ceasul local (nu UTC: seara târziu jurnalul nu trebuie să sară la mâine sau să rămână la ieri). */
export function localToday(now = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0'); return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`;
}
/** Validare pentru creare (completă) sau modificare (parțială: doar câmpurile trimise). Vizibilitatea e implicit oprită. */
export function checkLogEntry(x: unknown, partial = false): Partial<SiteLogInput> {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new SiteLogError('Intrarea de jurnal e invalidă.');
  const out: Partial<SiteLogInput> = {};
  if (o.date !== undefined || !partial){ if (!isLogDate(o.date)) throw new SiteLogError('Data trebuie să fie de forma AAAA-LL-ZZ.'); out.date = o.date; }
  if (o.text !== undefined || !partial){ const t = clean(o.text, LOG_LIMITS.text); if (!t) throw new SiteLogError('Scrie ce s-a lucrat.'); out.text = t; }
  if (o.weather !== undefined){ if (o.weather !== null && typeof o.weather !== 'string') throw new SiteLogError('Meteo e invalid.'); out.weather = clean(o.weather ?? '', LOG_LIMITS.weather); }
  if (o.workers !== undefined){ if (o.workers !== null && !(typeof o.workers === 'number' && Number.isInteger(o.workers) && o.workers >= 0 && o.workers <= LOG_LIMITS.workers)) throw new SiteLogError(`Numărul de oameni trebuie să fie un întreg între 0 și ${LOG_LIMITS.workers}.`); out.workers = o.workers; }
  if (o.trades !== undefined){ if (!Array.isArray(o.trades) || o.trades.length > TRADES.length || !o.trades.every((t: unknown) => (TRADES as readonly unknown[]).includes(t)) || new Set(o.trades).size !== o.trades.length) throw new SiteLogError('Meserie necunoscută.'); out.trades = o.trades; }
  // id-urile pozelor în litere mici: baza le compară ca text, iar o variantă cu majuscule ar fi luată drept poză nefolosită și ștearsă
  if (o.photos !== undefined){ const ph = Array.isArray(o.photos) ? o.photos.map((x: unknown) => typeof x === 'string' ? x.toLowerCase() : x) : null; if (!ph || ph.length > LOG_LIMITS.photos || !ph.every(isUuid) || new Set(ph).size !== ph.length) throw new SiteLogError('Pozele sunt invalide.'); out.photos = ph as string[]; }
  if (o.clientVisible !== undefined){ if (typeof o.clientVisible !== 'boolean') throw new SiteLogError('Vizibilitatea e invalidă.'); out.clientVisible = o.clientVisible; }
  else if (!partial) out.clientVisible = false;
  return out;
}
/** Cele mai noi primele: după dată, apoi după momentul scrierii. */
export const sortLog = <T extends { date: string; createdAt?: string }>(xs: T[]): T[] =>
  [...xs].sort((a, b) => b.date.localeCompare(a.date) || (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
/** Ce vede clientul: doar intrările vizibile, fără câmpurile interne. */
export const clientLog = (xs: SiteLogEntry[]): ClientLogEntry[] =>
  sortLog(xs.filter(e => e.clientVisible === true)).map(e => ({ id: e.id, date: e.date, text: e.text, weather: e.weather, photos: e.photos }));
