'use client';
// Panoul de stil (mood board) al proiectului, cameră cu cameră: randarea camerei, paleta ponderată (60-30-10),
// mostrele materialelor alese și piesele, cu prețuri — pagina se tipărește (A4 orizontal) sau se arată clientului.
import { useEffect, useMemo, useState } from 'react';
import { DocSkeleton } from './Skeleton';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { roomBoard, RATIO } from '@/core/moodboard';
import { floors } from '@/core/levels';
import { formatMoney, formatDimsCm } from '@/core/format';
import { swatchStyle } from './MaterialPicker';
import { usePrefs } from '@/lib/prefs';
import LoadError from './LoadError';
import EmptyState from './EmptyState';

export default function MoodBoard({ id }: { id: string }){
  const { t, lang, units } = usePrefs(), [d, setD] = useState<{ name: string; draft: Snapshot; cat: Catalog; mc: MaterialsCatalog; renders: Record<string, string> } | null>(null), [err, setErr] = useState(false);
  useEffect(() => { Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject()), fetch('/api/catalog').then(r => r.json()), fetch('/api/materials').then(r => r.json()),
    fetch(`/api/projects/${id}/renders`).then(r => r.ok ? r.json() : { jobs: [] }).catch(() => ({ jobs: [] }))])
    .then(([p, cat, mc, rj]) => { const renders: Record<string, string> = {}; // cea mai nouă imagine a fiecărei camere
      for (const j of rj.jobs ?? []) if (j.status === 'done') for (const f of j.images) { const room = f.replace(/(-2)?\.png$/, ''); if (!renders[room] && !f.endsWith('-2.png')) renders[room] = `/api/projects/${id}/renders/${j.job}/${f}`; }
      setD({ name: p.name, draft: p.draft, cat, mc, renders }); }).catch(() => setErr(true)); }, [id]);
  const boards = useMemo(() => d ? floors(d.draft).flatMap(f => f.rooms).map(r => roomBoard(d.draft, d.cat, d.mc, r)).filter(b => b.materials.length || b.pieces.length) : [], [d]);
  if (err) return <LoadError />;
  if (!d) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  const money = (n: number | null) => n == null ? '—' : formatMoney(n, 'RON', lang), pct = (v: number) => `${Math.round(v * 100)}%`;
  return (<main className="report board">
    <div className="no-print report-bar"><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    <header><span className="kicker">{t('board.kicker')}</span><h1>{d.name}</h1><p className="muted">{t('board.intro')}</p></header>
    {boards.map(b => { const mat = (id: string) => d.mc.materials.find(m => m.id === id);
      return <section key={b.roomId} className="board-room">
        <h2>{b.name}</h2>
        <div className="board-grid">
          <div className="board-hero">{d.renders[b.roomId] ? <img src={d.renders[b.roomId]} alt={b.name} /> : <EmptyState className="board-noimg" title={t('empty.board.title')} text={t('empty.board.text')} />}</div>
          <div className="board-side">
            <h3>{t('board.palette')}</h3>
            <div className="board-bar" role="img" aria-label={t('board.palette')}>{b.palette.map((c, i) => <span key={i} style={{ background: c.hex, flexGrow: c.share }} title={`${c.label} · ${pct(c.share)}`} />)}</div>
            <ul className="board-colors">{b.palette.slice(0, 6).map((c, i) => <li key={i}><i style={{ background: c.hex }} /><span>{c.label}</span><small>{t(`board.role.${c.role}`)} · {pct(c.share)} · {c.hex}</small></li>)}</ul>
            <p className={b.balanced ? 'prov' : 'issue WARNING'}>{b.balanced ? t('board.balanced', { d: pct(b.ratio.dominant), s: pct(b.ratio.secondary), a: pct(b.ratio.accent) })
              : t('board.unbalanced', { d: pct(b.ratio.dominant), s: pct(b.ratio.secondary), a: pct(b.ratio.accent), dlo: pct(RATIO.dominant[0]), dhi: pct(RATIO.dominant[1]) })}</p>
          </div>
        </div>
        {b.materials.length > 0 && <><h3>{t('board.materials')}</h3><div className="board-swatches">{b.materials.map((m, i) => <figure key={i}><i className="sw" style={swatchStyle(mat(m.id))} /><figcaption><b>{t(`board.mrole.${m.role}`)}</b>{m.name}<small>{m.supplier} · {money(m.price)}/{m.unit}</small></figcaption></figure>)}</div></>}
        {b.pieces.length > 0 && <><h3>{t('board.pieces')}</h3><table className="board-pieces"><tbody>{b.pieces.map(p => <tr key={p.id}><td>{p.hex && <i style={{ background: p.hex }} />}</td><td><small>{p.label}</small>{p.name}</td><td>{p.dims ? formatDimsCm(p.dims, units, lang) : ''}</td><td className="num">{money(p.price)}</td></tr>)}</tbody></table></>}
      </section>; })}
  </main>);
}
