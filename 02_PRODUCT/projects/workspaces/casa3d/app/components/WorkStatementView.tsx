'use client';
// Situația de lucrări tipăribilă (A4 culcat, PDF din browser): antet cu numărul și perioada, tabelul complet pe devizul de
// bază, lucrările suplimentare separat, totalurile, foile de atașament ca anexă și rândurile de semnătură.
// E un document de decontare între părți, nu un formular tipizat și nu un document fiscal; pagina o spune explicit.
import { useEffect, useState } from 'react';
import { DocSkeleton } from './Skeleton';
import LoadError from './LoadError';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { formatNumber, normalizeUnit } from '@/core/format';
import { computeStatement, statementIssues, type Baseline, type BaseKind, type StatementInput } from '@/core/progress';
import type { ClientInfo } from '@/core/project-meta';
import type { StudioProfile } from '@/core/studio-profile';

type Data = { name: string; client: ClientInfo | null; siteAddress: string | null; seller: StudioProfile; baseline: Baseline | null; statements: StatementInput[] };
const KINDS: BaseKind[] = ['labor', 'material', 'service'];

export default function WorkStatementView({ id, n }: { id: string; n: number }){
  const { t, lang } = usePrefs(), [d, setD] = useState<Data | null>(null), [err, setErr] = useState(false);
  useEffect(() => { fetch(`/api/projects/${id}/progress?doc=1`).then(r => r.ok ? r.json() : Promise.reject()).then(setD).catch(() => setErr(true)); }, [id]);
  if (err) return <LoadError />;
  if (!d) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  const st = d.statements.find(s => s.number === n);
  if (!d.baseline || !st) return <LoadError />;
  const loc = intlLocale(lang), n2 = (v: number) => new Intl.NumberFormat(loc, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v), qf = (v: number) => formatNumber(v, lang, { max: 3 });
  const date = (s: string) => new Date(s.slice(0, 10) + 'T12:00:00').toLocaleDateString(loc, { day: 'numeric', month: 'long', year: 'numeric' });
  const c = computeStatement(d.baseline, d.statements, n), T = c.totals, s = d.seller, issues = statementIssues(d.statements).filter(i => i.number === n);
  const labelOf = (k: string) => d.baseline!.lines.find(l => l.key === k)?.label ?? k, rowOf = (k: string) => c.rows.find(r => r.key === k);
  const missing = [!s.name && t('doc.m.studio'), !d.client?.name && t('doc.m.client')].filter(Boolean) as string[];
  const sheets = Object.entries(st.measures ?? {}).filter(([k]) => rowOf(k));
  let no = 0;
  return (<main className="report wstat">
    <style>{`.wstat{max-width:1180px}.wstat table{width:100%;border-collapse:collapse;font-size:11px}.wstat th,.wstat td{border-bottom:1px solid var(--rule);padding:3px 5px;text-align:left;vertical-align:top}
.wstat thead th{border-bottom:1px solid var(--ink);font-weight:600}.wstat td.n,.wstat th.n{text-align:right;white-space:nowrap}.wstat tr.ws-group td{font-weight:600;padding-top:8px}.wstat tr.ws-total td{font-weight:600;border-top:1px solid var(--ink)}
.ws-parties{display:grid;grid-template-columns:1fr 1fr;gap:24px;margin:12px 0}.ws-parties small{display:block}.ws-sum{margin:14px 0 0 auto;max-width:420px}.wstat .ws-sum td{border:0}.wstat .ws-sum .ws-due td{font-weight:600;border-top:1px solid var(--ink)}
.ws-sheet{break-inside:avoid;margin:10px 0}.ws-sheet table{max-width:560px}.ws-annex{break-before:page}.ws-sign{display:grid;grid-template-columns:1fr 1fr;gap:48px;margin-top:36px;break-inside:avoid}
.ws-sign div{border-top:1px solid var(--ink);padding-top:6px;min-height:64px}.ws-sign small{display:block}.ws-legal{margin-top:18px}.wstat thead{display:table-header-group}.wstat tr{break-inside:avoid}
@page{size:A4 landscape;margin:12mm;@bottom-right{content:counter(page) ' / ' counter(pages);font:9pt 'Instrument Sans',sans-serif;color:#5F584F}}@media (max-width:560px){.ws-parties,.ws-sign{grid-template-columns:1fr}}`}</style>
    <div className="no-print report-bar"><span className="muted small">{t('wst.barNote')}</span><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    {missing.length > 0 && <p className="missing no-print" role="status"><b>{t('doc.missing')}</b>{missing.map(m => <span key={m}>{m}</span>)}</p>}
    {(c.warnings.length > 0 || issues.length > 0) && <ul className="no-print bad small" role="status">
      {c.warnings.map((w, k) => <li key={k}>{t(`prog.warn.${w.code}`, { l: labelOf(w.key) })}</li>)}{issues.map((i, k) => <li key={'i' + k}>{t(`prog.warn.${i.code}`, { n: i.number })}</li>)}</ul>}
    <header><span className="kicker">{t('wst.kicker')}</span>
      <h1>{t('wst.title', { n })}</h1>
      <p>{t('wst.period', { a: date(st.from), b: date(st.to) })} · {d.name}</p>
      <p className="small">{t('wst.baseline', { d: date(d.baseline.createdAt), v: n2(T.contractValue) })}</p></header>
    <div className="ws-parties">
      <div><h3>{t('wst.contractor')}</h3><b>{s.name || '—'}</b>{s.cui && <small>CUI {s.cui}</small>}{s.address && <small>{s.address}</small>}{(s.phone || s.email) && <small>{[s.phone, s.email].filter(Boolean).join(' · ')}</small>}</div>
      <div><h3>{t('wst.beneficiary')}</h3><b>{d.client?.name || '—'}</b>{d.client?.address && <small>{d.client.address}</small>}{d.siteAddress && <small>{t('com.site', { a: d.siteAddress })}</small>}</div>
    </div>

    <table><thead><tr><th>{t('wst.col.code')}</th><th>{t('prog.col.name')}</th><th>{t('prog.col.unit')}</th><th className="n">{t('prog.col.contract')}</th><th className="n">{t('prog.col.prev')}</th><th className="n">{t('prog.col.this')}</th><th className="n">{t('prog.col.cum')}</th>
      <th className="n">{t('prog.col.rest')}</th><th className="n">{t('prog.col.pct')}</th><th className="n">{t('prog.col.price')}</th><th className="n">{t('wst.col.valueThis')}</th><th className="n">{t('wst.col.valueCum')}</th></tr></thead>
      {KINDS.map(kind => { const rows = c.rows.filter(r => r.kind === kind); if (!rows.length) return null;
        return <tbody key={kind}><tr className="ws-group"><td colSpan={12}>{t(`prog.kind.${kind}`)}</td></tr>
          {rows.map(r => <tr key={r.key}><td>{++no}</td><td>{r.label}</td><td>{normalizeUnit(r.unit)}</td><td className="n">{qf(r.contractQty)}</td><td className="n">{qf(r.prevQty)}</td><td className="n">{qf(r.thisQty)}</td><td className="n">{qf(r.cumQty)}</td>
            <td className="n">{qf(r.remainingQty)}</td><td className="n">{r.pct == null ? '—' : formatNumber(r.pct, lang, { max: 1 })}</td><td className="n">{n2(r.unitPrice)}</td><td className="n">{n2(r.thisValue)}</td><td className="n">{n2(r.cumValue)}</td></tr>)}</tbody>; })}
      <tbody><tr className="ws-total"><td colSpan={10}>{t('wst.totalBase')}</td><td className="n">{n2(T.thisValue - T.extraThis)}</td><td className="n">{n2(T.cumValue)}</td></tr></tbody>
    </table>

    {c.extra.length > 0 && <>
      <h2>{t('wst.extra')}</h2>
      <p className="small">{t('wst.extraNote')}</p>
      <table><thead><tr><th>{t('wst.col.code')}</th><th>{t('prog.col.name')}</th><th>{t('prog.col.unit')}</th><th className="n">{t('prog.col.this')}</th><th className="n">{t('prog.col.price')}</th><th className="n">{t('wst.col.valueThis')}</th></tr></thead>
        <tbody>{c.extra.map((r, k) => <tr key={r.key}><td>S{k + 1}</td><td>{r.label}</td><td>{normalizeUnit(r.unit)}</td><td className="n">{qf(r.thisQty)}</td><td className="n">{n2(r.unitPrice)}</td><td className="n">{n2(r.thisValue)}</td></tr>)}
          <tr className="ws-total"><td colSpan={5}>{t('wst.totalExtra')}</td><td className="n">{n2(T.extraThis)}</td></tr></tbody></table></>}

    <table className="ws-sum"><tbody>
      {c.extra.length > 0 && <><tr><td>{t('wst.totalBase')}</td><td className="n">{n2(T.thisValue - T.extraThis)}</td></tr><tr><td>{t('wst.totalExtra')}</td><td className="n">{n2(T.extraThis)}</td></tr></>}
      <tr><td><b>{t('prog.t.this')}</b></td><td className="n"><b>{n2(T.thisValue)}</b></td></tr>
      {st.advancePct != null && <tr><td>{t('prog.t.advance', { p: st.advancePct })}</td><td className="n">−{n2(T.advance)}</td></tr>}
      {T.vat != null && <tr><td>{t('prog.t.vat', { p: st.vatPct ?? 0 })}</td><td className="n">{n2(T.vat)}</td></tr>}
      {st.retentionPct != null && <tr><td>{t('prog.t.retention', { p: st.retentionPct })}</td><td className="n">−{n2(T.retention)}</td></tr>}
      <tr className="ws-due"><td>{t('prog.t.payable')}</td><td className="n">{n2(T.payable)} RON</td></tr>
      <tr><td><small>{t('wst.cumLine')}</small></td><td className="n"><small>{n2(T.cumValue)} / {n2(T.contractValue)}{T.pct != null && ` (${formatNumber(T.pct, lang, { max: 2 })}%)`}</small></td></tr>
    </tbody></table>
    {T.vat == null && <p className="small">{t(d.baseline.vatIncluded ? 'prog.vatIncluded' : 'wst.noVat')}</p>}
    {st.note && <p><b>{t('prog.note')}:</b> {st.note}</p>}

    <div className="ws-sign">
      <div><b>{t('wst.contractor')}</b><small>{s.name || ' '}</small><small>{t('wst.signLine')}</small></div>
      <div><b>{t('wst.beneficiary')}</b><small>{d.client?.name || ' '}</small><small>{t('wst.signLine')}</small></div>
    </div>
    <p className="muted small ws-legal">{t('wst.legal')}</p>

    {sheets.length > 0 && <section className="ws-annex">
      <h2>{t('wst.annex', { n })}</h2>
      <p className="small">{t('wst.annexNote')}</p>
      {sheets.map(([k, ms]) => { const r = rowOf(k)!, sum = ms.reduce((a, m) => a + m.qty, 0);
        return <div key={k} className="ws-sheet"><h3>{r.label}</h3>
          <table><thead><tr><th>#</th><th>{t('prog.measureLabel')}</th><th className="n">{t('wst.col.qty', { u: normalizeUnit(r.unit) })}</th></tr></thead>
            <tbody>{ms.map((m, i) => <tr key={i}><td>{i + 1}</td><td>{m.label}</td><td className="n">{qf(m.qty)}</td></tr>)}
              <tr className="ws-total"><td colSpan={2}>{t('wst.sheetTotal')}</td><td className="n">{qf(sum)}</td></tr></tbody></table>
          {Math.abs(sum - r.thisQty) > 1e-6 && <p className="bad small">{t('prog.measureMismatch', { s: qf(sum), q: qf(r.thisQty) })}</p>}</div>; })}
    </section>}
  </main>);
}
