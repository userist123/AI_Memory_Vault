'use client';
// Etapa „Șantier”: remedierile găsite la vizite (snag list) — poză făcută pe loc, camera, meseria, descrierea, starea.
// Gândită pentru telefon: butonul mare „+ Remediere”, camera se deschide direct, stările se schimbă dintr-o atingere.
import { useEffect, useMemo, useRef, useState } from 'react';
import { ListSkeleton } from './Skeleton';
import { TRADES, SNAG_STATUSES, snagSummary, type Snag, type SnagStatus, type Trade } from '@/core/site';
import { LOG_LIMITS, localToday, sortLog, type SiteLogEntry } from '@/core/site-log';
import type { Room } from '@/core/types';
import { shrinkPhoto } from '@/lib/image-resize';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';
import { intlLocale } from '@/lib/i18n';
import EmptyState from './EmptyState';
import SchedulePanel from './SchedulePanel';

export default function SitePage({ id, rooms, say }: { id: string; rooms: Room[]; say(s: string): void }){
  const { t, tp, lang } = usePrefs(), [items, setItems] = useState<Snag[] | null>(null), [adding, setAdding] = useState(false), [filter, setFilter] = useState<'open' | 'all'>('open'), [edit, setEdit] = useState<string | null>(null);
  const load = () => fetch(`/api/projects/${id}/site`).then(r => r.ok ? r.json() : null).then(j => j && setItems(j)).catch(() => say(t('site.loadError')));
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  const roomName = (rid: string | null) => rooms.find(r => r.id === rid)?.name ?? t('site.noRoom');
  async function patch(s: Snag, body: Partial<Snag>){ const r = await fetch(`/api/projects/${id}/site/${s.id}`, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok){ const j = await r.json().catch(() => ({})); say(j.error || t('site.saveError')); return; } const n = await r.json(); setItems(xs => (xs ?? []).map(x => x.id === n.id ? n : x)); }
  async function remove(s: Snag){ if (!(await askConfirm({ title: t('site.deleteConfirm', { n: s.number }), confirmLabel: t('dlg.siteDeleteOk'), danger: true }))) return; const r = await fetch(`/api/projects/${id}/site/${s.id}`, { method: 'DELETE' }); if (r.ok) setItems(xs => (xs ?? []).filter(x => x.id !== s.id)); }
  const sum = snagSummary(items ?? []);
  const groups = useMemo(() => { const m = new Map<string, Snag[]>(); for (const s of (items ?? []).filter(x => filter === 'all' || x.status === 'open')) m.set(s.roomId ?? '', [...(m.get(s.roomId ?? '') ?? []), s]); return [...m.entries()]; }, [items, filter]);
  return (<div className="orders site">
    <header className="orders-head"><div><h1>{t('site.title')}</h1><p className="muted">{t('site.sub')}</p></div>
      <dl className="orders-sum"><div className={sum.open ? 'late' : ''}><dt>{t('site.sum.open')}</dt><dd>{sum.open}</dd></div><div><dt>{t('site.sum.fixed')}</dt><dd>{sum.fixed}</dd></div><div><dt>{t('site.sum.verified')}</dt><dd>{sum.verified}</dd></div></dl></header>
    <div className="site-act"><button className="btn primary big" onClick={() => setAdding(true)}>{t('site.add')}</button><a className="btn" href={`/p/${id}/site-report`} target="_blank" rel="noopener">{t('site.report')}</a><a className="btn" href={`/p/${id}/handover`} target="_blank" rel="noopener">{t('ho.kicker')}</a>
      <div className="filters" role="tablist">{(['open', 'all'] as const).map(f => <button key={f} role="tab" aria-selected={filter === f} onClick={() => setFilter(f)}>{t(`site.filter.${f}`)}</button>)}</div></div>
    <SchedulePanel id={id} say={say} />
    {adding && <SnagForm id={id} rooms={rooms} say={say} onDone={s => { setAdding(false); if (s) setItems(xs => [...(xs ?? []), s]); }} />}
    {items === null ? <ListSkeleton label={t('site.loading')} rows={3} /> : groups.length === 0 ? (filter === 'open' && items.length ? <EmptyState title={t('site.allDone')} /> : <EmptyState title={t('empty.site.title')} text={t('site.none')} action={{ label: t('site.add'), onClick: () => setAdding(true) }} />) :
      groups.map(([rid, list]) => <section key={rid} className="supplier"><header><h2>{roomName(rid || null)}</h2><span className="muted">{tp('site.count', list.length)}</span></header>
        <ul>{list.map(s => <li key={s.id} className={`snag st-s-${s.status}`}>
          <div className="snag-main"><b className="snag-n">#{s.number}</b><div className="snag-txt"><b>{s.title}</b>{s.body && <p>{s.body}</p>}
            <small>{t(`trade.${s.trade}`)} · {new Date(s.createdAt).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' })}{s.resolvedAt ? ` · ${t('site.resolvedOn', { d: new Date(s.resolvedAt).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' }) })}` : ''}</small></div></div>
          {s.photos.length > 0 && <div className="snag-photos">{s.photos.map(p => <a key={p} href={`/api/projects/${id}/site/photos/${p}`} target="_blank" rel="noopener"><img src={`/api/projects/${id}/site/photos/${p}`} alt={s.title} loading="lazy" /></a>)}</div>}
          <div className="ostatuses" role="radiogroup" aria-label={t('site.status')}>{SNAG_STATUSES.map(st => <button key={st} type="button" role="radio" aria-checked={s.status === st} onClick={() => patch(s, { status: st })}>{t(`snag.${st}`)}</button>)}
            <button type="button" className="snag-more" onClick={() => setEdit(e => e === s.id ? null : s.id)} aria-expanded={edit === s.id}>{t('site.more')}</button></div>
          {edit === s.id && <div className="oedit-act"><AddPhotos id={id} say={say} onAdded={ids => patch(s, { photos: [...s.photos, ...ids].slice(0, 8) })} /><button className="btn danger" onClick={() => remove(s)}>{t('site.delete')}</button></div>}
        </li>)}</ul></section>)}
    <SiteLog id={id} say={say} />
  </div>);
}
/** Jurnalul zilnic de șantier: cele mai noi intrări primele. O intrare e internă până când comutatorul „Vizibil clientului”
 *  o trimite pe linkul clientului (acolo apar doar data, textul, meteo și pozele). */
function SiteLog({ id, say }: { id: string; say(s: string): void }){
  const { t, tp, lang } = usePrefs(), [items, setItems] = useState<SiteLogEntry[] | null>(null), [form, setForm] = useState<string | null>(null);   // 'new' sau id-ul intrării modificate
  useEffect(() => { fetch(`/api/projects/${id}/site-log`).then(r => r.ok ? r.json() : null).then(j => j && setItems(j)).catch(() => say(t('slog.loadError'))); }, [id]); // eslint-disable-line
  const day = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString(intlLocale(lang), { weekday: 'short', day: 'numeric', month: 'long', year: 'numeric' });
  const put = (n: SiteLogEntry) => setItems(xs => sortLog([...(xs ?? []).filter(x => x.id !== n.id), n]));
  async function setVisible(e: SiteLogEntry, clientVisible: boolean){ const r = await fetch(`/api/projects/${id}/site-log/${e.id}`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ clientVisible }) });
    const j = await r.json().catch(() => ({})); if (!r.ok){ say(j.error || t('slog.saveError')); return; } put(j); }
  async function remove(e: SiteLogEntry){ if (!(await askConfirm({ title: t('slog.deleteConfirm', { d: day(e.date) }), confirmLabel: t('dlg.slogDeleteOk'), danger: true }))) return;
    const r = await fetch(`/api/projects/${id}/site-log/${e.id}`, { method: 'DELETE' }); if (r.ok) setItems(xs => (xs ?? []).filter(x => x.id !== e.id)); else say(t('slog.saveError')); }
  return (<section className="sitelog" aria-labelledby="slog-h">
    <header><div><h2 id="slog-h">{t('slog.title')}</h2><p className="muted">{t('slog.sub')}</p></div>
      {form !== 'new' && <button className="btn primary" onClick={() => setForm('new')}>{t('slog.add')}</button>}</header>
    {form === 'new' && <LogForm id={id} say={say} onDone={n => { setForm(null); if (n) put(n); }} />}
    {items === null ? <ListSkeleton label={t('slog.loading')} rows={2} /> : items.length === 0 ? (form !== 'new' && <p className="muted">{t('slog.none')}</p>) :
      <ul className="slog-list">{items.map(e => form === e.id ? <li key={e.id} className="slog"><LogForm id={id} say={say} entry={e} onDone={n => { setForm(null); if (n) put(n); }} /></li> :
        <li key={e.id} className={`slog ${e.clientVisible ? 'is-visible' : ''}`}>
          <div className="slog-head"><time dateTime={e.date}>{day(e.date)}</time><span className={`slog-badge ${e.clientVisible ? 'on' : ''}`}>{t(e.clientVisible ? 'slog.badgeVisible' : 'slog.badgeInternal')}</span></div>
          <p className="slog-text">{e.text}</p>
          {(e.weather || e.workers != null || e.trades.length > 0) && <small>{[e.weather, e.workers != null ? tp('slog.workersN', e.workers) : '', e.trades.map(tr => t(`trade.${tr}`)).join(', ')].filter(Boolean).join(' · ')}</small>}
          {e.photos.length > 0 && <div className="snag-photos">{e.photos.map(p => <a key={p} href={`/api/projects/${id}/site/photos/${p}`} target="_blank" rel="noopener"><img src={`/api/projects/${id}/site/photos/${p}`} alt={t('slog.photoAlt', { d: day(e.date) })} loading="lazy" /></a>)}</div>}
          <div className="slog-act"><label className="slog-vis"><input type="checkbox" checked={e.clientVisible} onChange={ev => setVisible(e, ev.target.checked)} /><span>{t('slog.visible')}</span></label>
            <button type="button" className="btn" onClick={() => setForm(e.id)}>{t('slog.edit')}</button><button type="button" className="btn danger" onClick={() => remove(e)}>{t('slog.delete')}</button></div>
        </li>)}</ul>}
  </section>);
}
function LogForm({ id, say, entry, onDone }: { id: string; say(s: string): void; entry?: SiteLogEntry; onDone(e: SiteLogEntry | null): void }){
  const { t } = usePrefs(), [date, setDate] = useState(entry?.date ?? localToday()), [text, setText] = useState(entry?.text ?? ''), [weather, setWeather] = useState(entry?.weather ?? ''),
    [workers, setWorkers] = useState(entry?.workers != null ? String(entry.workers) : ''), [trades, setTrades] = useState<Trade[]>(entry?.trades ?? []), [photos, setPhotos] = useState<string[]>(entry?.photos ?? []),
    [clientVisible, setClientVisible] = useState(entry?.clientVisible ?? false), [busy, setBusy] = useState(false);
  const left = LOG_LIMITS.photos - photos.length, w = workers.trim() === '' ? null : Number(workers), wOk = w === null || (Number.isInteger(w) && w >= 0 && w <= LOG_LIMITS.workers);
  async function save(e: React.FormEvent){ e.preventDefault(); setBusy(true);
    const r = await fetch(entry ? `/api/projects/${id}/site-log/${entry.id}` : `/api/projects/${id}/site-log`, { method: entry ? 'PUT' : 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ date, text, weather, workers: w, trades, photos, clientVisible }) }), j = await r.json().catch(() => ({}));
    setBusy(false); if (!r.ok){ say(j.error || t('slog.saveError')); return; } onDone(j); }
  return (<form className="snag-form slog-form" onSubmit={save}>
    <div className="grid2">
      <label className="f"><span>{t('slog.date')}</span><input type="date" value={date} onChange={e => setDate(e.target.value)} required /></label>
      <label className="f"><span>{t('slog.weather')}</span><input value={weather} onChange={e => setWeather(e.target.value)} maxLength={LOG_LIMITS.weather} placeholder={t('slog.weatherPh')} /></label></div>
    <label className="f"><span>{t('slog.text')}</span><textarea rows={4} value={text} onChange={e => setText(e.target.value)} maxLength={LOG_LIMITS.text} required autoFocus={!entry} placeholder={t('slog.textPh')} /></label>
    <label className="f"><span>{t('slog.workers')}</span><input type="number" inputMode="numeric" min={0} max={LOG_LIMITS.workers} step={1} value={workers} onChange={e => setWorkers(e.target.value)} /></label>
    <fieldset className="slog-trades"><legend>{t('slog.trades')}</legend><div className="chips">{TRADES.map(tr => <label key={tr} className="chip"><input type="checkbox" checked={trades.includes(tr)}
      onChange={e => setTrades(e.target.checked ? TRADES.filter(x => x === tr || trades.includes(x)) : trades.filter(x => x !== tr))} />{t(`trade.${tr}`)}</label>)}</div></fieldset>
    <div className="snag-photos">{photos.map(p => <span key={p} className="slog-ph"><img src={`/api/projects/${id}/site/photos/${p}`} alt="" /><button type="button" aria-label={t('slog.removePhoto')} title={t('slog.removePhoto')} onClick={() => setPhotos(x => x.filter(y => y !== p))}>×</button></span>)}
      {left > 0 && <AddPhotos id={id} say={say} max={left} label={photos.length ? t('site.addPhoto') : t('site.takePhoto')} onAdded={ids => setPhotos(x => [...x, ...ids].slice(0, LOG_LIMITS.photos))} />}</div>
    <label className="slog-vis slog-vis-box"><input type="checkbox" checked={clientVisible} onChange={e => setClientVisible(e.target.checked)} /><span><b>{t('slog.visible')}</b><small>{t('slog.visibleHint')}</small></span></label>
    <div className="oedit-act"><button type="button" className="btn" onClick={() => onDone(null)}>{t('site.cancel')}</button><button className="btn primary" disabled={busy || !text.trim() || !date || !wOk}>{t('slog.save')}</button></div>
  </form>);
}
function AddPhotos({ id, say, onAdded, label, max = 8 }: { id: string; say(s: string): void; onAdded(ids: string[]): void; label?: string; max?: number }){
  const { t } = usePrefs(), ref = useRef<HTMLInputElement>(null), [busy, setBusy] = useState(false);
  async function pick(files: FileList | null){ if (!files?.length) return; setBusy(true); const ids: string[] = [];
    try { if (files.length > max) say(t('site.tooMany')); for (const f of [...files].slice(0, max)){ const blob = await shrinkPhoto(f), r = await fetch(`/api/projects/${id}/site/photos`, { method: 'POST', body: blob });
        const j = await r.json().catch(() => ({})); if (!r.ok){ say(j.error || t('site.photoError')); continue; } ids.push(j.id); }
      if (ids.length) onAdded(ids); } finally { setBusy(false); if (ref.current) ref.current.value = ''; } }
  return (<label className="btn photo-btn">{busy ? t('site.uploading') : label ?? t('site.addPhoto')}<input ref={ref} type="file" accept="image/*" capture="environment" multiple hidden disabled={busy} onChange={e => pick(e.target.files)} /></label>);
}
function SnagForm({ id, rooms, say, onDone }: { id: string; rooms: Room[]; say(s: string): void; onDone(s: Snag | null): void }){
  const { t } = usePrefs(), [title, setTitle] = useState(''), [body, setBody] = useState(''), [roomId, setRoomId] = useState<string>(''), [trade, setTrade] = useState<Trade>('general'), [photos, setPhotos] = useState<string[]>([]), [busy, setBusy] = useState(false);
  async function save(e: React.FormEvent){ e.preventDefault(); setBusy(true);
    const r = await fetch(`/api/projects/${id}/site`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ title, body, roomId: roomId || null, trade, photos }) }), j = await r.json().catch(() => ({}));
    setBusy(false); if (!r.ok){ say(j.error || t('site.saveError')); return; } onDone(j); }
  return (<form className="snag-form" onSubmit={save}>
    <div className="snag-photos">{photos.map(p => <img key={p} src={`/api/projects/${id}/site/photos/${p}`} alt="" />)}<AddPhotos id={id} say={say} label={photos.length ? t('site.addPhoto') : t('site.takePhoto')} onAdded={ids => setPhotos(x => [...x, ...ids].slice(0, 8))} /></div>
    <label className="f"><span>{t('site.what')}</span><input value={title} onChange={e => setTitle(e.target.value)} maxLength={160} required autoFocus placeholder={t('site.whatPh')} /></label>
    <div className="grid2">
      <label className="f"><span>{t('site.room')}</span><select value={roomId} onChange={e => setRoomId(e.target.value)}><option value="">{t('site.noRoom')}</option>{rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label>
      <label className="f"><span>{t('site.trade')}</span><select value={trade} onChange={e => setTrade(e.target.value as Trade)}>{TRADES.map(tr => <option key={tr} value={tr}>{t(`trade.${tr}`)}</option>)}</select></label></div>
    <label className="f"><span>{t('site.details')}</span><textarea rows={2} value={body} onChange={e => setBody(e.target.value)} maxLength={2000} /></label>
    <div className="oedit-act"><button type="button" className="btn" onClick={() => onDone(null)}>{t('site.cancel')}</button><button className="btn primary" disabled={busy || !title.trim()}>{t('site.save')}</button></div>
  </form>);
}
