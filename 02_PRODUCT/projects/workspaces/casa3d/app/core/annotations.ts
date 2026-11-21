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
  // textul (cu simbolul de nivel și cu vârful săgeții, dacă are): lățimea e estimată, ca înainte
  const h = textHeight(an), w = h * .6 * an.text.length, ta = textAnchorAt(an), end = textAnchor(an) === 'end';
  const xs = [end ? ta[0] - w : ta[0], end ? ta[0] : ta[0] + w, an.at[0]], zs = [ta[1] - h, ta[1], an.at[1]];
  if (an.symbol === 'level'){ const g = levelMarkGeometry(an); for (const q of g.tri){ xs.push(q[0]); zs.push(q[1]); } }
  if (an.leader){ xs.push(an.leader[0]); zs.push(an.leader[1]); }
  return { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) };
}
/** Sub atât (m) săgeata nu se desenează: vârful e practic pe text. */
export const LEADER_MIN_M = .02;
type Note = Pick<TextAnnotation, 'at' | 'size' | 'rotation' | 'leader' | 'symbol'>;
/** Săgeata cu notă: linia de la text (`at`) la vârf și triunghiul vârfului (vârf, două colțuri de bază). Capul are 80 % din
 *  înălțimea textului, dar cel mult jumătate din linie. `null` fără vârf sau când vârful e la sub 2 cm de text. */
export function leaderGeometry(an: Note): { from: P; tip: P; head: [P, P, P]; length: number } | null {
  if (!an.leader) return null; const [tx, tz] = an.leader, dx = tx - an.at[0], dz = tz - an.at[1], L = Math.hypot(dx, dz);
  if (!(L >= LEADER_MIN_M)) return null; const ux = dx / L, uz = dz / L, hl = Math.min(textHeight(an) * .8, L / 2), hw = hl * .3, bx = tx - ux * hl, bz = tz - uz * hl;
  return { from: [an.at[0], an.at[1]], tip: [tx, tz], head: [[tx, tz], [bx - uz * hw, bz + ux * hw], [bx + uz * hw, bz - ux * hw]], length: L };
}
/** De ce parte a punctului `at` stă textul: 'end' (textul se termină în `at`) când vârful săgeții e în direcția de scriere,
 *  ca linia să nu taie textul; altfel 'start'. Cota de nivel scrie mereu spre dreapta simbolului. */
export function textAnchor(an: Note): 'start' | 'end' {
  if (!an.leader || an.symbol === 'level') return 'start'; const r = (an.rotation ?? 0) * Math.PI / 180;
  return (an.leader[0] - an.at[0]) * Math.cos(r) + (an.leader[1] - an.at[1]) * Math.sin(r) > 0 ? 'end' : 'start';
}
/** Simbolul cotei de nivel, nerotit: triunghi cu vârful în jos, în `at` (punctul cotat), baza la 90 % din înălțimea textului
 *  deasupra; valoarea se scrie în dreapta lui, cu baza textului la nivelul vârfului (`textAt`). Rotația adnotării se aplică
 *  în jurul lui `at`, pe simbol și pe text împreună. */
export function levelMarkGeometry(an: Pick<TextAnnotation, 'at' | 'size'>): { tri: [P, P, P]; textAt: P } {
  const h = textHeight(an), [x, z] = an.at; return { tri: [[x, z], [x - h * .5, z - h * .9], [x + h * .5, z - h * .9]], textAt: [x + h * .75, z] };
}
/** Punctul de la care se scrie textul (înainte de rotația în jurul lui `at`): `at`, sau în dreapta simbolului de nivel. */
export const textAnchorAt = (an: Pick<TextAnnotation, 'at' | 'size' | 'symbol'>): P => an.symbol === 'level' ? levelMarkGeometry(an).textAt : [an.at[0], an.at[1]];
/** Punctul `q` rotit cu `deg` grade în jurul lui `c`, în sensul lui `rotate()` din SVG (z în jos). */
export function rotateAbout(q: P, c: P, deg: number): P {
  if (!deg) return [q[0], q[1]]; const r = deg * Math.PI / 180, x = q[0] - c[0], z = q[1] - c[1];
  return [c[0] + x * Math.cos(r) - z * Math.sin(r), c[1] + x * Math.sin(r) + z * Math.cos(r)];
}
/** Mută adnotarea cu (dx, dz): cota își mută ambele capete, textul își mută și vârful săgeții (rotunjit la mm). */
export function moveAnnotation(an: Annotation, from: Annotation, dx: number, dz: number): void {
  const mv = (q: P): P => [Math.round((q[0] + dx) * 1000) / 1000, Math.round((q[1] + dz) * 1000) / 1000];
  if (an.kind === 'dim' && from.kind === 'dim'){ an.a = mv(from.a); an.b = mv(from.b); }
  else if (an.kind === 'text' && from.kind === 'text'){ an.at = mv(from.at); if (from.leader) an.leader = mv(from.leader); }
}
export const annotationCenter = (an: Annotation): P => { const b = annotationBox(an); return [(b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2]; };
