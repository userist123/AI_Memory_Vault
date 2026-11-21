'use client';
// Graficul de execuție pe pagina Șantier: diagramă Gantt (bare pe meserii, linia de azi, termenul proiectului) și lista
// editabilă a etapelor. „Propune ordinea lucrărilor” citește proiectul și pune etapele în ordinea uzuală, fără date:
// datele și duratele le introduce utilizatorul (core/schedule.ts).
import { useEffect, useMemo, useState } from 'react';
import { proposeSchedule, scheduleIssues, scheduleRange, SCHEDULE_LIMITS, type Phase } from '@/core/schedule';
import { TRADES, type Trade } from '@/core/site';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { askConfirm } from '@/lib/dialog';

const TRADE_COLOR: Record<Trade, string> = { general: '#6b6259', paint: '#c98a2b', electric: '#e0a100', plumbing: '#2f6fd0', tiles: '#2a9d8f', flooring: '#8a5a33', carpentry: '#7b4f9e', doors: '#a0522d', hvac: '#3c8d5a', cleaning: '#8d99ae' };
const localToday = () => { const d = new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10); };
const day = (s: string) => Date.parse(s + 'T00:00:00Z') / 86400000;

export default function SchedulePanel({ id, say }: { id: string; say(s: string): void }){
  const { t, lang } = usePrefs();
  const [phases, setPhases] = useState<Phase[] | null>(null), [due, setDue] = useState<string | null>(null), [dirty, setDirty] = useState(false), [busy, setBusy] = useState(false), [err, setErr] = useState('');
  useEffect(() => { fetch(`/api/projects/${id}/schedule`).then(r => r.ok ? r.json() : Promise.reject(r)).then(j => { setPhases(j.phases); setDue(j.due); }).catch(() => setErr(t('works.loadError'))); }, [id]); // eslint-disable-line
  const edit = (fn: (ps: Phase[]) => Phase[]) => { setPhases(ps => fn(ps ?? [])); setDirty(true); };
  const patch = (pid: string, p: Partial<Phase>) => edit(ps => ps.map(x => x.id === pid ? { ...x, ...p } : x));
  const newId = (ps: Phase[]) => { let n = ps.length + 1; while (ps.some(p => p.id === `ph-${n}`)) n++; return `ph-${n}`; };
  async function propose(){
    setErr(''); const r = await fetch(`/api/projects/${id}`); if (!r.ok) { setErr(t('works.loadError')); return; }
    const j = await r.json(), proposal = proposeSchedule(j.draft, k => t(k));
    if ((phases?.length ?? 0) > 0 && !(await askConfirm({ title: t('works.replaceConfirm'), confirmLabel: t('works.replaceOk') }))) return;
    setPhases(proposal); setDirty(true); say(t('works.proposed', { n: proposal.length }));
  }
  async function save(){
    if (!phases) return; setBusy(true); setErr('');
    try { const r = await fetch(`/api/projects/${id}/schedule`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ phases }) }), j = await r.json().catch(() => ({}));
      if (!r.ok) { setErr(j.error || t('works.saveError')); return; } setPhases(j.phases); setDue(j.due); setDirty(false); say(t('works.saved')); } finally { setBusy(false); }
  }
  const issues = useMemo(() => phases ? scheduleIssues(phases, due) : [], [phases, due]), range = useMemo(() => phases ? scheduleRange(phases, due) : null, [phases, due]);
  const name = (pid?: string) => phases?.find(p => p.id === pid)?.title ?? '';
  const fmt = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' });
  // diagrama: o coloană de etichete și una de timp; 1 zi = W / zile; liniile de săptămână (luni) ca reper
  const ROW = 26, LABEL = 190, W = 760, dated = (phases ?? []).filter(p => p.start && p.end);
  const gantt = range && dated.length > 0 && (() => { const x = (d: string) => LABEL + (day(d) - day(range.from)) / range.days * (W - LABEL), H = (phases!.length) * ROW + 28, today = localToday();
    const weeks: string[] = []; for (let i = 0; i < range.days; i++) { const d = new Date((day(range.from) + i) * 86400000); if (d.getUTCDay() === 1) weeks.push(d.toISOString().slice(0, 10)); }
    return (<div className="sched-gantt-wrap"><svg className="sched-gantt" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={t('works.ganttAria', { from: fmt(range.from), to: fmt(range.to) })}>
      {weeks.map(w => <g key={w}><line x1={x(w)} x2={x(w)} y1={18} y2={H} stroke="#e4ddd3" /><text x={x(w) + 2} y={12} fontSize={10} fill="#7a7068">{fmt(w)}</text></g>)}
      {phases!.map((p, i) => { const y = 22 + i * ROW;
        return <g key={p.id}><text x={4} y={y + 15} fontSize={11.5} fill="#2b2420">{p.title.length > 28 ? p.title.slice(0, 27) + '…' : p.title}</text>
          {p.start && p.end && <rect x={x(p.start)} y={y + 4} width={Math.max(4, x(p.end) - x(p.start) + (W - LABEL) / range.days)} height={ROW - 10} rx={4} fill={TRADE_COLOR[p.trade]} opacity={p.done ? .4 : .9}><title>{`${p.title} · ${t(`trade.${p.trade}`)} · ${fmt(p.start)}–${fmt(p.end)}`}</title></rect>}</g>; })}
      {day(today) >= day(range.from) && day(today) <= day(range.to) && <g><line x1={x(today)} x2={x(today)} y1={16} y2={H} stroke="#2b2420" strokeDasharray="3 3" /><text x={x(today) + 3} y={H - 4} fontSize={10} fill="#2b2420">{t('works.today')}</text></g>}
      {due && <g><line x1={x(due) + (W - LABEL) / range.days} x2={x(due) + (W - LABEL) / range.days} y1={16} y2={H} stroke="#C63C29" strokeWidth={2} /><text x={x(due) - 2} y={H - 4} fontSize={10} fill="#C63C29" textAnchor="end">{t('works.due', { d: fmt(due) })}</text></g>}
    </svg></div>); })();
  const issueText = (i: (typeof issues)[number]) => i.code === 'BEFORE_PREDECESSOR' ? t('works.issue.before', { p: name(i.phase), a: name(i.after) }) : i.code === 'PAST_DUE' ? t('works.issue.pastDue', { p: name(i.phase), d: due ? fmt(due) : '' }) : t('works.issue.missing', { n: i.count ?? 0 });
  return (<section className="sched">
    <header><h2>{t('works.title')}</h2><span className="muted">{t('works.hint')}</span></header>
    {err && <p role="alert" className="issue ERROR">{err}</p>}
    {phases === null ? <p className="muted">{t('works.loading')}</p> : <>
      {gantt}
      {issues.length > 0 && <ul className="sched-issues">{issues.map((i, k) => <li key={k} className={`issue ${i.code === 'MISSING_DATES' ? '' : 'WARNING'}`}>{issueText(i)}</li>)}</ul>}
      {phases.length === 0 && <p className="muted">{t('works.empty')}</p>}
      {phases.length > 0 && <div className="sched-table" role="table" aria-label={t('works.title')}>{phases.map(p => <div key={p.id} role="row" className={`sched-row ${p.done ? 'done' : ''}`}>
        <i className="sched-dot" style={{ background: TRADE_COLOR[p.trade] }} aria-hidden />
        <input className="sr-title" aria-label={t('works.name')} value={p.title} maxLength={SCHEDULE_LIMITS.titleMax} onChange={e => patch(p.id, { title: e.target.value })} />
        <select className="sr-trade" aria-label={t('works.trade')} value={p.trade} onChange={e => patch(p.id, { trade: e.target.value as Trade })}>{TRADES.map(tr => <option key={tr} value={tr}>{t(`trade.${tr}`)}</option>)}</select>
        <input className="sr-start" type="date" aria-label={t('works.start')} value={p.start ?? ''} onChange={e => patch(p.id, { start: e.target.value || null })} />
        <input className="sr-end" type="date" aria-label={t('works.end')} value={p.end ?? ''} onChange={e => patch(p.id, { end: e.target.value || null })} />
        <select className="sr-after" aria-label={t('works.after')} value={p.after ?? ''} onChange={e => patch(p.id, { after: e.target.value || null })}><option value="">{t('works.noAfter')}</option>{phases.filter(q => q.id !== p.id).map(q => <option key={q.id} value={q.id}>{t('works.afterX', { p: q.title })}</option>)}</select>
        <label className="sched-done"><input type="checkbox" checked={p.done} onChange={e => patch(p.id, { done: e.target.checked })} /> {t('works.doneLabel')}</label>
        <button className="btn-x sr-x" aria-label={t('works.remove', { p: p.title })} onClick={() => edit(ps => ps.filter(x => x.id !== p.id).map(x => x.after === p.id ? { ...x, after: p.after } : x))}>×</button>
      </div>)}</div>}
      <div className="sched-act">
        <button className="btn" onClick={propose}>{t('works.propose')}</button>
        <button className="btn" disabled={(phases.length >= SCHEDULE_LIMITS.maxPhases)} onClick={() => edit(ps => [...ps, { id: newId(ps), title: t('works.newPhase'), trade: 'general', start: null, end: null, after: ps.length ? ps[ps.length - 1]!.id : null, done: false }])}>{t('works.add')}</button>
        <button className="btn primary" disabled={!dirty || busy} onClick={save}>{t('works.save')}</button>
      </div>
    </>}
  </section>);
}
