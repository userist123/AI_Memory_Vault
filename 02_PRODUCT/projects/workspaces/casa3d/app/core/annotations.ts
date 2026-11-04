// Adnotări de planșă: cote persistente (aliniate = oblice, sau ortogonale) și texte libere. Geometria e comună planului,
// planșei tipărite și DXF-ului; funcții pure, fără DOM. Unități: metri, z în jos ca în plan.
import type { Annotation, DimAnnotation, TextAnnotation } from './types';
import { PLAN_LIMITS } from './plan-limits';

type P = [number, number];
export interface DimGeometry { p1: P; p2: P; value: number; u: P; n: P; mid: P; /** unghiul textului, în grade, mereu lizibil (−90..90] */ rot: number }
/** Direcția măsurată: aliniată = de la a la b; ortogonală = axa dominantă (orizontală sau verticală). */
function axes(a: P, b: P, mode: DimAnnotation['mode']): { u: P; value: number } {
  const dx = b[0] - a[0], dz = b[1] - a[1];
  if (mode === 'ortho') return Math.abs(dx) >= Math.abs(dz) ? { u: [1, 0], value: Math.abs(dx) } : { u: [0, 1], value: Math.abs(dz) };
  const L = Math.hypot(dx, dz) || 1; return { u: [dx / L, dz / L], value: Math.hypot(dx, dz) };
}
/** Linia de cotă: capetele p1/p2 (la `offset` de a, pe normala n), valoarea, mijlocul și unghiul textului. */
export function dimGeometry(d: Pick<DimAnnotation, 'a' | 'b' | 'offset' | 'mode'>): DimGeometry {
  const { u, value } = axes(d.a, d.b, d.mode), n: P = [-u[1], u[0]], o = d.offset;
  // capetele: proiecțiile lui a și b pe direcția măsurată, mutate pe linia de cotă (prin a, decalată cu offset)
  const along = (q: P) => (q[0] - d.a[0]) * u[0] + (q[1] - d.a[1]) * u[1];
  const p1: P = [d.a[0] + n[0] * o, d.a[1] + n[1] * o], s2 = along(d.b), p2: P = [p1[0] + u[0] * s2, p1[1] + u[1] * s2];
  let rot = Math.atan2(u[1], u[0]) * 180 / Math.PI; if (rot > 90) rot -= 180; if (rot <= -90) rot += 180;
  return { p1, p2, value, u, n, mid: [(p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2], rot };
}
/** Decalajul liniei de cotă care trece prin punctul `pt` (al treilea clic). */
export function dimOffsetAt(a: P, b: P, mode: DimAnnotation['mode'], pt: P): number {
  const { u } = axes(a, b, mode); return Math.round(((pt[0] - a[0]) * -u[1] + (pt[1] - a[1]) * u[0]) * 1000) / 1000;
}
/** Înălțimea textului în metri de plan (se scalează cu planșa, ca orice element desenat). */
export const TEXT_SIZE_M: Record<NonNullable<TextAnnotation['size']>, number> = { s: .1, m: .15, l: .25 };
export const textHeight = (t: Pick<TextAnnotation, 'size'>) => TEXT_SIZE_M[t.size ?? 'm'];
/** O cotă e validă de la 1 cm (sub atât se refuză la desenare și pe server). */
export const dimOk = (a: P, b: P, mode?: DimAnnotation['mode']) => axes(a, b, mode).value >= PLAN_LIMITS.annotation.dimMinM;
/** Conturul aproximativ al adnotării (încadrare, Tab). */
export function annotationBox(an: Annotation): { x0: number; z0: number; x1: number; z1: number } {
  if (an.kind === 'dim'){ const g = dimGeometry(an), xs = [an.a[0], an.b[0], g.p1[0], g.p2[0]], zs = [an.a[1], an.b[1], g.p1[1], g.p2[1]];
    return { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) }; }
  const h = textHeight(an), w = h * .6 * an.text.length; return { x0: an.at[0], x1: an.at[0] + w, z0: an.at[1] - h, z1: an.at[1] };
}
export const annotationCenter = (an: Annotation): P => { const b = annotationBox(an); return [(b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2]; };
