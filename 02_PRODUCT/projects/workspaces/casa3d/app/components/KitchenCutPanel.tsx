'use client';
// Lista de debitare a bucătăriei pe module (core/kitchen-cut.ts): parametrii de construcție (de verificat cu atelierul),
// piesele pe corpuri, rezumatul (plăci din planul de tăiere, m², cant, feronerie), observațiile, planurile de tăiere,
// CSV-ul pentru atelier și tipărirea planurilor.
import { useEffect, useMemo, useState } from 'react';
import type { KitchenSpec } from '@/core/types';
import type { KitchenModule } from '@/core/kitchen-modules';
import { kitchenCutList, kitchenCutCsv, kitchenCutOptions, kitchenCutDiff, checkKitchenCut, KITCHEN_CUT_DEFAULT, KITCHEN_CUT_RANGE, EDGE_MM, type KitchenCutOptions, type KitchenCutGroup, type KitchenCutPart, type KitchenMaterial } from '@/core/kitchen-cut';
import { layoutCutParts } from '@/core/sheet-layout';
import CutPlans, { type PlanPart } from './CutPlans';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

type NumKey = keyof typeof KITCHEN_CUT_RANGE;
const FIELDS: NumKey[] = ['carcassH', 'depth', 'plinth', 'upperH', 'upperD', 'tallH', 'kerf', 'trim', 'sheetL', 'sheetW', 'minPartMm'];
const MATS: KitchenMaterial[] = ['pal', 'front', 'hdf'];

/** Câmp numeric care lasă utilizatorul să scrie (valorile intermediare nu sunt aruncate); se aplică doar valoarea din interval. */
function NumField({ label, value, min, max, step, onCommit }: { label: string; value: number; min: number; max: number; step: number; onCommit(v: number): void }){
  const [txt, setTxt] = useState(String(value)), v = Number(txt.replace(',', '.')), ok = txt.trim() !== '' && Number.isFinite(v) && v >= min && v <= max;
  useEffect(() => { setTxt(String(value)); }, [value]);
  return <label className="f"><span>{label}</span><input type="number" inputMode="decimal" min={min} max={max} step={step} value={txt} aria-invalid={!ok} title={`${min}–${max} mm`}
    onChange={e => { setTxt(e.target.value); const x = Number(e.target.value); if (e.target.value.trim() !== '' && Number.isFinite(x) && x >= min && x <= max) onCommit(x); }} onBlur={() => { if (!ok) setTxt(String(value)); }} /></label>;
}
/** Cantul unei piese ca desen: laturile cantuite sunt îngroșate (sus/jos = L1/L2, stânga/dreapta = l1/l2). */
function EdgeGlyph({ edges, label }: { edges: readonly number[]; label: string }){
  const side = (on: number, x1: number, y1: number, x2: number, y2: number) => <line x1={x1} y1={y1} x2={x2} y2={y2} stroke="currentColor" strokeWidth={on ? 3 : .8} strokeLinecap="square" opacity={on ? 1 : .55} />;
  return <svg className="cut-edge" viewBox="0 0 26 16" width={26} height={16} role="img" aria-label={label}><title>{label}</title>{side(edges[0]!, 3, 3, 23, 3)}{side(edges[1]!, 3, 13, 23, 13)}{side(edges[2]!, 3, 3, 3, 13)}{side(edges[3]!, 23, 3, 23, 13)}</svg>;
}

export default function KitchenCutPanel({ modules, kitchen, projectName, onCut }: { modules: KitchenModule[]; kitchen: KitchenSpec; projectName: string; onCut(cut: Partial<KitchenCutOptions> | undefined): void }){
  const { t, lang } = usePrefs(), nf = (v: number) => v.toLocaleString(intlLocale(lang), { maximumFractionDigits: 2 });
  const o = useMemo(() => kitchenCutOptions(kitchen.cut), [kitchen.cut]);
  const res = useMemo(() => kitchenCutList(modules, kitchen, o), [modules, kitchen, o]);
  const layout = useMemo(() => layoutCutParts(res.parts, o), [res, o]);
  const [err, setErr] = useState('');
  // se salvează valorile normalizate (ca ecranul să arate exact ce păstrează serverul) și doar ce diferă de implicit; aceeași validare ca pe server, ca o combinație greșită să nu ajungă la salvare
  const set = (patch: Partial<KitchenCutOptions>) => { const next = { ...o, ...patch }; try { const clean = { ...o, ...checkKitchenCut(next) }; setErr(''); onCut(kitchenCutDiff(clean)); } catch (e: any){ setErr(e?.message || t('kcut.invalid')); } };
  const groupLabel = (g: KitchenCutGroup) => g.row === 'plinth' ? t('kcut.p.plinth') : `${g.id} ${g.row === 'upper' ? t('kcut.upper') : t(`kmod.t.${g.type}`)} ${nf(g.wCm ?? 0)}`;
  const labels = useMemo(() => new Map(res.groups.map(g => [g.id, groupLabel(g)])), [res.groups, lang]);   // eslint-disable-line react-hooks/exhaustive-deps
  const matLabel = (m: string) => m === 'front' ? t(`kcut.mat.front.${o.frontMaterial}`) : t(`kcut.mat.${m}`);
  const planParts: PlanPart[] = res.parts.map(p => ({ label: t(`kcut.p.${p.kind}`), l: p.l, w: p.w, module: labels.get(p.group) }));
  const nr = new Map(res.parts.map((p, i) => [p, i + 1])), edgeMm = (p: KitchenCutPart) => p.material === 'front' ? o.edgeFront : o.edgeCarcass;
  const edgeText = (p: KitchenCutPart) => p.edges.every(e => !e) ? t('kcut.edge.none') : p.edges.every(e => e) ? t('kcut.edge.all', { mm: nf(edgeMm(p)) }) : t('kcut.edge.front', { mm: nf(edgeMm(p)) });
  const small = new Set(res.small), empty = res.groups.filter(g => !res.parts.some(p => p.group === g.id));
  function csv(){ const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([kitchenCutCsv(res, kitchen, projectName)], { type: 'text/csv;charset=utf-8' }));
    a.download = `${(projectName || 'proiect').replace(/[\\/:*?"<>|]/g, '_')} - debitare bucatarie.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 2000); }
  return (<div className="kcut">
    <p className="muted small">{t('kcut.intro')}</p>
    {res.small.length > 0 && <div className="issue WARNING">{t('kcut.w.small', { min: nf(o.minPartMm), list: res.small.join(', ') })}</div>}
    {layout.unplaced.map((u, k) => <div key={k} className="issue WARNING">{t(`cut.reason.${u.reason}`, { n: u.id, name: planParts[Number(u.id) - 1]?.label ?? u.name, l: res.parts[Number(u.id) - 1]?.l ?? 0, w: res.parts[Number(u.id) - 1]?.w ?? 0, sl: o.sheetL, sw: o.sheetW })}</div>)}
    {empty.length > 0 && <div className="issue WARNING">{t('kcut.w.empty', { list: empty.map(g => labels.get(g.id)).join(', ') })}</div>}

    <details className="kcut-params"><summary>{t('kcut.params')}</summary>
      <div className="kcut-grid">
        {FIELDS.map(k => <NumField key={k} label={t(`kcut.f.${k}`)} value={o[k]} min={KITCHEN_CUT_RANGE[k][0]} max={KITCHEN_CUT_RANGE[k][1]} step={k === 'kerf' ? .1 : 1} onCommit={v => set({ [k]: v })} />)}
        <label className="f"><span>{t('kcut.f.frontMaterial')}</span><select value={o.frontMaterial} onChange={e => set({ frontMaterial: e.target.value as KitchenCutOptions['frontMaterial'] })}><option value="pal">{t('kcut.mat.front.pal')}</option><option value="mdf">{t('kcut.mat.front.mdf')}</option></select></label>
        <label className="f"><span>{t('kcut.f.edgeCarcass')}</span><select value={o.edgeCarcass} onChange={e => set({ edgeCarcass: Number(e.target.value) })}>{EDGE_MM.map(mm => <option key={mm} value={mm}>{t('kcut.abs', { mm: nf(mm) })}</option>)}</select></label>
        <label className="f"><span>{t('kcut.f.edgeFront')}</span><select value={o.edgeFront} disabled={o.frontMaterial === 'mdf'} onChange={e => set({ edgeFront: Number(e.target.value) })}>{EDGE_MM.map(mm => <option key={mm} value={mm}>{t('kcut.abs', { mm: nf(mm) })}</option>)}</select></label>
      </div>
      {err && <div className="issue ERROR" role="alert">{err}</div>}
      <div className="kmod-act"><button className="btn" onClick={() => { setErr(''); onCut(undefined); }} disabled={!kitchenCutDiff(o)}>{t('kcut.reset')}</button><small className="muted">{t('kcut.paramsNote', { h: KITCHEN_CUT_DEFAULT.carcassH, d: KITCHEN_CUT_DEFAULT.depth })}</small></div>
    </details>

    <section className="kcut-sum" aria-label={t('kcut.summary')}>
      <dl>{MATS.filter(m => res.areas[m] > 0).map(m => <div key={m}><dt>{matLabel(m)}</dt><dd><b>{t('kcut.sheets', { n: layout.stats.sheets[m] ?? 0 })}</b> · {nf(res.areas[m])} m²</dd></div>)}
        {res.edgeM.map(e => <div key={e.mm}><dt>{t('kcut.edgeTotal', { mm: nf(e.mm) })}</dt><dd><b>{nf(e.m)} m</b></dd></div>)}</dl>
      <p className="muted small">{t('kcut.sheetNote', { l: o.sheetL, w: o.sheetW, kerf: nf(o.kerf), trim: nf(o.trim) })}</p>
      <h3>{t('kcut.hardware')}</h3>
      <ul className="kcut-hw">{res.hardware.map(h => <li key={h.key}>{t(`kcut.hw.${h.key === 'handle' && kitchen.handle === 'knob' ? 'knob' : h.key}`)}: <b>{nf(h.qty)} {t(`kcut.u.${h.unit}`)}</b></li>)}</ul>
    </section>

    <div className="kcut-scroll"><table className="wd-table kcut-table"><caption className="sr-only">{t('kcut.title')}</caption>
      <thead><tr><th>{t('cut.nr')}</th><th>{t('wd.part')}</th><th>{t('cut.dims')}</th><th>{t('wd.qty')}</th><th>{t('kcut.grain')}</th><th>{t('wd.edges')}</th><th>{t('kcut.material')}</th></tr></thead>
      {res.groups.map(g => { const ps = res.parts.filter(p => p.group === g.id), ns = res.notes.filter(x => x.group === g.id);
        return <tbody key={g.id}><tr className="kcut-group"><th colSpan={7} scope="rowgroup">{labels.get(g.id)}{ns.map((x, k) => <small key={k} className="muted"> — {t(`kcut.n.${x.code}`)}</small>)}</th></tr>
          {ps.map(p => <tr key={nr.get(p)} className={small.has(nr.get(p)!) ? 'kcut-small' : undefined}><td>{nr.get(p)}</td><td>{t(`kcut.p.${p.kind}`)}</td><td>{p.l} × {p.w}</td><td>{p.qty}</td>
            <td>{p.grain ? <span title={t('kcut.grainYes')} aria-label={t('kcut.grainYes')}>↔</span> : <span aria-label={t('kcut.grainNo')}>—</span>}</td>
            <td><EdgeGlyph edges={p.edges} label={edgeText(p)} /></td><td>{matLabel(p.material)}</td></tr>)}</tbody>; })}
    </table></div>
    <ul className="kcut-notes muted small">{res.notes.filter(x => !x.group).map((x, k) => <li key={k}>{t(`kcut.n.${x.code}`)}</li>)}<li>{t('kcut.convention')}</li></ul>

    <h3>{t('cut.plans')}</h3>
    <CutPlans layout={layout} opt={o} parts={planParts} title={t('kcut.printTitle', { name: projectName })} materialLabel={matLabel} />
    <div className="oedit-act"><button className="btn primary" onClick={csv} disabled={!res.parts.length}>{t('kcut.csv')}</button></div>
  </div>);
}
