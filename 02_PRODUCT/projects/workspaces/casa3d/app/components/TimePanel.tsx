'use client';
// Pontajul proiectului: adaugi rapid orele (azi, „1,5” sau „1:30”, etapa), vezi totalul pe etape și tariful efectiv față de onorariu.
import { useEffect, useState } from 'react';
import { TIME_KINDS, parseDuration, type TimeEntry, type TimeKind } from '@/core/timesheet';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

type Data = { entries: TimeEntry[]; summary: { minutes: number; hours: number; byKind: Partial<Record<TimeKind, number>>; effectiveRate: number | null; billable: number | null } };
const today = () => new Date().toISOString().slice(0, 10);
const STAGE_KINDS = new Set(['survey', 'concept', 'finishes', 'ffe', 'mep', 'docs', 'quote', 'present', 'orders', 'site']);
export default function TimePanel({ id, cur, say }: { id: string; cur: string; say(s: string): void }){
  const { t, lang } = usePrefs(), [d, setD] = useState<Data | null>(null), [f, setF] = useState({ date: today(), dur: '', kind: 'concept' as TimeKind, note: '' }), [busy, setBusy] = useState(false), [all, setAll] = useState(false);
  const load = () => fetch(`/api/projects/${id}/time`).then(r => r.ok ? r.json() : null).then(j => j && setD(j)).catch(() => {});
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  const label = (k: TimeKind) => STAGE_KINDS.has(k) ? t(`stage.${k}`) : t(`time.k.${k}`), mins = parseDuration(f.dur);
  const hm = (m: number) => `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}`;
  async function add(){ if (!mins) return; setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/time`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ date: f.date, minutes: mins, kind: f.kind, note: f.note }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ say(j.error || t('time.error')); return; } setF(x => ({ ...x, dur: '', note: '' })); load(); } finally { setBusy(false); } }
  async function del(e: TimeEntry){ const r = await fetch(`/api/projects/${id}/time?entry=${e.id}`, { method: 'DELETE' }); if (r.ok) load(); else say(t('time.error')); }
  const s = d?.summary, kinds = s ? (Object.entries(s.byKind) as [TimeKind, number][]).sort((a, b) => b[1] - a[1]) : [];
  return (<section className="timep"><h3>{t('time.title')}{s && s.minutes > 0 && <span className="pill">{hm(s.minutes)} h</span>}</h3>
    {s && s.minutes > 0 && <p className="timep-sum">{s.effectiveRate != null ? t('time.rate', { v: formatMoney(s.effectiveRate, cur, lang) }) : s.billable != null ? t('time.billable', { v: formatMoney(s.billable, cur, lang) }) : t('time.noFee')}
      {kinds.length > 0 && <small>{kinds.map(([k, m]) => `${label(k)} ${hm(m)}`).join(' · ')}</small>}</p>}
    <div className="timep-add">
      <input type="date" value={f.date} onChange={e => setF(x => ({ ...x, date: e.target.value }))} aria-label={t('time.date')} />
      <input inputMode="decimal" value={f.dur} onChange={e => setF(x => ({ ...x, dur: e.target.value }))} placeholder={t('time.durPh')} aria-label={t('time.dur')} className={f.dur && !mins ? 'bad' : ''} />
      <select value={f.kind} onChange={e => setF(x => ({ ...x, kind: e.target.value as TimeKind }))} aria-label={t('time.kind')}>{TIME_KINDS.map(k => <option key={k} value={k}>{label(k)}</option>)}</select>
      <input value={f.note} maxLength={300} onChange={e => setF(x => ({ ...x, note: e.target.value }))} placeholder={t('time.notePh')} aria-label={t('time.notePh')} />
      <button className="btn" disabled={busy || !mins || mins < 5} onClick={add}>{t('time.add')}</button></div>
    {d && d.entries.length > 0 && <ul className="timep-list">{(all ? d.entries : d.entries.slice(0, 5)).map(e => <li key={e.id}>
      <span>{new Date(e.date + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' })} · <b>{hm(e.minutes)}</b> · {label(e.kind)}{e.note ? ` — ${e.note}` : ''}</span>
      <button className="btn-x" aria-label={t('time.delete')} onClick={() => del(e)}>×</button></li>)}</ul>}
    {d && d.entries.length > 5 && <button className="btn link" onClick={() => setAll(x => !x)}>{all ? t('time.less') : t('time.more', { n: d.entries.length - 5 })}</button>}
  </section>);
}
