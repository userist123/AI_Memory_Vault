'use client';
// Raportul de remedieri pentru echipe (PDF din browser): pe camere, cu număr, meserie, stare, descriere și poze.
import { useEffect, useState } from 'react';
import { DocSkeleton } from './Skeleton';
import type { Snag } from '@/core/site';
import type { Snapshot } from '@/core/types';
import type { ProjectMeta } from '@/core/project-meta';
import { floors } from '@/core/levels';
import { usePrefs } from '@/lib/prefs';
import LoadError from './LoadError';
import { intlLocale } from '@/lib/i18n';

export default function SiteReport({ id }: { id: string }){
  const { t, lang } = usePrefs(), [d, setD] = useState<{ snap: Snapshot; meta: ProjectMeta; items: Snag[] } | null>(null), [err, setErr] = useState(false), [open, setOpen] = useState(true);
  useEffect(() => { Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject()), fetch(`/api/projects/${id}/site`).then(r => r.ok ? r.json() : Promise.reject())])
    .then(([p, items]) => setD({ snap: p.draft, meta: p.meta, items })).catch(() => setErr(true)); }, [id]);
  if (err) return <LoadError />;
  if (!d) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  const rooms = floors(d.snap).flatMap(f => f.rooms), list = d.items.filter(s => !open || s.status === 'open');
  const by = [...rooms.map(r => [r.id, r.name] as const), ['', t('site.noRoom')] as const].map(([rid, name]) => ({ name, items: list.filter(s => (s.roomId ?? '') === rid) })).filter(g => g.items.length);
  const date = (s: string) => new Date(s).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long', year: 'numeric' });
  return (<main className="report">
    <div className="no-print report-bar"><label><input type="checkbox" checked={open} onChange={e => setOpen(e.target.checked)} /> {t('site.reportOpenOnly')}</label><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    <header><span className="kicker">{t('site.reportKicker')}</span><h1>{d.snap.name}</h1>
      <p>{[d.meta.client?.name, d.meta.siteAddress || d.meta.client?.address, date(new Date().toISOString())].filter(Boolean).join(' · ')}</p>
      <p className="muted">{t('site.reportCount', { n: list.length })}</p></header>
    {by.length === 0 && <p>{t('site.none')}</p>}
    {by.map(g => <section key={g.name}><h2>{g.name}</h2>
      {g.items.map(s => <article key={s.id} className={`r-snag st-s-${s.status}`}><div className="r-head"><b>#{s.number} {s.title}</b><span>{t(`trade.${s.trade}`)} · {t(`snag.${s.status}`)} · {date(s.createdAt)}</span></div>
        {s.body && <p>{s.body}</p>}
        {s.photos.length > 0 && <div className="r-photos">{s.photos.map(p => <img key={p} src={`/api/projects/${id}/site/photos/${p}`} alt="" />)}</div>}</article>)}
    </section>)}
  </main>);
}
