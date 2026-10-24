// Textele fișei de finisaje (CSV și tabel), separate de componentă ca să fie testabile.
import type { ScheduleRow } from '@/core/finish-schedule';

/** O celulă CSV sigură: ghilimele dublate, iar textul care începe cu = + - @ (sau tab/CR) primește un apostrof,
 *  ca Excel să nu-l execute ca formulă (numele camerei sau al proiectului vin de la utilizator). Numerele negative rămân numere. */
export const csvCell = (v: unknown) => { let s = String(v ?? ''); if (/^[=+\-@\t\r]/.test(s) && !/^-?\d+([.,]\d+)?$/.test(s)) s = "'" + s; return `"${s.replace(/"/g, '""')}"`; };
export type T = (k: string, v?: Record<string, string | number>) => string;
export const detailText = (r: ScheduleRow, t: T) => r.details.map(d => d.key === 'sched.wallSide' ? t(`fin.side.${d.vars?.side}`) : t(d.key, d.vars)).join(' · ');
export const unitText = (u: string, t: T) => u === 'm2' ? 'm²' : u === 'buc' ? t('fin.unit.pc') : u;
const COLS = ['level', 'room', 'element', 'product', 'supplier', 'code', 'details', 'netQty', 'unit', 'waste', 'orderedQty', 'packs', 'unitPrice', 'total', 'link', 'verified', 'confidence'] as const;

export function scheduleCsv(rows: ScheduleRow[], t: T, cur: string, levelName: (i: number) => string): string {
  const head = COLS.map(c => c === 'unitPrice' || c === 'total' ? t(`sched.col.${c}`, { cur }) : t(`sched.col.${c}`));
  const body = rows.map(r => [levelName(r.level), r.room || t('sched.wholeHouse'), t(`sched.el.${r.element}`), r.product, r.supplier, r.code ?? '', detailText(r, t), r.netQty, unitText(r.unit, t),
    Math.round(r.wastePct * 100), r.orderedQty, r.packs ?? '', r.unitPrice ?? t('budget.csv.unknown'), r.total ?? t('budget.csv.unknown'), r.url ?? '', r.verifiedAt ?? '', r.confidence]);
  return '\ufeff' + [head, ...body].map(row => row.map(csvCell).join(';')).join('\n');
}
