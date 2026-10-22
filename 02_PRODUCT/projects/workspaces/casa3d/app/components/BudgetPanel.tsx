'use client';
import { csvCell } from '@/lib/finish-schedule-text';
import { finishSchedule } from '@/core/finish-schedule';
import { downloadSchedule } from './FinishSchedule';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Snapshot, MaterialsCatalog, BudgetSettings } from '@/core/types';
import { computeBudget, type BoqItem } from '@/core/boq';
import { relFor, freshness } from '@/core/outbound';
import { WASTE, PAINT_COATS, BATH_TILE_HEIGHT, BACKSPLASH_HEIGHT, LIGHTS_EXTRA_PER_M2 } from '@/core/rules.boq';
import { usePrefs } from '@/lib/prefs';
import { formatMoney, formatLength, formatArea, catalogCurrency } from '@/core/format';
import { formatNumber } from '@/lib/i18n';
import { formatNumber as fmtNum, qtyDecimals } from '@/core/format';
import { commercialSources, profitReport, projectChain, type CustomSource, type Terms } from '@/core/commercial';
import { ChainTable, FEE_EVENT } from './ChainTable';
import { excludedGroups } from '@/core/budget-excluded';

/** Ce vine de pe server pentru lanțul totalului: termenii comerciali (adaos, discount) și onorariul contractului (sursa unică). */
export type CommercialInputs = { data: { defaults: Terms; suppliers: Record<string, Terms> }; fee: { kind: string | null; net: number | null; vat: number; gross: number | null; vatPayer: boolean; reason: string | null } };

// Cantitățile de materiale rămân în unitățile de comandă ale magazinului (m², ml, L, kg, buc); doar banii, lungimile și suprafețele de proiect urmează limba și unitățile.
const pc = (x: number) => Math.round(x * 1000) / 10;
const CAT_LABEL: Record<string, string> = { furniture: 'budget.cat.furniture', finishes: 'budget.cat.finishes', lighting: 'budget.cat.lighting', appliances: 'budget.cat.appliances', sanitary: 'budget.cat.sanitary' , textiles: 'budget.cat.textiles', custom: 'cprod.budget' };
const UNIT: Record<string, string> = { m2: 'm²', ml: 'budget.unit.ml', L: 'L', kg: 'kg', buc: 'budget.unit.buc' };
const CONF: Record<string, string> = { HIGH: 'budget.conf.HIGH', MEDIUM: 'budget.conf.MEDIUM', LOW: 'budget.conf.LOW', UNKNOWN: 'budget.conf.UNKNOWN' };

export type Outbound = { targets: Record<string, 'direct' | 'affiliate'>; disclosure: boolean };
export function linkFor(i: BoqItem, out: Outbound | null){ const mat = i.category === 'finishes' || i.category === 'lighting' || i.category === 'textiles', key = mat ? `m:${i.refId}` : `o:offer-${i.refId}`, t = out?.targets[key];
  return t ? { href: mat ? `/go/m/${i.refId}` : `/go/o/offer-${i.refId}`, rel: relFor(t), affiliate: t === 'affiliate' } : null; }
export function Fresh({ at }: { at: string | null }){ const { t } = usePrefs(), f = freshness(at); return f.stale ? <span className="stale">{f.days == null ? t('budget.dateUnknown') : t('budget.staleDays', { days: f.days })}</span> : null; }
export default function BudgetPanel({ snap, catalog, mc, out, onBudget, custom, com, projectId, onFee }: { snap: Snapshot; catalog: Catalog; mc: MaterialsCatalog; out: Outbound | null; onBudget(patch: Partial<BudgetSettings>): void; custom?: CustomSource[];
  com?: CommercialInputs | null; projectId?: string; onFee?(): void }){
  const { t, lang, units } = usePrefs(), cur = catalogCurrency(catalog), lei = (v: number) => formatMoney(v, cur, lang), num = (v: number, d = 2) => formatNumber(v, lang, d), qn = (v: number, u: string) => fmtNum(v, lang, { decimals: qtyDecimals(u, v) }), unit = (u: string) => UNIT[u]?.startsWith('budget.') ? t(UNIT[u]!) : UNIT[u] ?? u;
  const b = useMemo(() => computeBudget(snap, catalog, mc, custom), [snap, catalog, mc, custom]);
  const [open, setOpen] = useState<'mat' | 'lab' | 'mob' | null>(null), s = b.settings, c = b.chosen;
  const roomName = (id: string | null) => id ? snap.floor.rooms.find(r => r.id === id)?.name || '' : t('budget.wholeHouse');
  const numIn = (v: string) => v.trim() === '' ? null : Math.max(0, Number(v.replace(',', '.')) || 0);
  // lanțul canonic (core/commercial.ts): aceleași funcții și aceleași rânduri ca panoul comercial și proforma
  const chain = useMemo(() => { if (!com) return null; const src = commercialSources(b, custom ?? []);
    return projectChain(profitReport(com.data, src.items, src.labor, com.fee.net, com.fee.vat, src.extras), s.contingencyPct); }, [b, com, custom, s.contingencyPct]);
  const shown = chain ? chain.total : c.total, shift = shown - c.total, diff = b.target != null ? Math.round((b.target - shown) * 100) / 100 : null;
  const parts = [...Object.entries(b.categories).map(([k, v]) => [t(CAT_LABEL[k]!), v] as const), ...(chain && chain.markup ? [[t('chain.markup'), chain.markup] as const] : []), [t('budget.labor'), c.labor] as const, ...b.extras.map(e => [e.label, e.amount || 0] as const),
    ...(chain && chain.feeGross ? [[t('com.cat.fee'), chain.feeGross] as const] : []), [t('budget.contingencyPct', { pct: s.contingencyPct }), chain ? chain.contingency : c.contingency] as const].filter(([, v]) => v > 0);
  const excluded = useMemo(() => excludedGroups(b.unknownLines.map(u => u.label), b.unknownItems), [b]);
  const maxPart = Math.max(1, ...parts.map(([, v]) => v));
  function csv(){
    const rows = [[t('budget.csv.category'), t('budget.csv.room'), t('budget.csv.item'), t('budget.csv.netQty'), t('budget.csv.unit'), t('budget.csv.waste'), t('budget.csv.orderedQty'), t('budget.csv.packs'), t('budget.csv.unitPrice', { cur }), t('budget.csv.total', { cur }), t('budget.csv.supplier'), t('budget.csv.source'), t('budget.csv.verified'), t('budget.csv.confidence')],
      ...b.items.map(i => [t(CAT_LABEL[i.category]!), roomName(i.roomId), i.label, i.netQty, i.unit, Math.round(i.wastePct * 100), i.orderedQty, i.packs != null ? `${i.packs} × ${i.packLabel}` : '', i.unitPrice ?? t('budget.csv.unknown'), i.total ?? t('budget.csv.unknown'), i.supplier, i.sourceUrl ?? '', i.verifiedAt ?? '', i.confidence]),
      ...b.labor.map(l => [t('budget.labor'), roomName(l.roomId), l.label, l.qty, l.unit, 0, l.qty, '', '', l.expected, t('budget.csv.range', { low: l.low, high: l.high }), l.sources.map(x => x.url).join(' '), mc.verifiedAt, l.confidence])];
    const text = '\ufeff' + rows.map(r => r.map(csvCell).join(';')).join('\n');
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' })); a.download = `${snap.name.replace(/[^\w\- ]+/g, '').trim() || t('budget.csv.fileFallback')}-${t('budget.csv.fileSuffix')}.csv`; a.click(); URL.revokeObjectURL(a.href);
  }
  return (<div className="budget">
    <h3>{t('budget.estimate')}</h3>
    {out?.disclosure && <div className="issue WARNING">{t('budget.affiliateNote')} <a href="/despre-linkuri" target="_blank">{t('budget.details')}</a></div>}
    <div className="bigtotal"><span>{t('chain.total')}</span><b>{lei(shown)}</b><small>{t('budget.rangeVat', { low: lei(b.scenarios.low.total + shift), high: lei(b.scenarios.high.total + shift), vat: lei(c.vat) })}</small></div>
    {diff != null && <div className={`issue ${diff < 0 ? 'ERROR' : 'PASS'}`}>{diff < 0 ? t('budget.overTarget', { amount: lei(-diff) }) : t('budget.underTarget', { amount: lei(diff) })}</div>}
    <div className="bars">{parts.map(([l, v]) => <div key={l} className="bar"><span>{l}</span><b className="mono">{lei(v)}</b><em className="track"><i style={{ width: `${Math.max(2, v / maxPart * 100)}%` }} /></em></div>)}</div>
    {chain && <ChainTable chain={chain} money={lei} />}
    {excluded.length > 0 && <ExcludedList groups={excluded} />}
    <h4>{t('budget.settings')}</h4>
    <div className="grid2">
      <label className="f"><span>{t('budget.target', { cur })}</span><input type="number" min={0} value={s.target ?? ''} placeholder={t('budget.none')} onChange={e => onBudget({ target: numIn(e.target.value) })} /></label>
      <label className="f"><span>{t('budget.contingency')}</span><input type="number" min={0} max={100} value={s.contingencyPct} onChange={e => onBudget({ contingencyPct: Math.min(100, numIn(e.target.value) ?? 0) })} /></label>
      <label className="f"><span>{t('budget.labor')}</span><select value={s.includeLabor ? s.laborScenario : 'none'} onChange={e => e.target.value === 'none' ? onBudget({ includeLabor: false }) : onBudget({ includeLabor: true, laborScenario: e.target.value as any })}>
        <option value="low">{t('budget.laborLow')}</option><option value="expected">{t('budget.laborExpected')}</option><option value="high">{t('budget.laborHigh')}</option><option value="none">{t('budget.laborNone')}</option></select></label>
      {projectId && <FeeField id={projectId} com={com ?? null} cur={cur} money={lei} onSaved={onFee} />}
      <label className="f"><span>{t('budget.deliveryDedeman', { cur })}</span><input type="number" min={0} value={s.deliveryDedeman ?? ''} placeholder={t('common.unknown')} onChange={e => onBudget({ deliveryDedeman: numIn(e.target.value) })} /></label>
      <label className="f"><span>{t('budget.assembly', { cur })}</span><input type="number" min={0} value={s.furnitureAssembly ?? ''} placeholder={t('common.unknown')} onChange={e => onBudget({ furnitureAssembly: numIn(e.target.value) })} /></label>
    </div>
    <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={s.deliveryIkea} onChange={e => onBudget({ deliveryIkea: e.target.checked })} /> {t('budget.deliveryIkea')}</label>
    <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={s.kitchenAssembly} onChange={e => onBudget({ kitchenAssembly: e.target.checked })} /> {t('budget.kitchenAssembly')}</label>

    <button className="acc" onClick={() => setOpen(open === 'mat' ? null : 'mat')} aria-expanded={open === 'mat'}>{t('budget.materials', { n: b.items.filter(i => i.category === 'finishes' || i.category === 'lighting' || i.category === 'textiles').length })}</button>
    {open === 'mat' && <div className="tbl">{b.items.filter(i => i.category === 'finishes' || i.category === 'lighting' || i.category === 'textiles').map(i => <div key={i.key} className="trow">
      <ItemLink i={i} out={out} /><b className="mono">{i.total != null ? lei(i.total) : '—'}</b>
      <small>{qn(i.netQty, i.unit)} {unit(i.unit)} {t('budget.net')}{i.wastePct ? ` + ${Math.round(i.wastePct * 100)}% = ` : ' · '}{i.packs != null ? `${i.packs} × ${i.packLabel} (${qn(i.orderedQty, i.unit)} ${unit(i.unit)})` : `${qn(i.orderedQty, i.unit)} ${unit(i.unit)}`} · {formatMoney(i.unitPrice || 0, cur, lang)}/{unit(i.unit)} · {i.supplier}, {i.verifiedAt}, {t(CONF[i.confidence]!)} <Fresh at={i.verifiedAt} /></small></div>)}</div>}
    <button className="acc" onClick={() => setOpen(open === 'lab' ? null : 'lab')} aria-expanded={open === 'lab'}>{t('budget.laborSection', { n: b.labor.length })}</button>
    {open === 'lab' && <div className="tbl">{b.labor.map(l => <div key={l.key} className="trow"><span>{l.label} · {roomName(l.roomId)}</span><b className="mono">{lei(l.expected)}</b>
      <small>{qn(l.qty, l.unit)} {unit(l.unit)} · {t('budget.range', { low: lei(l.low), high: lei(l.high) })} · {t('budget.sources')}: {l.sources.map((x, k) => <a key={k} href={x.url} target="_blank" rel="noopener noreferrer">{x.name}</a>).reduce((a: any, el, k) => k ? [...a, '; ', el] : [el], [])}</small></div>)}</div>}
    <button className="acc" onClick={() => setOpen(open === 'mob' ? null : 'mob')} aria-expanded={open === 'mob'}>{t('budget.furnitureSection', { n: b.items.filter(i => ['furniture', 'sanitary', 'appliances'].includes(i.category)).length })}</button>
    {open === 'mob' && <div className="tbl">{b.items.filter(i => ['furniture', 'sanitary', 'appliances'].includes(i.category)).map(i => <div key={i.key} className="trow">
      <ItemLink i={i} out={out} /><b className="mono">{i.total != null ? lei(i.total) : t('common.unknown')}</b><small>{roomName(i.roomId)} · {i.supplier}, {i.verifiedAt}, {t(CONF[i.confidence]!)} <Fresh at={i.verifiedAt} /></small></div>)}</div>}
    <button className="btn" onClick={csv}>{t('budget.downloadCsv')}</button>
    <button className="btn" onClick={() => downloadSchedule(finishSchedule(snap, catalog, mc), t, cur, snap.name, i => i === 0 ? t('level.ground') : t('level.n', { n: i }))}>{t('sched.download')}</button>
    <p className="prov">{t('budget.linksNote')} <a href="/despre-linkuri" target="_blank">{t('budget.howItWorks')}</a>.</p>
    <p className="prov">{t('budget.assumptions', { parquet: pc(WASTE.parquet), tile: pc(WASTE.floor_tile), baseboard: pc(WASTE.baseboard), paint: pc(WASTE.paint), coats: PAINT_COATS, bath: formatLength(BATH_TILE_HEIGHT, units, lang), splash: formatLength(BACKSPLASH_HEIGHT, units, lang), per: formatArea(LIGHTS_EXTRA_PER_M2, units, lang) })}</p>
  </div>);
}

function ItemLink({ i, out }: { i: BoqItem; out: Outbound | null }){ const { t } = usePrefs(), l = linkFor(i, out);
  return l ? <a href={l.href} target="_blank" rel={l.rel}>{i.label}{l.affiliate ? ` ${t('budget.affiliateLink')}` : ''}</a> : <span>{i.label} <span className="stale">{t('budget.noOffer')}</span></span>; }

/** Onorariul în buget: editează ACEEAȘI valoare ca „Contract și ofertă” (contractul e sursa unică). Doar suma fixă se scrie de aici. */
function FeeField({ id, com, cur, money, onSaved }: { id: string; com: CommercialInputs | null; cur: string; money(v: number): string; onSaved?(): void }){
  const { t } = usePrefs(), fee = com?.fee, editable = !!com && (fee?.kind == null || fee.kind === 'fixed');
  const [v, setV] = useState(''), [state, setState] = useState<'saved' | 'saving' | 'error'>('saved'), timer = useRef<any>(null), typing = useRef(false);
  useEffect(() => { if (!typing.current) setV(fee?.kind === 'fixed' && fee.net ? String(fee.net) : ''); }, [fee?.kind, fee?.net]);
  function change(x: string){ setV(x); typing.current = true; setState('saving'); clearTimeout(timer.current);
    timer.current = setTimeout(async () => { const n = Number(x.replace(',', '.')), body = { fee: x.trim() === '' || !(n > 0) ? null : { kind: 'fixed', amount: n } };
      const r = await fetch(`/api/projects/${id}/fee`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }).catch(() => null);
      typing.current = false; if (!r?.ok){ setState('error'); return; } setState('saved'); window.dispatchEvent(new CustomEvent(FEE_EVENT, { detail: { id } })); onSaved?.(); }, 700); }
  useEffect(() => () => clearTimeout(timer.current), []);
  if (!com) return <p className="prov">{t('budget.feeUnavailable')}</p>;
  if (!editable) return <p className="prov">{t('budget.feeFromContract', { v: fee?.gross != null ? money(fee.gross) : t('common.unknown') })}</p>;
  return (<label className="f"><span>{t('budget.fee', { cur })}</span><input type="number" min={0} inputMode="decimal" value={v} placeholder="0" onChange={e => change(e.target.value)} />
    <small className={`prov ${state === 'error' ? 'err' : ''}`}>{state === 'saved' ? t('budget.feeShared') : t(`com.state.${state}`)}{fee?.vatPayer && fee.vat ? ` · ${t('budget.feeVat', { v: money(fee.vat) })}` : ''}</small></label>);
}

/** „Neincluse în total”: o listă curată, fără dubluri, grupată pe tip; culorile apar ca o pastilă cu numele lor, nu ca #hex. */
function ExcludedList({ groups }: { groups: ReturnType<typeof excludedGroups> }){
  const { t } = usePrefs();
  return (<div className="issue WARNING excluded"><b>{t('budget.excludedTitle')}</b>
    {groups.map(g => <div key={g.kind}><small>{t(`budget.excl.${g.kind}`)}</small><ul>{g.items.map(i => <li key={i.key}>
      {i.color && <i className="chip" style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 5, background: i.color, border: '1px solid var(--rule, #ccc)', marginRight: 4, verticalAlign: 'middle' }} />}
      {i.label}{i.count > 1 ? ` ×${i.count}` : ''}{i.note ? <small> · {t(`budget.exclNote.${i.note}`)}</small> : null}</li>)}</ul></div>)}
    <small>{t('budget.excludedHelp')}</small></div>);
}
