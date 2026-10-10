// Plan 2D read-only pentru linkurile de partajare: aceleași date (camere, pereți, goluri, amprente), fără unelte.
import type { Catalog, Snapshot } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve, furnitureTotal } from '@/core/catalog';
import { area } from '@/core/geometry';

const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };

export default function SharedPlanView({ snap, catalog }: { snap: Snapshot; catalog: Catalog }){
  const f = snap.floor;
  const xs = f.walls.flatMap(w => [w.a[0], w.b[0]]).concat(f.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = f.walls.flatMap(w => [w.a[1], w.b[1]]).concat(f.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  const b = xs.length ? { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) } : { x0: 0, x1: 6, z0: 0, z1: 5 };
  const pad = 0.8, vb = `${b.x0 - pad} ${b.z0 - pad} ${b.x1 - b.x0 + 2 * pad} ${b.z1 - b.z0 + 2 * pad}`;
  // Prețul lipsă rămâne necunoscut (nu 0). Prețurile sunt cele din catalogul curent, nu cele de la data reviziei.
  const { known: total, unknown } = furnitureTotal(catalog, snap.placements);
  return (<div>
    <svg viewBox={vb} role="img" aria-label="Planul locuinței, doar vizualizare" style={{ width: '100%', height: 'auto', background: '#fbfaf7', border: '1px solid #ddd', borderRadius: 8 }}>
      {f.rooms.map(r => <g key={r.id}><rect x={r.rect.x0} y={r.rect.z0} width={r.rect.x1 - r.rect.x0} height={r.rect.z1 - r.rect.z0} fill={ROOM_FILL[r.type] || '#f3f1ec'} stroke="#c9c4b8" strokeWidth={0.02} />
</g>)}
      {f.walls.map(w => <line key={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#333" strokeWidth={w.thickness} strokeLinecap="square" />)}
      {f.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
        return w.openings.map(o => <line key={o.id} x1={w.a[0] + ux * o.offset} y1={w.a[1] + uz * o.offset} x2={w.a[0] + ux * (o.offset + o.width)} y2={w.a[1] + uz * (o.offset + o.width)} stroke={o.kind === 'door' ? '#B7791F' : '#5b8fb9'} strokeWidth={w.thickness + 0.02} />); })}
      {snap.placements.map(p => { const fp = footprintOf(catalog, p); if (!fp) return null; const rv = resolve(catalog, p.variantId);
        return <g key={p.id}><rect x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill="#ffffffcc" stroke="#1F4E79" strokeWidth={0.02} rx={0.03} />
          <title>{rv?.product.name ?? p.group}</title>
          {(fp.x1 - fp.x0) > 0.45 && <text x={(fp.x0 + fp.x1) / 2} y={(fp.z0 + fp.z1) / 2 + 0.04} fontSize={Math.min(0.13, (fp.x1 - fp.x0) / 6)} textAnchor="middle" fill="#1F4E79">{(rv?.product.name ?? p.group).split(' ')[0]}</text>}</g>; })}
      {/* etichetele camerelor se desenează ultimele, peste mobilă, cu contur alb ca să rămână lizibile */}
      {f.rooms.map(r => <g key={'l' + r.id}>
        <text x={r.rect.x0 + 0.18} y={r.rect.z1 - 0.2} fontSize={0.2} fill="#444" stroke="#fbfaf7" strokeWidth={0.07} strokeLinejoin="round" paintOrder="stroke">{r.name} · {area(r.rect).toFixed(1)} m²</text></g>)}
    </svg>
    {/* numele complete stau în listă, nu pe plan, ca să nu se suprapună */}
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: '6px 18px', marginTop: 12, fontSize: 13 }}>
      {f.rooms.map(r => { const items = snap.placements.filter(p => p.roomId === r.id); if (!items.length) return null;
        return <div key={r.id}><b>{r.name}</b><ul style={{ margin: '2px 0 0', paddingLeft: 18 }}>{items.map(p => { const rv = resolve(catalog, p.variantId), price = rv?.offer?.price;
          return <li key={p.id}>{rv?.product.name ?? p.group} <span className="muted">{typeof price === 'number' ? `${Math.round(price).toLocaleString('ro-RO')} lei` : 'preț necunoscut'}</span></li>; })}</ul></div>; })}
    </div>
    <div className="prov" style={{ marginTop: 8 }}>{f.rooms.length} încăperi · {snap.placements.length} piese · mobilier {Math.round(total).toLocaleString('ro-RO')} lei{unknown ? ` + ${unknown} ${unknown === 1 ? 'piesă' : 'piese'} cu preț necunoscut` : ''} (prețuri din catalogul curent, cu data verificării din catalog; nu sunt înghețate odată cu revizia)</div>
  </div>);
}
