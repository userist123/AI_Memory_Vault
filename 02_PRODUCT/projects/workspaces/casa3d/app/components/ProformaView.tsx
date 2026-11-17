'use client';
// Proforma tipăribilă (A4, PDF din browser): vânzătorul din profilul studioului, cumpărătorul din fișa proiectului,
// liniile cu prețuri de client grupate pe categorie, TVA 21% și etapa de plată la care se referă.
// Nu e factură fiscală și nu se transmite prin e-Factura; pagina o spune explicit.
import { useEffect, useState } from 'react';
import { DocSkeleton } from './Skeleton';
import type { Proforma } from '@/core/commercial';
import type { ClientInfo } from '@/core/project-meta';
import type { StudioProfile } from '@/core/studio-profile';
import { usePrefs } from '@/lib/prefs';
import LoadError from './LoadError';
import { formatNumber, normalizeUnit, qtyDecimals } from '@/core/format';
import { intlLocale } from '@/lib/i18n';
import { askConfirm } from '@/lib/dialog';
/** gazda paginii produsului (ex. www.dedeman.ro), ca text scurt lângă preț; linkul complet rămâne în href */
const hostOf = (u: string) => { try { return new URL(u).hostname; } catch { return u; } };

type Data = { name: string; client: ClientInfo | null; siteAddress: string | null; seller: StudioProfile; series: string; number: number; issueDate: string; dueDate: string | null; proforma: Proforma;
  chain?: { invoiceable: number; contingency: number; contingencyPct: number; total: number }; frozen?: true; issuedAt?: string };
type Issued = { series: string; number: number; issuedAt: string; gross: number | null };
export default function ProformaView({ id }: { id: string }){
  const { t, lang } = usePrefs(), [d, setD] = useState<Data | null>(null), [err, setErr] = useState(false), [issued, setIssued] = useState<Issued[]>([]), [busy, setBusy] = useState(false), [msg, setMsg] = useState('');
  // ?s=<serie>&n=<număr>: proforma emisă (înghețată); fără: ciorna, calculată din proiectul curent
  const [sel] = useState(() => { if (typeof location === 'undefined') return null; const u = new URLSearchParams(location.search), s = u.get('s'), n = u.get('n'); return s && n ? { s, n } : null; });
  const q = sel ? `?s=${encodeURIComponent(sel.s)}&n=${encodeURIComponent(sel.n)}` : '';
  const seenDate = (d: string) => { const x = new Date(d + 'T00:00:00Z'); return isNaN(+x) ? d : x.toLocaleDateString(intlLocale(lang), { timeZone: 'UTC' }); };
  useEffect(() => { fetch(`/api/projects/${id}/commercial/proforma${q}`).then(r => r.ok ? r.json() : Promise.reject()).then(setD).catch(() => setErr(true));
    fetch(`/api/projects/${id}/commercial/proformas`).then(r => r.ok ? r.json() : Promise.reject()).then(setIssued).catch(() => setMsg(t('pf.listFailed'))); }, [id, q]);
  async function issue(){ if (!d || busy) return;
    if (!(await askConfirm({ title: t('pf.issueTitle', { s: d.series, n: d.number }), body: t('pf.issueBody'), confirmLabel: t('pf.issueOk') }))) return;
    setBusy(true); setMsg(''); try { const r = await fetch(`/api/projects/${id}/commercial/proforma/issue`, { method: 'POST' }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setMsg(j.error || t('pf.issueFailed')); return; } location.assign(`/p/${id}/proforma?s=${encodeURIComponent(j.series)}&n=${j.number}${j.nextSaved === false ? '&next=0' : ''}`); } finally { setBusy(false); } }
  if (err) return <LoadError />;
  if (!d) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  const loc = intlLocale(lang), n2 = (v: number) => new Intl.NumberFormat(loc, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v);
  const date = (s: string) => new Date(s + 'T12:00:00').toLocaleDateString(loc, { day: 'numeric', month: 'long', year: 'numeric' });
  // înainte de tipărire: ce date lipsesc de pe document (se completează în profilul studioului și în fișa proiectului)
  const missing = [!d.seller.name && t('doc.m.studio'), !d.seller.cui && 'CUI', !d.seller.iban && 'IBAN', !d.seller.address && t('doc.m.studioAddr'), !d.client?.name && t('doc.m.client')].filter(Boolean) as string[];
  const s = d.seller, p = d.proforma, catLabel = (c: string) => c === 'labor' ? t('com.cat.labor') : c === 'fee' ? t('com.cat.fee') : c === 'extras' ? t('com.cat.extras') : t(`budget.cat.${c}`);
  return (<main className="report proforma">
    <style>{`.proforma table{width:100%;border-collapse:collapse;font-size:12px}.proforma th,.proforma td{border-bottom:1px solid var(--rule);padding:4px 6px;text-align:left;vertical-align:top}
.proforma td.n,.proforma th.n{text-align:right;white-space:nowrap}.pf-parties{display:grid;grid-template-columns:1fr 1fr;gap:24px;margin:12px 0}.pf-parties small{display:block}
.pf-sum{margin-left:auto;max-width:340px}.pf-sum td{border:0}.pf-due td{font-weight:600;border-top:1px solid var(--ink)}.pf-logo{max-height:56px;max-width:180px}
@page{size:A4;margin:14mm;@bottom-right{content:'Pagina ' counter(page) ' din ' counter(pages);font:9pt 'Instrument Sans',sans-serif;color:#5F584F}}@media (max-width:560px){.pf-parties{grid-template-columns:1fr}}`}</style>
    <div className="no-print report-bar"><span className="muted small">{d.frozen ? t('pf.issuedBanner', { d: new Date(d.issuedAt!).toLocaleString(loc, { dateStyle: 'long', timeStyle: 'short' }) }) : d.chain ? t('pf.chainNote', { inv: n2(d.chain.invoiceable), total: n2(d.chain.total), res: n2(d.chain.contingency), pct: d.chain.contingencyPct }) : null}</span>
      <a className="btn" href={`/api/projects/${id}/commercial/proforma.csv${q}`}>{t('pf.csv')}</a>
      {d.frozen ? <a className="btn" href={`/p/${id}/proforma`}>{t('pf.viewDraft')}</a> : <button className="btn" disabled={busy} onClick={issue}>{t('pf.issue', { s: d.series, n: d.number })}</button>}
      <button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    {!d.frozen && <p className="missing no-print" role="note"><b>{t('pf.draftBanner')}</b></p>}
    {msg && <p className="missing no-print" role="alert">{msg}</p>}
    {d.frozen && typeof location !== 'undefined' && new URLSearchParams(location.search).get('next') === '0' && <p className="missing no-print" role="alert">{t('pf.nextNotSaved')}</p>}
    {issued.length > 0 && <details className="no-print pf-issued"><summary>{t('pf.issuedList', { n: issued.length })}</summary><ul>{issued.map(x => <li key={`${x.series}-${x.number}`}><a href={`/p/${id}/proforma?s=${encodeURIComponent(x.series)}&n=${x.number}`}>{t('com.proformaNo', { s: x.series, n: x.number })}</a> · {new Date(x.issuedAt).toLocaleDateString(loc)}{x.gross != null && <> · {n2(x.gross)} RON</>}</li>)}</ul></details>}
    {missing.length > 0 && <p className="missing no-print" role="status"><b>{t('doc.missing')}</b>{missing.map(m => <span key={m}>{m}</span>)}</p>}
    <header>{s.logo && <img className="pf-logo" src={s.logo} alt="" />}<span className="kicker">{d.frozen ? t('com.proformaKicker') : t('pf.draftKicker')}</span>
      <h1>{t('com.proformaNo', { s: d.series, n: d.number })}</h1>
      <p>{d.frozen ? t('com.issued', { d: date(d.issueDate) }) : t('pf.draftOn', { d: date(d.issueDate) })}{d.dueDate && <> · {t('com.due', { d: date(d.dueDate) })}</>} · {d.name}</p></header>
    <div className="pf-parties">
      <div><h3>{t('com.seller')}</h3><b>{s.name || '—'}</b>{s.cui && <small>CUI {s.cui}</small>}{s.regCom && <small>{t('com.regCom')} {s.regCom}</small>}{s.address && <small>{s.address}</small>}
        {s.iban && <small>IBAN {s.iban}</small>}{s.bank && <small>{t('com.bank')} {s.bank}</small>}{(s.phone || s.email) && <small>{[s.phone, s.email].filter(Boolean).join(' · ')}</small>}
        {!s.name && <small className="no-print bad">{t('com.noSeller')}</small>}</div>
      <div><h3>{t('com.buyer')}</h3><b>{d.client?.name || '—'}</b>{d.client?.address && <small>{d.client.address}</small>}{d.siteAddress && <small>{t('com.site', { a: d.siteAddress })}</small>}
        {(d.client?.phone || d.client?.email) && <small>{[d.client?.phone, d.client?.email].filter(Boolean).join(' · ')}</small>}</div>
    </div>
    <table><thead><tr><th>#</th><th>{t('com.col.name')}</th><th>{t('com.col.unit')}</th><th className="n">{t('com.col.qty')}</th><th className="n">{t('com.col.unitNet')}</th><th className="n">{t('com.col.net')}</th><th className="n">{t('com.col.vat', { p: p.vatPct })}</th></tr></thead>
      {p.groups.map(g => <tbody key={g.category}><tr><td colSpan={7}><b>{catLabel(g.category)}</b></td></tr>
        {g.lines.map((l, k) => <tr key={k}><td>{k + 1}</td><td>{l.name}{l.source && <><br /><small className="muted pf-src">{t('pf.seen', { store: l.source.store, date: seenDate(l.source.seenAt) })} <a href={l.source.url} target="_blank" rel="noopener noreferrer">{hostOf(l.source.url)}</a></small></>}</td><td>{normalizeUnit(l.unit)}</td><td className="n">{formatNumber(l.qty, lang, { decimals: qtyDecimals(l.unit, l.qty) })}</td><td className="n">{n2(l.unitNet)}</td><td className="n">{n2(l.net)}</td><td className="n">{n2(l.vat)}</td></tr>)}
        <tr><td colSpan={5} className="n"><small>{t('com.subtotal', { c: catLabel(g.category) })}</small></td><td className="n">{n2(g.net)}</td><td className="n">{n2(g.vat)}</td></tr></tbody>)}
    </table>
    <table className="pf-sum"><tbody>
      <tr><td>{t('com.totalNet')}</td><td className="n">{n2(p.net)}</td></tr><tr><td>{t('com.totalVat', { p: p.vatPct })}</td><td className="n">{n2(p.vat)}</td></tr>
      <tr><td><b>{t('com.totalGross')}</b></td><td className="n"><b>{n2(p.gross)}</b></td></tr>
      {p.milestone && <tr><td>{t('com.milestoneLine', { l: p.milestone.label + (p.milestone.pct != null ? ` (${p.milestone.pct}%)` : '') })}</td><td className="n">{n2(p.milestone.net)} + {n2(p.milestone.vat)}</td></tr>}
      <tr className="pf-due"><td>{p.milestone ? t('com.dueNowMilestone') : t('com.dueNowFull')}</td><td className="n">{n2(p.due)} RON</td></tr></tbody></table>
    {!p.feeVatPayer && p.groups.some(g => g.category === 'fee') && <p className="small">{t('com.feeNoVat')}</p>}
    {p.unknownLines > 0 && <p className="muted small">{t('com.pfUnknown', { n: p.unknownLines })}</p>}
    {p.groups.some(g => g.lines.some(l => l.source)) && <p className="muted small">{t('pf.observedNote')}</p>}
    <p className="muted small">{t('com.notFiscal')}</p>
  </main>);
}
