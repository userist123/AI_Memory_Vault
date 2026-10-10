'use client';
import { useEffect, useState } from 'react';
import { templateSummary } from '../core/templates';
import { formatArea } from '../core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import PrefsSwitcher from '@/components/PrefsSwitcher';
type P = { id: string; name: string; updated_at: string; current_revision: number };
export default function Home(){
  const { t, tp, lang, units } = usePrefs(), L = lang === 'en' ? 'en' : 'ro';
  const [list, setList] = useState<P[] | null>(null), [name, setName] = useState(''), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  useEffect(() => { fetch('/api/projects').then(r => r.json()).then(setList).catch(() => setErr(t('home.loadFailed'))); }, []); // eslint-disable-line
  const templates = templateSummary();
  async function create(template: string){ setBusy(true); setErr('');
    const r = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name: name.trim() || t('home.defaultName'), template, lang }) }); const j = await r.json();
    if (!r.ok){ setErr(j.error || t('home.error')); setBusy(false); return; } location.href = `/p/${j.id}`; }
  return (<main className="home">
    <div style={{ display: 'flex', justifyContent: 'flex-end' }}><PrefsSwitcher /></div>
    <h1>{t('home.title')}</h1>
    <p className="lead">{t('home.lead')}</p>
    <div className="new">
      <input type="text" value={name} placeholder={t('home.defaultName')} onChange={e => setName(e.target.value)} aria-label={t('home.nameAria')} maxLength={120} />
      <button className="btn" disabled={busy} onClick={() => create('blank')}>{t('home.blank')}</button>
    </div>
    <div className="tgrid" role="list" aria-label={t('home.templatesAria')}>
      {templates.map(tm => <article key={tm.id} className="tcard" role="listitem">
        <h2>{tm.name[L]}</h2>
        <p className="tmeta mono">{tp('home.rooms', tm.rooms)} · {formatArea(tm.area, units, lang)}</p>
        <p className="muted">{tm.description[L]}</p>
        <button className="btn primary" disabled={busy} onClick={() => create(tm.id)}>{t('home.start')}</button>
      </article>)}
    </div>
    {err && <p className="issue ERROR">{err}</p>}
    <div className="plist">
      {list === null ? <p className="muted">{t('home.loading')}</p> : list.length === 0 ? <p className="muted">{t('home.none')}</p> :
        list.map(p => <a key={p.id} href={`/p/${p.id}`}><span>{p.name}</span><small>{p.current_revision ? t('home.revision', { n: p.current_revision }) : t('home.noRevisions')}</small><small>{t('home.modified', { when: new Date(p.updated_at).toLocaleString(intlLocale(lang)) })}</small></a>)}
    </div>
  </main>);
}
