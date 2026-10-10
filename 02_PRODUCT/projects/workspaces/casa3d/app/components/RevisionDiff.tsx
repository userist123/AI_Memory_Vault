'use client';
import { useState } from 'react';
import { formatMoney, formatMoneyDelta } from '@/core/format';
import type { SnapshotDiff, FurnitureChange } from '@/core/diff';

type Result = SnapshotDiff & { from: number; to: number | 'draft' };
const MAX = 8;
// Toate textele vizibile, într-un singur loc, ca să poată fi ridicate de stratul i18n.
const T = {
  title: 'Compară', from: 'De la', fromPh: 'De la…', to: 'Până la', current: 'proiectul curent', revision: 'Revizia', revisionLc: 'revizia', run: 'Compară',
  pickFrom: 'Alege revizia de pornire.', failed: 'Compararea a eșuat.', noDiff: 'Nicio diferență între', and: 'și',
  rooms: 'Camere', walls: 'pereți', furniture: 'mobilier', moved: 'mutate', swapped: 'schimbate', more: 'altele', unpriced: 'fără preț',
  roomsAdded: 'Camere adăugate', roomsRemoved: 'Camere eliminate', roomsChanged: 'Camere modificate',
  furAdded: 'Mobilier adăugat', furRemoved: 'Mobilier eliminat', furMoved: 'Mobilier mutat', furSwapped: 'Variante schimbate', furResized: 'Dimensiuni schimbate', looks: 'elemente cu culoare sau material schimbat', cost: 'Mobilier',
};

function Lines({ title, lines }: { title: string; lines: string[] }){
  if (!lines.length) return null;
  return <div><h4>{title} ({lines.length})</h4><ul>{lines.slice(0, MAX).map((l, i) => <li key={i}>{l}</li>)}{lines.length > MAX && <li className="muted">+{lines.length - MAX} {T.more}</li>}</ul></div>;
}
const fur = (c: FurnitureChange) => `${c.name} · ${c.roomName}${c.detail ? ` · ${c.detail}` : ''}`;

export default function RevisionDiff({ id, revs }: { id: string; revs: { number: number }[] }){
  const [from, setFrom] = useState(''), [to, setTo] = useState('draft'), [res, setRes] = useState<Result | null>(null), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  async function run(){
    setErr(''); setRes(null); if (!from){ setErr(T.pickFrom); return; } setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/revisions/diff?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`); const j = await r.json();
      if (!r.ok) setErr(j.error || T.failed); else setRes(j); }
    catch { setErr(T.failed); } finally { setBusy(false); }
  }
  if (revs.length === 0) return null;
  const label = (n: number | 'draft') => n === 'draft' ? T.current : `${T.revisionLc} ${n}`;
  return (<div className="revdiff"><h4>{T.title}</h4>
    <div className="r">
      <select aria-label={T.from} value={from} onChange={e => setFrom(e.target.value)}><option value="">{T.fromPh}</option>{revs.map(r => <option key={r.number} value={r.number}>{T.revision} {r.number}</option>)}</select>
      <select aria-label={T.to} value={to} onChange={e => setTo(e.target.value)}>{revs.map(r => <option key={r.number} value={r.number}>{T.revision} {r.number}</option>)}<option value="draft">{T.current}</option></select>
      <button className="btn" disabled={busy} onClick={run}>{T.run}</button>
    </div>
    {err && <p role="alert" className="muted">{err}</p>}
    {res && (res.isEmpty ? <p className="muted">{T.noDiff} {label(res.from)} {T.and} {label(res.to)}.</p> : <div>
      <p className="prov">{T.rooms}: +{res.rooms.added.length} −{res.rooms.removed.length} ~{res.rooms.changed.length} · {T.walls}: +{res.walls.added} −{res.walls.removed} ~{res.walls.changed} · {T.furniture}: +{res.furniture.added.length} −{res.furniture.removed.length} {T.moved} {res.furniture.moved.length} {T.swapped} {res.furniture.swapped.length}</p>
      <Lines title={T.roomsAdded} lines={res.rooms.added.map(r => r.name)} />
      <Lines title={T.roomsRemoved} lines={res.rooms.removed.map(r => r.name)} />
      <Lines title={T.roomsChanged} lines={res.rooms.changed.map(r => `${r.name}: ${r.changes.join('; ')}`)} />
      <Lines title={T.furAdded} lines={res.furniture.added.map(fur)} />
      <Lines title={T.furRemoved} lines={res.furniture.removed.map(fur)} />
      <Lines title={T.furMoved} lines={res.furniture.moved.map(fur)} />
      <Lines title={T.furSwapped} lines={res.furniture.swapped.map(fur)} />
      <Lines title={T.furResized} lines={(res.furniture.resized ?? []).map(fur)} />
      {res.looks > 0 && <p className="prov">{res.looks} {T.looks}</p>}
      {Object.entries(res.cost).map(([cur, c]) => <p key={cur} className="prov">{T.cost} ({cur}): {formatMoney(c.before, cur)} → {formatMoney(c.after, cur)} (<b>{formatMoneyDelta(c.delta, cur)}</b>)
        {(c.unknownBefore > 0 || c.unknownAfter > 0) && ` · ${T.unpriced}: ${c.unknownBefore} → ${c.unknownAfter}`}</p>)}
    </div>)}
  </div>);
}
