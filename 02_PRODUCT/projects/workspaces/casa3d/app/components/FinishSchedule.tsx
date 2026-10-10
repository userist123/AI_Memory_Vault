'use client';
// Fișa de finisaje: tabel pentru prezentarea tipărită și export CSV (separator „;”, UTF-8 cu BOM, se deschide direct în Excel).
import type { ScheduleRow } from '@/core/finish-schedule';
import { formatMoney } from '@/core/format';

import { detailText, unitText, scheduleCsv, type T } from '@/lib/finish-schedule-text';
export { detailText, scheduleCsv };

export function downloadSchedule(rows: ScheduleRow[], t: T, cur: string, name: string, levelName: (i: number) => string){
  const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([scheduleCsv(rows, t, cur, levelName)], { type: 'text/csv;charset=utf-8' }));
  a.download = `${name.replace(/[^\w\- ]+/g, '').trim() || t('budget.csv.fileFallback')}-${t('sched.file')}.csv`; a.click(); URL.revokeObjectURL(a.href);
}
/** Tabelul tipărit: o grupă pe cameră, cu linkul și data verificării fiecărui produs. */
export function ScheduleTable({ rows, t, cur, locale }: { rows: ScheduleRow[]; t: T; cur: string; locale: string }){
  const groups: { room: string; rows: ScheduleRow[] }[] = [];
  for (const r of rows){ const name = r.room || t('sched.wholeHouse'), g = groups.at(-1); if (g && g.room === name) g.rows.push(r); else groups.push({ room: name, rows: [r] }); }
  return (<table className="print-table sched-table">
    <thead><tr><th>{t('sched.col.element')}</th><th>{t('sched.col.product')}</th><th>{t('sched.col.details')}</th><th className="num">{t('sched.col.orderedQty')}</th><th className="num">{t('sched.col.total', { cur })}</th></tr></thead>
    {groups.map((g, i) => <tbody key={i}><tr><th colSpan={5}>{g.room}</th></tr>
      {g.rows.map((r, k) => <tr key={k}><td>{t(`sched.el.${r.element}`)}</td>
        <td>{r.product}<br /><small>{r.supplier}{r.code ? ` · ${t('sched.col.code')} ${r.code}` : ''}{r.url ? <> · <a href={r.url}>{t('sched.col.link')}</a></> : null}{r.verifiedAt ? ` · ${r.verifiedAt}` : ''}</small></td>
        <td><small>{detailText(r, t)}</small></td><td className="num">{r.orderedQty} {unitText(r.unit, t)}{r.packs ? <><br /><small>{r.packs}</small></> : null}</td>
        <td className="num">{r.total != null ? formatMoney(r.total, cur, locale) : t('budget.csv.unknown')}</td></tr>)}</tbody>)}
  </table>);
}
