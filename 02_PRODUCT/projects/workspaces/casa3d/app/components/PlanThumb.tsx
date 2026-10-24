// Miniatura unui plan (camere colorate pe tip, pereți), pentru panoul de proiecte. Fără interacțiune.
import type { Floor } from '@/core/types';

const FILL: Record<string, string> = { baie: '#dfe3df', bucatarie: '#e6e3da', hol: '#efe9df', living: '#ece2d3', dormitor: '#e9ded2' };
export default function PlanThumb({ floor, label }: { floor: Floor; label: string }){
  const xs = floor.walls.flatMap(w => [w.a[0], w.b[0]]).concat(floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = floor.walls.flatMap(w => [w.a[1], w.b[1]]).concat(floor.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  if (!xs.length) return <div className="thumb empty" role="img" aria-label={label} />;
  const x0 = Math.min(...xs) - .4, z0 = Math.min(...zs) - .4, w = Math.max(...xs) + .4 - x0, h = Math.max(...zs) + .4 - z0;
  return (<svg className="thumb" viewBox={`${x0} ${z0} ${w} ${h}`} preserveAspectRatio="xMidYMid meet" role="img" aria-label={label}>
    {floor.rooms.map(r => <rect key={r.id} x={r.rect.x0} y={r.rect.z0} width={r.rect.x1 - r.rect.x0} height={r.rect.z1 - r.rect.z0} fill={FILL[r.type] ?? '#eee8de'} />)}
    {floor.walls.map(wl => <line key={wl.id} x1={wl.a[0]} y1={wl.a[1]} x2={wl.b[0]} y2={wl.b[1]} stroke="#2a2622" strokeWidth={wl.exterior ? .22 : .1} strokeLinecap="square" />)}
  </svg>);
}
