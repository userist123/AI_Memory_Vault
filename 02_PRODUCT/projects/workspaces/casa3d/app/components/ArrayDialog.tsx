'use client';
// „Repetă în rând”: câte copii, în ce direcție și la ce distanță (pasul dintre copii sau distanța totală de umplut).
// Operația e în core/arrange.ts (arrayGroup); fereastra doar adună valorile și le trimite. Nimic nu e precompletat în afara
// unor valori de pornire vizibile, pe care omul le schimbă (3 copii, 100 cm, spre dreapta).
import { useEffect, useRef, useState } from 'react';
import { ARRAY_LIMITS, stepToFill } from '@/core/arrange';
import { usePrefs } from '@/lib/prefs';

export type ArrayDir = 'right' | 'down' | 'left' | 'up';
const DIR: Record<ArrayDir, [number, number]> = { right: [1, 0], down: [0, 1], left: [-1, 0], up: [0, -1] };
/** Pasul (cm) pe x și z din direcție, mod și distanță. */
export function arrayStep(dir: ArrayDir, mode: 'step' | 'fill', cm: number, count: number): [number, number] {
  const s = mode === 'fill' ? stepToFill(cm, count) : cm, [dx, dz] = DIR[dir]; return [dx * s, dz * s];
}

export default function ArrayDialog({ pieces, onApply, onClose }: { pieces: number; onApply(count: number, stepX: number, stepZ: number): void; onClose(): void }){
  const { t, lang } = usePrefs(), [count, setCount] = useState(3), [cm, setCm] = useState(100), [dir, setDir] = useState<ArrayDir>('right'), [mode, setMode] = useState<'step' | 'fill'>('step');
  // fereastra nativă (ca DialogHost): showModal dă focusul prins în ea, Escape și fundalul inert; focusul revine la închidere
  const box = useRef<HTMLDialogElement>(null), first = useRef<HTMLInputElement>(null);
  useEffect(() => { const d = box.current, back = document.activeElement as HTMLElement | null; if (d && !d.open) d.showModal(); first.current?.focus(); first.current?.select();
    return () => { try { d?.close(); } catch { /* deja închisă */ } back?.focus?.(); }; }, []);
  const okCount = Number.isInteger(count) && count >= ARRAY_LIMITS.countMin && count <= ARRAY_LIMITS.countMax, okCm = Number.isFinite(cm) && cm >= 1 && cm <= ARRAY_LIMITS.stepMaxCm;
  const step = mode === 'fill' ? stepToFill(cm, count) : cm;
  const apply = () => { if (!okCount || !okCm) return; const [x, z] = arrayStep(dir, mode, cm, count); onApply(count, x, z); };
  return (<dialog ref={box} className="dlg" aria-labelledby="array-title" onCancel={e => { e.preventDefault(); onClose(); }} onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
    <form className="dlg-card array-dlg" method="dialog" onSubmit={e => { e.preventDefault(); apply(); }}>
      <h2 id="array-title">{t('array.title')}</h2>
      <p className="muted small">{t('array.intro', { n: pieces })}</p>
      <div className="array-grid">
        <label>{t('array.count')}<input ref={first} type="number" min={ARRAY_LIMITS.countMin} max={ARRAY_LIMITS.countMax} step={1} value={Number.isFinite(count) ? count : ''} onChange={e => setCount(e.target.valueAsNumber)} aria-invalid={!okCount} /></label>
        <fieldset><legend>{t('array.dir')}</legend><div className="array-dirs">{(['left', 'up', 'down', 'right'] as const).map(d =>
          <button key={d} type="button" className="btn" aria-pressed={dir === d} aria-label={t(`array.dir.${d}`)} title={t(`array.dir.${d}`)} onClick={() => setDir(d)}>{{ left: '←', up: '↑', down: '↓', right: '→' }[d]}</button>)}</div></fieldset>
        <fieldset><legend>{t('array.mode')}</legend>
          <label className="array-radio"><input type="radio" name="array-mode" checked={mode === 'step'} onChange={() => setMode('step')} /> {t('array.modeStep')}</label>
          <label className="array-radio"><input type="radio" name="array-mode" checked={mode === 'fill'} onChange={() => setMode('fill')} /> {t('array.modeFill')}</label></fieldset>
        <label>{mode === 'step' ? t('array.stepCm') : t('array.totalCm')}<input type="number" min={1} max={ARRAY_LIMITS.stepMaxCm} step={1} value={Number.isFinite(cm) ? cm : ''} onChange={e => setCm(e.target.valueAsNumber)} aria-invalid={!okCm} /></label>
      </div>
      <p className="muted small" role="status">{okCount && okCm ? t('array.summary', { n: count, step: lang === 'en' ? String(step) : String(step).replace('.', ','), total: pieces * count }) : t('array.invalid', { max: ARRAY_LIMITS.countMax })}</p>
      <div className="dlg-act"><button type="button" className="btn" onClick={onClose}>{t('array.cancel')}</button><button type="submit" className="btn primary" disabled={!okCount || !okCm}>{t('array.apply')}</button></div>
    </form>
  </dialog>);
}
