'use client';
// Panoul stratului tehnic: sugestii deterministe, adăugare manuală din plan, numărătoare pe camere pentru meseriași.
import type { Snapshot } from '@/core/types';
import { TECH_KINDS, techCounts, type TechKind } from '@/core/technical';
import { usePrefs } from '@/lib/prefs';

export const TECH_SYMBOL: Record<TechKind, { letter: string; color: string }> = {
  outlet: { letter: 'P', color: '#1F4E79' }, outlet_double: { letter: 'P2', color: '#1F4E79' }, switch: { letter: 'I', color: '#5E636B' }, light_point: { letter: 'L', color: '#B7791F' },
  data: { letter: 'D', color: '#2F6B4F' }, cooker: { letter: 'K', color: '#8a3b2b' }, water_cold: { letter: 'A', color: '#2E6DA4' }, water_hot: { letter: 'C', color: '#B3261E' }, drain: { letter: 'S', color: '#4a4a48' },
};
const KINDS = Object.keys(TECH_KINDS) as TechKind[];

export default function TechPanel({ snap, show, kind, onShow, onKind, onSuggest, onClear, onDelete }: { snap: Snapshot; show: boolean; kind: TechKind; onShow(v: boolean): void; onKind(k: TechKind): void; onSuggest(): void; onClear(): void; onDelete(id: string): void }){
  const { t, lang } = usePrefs(), pts = snap.tech || [], c = techCounts(pts), label = (k: TechKind) => TECH_KINDS[k].label[lang === 'en' ? 'en' : 'ro'];
  const used = KINDS.filter(k => c.total[k]);
  return (<div className="design">
    <h3>{t('tech.title')}</h3>
    <p className="prov" style={{ margin: 0 }}>{t('tech.intro')}</p>
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      <button className="btn primary" onClick={() => { if (pts.length && !confirm(t('tech.replaceConfirm'))) return; onSuggest(); }}>{t('tech.suggest')}</button>
      {pts.length > 0 && <button className="btn danger" onClick={() => { if (confirm(t('tech.clearConfirm'))) onClear(); }}>{t('tech.clear')}</button>}
    </div>
    <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={show} onChange={e => onShow(e.target.checked)} /> {t('tech.show')}</label>
    <label className="f"><span>{t('tech.kind')}</span><select value={kind} onChange={e => onKind(e.target.value as TechKind)}>{KINDS.map(k => <option key={k} value={k}>{TECH_SYMBOL[k].letter} · {label(k)}</option>)}</select></label>
    <p className="prov" style={{ margin: 0 }}>{t('tech.addHint')}</p>
    {!pts.length ? <p className="muted">{t('tech.none')}</p> : <>
      <table className="techtable"><thead><tr><th>{t('tech.room')}</th>{used.map(k => <th key={k} title={label(k)}><i className="techdot" style={{ background: TECH_SYMBOL[k].color }}>{TECH_SYMBOL[k].letter}</i></th>)}</tr></thead>
        <tbody>{snap.floor.rooms.map(r => <tr key={r.id}><td>{r.name}</td>{used.map(k => <td key={k}>{c.byRoom[r.id]?.[k] ?? ''}</td>)}</tr>)}
          <tr className="tot"><td>{t('tech.total')}</td>{used.map(k => <td key={k}>{c.total[k]}</td>)}</tr></tbody></table>
      <div className="list">{snap.floor.rooms.map(r => { const rp = pts.filter(p => p.roomId === r.id); if (!rp.length) return null;
        return <details key={r.id}><summary>{r.name} · {rp.length}</summary>{rp.map(p => <div key={p.id} className="row"><span><i className="techdot" style={{ background: TECH_SYMBOL[p.kind].color }}>{TECH_SYMBOL[p.kind].letter}</i> {label(p.kind)}{p.reason && p.reason !== 'manual' ? ` · ${p.reason}` : ''}</span>
          <button className="btn" onClick={() => onDelete(p.id)}>{t('tech.delete')}</button></div>)}</details>; })}</div>
    </>}
  </div>);
}
