'use client';
import { useMemo, useState } from 'react';
import EmptyState from './EmptyState';
import type { Catalog, Snapshot } from '@/core/types';
import { searchCatalog, catalogCurrencies, catalogMarkets } from '@/core/catalog-filter';
import { groups } from '@/core/catalog';
import { formatMoney, formatDimsCm, inputToCm, lengthInputUnit, type Units } from '@/core/format';
import { translator, type Lang } from '@/lib/i18n';
import { planSymbol } from '@/core/plan-symbols';
import { cardNames } from '@/core/furniture-fit';

/** Miniatura cardului: simbolul de plan al piesei (aceleași desene ca planul), cu culoarea variantei; fără dimensiuni, o pastilă de culoare. */
function Thumb({ group, dims, color }: { group: string; dims: { w: number; d: number } | null; color?: string }){
  if (!dims) return <span aria-hidden="true" style={{ width: 40, height: 40, borderRadius: 6, background: color ?? 'var(--rule, #ddd)', flex: '0 0 40px' }} />;
  const w = dims.w / 100, d = dims.d / 100, s = planSymbol(group, w, d), m = Math.max(w, d) * 0.62;
  return <svg width={48} height={48} viewBox={`${-m} ${-m} ${2 * m} ${2 * m}`} aria-hidden="true" style={{ flex: '0 0 48px' }}>
    <path d={s.outline} fill={color ?? 'none'} fillOpacity={color ? 0.35 : 0} stroke="currentColor" strokeWidth={m / 40} /><path d={s.detail} fill="none" stroke="currentColor" strokeWidth={m / 60} /></svg>;
}

// câmpurile de dimensiune sunt în cm (metric) sau inci (imperial); filtrul lucrează în cm
const num = (s: string, units: Units = 'metric') => { const n = inputToCm(s, units); return n != null && n >= 0 ? n : undefined; };
const numPrice = (s: string) => { const n = Number(s); return s.trim() !== '' && Number.isFinite(n) && n >= 0 ? n : undefined; };

export default function CatalogPanel({ catalog, snap, onUse, onAdd, locale = 'ro', units = 'metric' }: { catalog: Catalog; snap: Snapshot; onUse(group: string, variantId: string): void; onAdd?: ((variantId: string) => void) | undefined; locale?: Lang; units?: Units }){
  const t = translator(locale), u = lengthInputUnit(units);
  const [q, setQ] = useState(''), [ret, setRet] = useState<string[]>([]), [mk, setMk] = useState<string[]>([]), [cur, setCur] = useState(''), [minP, setMinP] = useState(''), [maxP, setMaxP] = useState(''), [maxW, setMaxW] = useState(''), [maxD, setMaxD] = useState(''), [group, setGroup] = useState(''), [sort, setSort] = useState<'price' | 'name'>('price');
  const G = useMemo(() => groups(catalog), [catalog]), currs = useMemo(() => catalogCurrencies(catalog), [catalog]), markets = useMemo(() => catalogMarkets(catalog), [catalog]);
  const priceSet = numPrice(minP) !== undefined || numPrice(maxP) !== undefined, needCur = priceSet && currs.length > 1 && !cur;
  const rows = useMemo(() => needCur ? [] : searchCatalog(catalog, { q, retailers: ret, markets: mk, group, sort,
    ...(cur ? { currency: cur } : {}), ...(numPrice(minP) !== undefined ? { minPrice: numPrice(minP)! } : {}), ...(numPrice(maxP) !== undefined ? { maxPrice: numPrice(maxP)! } : {}),
    ...(num(maxW, units) !== undefined ? { maxW: num(maxW, units)! } : {}), ...(num(maxD, units) !== undefined ? { maxD: num(maxD, units)! } : {}) }), [catalog, needCur, q, ret, mk, cur, minP, maxP, maxW, maxD, group, sort, units]);
  const colorOf = useMemo(() => new Map(catalog.variants.map(v => [v.id, (v as any).style?.col as string | undefined])), [catalog]);
  const shown = rows.slice(0, 100), toggle = (list: string[], set: (l: string[]) => void, v: string, on: boolean) => set(on ? [...list, v] : list.filter(x => x !== v));
  return (<>
    <h3>{t('catalog.title')}</h3>
    <label className="f"><span>{t('catalog.search')}</span><input type="search" value={q} placeholder={t('catalog.searchPh')} onChange={e => setQ(e.target.value)} /></label>
    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }} role="group" aria-label={t('catalog.retailer')}>
      {catalog.suppliers.map(s => <label key={s.id} style={{ display: 'flex', gap: 4, alignItems: 'center' }}><input type="checkbox" checked={ret.includes(s.id)} onChange={e => toggle(ret, setRet, s.id, e.target.checked)} />{s.name}</label>)}
    </div>
    {markets.length > 1 && <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }} role="group" aria-label={t('catalog.market')}>
      {markets.map(c => <label key={c} style={{ display: 'flex', gap: 4, alignItems: 'center' }}><input type="checkbox" checked={mk.includes(c)} onChange={e => toggle(mk, setMk, c, e.target.checked)} />{c}</label>)}
    </div>}
    <div className="grid2">
      {currs.length > 1 && <label className="f"><span>{t('catalog.currency')}</span><select value={cur} onChange={e => setCur(e.target.value)}><option value="">{t('catalog.pickCurrency')}</option>{currs.map(c => <option key={c} value={c}>{c}</option>)}</select></label>}
      <label className="f"><span>{t('catalog.minPrice')}</span><input type="number" min={0} value={minP} onChange={e => setMinP(e.target.value)} /></label>
      <label className="f"><span>{t('catalog.maxPrice')}</span><input type="number" min={0} value={maxP} onChange={e => setMaxP(e.target.value)} /></label>
      <label className="f"><span>{t('catalog.maxW', { u })}</span><input type="number" min={0} value={maxW} onChange={e => setMaxW(e.target.value)} /></label>
      <label className="f"><span>{t('catalog.maxD', { u })}</span><input type="number" min={0} value={maxD} onChange={e => setMaxD(e.target.value)} /></label>
      <label className="f"><span>{t('catalog.group')}</span><select value={group} onChange={e => setGroup(e.target.value)}><option value="">{t('catalog.all')}</option>{Object.entries(G).map(([k, g]) => <option key={k} value={k}>{g.label}</option>)}</select></label>
      <label className="f"><span>{t('catalog.sort')}</span><select value={sort} onChange={e => setSort(e.target.value as 'price' | 'name')}><option value="price">{t('catalog.sortPrice')}</option><option value="name">{t('catalog.sortName')}</option></select></label>
    </div>
    {needCur && <div className="issue WARNING">{t('catalog.needCurrency')}</div>}
    <div className="prov" role="status">{rows.length} {t('catalog.results')}{rows.length > shown.length ? ` (${t('catalog.firstN')} ${shown.length})` : ''}</div>
    {rows.length === 0 && !needCur && <EmptyState title={t('empty.catalog.title')} text={t('empty.catalog.text')} action={{ label: t('empty.catalog.action'), onClick: () => { setQ(''); setRet([]); setMk([]); setCur(''); setMinP(''); setMaxP(''); setMaxW(''); setMaxD(''); setGroup(''); } }} />}
    {shown.map(r => { const used = snap.selections[r.group] === r.variantId;
      const nm = cardNames(r.productName, r.variantName);
      return <div key={r.variantId} className="var" style={{ display: 'grid', gap: 4, minWidth: 0 }}>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', minWidth: 0 }}><Thumb group={r.group} dims={r.dims} color={colorOf.get(r.variantId)} />
          <span style={{ display: 'grid', minWidth: 0 }}><strong>{nm.title}</strong><small>{[nm.sub, r.retailer || t('catalog.noOffer')].filter(Boolean).join(' · ')}</small></span></div>
        <span className="mono">{r.price !== null && r.currency ? formatMoney(r.price, r.currency, locale) : t('catalog.unknownPrice')}</span>
        <small>{r.dims ? formatDimsCm(r.dims, units, locale) : t('catalog.unknownDims')}{r.confidence === 'MEDIUM' ? ` ${t('catalog.approx')}` : ''}</small>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          <button className="btn" aria-pressed={used} disabled={used} onClick={() => onUse(r.group, r.variantId)}>{t('catalog.useFor')} {r.groupLabel}</button>
          {onAdd && <button className="btn" onClick={() => onAdd(r.variantId)}>{t('catalog.addToRoom')}</button>}
          {r.offerId && <a className="btn" href={`/go/o/${r.offerId}`} target="_blank" rel="noopener">{t('catalog.viewStore')}</a>}
        </div>
      </div>; })}
  </>);
}
