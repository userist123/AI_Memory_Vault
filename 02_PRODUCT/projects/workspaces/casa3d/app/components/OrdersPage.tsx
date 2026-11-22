'use client';
// Etapa „Comenzi”: tot ce e de cumpărat, grupat pe furnizor. Pe fiecare linie: starea (de comandat → ofertă cerută →
// comandat → livrat → montat), nr. comenzii, datele; întârzierile sus. Lista unui furnizor se tipărește sau se descarcă (CSV).
import { useEffect, useMemo, useRef, useState } from 'react';
import EmptyState from './EmptyState';
import { ListSkeleton } from './Skeleton';
import { ORDER_STATUSES, withOrderState, bySupplier, procurementSummary, type OrderState, type ProcurementRow, type SupplierGroup } from '@/core/procurement';
import { formatMoney, formatQty } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';
import { intlLocale } from '@/lib/i18n';
import { safeCell, quoteCell } from '@/core/csv-safe';
import { ImportCsvButtons } from './CustomItemsImport';
import FxRatesBox from './FxRates';
import { FX_COMMON, PROJECT_CURRENCY, fxRowText, formatRate, isCurrencyCode, type FxRates } from '@/core/fx';

type Data = { rows: ProcurementRow[]; groups: SupplierGroup[]; summary: { lines: number; todo: number; ordered: number; delivered: number; installed: number; overdue: number; orderLate: number; orderSoon: number; ordersValue: number };
  /** monedele străine ale produselor la comandă și cursurile proiectului (din ciorna salvată) */
  fx?: { used: string[]; missing: string[]; rates: FxRates } };
const csvCell = (v: unknown) => quoteCell(safeCell(v));   // protecția comună: formule, tab/CR în față, ghilimele
type RoomRef = { id: string; name: string };
export default function OrdersPage({ id, cur, say, projectName, rooms = [], onPickFurniture, fx, onFx }: { onPickFurniture?(): void; id: string; cur: string; say(s: string): void; projectName: string; rooms?: RoomRef[];
  /** cursurile din proiectul deschis în editor și salvarea lor (întoarce true după ce ciorna a ajuns pe server) */
  fx?: FxRates; onFx?(next: FxRates): Promise<boolean> }){
  const { t, lang } = usePrefs(), [data, setData] = useState<Data | null>(null), [open, setOpen] = useState<string | null>(null), [filter, setFilter] = useState<'all' | 'open' | 'overdue'>('all');
  const load = () => fetch(`/api/projects/${id}/orders`).then(r => r.ok ? r.json() : null).then(j => j && setData(j)).catch(() => say(t('orders.loadError')));
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  async function removeItem(r: ProcurementRow){ if (!r.itemId || !(await askConfirm({ title: t('clip.confirmDelete', { name: r.product }), confirmLabel: t('dlg.clipDeleteOk'), danger: true }))) return;
    const res = await fetch(`/api/projects/${id}/items/${r.itemId}`, { method: 'DELETE' }); if (!res.ok){ say(t('clip.saveError')); return; } setOpen(null); load(); }
  async function setCurrency(r: ProcurementRow, currency: string){ if (!r.itemId) return;
    const res = await fetch(`/api/projects/${id}/items/${r.itemId}`, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ currency: currency === PROJECT_CURRENCY ? null : currency }) });
    if (!res.ok){ const j = await res.json().catch(() => ({})); say(j.error || t('clip.saveError')); return; } load(); }
  async function setQty(r: ProcurementRow, qty: number){ if (!r.itemId) return;
    const res = await fetch(`/api/projects/${id}/items/${r.itemId}`, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ qty }) });
    if (!res.ok){ const j = await res.json().catch(() => ({})); say(j.error || t('clip.saveError')); return; } load(); }
  // starea se aplică imediat (contoarele de sus se schimbă la clic); salvarea pe server o face editorul, automat (OrderEditor)
  function applyLocal(key: string, state: OrderState){ setData(d => { if (!d) return d; const rows = withOrderState(d.rows, key, state, new Date().toISOString().slice(0, 10)); return { rows, groups: bySupplier(rows), summary: procurementSummary(rows) }; }); }
  async function save(key: string, state: OrderState){
    const r = await fetch(`/api/projects/${id}/orders`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ key, state }) }).catch(() => null);
    if (!r?.ok){ const j = await r?.json().catch(() => ({})); say(j?.error || t('orders.saveError')); load(); return false; } return true; }
  const money = (v: number | null) => formatMoney(v, cur, lang, '—'), today = new Date().toISOString().slice(0, 10);
  const groups = useMemo(() => (data?.groups ?? []).map(g => ({ ...g, rows: g.rows.filter(r => filter === 'all' ? true : filter === 'overdue' ? r.overdue || r.orderUrgency != null : ['todo', 'quoted'].includes(r.state.status)) })).filter(g => g.rows.length), [data, filter]);
  // prețurile sunt în moneda proiectului; pentru produsele în altă monedă se adaugă coloanele cu prețul original și cursul
  function csv(g: SupplierGroup){ const withFx = g.rows.some(r => r.fx), head = [t('orders.col.product'), t('orders.col.code'), t('orders.col.qty'), t('orders.col.unit'), t('orders.col.packs'), t('orders.col.unitPrice'), t('orders.col.total'), t('orders.col.rooms'), t('orders.col.status'), t('orders.col.ref'), 'URL', ...(withFx ? [t('fx.col.orig'), t('fx.currency'), t('fx.col.rate')] : [])];
    const lines = [head, ...g.rows.map(r => [r.product, r.code ?? '', r.qty, r.unit, r.packs ?? '', r.unitPrice ?? '', r.total ?? '', r.rooms.join(', '), t(`ostatus.${r.state.status}`), r.state.ref ?? '', r.url ?? '',
      ...(withFx ? [r.fx?.price ?? '', r.fx?.currency ?? '', r.fx ? r.fx.rate ?? t('fx.rowMissing', { c: r.fx.currency }) : ''] : [])])].map(l => l.map(csvCell).join(';')).join('\n');
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob(['﻿' + lines], { type: 'text/csv;charset=utf-8' })); a.download = `${projectName || 'proiect'} - ${g.supplier}.csv`.replace(/[\\/:*?"<>|]/g, '_'); a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 2000); }
  if (!data) return <div className="orders"><ListSkeleton label={t('orders.loading')} rows={5} /></div>;
  const s = data.summary;
  return (<div className="orders">
    <header className="orders-head"><div><h1>{t('orders.title')}</h1><p className="muted">{t('orders.sub')}</p><p><a className="btn" href={`/p/${id}/handover`} target="_blank" rel="noopener">{t('ho.kicker')}</a></p></div>
      <dl className="orders-sum"><div><dt>{t('orders.sum.todo')}</dt><dd>{s.todo}</dd></div><div><dt>{t('orders.sum.ordered')}</dt><dd>{s.ordered}</dd></div><div><dt>{t('orders.sum.delivered')}</dt><dd>{s.delivered}</dd></div>
        <div className={s.overdue ? 'late' : ''}><dt>{t('orders.sum.overdue')}</dt><dd>{s.overdue}</dd></div>
        {(s.orderLate + s.orderSoon) > 0 && <div className="late"><dt>{t('orders.sum.orderNow')}</dt><dd>{s.orderLate + s.orderSoon}</dd></div>}<div><dt>{t('orders.sum.value')}</dt><dd>{money(s.ordersValue)}</dd></div></dl></header>
    {onFx && <FxRatesBox projectId={id} used={data.fx?.used ?? []} rates={fx} cur={cur} say={say} onChange={async next => { if (await onFx(next)) await load(); else say(t('fx.saveError')); }} />}
    <ClipAdder id={id} rooms={rooms} say={say} onAdded={load} />
    <ImportCsvButtons id={id} say={say} onDone={load} />
    <div className="filters" role="tablist">{(['all', 'open', 'overdue'] as const).map(f => <button key={f} role="tab" aria-selected={filter === f} onClick={() => setFilter(f)}>{t(`orders.filter.${f}`)}</button>)}</div>
    {groups.length === 0 && (filter !== 'all' ? <EmptyState title={t('empty.ordersFilter.title')} text={t('orders.none')} action={{ label: t('empty.ordersFilter.action'), onClick: () => setFilter('all') }} />
      : <EmptyState title={t('empty.orders.title')} text={t('empty.orders.text')} action={onPickFurniture ? { label: t('empty.orders.action'), onClick: onPickFurniture } : undefined} />)}
    {groups.map(g => <section key={g.supplier} className="supplier">
      <header><h2>{g.supplier}</h2><span className="muted">{t('orders.groupInfo', { n: g.rows.length, open: g.open })}{g.overdue ? ` · ` : ''}{g.overdue ? <b className="late">{t('orders.groupLate', { n: g.overdue })}</b> : null}</span>
        <b className="gtotal">{money(g.total)}</b><button className="btn" onClick={() => csv(g)}>{t('orders.csv')}</button></header>
      <ul>{g.rows.map(r => <li key={r.key} className={`oline st-${r.state.status} ${r.overdue ? 'is-late' : ''}`}>
        <button className="oline-main" aria-expanded={open === r.key} onClick={() => setOpen(o => o === r.key ? null : r.key)}>
          {r.image ? <img className="othumb" src={r.image} alt="" referrerPolicy="no-referrer" loading="lazy" /> : null}<span className="oname"><b>{r.product}</b><small>{[r.code, r.rooms.join(', ')].filter(Boolean).join(' · ')}</small>{r.fx && <FxLine row={r} cur={cur} />}</span>
          <span className="oqty">{formatQty(r.qty, r.unit, lang)}{r.packs ? <small>{r.packs}</small> : null}</span>
          <span className="ototal">{money(r.total)}</span>
          <span className={`st st-o-${r.state.status}`}>{t(`ostatus.${r.state.status}`)}</span>
          {r.orderBy && (r.state.status === 'todo' || r.state.status === 'quoted') && <small className={r.orderUrgency ? 'late' : 'muted'}>{t(r.orderUrgency === 'late' ? 'orders.orderByLate' : 'orders.orderBy', { d: new Date(r.orderBy + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long' }) })}</small>}
          {r.overdue && <small className="late">{t('orders.lateSince', { d: new Date(r.state.eta! + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long' }) })}</small>}</button>
        {open === r.key && <OrderEditor row={r} today={today} onChange={st => applyLocal(r.key, st)} onSave={st => save(r.key, st)} onQty={q => setQty(r, q)} onCurrency={c => setCurrency(r, c)} onRemove={() => removeItem(r)} />}</li>)}</ul>
    </section>)}
  </div>);
}
/**
 * Editorul unei linii: se salvează singur (ca celelalte panouri), la 0,6 s după ultima modificare; la închidere salvează
 * imediat ce a rămas. Starea apare imediat în listă și în contoare (onChange), înainte de răspunsul serverului.
 */
function OrderEditor({ row, today, onChange, onSave, onQty, onCurrency, onRemove }: { row: ProcurementRow; today: string; onChange(s: OrderState): void; onSave(s: OrderState): Promise<boolean>; onQty(q: number): void; onCurrency(c: string): void; onRemove(): void }){
  const { t } = usePrefs(), [s, setS] = useState<OrderState>(row.state), [qty, setQty] = useState(String(row.qty)), [curDraft, setCurDraft] = useState(row.fx?.currency ?? PROJECT_CURRENCY), [st, setSt] = useState<'saved' | 'saving' | 'error'>('saved');
  const timer = useRef<any>(null), pending = useRef<OrderState | null>(null), saveRef = useRef(onSave); saveRef.current = onSave;
  const flush = async () => { clearTimeout(timer.current); const p = pending.current; if (!p) return; pending.current = null; const ok = await saveRef.current(p); if (!pending.current) setSt(ok ? 'saved' : 'error'); };
  useEffect(() => () => { clearTimeout(timer.current); const p = pending.current; if (p){ pending.current = null; void saveRef.current(p); } }, []);
  const set = (patch: Partial<OrderState>) => { const n = { ...(pending.current ?? s), ...patch }; pending.current = n; setS(n); onChange(n); setSt('saving'); clearTimeout(timer.current); timer.current = setTimeout(flush, 600); };
  // trecerea la „comandat” / „livrat” completează data de azi dacă lipsește
  const status = (st: OrderState['status']) => set({ status: st, ...(st === 'ordered' && !s.orderedAt ? { orderedAt: today } : {}), ...((st === 'delivered' || st === 'installed') && !s.deliveredAt ? { deliveredAt: today } : {}) });
  const rowCur = row.fx?.currency ?? PROJECT_CURRENCY;
  return (<div className="oedit">
    <div className="ostatuses" role="radiogroup">{ORDER_STATUSES.map(st => <button key={st} type="button" role="radio" aria-checked={s.status === st} onClick={() => status(st)}>{t(`ostatus.${st}`)}</button>)}</div>
    <div className="grid2">
      <label className="f"><span>{t('orders.ref')}</span><input value={s.ref ?? ''} maxLength={80} onChange={e => set({ ref: e.target.value })} /></label>
      <label className="f"><span>{t('orders.orderedAt')}</span><input type="date" value={s.orderedAt ?? ''} onChange={e => set({ orderedAt: e.target.value || undefined })} /></label>
      <label className="f"><span>{t('orders.eta')}</span><input type="date" value={s.eta ?? ''} onChange={e => set({ eta: e.target.value || undefined })} /></label>
      <label className="f"><span>{t('orders.deliveredAt')}</span><input type="date" value={s.deliveredAt ?? ''} onChange={e => set({ deliveredAt: e.target.value || undefined })} /></label></div>
    <div className="grid2">
      <label className="f"><span>{t('orders.neededBy')}</span><input type="date" value={s.neededBy ?? ''} onChange={e => set({ neededBy: e.target.value || undefined })} /></label>
      <label className="f"><span>{t('orders.warranty')}</span><input type="number" inputMode="numeric" min={0} max={240} value={s.warrantyMonths ?? ''} placeholder="24" onChange={e => set({ warrantyMonths: e.target.value === '' ? undefined : Math.max(0, Math.min(240, Math.round(Number(e.target.value)))) })} /></label>
      <label className="f"><span>{t('orders.leadDays')}</span><input type="number" inputMode="numeric" min={0} max={365} value={s.leadDays ?? ''} placeholder={String(row.leadDays)} onChange={e => set({ leadDays: e.target.value === '' ? undefined : Math.max(0, Math.min(365, Math.round(Number(e.target.value)))) })} /></label></div>
    <label className="f"><span>{t('orders.note')}</span><input value={s.note ?? ''} maxLength={500} onChange={e => set({ note: e.target.value })} /></label>
    {row.kind === 'custom' && <div className="oedit-item"><label className="f"><span>{t('orders.col.qty')} ({row.unit})</span><input type="number" inputMode="decimal" min="0.01" step="any" value={qty} onChange={e => setQty(e.target.value)} /></label>
      <button className="btn" disabled={!(Number(qty) > 0) || Number(qty) === row.qty} onClick={() => onQty(Number(qty))}>{t('clip.updateQty')}</button>
      <label className="f"><span>{t('fx.currency')}</span><CurrencyPick value={curDraft} onChange={setCurDraft} /></label>
      <button className="btn" disabled={!isCurrencyCode(curDraft) || curDraft === rowCur} onClick={() => onCurrency(curDraft)}>{t('fx.setCurrency')}</button><button className="btn danger" onClick={onRemove}>{t('clip.remove')}</button></div>}
    <div className="oedit-act">{row.url && <a className="btn" href={row.url} target="_blank" rel="noopener noreferrer">{t('orders.openShop')}</a>}<small className={`prov ${st === 'error' ? 'err' : ''}`} role="status">{t(`orders.state.${st}`)}</small>{st === 'error' && <button className="btn" onClick={() => { pending.current = s; setSt('saving'); void flush(); }}>{t('com.retry')}</button>}</div>
  </div>);
}

/** Sub numele produsului: „1.200 EUR × 4,9771 = 5.972,52 lei” sau „lipsește cursul EUR” (prețul nu e adunat). */
function FxLine({ row, cur }: { row: ProcurementRow; cur: string }){
  const { t, lang } = usePrefs(), fx = row.fx!, x = fxRowText(fx, row.unitPrice, intlLocale(lang), cur);
  return x.text ? <small className="fxline">{x.text}</small> : <small className="fxline late">{t('fx.rowMissing', { c: fx.currency })}{x.orig ? ` · ${x.orig}` : ''}{fx.rate != null ? ` × ${formatRate(fx.rate, intlLocale(lang))}` : ''}</small>;
}
/** Moneda prețului: lista uzuală sau „altă monedă” (cod ISO de 3 litere, scris de mână). Gol = moneda proiectului. */
function CurrencyPick({ value, onChange }: { value: string; onChange(c: string): void }){
  const { t } = usePrefs(), known = value === '' || (FX_COMMON as readonly string[]).includes(value), [picked, setPicked] = useState(false), other = picked || !known;
  return (<span className={other ? 'cur-pick other' : 'cur-pick'}><select value={other ? 'other' : value || PROJECT_CURRENCY} aria-label={t('fx.currency')} onChange={e => { if (e.target.value === 'other'){ setPicked(true); onChange(''); } else { setPicked(false); onChange(e.target.value); } }}>
    {FX_COMMON.map(c => <option key={c} value={c}>{c}</option>)}<option value="other">{t('fx.other')}</option></select>
    {other && <input value={value} maxLength={3} placeholder="SEK" autoCapitalize="characters" aria-label={t('fx.otherAria')} aria-invalid={!isCurrencyCode(value)} onChange={e => onChange(e.target.value.toUpperCase().replace(/[^A-Z]/g, ''))} />}</span>);
}

type Draft = { name: string; supplier: string; url: string; image: string; sku: string; price: string; currency: string; qty: string; unit: string; roomId: string; costPrice: string; leadDays: string; dimension: string };
const EMPTY: Draft = { name: '', supplier: '', url: '', image: '', sku: '', price: '', currency: '', qty: '1', unit: 'buc', roomId: '', costPrice: '', leadDays: '', dimension: '' };
/** „+ Produs din link”: lipești adresa paginii din magazin, aplicația citește numele, prețul și poza; le verifici și adaugi. */
function ClipAdder({ id, rooms, say, onAdded }: { id: string; rooms: RoomRef[]; say(s: string): void; onAdded(): void }){
  const { t } = usePrefs(), [open, setOpen] = useState(false), [link, setLink] = useState(''), [d, setD] = useState<Draft | null>(null), [busy, setBusy] = useState(false), [err, setErr] = useState('');
  const set = (patch: Partial<Draft>) => setD(x => ({ ...(x ?? EMPTY), ...patch }));
  async function fetchLink(){ setBusy(true); setErr('');
    try { const r = await fetch(`/api/projects/${id}/clip`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ url: link.trim() }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('clip.fetchError')); setD(x => ({ ...(x ?? EMPTY), url: link.trim() })); return; }
      setD({ ...EMPTY, name: j.name ?? '', supplier: j.supplier ?? '', url: j.url ?? link.trim(), image: j.image ?? '', sku: j.sku ?? '', price: j.price != null ? String(j.price) : '', currency: j.currency ?? '' });
    } catch { setErr(t('clip.fetchError')); } finally { setBusy(false); } }
  async function add(){ if (!d) return; setBusy(true); setErr('');
    const body = { name: d.name, supplier: d.supplier, url: d.url.trim() || null, image: d.image || null, sku: d.sku || null, price: d.price.trim() ? Number(d.price.replace(',', '.')) : null, currency: d.currency || null, qty: Number(d.qty.replace(',', '.')), unit: d.unit, roomId: d.roomId || null,
      costPrice: d.costPrice.trim() ? Number(d.costPrice.replace(',', '.')) : null, leadDays: d.leadDays.trim() ? Number(d.leadDays) : null, dimension: d.dimension.trim() || null };
    try { const r = await fetch(`/api/projects/${id}/items`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('clip.saveError')); return; } say(t('clip.added', { name: d.name })); setD(null); setLink(''); setOpen(false); onAdded();
    } finally { setBusy(false); } }
  // moneda: goală = moneda proiectului; altfel un cod de 3 litere (lista uzuală sau scris de mână)
  const curBad = !!d && d.currency !== '' && !isCurrencyCode(d.currency);
  if (!open) return <div className="clip-open"><button className="btn" onClick={() => setOpen(true)}>{t('clip.open')}</button></div>;
  return (<section className="clip" aria-label={t('clip.title')}>
    <header><h2>{t('clip.title')}</h2><button className="btn" onClick={() => { setOpen(false); setD(null); setErr(''); }}>{t('clip.close')}</button></header>
    <p className="muted">{t('clip.hint')}</p>
    <form className="clip-link" onSubmit={e => { e.preventDefault(); if (link.trim()) fetchLink(); }}>
      <input type="url" inputMode="url" placeholder="https://www.dedeman.ro/…" value={link} onChange={e => setLink(e.target.value)} aria-label={t('clip.linkAria')} />
      <button className="btn primary" disabled={busy || !link.trim()}>{busy && !d ? t('clip.fetching') : t('clip.fetch')}</button>
      {!d && <button type="button" className="btn" onClick={() => setD({ ...EMPTY, url: link.trim() })}>{t('clip.manual')}</button>}</form>
    {err && <p className="issue WARNING" role="alert">{err}</p>}
    {d && <div className="clip-card">
      {d.image ? <img src={d.image} alt="" referrerPolicy="no-referrer" /> : <div className="noimg" />}
      <div className="clip-fields">
        <label className="f"><span>{t('clip.name')}</span><input value={d.name} maxLength={200} onChange={e => set({ name: e.target.value })} /></label>
        <div className="grid2"><label className="f"><span>{t('clip.supplier')}</span><input value={d.supplier} maxLength={80} onChange={e => set({ supplier: e.target.value })} /></label>
          <label className="f"><span>{t('orders.col.code')}</span><input value={d.sku} maxLength={60} onChange={e => set({ sku: e.target.value })} /></label>
          <label className="f"><span>{t('orders.col.unitPrice')}{d.currency ? ` (${d.currency})` : ''}</span><input inputMode="decimal" value={d.price} onChange={e => set({ price: e.target.value })} /></label>
          <label className="f"><span>{t('fx.currency')}</span><CurrencyPick value={d.currency} onChange={c => set({ currency: c })} />{curBad && <small className="late">{t('fx.otherBad')}</small>}</label>
          <label className="f"><span>{t('orders.col.qty')}</span><span className="qty-unit"><input inputMode="decimal" value={d.qty} onChange={e => set({ qty: e.target.value })} /><input value={d.unit} maxLength={12} onChange={e => set({ unit: e.target.value })} aria-label={t('orders.col.unit')} /></span></label></div>
        <div className="grid2"><label className="f"><span>{t('cprod.cost')}{d.currency ? ` (${d.currency})` : ''}</span><input inputMode="decimal" value={d.costPrice} onChange={e => set({ costPrice: e.target.value })} /></label>
          <label className="f"><span>{t('cprod.lead')}</span><input inputMode="numeric" value={d.leadDays} onChange={e => set({ leadDays: e.target.value })} /></label></div>
        <label className="f"><span>{t('cprod.dimension')}</span><input value={d.dimension} maxLength={80} onChange={e => set({ dimension: e.target.value })} /></label>
        <label className="f"><span>{t('clip.url')}</span><input type="url" inputMode="url" value={d.url} maxLength={1000} onChange={e => set({ url: e.target.value })} /></label>
        {rooms.length > 0 && <label className="f"><span>{t('clip.room')}</span><select value={d.roomId} onChange={e => set({ roomId: e.target.value })}><option value="">{t('clip.noRoom')}</option>{rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label>}
        <div className="oedit-act"><button className="btn primary" disabled={busy || curBad || !d.name.trim() || !d.supplier.trim() || !(Number(d.qty.replace(',', '.')) > 0)} onClick={add}>{t('clip.add')}</button></div>
      </div></div>}
  </section>);
}
