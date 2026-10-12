// Planșele pentru echipe (tipărite la scară): plan de tavan și iluminat, plan de pardoseli, elevațiile pereților pe cameră.
// Geometria vine din core/drawings.ts; aici e doar desenul.
import type { Catalog, Floor, MaterialsCatalog, Room, Snapshot } from '@/core/types';
import { ceilingPlan, lightingLegend, floorPlan, wallElevations, type Elevation } from '@/core/drawings';
import { formatArea, formatLength, type Units } from '@/core/format';
import type { Lang, Translate } from '@/lib/i18n';

type Box = { x0: number; z0: number; w: number; d: number };
const INK = '#2a2d31', LIGHT = '#8A5A00', LED = '#d98a00';
const Walls = ({ fl }: { fl: Floor }) => <>{fl.walls.map(w => <line key={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#333" strokeWidth={w.thickness} strokeLinecap="square" />)}
  {fl.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    return w.openings.map(o => <line key={o.id} x1={w.a[0] + ux * o.offset} y1={w.a[1] + uz * o.offset} x2={w.a[0] + ux * (o.offset + o.width)} y2={w.a[1] + uz * (o.offset + o.width)} stroke={o.kind === 'door' ? '#fff' : '#5b8fb9'} strokeWidth={w.thickness * .8} />); })}</>;
/** Simbolurile de iluminat (aceleași ca pe planul din editor), cu `r` = raza în metri de plan. */
export function FixtureSymbol({ kind, x, z, r, nx = 0, nz = 0, len, alongX }: { kind: string; x: number; z: number; r: number; nx?: number; nz?: number; len?: number; alongX?: boolean }){
  const sw = r * .12;
  if (kind === 'track'){ const h = (len ?? 2) / 2, [x0, z0, x1, z1] = alongX ? [x - h, z, x + h, z] : [x, z - h, x, z + h], n = Math.max(2, Math.round((len ?? 2) / .5));
    return <g><line x1={x0} y1={z0} x2={x1} y2={z1} stroke={LIGHT} strokeWidth={sw * 2} />{Array.from({ length: n }, (_, k) => { const o = (k + .5) / n; return <circle key={k} cx={x0 + (x1 - x0) * o} cy={z0 + (z1 - z0) * o} r={r * .45} fill="#fff" stroke={LIGHT} strokeWidth={sw} />; })}</g>; }
  if (kind === 'sconce'){ const a = Math.atan2(nz, nx) * 180 / Math.PI; return <path transform={`translate(${x} ${z}) rotate(${a})`} d={`M0 ${-r} A${r} ${r} 0 0 1 0 ${r} Z`} fill="#fff" stroke={LIGHT} strokeWidth={sw} />; }
  if (kind === 'spot') return <g><circle cx={x} cy={z} r={r * .5} fill="#fff" stroke={LIGHT} strokeWidth={sw} /><circle cx={x} cy={z} r={r * .15} fill={LIGHT} /></g>;
  if (kind === 'center') return <g><circle cx={x} cy={z} r={r * 1.1} fill="#fff" stroke={INK} strokeWidth={sw} /><path d={`M${x - r * .8} ${z}L${x + r * .8} ${z}M${x} ${z - r * .8}L${x} ${z + r * .8}`} stroke={INK} strokeWidth={sw} /></g>;
  return <g><circle cx={x} cy={z} r={r} fill="#fff" stroke={LIGHT} strokeWidth={sw} /><path d={`M${x - r * .7} ${z - r * .7}L${x + r * .7} ${z + r * .7}M${x - r * .7} ${z + r * .7}L${x + r * .7} ${z - r * .7}`} stroke={LIGHT} strokeWidth={sw} /></g>;
}

export function CeilingSheet({ snap, fl, cat, mc, box, widthMm, heightMm, mm, t, units, locale }: { snap: Snapshot; fl: Floor; cat: Catalog; mc: MaterialsCatalog; box: Box; widthMm: number; heightMm: number; mm: number; t: Translate; units: Units; locale: Lang }){
  const rooms = ceilingPlan(snap, mc, cat, fl), legend = lightingLegend(snap, mc, fl), fs = (v: number) => v * mm, r = fs(1.6);
  return (<>
    <svg viewBox={`${box.x0} ${box.z0} ${box.w} ${box.d}`} width={`${widthMm}mm`} height={`${heightMm}mm`} role="img" aria-label={t('draw.ceiling')} style={{ background: '#fff' }}>
      <defs><pattern id="hatch-drop" width={fs(2)} height={fs(2)} patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><line x1={0} y1={0} x2={0} y2={fs(2)} stroke="#c9c4b8" strokeWidth={fs(.2)} /></pattern></defs>
      {rooms.map(c => { const q = c.rect, cx = (q.x0 + q.x1) / 2, cz = (q.z0 + q.z1) / 2;
        return <g key={c.roomId}>
          <rect x={q.x0} y={q.z0} width={q.x1 - q.x0} height={q.z1 - q.z0} fill={c.type === 'flat' ? '#fbfaf7' : 'url(#hatch-drop)'} stroke="#c9c4b8" strokeWidth={fs(.2)} />
          {c.cove && <rect x={c.cove.x0} y={c.cove.z0} width={c.cove.x1 - c.cove.x0} height={c.cove.z1 - c.cove.z0} fill="#fbfaf7" stroke={c.ledM ? LED : INK} strokeWidth={fs(.4)} strokeDasharray={`${fs(1.5)} ${fs(.8)}`} />}
          {c.cornice && <rect x={q.x0 + .12} y={q.z0 + .12} width={q.x1 - q.x0 - .24} height={q.z1 - q.z0 - .24} fill="none" stroke={INK} strokeWidth={fs(.15)} />}
          {c.centerLights > 0 && <FixtureSymbol kind="center" x={cx} z={cz} r={r} />}
          {c.centerLights > 1 && <text x={cx + r * 1.4} y={cz - r} fontSize={fs(2.2)} fill={INK}>×{c.centerLights}</text>}
          {c.spots.map(([x, z], k) => <FixtureSymbol key={k} kind="spot" x={x} z={z} r={r} />)}
          {c.fixtures.map((p, k) => <FixtureSymbol key={`f${k}`} kind={p.kind} x={p.x} z={p.z} r={r} nx={p.nx} nz={p.nz} {...(p.len != null ? { len: p.len } : {})} {...(p.alongX != null ? { alongX: p.alongX } : {})} />)}
          <text x={q.x0 + .15} y={q.z0 + fs(4)} fontSize={fs(2.6)} fill={INK} stroke="#fff" strokeWidth={fs(.8)} paintOrder="stroke">{c.name}</text>
          <text x={q.x0 + .15} y={q.z0 + fs(7)} fontSize={fs(2.1)} fill="#555" stroke="#fff" strokeWidth={fs(.8)} paintOrder="stroke">{t(`fin.ceiling.${c.type}`)} · h {formatLength(c.heightM, units, locale)}{c.ledM ? ` · LED ${formatLength(c.ledM, units, locale)}` : ''}</text>
          <text x={q.x0 + .15} y={q.z0 + fs(9.6)} fontSize={fs(2.1)} fill={c.target && c.lux < c.target[0] ? '#B3261E' : '#555'} stroke="#fff" strokeWidth={fs(.8)} paintOrder="stroke">~{c.lux} lx{c.target ? ` (${c.target[0]}–${c.target[1]})` : ''}</text>
        </g>; })}
      <Walls fl={fl} />
    </svg>
    <table className="print-table"><thead><tr><th>{t('draw.symbol')}</th><th>{t('draw.product')}</th><th className="num">{t('print.qty')}</th></tr></thead>
      <tbody>{legend.map((l, i) => <tr key={i}><td><svg width="8mm" height="6mm" viewBox="-1.6 -1.2 3.2 2.4">{l.kind === 'led' ? <line x1={-1.4} y1={0} x2={1.4} y2={0} stroke={LED} strokeWidth={.25} strokeDasharray=".5 .25" /> : <FixtureSymbol kind={l.kind} x={0} z={0} r={.8} nx={1} len={2.4} alongX />}</svg> {t(`draw.kind.${l.kind}`)}</td>
        <td>{l.name || <span className="muted">{t('draw.noProduct')}</span>}</td><td className="num">{l.count} {l.unit}</td></tr>)}</tbody></table>
  </>);
}

const DIR_LABEL = (p: string) => `fin.pattern.${p}`;
export function FloorSheet({ snap, fl, mc, box, widthMm, heightMm, mm, t, units, locale }: { snap: Snapshot; fl: Floor; mc: MaterialsCatalog; box: Box; widthMm: number; heightMm: number; mm: number; t: Translate; units: Units; locale: Lang }){
  const rooms = floorPlan(snap, mc, fl), fs = (v: number) => v * mm;
  return (<>
    <svg viewBox={`${box.x0} ${box.z0} ${box.w} ${box.d}`} width={`${widthMm}mm`} height={`${heightMm}mm`} role="img" aria-label={t('draw.floor')} style={{ background: '#fff' }}>
      <defs>{rooms.map(f => { const L = f.pieceCm[0] / 100, W = f.pieceCm[1] / 100, rot = (f.pattern === 'diagonal' ? 45 : 0) + f.angle, stroke = f.kind === 'tile' ? '#9a958c' : '#b59a78', sw = fs(.12), id = `pat-${f.roomId}`;
        if (f.pattern === 'herringbone' || f.pattern === 'chevron'){ const a = L * Math.SQRT1_2, b = W * Math.SQRT2;
          return <pattern key={id} id={id} width={2 * a} height={b} patternUnits="userSpaceOnUse" patternTransform={`rotate(${f.angle})`}><path d={`M0 0L${a} ${a}L${2 * a} 0M0 ${b}L${a} ${a + b}L${2 * a} ${b}M${a} ${-b + a}L${a} ${a + b}`} fill="none" stroke={stroke} strokeWidth={sw} /></pattern>; }
        const shift = f.pattern === 'brick' ? .5 : f.pattern === 'third' ? 1 / 3 : 0, rows = shift === 0 ? 1 : Math.round(1 / shift);
        return <pattern key={id} id={id} width={L} height={W * rows} patternUnits="userSpaceOnUse" patternTransform={`rotate(${rot})`}>
          {Array.from({ length: rows }, (_, k) => { const off = ((k * shift) % 1) * L, y = k * W; return <path key={k} d={`M0 ${y}L${L} ${y}M${off} ${y}L${off} ${y + W}`} fill="none" stroke={stroke} strokeWidth={sw} />; })}
          {f.pattern === 'checker' && <rect x={0} y={0} width={L / 2} height={W} fill="#00000010" />}</pattern>; })}</defs>
      {rooms.map(f => { const q = f.rect, cx = (q.x0 + q.x1) / 2, cz = (q.z0 + q.z1) / 2, along = f.angle === 90 ? [0, 1] : [1, 0], al = Math.min(.6, (q.x1 - q.x0) / 4);
        return <g key={f.roomId}><rect x={q.x0} y={q.z0} width={q.x1 - q.x0} height={q.z1 - q.z0} fill={`${f.color}55`} /><rect x={q.x0} y={q.z0} width={q.x1 - q.x0} height={q.z1 - q.z0} fill={`url(#pat-${f.roomId})`} stroke="#c9c4b8" strokeWidth={fs(.2)} />
          <g stroke={INK} strokeWidth={fs(.35)} fill={INK}><line x1={cx - along[0]! * al} y1={cz + fs(9) - along[1]! * al} x2={cx + along[0]! * al} y2={cz + fs(9) + along[1]! * al} />
            <path d={`M${cx + along[0]! * al} ${cz + fs(9) + along[1]! * al}l${-along[0]! * fs(1.6) - along[1]! * fs(.9)} ${-along[1]! * fs(1.6) - along[0]! * fs(.9)}l${along[1]! * fs(1.8)} ${along[0]! * fs(1.8)}z`} /></g>
          <text x={cx} y={cz - fs(4)} textAnchor="middle" fontSize={fs(2.6)} fill={INK} stroke="#fff" strokeWidth={fs(.9)} paintOrder="stroke">{f.name} · {formatArea(f.areaM2, units, locale)}</text>
          <text x={cx} y={cz - fs(1)} textAnchor="middle" fontSize={fs(2)} fill="#333" stroke="#fff" strokeWidth={fs(.9)} paintOrder="stroke">{f.name2.length > 48 ? f.name2.slice(0, 47) + '…' : f.name2}</text>
          <text x={cx} y={cz + fs(2)} textAnchor="middle" fontSize={fs(2)} fill="#333" stroke="#fff" strokeWidth={fs(.9)} paintOrder="stroke">{t(DIR_LABEL(f.pattern))} · {f.pieceCm[0]}×{f.pieceCm[1]} cm{f.groutMm != null ? ` · ${t('draw.grout', { mm: f.groutMm })}` : ''}</text></g>; })}
      <Walls fl={fl} />
    </svg>
    <p className="muted">{t('draw.floorNote')}</p>
  </>);
}

const ELEV_SCALE = 50;  // 1:50
const BAND_FILL: Record<string, string> = { walltile: '#dfe6ea', tile: '#dfe6ea', paint: '#efe9df', panel: '#e9e1d2', rail: '#8a7a64', wallpaper: '#e8e2f0', slats: '#d8c8ae', plaster: '#e3ddd3', brick: '#c98f74', stone: '#d6d0c4' };
export function ElevationSheet({ elevs, t, units, locale }: { elevs: Elevation[]; t: Translate; units: Units; locale: Lang }){
  return (<div className="print-elevs">{elevs.map(e => { const k = 1000 / ELEV_SCALE, W = e.lengthM, H = e.heightM, pad = .35, fsz = .09;
    if (W < .05) return null;
    return (<figure key={e.side} className="print-elev"><figcaption>{t('draw.wall', { side: e.side })} · {formatLength(W, units, locale)} × {formatLength(H, units, locale)}</figcaption>
      <svg viewBox={`${-pad} ${-pad} ${W + 2 * pad} ${H + 2 * pad}`} width={`${(W + 2 * pad) * k}mm`} height={`${(H + 2 * pad) * k}mm`} role="img" aria-label={`${t('draw.wall', { side: e.side })}`}>
        <g transform={`translate(0 ${H}) scale(1 -1)`}>
          <rect x={0} y={0} width={W} height={H} fill="#faf9f6" stroke={INK} strokeWidth={.02} />
          {e.bands.map((b, i) => <rect key={i} x={0} y={b.y0} width={W} height={Math.max(.01, b.y1 - b.y0)} fill={b.color ?? BAND_FILL[b.kind] ?? '#eee'} fillOpacity={.85} stroke="#8a857a" strokeWidth={.006} />)}
          {e.baseboardM && <rect x={0} y={0} width={W} height={e.baseboardM} fill="#fff" stroke="#8a857a" strokeWidth={.006} />}
          {e.ceilingDropM > 0 && <line x1={0} y1={H - e.ceilingDropM} x2={W} y2={H - e.ceilingDropM} stroke={INK} strokeWidth={.01} strokeDasharray=".06 .04" />}
          {e.openings.map((o, i) => <g key={i}><rect x={o.u0} y={o.y0} width={o.u1 - o.u0} height={o.y1 - o.y0} fill="#fff" stroke={INK} strokeWidth={.015} />
            {o.kind === 'window' ? <path d={`M${(o.u0 + o.u1) / 2} ${o.y0}L${(o.u0 + o.u1) / 2} ${o.y1}`} stroke="#5b8fb9" strokeWidth={.01} /> : <path d={`M${o.u0} ${o.y0}L${o.u1} ${(o.y0 + o.y1) / 2}L${o.u0} ${o.y1}`} fill="none" stroke="#999" strokeWidth={.008} strokeDasharray=".04 .03" />}</g>)}
          {e.sconces.map((s, i) => <circle key={i} cx={s.u} cy={s.y} r={.07} fill="#fff" stroke={LIGHT} strokeWidth={.015} />)}
        </g>
        {/* cote: lungimea jos, înălțimea în dreapta, marginile benzilor și ale golurilor */}
        <g fontSize={fsz} fill={INK} stroke="none">
          <line x1={0} y1={H + .15} x2={W} y2={H + .15} stroke={INK} strokeWidth={.006} /><text x={W / 2} y={H + .28} textAnchor="middle">{formatLength(W, units, locale)}</text>
          <line x1={W + .15} y1={0} x2={W + .15} y2={H} stroke={INK} strokeWidth={.006} /><text x={W + .2} y={H / 2} transform={`rotate(-90 ${W + .2} ${H / 2})`} textAnchor="middle" dy={-.03}>{formatLength(H, units, locale)}</text>
          {[...new Set(e.bands.flatMap(b => [b.y0, b.y1]).filter(y => y > .01 && y < H - .01).map(y => Math.round(y * 100) / 100))].sort((a, b) => a - b).filter((y, i, a) => i === 0 || y - a[i - 1]! > .08).map(y => <g key={y}><line x1={W} y1={H - y} x2={W + .1} y2={H - y} stroke={INK} strokeWidth={.006} /><text x={-.04} y={H - y + .03} textAnchor="end">{formatLength(y, units, locale)}</text></g>)}
          {e.openings.map((o, i) => <text key={i} x={(o.u0 + o.u1) / 2} y={H - o.y1 - .04} textAnchor="middle" fontSize={fsz * .85}>{formatLength(o.u1 - o.u0, units, locale)}{o.kind === 'window' ? ` · ${t('draw.sill')} ${formatLength(o.y0, units, locale)}` : ''}</text>)}
        </g>
      </svg>
      <ul className="print-elev-legend">{e.bands.map((b, i) => <li key={i}><i style={{ background: b.color ?? BAND_FILL[b.kind] ?? '#eee' }} />{t(b.kind === 'walltile' ? 'sched.el.walltile' : `fin.kind.${b.kind}`)} {formatLength(b.y0, units, locale)}–{formatLength(b.y1, units, locale)}: {b.name}{b.color ? ` (${b.color})` : ''}</li>)}
        {e.baseboardM ? <li><i style={{ background: '#fff' }} />{t('sched.el.baseboard')} {formatLength(e.baseboardM, units, locale)}</li> : null}
        {e.sconces.length > 0 && <li>{t('draw.sconces', { n: e.sconces.length, h: formatLength(e.sconces[0]!.y, units, locale) })}</li>}</ul>
    </figure>); })}</div>);
}
export const roomElevations = (snap: Snapshot, mc: MaterialsCatalog, cat: Catalog, fl: Floor, room: Room) => wallElevations(snap, mc, cat, fl, room);
