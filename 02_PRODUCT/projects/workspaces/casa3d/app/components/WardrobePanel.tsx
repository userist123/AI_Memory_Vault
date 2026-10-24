'use client';
// Dulapul pe comandă din PAL pentru piesa selectată: corpuri (haine lungi, rafturi, bară + raft, sertare), uși, soclu,
// decor; lista de debitare (CSV pentru Dedeman / Hornbach / ateliere), plăcile, cantul și feroneria.
import { useMemo } from 'react';
import type { Catalog, FurniturePlacement, Snapshot } from '@/core/types';
import { resolve } from '@/core/catalog';
import { wardrobeCutList, cutListCsv, DEFAULT_WARDROBE, type WardrobeSpec, type SectionKind } from '@/core/joinery';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

const KINDS: SectionKind[] = ['hang', 'shelves', 'hangShelf', 'drawers'];
export default function WardrobePanel({ p, snap, catalog, onChange }: { p: FurniturePlacement; snap: Snapshot; catalog: Catalog; onChange(spec: WardrobeSpec | null): void }){
  const { t, lang } = usePrefs(), nf = (v: number) => v.toLocaleString(intlLocale(lang), { maximumFractionDigits: 2 }), spec = snap.joinery?.[p.id], rv = resolve(catalog, p.variantId);
  const dims = { W: Math.round((p.size?.w ?? (rv?.w ?? 0) * 100) * 10), D: Math.round((p.size?.d ?? (rv?.d ?? 0) * 100) * 10), H: Math.round((p.size?.h ?? (rv?.h ?? 0) * 100) * 10) };
  const res = useMemo(() => { if (!spec) return null; try { return { ok: wardrobeCutList(dims.W, dims.H, dims.D, spec) }; } catch (e: any){ return { err: e.message as string }; } }, [spec, dims.W, dims.H, dims.D]);
  if (!spec) return <div className="wdp"><button className="btn" onClick={() => onChange(DEFAULT_WARDROBE)}>{t('wd.enable')}</button><small className="muted">{t('wd.enableSub')}</small></div>;
  const set = (patch: Partial<WardrobeSpec>) => onChange({ ...spec, ...patch });
  const setSec = (i: number, patch: Partial<WardrobeSpec['sections'][number]>) => set({ sections: spec.sections.map((s, k) => k === i ? { ...s, ...patch } : s) });
  function csv(){ if (!res?.ok) return; const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob(['﻿' + cutListCsv(res.ok.parts, spec!)], { type: 'text/csv;charset=utf-8' }));
    a.download = `${(snap.name || 'proiect').replace(/[\\/:*?"<>|]/g, '_')} - debitare dulap.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 2000); }
  return (<div className="wdp"><h4>{t('wd.title')} <small className="muted">{dims.W} × {dims.H} × {dims.D} mm</small></h4>
    <div className="wd-secs">{spec.sections.map((s, i) => <div key={i} className="wd-sec"><b>{i + 1}</b>
      <select value={s.kind} onChange={e => setSec(i, { kind: e.target.value as SectionKind, shelves: undefined, drawers: undefined })} aria-label={t('wd.section', { n: i + 1 })}>{KINDS.map(k => <option key={k} value={k}>{t(`wd.k.${k}`)}</option>)}</select>
      {(s.kind === 'shelves' || s.kind === 'hangShelf') && <input type="number" min={0} max={12} value={s.shelves ?? (s.kind === 'shelves' ? 4 : 1)} onChange={e => setSec(i, { shelves: Math.max(0, Math.min(12, Math.round(Number(e.target.value)))) })} aria-label={t('wd.shelves')} title={t('wd.shelves')} />}
      {s.kind === 'drawers' && <input type="number" min={1} max={8} value={s.drawers ?? 3} onChange={e => setSec(i, { drawers: Math.max(1, Math.min(8, Math.round(Number(e.target.value)))) })} aria-label={t('wd.drawers')} title={t('wd.drawers')} />}
      {spec.sections.length > 1 && <button className="btn-x" aria-label={t('wd.removeSec')} onClick={() => set({ sections: spec.sections.filter((_, k) => k !== i) })}>×</button>}</div>)}
      {spec.sections.length < 8 && <button className="btn" onClick={() => set({ sections: [...spec.sections, { kind: 'shelves', shelves: 4 }] })}>{t('wd.addSec')}</button>}</div>
    <div className="grid2">
      <label className="f"><span>{t('wd.doors')}</span><select value={spec.doors} onChange={e => set({ doors: e.target.value as WardrobeSpec['doors'] })}>{(['hinged', 'sliding', 'none'] as const).map(d => <option key={d} value={d}>{t(`wd.d.${d}`)}</option>)}</select></label>
      <label className="f"><span>{t('wd.plinth')}</span><input type="number" min={0} max={150} value={spec.plinthMm} onChange={e => set({ plinthMm: Math.max(0, Math.min(150, Math.round(Number(e.target.value)))) })} /></label>
      <label className="f"><span>{t('wd.decor')}</span><input value={spec.decor ?? ''} maxLength={80} placeholder="Egger W1000 ST9" onChange={e => set({ decor: e.target.value || undefined })} /></label>
      <label className="f"><span>{t('wd.edge')}</span><select value={spec.edge} onChange={e => set({ edge: e.target.value as WardrobeSpec['edge'] })}><option value="abs2">ABS 2 mm</option><option value="abs04">ABS 0,4 mm</option></select></label></div>
    {res?.err && <p className="issue WARNING">{res.err}</p>}
    {res?.ok && <>
      <p className="wd-sum">{t('wd.sum', { sheets: res.ok.sheets, pal: nf(res.ok.palM2), hdf: nf(res.ok.hdfM2), edge: nf(res.ok.edgeM) })}</p>
      <table className="wd-table"><thead><tr><th>{t('wd.part')}</th><th>L × l (mm)</th><th>{t('wd.qty')}</th><th>{t('wd.edges')}</th></tr></thead>
        <tbody>{res.ok.parts.map((x, k) => <tr key={k}><td>{x.name}</td><td>{x.l} × {x.w}</td><td>{x.qty}</td><td>{x.material === 'hdf' ? '—' : x.edges.every(e => e) ? t('wd.allEdges') : x.edges.some(e => e) ? t('wd.frontEdge') : '—'}</td></tr>)}</tbody></table>
      <p className="muted small">{res.ok.hardware.map(h => `${h.name}: ${h.qty} ${h.unit}`).join(' · ')}</p>
      <div className="oedit-act"><button className="btn primary" onClick={csv}>{t('wd.csv')}</button><button className="btn" onClick={() => onChange(null)}>{t('wd.disable')}</button></div>
      <p className="muted small">{t('wd.note')}</p></>}
  </div>);
}
