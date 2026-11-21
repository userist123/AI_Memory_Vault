'use client';
// Import DXF (experimental, docs/bim/GHID_BIM.md Faza 2): fișierul se citește în browser, nu pleacă pe server. Utilizatorul vede
// candidații, alege unitatea dacă desenul nu o declară, straturile și grosimea pentru liniile simple, apoi aprobă adăugarea.
import { useMemo, useRef, useState } from 'react';
import type { Floor } from '@/core/types';
import { parseDxf, wallCandidates, applyWallCandidates, DXF_LIMITS, type DxfParse, type DxfUnit } from '@/core/dxf-import';
import { sha256Hex } from '@/core/underlay';
import { usePrefs } from '@/lib/prefs';

const UNITS: DxfUnit[] = ['mm', 'cm', 'm', 'in', 'ft'];
// coordonatele DXF sunt ale desenului (adesea departe de origine, cu Y în sus): colțul minim al pereților aleși ajunge în originea planului
const originOffset = (cs: { a: [number, number]; b: [number, number] }[]): [number, number] => cs.length ? [-Math.min(...cs.flatMap(c => [c.a[0], c.b[0]])), -Math.min(...cs.flatMap(c => [c.a[1], c.b[1]]))] : [0, 0];
interface Props { floor: Floor; takenIds: string[]; say(t: string): void; onApply(walls: Floor['walls']): void }

export default function DxfImportPanel({ floor, takenIds, say, onApply }: Props){
  const { t } = usePrefs();
  const file = useRef<HTMLInputElement>(null);
  const [src, setSrc] = useState<{ parse: DxfParse; sha256: string; name: string } | null>(null);
  const [unit, setUnit] = useState<DxfUnit | ''>(''), [off, setOff] = useState<Set<string>>(new Set());
  const [withSingle, setWithSingle] = useState(false), [thickCm, setThickCm] = useState(''), [err, setErr] = useState('');
  async function pick(f: File | undefined){
    setErr(''); setSrc(null); if (!f) return;
    if (f.size > DXF_LIMITS.maxBytes){ setErr(t('dxf.tooBig')); return; }
    try { const buf = await f.arrayBuffer(), parse = parseDxf(new TextDecoder('utf-8').decode(buf));
      setSrc({ parse, sha256: await sha256Hex(buf), name: f.name.slice(0, 200) }); setUnit(parse.units ?? ''); setOff(new Set()); }
    catch (e: any){ setErr(e?.message || t('dxf.readFail')); }
    finally { if (file.current) file.current.value = ''; }
  }
  const cands = useMemo(() => { if (!src || !unit) return [];
    try { return wallCandidates(src.parse, { unit, layers: src.parse.layers.filter(l => !off.has(l)) }); } catch { return []; } }, [src, unit, off]);
  const chosen = cands.filter(c => c.thickness != null || withSingle), measured = cands.filter(c => c.thickness != null).length;
  function apply(){
    setErr(''); if (!src) return;
    try { const out = applyWallCandidates(floor, chosen, { sha256: src.sha256, offset: originOffset(chosen), takenIds, ...(withSingle ? { manualThicknessM: Number(thickCm) / 100 } : {}) });
      onApply(out.walls); say(t('dxf.added', { n: chosen.length })); setSrc(null); }
    catch (e: any){ setErr(e?.message || t('dxf.applyFail')); }
  }
  // previzualizarea: pereții cu grosime măsurată plini, liniile simple punctate
  const view = useMemo(() => { const xs = cands.flatMap(c => [c.a[0], c.b[0]]), zs = cands.flatMap(c => [c.a[1], c.b[1]]); if (!xs.length) return null;
    const x0 = Math.min(...xs), z0 = Math.min(...zs), w = Math.max(.1, Math.max(...xs) - x0), h = Math.max(.1, Math.max(...zs) - z0); return { x0, z0, w, h }; }, [cands]);
  return (<div className="dxf-import">
    <h4>{t('dxf.title')} <small className="muted">({t('dxf.experimental')})</small></h4>
    <p className="prov" style={{ margin: 0 }}>{t('dxf.hint')}</p>
    <input ref={file} type="file" accept=".dxf" aria-label={t('dxf.choose')} onChange={e => pick(e.target.files?.[0])} />
    {err && <p role="alert" className="issue ERROR">{err}</p>}
    {src && <>
      <div className="prov">{src.name} · SHA-256 {src.sha256.slice(0, 12)}…</div>
      <label className="f"><span>{t('dxf.unit')}{!src.parse.units && ' *'}</span>
        <select value={unit} onChange={e => setUnit(e.target.value as DxfUnit)}>{!src.parse.units && <option value="">{t('dxf.unitPick')}</option>}
          {UNITS.map(u => <option key={u} value={u}>{u}{u === src.parse.units ? ` (${t('dxf.unitFromFile')})` : ''}</option>)}</select></label>
      {!unit && <p className="issue WARNING">{t('dxf.unitMissing')}</p>}
      {src.parse.layers.length > 1 && <fieldset><legend>{t('dxf.layers')}</legend>{src.parse.layers.map(l =>
        <label key={l} style={{ display: 'flex', gap: 6 }}><input type="checkbox" checked={!off.has(l)} onChange={e => { const n = new Set(off); if (e.target.checked) n.delete(l); else n.add(l); setOff(n); }} />{l}</label>)}</fieldset>}
      {unit && <>
        <p className="prov">{t('dxf.summary', { n: cands.length, m: measured, s: cands.length - measured })}</p>
        {view && <svg role="img" aria-label={t('dxf.preview')} viewBox={`${view.x0 - .2} ${view.z0 - .2} ${view.w + .4} ${view.h + .4}`} style={{ width: '100%', height: 160, background: '#fafaf7', border: '1px solid #ddd' }}>
          {cands.map(c => <line key={c.key} x1={c.a[0]} y1={c.a[1]} x2={c.b[0]} y2={c.b[1]} stroke={c.thickness != null ? '#2d5a3d' : '#b0702a'} strokeWidth={c.thickness ?? 2} strokeDasharray={c.thickness != null ? undefined : `${view.w / 80}`} vectorEffect={c.thickness != null ? undefined : 'non-scaling-stroke'} />)}
        </svg>}
        {cands.length > measured && <>
          <label style={{ display: 'flex', gap: 6 }}><input type="checkbox" checked={withSingle} onChange={e => setWithSingle(e.target.checked)} />{t('dxf.includeSingle')}</label>
          {withSingle && <label className="f"><span>{t('dxf.thickness')}</span><input type="number" min={5} max={100} value={thickCm} onChange={e => setThickCm(e.target.value)} /></label>}
        </>}
        <button className="btn" disabled={!chosen.length} onClick={apply}>{t('dxf.apply', { n: chosen.length })}</button>
      </>}
    </>}
  </div>);
}
