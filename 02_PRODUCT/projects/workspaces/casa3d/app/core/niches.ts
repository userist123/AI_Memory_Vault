// Nișe încastrate în perete (duș, TV, bibliotecă): geometria în plan, camera în care dau, validările și suprafața de
// placat. Funcții pure; planul, 3D-ul, DXF-ul, devizul și serverul citesc de aici.
import type { Floor, Issue, Niche, Room, Wall } from './types';

export interface NicheGeometry { wall: Wall; L: number; ux: number; uz: number; nx: number; nz: number; s0: number; s1: number;
  /** scobitura în grosimea peretelui, pe fața nișei: [fața 0, fața 1, fundul 1, fundul 0] */ quad: [number, number][]; center: [number, number] }
/** Geometria nișei pe peretele ei, sau null dacă peretele nu (mai) există. `nx/nz` = normala feței nișei (spre cameră). */
export function nicheGeometry(floor: Pick<Floor, 'walls'>, n: Niche): NicheGeometry | null {
  const w = floor.walls.find(x => x.id === n.wallId); if (!w) return null;
  const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]); if (L < .01) return null;
  const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, sg = n.side ?? 1, nx = -uz * sg, nz = ux * sg, s0 = n.offset, s1 = n.offset + n.width, h = w.thickness / 2;
  const P = (s: number, d: number): [number, number] => [w.a[0] + ux * s + nx * d, w.a[1] + uz * s + nz * d];
  const dIn = Math.max(-h, h - n.depth);
  return { wall: w, L, ux, uz, nx, nz, s0, s1, quad: [P(s0, h), P(s1, h), P(s1, dIn), P(s0, dIn)], center: P((s0 + s1) / 2, h) };
}
/** Camera în care se deschide nișa (prima cameră din fața ei), sau null. */
export function nicheRoom(floor: Pick<Floor, 'walls' | 'rooms'>, n: Niche): Room | null {
  const g = nicheGeometry(floor, n); if (!g) return null; const x = g.center[0] + g.nx * .15, z = g.center[1] + g.nz * .15;
  return floor.rooms.find(r => x > r.rect.x0 && x < r.rect.x1 && z > r.rect.z0 && z < r.rect.z1) ?? null;
}
/** Partea peretelui pe care a dat clic utilizatorul: +1 = spre normala (-uz, ux), −1 = invers. */
export function nicheSide(w: Pick<Wall, 'a' | 'b'>, pt: [number, number]): 1 | -1 {
  const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
  return (pt[0] - w.a[0]) * -uz + (pt[1] - w.a[1]) * ux < 0 ? -1 : 1;
}
/** Suprafața de placat a nișei (fundul + cele patru fețe interioare), m². */
export const nicheTileArea = (n: Pick<Niche, 'width' | 'height' | 'depth'>) => n.width * n.height + 2 * (n.width + n.height) * n.depth;
/** Golul pe verticală: ușa de la pardoseală, fereastra de la parapet (aceleași valori implicite ca în 3D). */
const openingSpan = (o: Wall['openings'][number]): [number, number] => o.kind === 'door' ? [0, o.height ?? 2.1] : [o.sill ?? .9, (o.sill ?? .9) + (o.height ?? 1.3)];

/** Nișa trebuie să stea în perete și sub tavan, să nu fie mai adâncă decât peretele (minus 2 cm) și să nu taie un gol. */
export function nicheIssues(floor: Floor): Issue[] {
  const out: Issue[] = [];
  for (const n of floor.niches ?? []){ const g = nicheGeometry(floor, n); if (!g) continue; const w = g.wall;
    if (n.offset < -1e-6 || n.offset + n.width > g.L + 1e-6 || n.elevation < 0 || n.elevation + n.height > floor.ceilingHeight + 1e-6)
      out.push({ code: 'NICHE_OUTSIDE_WALL', severity: 'ERROR', message: `Nișa ${n.id} iese din peretele ${w.id} sau trece de tavan.`, key: 'issue.NICHE_OUTSIDE_WALL', vars: { niche: n.id } });
    if (n.depth >= w.thickness - .02 - 1e-9)
      out.push({ code: 'NICHE_TOO_DEEP', severity: w.exterior ? 'ERROR' : 'WARNING', message: `Nișa ${n.id} are ${Math.round(n.depth * 100)} cm adâncime într-un perete de ${Math.round(w.thickness * 100)} cm.`, key: 'issue.NICHE_TOO_DEEP', vars: { niche: n.id, d: Math.round(n.depth * 100), t: Math.round(w.thickness * 100) } });
    if (w.openings.some(o => { const [y0, y1] = openingSpan(o); return Math.min(o.offset + o.width, n.offset + n.width) - Math.max(o.offset, n.offset) > .005 && Math.min(y1, n.elevation + n.height) - Math.max(y0, n.elevation) > .005; }))
      out.push({ code: 'NICHE_OVER_OPENING', severity: 'ERROR', message: `Nișa ${n.id} se suprapune cu un gol din perete.`, key: 'issue.NICHE_OVER_OPENING', vars: { niche: n.id } }); }
  return out;
}
