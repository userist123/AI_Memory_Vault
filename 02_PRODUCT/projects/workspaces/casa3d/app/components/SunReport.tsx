'use client';
// Raportul de însorire (PDF din browser) pentru client și pentru dosarul proiectantului: verdictul art. 3 pe camere, la
// 21 decembrie, efectul asupra vecinilor, declanșatorul D ≤ H_max, SR EN 17037, vara după AD O, harta orelor pe fiecare
// nivel și metodologia cu convențiile și limitele. Calculul e același ca în editor (core/insolation.ts, core/neighbours.ts).
import { useEffect, useMemo, useState } from 'react';
import type { Snapshot } from '@/core/types';
import type { ProjectMeta } from '@/core/project-meta';
import { floors, levelView } from '@/core/levels';
import { insolationStudy, NORM_HOURS, NORM_REFERENCE_DATE, EN17037_DATE, en17037Level, type Study } from '@/core/insolation';
import { obstaclesFor, neighbourStudy, studyTriggers, corners } from '@/core/neighbours';
import { summerCheck } from '@/core/overheating';
import { daylightStudy } from '@/core/daylight';
import { lightScore } from '@/core/light-class';
import { readSite, PLACES, type SiteInfo } from '@/core/site-location';
import { roomBoundary } from '@/core/room-geometry';
import { localToUtc } from '@/core/solar';
import { heatColor, maxHours } from './SunStudy';
import { DocSkeleton } from './Skeleton';
import LoadError from './LoadError';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { localizeSnapshotRoomNames } from '@/core/templates';

const fmtH = (h: number) => { const m = Math.round(h * 60); return `${Math.floor(m / 60)} h ${String(m % 60).padStart(2, '0')}`; };

export default function SunReport({ id }: { id: string }){
  const { t, lang } = usePrefs(), [d, setD] = useState<{ snap: Snapshot; meta: ProjectMeta } | null>(null), [err, setErr] = useState(false);
  useEffect(() => { fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject()).then(p => setD({ snap: p.draft, meta: p.meta })).catch(() => setErr(true)); }, [id]);
  const snap = useMemo(() => d ? localizeSnapshotRoomNames(d.snap, lang) : null, [d, lang]);
  const site = useMemo(() => snap ? readSite(snap.site) : null, [snap]);
  const res = useMemo(() => snap && site ? compute(snap, site) : null, [snap, site]);
  if (err) return <LoadError />;
  if (!d || !snap) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  const loc = intlLocale(lang), num = (v: number, k = 1) => v.toLocaleString(loc, { maximumFractionDigits: k });
  const place = site?.place ? PLACES.find(p => p.id === site.place)?.name : null;
  return (<main className="report sun-report">
    <div className="no-print report-bar"><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    <header><span className="kicker">{t('sunr.kicker')}</span><h1>{snap.name}</h1>
      <p>{[d.meta.client?.name, d.meta.siteAddress || d.meta.client?.address, new Date().toLocaleDateString(loc, { day: 'numeric', month: 'long', year: 'numeric' })].filter(Boolean).join(' · ')}</p></header>
    {!site || !res ? <p>{t('sunr.noSite')}</p> : <>
      <section><h2>{t('sunr.site')}</h2>
        <p>{t('sunr.siteLine', { place: place ?? t('sun.custom').replace('…', ''), lat: num(site.lat, 2), lon: num(site.lon, 2), tz: site.tz, north: site.northDeg })}</p></section>
      {res.score.cls && <section className={`sun-class sun-class-${res.score.cls}`}><div className="sun-class-badge" aria-hidden="true">{res.score.cls}</div>
        <div><b>{t('sun.cls.title', { cls: res.score.cls })}</b> · {t('sun.cls.points', { p: res.score.points, max: res.score.max })}{res.score.capped ? ` · ${t('sun.cls.capped')}` : ''}
          <ul>{res.score.parts.map(p => <li key={p.key}>{t(`sun.cls.${p.key}`)} {p.got}/{p.max}{!p.known ? ` · ${t('sun.cls.unknown')}` : ''}</li>)}</ul>
          <p className="muted">{t('sunr.clsNote')}</p></div></section>}
      <section><h2>{t('sunr.verdicts')}</h2>
        <p><b>{t('sun.trig.title')}</b> {!site.neighbors?.length ? t('sun.trig.noNb') : res.triggers.some(x => x.required)
          ? (() => { const x = res.triggers.filter(q => q.required).sort((a, b) => a.distM - b.distM)[0]!; return t('sun.trig.yes', { d: `${num(x.distM)} m`, h: `${num(x.hMaxM)} m`, nm: x.name }); })() : t('sun.trig.no')}</p>
        <p><b>{t('sunr.normAll', { h: fmtH(NORM_HOURS) })}</b> {res.low.length ? t('sun.chk.fail', { n: res.low.length, rooms: res.low.join(', ') }) : res.habitable ? t('sun.chk.pass', { n: res.habitable }) : t('sun.chk.none')}</p></section>
      {res.levels.map(L => <section key={L.i} className="sun-r-level"><h2>{L.i === 0 ? t('level.ground') : L.name || t('level.n', { n: L.i })} · {t('sunr.date')}</h2>
        <table className="print-table"><thead><tr><th>{t('sun.col.room')}</th><th>{t('sunr.total')}</th><th>{t('sunr.longest')}</th><th>{t('sun.col.when')}</th><th>{t('sun.col.verdict')}</th><th>{t('sunr.en')}</th><th>{t('sunr.df')}</th><th>{t('sunr.summer')}</th></tr></thead>
          <tbody>{L.norm.rooms.map(r => { const ws = L.norm.windows.filter(w => w.roomId === r.roomId), first = ws.map(w => w.first).filter(Boolean).sort()[0], last = ws.map(w => w.last).filter(Boolean).sort().at(-1);
            const en = L.en.rooms.find(x => x.roomId === r.roomId), su = L.summer.rooms.find(x => x.roomId === r.roomId), df = L.df.rooms.find(x => x.roomId === r.roomId);
            return <tr key={r.roomId}><td>{r.name}</td><td>{fmtH(r.hours)}</td><td>{fmtH(r.longestH)}</td><td>{first && last ? `${first}–${last}` : '—'}</td><td>{t(`sun.verdict.${r.verdict}`)}</td>
              <td>{en && r.habitable ? `${fmtH(en.hours)} · ${t(`sun.en.l${en.windows ? en17037Level(en.hours) : 0}`)}` : '—'}</td>
              <td>{df && df.verdict !== 'none' && (df.target != null || df.mean > 0) ? `${num(df.mean)} %${df.target != null ? ` / ${num(df.target)} % · ${t(`sun.df.v.${df.verdict}`)}` : ''}` : '—'}</td>
              <td>{su && su.verdict !== 'na' ? `${num(su.pct)} % / ${num(su.limitPct ?? 0)} % · ${t(`sun.sum.v.${su.verdict}`)}` : '—'}</td></tr>; })}</tbody></table>
        <HeatPlan snap={snap} i={L.i} grid={L.grid} site={site} /></section>)}
      {res.nb.length > 0 && <section><h2>{t('sun.nb.title')}</h2>
        <table className="print-table"><thead><tr><th>{t('sun.nb.name')}</th><th>{t('sun.nb.h')}</th><th>{t('sunr.dist')}</th><th>{t('sunr.worst')}</th><th>{t('sun.col.verdict')}</th></tr></thead>
          <tbody>{res.nb.map(n => { const nbr = site.neighbors!.find(x => x.id === n.neighbourId)!, tr = res.triggers.find(x => x.neighbourId === n.neighbourId);
            return <tr key={n.neighbourId}><td>{n.name}{nbr.older ? ` (${t('sunr.older')})` : ''}</td><td>{num(nbr.h)} m</td><td>{tr ? `${num(tr.distM)} m` : '—'}</td>
              <td>{n.targets.length ? `${fmtH(n.worstBefore)} → ${fmtH(n.worstAfter)}` : '—'}</td><td>{t(`sun.nb.st.${n.status}`, { was: fmtH(n.worstBefore), got: fmtH(n.worstAfter), n: n.affected, of: n.targets.length })}</td></tr>; })}</tbody></table>
        <p className="muted">{t('sun.nb.note')}</p></section>}
      <section><h2>{t('sunr.method')}</h2>
        <p>{t('sun.norm', { h: fmtH(NORM_HOURS) })}</p><p>{t('sun.method')}</p><p>{t('sun.en.note')}</p><p>{t('sun.df.note')}</p><p>{t('sun.sum.intro')} {t('sun.sum.note')}</p><p>{t('sun.limits')}</p>
        <p className="muted">{t('sunr.sources')}</p></section>
    </>}
  </main>);
}

function compute(snap: Snapshot, site: SiteInfo){
  const toUtc = (d: string, m: number) => localToUtc(d, m, site.tz);
  const levels = floors(snap).map((f, i) => { const v = levelView(snap, i), obs = obstaclesFor(snap, i, site);
    return { i, name: f.name, norm: insolationStudy(v.floor, site, toUtc, { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false, obs }),
      grid: insolationStudy(v.floor, site, toUtc, { date: NORM_REFERENCE_DATE, stepMin: 15, cellM: .3, obs }),
      en: insolationStudy(v.floor, site, toUtc, { date: EN17037_DATE, stepMin: 5, floorGrid: false, obs, point: 'en17037' }), summer: summerCheck(v.floor, site), df: daylightStudy(v.floor, { obs }) }; });
  const hab = levels.flatMap(L => L.norm.rooms.filter(r => r.habitable));
  const nb = neighbourStudy(snap, site, toUtc, NORM_REFERENCE_DATE, NORM_HOURS);
  // clasa de lumină a casei: camerele tuturor nivelurilor puse împreună
  const score = lightScore({ norm: levels.flatMap(L => L.norm.rooms), en: levels.flatMap(L => L.en.rooms), daylight: levels.flatMap(L => L.df.rooms), summer: levels.flatMap(L => L.summer.rooms), nb: site.neighbors?.length ? nb : null });
  return { levels, habitable: hab.length, low: hab.filter(r => r.verdict !== 'ok').map(r => r.name), nb, triggers: studyTriggers(snap, site), score };
}

/** Planul nivelului cu harta orelor de soare la 21 decembrie și vecinii (SVG, metri). */
function HeatPlan({ snap, i, grid, site }: { snap: Snapshot; i: number; grid: Study; site: SiteInfo }){
  const { t } = usePrefs(), f = floors(snap)[i]!, maxH = maxHours(grid);
  const pts = f.rooms.flatMap(r => roomBoundary(r)).concat((site.neighbors ?? []).flatMap(n => corners(n)));
  if (!pts.length) return null;
  const xs = pts.map(p => p[0]), zs = pts.map(p => p[1]), x0 = Math.min(...xs) - 1, z0 = Math.min(...zs) - 1, w = Math.max(...xs) + 1 - x0, h = Math.max(...zs) + 1 - z0;
  return (<figure className="sun-r-plan"><svg viewBox={`${x0} ${z0} ${w} ${h}`} role="img" aria-label={t('sunr.planAria', { max: fmtH(Math.max(2, maxH)) })}>
    {grid.cells.map((c, k) => { const col = heatColor(c.hours, maxH); return col ? <rect key={k} x={c.x - grid.cellM / 2} y={c.z - grid.cellM / 2} width={grid.cellM + .005} height={grid.cellM + .005} fill={col} fillOpacity={.75} /> : null; })}
    {f.rooms.map(r => <polygon key={r.id} points={roomBoundary(r).map(q => q.join(',')).join(' ')} fill="none" stroke="#2B2F36" strokeWidth={.06} />)}
    {f.walls.filter(x => x.status !== 'demolish').map(x => <line key={x.id} x1={x.a[0]} y1={x.a[1]} x2={x.b[0]} y2={x.b[1]} stroke="#2B2F36" strokeWidth={x.thickness} strokeOpacity={.55} />)}
    {(site.neighbors ?? []).map(n => <g key={n.id}><polygon points={corners(n).map(q => q.join(',')).join(' ')} fill="#8A8F98" fillOpacity={.3} stroke="#5E636B" strokeWidth={.05} />
      <text x={n.x} y={n.z} textAnchor="middle" fontSize={.5} fill="#2B2F36">{`${n.name} · ${n.h} m`}</text></g>)}
    <g transform={`translate(${x0 + w - .8} ${z0 + .9}) rotate(${site.northDeg})`}><circle r={.5} fill="#fff" stroke="#5E636B" strokeWidth={.03} />
      <path d="M0 -.42 L.15 .12 L0 .03 L-.15 .12 Z" fill="#2B2F36" /><text y={-.55} textAnchor="middle" fontSize={.3} fontWeight={700} fill="#2B2F36">{t('sun.northLetter')}</text></g>
  </svg><figcaption>{t('sunr.planCaption', { max: fmtH(Math.max(2, maxH)) })}</figcaption></figure>);
}
