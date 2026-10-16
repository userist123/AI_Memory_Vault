'use client';
// Contract și ofertă în editor: tipul și suma onorariului, etapele, avansul, rundele de modificări, TVA; se salvează singură, separat de plan.
import { useEffect, useRef, useState } from 'react';
import { ListSkeleton } from './Skeleton';
import { PHASE_IDS, FEE_HINTS, DEFAULT_CONTRACT, CONTRACT_LIMITS, hintPosition, type ContractData, type FeeKind, type PayRow } from '@/core/contract';
import { FEE_KINDS, type ProjectMeta } from '@/core/project-meta';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { FEE_EVENT } from './ChainTable';

type Calc = { net: number | null; vat: number; gross: number | null; reason?: string };
export default function ContractPanel({ id, meta, say }: { id: string; meta: ProjectMeta; say(s: string): void }){
  const { t, lang } = usePrefs(), [d, setD] = useState<ContractData | null>(null), [calc, setCalc] = useState<Calc | null>(null), [sched, setSched] = useState<PayRow[]>([]), [state, setState] = useState<'saved' | 'saving' | 'error'>('saved'), [loadErr, setLoadErr] = useState<string | null>(null);
  const timer = useRef<any>(null), dirty = useRef(false), money = (n: number | null) => formatMoney(n, 'RON', lang);
  // ceilalți (fișa proiectului, bugetul) află imediat onorariul: contractul e sursa unică
  const apply = (j: any) => { setCalc(j.calc); setSched(j.schedule); window.dispatchEvent(new CustomEvent(FEE_EVENT, { detail: { id, calc: j.calc } })); };
  useEffect(() => { fetch(`/api/projects/${id}/contract`).then(async r => { if (r.ok) return r.json(); const j = await r.json().catch(() => null); setLoadErr(j?.error || t('ctr.loadError')); return Promise.reject(); }).then(j => { setD(j.data); apply(j); }).catch(() => setState('error')); }, [id]); // eslint-disable-line
  useEffect(() => { if (!d || !dirty.current) return; setState('saving'); clearTimeout(timer.current);
    timer.current = setTimeout(async () => { const r = await fetch(`/api/projects/${id}/contract`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ ...d, fee: d.fee ?? null }) }).catch(() => null);
      if (r?.ok){ apply(await r.json()); setState('saved'); } else { setState('error'); const j = await r?.json().catch(() => null); say(j?.error || t('ctr.saveError')); } }, 700);
    return () => clearTimeout(timer.current); }, [d]); // eslint-disable-line
  if (!d) return <div className="ctr-panel"><h3>{t('ctr.title')}</h3>{loadErr ? <p className="bad" role="alert">{loadErr}</p> : state === 'error' ? <p className="muted">{t('ctr.saveError')}</p> : <ListSkeleton label={t('print.loading')} rows={2} />}</div>;
  const set = (p: Partial<ContractData>) => { dirty.current = true; setD(x => ({ ...(x ?? DEFAULT_CONTRACT), ...p })); };
  const fee = d.fee, setFee = (p: Partial<NonNullable<ContractData['fee']>>) => set({ fee: { kind: 'fixed', amount: 0, ...fee, ...p } });
  const hint = fee ? FEE_HINTS[fee.kind] : null, pos = hintPosition(fee), num = (v: string) => { const n = Number(v); return Number.isFinite(n) && n >= 0 ? n : null; };
  return (<div className="ctr-panel">
    <h3>{t('ctr.title')}</h3><p className="notice">{t('ctr.notice')}</p>
    <div className="grid2">
      <label className="f"><span>{t('ctr.feeKind')}</span><select value={fee?.kind ?? ''} onChange={e => e.target.value ? setFee({ kind: e.target.value as FeeKind }) : set({ fee: undefined })}>
        <option value="">{t('pcard.noFee')}</option>{FEE_KINDS.map(k => <option key={k} value={k}>{t(`ctr.fee.${k}`)}</option>)}</select></label>
      {fee && <label className="f"><span>{t(`ctr.amount.${fee.kind}`)}</span><input type="number" inputMode="decimal" min={0} step="any" value={fee.amount} onChange={e => { const v = num(e.target.value); if (v != null) setFee({ amount: v }); }} /></label>}</div>
    {fee?.kind === 'hourly' && <label className="f"><span>{t('ctr.hours')}</span><input type="number" inputMode="decimal" min={0} max={CONTRACT_LIMITS.hours} step="any" value={fee.hours ?? ''} onChange={e => setFee({ hours: e.target.value === '' ? undefined : num(e.target.value) ?? 0 })} /></label>}
    {hint && <p className="hint">{t('ctr.hint', { min: hint.min, max: hint.max, unit: hint.unit })} {pos && pos !== 'within' ? t(`ctr.hint.${pos}`) : ''}</p>}
    <label className="chk"><input type="checkbox" checked={d.vatPayer} onChange={e => set({ vatPayer: e.target.checked })} /><span>{t('ctr.vatPayer')}</span></label>
    {calc && <p className="prov">{calc.net == null ? t(`ctr.calc.${calc.reason ?? 'no_fee'}`) : t('ctr.calc', { net: money(calc.net), vat: money(calc.vat), gross: money(calc.gross) })}</p>}
    <fieldset className="chk-group" style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 4 }}><legend className="muted small">{t('ctr.phases')}</legend>
      {PHASE_IDS.map(p => <label key={p} className="chk"><input type="checkbox" checked={d.phases.includes(p)} onChange={e => { const s = new Set(d.phases); e.target.checked ? s.add(p) : s.delete(p); if (s.size) set({ phases: PHASE_IDS.filter(x => s.has(x)) }); }} /><span>{t(`ctr.phase.${p}`)}</span></label>)}</fieldset>
    <div className="grid2">
      <label className="f"><span>{t('ctr.deposit')}</span><input type="number" inputMode="decimal" min={0} max={100} step={1} value={d.depositPct} onChange={e => { const v = num(e.target.value); if (v != null && v <= 100) set({ depositPct: v }); }} /></label>
      <label className="f"><span>{t('ctr.installments')}</span><input type="number" inputMode="numeric" min={0} max={CONTRACT_LIMITS.installments} step={1} value={d.installments} onChange={e => { const v = num(e.target.value); if (v != null && Number.isInteger(v) && v <= CONTRACT_LIMITS.installments) set({ installments: v }); }} /></label></div>
    <div className="grid2">
      <label className="f"><span>{t('ctr.revisions')}</span><input type="number" inputMode="numeric" min={0} max={CONTRACT_LIMITS.revisions} step={1} value={d.revisions} onChange={e => { const v = num(e.target.value); if (v != null && Number.isInteger(v) && v <= CONTRACT_LIMITS.revisions) set({ revisions: v }); }} /></label>
      <label className="f"><span>{t('ctr.validUntil')}</span><input type="date" value={d.validUntil ?? ''} onChange={e => set({ validUntil: e.target.value || undefined })} /></label></div>
    <label className="f"><span>{t('ctr.contractNo')}</span><input value={d.contractNo ?? ''} maxLength={CONTRACT_LIMITS.no} onChange={e => set({ contractNo: e.target.value || undefined })} /></label>
    {sched.length > 0 && <ul className="hint">{sched.map((p, k) => <li key={k}>{t(`ctr.pay.${p.key}`, { n: p.n })}: {money(p.amount)}</li>)}</ul>}
    <small className={`prov ${state === 'error' ? 'err' : ''}`}>{t(`ctr.state.${state}`)}</small>
    <div className="pcard-act"><a className="btn" href={`/p/${id}/contract`} target="_blank" rel="noopener">{t('ctr.openContract')}</a><a className="btn" href={`/p/${id}/offer`} target="_blank" rel="noopener">{t('ctr.openOffer')}</a></div>
  </div>);
}
