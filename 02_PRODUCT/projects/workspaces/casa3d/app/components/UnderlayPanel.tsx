'use client';
import { useRef, useState } from 'react';
import type { Underlay } from '@/core/types';
import { fitWithin, UNDERLAY_LIMITS } from '@/core/underlay';

const T = {
  title: 'Plan din imagine', hint: 'Încarcă o poză sau o scanare a planului (PNG sau JPG) și trasează deasupra ei. Imaginea nu intră în calcule, doar ajută la desen.',
  choose: 'Alege imaginea', replace: 'Înlocuiește imaginea', opacity: 'Opacitate', lock: 'Blochează imaginea (nu se mai mută)', remove: 'Șterge imaginea',
  calibrate: 'Calibrează scara', calibrateHint: 'Click pe două puncte de pe imagine a căror distanță reală o știi.', picking: 'Apasă pe două puncte ale imaginii în plan… (Esc anulează)',
  realDistance: 'Distanța reală dintre cele două puncte (cm)', apply: 'Aplică', cancel: 'Anulează', width: 'Lățimea imaginii pe plan',
  badType: 'Alege un fișier PNG sau JPG.', tooBig: 'Imaginea e prea mare chiar și după reducere; încearcă una mai mică.', readFail: 'Nu am putut citi imaginea.', added: 'Imaginea a fost adăugată; calibrează scara ca să se potrivească.',
  badCm: 'Introdu o distanță în centimetri, mai mare ca zero.', scaled: 'Scara imaginii a fost actualizată.', cantScale: 'Scara nu a putut fi calculată; alege două puncte diferite.',
};
const MAX_BYTES = 1_500_000;

/** Reduce imaginea în browser: latura lungă ≤ 2000 px, JPEG calitate 0,85 (scade treptat dacă tot depășește limita serverului). */
async function downscale(file: File): Promise<string> {
  const url = URL.createObjectURL(file);
  try {
    const img = await new Promise<HTMLImageElement>((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = () => no(new Error(T.readFail)); i.src = url; });
    let side: number = UNDERLAY_LIMITS.maxSide, q: number = UNDERLAY_LIMITS.jpegQuality;
    for (let attempt = 0; attempt < 5; attempt++){
      const { w, h } = fitWithin(img.naturalWidth, img.naturalHeight, side), c = document.createElement('canvas'); c.width = w; c.height = h;
      const g = c.getContext('2d')!; g.fillStyle = '#fff'; g.fillRect(0, 0, w, h); g.drawImage(img, 0, 0, w, h);
      const out = c.toDataURL('image/jpeg', q), bytes = (out.length - out.indexOf(',') - 1) * 3 / 4;
      if (bytes <= MAX_BYTES) return out;
      side = Math.round(side * .75); q = Math.max(.6, q - .08);
    }
    throw new Error(T.tooBig);
  } finally { URL.revokeObjectURL(url); }
}

export interface Calib { stage: 'pick' | 'enter'; a?: [number, number]; b?: [number, number] }
interface Props { underlay: Underlay | undefined; calib: Calib | null; say(t: string): void;
  onAdd(dataUrl: string): void; onPatch(patch: Partial<Underlay>, record: boolean): void; onRemove(): void;
  onCalibStart(): void; onCalibCancel(): void; onCalibApply(realCm: number): boolean; }

export default function UnderlayPanel({ underlay, calib, say, onAdd, onPatch, onRemove, onCalibStart, onCalibCancel, onCalibApply }: Props){
  const file = useRef<HTMLInputElement>(null), [cm, setCm] = useState(''), [busy, setBusy] = useState(false);
  async function pick(f: File | undefined){
    if (!f) return; if (!/^image\/(png|jpeg)$/.test(f.type)){ say(T.badType); return; }
    setBusy(true); try { onAdd(await downscale(f)); say(T.added); } catch (e: any){ say(e?.message || T.readFail); } finally { setBusy(false); if (file.current) file.current.value = ''; }
  }
  return (<>
    <h4>{T.title}</h4>
    <p className="prov" style={{ margin: 0 }}>{T.hint}</p>
    <input ref={file} type="file" accept="image/png,image/jpeg" aria-label={T.choose} disabled={busy} onChange={e => pick(e.target.files?.[0])} />
    {underlay && <>
      <label className="f"><span>{T.opacity}: {Math.round(underlay.opacity * 100)}%</span>
        <input type="range" min={UNDERLAY_LIMITS.minOpacity * 100} max={UNDERLAY_LIMITS.maxOpacity * 100} step={5} value={Math.round(underlay.opacity * 100)} onChange={e => onPatch({ opacity: Number(e.target.value) / 100 }, false)} /></label>
      <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={underlay.locked} onChange={e => onPatch({ locked: e.target.checked }, false)} /> {T.lock}</label>
      <div className="prov">{T.width}: {underlay.widthM.toLocaleString('ro-RO', { maximumFractionDigits: 2 })} m</div>
      {!calib && <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}><button className="btn" onClick={() => { setCm(''); onCalibStart(); }} title={T.calibrateHint}>{T.calibrate}</button><button className="btn danger" onClick={onRemove}>{T.remove}</button></div>}
      {calib?.stage === 'pick' && <><div className="issue WARNING">{T.picking}</div><button className="btn" onClick={onCalibCancel}>{T.cancel}</button></>}
      {calib?.stage === 'enter' && <>
        <label className="f"><span>{T.realDistance}</span><input type="number" min={1} autoFocus value={cm} onChange={e => setCm(e.target.value)} /></label>
        <div style={{ display: 'flex', gap: 8 }}><button className="btn primary" onClick={() => { const v = Number(cm.replace(',', '.')); if (!Number.isFinite(v) || v <= 0){ say(T.badCm); return; } if (onCalibApply(v)) say(T.scaled); else say(T.cantScale); }}>{T.apply}</button><button className="btn" onClick={onCalibCancel}>{T.cancel}</button></div>
      </>}
    </>}
  </>);
}
