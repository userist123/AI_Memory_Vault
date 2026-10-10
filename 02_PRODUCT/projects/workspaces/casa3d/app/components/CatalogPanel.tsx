'use client';
import { useMemo, useState } from 'react';
import type { Catalog, Snapshot } from '@/core/types';
import { searchCatalog, catalogCurrencies, catalogMarkets } from '@/core/catalog-filter';
import { groups } from '@/core/catalog';
import { formatMoney, formatLength, type Locale } from '@/core/format';

// Toate textele vizibile, într-un singur loc, pentru stratul i18n.
const T = {
  title: 'Catalog', search: 'Caută', searchPh: 'ex. canapea, noptieră, KIVIK', retailer: 'Magazin', market: 'Piață', currency: 'Monedă', pickCurrency: 'alege',
  needCurrency: 'Catalogul are mai multe monede: alege moneda pentru a filtra după preț.', minPrice: 'Preț min', maxPrice: 'Preț max', maxW: 'Lățime max (cm)', maxD: 'Adâncime max (cm)',
  group: 'Grupă', all: 'Toate', sort: 'Sortare', sortPrice: 'Preț crescător', sortName: 'Nume', results: 'rezultate', firstN: 'primele', noOffer: 'fără ofertă',
  unknownPrice: 'preț necunoscut', unknownDims: 'dimensiuni necunoscute', approx: '(aprox.)', useFor: 'Folosește pentru', addToRoom: 'Adaugă în camera selectată', viewStore: 'Vezi la magazin',
};
const num = (s: string) => { const n = Number(s); return s.trim() !== '' && Number.isFinite(n) && n >= 0 ? n : undefined; };

export default function CatalogPanel({ catalog, snap, onUse, onAdd, locale = 'ro' }: { catalog: Catalog; snap: Snapshot; onUse(group: string, variantId: string): void; onAdd?: ((variantId: string) => void) | undefined; locale?: Locale }){
  const [q, setQ] = useState(''), [ret, setRet] = useState<string[]>([]), [mk, setMk] = useState<string[]>([]), [cur, setCur] = useState(''), [minP, setMinP] = useState(''), [maxP, setMaxP] = useState(''), [maxW, setMaxW] = useState(''), [maxD, setMaxD] = useState(''), [group, setGroup] = useState(''), [sort, setSort] = useState<'price' | 'name'>('price');
  const G = useMemo(() => groups(catalog), [catalog]), currs = useMemo(() => catalogCurrencies(catalog), [catalog]), markets = useMemo(() => catalogMarkets(catalog), [catalog]);
  const priceSet = num(minP) !== undefined || num(maxP) !== undefined, needCur = priceSet && currs.length > 1 && !cur;
  const rows = useMemo(() => needCur ? [] : searchCatalog(catalog, { q, retailers: ret, markets: mk, group, sort,
    ...(cur ? { currency: cur } : {}), ...(num(minP) !== undefined ? { minPrice: num(minP)! } : {}), ...(num(maxP) !== undefined ? { maxPrice: num(maxP)! } : {}),
    ...(num(maxW) !== undefined ? { maxW: num(maxW)! } : {}), ...(num(maxD) !== undefined ? { maxD: num(maxD)! } : {}) }), [catalog, needCur, q, ret, mk, cur, minP, maxP, maxW, maxD, group, sort]);
  const shown = rows.slice(0, 100), toggle = (list: string[], set: (l: string[]) => void, v: string, on: boolean) => set(on ? [...list, v] : list.filter(x => x !== v));
  return (<>
    <h3>{T.title}</h3>
    <label className="f"><span>{T.search}</span><input type="search" value={q} placeholder={T.searchPh} onChange={e => setQ(e.target.value)} /></label>
    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }} role="group" aria-label={T.retailer}>
      {catalog.suppliers.map(s => <label key={s.id} style={{ display: 'flex', gap: 4, alignItems: 'center' }}><input type="checkbox" checked={ret.includes(s.id)} onChange={e => toggle(ret, setRet, s.id, e.target.checked)} />{s.name}</label>)}
    </div>
    {markets.length > 1 && <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }} role="group" aria-label={T.market}>
      {markets.map(c => <label key={c} style={{ display: 'flex', gap: 4, alignItems: 'center' }}><input type="checkbox" checked={mk.includes(c)} onChange={e => toggle(mk, setMk, c, e.target.checked)} />{c}</label>)}
    </div>}
    <div className="grid2">
      {currs.length > 1 && <label className="f"><span>{T.currency}</span><select value={cur} onChange={e => setCur(e.target.value)}><option value="">{T.pickCurrency}</option>{currs.map(c => <option key={c} value={c}>{c}</option>)}</select></label>}
      <label className="f"><span>{T.minPrice}</span><input type="number" min={0} value={minP} onChange={e => setMinP(e.target.value)} /></label>
      <label className="f"><span>{T.maxPrice}</span><input type="number" min={0} value={maxP} onChange={e => setMaxP(e.target.value)} /></label>
      <label className="f"><span>{T.maxW}</span><input type="number" min={0} value={maxW} onChange={e => setMaxW(e.target.value)} /></label>
      <label className="f"><span>{T.maxD}</span><input type="number" min={0} value={maxD} onChange={e => setMaxD(e.target.value)} /></label>
      <label className="f"><span>{T.group}</span><select value={group} onChange={e => setGroup(e.target.value)}><option value="">{T.all}</option>{Object.entries(G).map(([k, g]) => <option key={k} value={k}>{g.label}</option>)}</select></label>
      <label className="f"><span>{T.sort}</span><select value={sort} onChange={e => setSort(e.target.value as 'price' | 'name')}><option value="price">{T.sortPrice}</option><option value="name">{T.sortName}</option></select></label>
    </div>
    {needCur && <div className="issue WARNING">{T.needCurrency}</div>}
    <div className="prov" role="status">{rows.length} {T.results}{rows.length > shown.length ? ` (${T.firstN} ${shown.length})` : ''}</div>
    {shown.map(r => { const used = snap.selections[r.group] === r.variantId;
      return <div key={r.variantId} className="var" style={{ display: 'grid', gap: 4 }}>
        <strong>{r.productName}</strong>
        <span>{r.variantName} · {r.retailer || T.noOffer}</span>
        <span className="mono">{r.price !== null && r.currency ? formatMoney(r.price, r.currency, locale) : T.unknownPrice}</span>
        <small>{r.dims ? [r.dims.w, r.dims.d, r.dims.h].map(c => formatLength(c / 100, 'metric', locale)).join(' × ') : T.unknownDims}{r.confidence === 'MEDIUM' ? ` ${T.approx}` : ''}</small>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          <button className="btn" aria-pressed={used} disabled={used} onClick={() => onUse(r.group, r.variantId)}>{T.useFor} {r.groupLabel}</button>
          {onAdd && <button className="btn" onClick={() => onAdd(r.variantId)}>{T.addToRoom}</button>}
          {r.offerId && <a className="btn" href={`/go/o/${r.offerId}`} target="_blank" rel="noopener">{T.viewStore}</a>}
        </div>
      </div>; })}
  </>);
}
