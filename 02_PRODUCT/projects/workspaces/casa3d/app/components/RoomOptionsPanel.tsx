'use client';
// Variante A/B/C pe cameră, în editor: păstrează camera așa cum e, încearcă altceva, păstrează și varianta nouă,
// compară costul și ce diferă, aplică oricare. Alegerea clientului de pe link apare lângă varianta aleasă.
import { useEffect, useMemo, useState } from 'react';
import EmptyState from './EmptyState';
import type { Catalog, MaterialsCatalog, Room, Snapshot } from '@/core/types';
import { captureOption, updateOption, removeOption, applyOption, compareOptions, optionDescription, MAX_OPTIONS, OptionError } from '@/core/room-options';
import { groups } from '@/core/catalog';
import { formatMoney, formatMoneyDelta } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';
import { intlLocale } from '@/lib/i18n';

interface Choice { roomId: string; optionId: string; label: string; author: string; note: string; chosenAt: string; revisionNumber: number }
const newId = () => (globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`);

export default function RoomOptionsPanel({ id, room, snap, cat, mc, cur, onSnap, say }: { id: string; room: Room; snap: Snapshot; cat: Catalog; mc: MaterialsCatalog; cur: string; onSnap(s: Snapshot): void; say(m: string): void }){
  const { t, lang } = usePrefs(), G = useMemo(() => groups(cat), [cat]);
  const [note, setNote] = useState(''), [choices, setChoices] = useState<Choice[]>([]);
  useEffect(() => { fetch(`/api/projects/${id}/choices`).then(r => r.ok ? r.json() : []).then(setChoices).catch(() => {}); }, [id]);
  const list = useMemo(() => compareOptions(snap, cat, mc, room.id), [snap, cat, mc, room.id]);
  const choice = choices.find(c => c.roomId === room.id), active = list.find(o => o.active);
  const run = (fn: () => Snapshot) => { try { onSnap(fn()); } catch (e) { if (e instanceof OptionError) say(e.message); else throw e; } };
  const money = (v: number) => formatMoney(v, cur, lang), date = (s: string) => new Date(s).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' });
  const what = (c: { kind: string; label: string }) => c.kind === 'product' ? (G[c.label]?.label ?? c.label) : t(`ropt.slot.${c.label}`);
  return (<section className="ropt" aria-labelledby={`ropt-${room.id}`}>
    <h3 id={`ropt-${room.id}`}>{t('ropt.title')}</h3>
    {list.length ? <p className="muted">{t('ropt.intro')}</p> : <EmptyState title={t('empty.ropt.title')} text={t('ropt.empty')} />}
    {list.length > 0 && <ol className="ropt-list">{list.map((o, i) => <li key={o.option.id} className={o.active ? 'is-active' : ''}>
      <header><b className="ropt-tag">{o.option.label}</b><span>{(d => d.key ? t(d.key, d.vars) : d.text)(optionDescription(o.option.note, o.option.label, o.changes.filter(c => c.label !== 'other').map(what)))}</span>{o.active && <em>{t('ropt.current')}</em>}</header>
      <p className="ropt-cost"><span>{t('ropt.total')}: {money(o.total)}</span>{i > 0 && <b className={o.delta > 0 ? 'up' : o.delta < 0 ? 'down' : ''}>{o.delta === 0 ? t('ropt.sameCost') : t('ropt.vsA', { d: formatMoneyDelta(o.delta, cur, lang) })}</b>}</p>
      {o.changes.length > 0 && <ul className="ropt-changes">{o.changes.slice(0, 6).map((c, k) => <li key={k}><small>{what(c)}:</small>{' '}{c.label === 'other' ? t('ropt.otherDiff') : <>{c.from} → <b>{c.to}</b></>}</li>)}</ul>}
      {i > 0 && !o.changes.length && <p className="muted">{t('ropt.noDiff')}</p>}
      {o.missing > 0 && <p className="muted">{t('ropt.missing', { n: o.missing })}</p>}
      {choice?.optionId === o.option.id && <p className="ropt-client">{t('ropt.clientChose', { who: choice.author, d: date(choice.chosenAt), rev: choice.revisionNumber })}{choice.note && <q>{choice.note}</q>}</p>}
      <div className="row">
        {!o.active && <button className="btn primary" onClick={() => run(() => applyOption(snap, room.id, o.option.id))}>{t('ropt.apply', { l: o.option.label })}</button>}
        {!o.active && <button className="btn" title={t('ropt.updateTitle')} onClick={() => run(() => updateOption(snap, room.id, o.option.id, new Date().toISOString()))}>{t('ropt.update')}</button>}
        <button className="btn ghost" onClick={async () => { if (await askConfirm({ title: t('ropt.removeConfirm', { l: o.option.label }), confirmLabel: t('dlg.roptRemoveOk', { l: o.option.label }), danger: true })) run(() => removeOption(snap, room.id, o.option.id)); }}>{t('ropt.remove')}</button>
      </div></li>)}</ol>}
    {choice && !list.some(o => o.option.id === choice.optionId) && <p className="muted">{t('ropt.choiceGone', { who: choice.author, l: choice.label })}</p>}
    {list.length < MAX_OPTIONS && (active ? <p className="muted">{t('ropt.changeFirst', { l: active.option.label })}</p> : <div className="ropt-new">
      <input value={note} maxLength={120} placeholder={t('ropt.notePh')} aria-label={t('ropt.notePh')} onChange={e => setNote(e.target.value)} />
      <button className="btn" onClick={() => { run(() => captureOption(snap, room.id, note, newId(), new Date().toISOString())); setNote(''); }}>{list.length ? t('ropt.keepAsNext') : t('ropt.keepAsA')}</button>
    </div>)}
  </section>);
}
