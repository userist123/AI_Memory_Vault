'use client';
import { useState } from 'react';
import { formatMoney, formatMoneyDelta, type Units } from '@/core/format';
import { translator, type Lang } from '@/lib/i18n';
import type { SnapshotDiff, FurnitureChange } from '@/core/diff';

type Result = SnapshotDiff & { from: number; to: number | 'draft' };
const MAX = 8;

function Lines({ title, lines, more }: { title: string; lines: string[]; more: string }){
  if (!lines.length) return null;
  return <div><h4>{title} ({lines.length})</h4><ul>{lines.slice(0, MAX).map((l, i) => <li key={i}>{l}</li>)}{lines.length > MAX && <li className="muted">+{lines.length - MAX} {more}</li>}</ul></div>;
}
const fur = (c: FurnitureChange) => `${c.name} · ${c.roomName}${c.detail ? ` · ${c.detail}` : ''}`;

// `units` e primit pentru uniformitate cu celelalte panouri; diferențele nu conțin lungimi afișate aici.
export default function RevisionDiff({ id, revs, lang = 'ro' }: { id: string; revs: { number: number }[]; lang?: Lang; units?: Units }){
  const t = translator(lang);
  const [from, setFrom] = useState(''), [to, setTo] = useState('draft'), [res, setRes] = useState<Result | null>(null), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  async function run(){
    setErr(''); setRes(null); if (!from){ setErr(t('diff.pickFrom')); return; } setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/revisions/diff?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`); const j = await r.json();
      if (!r.ok) setErr(j.error || t('diff.failed')); else setRes(j); }
    catch { setErr(t('diff.failed')); } finally { setBusy(false); }
  }
  if (revs.length === 0) return null;
  const label = (n: number | 'draft') => n === 'draft' ? t('diff.current') : `${t('diff.revisionLc')} ${n}`;
  return (<div className="revdiff"><h4>{t('diff.title')}</h4>
    <div className="r">
      <select aria-label={t('diff.from')} value={from} onChange={e => setFrom(e.target.value)}><option value="">{t('diff.fromPh')}</option>{revs.map(r => <option key={r.number} value={r.number}>{t('diff.revision')} {r.number}</option>)}</select>
      <select aria-label={t('diff.to')} value={to} onChange={e => setTo(e.target.value)}>{revs.map(r => <option key={r.number} value={r.number}>{t('diff.revision')} {r.number}</option>)}<option value="draft">{t('diff.current')}</option></select>
      <button className="btn" disabled={busy} onClick={run}>{t('diff.run')}</button>
    </div>
    {err && <p role="alert" className="muted">{err}</p>}
    {res && (res.isEmpty ? <p className="muted">{t('diff.noDiff')} {label(res.from)} {t('diff.and')} {label(res.to)}.</p> : <div>
      <p className="prov">{t('diff.rooms')}: +{res.rooms.added.length} −{res.rooms.removed.length} ~{res.rooms.changed.length} · {t('diff.walls')}: +{res.walls.added} −{res.walls.removed} ~{res.walls.changed} · {t('diff.furniture')}: +{res.furniture.added.length} −{res.furniture.removed.length} {t('diff.moved')} {res.furniture.moved.length} {t('diff.swapped')} {res.furniture.swapped.length}</p>
      <Lines title={t('diff.roomsAdded')} lines={res.rooms.added.map(r => r.name)} more={t('diff.more')} />
      <Lines title={t('diff.roomsRemoved')} lines={res.rooms.removed.map(r => r.name)} more={t('diff.more')} />
      <Lines title={t('diff.roomsChanged')} lines={res.rooms.changed.map(r => `${r.name}: ${r.changes.join('; ')}`)} more={t('diff.more')} />
      <Lines title={t('diff.furAdded')} lines={res.furniture.added.map(fur)} more={t('diff.more')} />
      <Lines title={t('diff.furRemoved')} lines={res.furniture.removed.map(fur)} more={t('diff.more')} />
      <Lines title={t('diff.furMoved')} lines={res.furniture.moved.map(fur)} more={t('diff.more')} />
      <Lines title={t('diff.furSwapped')} lines={res.furniture.swapped.map(fur)} more={t('diff.more')} />
      <Lines title={t('diff.furResized')} lines={(res.furniture.resized ?? []).map(fur)} more={t('diff.more')} />
      {res.looks > 0 && <p className="prov">{res.looks} {t('diff.looks')}</p>}
      {Object.entries(res.cost).map(([cur, c]) => <p key={cur} className="prov">{t('diff.cost')} ({cur}): {formatMoney(c.before, cur, lang)} → {formatMoney(c.after, cur, lang)} (<b>{formatMoneyDelta(c.delta, cur, lang)}</b>)
        {(c.unknownBefore > 0 || c.unknownAfter > 0) && ` · ${t('diff.unpriced')}: ${c.unknownBefore} → ${c.unknownAfter}`}</p>)}
    </div>)}
  </div>);
}
