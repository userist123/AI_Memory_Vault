'use client';
// Panoul stratului tehnic: sugestii deterministe, adăugare manuală din plan, numărătoare pe camere pentru meseriași.
import type { Snapshot } from '@/core/types';
import EmptyState from './EmptyState';
import { TECH_KINDS, techCounts, type TechKind } from '@/core/technical';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';

export const TECH_SYMBOL: Record<TechKind, { letter: string; color: string }> = {
  outlet: { letter: 'P', color: '#8B6A45' }, outlet_double: { letter: 'P2', color: '#8B6A45' }, switch: { letter: 'I', color: '#5E636B' }, light_point: { letter: 'L', color: '#B7791F' },
  data: { letter: 'D', color: '#2F6B4F' }, cooker: { letter: 'K', color: '#8a3b2b' }, water_cold: { letter: 'A', color: '#6E8798' }, water_hot: { letter: 'C', color: '#B3261E' }, drain: { letter: 'S', color: '#4a4a48' },
  hvac_split: { letter: 'AC', color: '#2B6CB0' }, thermostat: { letter: 'T', color: '#319795' }, vent_exhaust: { letter: 'V', color: '#4A5568' }, radiator: { letter: 'R', color: '#9B2C2C' },
};
const KINDS = Object.keys(TECH_KINDS) as TechKind[];

export default function TechPanel({ snap, show, kind, onShow, onKind, onSuggest, onClear, onDelete, onDraw }: { onDraw?(): void; snap: Snapshot; show: boolean; kind: TechKind; onShow(v: boolean): void; onKind(k: TechKind): void; onSuggest(): void; onClear(): void; onDelete(id: string): void }){
  const { t, lang } = usePrefs(), pts = snap.tech || [], c = techCounts(pts), label = (k: TechKind) => TECH_KINDS[k].label[lang === 'en' ? 'en' : 'ro'];
  const used = KINDS.filter(k => c.total[k]);
  return (<div className="design">
    <h3>{t('tech.title')}</h3>
    <p className="prov" style={{ margin: 0 }}>{t('tech.intro')}</p>
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      <button className="btn primary" onClick={async () => { if (pts.length && !(await askConfirm({ title: t('dlg.techReplaceTitle'), body: t('tech.replaceConfirm'), confirmLabel: t('dlg.techReplaceOk') }))) return; onSuggest(); }}>{t('tech.suggest')}</button>
      {pts.length > 0 && <button className="btn danger" onClick={async () => { if (await askConfirm({ title: t('tech.clearConfirm'), confirmLabel: t('tech.clear'), danger: true })) onClear(); }}>{t('tech.clear')}</button>}
    </div>
    <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={show} onChange={e => onShow(e.target.checked)} /> {t('tech.show')}</label>
    <label className="f"><span>{t('tech.kind')}</span><select value={kind} onChange={e => onKind(e.target.value as TechKind)}>{KINDS.map(k => <option key={k} value={k}>{TECH_SYMBOL[k].letter} · {label(k)}</option>)}</select></label>
    <p className="prov" style={{ margin: 0 }}>{t('tech.addHint')}</p>
    {!pts.length ? <EmptyState title={t('empty.tech.title')} text={t('tech.none')} action={onDraw ? { label: t('empty.tech.action'), onClick: onDraw } : undefined} /> : <>
      <table className="techtable"><thead><tr><th>{t('tech.room')}</th>{used.map(k => <th key={k} title={label(k)}><i className="techdot" style={{ background: TECH_SYMBOL[k].color }}>{TECH_SYMBOL[k].letter}</i></th>)}</tr></thead>
        <tbody>{snap.floor.rooms.map(r => <tr key={r.id}><td>{r.name}</td>{used.map(k => <td key={k}>{c.byRoom[r.id]?.[k] ?? ''}</td>)}</tr>)}
          <tr className="tot"><td>{t('tech.total')}</td>{used.map(k => <td key={k}>{c.total[k]}</td>)}</tr></tbody></table>
      <div className="list">{snap.floor.rooms.map(r => { const rp = pts.filter(p => p.roomId === r.id); if (!rp.length) return null;
        return <details key={r.id}><summary>{r.name} · {rp.length}</summary>{rp.map(p => <div key={p.id} className="row"><span><i className="techdot" style={{ background: TECH_SYMBOL[p.kind].color }}>{TECH_SYMBOL[p.kind].letter}</i> {label(p.kind)}{p.reason && p.reason !== 'manual' ? ` · ${p.reason}` : ''}</span>
          <button className="btn" onClick={() => onDelete(p.id)}>{t('tech.delete')}</button></div>)}</details>; })}</div>
    </>}
  </div>);
}
