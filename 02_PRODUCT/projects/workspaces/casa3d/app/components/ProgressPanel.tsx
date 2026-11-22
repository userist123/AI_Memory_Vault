'use client';
// „Situații de lucrări” (pagina Ofertă, sub panoul Comercial; doar pentru designer): devizul de bază înghețat din oferta de
// acum, apoi situațiile pe perioade: cantitatea executată pe fiecare linie, măsurătorile care o justifică (foaia de
// atașament), lucrările suplimentare și totalurile (avans recuperat, reținere, TVA doar dacă e declarat). Calculele sunt
// cele din core/progress.ts, aceleași ca pe planșa tipăribilă (/p/[id]/statement/[n]).
import { useEffect, useState, type InputHTMLAttributes } from 'react';
import { ListSkeleton } from './Skeleton';
import { formatMoney, formatNumber, normalizeUnit } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { askConfirm } from '@/lib/dialog';
import { computeStatement, statementIssues, PROGRESS_LIMITS, type Baseline, type BaseLine, type BaseKind, type Measure, type StatementInput } from '@/core/progress';
import type { ChangeOrder } from '@/core/change-orders';

type Resp = { baseline: Baseline | null; statements: StatementInput[]; issues: { code: 'GAP' | 'OVERLAP'; number: number }[]; skipped?: number };
type Draft = StatementInput & { isNew: boolean; measures: Record<string, Measure[]> };
const KINDS: BaseKind[] = ['labor', 'material', 'service'];
const today = () => new Date().toISOString().slice(0, 10);
const nextDay = (d: string) => new Date(Date.parse(d + 'T00:00:00Z') + 864e5).toISOString().slice(0, 10);
const numOrUndef = (s: string) => { const v = Number(s.replace(',', '.')); return s.trim() === '' || !Number.isFinite(v) || v < 0 ? undefined : v; };
const newKey = () => 'x' + Math.random().toString(36).slice(2, 10);
const coKey = (n: number) => `co:${n}`;
/** Procent opțional: gol = nedeclarat (undefined), „0” = declarat 0. */
function PctInput({ value, max, onNum }: { value: number | undefined; max: number; onNum(n: number | undefined): void }){
  const [txt, setTxt] = useState(value == null ? '' : String(value));
  return <input type="number" inputMode="decimal" min={0} max={max} step="0.5" placeholder="—" value={txt} onChange={e => { setTxt(e.target.value); onNum(numOrUndef(e.target.value)); }} />;
}
/** Câmp numeric care păstrează ce s-a tastat („0”, „0,”, „1.”) și dă mai departe numărul (sau undefined dacă e gol / invalid). */
function NumInput({ value, onNum, ...rest }: { value: number | undefined; onNum(n: number | undefined): void } & Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'>){
  const [txt, setTxt] = useState(value ? String(value) : '');
  useEffect(() => { setTxt(p => (numOrUndef(p) ?? 0) === (value ?? 0) ? p : value ? String(value) : ''); }, [value]);
  return <input type="number" inputMode="decimal" min={0} step="any" placeholder="0" {...rest} value={txt} onChange={e => { setTxt(e.target.value); onNum(numOrUndef(e.target.value)); }} />;
}

export default function ProgressPanel({ id, cur, say }: { id: string; cur: string; say(s: string): void }){
  const { t, lang } = usePrefs(), [r, setR] = useState<Resp | null>(null), [loadErr, setLoadErr] = useState<string | null>(null), [busy, setBusy] = useState(false);
  const [d, setD] = useState<Draft | null>(null), [openM, setOpenM] = useState<Record<string, boolean>>({}), [changes, setChanges] = useState<ChangeOrder[]>([]);
  const money = (v: number | null | undefined) => formatMoney(v, cur, lang, '—'), qf = (v: number) => formatNumber(v, lang, { max: 3 });
  const date = (s: string) => new Date(s.slice(0, 10) + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short', year: 'numeric' });
  const load = () => fetch(`/api/projects/${id}/progress`).then(async x => { const j = await x.json().catch(() => null); if (!x.ok){ setLoadErr(j?.error || t('prog.loadError')); return; } setR(j); setLoadErr(null); }).catch(() => setLoadErr(t('prog.loadError')));
  useEffect(() => { setD(null); load(); fetch(`/api/projects/${id}/changes`).then(x => x.ok ? x.json() : []).then(j => setChanges(Array.isArray(j) ? j : [])).catch(() => {}); }, [id]); // eslint-disable-line
  if (loadErr && !r) return <section className="progress"><h3>{t('prog.title')}</h3><p className="bad" role="alert">{loadErr}</p></section>;
  if (!r) return <section className="progress"><h3>{t('prog.title')}</h3><ListSkeleton label={t('print.loading')} rows={2} /></section>;

  const base = r.baseline, list = r.statements, last = list[list.length - 1];
  async function call(url: string, method: string, body?: unknown): Promise<any | null> {
    setBusy(true);
    try { const res = await fetch(url, { method, headers: body === undefined ? undefined : { 'content-type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) }), j = await res.json().catch(() => ({}));
      if (!res.ok){ say(j.error || t('prog.saveError')); return null; } return j;
    } catch { say(t('prog.saveError')); return null; } finally { setBusy(false); }
  }
  async function fix(){
    if (base && !await askConfirm({ title: t('prog.refixConfirm'), confirmLabel: t('prog.fix') })) return;
    const j = await call(`/api/projects/${id}/progress`, 'POST', { action: 'fixBaseline' }); if (!j) return;
    setR(j); say(j.skipped > 0 ? t('prog.fixedSkipped', { n: j.baseline.lines.length, s: j.skipped }) : t('prog.fixed', { n: j.baseline.lines.length }));
  }
  const startNew = () => { const from = last ? nextDay(last.to) : today(), td = today();
    setOpenM({}); setD({ isNew: true, id: '', number: (last?.number ?? 0) + 1, from, to: td >= from ? td : from, qty: {}, extra: [], measures: {} }); };
  const startEdit = (s: StatementInput) => { setOpenM({}); setD({ ...JSON.parse(JSON.stringify(s)), isNew: false, measures: JSON.parse(JSON.stringify(s.measures ?? {})) }); };
  const edit = (fn: (x: Draft) => void) => setD(p => { if (!p) return p; const x: Draft = JSON.parse(JSON.stringify(p)); fn(x); return x; });
  async function save(){
    if (!d) return;
    // rândurile de măsurători complet goale nu se trimit; unul început (doar descriere sau doar cantitate) îl validează serverul
    const measures = Object.fromEntries(Object.entries(d.measures).map(([k, ms]) => [k, ms.filter(m => m.label.trim() || m.qty > 0)]).filter(([, ms]) => (ms as Measure[]).length));
    const { isNew, id: sid, number, ...rest } = d, body = { ...rest, measures };
    const j = await call(isNew ? `/api/projects/${id}/progress/statements` : `/api/projects/${id}/progress/statements/${encodeURIComponent(sid)}`, isNew ? 'POST' : 'PUT', isNew ? { ...body, number } : body);
    if (!j) return; setR(j); setD(null); say(t('prog.saved', { n: j.statement.number }));
  }
  async function remove(s: StatementInput){
    if (!await askConfirm({ title: t('prog.deleteConfirm', { n: s.number }), confirmLabel: t('common.delete'), danger: true })) return;
    const j = await call(`/api/projects/${id}/progress/statements/${encodeURIComponent(s.id)}`, 'DELETE'); if (j){ setR(j); if (d && !d.isNew && d.id === s.id) setD(null); }
  }

  // calculul „pe viu” al situației în lucru: aceleași funcții ca pe server și pe planșă
  const others = d ? list.filter(s => d.isNew || s.id !== d.id) : list, all = d ? [...others, d as StatementInput] : list;
  const calc = d && base ? computeStatement(base, all, d.number) : null, issues = d ? statementIssues(all) : r.issues;
  const labelOf = (k: string) => base?.lines.find(l => l.key === k)?.label ?? k;
  const contract = base ? base.lines.reduce((a, l) => a + l.qty * l.unitPrice, 0) : 0;
  const usedCo = new Set(all.flatMap(s => s.extra.map(l => l.key)));
  const importable = changes.filter(c => c.status === 'approved' && c.amount > 0 && !usedCo.has(coKey(c.number)));
  const pctField = (k: 'advancePct' | 'retentionPct' | 'vatPct', label: string) => <label>{label}
    <PctInput key={d?.isNew ? 'new' : d?.id} value={d?.[k]} max={PROGRESS_LIMITS.pctMax} onNum={n => edit(x => { if (n == null) delete x[k]; else x[k] = Math.min(PROGRESS_LIMITS.pctMax, n); })} /></label>;

  return (<section className="progress">
    <h3>{t('prog.title')}</h3>
    <p className="muted small">{t('prog.intro')}</p>

    <h4>{t('prog.baseline')}</h4>
    {!base ? <>
      <p className="muted small">{t('prog.fixHelp')}</p>
      <p><button className="btn primary" disabled={busy} onClick={fix}>{t('prog.fix')}</button></p></> : <>
      <p className="small">{t('prog.baselineInfo', { d: date(base.createdAt), n: base.lines.length, v: money(contract) })}</p>
      <p className="muted small">{list.length ? t('prog.refixLocked') : t('prog.refixHelp')}{!list.length && !d && <> <button className="btn" disabled={busy} onClick={fix}>{t('prog.refix')}</button></>}</p></>}

    {base && <>
      <h4>{t('prog.list')}</h4>
      {list.length === 0 && !d && <p className="muted small">{t('prog.none')}</p>}
      {list.length > 0 && <table className="prog-list"><thead><tr><th>{t('prog.col.no')}</th><th>{t('prog.period')}</th><th className="n">{t('prog.t.this')}</th><th className="n">{t('prog.t.payable')}</th><th className="n">{t('prog.col.pct')}</th><th /></tr></thead>
        <tbody>{list.map(s => { const c = computeStatement(base, list, s.number), warn = c.warnings.length + r.issues.filter(i => i.number === s.number).length;
          return <tr key={s.id} className={d && !d.isNew && d.id === s.id ? 'on' : ''}><td><b>{s.number}</b>{warn > 0 && <span className="pill bad" title={t('prog.warnN', { n: warn })}>!</span>}</td><td>{date(s.from)} – {date(s.to)}</td>
            <td className="n">{money(c.totals.thisValue)}</td><td className="n">{money(c.totals.payable)}</td><td className="n">{c.totals.pct == null ? '—' : `${formatNumber(c.totals.pct, lang, { max: 2 })}%`}</td>
            <td className="prog-acts"><button className="btn" disabled={busy} onClick={() => startEdit(s)}>{t('prog.edit')}</button>
              <a className="btn" href={`/p/${id}/statement/${s.number}`} target="_blank" rel="noopener">{t('prog.print')}</a>
              {s.id === last?.id && <button className="btn danger" disabled={busy} onClick={() => remove(s)}>{t('common.delete')}</button>}</td></tr>; })}</tbody></table>}
      {list.length > 1 && <p className="muted small">{t('prog.deleteLastOnly')}</p>}
      {!d && r.issues.map((i, k) => <p key={k} className="bad small" role="status">{t(`prog.warn.${i.code}`, { n: i.number })}</p>)}
      {!d && list.length < PROGRESS_LIMITS.statements && <p><button className="btn primary" disabled={busy} onClick={startNew}>{t('prog.new')}</button></p>}
    </>}

    {base && d && calc && <div className="prog-edit">
      <h4>{d.isNew ? t('prog.newTitle', { n: d.number }) : t('prog.editTitle', { n: d.number })}</h4>
      <div className="prog-head">
        <label>{t('prog.from')} <input type="date" value={d.from} onChange={e => edit(x => { x.from = e.target.value; })} /></label>
        <label>{t('prog.to')} <input type="date" value={d.to} min={d.from} onChange={e => edit(x => { x.to = e.target.value; })} /></label>
        {pctField('advancePct', t('prog.advancePct'))}{pctField('retentionPct', t('prog.retentionPct'))}{base?.vatIncluded ? <p className="muted small">{t('prog.vatIncluded')}</p> : pctField('vatPct', t('prog.vatPct'))}
      </div>
      <p className="muted small">{t('prog.pctHelp')}{!base?.vatIncluded && <> {t('prog.pctHelpVat')}</>}</p>

      <div className="prog-scroll"><table className="prog-table">
        <thead><tr><th>{t('prog.col.name')}</th><th>{t('prog.col.unit')}</th><th className="n">{t('prog.col.contract')}</th><th className="n">{t('prog.col.prev')}</th><th className="n">{t('prog.col.this')}</th><th className="n">{t('prog.col.cum')}</th>
          <th className="n">{t('prog.col.rest')}</th><th className="n">{t('prog.col.pct')}</th><th className="n">{t('prog.col.price')}</th><th className="n">{t('prog.col.value')}</th><th /></tr></thead>
        {KINDS.map(kind => { const rows = calc.rows.filter(x => x.kind === kind); if (!rows.length) return null;
          return <tbody key={kind}><tr className="prog-group"><td colSpan={11}>{t(`prog.kind.${kind}`)}</td></tr>
            {rows.map(row => { const ms = d.measures[row.key] ?? [], msSum = ms.reduce((a, m) => a + m.qty, 0), msBad = ms.length > 0 && Math.abs(msSum - row.thisQty) > 1e-6, open = openM[row.key] ?? false;
              return [<tr key={row.key} className={row.over ? 'over' : ''}><td>{row.label}</td><td>{normalizeUnit(row.unit)}</td><td className="n">{qf(row.contractQty)}</td><td className="n">{qf(row.prevQty)}</td>
                <td className="n"><NumInput aria-label={t('prog.qtyFor', { l: row.label })} value={d.qty[row.key]} onNum={n => edit(x => { if (!n) delete x.qty[row.key]; else x.qty[row.key] = n; })} /></td>
                <td className="n">{qf(row.cumQty)}</td><td className={`n${row.over ? ' bad' : ''}`}>{qf(row.remainingQty)}</td><td className="n">{row.pct == null ? '—' : `${formatNumber(row.pct, lang, { max: 1 })}%`}</td>
                <td className="n">{money(row.unitPrice)}</td><td className="n">{row.thisValue ? money(row.thisValue) : '—'}</td>
                <td><button className={`btn small${msBad ? ' danger' : ''}`} aria-expanded={open} title={t('prog.measures')} aria-label={`${t('prog.measures')}: ${row.label}`} onClick={() => setOpenM(o => ({ ...o, [row.key]: !open }))}>{ms.length ? t('prog.measuresN', { n: ms.length }) : t('prog.measuresAdd')}</button></td></tr>,
              open && <tr key={row.key + ':m'} className="prog-measures"><td colSpan={11}>
                <b>{t('prog.measuresFor', { l: row.label })}</b>
                {ms.map((m, i) => <div key={i} className="prog-mrow">
                  <input value={m.label} maxLength={PROGRESS_LIMITS.label} placeholder={t('prog.measureLabel')} aria-label={t('prog.measureLabel')} onChange={e => edit(x => { x.measures[row.key]![i]!.label = e.target.value; })} />
                  <NumInput aria-label={t('prog.measureQty')} value={m.qty} onNum={n => edit(x => { x.measures[row.key]![i]!.qty = n ?? 0; })} />
                  <span>{normalizeUnit(row.unit)}</span>
                  <button className="btn danger" aria-label={t('prog.measureRemove')} onClick={() => edit(x => { x.measures[row.key]!.splice(i, 1); if (!x.measures[row.key]!.length) delete x.measures[row.key]; })}>×</button></div>)}
                <p>{ms.length < PROGRESS_LIMITS.measuresPerLine && <button className="btn" onClick={() => edit(x => { (x.measures[row.key] ??= []).push({ label: '', qty: 0 }); })}>{t('prog.measureAdd')}</button>}
                  {ms.length > 0 && <> <button className="btn" onClick={() => edit(x => { const s = Math.round(msSum * 1000) / 1000; if (s > 0) x.qty[row.key] = s; else delete x.qty[row.key]; })}>{t('prog.measureUse', { q: qf(msSum) })}</button></>}</p>
                {msBad && <p className="bad small" role="status">{t('prog.measureMismatch', { s: qf(msSum), q: qf(row.thisQty) })}</p>}</td></tr>]; })}</tbody>; })}
      </table></div>

      <h4>{t('prog.extra')}</h4>
      <p className="muted small">{t('prog.extraHelp')}</p>
      {d.extra.map((l, i) => <div key={l.key} className="prog-xrow">
        <input value={l.label} maxLength={PROGRESS_LIMITS.label} placeholder={t('prog.col.name')} aria-label={t('prog.col.name')} onChange={e => edit(x => { x.extra[i]!.label = e.target.value; })} />
        <input value={l.unit} maxLength={PROGRESS_LIMITS.unit} placeholder={t('prog.col.unit')} aria-label={t('prog.col.unit')} onChange={e => edit(x => { x.extra[i]!.unit = e.target.value; })} />
        <NumInput aria-label={t('prog.col.this')} value={l.qty} onNum={n => edit(x => { x.extra[i]!.qty = n ?? 0; })} />
        <NumInput aria-label={t('prog.col.price')} value={l.unitPrice} onNum={n => edit(x => { x.extra[i]!.unitPrice = n ?? 0; })} />
        <span className="n">{money(calc.extra[i]?.thisValue ?? 0)}</span>
        <button className="btn danger" aria-label={t('prog.extraRemove')} onClick={() => edit(x => { x.extra.splice(i, 1); })}>×</button></div>)}
      {d.extra.length < PROGRESS_LIMITS.extra && <p className="prog-xadd">
        <button className="btn" onClick={() => edit(x => { x.extra.push({ key: newKey(), label: '', unit: 'buc', qty: 1, unitPrice: 0, kind: 'extra' } as BaseLine); })}>{t('prog.extraAdd')}</button>
        {importable.map(c => <button key={c.id} className="btn" onClick={() => edit(x => { x.extra.push({ key: coKey(c.number), label: t('prog.extraFromChange', { n: c.number, title: c.title }).slice(0, PROGRESS_LIMITS.label), unit: 'serv', qty: 1, unitPrice: c.amount, kind: 'extra' }); })}>
          {t('prog.extraImport', { n: c.number, title: c.title, v: money(c.amount) })}</button>)}</p>}
      {importable.length === 0 && <p className="muted small">{t('prog.extraNoApproved')}</p>}

      {(calc.warnings.length > 0 || issues.some(i => i.number === d.number)) && <ul className="prog-warn" role="status">
        {calc.warnings.map((w, k) => <li key={k}>{t(`prog.warn.${w.code}`, { l: labelOf(w.key) })}</li>)}
        {issues.filter(i => i.number === d.number).map((i, k) => <li key={'i' + k}>{t(`prog.warn.${i.code}`, { n: i.number })}</li>)}</ul>}

      <table className="prog-sum"><tbody>
        <tr><td>{t('prog.t.this')}</td><td className="n">{money(calc.totals.thisValue)}</td></tr>
        {calc.totals.extraThis > 0 && <tr><td><small>{t('prog.t.extra')}</small></td><td className="n"><small>{money(calc.totals.extraThis)}</small></td></tr>}
        {d.advancePct != null && <tr><td>{t('prog.t.advance', { p: d.advancePct })}</td><td className="n">−{money(calc.totals.advance)}</td></tr>}
        {calc.totals.vat != null && <tr><td>{t('prog.t.vat', { p: d.vatPct ?? 0 })}</td><td className="n">{money(calc.totals.vat)}</td></tr>}
        {d.retentionPct != null && <tr><td>{t('prog.t.retention', { p: d.retentionPct })}</td><td className="n">−{money(calc.totals.retention)}</td></tr>}
        <tr className="prog-due"><td><b>{t('prog.t.payable')}</b></td><td className="n"><b>{money(calc.totals.payable)}</b></td></tr>
        <tr><td><small>{t('prog.t.cum')}</small></td><td className="n"><small>{money(calc.totals.cumValue)} / {money(calc.totals.contractValue)}{calc.totals.pct != null && ` (${formatNumber(calc.totals.pct, lang, { max: 2 })}%)`}</small></td></tr>
      </tbody></table>
      <label className="prog-note">{t('prog.note')} <textarea rows={2} maxLength={PROGRESS_LIMITS.note} value={d.note ?? ''} onChange={e => edit(x => { if (e.target.value) x.note = e.target.value; else delete x.note; })} /></label>
      <p className="prog-actions"><button className="btn primary" disabled={busy || !d.from || !d.to || d.to < d.from} onClick={save}>{t('prog.save')}</button> <button className="btn" disabled={busy} onClick={() => setD(null)}>{t('dlg.cancel')}</button>
        {(!d.from || !d.to || d.to < d.from) && <small className="bad"> {t('prog.periodInvalid')}</small>}</p>
    </div>}
  </section>);
}
