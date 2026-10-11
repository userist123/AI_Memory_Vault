'use client';
// Panoul studioului: proiectele în lucru (cu miniatura planului) și tipologiile de pornire pentru un proiect nou.
import { useEffect, useState } from 'react';
import { TEMPLATES, floorArea } from '../core/templates';
import type { Floor } from '../core/types';
import { formatArea } from '../core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import PrefsSwitcher from '@/components/PrefsSwitcher';
import PlanThumb from '@/components/PlanThumb';
import PhoneAccess from '@/components/PhoneAccess';
import TasksPanel from '@/components/TasksPanel';
import { PROJECT_STATUSES, daysLeft, type ProjectStatus } from '../core/project-meta';
type P = { id: string; name: string; updated_at: string; current_revision: number; status: ProjectStatus; client: string | null; dueDate: string | null };
const ACTIVE: ProjectStatus[] = ['lead', 'offer', 'design', 'execution'];
export default function Home(){
  const { t, tp, lang, units } = usePrefs(), L = lang === 'en' ? 'en' : 'ro';
  const [filter, setFilter] = useState<'all' | 'active' | ProjectStatus>('active'), [list, setList] = useState<P[] | null>(null), [plans, setPlans] = useState<Record<string, Floor>>({}), [name, setName] = useState(''), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  useEffect(() => { fetch('/api/projects').then(r => r.json()).then((ps: P[]) => { setList(ps);
    // miniaturile: planul din ciorna fiecărui proiect (primele 12)
    for (const p of ps.slice(0, 12)) fetch(`/api/projects/${p.id}`).then(r => r.ok ? r.json() : null).then(j => { if (j?.draft?.floor) setPlans(m => ({ ...m, [p.id]: j.draft.floor })); }).catch(() => {}); })
    .catch(() => setErr(t('home.loadFailed'))); }, []); // eslint-disable-line
  async function create(template: string){ setBusy(true); setErr('');
    const r = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name: name.trim() || t('home.defaultName'), template, lang }) }); const j = await r.json();
    if (!r.ok){ setErr(j.error || t('home.error')); setBusy(false); return; } location.href = `/p/${j.id}`; }
  const today = new Date().toISOString().slice(0, 10);
  // active: cele cu termen mai întâi (cel mai apropiat), apoi după ultima modificare
  const shown = (list ?? []).filter(p => filter === 'all' ? true : filter === 'active' ? ACTIVE.includes(p.status) : p.status === filter)
    .sort((a, b) => (a.dueDate && b.dueDate ? a.dueDate.localeCompare(b.dueDate) : a.dueDate ? -1 : b.dueDate ? 1 : 0));
  const count = (f: typeof filter) => (list ?? []).filter(p => f === 'all' ? true : f === 'active' ? ACTIVE.includes(p.status) : p.status === f).length;
  const when = (d: string) => new Date(d).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short', year: 'numeric' });
  return (<main className="home">
    <header className="home-head"><span className="wordmark">Casa<b>3D</b></span><span className="home-sub">{t('home.studio')}</span><PrefsSwitcher /></header>
    {list !== null && <TasksPanel projects={(list ?? []).map(p => ({ id: p.id, name: p.name }))} />}
    <section>
      <div className="home-sec"><h1>{t('home.projects')}</h1>{list && list.length > 0 && <span className="muted">{tp('home.count', list.length)}</span>}</div>
      {err && <p className="issue ERROR">{err}</p>}
      {list && list.length > 0 && <div className="filters" role="tablist">{(['active', 'all', ...PROJECT_STATUSES] as const).filter(f => f === 'active' || f === 'all' || count(f) > 0).map(f =>
        <button key={f} role="tab" aria-selected={filter === f} onClick={() => setFilter(f)}>{f === 'all' ? t('home.filterAll') : f === 'active' ? t('home.filterActive') : t(`pstatus.${f}`)} <i>{count(f)}</i></button>)}</div>}
      {list === null ? <p className="muted">{t('home.loading')}</p> : list.length === 0 ? <p className="muted">{t('home.none')}</p> : shown.length === 0 ? <p className="muted">{t('home.noneFilter')}</p> :
        <div className="pgrid">{shown.map(p => { const left = daysLeft({ status: p.status, dueDate: p.dueDate ?? undefined }, today); return <a key={p.id} className="pcard" href={`/p/${p.id}`}>
          {plans[p.id] ? <PlanThumb floor={plans[p.id]!} label={p.name} /> : <div className="thumb empty" />}
          <span className={`st st-${p.status}`}>{t(`pstatus.${p.status}`)}</span>
          <b>{p.name}</b>{p.client && <span className="pclient">{p.client}</span>}
          <small>{left != null ? <em className={left < 0 ? 'late' : left <= 7 ? 'soon' : ''}>{left < 0 ? t('pcard.late', { n: -left }) : t('pcard.left', { n: left })} · </em> : null}{p.current_revision ? t('home.revision', { n: p.current_revision }) : t('home.noRevisions')} · {when(p.updated_at)}</small></a>; })}</div>}
    </section>
    <section>
      <div className="home-sec"><h2>{t('home.newProject')}</h2></div>
      <div className="new"><label className="f"><span>{t('home.nameAria')}</span><input type="text" value={name} placeholder={t('home.defaultName')} onChange={e => setName(e.target.value)} maxLength={120} /></label></div>
      <div className="tgrid" role="list" aria-label={t('home.templatesAria')}>
        <button className="tcard" role="listitem" disabled={busy} onClick={() => create('blank')}><div className="thumb empty blank">+</div><b>{t('home.blank')}</b><small>{t('home.blankSub')}</small></button>
        {TEMPLATES.map(tm => <button key={tm.id} className="tcard" role="listitem" disabled={busy} onClick={() => create(tm.id)} title={tm.description[L]}>
          <PlanThumb floor={tm.floor} label={tm.name[L]} /><b>{tm.name[L]}</b><small>{tp('home.rooms', tm.floor.rooms.length)} · {formatArea(floorArea(tm.floor), units, lang)}</small></button>)}
      </div>
    </section>
    <PhoneAccess />
  </main>);
}
