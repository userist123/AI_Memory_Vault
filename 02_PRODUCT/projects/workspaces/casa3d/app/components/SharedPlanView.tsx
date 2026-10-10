'use client';
// Plan 2D read-only pentru linkurile de partajare: aceleași date (camere, pereți, goluri, amprente), fără unelte.
import type { Catalog, Snapshot } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve, furnitureTotal } from '@/core/catalog';
import { area } from '@/core/geometry';
import { formatArea, formatMoney, catalogCurrency } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { floors, levelView, stairGeometry, stairwells, SLAB } from '@/core/levels';

const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };

/** Planul unui nivel (vedere din core/levels.ts), cu scările lui și golurile scărilor de dedesubt. */
function LevelPlan({ snap, catalog, voids, aria }: { snap: Snapshot; catalog: Catalog; voids: { x0: number; z0: number; x1: number; z1: number }[]; aria: string }){
  const { lang, units } = usePrefs(), f = snap.floor;
  const xs = f.walls.flatMap(w => [w.a[0], w.b[0]]).concat(f.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = f.walls.flatMap(w => [w.a[1], w.b[1]]).concat(f.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  const b = xs.length ? { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) } : { x0: 0, x1: 6, z0: 0, z1: 5 };
  const pad = 0.8, vb = `${b.x0 - pad} ${b.z0 - pad} ${b.x1 - b.x0 + 2 * pad} ${b.z1 - b.z0 + 2 * pad}`;
  return (
    <svg viewBox={vb} role="img" aria-label={aria} style={{ width: '100%', height: 'auto', background: '#fbfaf7', border: '1px solid #ddd', borderRadius: 8 }}>
      {f.rooms.map(r => <g key={r.id}><rect x={r.rect.x0} y={r.rect.z0} width={r.rect.x1 - r.rect.x0} height={r.rect.z1 - r.rect.z0} fill={ROOM_FILL[r.type] || '#f3f1ec'} stroke="#c9c4b8" strokeWidth={0.02} />
</g>)}
      {f.walls.map(w => <line key={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#333" strokeWidth={w.thickness} strokeLinecap="square" />)}
      {f.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
        return w.openings.map(o => <line key={o.id} x1={w.a[0] + ux * o.offset} y1={w.a[1] + uz * o.offset} x2={w.a[0] + ux * (o.offset + o.width)} y2={w.a[1] + uz * (o.offset + o.width)} stroke={o.kind === 'door' ? '#B7791F' : '#5b8fb9'} strokeWidth={w.thickness + 0.02} />); })}
      {voids.map((v, k) => <path key={'v' + k} d={`M${v.x0} ${v.z0}H${v.x1}V${v.z1}H${v.x0}ZM${v.x0} ${v.z0}L${v.x1} ${v.z1}M${v.x1} ${v.z0}L${v.x0} ${v.z1}`} fill="#fff" stroke="#777" strokeWidth={0.015} strokeDasharray="0.08 0.05" />)}
      {(f.stairs ?? []).map(st => { const g = stairGeometry(st, f.ceilingHeight + SLAB), r = g.rect, [dx, dz] = g.dir, px = -dz, pz = dx, hw = st.width / 2;
        const treads = Array.from({ length: g.steps - 1 }, (_, k) => { const cx = g.bottom[0] + dx * g.going * (k + 1), cz = g.bottom[1] + dz * g.going * (k + 1); return `M${cx - px * hw} ${cz - pz * hw}L${cx + px * hw} ${cz + pz * hw}`; }).join('');
        return <g key={st.id} stroke="#555" fill="none"><rect x={r.x0} y={r.z0} width={r.x1 - r.x0} height={r.z1 - r.z0} fill="#efe9de" strokeWidth={0.02} /><path d={treads} strokeWidth={0.01} /><path d={`M${g.bottom[0]} ${g.bottom[1]}L${g.top[0]} ${g.top[1]}`} strokeWidth={0.02} /></g>; })}
      {snap.placements.map(p => { const fp = footprintOf(catalog, p); if (!fp) return null; const rv = resolve(catalog, p.variantId);
        return <g key={p.id}><rect x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill="#ffffffcc" stroke="#1F4E79" strokeWidth={0.02} rx={0.03} />
          <title>{rv?.product.name ?? p.group}</title>
          {(fp.x1 - fp.x0) > 0.45 && <text x={(fp.x0 + fp.x1) / 2} y={(fp.z0 + fp.z1) / 2 + 0.04} fontSize={Math.min(0.13, (fp.x1 - fp.x0) / 6)} textAnchor="middle" fill="#1F4E79">{(rv?.product.name ?? p.group).split(' ')[0]}</text>}</g>; })}
      {/* etichetele camerelor se desenează ultimele, peste mobilă, cu contur alb ca să rămână lizibile */}
      {f.rooms.map(r => <g key={'l' + r.id}>
        <text x={r.rect.x0 + 0.18} y={r.rect.z1 - 0.2} fontSize={0.2} fill="#444" stroke="#fbfaf7" strokeWidth={0.07} strokeLinejoin="round" paintOrder="stroke">{r.name} · {formatArea(area(r.rect), units, lang)}</text></g>)}
    </svg>);
}

export default function SharedPlanView({ snap, catalog }: { snap: Snapshot; catalog: Catalog }){
  const { t, tp, lang } = usePrefs(), cur = catalogCurrency(catalog);
  // câte un plan pe nivel; lista și sumarul cuprind toată casa
  const views = floors(snap).map((_, i) => levelView(snap, i)), multi = views.length > 1, rooms = views.flatMap(v => v.floor.rooms);
  const name = (i: number) => i === 0 ? t('level.ground') : views[i]!.floor.name || t('level.n', { n: i });
  // Prețul lipsă rămâne necunoscut (nu 0). Prețurile sunt cele din catalogul curent, nu cele de la data reviziei.
  const { known: total, unknown } = furnitureTotal(catalog, snap.placements);
  return (<div>
    {views.map((v, i) => <div key={v.floor.id} style={{ marginBottom: multi ? 14 : 0 }}>{multi && <h3 style={{ margin: '0 0 6px' }}>{name(i)}</h3>}
      <LevelPlan snap={v} catalog={catalog} voids={stairwells(snap, i)} aria={multi ? `${t('share.planAria')} · ${name(i)}` : t('share.planAria')} /></div>)}
    {/* numele complete stau în listă, nu pe plan, ca să nu se suprapună */}
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: '6px 18px', marginTop: 12, fontSize: 13 }}>
      {rooms.map(r => { const items = snap.placements.filter(p => p.roomId === r.id); if (!items.length) return null;
        return <div key={r.id}><b>{r.name}</b><ul style={{ margin: '2px 0 0', paddingLeft: 18 }}>{items.map(p => { const rv = resolve(catalog, p.variantId), price = rv?.offer?.price;
          return <li key={p.id}>{rv?.product.name ?? p.group} <span className="muted">{typeof price === 'number' ? formatMoney(price, cur, lang) : t('common.unknownPrice')}</span></li>; })}</ul></div>; })}
    </div>
    <div className="prov" style={{ marginTop: 8 }}>{tp('editor.summaryRooms', rooms.length)} · {tp('editor.summaryPieces', snap.placements.length)} · {t('editor.summaryFurniture', { total: formatMoney(total, cur, lang) })}{unknown ? ` + ${tp('share.unknownPieces', unknown)}` : ''} {t('share.pricesNote')}</div>
  </div>);
}
