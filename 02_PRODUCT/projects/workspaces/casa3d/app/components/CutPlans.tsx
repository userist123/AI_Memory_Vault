'use client';
// Planurile de tăiere (comune dulapului și bucătăriei): câte un desen pe placă — piesele numerotate ca în lista pentru atelier,
// cotele pe piesă, marginea tunsă hașurată, piesele rotite marcate, procentul de folosire — cu navigare între plăci, legendă
// și tipărire (toate plăcile, câte una pe pagină; restul interfeței e ascuns la tipar). Fereastra mare în care stau: CutDialog.
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import type { LayoutOptions, LayoutResult, SheetLayout } from '@/core/sheet-layout';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

/** Piesele listei, în ordinea numerotării (piesa cu id „3” e a treia); `label` = denumirea tradusă, `module` = corpul. */
export interface PlanPart { label: string; l: number; w: number; module?: string }
const INK = '#1b1d21', LINE = '#23262B';

/** Fereastră modală mare (nativă: focus prins, Escape, fundal inert); focusul revine la butonul care a deschis-o. */
export function CutDialog({ title, onClose, children }: { title: string; onClose(): void; children: ReactNode }){
  const { t } = usePrefs(), box = useRef<HTMLDialogElement>(null), tid = useId();
  useEffect(() => { const d = box.current, back = document.activeElement as HTMLElement | null; if (d && !d.open) d.showModal();
    return () => { try { d?.close(); } catch { /* deja închisă */ } back?.focus?.(); }; }, []);
  return (<dialog ref={box} className="cut-dlg" aria-labelledby={tid} onCancel={e => { e.preventDefault(); onClose(); }} onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
    <div className="cut-dlg-head"><h2 id={tid}>{title}</h2><button className="btn" onClick={onClose}>{t('common.close')}</button></div>
    <div className="cut-dlg-body">{children}</div>
  </dialog>);
}

/** Desenul unei plăci. Culorile sunt ale desenului (hârtie), nu ale temei: rămâne lizibil și în tema întunecată, și la tipar. */
export function CutSheetSvg({ sheet, opt, parts, label }: { sheet: SheetLayout; opt: LayoutOptions; parts: PlanPart[]; label: string }){
  const { t } = usePrefs(), hid = useId().replace(/[^a-zA-Z0-9]/g, ''), { sheetL: L, sheetW: W, trim } = opt;
  return (<svg className="cut-svg" viewBox={`0 0 ${L} ${W}`} role="img" aria-label={label} preserveAspectRatio="xMidYMid meet">
    <defs><pattern id={`h${hid}`} width={28} height={28} patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width={28} height={28} fill="#f1efe9" /><line x1={0} y1={0} x2={0} y2={28} stroke="#8a8f98" strokeWidth={5} /></pattern></defs>
    <rect x={0} y={0} width={L} height={W} fill={`url(#h${hid})`} stroke={LINE} strokeWidth={2} vectorEffect="non-scaling-stroke" />
    <rect x={trim} y={trim} width={Math.max(0, L - 2 * trim)} height={Math.max(0, W - 2 * trim)} fill="#fbfaf7" />
    {sheet.placements.map((q, i) => { const p = parts[Number(q.id) - 1], num = q.id + (q.rotated ? ' ↻' : ''), dims = `${p?.l ?? q.l}×${p?.w ?? q.w}`;
      // textul curge pe latura lungă a piesei; cota apare doar dacă încape lizibil, altfel rămâne numărul (cotele sunt în legendă)
      const vert = q.w > q.l * 1.25, a = vert ? q.w : q.l, b = vert ? q.l : q.w, cx = q.x + q.l / 2, cy = q.y + q.w / 2;
      const two = Math.min(64, b * .34, a / (Math.max(num.length, dims.length) * .6)), one = Math.min(64, b * .7, a / (num.length * .62));
      return <g key={i}><title>{t('cut.pieceTitle', { n: q.id, name: p?.label ?? q.name, l: p?.l ?? q.l, w: p?.w ?? q.w })}{p?.module ? ` · ${p.module}` : ''}{q.rotated ? ` · ${t('cut.rotated')}` : ''}</title>
        <rect x={q.x} y={q.y} width={q.l} height={q.w} fill={q.rotated ? '#f6e2b8' : '#e3ecf3'} stroke={LINE} strokeWidth={1} vectorEffect="non-scaling-stroke" />
        {two >= 24 ? <g transform={vert ? `rotate(-90 ${cx} ${cy})` : undefined} textAnchor="middle" fill={INK} fontFamily="system-ui, sans-serif">
            <text x={cx} y={cy - two * .12} fontSize={two} fontWeight={700}>{num}</text><text x={cx} y={cy + two * .95} fontSize={two * .82}>{dims}</text></g>
          : one >= 18 ? <text x={cx} y={cy} dominantBaseline="central" textAnchor="middle" transform={vert ? `rotate(-90 ${cx} ${cy})` : undefined} fontSize={one} fontWeight={700} fill={INK} fontFamily="system-ui, sans-serif">{num}</text> : null}
      </g>; })}
  </svg>);
}

/** Legenda unei plăci: piesele de pe ea, cu numărul din listă, cotele și câte bucăți sunt pe această placă. */
function Legend({ sheet, parts }: { sheet: SheetLayout; parts: PlanPart[] }){
  const { t } = usePrefs(), rows = new Map<string, { n: number; rot: number }>();
  for (const q of sheet.placements){ const r = rows.get(q.id) ?? { n: 0, rot: 0 }; r.n++; if (q.rotated) r.rot++; rows.set(q.id, r); }
  return (<table className="wd-table cut-legend"><thead><tr><th>{t('cut.nr')}</th><th>{t('wd.part')}</th><th>{t('cut.dims')}</th><th>{t('wd.qty')}</th></tr></thead>
    <tbody>{[...rows].sort((a, b) => Number(a[0]) - Number(b[0])).map(([id, r]) => { const p = parts[Number(id) - 1];
      return <tr key={id}><td>{id}</td><td>{p?.label}{p?.module ? <small className="muted"> · {p.module}</small> : null}</td><td>{p?.l} × {p?.w}</td><td>{r.n}{r.rot ? ` (${t('cut.rotatedN', { n: r.rot })})` : ''}</td></tr>; })}</tbody></table>);
}

export default function CutPlans({ layout, opt, parts, title, materialLabel }: { layout: LayoutResult; opt: LayoutOptions; parts: PlanPart[]; title: string; materialLabel(material: string): string }){
  const { t, lang } = usePrefs(), [at, setAt] = useState(0), [printing, setPrinting] = useState(false), n = layout.sheets.length, i = Math.min(at, Math.max(0, n - 1));
  const pct = (s: SheetLayout) => Math.round((1 - s.waste) * 100), nf = (v: number) => v.toLocaleString(intlLocale(lang), { maximumFractionDigits: 2 });
  const head = (s: SheetLayout, k: number) => t('cut.sheetHead', { n: k + 1, total: n, material: materialLabel(s.material), l: opt.sheetL, w: opt.sheetW, pct: pct(s) });
  const aria = (s: SheetLayout, k: number) => t('cut.sheetAria', { n: k + 1, total: n, material: materialLabel(s.material), pieces: s.placements.length, pct: pct(s), rot: s.placements.filter(q => q.rotated).length });
  // tipărirea: conținutul de tipar e montat direct în <body>, iar clasa de pe <body> ascunde restul paginii doar cât ține tiparul
  useEffect(() => { if (!printing) return; document.body.classList.add('cut-printing');
    const done = () => { document.body.classList.remove('cut-printing'); setPrinting(false); }; window.addEventListener('afterprint', done, { once: true });
    // unele browsere de telefon nu trimit „afterprint”: ieșirea din modul tipar sau revenirea în fereastră închid și ele tiparul
    const mq = window.matchMedia?.('print'), onMq = (e: MediaQueryListEvent) => { if (!e.matches) done(); }, onFocus = () => window.setTimeout(done, 500);
    mq?.addEventListener?.('change', onMq); window.addEventListener('focus', onFocus, { once: true });
    const id = window.setTimeout(() => window.print(), 50);
    return () => { window.clearTimeout(id); window.removeEventListener('afterprint', done); mq?.removeEventListener?.('change', onMq); window.removeEventListener('focus', onFocus); document.body.classList.remove('cut-printing'); }; }, [printing]);
  if (!n) return null;
  const s = layout.sheets[i]!;
  return (<div className="cut-plans">
    <div className="cut-nav"><b>{head(s, i)}</b>
      <span className="cut-nav-btns"><button className="btn" onClick={() => setAt(Math.max(0, i - 1))} disabled={i === 0} aria-label={t('cut.prev')}>‹</button>
        <button className="btn" onClick={() => setAt(Math.min(n - 1, i + 1))} disabled={i >= n - 1} aria-label={t('cut.next')}>›</button>
        <button className="btn" onClick={() => setPrinting(true)}>{t('cut.print')}</button></span></div>
    <CutSheetSvg sheet={s} opt={opt} parts={parts} label={aria(s, i)} />
    <p className="muted small cut-key"><i className="cut-sw" style={{ background: '#e3ecf3' }} />{t('cut.keyPiece')} <i className="cut-sw" style={{ background: '#f6e2b8' }} />{t('cut.keyRotated')} <i className="cut-sw cut-svg-trim" />{t('cut.keyTrim', { trim: nf(opt.trim), kerf: nf(opt.kerf) })}</p>
    <Legend sheet={s} parts={parts} />
    {printing && createPortal(<div className="cut-print"><h1>{title}</h1><p>{t('cut.printNote', { l: opt.sheetL, w: opt.sheetW, trim: nf(opt.trim), kerf: nf(opt.kerf) })}</p>
      {layout.sheets.map((x, k) => <section key={k} className="cut-page"><h2>{head(x, k)}</h2><CutSheetSvg sheet={x} opt={opt} parts={parts} label={aria(x, k)} /><Legend sheet={x} parts={parts} /></section>)}</div>, document.body)}
  </div>);
}
