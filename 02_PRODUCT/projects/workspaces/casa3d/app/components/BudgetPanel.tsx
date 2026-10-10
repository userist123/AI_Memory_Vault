'use client';
import { useMemo, useState } from 'react';
import type { Catalog, Snapshot, MaterialsCatalog, BudgetSettings } from '@/core/types';
import { computeBudget, type BoqItem } from '@/core/boq';
import { relFor, freshness } from '@/core/outbound';
import { WASTE, PAINT_COATS, BATH_TILE_HEIGHT, BACKSPLASH_HEIGHT, LIGHTS_EXTRA_PER_M2 } from '@/core/rules.boq';

const lei = (v: number) => v.toLocaleString('ro-RO', { minimumFractionDigits: 0, maximumFractionDigits: 0 }) + ' lei';
const num = (v: number, d = 2) => v.toLocaleString('ro-RO', { maximumFractionDigits: d });
const CAT_LABEL: Record<string, string> = { furniture: 'Mobilier', finishes: 'Finisaje', lighting: 'Iluminat', appliances: 'Electrocasnice', sanitary: 'Sanitare' };
const UNIT: Record<string, string> = { m2: 'm²', ml: 'ml', L: 'L', kg: 'kg', buc: 'buc' };
const CONF: Record<string, string> = { HIGH: 'încredere mare', MEDIUM: 'încredere medie', LOW: 'orientativ', UNKNOWN: 'necunoscut' };

export type Outbound = { targets: Record<string, 'direct' | 'affiliate'>; disclosure: boolean };
export function linkFor(i: BoqItem, out: Outbound | null){ const mat = i.category === 'finishes' || i.category === 'lighting', key = mat ? `m:${i.refId}` : `o:offer-${i.refId}`, t = out?.targets[key];
  return t ? { href: mat ? `/go/m/${i.refId}` : `/go/o/offer-${i.refId}`, rel: relFor(t), affiliate: t === 'affiliate' } : null; }
export function Fresh({ at }: { at: string | null }){ const f = freshness(at); return f.stale ? <span className="stale">{f.days == null ? 'dată necunoscută' : `preț verificat acum ${f.days} zile, de reverificat`}</span> : null; }
export default function BudgetPanel({ snap, catalog, mc, out, onBudget }: { snap: Snapshot; catalog: Catalog; mc: MaterialsCatalog; out: Outbound | null; onBudget(patch: Partial<BudgetSettings>): void }){
  const b = useMemo(() => computeBudget(snap, catalog, mc), [snap, catalog, mc]);
  const [open, setOpen] = useState<'mat' | 'lab' | 'mob' | null>(null), s = b.settings, c = b.chosen;
  const roomName = (id: string | null) => id ? snap.floor.rooms.find(r => r.id === id)?.name || '' : 'Toată casa';
  const numIn = (v: string) => v.trim() === '' ? null : Math.max(0, Number(v.replace(',', '.')) || 0);
  const parts = [...Object.entries(b.categories).map(([k, v]) => [CAT_LABEL[k], v] as const), ['Manoperă', c.labor] as const, ...b.extras.map(e => [e.label, e.amount || 0] as const), [`Rezervă ${s.contingencyPct}%`, c.contingency] as const].filter(([, v]) => v > 0);
  function csv(){
    const rows = [['Categorie', 'Cameră', 'Articol', 'Cantitate netă', 'UM', 'Pierderi %', 'Cantitate comandată', 'Ambalaje', 'Preț unitar (lei)', 'Total (lei)', 'Furnizor', 'Sursă', 'Verificat', 'Încredere'],
      ...b.items.map(i => [CAT_LABEL[i.category], roomName(i.roomId), i.label, i.netQty, i.unit, Math.round(i.wastePct * 100), i.orderedQty, i.packs != null ? `${i.packs} × ${i.packLabel}` : '', i.unitPrice ?? 'NECUNOSCUT', i.total ?? 'NECUNOSCUT', i.supplier, i.sourceUrl ?? '', i.verifiedAt ?? '', i.confidence]),
      ...b.labor.map(l => ['Manoperă', roomName(l.roomId), l.label, l.qty, l.unit, 0, l.qty, '', '', l.expected, `interval ${l.low}–${l.high}`, l.sources.map(x => x.url).join(' '), mc.verifiedAt, l.confidence])];
    const text = '\ufeff' + rows.map(r => r.map(v => `"${String(v).replace(/"/g, '""')}"`).join(';')).join('\n');
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' })); a.download = `${snap.name.replace(/[^\w\- ]+/g, '').trim() || 'proiect'}-buget.csv`; a.click(); URL.revokeObjectURL(a.href);
  }
  return (<div className="budget">
    <h3>Buget</h3>
    {out?.disclosure && <div className="issue WARNING">Unele linkuri sunt de afiliere: putem primi un comision, fără cost suplimentar pentru tine. <a href="/despre-linkuri" target="_blank">Detalii</a></div>}
    <div className="bigtotal"><span>Total estimat</span><b>{lei(c.total)}</b><small>interval {lei(b.scenarios.low.total)} – {lei(b.scenarios.high.total)} (după manoperă) · din care TVA 21%: {lei(c.vat)}</small></div>
    {b.target != null && <div className={`issue ${b.status === 'over' ? 'ERROR' : 'PASS'}`}>{b.status === 'over' ? `Depășești bugetul țintă cu ${lei(-b.diff!)}.` : `Rămân ${lei(b.diff!)} sub bugetul țintă.`}</div>}
    <div className="bars">{parts.map(([l, v]) => <div key={l} className="bar"><span>{l}</span><i style={{ width: `${Math.max(2, v / c.total * 100)}%` }} /><b className="mono">{lei(v)}</b></div>)}</div>
    {(b.unknownLines.length > 0 || b.unknownItems.length > 0) && <div className="issue WARNING">Neincluse în total (preț necunoscut): {[...b.unknownLines.map(u => u.label), ...b.unknownItems].join(', ')}. Introdu valorile din ofertă mai jos.</div>}
    <h4>Setări</h4>
    <div className="grid2">
      <label className="f"><span>Buget țintă (lei)</span><input type="number" min={0} value={s.target ?? ''} placeholder="fără" onChange={e => onBudget({ target: numIn(e.target.value) })} /></label>
      <label className="f"><span>Rezervă (%)</span><input type="number" min={0} max={100} value={s.contingencyPct} onChange={e => onBudget({ contingencyPct: Math.min(100, numIn(e.target.value) ?? 0) })} /></label>
      <label className="f"><span>Manoperă</span><select value={s.includeLabor ? s.laborScenario : 'none'} onChange={e => e.target.value === 'none' ? onBudget({ includeLabor: false }) : onBudget({ includeLabor: true, laborScenario: e.target.value as any })}>
        <option value="low">Minimă</option><option value="expected">Medie</option><option value="high">Maximă</option><option value="none">O fac eu (fără manoperă)</option></select></label>
      <label className="f"><span>Proiectare (lei)</span><input type="number" min={0} value={s.design || ''} placeholder="0" onChange={e => onBudget({ design: numIn(e.target.value) ?? 0 })} /></label>
      <label className="f"><span>Transport Dedeman (lei)</span><input type="number" min={0} value={s.deliveryDedeman ?? ''} placeholder="necunoscut" onChange={e => onBudget({ deliveryDedeman: numIn(e.target.value) })} /></label>
      <label className="f"><span>Montaj mobilier (lei)</span><input type="number" min={0} value={s.furnitureAssembly ?? ''} placeholder="necunoscut" onChange={e => onBudget({ furnitureAssembly: numIn(e.target.value) })} /></label>
    </div>
    <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={s.deliveryIkea} onChange={e => onBudget({ deliveryIkea: e.target.checked })} /> Livrare IKEA la domiciliu</label>
    <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={s.kitchenAssembly} onChange={e => onBudget({ kitchenAssembly: e.target.checked })} /> Montaj bucătărie prin IKEA</label>

    <button className="acc" onClick={() => setOpen(open === 'mat' ? null : 'mat')} aria-expanded={open === 'mat'}>Cantități materiale ({b.items.filter(i => i.category === 'finishes' || i.category === 'lighting').length})</button>
    {open === 'mat' && <div className="tbl">{b.items.filter(i => i.category === 'finishes' || i.category === 'lighting').map(i => <div key={i.key} className="trow">
      <ItemLink i={i} out={out} /><b className="mono">{i.total != null ? lei(i.total) : '—'}</b>
      <small>{num(i.netQty)} {UNIT[i.unit]} net{i.wastePct ? ` + ${Math.round(i.wastePct * 100)}% = ` : ' · '}{i.packs != null ? `${i.packs} × ${i.packLabel} (${num(i.orderedQty)} ${UNIT[i.unit]})` : `${num(i.orderedQty)} ${UNIT[i.unit]}`} · {num(i.unitPrice || 0)} lei/{UNIT[i.unit]} · {i.supplier}, {i.verifiedAt}, {CONF[i.confidence]} <Fresh at={i.verifiedAt} /></small></div>)}</div>}
    <button className="acc" onClick={() => setOpen(open === 'lab' ? null : 'lab')} aria-expanded={open === 'lab'}>Manoperă ({b.labor.length} lucrări, orientativ)</button>
    {open === 'lab' && <div className="tbl">{b.labor.map(l => <div key={l.key} className="trow"><span>{l.label} · {roomName(l.roomId)}</span><b className="mono">{lei(l.expected)}</b>
      <small>{num(l.qty)} {UNIT[l.unit]} · interval {lei(l.low)} – {lei(l.high)} · surse: {l.sources.map((x, k) => <a key={k} href={x.url} target="_blank" rel="noopener noreferrer">{x.name}</a>).reduce((a: any, el, k) => k ? [...a, '; ', el] : [el], [])}</small></div>)}</div>}
    <button className="acc" onClick={() => setOpen(open === 'mob' ? null : 'mob')} aria-expanded={open === 'mob'}>Mobilier, sanitare, electrocasnice ({b.items.filter(i => ['furniture', 'sanitary', 'appliances'].includes(i.category)).length})</button>
    {open === 'mob' && <div className="tbl">{b.items.filter(i => ['furniture', 'sanitary', 'appliances'].includes(i.category)).map(i => <div key={i.key} className="trow">
      <ItemLink i={i} out={out} /><b className="mono">{i.total != null ? lei(i.total) : 'necunoscut'}</b><small>{roomName(i.roomId)} · {i.supplier}, {i.verifiedAt}, {CONF[i.confidence]} <Fresh at={i.verifiedAt} /></small></div>)}</div>}
    <button className="btn" onClick={csv}>Descarcă tabelul (CSV pentru Excel)</button>
    <p className="prov">Linkurile duc la magazine printr-un redirect al aplicației; în testare nu primim comision. <a href="/despre-linkuri" target="_blank">Cum funcționează</a>.</p>
    <p className="prov">Ipoteze de calcul: pierderi parchet {WASTE.parquet * 100}%, gresie/faianță {WASTE.floor_tile * 100}%, plintă {WASTE.baseboard * 100}%, vopsea {WASTE.paint * 100}%; vopsea în {PAINT_COATS} straturi pe pereți (fără uși, ferestre și zone placate) și tavan; faianță în baie până la {BATH_TILE_HEIGHT} m, în bucătărie {BACKSPLASH_HEIGHT * 100} cm deasupra blatului; un corp de iluminat pe cameră plus unul la fiecare {LIGHTS_EXTRA_PER_M2} m². Prețurile magazinelor includ TVA. Manopera e orientativă, din ghiduri de preț publice; cere oferte reale înainte de a decide.</p>
  </div>);
}

function ItemLink({ i, out }: { i: BoqItem; out: Outbound | null }){ const l = linkFor(i, out);
  return l ? <a href={l.href} target="_blank" rel={l.rel}>{i.label}{l.affiliate ? ' (link de afiliere)' : ''}</a> : <span>{i.label} <span className="stale">fără ofertă</span></span>; }
