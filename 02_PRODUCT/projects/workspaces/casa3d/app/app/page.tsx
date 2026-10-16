'use client';
// Panoul studioului: proiectele în lucru (cu miniatura planului) și tipologiile de pornire pentru un proiect nou.
import { useEffect, useRef, useState } from 'react';
import EmptyState from '@/components/EmptyState';
import { CardsSkeleton } from '@/components/Skeleton';
import { singleFlight } from '@/lib/single-flight';
import { TEMPLATES, floorArea, newProjectRequest } from '../core/templates';
import type { Floor } from '../core/types';
import { formatArea } from '../core/format';
import { usePrefs, storedLang } from '@/lib/prefs';
import { askPrompt } from '@/lib/dialog';
import { intlLocale } from '@/lib/i18n';
import PrefsSwitcher from '@/components/PrefsSwitcher';
import PlanThumb from '@/components/PlanThumb';
import PhoneAccess from '@/components/PhoneAccess';
import TasksPanel from '@/components/TasksPanel';
import StudioProfileForm from '@/components/StudioProfileForm';
import { PROJECT_STATUSES, daysLeft, type ProjectStatus } from '../core/project-meta';
type P = { id: string; name: string; updated_at: string; current_revision: number; status: ProjectStatus; client: string | null; dueDate: string | null };
const ACTIVE: ProjectStatus[] = ['lead', 'offer', 'design', 'execution'];
export default function Home(){
  const { t, tp, lang, units, ready } = usePrefs(), L = lang === 'en' ? 'en' : 'ro';
  const [filter, setFilter] = useState<'all' | 'active' | ProjectStatus>('active'), [list, setList] = useState<P[] | null>(null), [plans, setPlans] = useState<Record<string, Floor>>({}), [name, setName] = useState(''), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  const [q, setQ] = useState(''), [sort, setSort] = useState<'due' | 'updated' | 'name'>('due'), [trash, setTrash] = useState<{ id: string; name: string; purgeAt: string }[]>([]), [undo, setUndo] = useState<{ id: string; name: string } | null>(null);
  const loadTrash = () => fetch('/api/projects/trash').then(r => r.ok ? r.json() : []).then(setTrash).catch(() => {});
  const thumbs = (ps: P[]) => { for (const p of ps.slice(0, 24)) fetch(`/api/projects/${p.id}`).then(r => r.ok ? r.json() : null).then(j => { if (j?.draft?.floor) setPlans(m => m[p.id] ? m : ({ ...m, [p.id]: j.draft.floor })); }).catch(() => {}); };
  const reload = () => fetch('/api/projects').then(r => r.ok ? r.json() : Promise.reject(r)).then((ps: P[]) => { if (!Array.isArray(ps)) throw new Error('list'); setList(ps); thumbs(ps); }).catch(() => setErr(t('home.loadFailed')));
  const [demo, setDemo] = useState(false), flight = useRef(singleFlight()).current, [pending, setPending] = useState<Record<string, boolean>>({});
  // o singură cerere pe buton: dublul click în același tick nu mai creează două proiecte / copii
  const once = <T,>(key: string, fn: () => Promise<T>, hold?: (r: T) => boolean) => { if (flight.has(key)) return Promise.resolve(undefined); setPending(m => ({ ...m, [key]: true }));
    return flight.run(key, fn, { hold }).finally(() => { if (!flight.has(key)) setPending(m => { const n = { ...m }; delete n[key]; return n; }); }); };
  useEffect(() => { loadTrash(); fetch('/api/health').then(r => r.json()).then(h => setDemo(!!h.demo)).catch(() => {}); }, []); // eslint-disable-line
  useEffect(() => { if (!undo) return; const tm = setTimeout(() => setUndo(null), 10000); return () => clearTimeout(tm); }, [undo]);
  // ștergerea mută în coș (30 de zile) și se poate anula imediat; ștergerea definitivă cere numele tastat
  const toTrash = (p: P) => once(`trash:${p.id}`, () => toTrashNow(p));
  async function toTrashNow(p: P){ const r = await fetch(`/api/projects/${p.id}`, { method: 'DELETE' }); if (!r.ok){ setErr(t('home.error')); return; } setUndo({ id: p.id, name: p.name }); reload(); loadTrash(); }
  async function restore(id: string){ const r = await fetch(`/api/projects/${id}/restore`, { method: 'POST' }); if (!r.ok){ setErr(t('home.error')); return; } setUndo(null); reload(); loadTrash(); }
  async function purge(x: { id: string; name: string }){ const typed = await askPrompt({ title: t('dlg.purgeTitle', { name: x.name }), body: t('home.purgeConfirm', { name: x.name }), confirmLabel: t('home.purge'), danger: true, input: { label: t('dlg.purgeLabel'), mustEqual: x.name, maxLength: 200 } }); if (typed == null) return;
    const r = await fetch(`/api/projects/${x.id}/purge`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name: typed }) }); const j = await r.json().catch(() => ({}));
    if (!r.ok){ setErr(j.error || t('home.error')); return; } loadTrash(); }
  const duplicate = (p: P) => once(`dup:${p.id}`, () => duplicateNow(p));
  async function duplicateNow(p: P){ const r = await fetch(`/api/projects/${p.id}/duplicate`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' }); const j = await r.json().catch(() => ({}));
    if (!r.ok){ setErr(j.error || t('home.error')); return; } reload(); }
  useEffect(() => { fetch('/api/projects').then(r => r.ok ? r.json() : Promise.reject(r)).then((ps: P[]) => { if (!Array.isArray(ps)) throw new Error('list'); setList(ps);
    // miniaturile: planul din ciorna fiecărui proiect (primele 12)
    for (const p of ps.slice(0, 12)) fetch(`/api/projects/${p.id}`).then(r => r.ok ? r.json() : null).then(j => { if (j?.draft?.floor) setPlans(m => ({ ...m, [p.id]: j.draft.floor })); }).catch(() => {}); })
    .catch(() => setErr(t('home.loadFailed'))); }, []); // eslint-disable-line
  // cheia rămâne ocupată după reușită: pagina trece în editor, orice alt click până atunci e ignorat
  const create = (template: string) => once('create', () => createNow(template), ok => ok === true);
  // limba proiectului = limba interfeței la creare (camerele „Bucătărie, Hol…” și „Casa mea” în română); un clic înainte ca
  // preferința să fie citită folosește aceeași regulă ca PrefsProvider, nu valoarea implicită de la prima randare
  async function createNow(template: string){ setBusy(true); setErr(''); const lg = ready ? lang : storedLang();
    try { const r = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(newProjectRequest(name, template, lg)) }); const j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('home.error')); setBusy(false); return false; } location.href = `/p/${j.id}`; return true; }
    catch { setErr(t('home.error')); setBusy(false); return false; } }
  const today = new Date().toISOString().slice(0, 10);
  // active: cele cu termen mai întâi (cel mai apropiat), apoi după ultima modificare
  const norm = (s: string) => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase(), needle = norm(q.trim());
  const shown = (list ?? []).filter(p => filter === 'all' ? true : filter === 'active' ? ACTIVE.includes(p.status) : p.status === filter)
    .filter(p => !needle || norm(`${p.name} ${p.client ?? ''}`).includes(needle))
    .sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name, lang) : sort === 'updated' ? b.updated_at.localeCompare(a.updated_at)
      : (a.dueDate && b.dueDate ? a.dueDate.localeCompare(b.dueDate) : a.dueDate ? -1 : b.dueDate ? 1 : 0));
  const daysTo = (iso: string) => Math.max(0, Math.ceil((Date.parse(iso) - Date.now()) / 864e5));
  const count = (f: typeof filter) => (list ?? []).filter(p => f === 'all' ? true : f === 'active' ? ACTIVE.includes(p.status) : p.status === f).length;
  const when = (d: string) => new Date(d).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short', year: 'numeric' });
  return (<main className="home">
    <header className="home-head"><span className="wordmark">Casa<b>3D</b></span><span className="home-sub">{t('home.studio')}</span><PrefsSwitcher /></header>
    {demo && <section className="demo-hero"><span className="kicker">{t('demo.kicker')}</span><h1>{t('demo.title')}</h1><p>{t('demo.lead')}</p>
      <ul>{['plan', 'products', 'finishes', 'budget', 'client', 'docs'].map(k => <li key={k}>{t(`demo.f.${k}`)}</li>)}</ul>
      <div className="row"><button className="btn primary" disabled={busy} onClick={() => create('demo')}>{t('demo.open')}</button><span className="muted">{t('demo.note')}</span></div></section>}
    <section>
      <div className="home-sec"><h1>{t('home.projects')}</h1>{list && list.length > 0 && <span className="muted">{tp('home.count', list.length)}</span>}</div>
      {err && <p className="issue ERROR">{err}</p>}
      {list && list.length > 0 && <div className="filters" role="tablist">{(['active', 'all', ...PROJECT_STATUSES] as const).filter(f => f === 'active' || f === 'all' || count(f) > 0).map(f =>
        <button key={f} role="tab" aria-selected={filter === f} onClick={() => setFilter(f)}>{f === 'all' ? t('home.filterAll') : f === 'active' ? t('home.filterActive') : t(`pstatus.${f}`)} <i>{count(f)}</i></button>)}</div>}
      {list && list.length > 3 && <div className="home-tools"><input type="search" value={q} onChange={e => setQ(e.target.value)} placeholder={t('home.search')} aria-label={t('home.search')} />
        <select value={sort} onChange={e => setSort(e.target.value as any)} aria-label={t('home.sort')}><option value="due">{t('home.sortDue')}</option><option value="updated">{t('home.sortUpdated')}</option><option value="name">{t('home.sortName')}</option></select></div>}
      {undo && <div className="undo-toast" role="status">{t('home.trashed', { name: undo.name })} <button className="btn" onClick={() => restore(undo.id)}>{t('home.undo')}</button></div>}
      {list === null ? <CardsSkeleton label={t('home.loading')} /> : list.length === 0 ? <EmptyState title={t('empty.home.title')} text={t('empty.home.text')} action={{ label: t('empty.home.action'), onClick: () => { const el = document.getElementById('new-project-name'); el?.scrollIntoView({ block: 'center' }); el?.focus({ preventScroll: true }); } }} />
        : shown.length === 0 ? <EmptyState title={t('empty.homeFilter.title')} text={t('empty.homeFilter.text')} action={{ label: t('empty.homeFilter.action'), onClick: () => { setFilter('all'); setQ(''); } }} /> :
        <div className="pgrid">{shown.map(p => { const left = daysLeft({ status: p.status, dueDate: p.dueDate ?? undefined }, today); return <div key={p.id} className="pcard-wrap"><a className="pcard" href={`/p/${p.id}`}>
          {plans[p.id] ? <PlanThumb floor={plans[p.id]!} label={p.name} /> : <div className="thumb empty" />}
          <span className={`st st-${p.status}`}>{t(`pstatus.${p.status}`)}</span>
          <b>{p.name}</b>{p.client && <span className="pclient">{p.client}</span>}
          <small>{left != null ? <em className={left < 0 ? 'late' : left <= 7 ? 'soon' : ''}>{left < 0 ? t('pcard.late', { n: -left }) : t('pcard.left', { n: left })} · </em> : null}{p.current_revision ? t('home.revision', { n: p.current_revision }) : t('home.noRevisions')} · {when(p.updated_at)}</small></a>
          <div className="pcard-tools"><button className="link" disabled={!!pending[`dup:${p.id}`]} onClick={() => duplicate(p)}>{t('home.duplicate')}</button><button className="link danger" disabled={!!pending[`trash:${p.id}`]} onClick={() => toTrash(p)}>{t('home.toTrash')}</button></div></div>; })}</div>}
      {trash.length > 0 && <details className="trash"><summary>{t('home.trash', { n: trash.length })}</summary><p className="prov">{t('home.trashNote')}</p>
        <ul>{trash.map(x => <li key={x.id}><b>{x.name}</b><small>{t('home.purgeIn', { n: daysTo(x.purgeAt) })}</small>
          <button className="btn" onClick={() => restore(x.id)}>{t('home.restore')}</button><button className="btn danger" onClick={() => purge(x)}>{t('home.purge')}</button></li>)}</ul></details>}
    </section>
    {list !== null && <TasksPanel projects={(list ?? []).map(p => ({ id: p.id, name: p.name }))} />}
    <section>
      <div className="home-sec"><h2>{t('home.newProject')}</h2></div>
      <div className="new"><label className="f"><span>{t('home.nameAria')}</span><input id="new-project-name" type="text" value={name} placeholder={t('home.defaultName')} onChange={e => setName(e.target.value)} maxLength={120} /></label></div>
      <div className="tgrid" role="list" aria-label={t('home.templatesAria')}>
        <button className="tcard" role="listitem" disabled={busy} onClick={() => create('blank')}><div className="thumb empty blank">+</div><b>{t('home.blank')}</b><small>{t('home.blankSub')}</small></button>
        {TEMPLATES.map(tm => <button key={tm.id} className="tcard" role="listitem" disabled={busy} onClick={() => create(tm.id)} title={tm.description[L]}>
          <PlanThumb floor={tm.floor} label={tm.name[L]} /><b>{tm.name[L]}</b><small>{tp('home.rooms', tm.floor.rooms.length)} · {formatArea(floorArea(tm.floor), units, lang)}</small></button>)}
      </div>
    </section>
    <section><StudioProfileForm /></section>
    {!demo && <PhoneAccess />}
  </main>);
}
