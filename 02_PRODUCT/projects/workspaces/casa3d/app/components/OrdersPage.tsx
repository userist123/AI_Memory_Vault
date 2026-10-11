'use client';
// Etapa „Comenzi”: tot ce e de cumpărat, grupat pe furnizor. Pe fiecare linie: starea (de comandat → ofertă cerută →
// comandat → livrat → montat), nr. comenzii, datele; întârzierile sus. Lista unui furnizor se tipărește sau se descarcă (CSV).
import { useEffect, useMemo, useState } from 'react';
import { ORDER_STATUSES, type OrderState, type ProcurementRow, type SupplierGroup } from '@/core/procurement';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

type Data = { rows: ProcurementRow[]; groups: SupplierGroup[]; summary: { lines: number; todo: number; ordered: number; delivered: number; installed: number; overdue: number; ordersValue: number } };
const csvCell = (v: unknown) => { const s = String(v ?? ''); return /[",;\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
export default function OrdersPage({ id, cur, say, projectName }: { id: string; cur: string; say(s: string): void; projectName: string }){
  const { t, lang } = usePrefs(), [data, setData] = useState<Data | null>(null), [open, setOpen] = useState<string | null>(null), [filter, setFilter] = useState<'all' | 'open' | 'overdue'>('all');
  const load = () => fetch(`/api/projects/${id}/orders`).then(r => r.ok ? r.json() : null).then(j => j && setData(j)).catch(() => say(t('orders.loadError')));
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  async function save(key: string, state: OrderState){
    const r = await fetch(`/api/projects/${id}/orders`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ key, state }) });
    if (!r.ok){ const j = await r.json().catch(() => ({})); say(j.error || t('orders.saveError')); return; } load(); }
  const money = (v: number | null) => formatMoney(v, cur, lang, '—'), today = new Date().toISOString().slice(0, 10);
  const groups = useMemo(() => (data?.groups ?? []).map(g => ({ ...g, rows: g.rows.filter(r => filter === 'all' ? true : filter === 'overdue' ? r.overdue : ['todo', 'quoted'].includes(r.state.status)) })).filter(g => g.rows.length), [data, filter]);
  function csv(g: SupplierGroup){ const head = [t('orders.col.product'), t('orders.col.code'), t('orders.col.qty'), t('orders.col.unit'), t('orders.col.packs'), t('orders.col.unitPrice'), t('orders.col.total'), t('orders.col.rooms'), t('orders.col.status'), t('orders.col.ref'), 'URL'];
    const lines = [head, ...g.rows.map(r => [r.product, r.code ?? '', r.qty, r.unit, r.packs ?? '', r.unitPrice ?? '', r.total ?? '', r.rooms.join(', '), t(`ostatus.${r.state.status}`), r.state.ref ?? '', r.url ?? ''])].map(l => l.map(csvCell).join(';')).join('\n');
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob(['﻿' + lines], { type: 'text/csv;charset=utf-8' })); a.download = `${projectName || 'proiect'} - ${g.supplier}.csv`.replace(/[\\/:*?"<>|]/g, '_'); a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 2000); }
  if (!data) return <div className="orders"><p className="muted">{t('orders.loading')}</p></div>;
  const s = data.summary;
  return (<div className="orders">
    <header className="orders-head"><div><h1>{t('orders.title')}</h1><p className="muted">{t('orders.sub')}</p></div>
      <dl className="orders-sum"><div><dt>{t('orders.sum.todo')}</dt><dd>{s.todo}</dd></div><div><dt>{t('orders.sum.ordered')}</dt><dd>{s.ordered}</dd></div><div><dt>{t('orders.sum.delivered')}</dt><dd>{s.delivered}</dd></div>
        <div className={s.overdue ? 'late' : ''}><dt>{t('orders.sum.overdue')}</dt><dd>{s.overdue}</dd></div><div><dt>{t('orders.sum.value')}</dt><dd>{money(s.ordersValue)}</dd></div></dl></header>
    <div className="filters" role="tablist">{(['all', 'open', 'overdue'] as const).map(f => <button key={f} role="tab" aria-selected={filter === f} onClick={() => setFilter(f)}>{t(`orders.filter.${f}`)}</button>)}</div>
    {groups.length === 0 && <p className="muted">{t('orders.none')}</p>}
    {groups.map(g => <section key={g.supplier} className="supplier">
      <header><h2>{g.supplier}</h2><span className="muted">{t('orders.groupInfo', { n: g.rows.length, open: g.open })}{g.overdue ? ` · ` : ''}{g.overdue ? <b className="late">{t('orders.groupLate', { n: g.overdue })}</b> : null}</span>
        <b className="gtotal">{money(g.total)}</b><button className="btn" onClick={() => csv(g)}>{t('orders.csv')}</button></header>
      <ul>{g.rows.map(r => <li key={r.key} className={`oline st-${r.state.status} ${r.overdue ? 'is-late' : ''}`}>
        <button className="oline-main" aria-expanded={open === r.key} onClick={() => setOpen(o => o === r.key ? null : r.key)}>
          <span className="oname"><b>{r.product}</b><small>{[r.code, r.rooms.join(', ')].filter(Boolean).join(' · ')}</small></span>
          <span className="oqty">{r.qty} {r.unit}{r.packs ? <small>{r.packs}</small> : null}</span>
          <span className="ototal">{money(r.total)}</span>
          <span className={`st st-o-${r.state.status}`}>{t(`ostatus.${r.state.status}`)}</span>
          {r.overdue && <small className="late">{t('orders.lateSince', { d: new Date(r.state.eta! + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long' }) })}</small>}</button>
        {open === r.key && <OrderEditor row={r} today={today} onSave={st => save(r.key, st)} />}</li>)}</ul>
    </section>)}
  </div>);
}
function OrderEditor({ row, today, onSave }: { row: ProcurementRow; today: string; onSave(s: OrderState): void }){
  const { t } = usePrefs(), [s, setS] = useState<OrderState>(row.state);
  const set = (patch: Partial<OrderState>) => setS(x => ({ ...x, ...patch }));
  // trecerea la „comandat” / „livrat” completează data de azi dacă lipsește
  const status = (st: OrderState['status']) => set({ status: st, ...(st === 'ordered' && !s.orderedAt ? { orderedAt: today } : {}), ...((st === 'delivered' || st === 'installed') && !s.deliveredAt ? { deliveredAt: today } : {}) });
  return (<div className="oedit">
    <div className="ostatuses" role="radiogroup">{ORDER_STATUSES.map(st => <button key={st} type="button" role="radio" aria-checked={s.status === st} onClick={() => status(st)}>{t(`ostatus.${st}`)}</button>)}</div>
    <div className="grid2">
      <label className="f"><span>{t('orders.ref')}</span><input value={s.ref ?? ''} maxLength={80} onChange={e => set({ ref: e.target.value })} /></label>
      <label className="f"><span>{t('orders.orderedAt')}</span><input type="date" value={s.orderedAt ?? ''} onChange={e => set({ orderedAt: e.target.value || undefined })} /></label>
      <label className="f"><span>{t('orders.eta')}</span><input type="date" value={s.eta ?? ''} onChange={e => set({ eta: e.target.value || undefined })} /></label>
      <label className="f"><span>{t('orders.deliveredAt')}</span><input type="date" value={s.deliveredAt ?? ''} onChange={e => set({ deliveredAt: e.target.value || undefined })} /></label></div>
    <label className="f"><span>{t('orders.note')}</span><input value={s.note ?? ''} maxLength={500} onChange={e => set({ note: e.target.value })} /></label>
    <div className="oedit-act">{row.url && <a className="btn" href={row.url} target="_blank" rel="noopener noreferrer">{t('orders.openShop')}</a>}<button className="btn primary" onClick={() => onSave(s)}>{t('orders.save')}</button></div>
  </div>);
}
