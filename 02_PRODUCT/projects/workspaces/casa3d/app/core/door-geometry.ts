// Geometria ușii pe perete, fără dependențe (o citesc și validate.ts, și planul, și motorul 3D): tipul, sensul de deschidere,
// peretele liber de lângă gol și zona în care parchează o foaie glisantă.
import type { DoorType, Floor, Opening } from './types';
import { DOOR_SLIDE_EXTRA_M } from './rules';

/** Tipul ușii unui gol: lipsă = batantă; golul de trecere și fereastra nu au tip (null). */
export const doorTypeOf = (o: Pick<Opening, 'kind' | 'passage' | 'doorType'>): DoorType | null => o.kind !== 'door' || o.passage ? null : o.doorType ?? 'swing';
/** Glisantele (pe perete și în perete) au nevoie de perete liber lângă gol, de partea unde parchează foaia. */
export const isSlidingType = (t: DoorType | null | undefined) => t === 'sliding' || t === 'pocket';
export interface SlideZone { ax: number; az: number; ux: number; uz: number; t0: number; t1: number; depth: number }
/** Zona de perete în care parchează foaia unei uși glisante: de la marginea golului spre `hinge`, cât foaia + 10 cm; adâncimea = jumătate de perete + 15 cm. */
export function slideZone(fl: Floor, wi: number, oi: number): SlideZone | null {
  const w = fl.walls[wi], o = w?.openings[oi]; if (!w || !o) return null; const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, sw = doorSwing(fl, wi, oi), len = o.width + DOOR_SLIDE_EXTRA_M;
  const [t0, t1] = sw.hinge === 'start' ? [o.offset - len, o.offset] : [o.offset + o.width, o.offset + o.width + len];
  return { ax: w.a[0], az: w.a[1], ux: (w.b[0] - w.a[0]) / L, uz: (w.b[1] - w.a[1]) / L, t0, t1, depth: w.thickness / 2 + .15 };
}
export const inSlideZone = (z: SlideZone, x: number, y: number) => { const dx = x - z.ax, dz = y - z.az, t = dx * z.ux + dz * z.uz, n = Math.abs(-dx * z.uz + dz * z.ux); return t >= z.t0 - 1e-6 && t <= z.t1 + 1e-6 && n <= z.depth; };
/** Lungimea de perete liber de o parte și de alta a golului: până la capătul peretelui, la alt gol sau la un perete care se prinde în el
 *  (minus jumătate din grosimea lui). `side` = capătul cu mai mult loc, unde parchează implicit o ușă glisantă. */
export function slideSide(fl: Floor, wi: number, oi: number): { start: number; end: number; side: 'start' | 'end' } {
  const w = fl.walls[wi]!, o = w.openings[oi]!, L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
  const a = o.offset, b = o.offset + o.width; let lo = 0, hi = L;
  for (const q of w.openings) if (q !== o){ if (q.offset + q.width <= a + 1e-6) lo = Math.max(lo, q.offset + q.width); else if (q.offset >= b - 1e-6) hi = Math.min(hi, q.offset); else { lo = Math.max(lo, a); hi = Math.min(hi, b); } }
  fl.walls.forEach((v, vi) => { if (vi === wi) return; for (const p of [v.a, v.b]){ const dx = p[0] - w.a[0], dz = p[1] - w.a[1], t = dx * ux + dz * uz, n = Math.abs(-dx * uz + dz * ux);
    if (n > w.thickness / 2 + 1e-3 || t < -v.thickness || t > L + v.thickness) continue; const h = v.thickness / 2;
    if (t <= a) lo = Math.max(lo, t + h); else if (t >= b) hi = Math.min(hi, t - h); } });
  const start = Math.max(0, a - lo), end = Math.max(0, hi - b); return { start, end, side: end > start + 1e-6 ? 'end' : 'start' };
}

// ---------- sensul de deschidere ----------
export interface DoorSwing { hinge: 'start' | 'end'; swing: 1 | -1; auto: boolean }
/** Cum se deschide o ușă: ce aleg utilizatorul, altfel regula de practică — ușa de intrare se deschide spre interior,
 *  ușile de cameră se deschid în cameră (nu în hol), iar balamaua stă spre colțul cel mai apropiat, ca foaia să se așeze lângă perete. */
export function doorSwing(fl: Floor, wallIndex: number, openingIndex: number): DoorSwing {
  const w = fl.walls[wallIndex]!, o = w.openings[openingIndex]!, L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, nx = -uz, nz = ux;
  const m = o.offset + o.width / 2, mx = w.a[0] + ux * m, mz = w.a[1] + uz * m, probe = (sg: number) => { const x = mx + nx * sg * .3, z = mz + nz * sg * .3;
    return fl.rooms.find(r => x > r.rect.x0 && x < r.rect.x1 && z > r.rect.z0 && z < r.rect.z1) ?? null; };
  const A = probe(1), B = probe(-1);
  let swing: 1 | -1;
  if (o.entrance || !A || !B) swing = A ? 1 : -1;                       // intrarea (sau un perete exterior): spre interior
  else if (A.type === 'hol' && B.type !== 'hol') swing = -1;             // din hol spre cameră
  else if (B.type === 'hol' && A.type !== 'hol') swing = 1;
  else { const ar = (r: typeof A) => (r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0); swing = ar(A) >= ar(B) ? 1 : -1; }  // între două camere: spre cea mai mare
  // balamaua: capătul golului mai aproape de colțul camerei în care se deschide (foaia se sprijină de perete)
  const room = swing === 1 ? A : B; let hinge: 'start' | 'end' = 'start';
  if (room){ const lo = Math.abs(ux) > .5 ? room.rect.x0 : room.rect.z0, hi = Math.abs(ux) > .5 ? room.rect.x1 : room.rect.z1, along = (t: number) => Math.abs(ux) > .5 ? w.a[0] + ux * t : w.a[1] + uz * t;
    const p0 = along(o.offset), p1 = along(o.offset + o.width), d0 = Math.min(Math.abs(p0 - lo), Math.abs(p0 - hi)), d1 = Math.min(Math.abs(p1 - lo), Math.abs(p1 - hi)); hinge = d1 < d0 ? 'end' : 'start'; }
  // glisantele parchează foaia spre capătul cu mai mult perete liber (până la colț sau la alt gol)
  if (o.hinge == null && isSlidingType(doorTypeOf(o))) hinge = slideSide(fl, wallIndex, openingIndex).side;
  return { hinge: o.hinge ?? hinge, swing: o.swing ?? swing, auto: o.hinge == null && o.swing == null };
}
