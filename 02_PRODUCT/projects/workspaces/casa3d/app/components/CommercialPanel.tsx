'use client';
// Panoul „Comercial” din etapa Ofertă (doar pentru designer): discount trade și adaos pe furnizor, profitul proiectului,
// graficul de plăți și legăturile spre proformă și CSV. Nimic de aici nu ajunge pe linkul clientului.
import { useEffect, useRef, useState } from 'react';
import { ListSkeleton } from './Skeleton';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { DEFAULT_MILESTONES, pctOk, pctSum, COMMERCIAL_LIMITS, type CommercialData, type Milestone, type ProfitReport, type ScheduleRow, type Terms, type ProjectChain } from '@/core/commercial';
import { ChainTable, TOTALS_EVENT, FEE_EVENT } from './ChainTable';

type Resp = { data: CommercialData; report: ProfitReport; chain: ProjectChain; schedule: { rows: ScheduleRow[]; paid: number; outstanding: number; overdue: number; pctSum: number; pctOk: boolean; fixedExceeds: boolean }; suppliers: string[] };
const today = () => new Date().toISOString().slice(0, 10);
const numOrUndef = (s: string) => { const v = Number(s.replace(',', '.')); return s.trim() === '' || !Number.isFinite(v) ? undefined : v; };
const newId = () => 'm' + Math.random().toString(36).slice(2, 10);

export default function CommercialPanel({ id, cur, say }: { id: string; cur: string; say(s: string): void }){
  const { t, lang } = usePrefs(), [r, setR] = useState<Resp | null>(null), [d, setD] = useState<CommercialData | null>(null), [dirty, setDirty] = useState(false), [loadErr, setLoadErr] = useState<string | null>(null);
  const [state, setState] = useState<'saved' | 'saving' | 'error'>('saved'), timer = useRef<any>(null), seq = useRef(0), dirtyRef = useRef(false);
  const money = (v: number | null | undefined) => formatMoney(v, cur, lang, t('common.unknownPrice'));
  // la o eroare de citire (de ex. 409: datele salvate nu trec validarea) formularul nu se afișează, deci nimic nu se salvează peste ele
  // `keepForm`: după o salvare automată, cifrele se reîmprospătează fără să suprascrie ce s-a tastat între timp
  const load = (keepForm = false) => fetch(`/api/projects/${id}/commercial`).then(async x => { if (x.ok) return x.json(); const j = await x.json().catch(() => null); setLoadErr(j?.error || t('com.loadError')); return null; })
    .then((j: Resp | null) => { if (j){ setR(j); if (!keepForm){ setD(j.data); setDirty(false); dirtyRef.current = false; } setLoadErr(null); } }).catch(() => setLoadErr(t('com.loadError')));
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  // onorariul schimbat din buget sau din contract: cifrele se reîncarcă (formularul rămâne dacă are modificări nesalvate)
  useEffect(() => { const on = (e: Event) => { if ((e as CustomEvent).detail?.id === id) load(dirtyRef.current); }; window.addEventListener(FEE_EVENT, on); return () => window.removeEventListener(FEE_EVENT, on); }, [id]); // eslint-disable-line
  // salvare automată, ca în celelalte panouri: la 0,8 s după ultima modificare, doar dacă graficul de plăți e valid (100%)
  useEffect(() => { if (!dirty || !d) return; if (!pctOk(d.milestones)){ clearTimeout(timer.current); return; } setState('saving'); clearTimeout(timer.current);
    timer.current = setTimeout(() => save(seq.current), 800); return () => clearTimeout(timer.current); }, [d, dirty]); // eslint-disable-line
  if (loadErr && (!r || !d)) return <section className="commercial"><h3>{t('com.title')}</h3><p className="bad" role="alert">{loadErr}</p></section>;
  if (!r || !d) return <section className="commercial"><h3>{t('com.title')}</h3><ListSkeleton label={t('print.loading')} rows={2} /></section>;
  const edit = (fn: (x: CommercialData) => void) => { const x: CommercialData = JSON.parse(JSON.stringify(d)); fn(x); seq.current++; dirtyRef.current = true; setD(x); setDirty(true); };
  const setTerms = (sup: string | null, k: keyof Terms, v: string) => edit(x => { const tgt = sup == null ? x.defaults : (x.suppliers[sup] ??= {}); const n = numOrUndef(v); if (n == null) delete tgt[k]; else tgt[k] = n;
    if (sup != null && !Object.keys(tgt).length) delete x.suppliers[sup]; });
  const setMs = (i: number, patch: Partial<Milestone>) => edit(x => { const m = { ...x.milestones[i], ...patch } as Milestone;
    for (const k of Object.keys(patch) as (keyof Milestone)[]) if (patch[k] === undefined) delete (m as any)[k];
    if (m.status !== 'paid') delete m.paidAt; else if (!m.paidAt) m.paidAt = today(); x.milestones[i] = m; });
  async function save(at: number){
    try { const res = await fetch(`/api/projects/${id}/commercial`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(d) }), j = await res.json().catch(() => ({}));
      if (!res.ok){ setState('error'); say(j.error || t('com.saveError')); return; }
      const fresh = seq.current === at; await load(!fresh); if (fresh) setState('saved');
      window.dispatchEvent(new CustomEvent(TOTALS_EVENT, { detail: { id } }));
    } catch { setState('error'); say(t('com.saveError')); } }
  const rep = r.report, sumOk = pctOk(d.milestones), rowOf = (mid: string) => r.schedule.rows.find(x => x.id === mid);
  const pctInput = (v: number | undefined, on: (s: string) => void, label: string, max: number) =>
    <input inputMode="decimal" type="number" min={0} max={max} step="0.5" value={v ?? ''} placeholder="—" aria-label={label} onChange={e => on(e.target.value)} />;
  return (<section className="commercial">
    <h3>{t('com.title')}</h3>
    <p className="muted small">{t('com.privateNote')}</p>

    <h4>{t('com.terms')}</h4>
    <p className="muted small">{t('com.termsHelp')}</p>
    <table className="com-table"><thead><tr><th>{t('com.supplier')}</th><th>{t('com.discount')}</th><th>{t('com.markup')}</th></tr></thead>
      <tbody>
        <tr><td><b>{t('com.default')}</b></td><td>{pctInput(d.defaults.discountPct, v => setTerms(null, 'discountPct', v), t('com.discount'), COMMERCIAL_LIMITS.discountMax)}</td><td>{pctInput(d.defaults.markupPct, v => setTerms(null, 'markupPct', v), t('com.markup'), COMMERCIAL_LIMITS.markupMax)}</td></tr>
        {r.suppliers.slice(0, COMMERCIAL_LIMITS.suppliers).map(s => <tr key={s}><td>{s}</td>
          <td>{pctInput(d.suppliers[s]?.discountPct, v => setTerms(s, 'discountPct', v), `${t('com.discount')} ${s}`, COMMERCIAL_LIMITS.discountMax)}</td>
          <td>{pctInput(d.suppliers[s]?.markupPct, v => setTerms(s, 'markupPct', v), `${t('com.markup')} ${s}`, COMMERCIAL_LIMITS.markupMax)}</td></tr>)}
      </tbody></table>

    <h4>{t('com.profit')}</h4>
    {dirty && state === 'saving' && <p className="muted small">{t('com.savingNote')}</p>}
    <table className="com-table"><thead><tr><th>{t('com.supplier')}</th><th>{t('com.cost')}</th><th>{t('com.clientPrice')}</th><th>{t('com.profitCol')}</th></tr></thead>
      <tbody>{Object.entries(rep.bySupplier).sort((a, b) => a[0].localeCompare(b[0])).map(([s, g]) => <tr key={s}><td>{s}{g.unknown > 0 && <small> {t('com.unknownN', { n: g.unknown })}</small>}</td><td>{money(g.cost)}</td><td>{money(g.client)}</td><td>{money(g.profit)}</td></tr>)}</tbody></table>
    <table className="com-table"><thead><tr><th>{t('com.category')}</th><th>{t('com.cost')}</th><th>{t('com.clientPrice')}</th><th>{t('com.profitCol')}</th></tr></thead>
      <tbody>{Object.entries(rep.byCategory).map(([c, g]) => <tr key={c}><td>{t(`budget.cat.${c}`)}</td><td>{money(g.cost)}</td><td>{money(g.client)}</td><td>{money(g.profit)}</td></tr>)}
        <tr><td>{t('com.cat.labor')} <small>{t('com.passThrough')}</small></td><td>{money(rep.labor)}</td><td>{money(rep.labor)}</td><td>{money(0)}</td></tr>
        {rep.extras > 0 && <tr><td>{t('com.cat.extras')} <small>{t('com.passThrough')}</small></td><td>{money(rep.extras)}</td><td>{money(rep.extras)}</td><td>{money(0)}</td></tr>}
        <tr><td>{t('com.cat.fee')}</td><td>—</td><td>{money(rep.fee == null ? null : rep.fee + rep.feeVat)}</td><td>{money(rep.fee)}</td></tr>
        <tr className="com-total"><td><b>{t('chain.invoiceable')}</b></td><td><b>{money(rep.totals.cost)}</b></td><td><b>{money(rep.totals.client)}</b></td><td><b>{money(rep.totals.profit)}</b>{rep.totals.marginPct != null && <small> {t('com.margin', { p: rep.totals.marginPct })}</small>}</td></tr></tbody></table>
    {rep.unknownLines > 0 && <p className="muted small">{t('com.unknownLines', { n: rep.unknownLines })}</p>}
    {rep.feeUnknown && <p className="muted small">{t('com.feeUnknown')}</p>}
    <p className="muted small">{t('com.vatBasis')}</p>
    <ChainTable chain={r.chain} money={money} />

    <h4>{t('com.schedule')} <span className={`pill${sumOk ? '' : ' bad'}`}>{t('com.pctSum', { p: pctSum(d.milestones) })}</span>{r.schedule.overdue > 0 && <span className="pill bad">{t('com.overdueN', { n: r.schedule.overdue })}</span>}</h4>
    {d.milestones.length === 0 && <p><button className="btn" onClick={() => edit(x => { x.milestones = DEFAULT_MILESTONES.map(m => ({ ...m })); })}>{t('com.useTemplate')}</button></p>}
    {d.milestones.map((m, i) => { const row = rowOf(m.id), late = m.status !== 'paid' && !!m.due && m.due < today();
      return <div key={m.id} className={`com-ms${late ? ' overdue' : ''}`}>
        <input value={m.label} maxLength={COMMERCIAL_LIMITS.label} aria-label={t('com.msLabel')} onChange={e => setMs(i, { label: e.target.value })} />
        <select value={m.fixed != null ? 'fixed' : 'pct'} aria-label={t('com.msKind')} onChange={e => setMs(i, e.target.value === 'fixed' ? { fixed: row?.amount ?? 0, pct: undefined } : { pct: 0, fixed: undefined })}>
          <option value="pct">%</option><option value="fixed">{cur}</option></select>
        <input type="number" inputMode="decimal" min={0} step="0.01" aria-label={t('com.msValue')} value={m.fixed ?? m.pct ?? ''} onChange={e => { const n = numOrUndef(e.target.value) ?? 0; setMs(i, m.fixed != null ? { fixed: n } : { pct: n }); }} />
        <input type="date" value={m.due ?? ''} aria-label={t('com.msDue')} onChange={e => setMs(i, { due: e.target.value || undefined })} />
        <select value={m.status} aria-label={t('com.msStatus')} onChange={e => setMs(i, { status: e.target.value as Milestone['status'] })}><option value="pending">{t('com.st.pending')}</option><option value="paid">{t('com.st.paid')}</option></select>
        {m.status === 'paid' && <input type="date" value={m.paidAt ?? ''} aria-label={t('com.msPaidAt')} onChange={e => setMs(i, { paidAt: e.target.value || undefined })} />}
        <span className="com-amt">{row && !dirty ? money(row.amount) : '…'}{late && <small className="bad"> {t('com.overdue')}</small>}</span>
        <button className="btn danger" aria-label={t('com.msRemove')} onClick={() => edit(x => { x.milestones.splice(i, 1); if (x.proforma.milestoneId === m.id) delete x.proforma.milestoneId; })}>×</button>
      </div>; })}
    {d.milestones.length > 0 && d.milestones.length < COMMERCIAL_LIMITS.milestones && <p><button className="btn" onClick={() => edit(x => { x.milestones.push({ id: newId(), label: t('com.msNew'), pct: 0, status: 'pending' }); })}>{t('com.msAdd')}</button></p>}
    {!sumOk && <p className="bad small">{t('com.pctError', { p: pctSum(d.milestones) })}</p>}
    {r.schedule.rows.length > 0 && !dirty && <p className="muted small">{t('com.paidOutstanding', { paid: money(r.schedule.paid), rest: money(r.schedule.outstanding) })}</p>}

    <h4>{t('com.proforma')}</h4>
    <div className="com-pf">
      <label>{t('com.series')} <input value={d.proforma.series} maxLength={COMMERCIAL_LIMITS.series} onChange={e => edit(x => { x.proforma.series = e.target.value.toUpperCase(); })} /></label>
      <label>{t('com.number')} <input type="number" min={1} step={1} value={d.proforma.number} onChange={e => edit(x => { x.proforma.number = Math.max(1, Math.floor(Number(e.target.value) || 1)); })} /></label>
      <label>{t('com.issueDate')} <input type="date" value={d.proforma.issueDate ?? ''} onChange={e => edit(x => { if (e.target.value) x.proforma.issueDate = e.target.value; else delete x.proforma.issueDate; })} /></label>
      <label>{t('com.dueDate')} <input type="date" value={d.proforma.dueDate ?? ''} onChange={e => edit(x => { if (e.target.value) x.proforma.dueDate = e.target.value; else delete x.proforma.dueDate; })} /></label>
      <label>{t('com.forMilestone')} <select value={d.proforma.milestoneId ?? ''} onChange={e => edit(x => { if (e.target.value) x.proforma.milestoneId = e.target.value; else delete x.proforma.milestoneId; })}>
        <option value="">{t('com.fullAmount')}</option>{d.milestones.map(m => <option key={m.id} value={m.id}>{m.label}</option>)}</select></label>
    </div>
    <p className="muted small" role="status">{!sumOk ? t('com.state.invalid') : t(`com.state.${dirty && state === 'saved' ? 'saving' : state}`)}
      {state === 'error' && <> <button className="btn" onClick={() => save(seq.current)}>{t('com.retry')}</button></>}</p>
    <div className="docs">
      <a className="docrow" href={`/p/${id}/proforma`} target="_blank" rel="noopener"><b>{t('com.openProforma')}</b><small>{t('com.openProformaSub')}</small></a>
      <a className="docrow" href={`/api/projects/${id}/commercial/proforma.csv`}><b>{t('com.csv')}</b><small>{t('com.csvGeneric')}</small></a>
    </div>
  </section>);
}
