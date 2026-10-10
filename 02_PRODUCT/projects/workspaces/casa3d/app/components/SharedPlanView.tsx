// Plan 2D read-only pentru linkurile de partajare: aceleași date (camere, pereți, goluri, amprente), fără unelte.
import type { Catalog, Snapshot } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve } from '@/core/catalog';
import { area } from '@/core/geometry';

const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };

export default function SharedPlanView({ snap, catalog }: { snap: Snapshot; catalog: Catalog }){
  const f = snap.floor;
  const xs = f.walls.flatMap(w => [w.a[0], w.b[0]]).concat(f.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = f.walls.flatMap(w => [w.a[1], w.b[1]]).concat(f.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  const b = xs.length ? { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) } : { x0: 0, x1: 6, z0: 0, z1: 5 };
  const pad = 0.8, vb = `${b.x0 - pad} ${b.z0 - pad} ${b.x1 - b.x0 + 2 * pad} ${b.z1 - b.z0 + 2 * pad}`;
  const total = snap.placements.reduce((a, p) => a + (resolve(catalog, p.variantId)?.offer?.price || 0), 0);
  return (<div>
    <svg viewBox={vb} role="img" aria-label="Planul locuinței, doar vizualizare" style={{ width: '100%', height: 'auto', background: '#fbfaf7', border: '1px solid #ddd', borderRadius: 8 }}>
      {f.rooms.map(r => <g key={r.id}><rect x={r.rect.x0} y={r.rect.z0} width={r.rect.x1 - r.rect.x0} height={r.rect.z1 - r.rect.z0} fill={ROOM_FILL[r.type] || '#f3f1ec'} stroke="#c9c4b8" strokeWidth={0.02} />
        <text x={(r.rect.x0 + r.rect.x1) / 2} y={r.rect.z0 + 0.35} fontSize={0.26} textAnchor="middle" fill="#444">{r.name} · {area(r.rect).toFixed(1)} m²</text></g>)}
      {f.walls.map(w => <line key={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#333" strokeWidth={w.thickness} strokeLinecap="square" />)}
      {f.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
        return w.openings.map(o => <line key={o.id} x1={w.a[0] + ux * o.offset} y1={w.a[1] + uz * o.offset} x2={w.a[0] + ux * (o.offset + o.width)} y2={w.a[1] + uz * (o.offset + o.width)} stroke={o.kind === 'door' ? '#B7791F' : '#5b8fb9'} strokeWidth={w.thickness + 0.02} />); })}
      {snap.placements.map(p => { const fp = footprintOf(catalog, p); if (!fp) return null; const rv = resolve(catalog, p.variantId);
        return <g key={p.id}><rect x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill="#ffffffcc" stroke="#1F4E79" strokeWidth={0.02} rx={0.03} />
          <text x={(fp.x0 + fp.x1) / 2} y={(fp.z0 + fp.z1) / 2 + 0.06} fontSize={0.16} textAnchor="middle" fill="#1F4E79">{rv?.product.name ?? p.group}</text></g>; })}
    </svg>
    <div className="prov" style={{ marginTop: 8 }}>{f.rooms.length} încăperi · {snap.placements.length} piese · mobilier {Math.round(total).toLocaleString('ro-RO')} lei (prețuri de catalog la data verificării)</div>
  </div>);
}
