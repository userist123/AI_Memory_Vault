'use client';
// Pagină de tipărire (A4 landscape → „Salvează ca PDF”): plan la scară reală, tabel camere, listă de cumpărături și buget.
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve, furnitureTotal } from '@/core/catalog';
import { roomWorks } from '@/core/room-works';
import { lighting, sunDirection } from '@/core/lighting';
import { viewerInput } from '@/lib/viewer-input';
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
  preparing: 'Se pregătesc imaginile 3D…', noImages: 'Imaginile 3D nu au putut fi generate pe acest dispozitiv (WebGL indisponibil); exportul conține planurile și listele.', overviewAlt: 'Vedere 3D a casei', roomAlt: 'Vedere 3D a camerei',
  keyFigures: 'Cifre cheie', roomsCount: 'Camere', totalArea: 'Suprafață totală', furnitureKnown: 'Mobilier (prețuri cunoscute)', furnitureUnknown: 'piese fără preț', budgetTotalLabel: 'Total buget',
  plan: 'Plan', whatIsDone: 'Ce se face în cameră', finishesWorks: 'Finisaje și materiale', laborWorks: 'Manoperă', qty: 'Cantitate', cost: 'Cost', laborExpected: 'Estimat', laborRange: 'Interval minim – maxim', roomFurniture: 'Mobilier în cameră', roomTotal: 'Subtotal cameră', roomLabor: 'manoperă estimată', noWorks: 'Nicio lucrare specifică.', noFurniture: 'Fără mobilier.',
  byRetailer: 'Magazin', productLink: 'Link produs', rev: 'Rev.',
};

interface ProjectDto { id: string; name: string; currentRevision: number | null; draft: Snapshot }
const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };
const MARGIN = 0.5;   // m în jurul planului pentru cote

type PlanProps = { snap: Snapshot; catalog: Catalog; box: { x0: number; z0: number; w: number; d: number }; widthMm: number; heightMm: number; mm: number; units: Units; locale: Locale; roomId?: string; showDims?: boolean; aria: string };
// Planul (complet sau decupat pe o cameră): pereți, deschideri, amprentele mobilierului și etichete; `mm` = metri pe plan per mm de hârtie.
function PlanSvg({ snap, catalog, box, widthMm, heightMm, mm, units, locale, roomId, showDims, aria }: PlanProps){
  const f = snap.floor, fs = (v: number) => v * mm, dims = showDims ? wallDimensions(f) : [], rooms = roomId ? f.rooms.filter(r => r.id === roomId) : f.rooms, pls = roomId ? snap.placements.filter(pl => pl.roomId === roomId) : snap.placements;
  return (<svg viewBox={`${box.x0} ${box.z0} ${box.w} ${box.d}`} width={`${widthMm}mm`} height={`${heightMm}mm`} overflow={roomId ? 'hidden' : 'visible'} role="img" aria-label={aria} style={{ background: '#fbfaf7' }}>
    {f.rooms.map(r => <rect key={r.id} x={r.rect.x0} y={r.rect.z0} width={r.rect.x1 - r.rect.x0} height={r.rect.z1 - r.rect.z0} fill={roomId && r.id !== roomId ? '#f6f4ef' : ROOM_FILL[r.type] || '#f3f1ec'} stroke="#c9c4b8" strokeWidth={fs(0.2)} />)}
    {f.walls.map(w => <line key={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#333" strokeWidth={w.thickness} strokeLinecap="square" />)}
    {f.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
      return w.openings.map(o => <line key={o.id} x1={w.a[0] + ux * o.offset} y1={w.a[1] + uz * o.offset} x2={w.a[0] + ux * (o.offset + o.width)} y2={w.a[1] + uz * (o.offset + o.width)} stroke={o.kind === 'door' ? '#B7791F' : '#5b8fb9'} strokeWidth={w.thickness + 0.02} />); })}
    {pls.map(pl => { const fp = footprintOf(catalog, pl); if (!fp) return null; const rv = resolve(catalog, pl.variantId), label = (rv?.product.name ?? pl.group).split(' ')[0] ?? '';
      return <g key={pl.id}><rect x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill="#ffffffcc" stroke="#1F4E79" strokeWidth={fs(0.2)} rx={0.03} />
        {(fp.x1 - fp.x0) > fs(label.length * 1.8) && <text x={(fp.x0 + fp.x1) / 2} y={(fp.z0 + fp.z1) / 2 + fs(0.7)} fontSize={fs(2)} textAnchor="middle" fill="#1F4E79">{label}</text>}</g>; })}
    {rooms.map(r => <text key={'l' + r.id} x={r.rect.x0 + 0.15} y={r.rect.z1 - 0.15} fontSize={fs(3)} fill="#444" stroke="#fbfaf7" strokeWidth={fs(1)} strokeLinejoin="round" paintOrder="stroke">{r.name} · {formatArea((r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0), units, locale)}</text>)}
    {dims.filter(d => d.exterior).map(d => { const w = f.walls.find(x => x.id === d.wallId)!, k = 0.3, tick = fs(1.2), nx = d.normal[0], nz = d.normal[1];
      const a = [w.a[0] + nx * k, w.a[1] + nz * k], c = [w.b[0] + nx * k, w.b[1] + nz * k], vertical = Math.abs(w.b[0] - w.a[0]) < Math.abs(w.b[1] - w.a[1]);
      const text = units === 'imperial' ? formatLength(d.lengthM, 'imperial', locale) : String(d.lengthCm);
      return <g key={'d' + d.wallId} stroke="#666" strokeWidth={fs(0.15)}><line x1={a[0]} y1={a[1]} x2={c[0]} y2={c[1]} />
        <line x1={a[0] - nx * tick} y1={a[1] - nz * tick} x2={a[0] + nx * tick} y2={a[1] + nz * tick} /><line x1={c[0] - nx * tick} y1={c[1] - nz * tick} x2={c[0] + nx * tick} y2={c[1] + nz * tick} />
        <text x={d.label[0]} y={d.label[1]} fontSize={fs(2.4)} fill="#333" stroke="none" textAnchor="middle" dominantBaseline="central" transform={vertical ? `rotate(-90 ${d.label[0]} ${d.label[1]})` : undefined}>{text}</text></g>; })}
  </svg>);
}
const nextFrame = () => new Promise<void>(res => requestAnimationFrame(() => res()));

export default function PrintView({ id, locale = 'ro', units = 'metric' }: { id: string; locale?: Locale; units?: Units }){
  const [data, setData] = useState<{ p: ProjectDto; catalog: Catalog; mc: MaterialsCatalog } | null>(null), [err, setErr] = useState(false), [today, setToday] = useState('');
  const [images, setImages] = useState<Record<string, string>>({}), [busy, setBusy] = useState(true), [imgErr, setImgErr] = useState(false), canvasRef = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    setToday(new Date().toLocaleDateString(locale === 'ro' ? 'ro-RO' : locale));
    Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/materials').then(r => r.ok ? r.json() : Promise.reject(r))])
      .then(([p, catalog, mc]) => setData({ p, catalog, mc })).catch(() => { setErr(true); setBusy(false); });
  }, [id, locale]);
  // imaginile 3D: un motor pe un canvas ascuns, în afara ecranului; o imagine pentru ansamblu și una pe cameră
  useEffect(() => {
    if (!data) return; let alive = true, viewer: any = null;
    (async () => { try {
      const m = await import('./viewer3d-engine.js'); if (!alive || !canvasRef.current) return;
      viewer = m.createViewer(canvasRef.current); const lp = lighting('day'); viewer.setLighting({ ...lp, dir: sunDirection(lp.azimuthDeg, lp.elevationDeg) });
      const snap = data.p.draft, { plan, items } = viewerInput(snap, data.catalog); viewer.setState(plan, items);
      await nextFrame(); await nextFrame(); if (!alive) return;
      const out: Record<string, string> = {}, ov = viewer.renderView(undefined, 1200, 800); if (!ov) throw new Error('no image'); out.overview = ov;
      for (const r of snap.floor.rooms){ const u = viewer.renderView({ roomId: r.id }, 1200, 800); if (u) out[r.id] = u; }
      if (alive) setImages(out);
    } catch { if (alive) setImgErr(true); } finally { try { viewer?.dispose(); } catch { /* deja eliberat */ } if (alive) setBusy(false); } })();
    return () => { alive = false; };
  }, [data]);
  const canvas = <canvas ref={canvasRef} width={1200} height={800} aria-hidden="true" style={{ position: 'fixed', left: -10000, top: 0, width: 1200, height: 800, pointerEvents: 'none' }} />;
  const view = useMemo(() => {
    if (!data) return null;
    const { p, catalog, mc } = data, snap = p.draft, f = snap.floor;
    const works = new Map(f.rooms.map(r => [r.id, roomWorks(snap, catalog, mc, r.id)] as const));
    return { works, budget: computeBudget(snap, catalog, mc), furn: furnitureTotal(catalog, snap.placements) };
  }, [data]);
  if (err) return <main className="print-page"><p>{T.loadError}</p></main>;
  if (!data || !view) return <main className="print-page">{canvas}<p>{T.loading}</p></main>;

  const { p, catalog } = data, snap = p.draft, f = snap.floor, budget = view.budget;
  const b = floorBounds(f) ?? { x0: 0, x1: 6, z0: 0, z1: 5 };
  const ex0 = b.x0 - MARGIN, ez0 = b.z0 - MARGIN, ew = b.x1 - b.x0 + 2 * MARGIN, ed = b.z1 - b.z0 + 2 * MARGIN;
  const sc = printScale(ew, ed), mm = sc.denominator / 1000;   // 1 mm pe hârtie = `mm` metri pe plan
  const sched = roomSchedule(snap), money = (n: number | null | undefined, cur: string) => formatMoney(n, cur, locale, T.unknownPrice);
  const cur = catalog.offers[0]?.currency ?? 'RON', bm = (n: number | null) => formatMoney(n, cur, locale, T.unknownPrice);
  const furnKnownByCur = new Map<string, number>(); for (const pl of snap.placements){ const o = resolve(catalog, pl.variantId)?.offer; if (o && Number.isFinite(o.price)) furnKnownByCur.set(o.currency, (furnKnownByCur.get(o.currency) ?? 0) + o.price); }

  // lista de cumpărături pe magazine; prețurile lipsă rămân necunoscute, totalul se face pe monedă
  const sums = new Map<string, number>(); let unknownCount = 0;
  const byRetailer = new Map<string, { id: string; name: string; variant: string; room: string; offer: NonNullable<ReturnType<typeof resolve>>['offer'] }[]>();
  for (const pl of snap.placements){ const rv = resolve(catalog, pl.variantId), o = rv?.offer ?? null, shop = o?.provenance.source ?? T.noOffer;
    if (o && Number.isFinite(o.price)) sums.set(o.currency, (sums.get(o.currency) ?? 0) + o.price); else unknownCount++;
    const arr = byRetailer.get(shop) ?? []; arr.push({ id: pl.id, name: rv?.product.name ?? pl.group, variant: rv?.variant.name ?? '', room: f.rooms.find(r => r.id === pl.roomId)?.name ?? '', offer: o }); byRetailer.set(shop, arr); }
  const catLines = ([['furniture', T.furniture], ['finishes', T.finishes], ['lighting', T.lighting], ['appliances', T.appliances], ['sanitary', T.sanitary]] as const).map(([k, l]) => [l, budget.categories[k] ?? 0] as const).filter(([, v]) => v > 0);
  const planBox = { x0: ex0, z0: ez0, w: ew, d: ed };
  const ROOM_MM = 100;

  return (<main className="print-page">
    {canvas}
    <header className="print-head no-print-border">
      <div><h1>{snap.name || p.name || T.unnamed}</h1><div className="muted">{T.printedOn} {today} · {p.currentRevision ? `${T.revision} ${p.currentRevision}` : T.noRevisions}</div></div>
      <div className="print-brand">{T.brand}<button className="btn primary no-print" onClick={() => window.print()} disabled={busy}>{T.print}</button>
        {busy && <span className="muted no-print" role="status">{T.preparing}</span>}</div>
    </header>
    {imgErr && !busy && <p className="muted no-print" role="status">{T.noImages}</p>}

    <section className="print-cover" data-testid="cover">
      {images.overview ? <img className="print-hero" src={images.overview} alt={T.overviewAlt} /> : <div className="print-hero print-hero-empty">{busy ? T.preparing : T.dash}</div>}
      <div className="print-figs"><h2>{T.keyFigures}</h2>
        <dl>
          <dt>{T.roomsCount}</dt><dd>{sched.totals.rooms}</dd>
          <dt>{T.totalArea}</dt><dd>{formatArea(sched.totals.area, units, locale)}</dd>
          <dt>{T.furnitureKnown}</dt><dd>{furnKnownByCur.size ? [...furnKnownByCur].map(([c, v]) => formatMoney(v, c, locale)).join(' + ') : T.dash}{view.furn.unknown > 0 && <small> · {view.furn.unknown} {T.furnitureUnknown}</small>}</dd>
          <dt>{T.budgetTotalLabel}</dt><dd>{bm(budget.chosen.total)}</dd>
        </dl></div>
    </section>

    <section className="print-plan print-break">
      <PlanSvg snap={snap} catalog={catalog} box={planBox} widthMm={sc.widthMm} heightMm={sc.heightMm} mm={mm} units={units} locale={locale} showDims aria={T.planAria} />
      <div className="print-scale"><b>{T.scale} 1:{sc.denominator}</b>
        <svg role="img" aria-label={T.scaleBar} width={`${5000 / sc.denominator}mm`} height="7mm" viewBox={`0 0 ${5000 / sc.denominator} 7`} overflow="visible">
          <rect x={0} y={2} width={1000 / sc.denominator} height={1.6} fill="#333" /><rect x={1000 / sc.denominator} y={2} width={4000 / sc.denominator} height={1.6} fill="#fff" stroke="#333" strokeWidth={0.2} />
          <text x={0} y={6.5} fontSize={2.4}>0</text><text x={1000 / sc.denominator} y={6.5} fontSize={2.4} textAnchor="middle">{formatLength(1, units, locale)}</text><text x={5000 / sc.denominator} y={6.5} fontSize={2.4} textAnchor="end">{formatLength(5, units, locale)}</text></svg></div>
      <div className="print-sec"><h2>{T.schedule}</h2>
        <table className="print-table"><thead><tr><th>{T.room}</th><th>{T.dims}</th><th>{T.areaCol}</th><th>{T.perimeter}</th></tr></thead>
          <tbody>{sched.rows.map(r => <tr key={r.id}><td>{r.name}</td><td>{formatLength(r.width, units, locale)} × {formatLength(r.depth, units, locale)}</td><td>{formatArea(r.area, units, locale)}</td><td>{formatLength(r.perimeter, units, locale)}</td></tr>)}</tbody>
          <tfoot><tr><th>{T.total} ({sched.totals.rooms} {T.rooms})</th><td></td><th>{formatArea(sched.totals.area, units, locale)}</th><td></td></tr></tfoot></table></div>
    </section>

    {f.rooms.map(r => { const w = view.works.get(r.id)!, row = sched.rows.find(x => x.id === r.id), rw = r.rect.x1 - r.rect.x0, rd = r.rect.z1 - r.rect.z0, PAD = 0.4;
      const bw = rw + 2 * PAD, bd = rd + 2 * PAD, k = ROOM_MM / Math.max(bw, bd), img = images[r.id];
      return (<section key={r.id} className="print-room print-break" data-testid={`room-${r.id}`}>
        <h2>{r.name} <small>· {formatArea(row?.area ?? rw * rd, units, locale)} · {formatLength(rw, units, locale)} × {formatLength(rd, units, locale)}</small></h2>
        <div className="print-room-cols">
          <div className="print-room-left">
            {img ? <img className="print-room-img" src={img} alt={`${T.roomAlt} ${r.name}`} /> : <div className="print-room-img print-hero-empty">{busy ? T.preparing : T.dash}</div>}
            <h3>{T.whatIsDone}</h3>
            <table className="print-table"><thead><tr><th>{T.finishesWorks}</th><th className="num">{T.qty}</th><th className="num">{T.cost}</th></tr></thead>
              <tbody>{w.materials.length === 0 && <tr><td colSpan={3} className="muted">{T.noWorks}</td></tr>}
                {w.materials.map(m => <tr key={m.key}><td>{m.label}</td><td className="num">{m.qty} {m.unit}</td><td className="num">{money(m.cost, cur)}</td></tr>)}</tbody>
              {w.labor.length > 0 && <><thead><tr><th>{T.laborWorks}</th><th className="num">{T.qty}</th><th className="num">{T.laborExpected} ({T.laborRange})</th></tr></thead>
                <tbody>{w.labor.map(l => <tr key={l.key}><td>{l.label}</td><td className="num">{l.qty} {l.unit}</td><td className="num">{money(l.expected, cur)} <small>({money(l.low, cur)} – {money(l.high, cur)})</small></td></tr>)}</tbody></>}
            </table>
          </div>
          <div className="print-room-right">
            <div className="print-room-plan"><PlanSvg snap={snap} catalog={catalog} box={{ x0: r.rect.x0 - PAD, z0: r.rect.z0 - PAD, w: bw, d: bd }} widthMm={bw * k} heightMm={bd * k} mm={1 / k} units={units} locale={locale} roomId={r.id} aria={`${T.plan} ${r.name}`} /></div>
            <table className="print-table"><thead><tr><th>{T.roomFurniture}</th><th>{T.retailer}</th><th className="num">{T.price}</th></tr></thead>
              <tbody>{w.furniture.length === 0 && <tr><td colSpan={3} className="muted">{T.noFurniture}</td></tr>}
                {w.furniture.map(x => <tr key={x.id}><td>{x.name}</td><td>{x.retailer ?? T.noOffer}</td><td className="num">{money(x.price, x.currency ?? cur)}</td></tr>)}</tbody></table>
            <p className="print-totals">{Object.entries(w.totals).map(([c, t]) => <span key={c}><b>{T.roomTotal}:</b> {formatMoney(t.known, c, locale)}{t.unknown > 0 && <> + {t.unknown} {T.furnitureUnknown}</>}{t.laborExpected > 0 && <> · {T.roomLabor} {formatMoney(t.laborExpected, c, locale)}</>} </span>)}</p>
          </div>
        </div>
      </section>); })}

    <section className="print-sec print-break"><h2>{T.shopping}</h2>
      {byRetailer.size === 0 && <p className="muted">{T.noItems}</p>}
      {[...byRetailer].map(([shop, rows]) => <div key={shop} className="print-nobreak"><h3>{shop}</h3>
        <table className="print-table"><thead><tr><th>{T.product}</th><th>{T.room}</th><th className="num">{T.price}</th><th>{T.productLink}</th></tr></thead>
          <tbody>{rows.map(i => <tr key={i.id}><td>{i.name}{i.variant && <small> · {i.variant}</small>}</td><td>{i.room}</td><td className="num">{i.offer ? money(i.offer.price, i.offer.currency) : T.unknownPrice}</td><td>{i.offer ? <a href={`/go/o/${i.offer.id}`}>/go/o/{i.offer.id}</a> : T.dash}</td></tr>)}</tbody></table></div>)}
      <p className="print-totals"><b>{T.knownSum}:</b> {sums.size ? [...sums].map(([c, v]) => formatMoney(v, c, locale)).join(' + ') : T.dash}{unknownCount > 0 && <> · <b>{unknownCount}</b> {T.unknownCount}</>}</p>

      <h2 style={{ marginTop: 16 }}>{T.budget}</h2>
      <table className="print-table"><tbody>
        {catLines.map(([l, v]) => <tr key={l}><td>{l}</td><td className="num">{bm(v)}</td></tr>)}
        {budget.extras.map(e => <tr key={e.key}><td>{e.label}</td><td className="num">{bm(e.amount)}</td></tr>)}
        {budget.unknownLines.map(e => <tr key={e.key}><td>{e.label}</td><td className="num">{T.unknownLines}</td></tr>)}
        {budget.laborTotals.high > 0 && <tr><td>{T.labor} ({T.low} / {T.expected} / {T.high})</td><td className="num">{bm(budget.laborTotals.low)} / {bm(budget.laborTotals.expected)} / {bm(budget.laborTotals.high)}</td></tr>}
        <tr><td>{T.subtotal}</td><td className="num">{bm(budget.chosen.subtotal)}</td></tr>
        {budget.chosen.contingency > 0 && <tr><td>{T.contingency} ({budget.settings.contingencyPct}%)</td><td className="num">{bm(budget.chosen.contingency)}</td></tr>}
      </tbody><tfoot><tr><th>{T.budgetTotal}</th><th className="num">{bm(budget.chosen.total)}</th></tr></tfoot></table>
      {budget.unknownItems.length > 0 && <p className="muted">{T.unknownItems}: {budget.unknownItems.join(', ')}</p>}
      {new Set(catalog.offers.map(o => o.currency)).size > 1 && <p className="muted">{T.currencyNote}</p>}
      <footer className="print-foot">{T.disclaimer}</footer></section>
  </main>);
}
