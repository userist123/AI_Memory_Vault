'use client';
// Pagină de tipărire (A4 landscape → „Salvează ca PDF”): plan la scară reală, tabel camere, listă de cumpărături și buget.
import { useEffect, useState } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve } from '@/core/catalog';
import { computeBudget } from '@/core/boq';
import { floorBounds, wallDimensions, roomSchedule, printScale } from '@/core/dimensions';
import { formatMoney, formatLength, formatArea, type Locale, type Units } from '@/core/format';

// Toate textele vizibile ale paginii, într-un singur loc, pentru stratul i18n.
const T = {
  brand: 'Casa mea 3D', printedOn: 'Tipărit la', revision: 'Revizia', noRevisions: 'fără revizii', loading: 'Se încarcă…', loadError: 'Proiectul nu există sau nu ai acces la el.',
  print: 'Tipărește / Salvează ca PDF', scale: 'Scara', planAria: 'Planul locuinței la scară', unnamed: 'Proiect fără nume',
  schedule: 'Tabel camere', room: 'Cameră', dims: 'Dimensiuni', areaCol: 'Suprafață', perimeter: 'Perimetru', total: 'Total', rooms: 'camere',
  shopping: 'Listă de cumpărături', product: 'Produs', retailer: 'Magazin', price: 'Preț', link: 'Link', offer: 'ofertă', unknownPrice: 'preț necunoscut', noItems: 'Nicio piesă plasată.',
  knownSum: 'Suma prețurilor cunoscute', unknownCount: 'piese cu preț necunoscut (nu sunt numărate ca 0)', noOffer: 'fără ofertă',
  budget: 'Buget', furniture: 'Mobilier', finishes: 'Finisaje', lighting: 'Iluminat', appliances: 'Electrocasnice', sanitary: 'Sanitare', extras: 'Servicii și transport', unknownLines: 'valoare necunoscută',
  labor: 'Manoperă', low: 'minim', expected: 'estimat', high: 'maxim', contingency: 'Rezervă', subtotal: 'Subtotal', budgetTotal: 'Total buget', unknownItems: 'Piese fără preț, neincluse în total',
  currencyNote: 'Bugetul este calculat în moneda catalogului; ofertele cu monede diferite sunt totalizate separat în lista de cumpărături.',
  disclaimer: 'Prețurile provin din catalogul curent, cu data verificării din catalog, și se pot schimba. Dimensiunile planului sunt cele desenate; verifică pe teren înainte de a comanda.',
  dash: '—', scaleBar: 'Bară de scară',
};

interface ProjectDto { id: string; name: string; currentRevision: number | null; draft: Snapshot }
const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };
const MARGIN = 0.5;   // m în jurul planului pentru cote

export default function PrintView({ id, locale = 'ro', units = 'metric' }: { id: string; locale?: Locale; units?: Units }){
  const [data, setData] = useState<{ p: ProjectDto; catalog: Catalog; mc: MaterialsCatalog } | null>(null), [err, setErr] = useState(false), [today, setToday] = useState('');
  useEffect(() => {
    setToday(new Date().toLocaleDateString(locale === 'ro' ? 'ro-RO' : locale));
    Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/materials').then(r => r.ok ? r.json() : Promise.reject(r))])
      .then(([p, catalog, mc]) => setData({ p, catalog, mc })).catch(() => setErr(true));
  }, [id, locale]);
  if (err) return <main className="print-page"><p>{T.loadError}</p></main>;
  if (!data) return <main className="print-page"><p>{T.loading}</p></main>;

  const { p, catalog, mc } = data, snap = p.draft, f = snap.floor;
  const b = floorBounds(f) ?? { x0: 0, x1: 6, z0: 0, z1: 5 };
  const ex0 = b.x0 - MARGIN, ez0 = b.z0 - MARGIN, ew = b.x1 - b.x0 + 2 * MARGIN, ed = b.z1 - b.z0 + 2 * MARGIN;
  const sc = printScale(ew, ed), mm = sc.denominator / 1000;   // 1 mm pe hârtie = `mm` metri pe plan
  const dims = wallDimensions(f), sched = roomSchedule(snap), money = (n: number | null | undefined, cur: string) => formatMoney(n, cur, locale, T.unknownPrice);

  // lista de cumpărături pe camere; prețurile lipsă rămân necunoscute, totalul se face pe monedă
  const sums = new Map<string, number>(); let unknownCount = 0;
  const shop = f.rooms.map(r => ({ room: r, items: snap.placements.filter(pl => pl.roomId === r.id).map(pl => { const rv = resolve(catalog, pl.variantId), o = rv?.offer ?? null;
    if (o && Number.isFinite(o.price)) sums.set(o.currency, (sums.get(o.currency) ?? 0) + o.price); else unknownCount++;
    return { id: pl.id, name: rv?.product.name ?? pl.group, variant: rv?.variant.name ?? '', retailer: o?.provenance.source ?? T.noOffer, offer: o }; }) })).filter(s => s.items.length);
  const budget = computeBudget(snap, catalog, mc), cur = catalog.offers[0]?.currency ?? 'RON', bm = (n: number | null) => formatMoney(n, cur, locale, T.unknownPrice);
  const catLines = ([['furniture', T.furniture], ['finishes', T.finishes], ['lighting', T.lighting], ['appliances', T.appliances], ['sanitary', T.sanitary]] as const).map(([k, l]) => [l, budget.categories[k] ?? 0] as const).filter(([, v]) => v > 0);
  const fs = (pxMm: number) => pxMm * mm;   // dimensiune text dată în mm de hârtie → metri pe plan

  return (<main className="print-page">
    <header className="print-head">
      <div><h1>{snap.name || p.name || T.unnamed}</h1><div className="muted">{T.printedOn} {today} · {p.currentRevision ? `${T.revision} ${p.currentRevision}` : T.noRevisions}</div></div>
      <div className="print-brand">{T.brand}<button className="btn primary no-print" onClick={() => window.print()}>{T.print}</button></div>
    </header>

    <section className="print-plan">
      <svg viewBox={`${ex0} ${ez0} ${ew} ${ed}`} width={`${sc.widthMm}mm`} height={`${sc.heightMm}mm`} overflow="visible" role="img" aria-label={T.planAria}>
        {f.rooms.map(r => <rect key={r.id} x={r.rect.x0} y={r.rect.z0} width={r.rect.x1 - r.rect.x0} height={r.rect.z1 - r.rect.z0} fill={ROOM_FILL[r.type] || '#f3f1ec'} stroke="#c9c4b8" strokeWidth={fs(0.2)} />)}
        {f.walls.map(w => <line key={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#333" strokeWidth={w.thickness} strokeLinecap="square" />)}
        {f.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
          return w.openings.map(o => <line key={o.id} x1={w.a[0] + ux * o.offset} y1={w.a[1] + uz * o.offset} x2={w.a[0] + ux * (o.offset + o.width)} y2={w.a[1] + uz * (o.offset + o.width)} stroke={o.kind === 'door' ? '#B7791F' : '#5b8fb9'} strokeWidth={w.thickness + 0.02} />); })}
        {snap.placements.map(pl => { const fp = footprintOf(catalog, pl); if (!fp) return null; const rv = resolve(catalog, pl.variantId), label = (rv?.product.name ?? pl.group).split(' ')[0];
          return <g key={pl.id}><rect x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill="#ffffffcc" stroke="#1F4E79" strokeWidth={fs(0.2)} rx={0.03} />
            {(fp.x1 - fp.x0) > fs(label.length * 1.8) && <text x={(fp.x0 + fp.x1) / 2} y={(fp.z0 + fp.z1) / 2 + fs(0.7)} fontSize={fs(2)} textAnchor="middle" fill="#1F4E79">{label}</text>}</g>; })}
        {f.rooms.map(r => <text key={'l' + r.id} x={r.rect.x0 + 0.15} y={r.rect.z1 - 0.15} fontSize={fs(3)} fill="#444" stroke="#fbfaf7" strokeWidth={fs(1)} strokeLinejoin="round" paintOrder="stroke">{r.name} · {formatArea((r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0), units, locale)}</text>)}
        {dims.filter(d => d.exterior).map(d => { const w = f.walls.find(x => x.id === d.wallId)!, k = 0.3, tick = fs(1.2), nx = d.normal[0], nz = d.normal[1];
          const a = [w.a[0] + nx * k, w.a[1] + nz * k], c = [w.b[0] + nx * k, w.b[1] + nz * k], vertical = Math.abs(w.b[0] - w.a[0]) < Math.abs(w.b[1] - w.a[1]);
          const text = units === 'imperial' ? formatLength(d.lengthM, 'imperial', locale) : String(d.lengthCm);
          return <g key={'d' + d.wallId} stroke="#666" strokeWidth={fs(0.15)}><line x1={a[0]} y1={a[1]} x2={c[0]} y2={c[1]} />
            <line x1={a[0] - nx * tick} y1={a[1] - nz * tick} x2={a[0] + nx * tick} y2={a[1] + nz * tick} /><line x1={c[0] - nx * tick} y1={c[1] - nz * tick} x2={c[0] + nx * tick} y2={c[1] + nz * tick} />
            <text x={d.label[0]} y={d.label[1]} fontSize={fs(2.4)} fill="#333" stroke="none" textAnchor="middle" dominantBaseline="central" transform={vertical ? `rotate(-90 ${d.label[0]} ${d.label[1]})` : undefined}>{text}</text></g>; })}
      </svg>
      <div className="print-scale"><b>{T.scale} 1:{sc.denominator}</b>
        <svg role="img" aria-label={T.scaleBar} width={`${5000 / sc.denominator}mm`} height="7mm" viewBox={`0 0 ${5000 / sc.denominator} 7`} overflow="visible">
          <rect x={0} y={2} width={1000 / sc.denominator} height={1.6} fill="#333" /><rect x={1000 / sc.denominator} y={2} width={4000 / sc.denominator} height={1.6} fill="#fff" stroke="#333" strokeWidth={0.2} />
          <text x={0} y={6.5} fontSize={2.4}>0</text><text x={1000 / sc.denominator} y={6.5} fontSize={2.4} textAnchor="middle">{formatLength(1, units, locale)}</text><text x={5000 / sc.denominator} y={6.5} fontSize={2.4} textAnchor="end">{formatLength(5, units, locale)}</text></svg></div>
    </section>

    <section className="print-sec"><h2>{T.schedule}</h2>
      <table className="print-table"><thead><tr><th>{T.room}</th><th>{T.dims}</th><th>{T.areaCol}</th><th>{T.perimeter}</th></tr></thead>
        <tbody>{sched.rows.map(r => <tr key={r.id}><td>{r.name}</td><td>{formatLength(r.width, units, locale)} × {formatLength(r.depth, units, locale)}</td><td>{formatArea(r.area, units, locale)}</td><td>{formatLength(r.perimeter, units, locale)}</td></tr>)}</tbody>
        <tfoot><tr><th>{T.total} ({sched.totals.rooms} {T.rooms})</th><td></td><th>{formatArea(sched.totals.area, units, locale)}</th><td></td></tr></tfoot></table></section>

    <section className="print-sec print-break"><h2>{T.shopping}</h2>
      {shop.length === 0 && <p className="muted">{T.noItems}</p>}
      {shop.map(s => <div key={s.room.id} className="print-nobreak"><h3>{s.room.name}</h3>
        <table className="print-table"><thead><tr><th>{T.product}</th><th>{T.retailer}</th><th>{T.price}</th><th>{T.link}</th></tr></thead>
          <tbody>{s.items.map(i => <tr key={i.id}><td>{i.name}{i.variant && <small> · {i.variant}</small>}</td><td>{i.retailer}</td><td>{i.offer ? money(i.offer.price, i.offer.currency) : T.unknownPrice}</td><td>{i.offer ? <a href={`/go/o/${i.offer.id}`}>/go/o/{i.offer.id}</a> : T.dash}</td></tr>)}</tbody></table></div>)}
      <p className="print-totals"><b>{T.knownSum}:</b> {sums.size ? [...sums].map(([c, v]) => formatMoney(v, c, locale)).join(' + ') : T.dash}{unknownCount > 0 && <> · <b>{unknownCount}</b> {T.unknownCount}</>}</p></section>

    <section className="print-sec print-break"><h2>{T.budget}</h2>
      <table className="print-table"><tbody>
        {catLines.map(([l, v]) => <tr key={l}><td>{l}</td><td className="num">{bm(v)}</td></tr>)}
        {budget.extras.map(e => <tr key={e.key}><td>{e.label}</td><td className="num">{bm(e.amount)}</td></tr>)}
        {budget.unknownLines.map(e => <tr key={e.key}><td>{e.label}</td><td className="num">{T.unknownLines}</td></tr>)}
        {budget.laborTotals.high > 0 && <tr><td>{T.labor} ({T.low} / {T.expected} / {T.high})</td><td className="num">{bm(budget.laborTotals.low)} / {bm(budget.laborTotals.expected)} / {bm(budget.laborTotals.high)}</td></tr>}
        <tr><td>{T.subtotal}</td><td className="num">{bm(budget.chosen.subtotal)}</td></tr>
        {budget.chosen.contingency > 0 && <tr><td>{T.contingency} ({budget.settings.contingencyPct}%)</td><td className="num">{bm(budget.chosen.contingency)}</td></tr>}
      </tbody><tfoot><tr><th>{T.budgetTotal}</th><th className="num">{bm(budget.chosen.total)}</th></tr></tfoot></table>
      {budget.unknownItems.length > 0 && <p className="muted">{T.unknownItems}: {budget.unknownItems.join(', ')}</p>}
      {new Set(catalog.offers.map(o => o.currency)).size > 1 && <p className="muted">{T.currencyNote}</p>}</section>

    <footer className="print-foot">{T.disclaimer}</footer>
  </main>);
}
