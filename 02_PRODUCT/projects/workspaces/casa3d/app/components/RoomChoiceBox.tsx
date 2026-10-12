'use client';
// Pe linkul clientului, sub planșa camerei: variantele pregătite de designer (A/B/C), cu costul total și ce diferă,
// și alegerea clientului (se poate răzgândi; ultima alegere rămâne).
import { useMemo, useState } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { compareOptions } from '@/core/room-options';
import { groups } from '@/core/catalog';
import { formatMoney, formatMoneyDelta } from '@/core/format';
import { usePrefs } from '@/lib/prefs';

export interface ClientChoice { roomId: string; optionId: string; label: string; author: string; note: string; chosenAt: string }
export default function RoomChoiceBox({ token, roomId, roomName, snap, cat, mc, cur, chosen, onChosen }: { token: string; roomId: string; roomName: string; snap: Snapshot; cat: Catalog; mc: MaterialsCatalog; cur: string; chosen?: ClientChoice; onChosen(c: ClientChoice): void }){
  const { t, lang } = usePrefs(), G = useMemo(() => groups(cat), [cat]);
  const list = useMemo(() => compareOptions(snap, cat, mc, roomId), [snap, cat, mc, roomId]);
  const [author, setAuthor] = useState(() => { try { return localStorage.getItem('casa3d.clientName') ?? ''; } catch { return ''; } });
  const [note, setNote] = useState(''), [busy, setBusy] = useState(false), [err, setErr] = useState('');
  if (list.length < 2) return null;
  const what = (c: { kind: string; label: string }) => c.kind === 'product' ? (G[c.label]?.label ?? c.label) : t(`ropt.slot.${c.label}`);
  const pick = async (optionId: string) => { setErr(''); if (!author.trim()){ setErr(t('ropt.nameNeeded')); return; } setBusy(true);
    try { const r = await fetch(`/api/share/${token}/choices`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId, optionId, author, note }) });
      const j = await r.json(); if (!r.ok){ setErr(j.error ?? t('ropt.error')); return; }
      try { localStorage.setItem('casa3d.clientName', author.trim()); } catch { /* fără stocare locală */ }
      onChosen(j); setNote(''); }
    catch { setErr(t('ropt.error')); } finally { setBusy(false); } };
  return (<section className="ropt ropt-client-box no-print" aria-labelledby={`rc-${roomId}`}>
    <h3 id={`rc-${roomId}`}>{t('ropt.clientTitle', { room: roomName })}</h3>
    <p className="muted">{t('ropt.clientIntro')}</p>
    <ol className="ropt-list ropt-grid">{list.map((o, i) => <li key={o.option.id} className={chosen?.optionId === o.option.id ? 'is-active' : ''}>
      <header><b className="ropt-tag">{o.option.label}</b><span>{o.option.note || t('ropt.noNote')}</span>{o.active && <em>{t('ropt.shown')}</em>}</header>
      <p className="ropt-cost"><span>{t('ropt.total')}: {formatMoney(o.total, cur, lang)}</span>{i > 0 && <b className={o.delta > 0 ? 'up' : o.delta < 0 ? 'down' : ''}>{o.delta === 0 ? t('ropt.sameCost') : t('ropt.vsA', { d: formatMoneyDelta(o.delta, cur, lang) })}</b>}</p>
      {i > 0 && o.changes.length > 0 && <ul className="ropt-changes">{o.changes.slice(0, 6).map((c, k) => <li key={k}><small>{what(c)}</small>{c.label === 'other' ? t('ropt.otherDiff') : <>{c.from} → <b>{c.to}</b></>}</li>)}</ul>}
      {chosen?.optionId === o.option.id ? <p className="ropt-client">{t('ropt.youChose')}</p>
        : <button className="btn primary" disabled={busy} onClick={() => pick(o.option.id)}>{t('ropt.choose', { l: o.option.label })}</button>}
    </li>)}</ol>
    <div className="ropt-new"><input value={author} maxLength={60} placeholder={t('ropt.namePh')} aria-label={t('ropt.namePh')} onChange={e => setAuthor(e.target.value)} />
      <input value={note} maxLength={500} placeholder={t('ropt.clientNotePh')} aria-label={t('ropt.clientNotePh')} onChange={e => setNote(e.target.value)} /></div>
    {err && <p className="err" role="alert">{err}</p>}
  </section>);
}
