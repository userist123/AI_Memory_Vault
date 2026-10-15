'use client';
// Produse proprii: dialogul „Importă CSV” (previzualizare cu erori pe rând, apoi import), export, șablon,
// și devizul cu produsele proprii. Validarea de autoritate e pe server; aici doar se arată rezultatul ei.
import { useEffect, useRef, useState } from 'react';
import BudgetPanel, { type CommercialInputs } from './BudgetPanel';
import { TOTALS_EVENT, FEE_EVENT } from './ChainTable';
import type { CustomSource } from '@/core/commercial';
import { usePrefs } from '@/lib/prefs';
import { formatMoney, catalogCurrency } from '@/core/format';
import { CSV_LIMITS } from '@/core/custom-csv';

type Row = { line: number; ok: boolean; errors: string[]; item: { name: string; supplier: string; qty: number; unit: string; price: number | null; costPrice: number | null } | null };
type Preview = { ok: number; failed: number; rows: Row[] };
export function ImportCsvButtons({ id, say, onDone }: { id: string; say(s: string): void; onDone(): void }){
  const { t } = usePrefs(), [open, setOpen] = useState(false), [csv, setCsv] = useState(''), [pv, setPv] = useState<Preview | null>(null), [err, setErr] = useState(''), [busy, setBusy] = useState(false), file = useRef<HTMLInputElement>(null);
  const reset = () => { setCsv(''); setPv(null); setErr(''); };
  async function send(dryRun: boolean){ setBusy(true); setErr('');
    try { const r = await fetch(`/api/projects/${id}/items/import`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ csv, dryRun }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('cprod.error')); if (j.details?.rows) setPv({ ok: 0, failed: j.details.rows.length, rows: j.details.rows }); return; }
      if (dryRun) setPv(j); else { say(t('cprod.imported', { n: j.imported })); setOpen(false); reset(); onDone(); }
    } catch { setErr(t('cprod.error')); } finally { setBusy(false); } }
  async function pick(f: File | undefined){ if (!f) return; if (f.size > CSV_LIMITS.bytes){ setErr(t('cprod.tooBig')); return; } setCsv(await f.text()); setPv(null); setErr(''); }
  return (<>
    <div className="clip-open"><button className="btn" onClick={() => setOpen(true)}>{t('cprod.import')}</button>
      <a className="btn" href={`/api/projects/${id}/items/export`} download>{t('cprod.export')}</a>
      <a className="btn" href={`/api/projects/${id}/items/template`} download>{t('cprod.template')}</a></div>
    {open && <section className="clip" role="dialog" aria-label={t('cprod.title')}>
      <header><h2>{t('cprod.title')}</h2><button className="btn" onClick={() => { setOpen(false); reset(); }}>{t('cprod.close')}</button></header>
      <p className="muted">{t('cprod.hint')}</p><p className="muted">{t('cprod.internal')}</p>
      <textarea rows={7} value={csv} placeholder={t('cprod.paste')} aria-label={t('cprod.paste')} onChange={e => { setCsv(e.target.value); setPv(null); }} />
      <div className="clip-link"><input ref={file} type="file" accept=".csv,text/csv,text/plain" aria-label={t('cprod.file')} onChange={e => pick(e.target.files?.[0])} />
        <button className="btn" disabled={busy || !csv.trim()} onClick={() => send(true)}>{t('cprod.preview')}</button>
        <button className="btn primary" disabled={busy || !pv || pv.failed > 0 || pv.ok === 0} onClick={() => send(false)}>{t('cprod.confirm', { n: pv?.ok ?? 0 })}</button></div>
      {err && <p className="issue WARNING" role="alert">{err}</p>}
      {pv && <><p><b>{t('cprod.rowsOk', { ok: pv.ok, failed: pv.failed })}</b></p>
        <ul className="clip-errors">{pv.rows.map(r => <li key={r.line} className={r.ok ? '' : 'late'}><b>{t('cprod.line', { n: r.line })}</b> {r.ok ? `${r.item?.name} · ${r.item?.supplier}` : r.errors.join(' ')}</li>)}</ul></>}
    </section>}
  </>);
}

/** Devizul cu produsele proprii (la prețul de client); costul de achiziție și marja se arată doar aici, în aplicația proprietarului. */
export default function BudgetWithCustom(props: React.ComponentProps<typeof BudgetPanel> & { id: string }){
  const { id, ...rest } = props, { t, lang } = usePrefs(), [data, setData] = useState<{ items: CustomSource[]; budget: { count: number; sell: number; cost: number; margin: number; noPrice: number } } | null>(null);
  const [com, setCom] = useState<CommercialInputs | null>(null);
  useEffect(() => { let alive = true; fetch(`/api/projects/${id}/items`).then(r => r.ok ? r.json() : null).then(j => { if (alive && j) setData(j); }).catch(() => {}); return () => { alive = false; }; }, [id]);
  // termenii comerciali (adaos) și onorariul contractului: totalul bugetului e același lanț ca în panoul comercial și pe proformă
  const loadCom = () => fetch(`/api/projects/${id}/commercial`).then(r => r.ok ? r.json() : null).then(j => setCom(j ? { data: j.data, fee: j.fee } : null)).catch(() => setCom(null));
  useEffect(() => { loadCom(); const on = (e: Event) => { if ((e as CustomEvent).detail?.id === id) loadCom(); };
    window.addEventListener(TOTALS_EVENT, on); window.addEventListener(FEE_EVENT, on); return () => { window.removeEventListener(TOTALS_EVENT, on); window.removeEventListener(FEE_EVENT, on); }; }, [id]); // eslint-disable-line
  const b = data?.budget, cur = catalogCurrency(rest.catalog), m = (v: number) => formatMoney(v, cur, lang);
  return (<><BudgetPanel {...rest} custom={data?.items} com={com} projectId={id} />
    {b && b.count > 0 && <p className="muted" role="note">{t('cprod.budget')}: {m(b.sell)} · {t('cprod.budgetCost')}: {m(b.cost)} · {t('cprod.margin')}: {m(b.margin)}{b.noPrice ? ` · ${t('cprod.noPrice', { n: b.noPrice })}` : ''}</p>}</>);
}
