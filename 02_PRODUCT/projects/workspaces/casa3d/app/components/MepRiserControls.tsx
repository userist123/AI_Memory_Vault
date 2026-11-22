'use client';
// Coloanele verticale ale traseului selectat (inspectorul din etapa Instalații): lista punctelor cu cota lor, „urcă/coboară
// aici” la un punct ales și coborârea/urcarea la capete. Cotele le tastează omul, în cm (core/mep-riser.ts face restul).
import { useState } from 'react';
import type { MepRun } from '@/core/types';
import { addRiser, addEndDrop, risers } from '@/core/mep-riser';
import { usePrefs } from '@/lib/prefs';

export default function MepRiserControls({ run, ceilingM, onApply, say }: { run: MepRun; ceilingM: number; onApply(r: MepRun): void; say(s: string): void }){
  const { t } = usePrefs(), [at, setAt] = useState<string>('end'), [cm, setCm] = useState<number>(NaN);
  const rs = risers(run), max = Math.round((ceilingM + 1) * 100);
  const apply = () => { const h = cm / 100;
    const r = at === 'start' || at === 'end' ? addEndDrop(run, at, h, ceilingM) : addRiser(run, Number(at), h, ceilingM);
    if (!r.ok){ say(t(r.key, { max })); return; } onApply(r.run); setCm(NaN); say(t('mepRiser.done')); };
  return (<fieldset className="mep-riser">
    <legend>{t('mepRiser.title')}</legend>
    <p className="muted small">{rs.length ? t('mepRiser.count', { n: rs.length }) : t('mepRiser.none')}</p>
    <label className="f"><span>{t('mepRiser.where')}</span><select value={at} onChange={e => setAt(e.target.value)}>
      <option value="start">{t('mepRiser.atStart', { h: Math.round(run.points[0]![1] * 100) })}</option>
      {run.points.slice(1, -1).map((p, i) => <option key={i + 1} value={String(i + 1)}>{t('mepRiser.atPoint', { n: i + 2, h: Math.round(p[1] * 100) })}</option>)}
      <option value="end">{t('mepRiser.atEnd', { h: Math.round(run.points[run.points.length - 1]![1] * 100) })}</option>
    </select></label>
    <label className="f"><span>{t('mepRiser.toCm')}</span><input type="number" min={-100} max={max} step={1} value={Number.isFinite(cm) ? cm : ''} placeholder={t('mepRiser.placeholder')} onChange={e => setCm(e.target.valueAsNumber)} /></label>
    <button type="button" className="btn" disabled={!Number.isFinite(cm)} onClick={apply}>{t('mepRiser.apply')}</button>
  </fieldset>);
}
