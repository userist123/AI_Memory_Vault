'use client';
// Panoul stratului tehnic: sugestii deterministe, adăugare manuală din plan, numărătoare pe camere pentru meseriași.
import type { Snapshot } from '@/core/types';
import EmptyState from './EmptyState';
import { TECH_KINDS, techCounts, type TechKind, type TechPoint } from '@/core/technical';
import { PLAN_LIMITS, RADIATOR_MODELS } from '@/core/plan-limits';
import { isSizedRadiator } from '@/core/tech-geometry';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';

export const TECH_SYMBOL: Record<TechKind, { letter: string; color: string }> = {
  outlet: { letter: 'P', color: '#8B6A45' }, outlet_double: { letter: 'P2', color: '#8B6A45' }, switch: { letter: 'I', color: '#5E636B' }, light_point: { letter: 'L', color: '#B7791F' },
  data: { letter: 'D', color: '#2F6B4F' }, cooker: { letter: 'K', color: '#8a3b2b' }, water_cold: { letter: 'A', color: '#6E8798' }, water_hot: { letter: 'C', color: '#B3261E' }, drain: { letter: 'S', color: '#4a4a48' },
  hvac_split: { letter: 'AC', color: '#2B6CB0' }, thermostat: { letter: 'T', color: '#319795' }, vent_exhaust: { letter: 'V', color: '#4A5568' }, radiator: { letter: 'R', color: '#9B2C2C' },
};
const KINDS = Object.keys(TECH_KINDS) as TechKind[];

/** `onPatch`: mărimile și tipul caloriferului (cm în câmpuri, m în model, aduse în limitele serverului); `issues`: avertismentele punctelor (aer condiționat). */
export default function TechPanel({ snap, show, kind, onShow, onKind, onSuggest, onClear, onDelete, onDraw, onPatch, issues }: { onDraw?(): void; snap: Snapshot; show: boolean; kind: TechKind; onShow(v: boolean): void; onKind(k: TechKind): void; onSuggest(): void; onClear(): void; onDelete(id: string): void;
  onPatch?(id: string, patch: Partial<Pick<TechPoint, 'widthM' | 'heightM' | 'model'>>): void; issues?: { text: string; severity: string }[] }){
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
    <p className="prov" style={{ margin: 0 }}>{t(kind === 'radiator' ? 'editor.hintRadiator' : 'tech.addHint')}</p>
    {(issues ?? []).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{i.text}</div>)}
    {!pts.length ? <EmptyState title={t('empty.tech.title')} text={t('tech.none')} action={onDraw ? { label: t('empty.tech.action'), onClick: onDraw } : undefined} /> : <>
      <table className="techtable"><thead><tr><th>{t('tech.room')}</th>{used.map(k => <th key={k} title={label(k)}><i className="techdot" style={{ background: TECH_SYMBOL[k].color }}>{TECH_SYMBOL[k].letter}</i></th>)}</tr></thead>
        <tbody>{snap.floor.rooms.map(r => <tr key={r.id}><td>{r.name}</td>{used.map(k => <td key={k}>{c.byRoom[r.id]?.[k] ?? ''}</td>)}</tr>)}
          <tr className="tot"><td>{t('tech.total')}</td>{used.map(k => <td key={k}>{c.total[k]}</td>)}</tr></tbody></table>
      <div className="list">{snap.floor.rooms.map(r => { const rp = pts.filter(p => p.roomId === r.id); if (!rp.length) return null;
        return <details key={r.id}><summary>{r.name} · {rp.length}</summary>{rp.map(p => <div key={p.id} className="row"><span><i className="techdot" style={{ background: TECH_SYMBOL[p.kind].color }}>{TECH_SYMBOL[p.kind].letter}</i> {label(p.kind)}{p.reason && p.reason !== 'manual' ? ` · ${p.reason}` : ''}</span>
          <button className="btn" onClick={() => onDelete(p.id)}>{t('tech.delete')}</button>
          {onPatch && isSizedRadiator(p) && <RadiatorFields p={p} onPatch={patch => onPatch(p.id, patch)} />}</div>)}</details>; })}</div>
    </>}
  </div>);
}

/** Lățimea, înălțimea (cm) și tipul unui calorifer, în rândul lui din listă. */
function RadiatorFields({ p, onPatch }: { p: TechPoint; onPatch(patch: Partial<Pick<TechPoint, 'widthM' | 'heightM' | 'model'>>): void }){
  const { t } = usePrefs(), R = PLAN_LIMITS.radiator;
  const cm = (k: 'widthM' | 'heightM', lo: number, hi: number) => (v: string) => { const x = Number(v); if (!Number.isFinite(x) || v.trim() === '') return; onPatch({ [k]: Math.round(Math.min(hi, Math.max(lo, x / 100)) * 1000) / 1000 }); };
  return (<div className="grid2" style={{ gridColumn: '1 / -1', width: '100%' }}>
    <label className="f"><span>{t('radiator.width')}</span><input type="number" min={R.widthMinM * 100} max={R.widthMaxM * 100} value={Math.round((p.widthM ?? 0) * 100)} onChange={e => cm('widthM', R.widthMinM, R.widthMaxM)(e.target.value)} /></label>
    <label className="f"><span>{t('radiator.height')}</span><input type="number" min={R.heightMinM * 100} max={R.heightMaxM * 100} value={Math.round((p.heightM ?? 0) * 100)} onChange={e => cm('heightM', R.heightMinM, R.heightMaxM)(e.target.value)} /></label>
    <label className="f"><span>{t('radiator.model')}</span><select value={p.model ?? 'panel'} onChange={e => onPatch({ model: e.target.value as TechPoint['model'] })}>
      {RADIATOR_MODELS.map(m => <option key={m} value={m}>{t(`radiator.model.${m}`)}</option>)}</select></label>
  </div>);
}
