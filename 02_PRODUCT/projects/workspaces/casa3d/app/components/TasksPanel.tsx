'use client';
// Sarcinile: pe panoul studioului ca „Azi” (întârziate, azi, săptămâna aceasta, pe toate proiectele), în proiect ca listă simplă.
// Adăugare dintr-un rând, bifare dintr-o atingere.
import { useEffect, useState, useRef } from 'react';
import { agenda, type Task } from '@/core/tasks';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import EmptyState from './EmptyState';

type T = Task & { projectName?: string | null };
const today = () => new Date().toISOString().slice(0, 10);
export default function TasksPanel({ projectId, projects }: { projectId?: string; projects?: { id: string; name: string }[] }){
  const { t, lang } = usePrefs(), [tasks, setTasks] = useState<T[] | null>(null), [title, setTitle] = useState(''), [due, setDue] = useState(''), [pid, setPid] = useState(projectId ?? ''), [err, setErr] = useState('');
  const load = () => fetch(`/api/tasks${projectId ? `?project=${projectId}` : ''}`).then(r => r.ok ? r.json() : null).then(j => j && setTasks(j)).catch(() => {});
  useEffect(() => { load(); }, [projectId]); // eslint-disable-line
  const adding = useRef(false), [addBusy, setAddBusy] = useState(false);   // dublul click nu creează două sarcini
  async function add(e: React.FormEvent){ e.preventDefault(); if (adding.current) return; adding.current = true; setAddBusy(true); setErr('');
    try { const r = await fetch('/api/tasks', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ title, due: due || null, projectId: pid || null }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('tasks.error')); return; } setTitle(''); setDue(''); load(); }
    catch { setErr(t('tasks.error')); } finally { adding.current = false; setAddBusy(false); } }
  async function toggle(x: T){ const r = await fetch(`/api/tasks/${x.id}`, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ done: !x.doneAt }) }); if (r.ok) load(); }
  async function remove(x: T){ const r = await fetch(`/api/tasks/${x.id}`, { method: 'DELETE' }); if (r.ok) load(); }
  const fmt = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString(intlLocale(lang), { weekday: 'short', day: 'numeric', month: 'short' });
  const item = (x: T, cls = '') => <li key={x.id} className={`task ${cls} ${x.doneAt ? 'done' : ''}`}>
    <button type="button" className="tick" role="checkbox" aria-checked={!!x.doneAt} aria-label={x.title} onClick={() => toggle(x)}>{x.doneAt ? '✓' : ''}</button>
    <span><b>{x.title}</b><small>{[x.due ? fmt(x.due) : null, !projectId && x.projectName ? x.projectName : null].filter(Boolean).join(' · ')}</small></span>
    {!projectId && x.projectId ? <a className="btn" href={`/p/${x.projectId}`} aria-label={t('tasks.openProject')}>→</a> : <button type="button" className="del" aria-label={t('common.delete')} onClick={() => remove(x)}>×</button>}</li>;
  const a = agenda(tasks ?? [], today()), done = (tasks ?? []).filter(x => x.doneAt);
  const sec = (key: string, list: T[], cls = '') => list.length > 0 && <div className="tsec"><h4 className={cls}>{t(`tasks.${key}`)} <i>{list.length}</i></h4><ul>{list.map(x => item(x, cls))}</ul></div>;
  return (<section className={`tasks ${projectId ? 'in-project' : ''}`}>
    {!projectId && <div className="home-sec"><h2>{t('tasks.today')}</h2><span className="muted">{new Date().toLocaleDateString(intlLocale(lang), { weekday: 'long', day: 'numeric', month: 'long' })}</span></div>}
    {projectId && <h3>{t('tasks.title')}</h3>}
    <form className="task-add" onSubmit={add}>
      <input value={title} onChange={e => setTitle(e.target.value)} maxLength={200} placeholder={t('tasks.placeholder')} aria-label={t('tasks.placeholder')} />
      <input type="date" value={due} onChange={e => setDue(e.target.value)} aria-label={t('tasks.due')} />
      {!projectId && projects && projects.length > 0 && <select value={pid} onChange={e => setPid(e.target.value)} aria-label={t('tasks.project')}><option value="">{t('tasks.noProject')}</option>{projects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select>}
      <button className="btn primary" disabled={!title.trim() || addBusy}>{t('tasks.add')}</button>
    </form>
    {err && <p className="fb-err">{err}</p>}
    {tasks && tasks.length === 0 && <EmptyState title={t('empty.tasks.title')} text={t('tasks.none')} />}
    {sec('overdue', a.overdue, 'late')}{sec('dueToday', a.today, 'soon')}{sec('week', a.week)}{sec('later', a.later)}{sec('noDate', a.noDate)}
    {done.length > 0 && <details className="tsec"><summary>{t('tasks.done')} <i>{done.length}</i></summary><ul>{done.map(x => item(x))}</ul></details>}
  </section>);
}
