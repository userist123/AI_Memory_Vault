'use client';
import { useState } from 'react';
// Panoul stratului tehnic: sugestii deterministe, adăugare manuală din plan, numărătoare pe camere pentru meseriași.
import type { Snapshot } from '@/core/types';
import EmptyState from './EmptyState';
import { TECH_KINDS, techCounts, type TechKind, type TechPoint } from '@/core/technical';
import { PLAN_LIMITS, RADIATOR_MODELS } from '@/core/plan-limits';
import { cmToClampedM, isSizedRadiator } from '@/core/tech-geometry';
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
          {onPatch && isSizedRadiator(p) && <RadiatorFields p={p} room={r.name} onPatch={patch => onPatch(p.id, patch)} />}</div>)}</details>; })}</div>
    </>}
  </div>);
}

/** Câmp în centimetri: în timpul tastării valoarea rămâne locală; se aplică (adusă în interval) doar la blur sau Enter. Esc renunță. */
function CmField({ label, aria, valueM, lo, hi, onCommit }: { label: string; aria: string; valueM: number; lo: number; hi: number; onCommit(m: number): void }){
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => { if (draft == null) return; const m = cmToClampedM(draft, lo, hi); setDraft(null); if (m != null && m !== valueM) onCommit(m); };
  return <label className="f"><span>{label}</span><input type="number" min={lo * 100} max={hi * 100} aria-label={aria} value={draft ?? String(Math.round(valueM * 100))}
    onChange={e => setDraft(e.target.value)} onBlur={commit} onKeyDown={e => { if (e.key === 'Enter') commit(); else if (e.key === 'Escape') setDraft(null); }} /></label>;
}
/** Lățimea, înălțimea (cm) și tipul unui calorifer, în rândul lui din listă (`room` = camera, pentru eticheta citită de cititorul de ecran). */
function RadiatorFields({ p, room, onPatch }: { p: TechPoint; room: string; onPatch(patch: Partial<Pick<TechPoint, 'widthM' | 'heightM' | 'model'>>): void }){
  const { t } = usePrefs(), R = PLAN_LIMITS.radiator;
  return (<div className="grid2" style={{ gridColumn: '1 / -1', width: '100%' }}>
    <CmField label={t('radiator.width')} aria={t('radiator.widthAria', { room })} valueM={p.widthM ?? 0} lo={R.widthMinM} hi={R.widthMaxM} onCommit={m => onPatch({ widthM: m })} />
    <CmField label={t('radiator.height')} aria={t('radiator.heightAria', { room })} valueM={p.heightM ?? 0} lo={R.heightMinM} hi={R.heightMaxM} onCommit={m => onPatch({ heightM: m })} />
    <label className="f"><span>{t('radiator.model')}</span><select value={p.model ?? 'panel'} onChange={e => onPatch({ model: e.target.value as TechPoint['model'] })}>
      {RADIATOR_MODELS.map(m => <option key={m} value={m}>{t(`radiator.model.${m}`)}</option>)}</select></label>
  </div>);
}
