'use client';
// „De urmărit azi” pe panoul studioului: ce cere atenție pe toate proiectele active (tranșe, comenzi, șantier, client),
// în ordinea urgenței, cu un buton care duce direct la etapa din proiect unde se rezolvă. Lista vine din /api/attention
// (core/attention.ts); un element poate fi trecut și în lista de sarcini.
import { useEffect, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import EmptyState from './EmptyState';
import type { AttentionItem, AttentionSeverity, AttentionCounts } from '@/core/attention';
import { TASK_LIMITS } from '@/core/tasks';

type Data = { items: AttentionItem[]; counts: AttentionCounts; total: number; today: string; ordersSkipped: number };
const SHORT = 6, SEVERITIES: AttentionSeverity[] = ['urgent', 'soon', 'info'];
// elementele a căror dată e „de când așteaptă”, nu un termen
const SINCE = new Set(['snag.open', 'brief.submitted', 'feedback.open', 'feedback.approvals', 'statement.stale', 'offer.accepted', 'offer.changed']);

// `reloadKey`: se schimbă când lista de proiecte s-a schimbat (coș, restaurare, duplicare), ca panoul să se reîncarce
export default function AttentionPanel({ onTaskAdded, reloadKey = 0 }: { onTaskAdded?: () => void; reloadKey?: number }){
  const { t, tp, lang } = usePrefs(), [data, setData] = useState<Data | null>(null), [failed, setFailed] = useState(false), [filter, setFilter] = useState<AttentionSeverity | null>(null);
  const [all, setAll] = useState(false), [added, setAdded] = useState<Record<string, true>>({}), [busy, setBusy] = useState<Record<string, true>>({}), [note, setNote] = useState(''), [err, setErr] = useState('');
  const adding = useRef(new Set<string>());
  const load = (fresh = false) => fetch(`/api/attention${fresh ? '?fresh=1' : ''}`).then(r => r.ok ? r.json() : Promise.reject(r))
    .then((j: Data) => { if (!Array.isArray(j?.items)) throw new Error('attention'); setData(j); setFailed(false); }).catch(() => setFailed(true));
  // la revenirea în filă (după ce ai rezolvat ceva în alt proiect) lista se reîncarcă
  useEffect(() => { load(); const on = () => { if (document.visibilityState === 'visible') load(); };
    document.addEventListener('visibilitychange', on); return () => document.removeEventListener('visibilitychange', on); }, [reloadKey]); // eslint-disable-line

  const text = (i: AttentionItem) => tp(`attn.${i.msg}`, i.n, i.vars);
  const fmt = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' });
  // sarcina primește textul elementului și proiectul lui; termenul e cel al elementului dacă e în viitor, altfel azi
  async function addTask(i: AttentionItem){ if (adding.current.has(i.id) || added[i.id]) return; adding.current.add(i.id); setBusy(m => ({ ...m, [i.id]: true })); setErr(''); setNote('');
    const due = !SINCE.has(i.msg) && i.due && data && i.due > data.today ? i.due : data?.today ?? null;
    try { const r = await fetch('/api/tasks', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ title: text(i).slice(0, TASK_LIMITS.title), due, projectId: i.projectId }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('attn.addFailed')); return; }
      setAdded(m => ({ ...m, [i.id]: true })); setNote(t('attn.added')); onTaskAdded?.(); }
    catch { setErr(t('attn.addFailed')); }
    finally { adding.current.delete(i.id); setBusy(m => { const n = { ...m }; delete n[i.id]; return n; }); } }

  const items = (data?.items ?? []).filter(i => !filter || i.severity === filter), shown = all ? items : items.slice(0, SHORT);
  return (<section className="attn" aria-labelledby="attn-h">
    <div className="home-sec"><h2 id="attn-h">{t('attn.title')}</h2><span className="muted">{t('attn.sub')}</span></div>
    {failed && !data && <p className="issue ERROR attn-fail" role="alert">{t('attn.loadFailed')} <button type="button" className="btn sm" onClick={() => load(true)}>{t('attn.retry')}</button></p>}
    {!data && !failed && <p className="attn-note" role="status">{t('attn.loading')}</p>}
    {data && data.total === 0 && <EmptyState className="attn-empty" title={t('attn.empty.title')} text={t('attn.empty.text')} />}
    {data && data.total > 0 && <>
      <div className="attn-counts" role="group" aria-label={t('attn.countsAria')}>
        {SEVERITIES.map(s => <button key={s} type="button" className={`attn-count sev-${s}`} aria-pressed={filter === s} disabled={data.counts[s] === 0} onClick={() => { setFilter(f => f === s ? null : s); setAll(false); }}>
          <b>{data.counts[s]}</b><span>{t(`attn.sev.${s}`)}</span></button>)}
        {filter && <button type="button" className="btn sm attn-all" onClick={() => setFilter(null)}>{t('attn.filterAll')}</button>}
      </div>
      {items.length === 0 ? <p className="attn-note">{t('attn.emptyFilter')}</p> : <ul className="attn-list" aria-label={t('attn.listAria')}>
        {shown.map(i => { const what = text(i);
          return <li key={i.id} className={`attn-item sev-${i.severity}`}>
            <span className="attn-sev">{t(`attn.sev.${i.severity}`)}</span>
            <div className="attn-main"><b>{what}</b>
              <small><span className="attn-proj">{i.projectName}</span> · {t(`attn.kind.${i.kind}`)}{i.due ? <> · <time dateTime={i.due}>{t(SINCE.has(i.msg) ? 'attn.since' : 'attn.due', { date: fmt(i.due) })}</time></> : null}</small></div>
            <div className="attn-acts">
              <button type="button" className="btn sm" disabled={!!busy[i.id] || !!added[i.id]} aria-label={added[i.id] ? `${t('attn.added')}: ${what}` : t('attn.addTaskAria', { what })} onClick={() => addTask(i)}>{added[i.id] ? t('attn.added') : t('attn.addTask')}</button>
              <a className="btn primary sm" href={i.href} aria-label={t('attn.openAria', { project: i.projectName, stage: t(`stage.${i.stage}`), what })}>{t('attn.open')}</a>
            </div></li>; })}
      </ul>}
      {items.length > SHORT && <button type="button" className="btn sm attn-more" aria-expanded={all} onClick={() => setAll(v => !v)}>{all ? t('attn.less') : t('attn.more', { n: items.length })}</button>}
      {data.total > data.items.length && <p className="attn-note">{t('attn.truncated', { n: data.items.length, total: data.total })}</p>}
      {data.ordersSkipped > 0 && <p className="attn-note">{t('attn.ordersSkipped', { n: data.ordersSkipped })}</p>}
    </>}
    {err && <p className="fb-err" role="alert">{err}</p>}
    <p className="sr-only" role="status" aria-live="polite">{note}</p>
  </section>);
}
