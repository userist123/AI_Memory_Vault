// Adnotări de planșă: cote persistente (aliniate = oblice, sau ortogonale) și texte libere. Geometria e comună planului,
// planșei tipărite și DXF-ului; funcții pure, fără DOM. Unități: metri, z în jos ca în plan.
import type { Annotation, AngleAnnotation, CloudAnnotation, DimAnnotation, TextAnnotation } from './types';
import { PLAN_LIMITS, armsAngleDeg, ANGLE_MIN_DEG } from './plan-limits';

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
  if (an.kind === 'angle'){ const g = angleGeometry(an), lb = angleLabelAt(an, ANGLE_TEXT_M), pts = [an.v, g.arms[0][1], g.arms[1][1], ...angleArcPoints(an)], xs = pts.map(q => q[0]), zs = pts.map(q => q[1]);
    xs.push(lb[0] - ANGLE_TEXT_M * 1.5, lb[0] + ANGLE_TEXT_M * 1.5); zs.push(lb[1] - ANGLE_TEXT_M / 2, lb[1] + ANGLE_TEXT_M / 2);
    return { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) }; }
  if (an.kind === 'cloud'){ const r = an.rect, s = cloudBulge(r), lb = cloudLabelAt(r);
    return { x0: r.x0 - s, x1: Math.max(r.x1 + s, an.label ? lb[0] + CLOUD_LABEL_M * .6 * an.label.length : -Infinity), z0: an.label ? lb[1] - CLOUD_LABEL_M : r.z0 - s, z1: r.z1 + s }; }
  // textul (cu simbolul de nivel și cu vârful săgeții, dacă are): lățimea e estimată, ca înainte
  const h = textHeight(an), w = h * .6 * an.text.length, ta = textAnchorAt(an), end = textAnchor(an) === 'end';
  const xs = [end ? ta[0] - w : ta[0], end ? ta[0] : ta[0] + w, an.at[0]], zs = [ta[1] - h, ta[1], an.at[1]];
  if (an.symbol === 'level'){ const g = levelMarkGeometry(an); for (const q of g.outline){ xs.push(q[0]); zs.push(q[1]); } }
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
/** Simbolul cotei de nivel pe PLAN, nerotit: valoarea într-un dreptunghi (practica românească de desen tehnic, STAS 1434-83 —
 *  triunghiul e convenția secțiunilor și fațadelor; confirmat independent de două cercetări, 2026-11-21). `at` e colțul de jos
 *  al textului (linia de bază), cu o margine de 30 % din înălțime; lățimea textului e estimată ca la conturul adnotării.
 *  Rotația adnotării se aplică în jurul lui `at`, pe chenar și pe text împreună. */
export function levelMarkGeometry(an: Pick<TextAnnotation, 'at' | 'size'> & { text?: string }): { outline: [P, P, P, P]; textAt: P } {
  const h = textHeight(an), [x, z] = an.at, pad = h * .3, w = h * .6 * Math.max(1, (an.text ?? '').length);
  const x0 = x - pad, x1 = x + w + pad, z0 = z - h - pad, z1 = z + pad;
  return { outline: [[x0, z0], [x1, z0], [x1, z1], [x0, z1]], textAt: [x, z] };
}
/** Valoarea cotei de nivel în forma de planșă: metri cu două zecimale și semn (`+2,80`, `-0,15`, `±0,00`). Un text care nu
 *  e număr rămâne cum l-a scris omul; nimic nu se completează în locul lui. */
export function formatLevelValue(raw: string, decimal: ',' | '.' = ','): string {
  const t = raw.trim().replace(/^±/, '').replace(/\s+/g, ''), m = /^([+-]?)(\d+)(?:[.,](\d+))?$/.exec(t); if (!m) return raw.trim();
  const v = Number(`${m[1] === '-' ? '-' : ''}${m[2]}.${m[3] ?? '0'}`); if (!Number.isFinite(v) || Math.abs(v) >= 1000) return raw.trim();
  const r = Math.round(v * 100) / 100; if (r === 0) return `±0${decimal}00`;
  return `${r > 0 ? '+' : '-'}${Math.abs(r).toFixed(2).replace('.', decimal)}`;
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
  else if (an.kind === 'angle' && from.kind === 'angle'){ an.v = mv(from.v); an.a = mv(from.a); an.b = mv(from.b); }
  else if (an.kind === 'cloud' && from.kind === 'cloud'){ const [x0, z0] = mv([from.rect.x0, from.rect.z0]), [x1, z1] = mv([from.rect.x1, from.rect.z1]); an.rect = { x0, z0, x1, z1 }; }
}

// ---- Cota unghiulară ----
type Angle = Pick<AngleAnnotation, 'v' | 'a' | 'b' | 'r'>;
const f4 = (n: number) => Math.round(n * 1e4) / 1e4;
/** Înălțimea valorii unghiului (m), pentru încadrare și planșă. */
export const ANGLE_TEXT_M = .15;
/** Brațele desenate depășesc arcul cu un sfert din rază, cel mult 15 cm. */
const armLength = (r: number) => r + Math.min(r * .25, .15);
export interface AngleGeometry {
  /** unghiul dintre brațe, în grade (0–180) */ deg: number;
  /** direcția brațului `a`, în radiani (atan2(dz, dx); z în jos, deci pozitiv = în sensul acelor de ceas pe ecran) */ start: number;
  /** unghiul cu semn de la brațul `a` la brațul `b`, pe drumul scurt, în radiani (−π, π]; pozitiv = în sensul acelor de ceas pe ecran */ sweep: number;
  /** capetele arcului (pe brațul `a`, pe brațul `b`) și mijlocul lui */ p1: P; p2: P; mid: P;
  /** cele două brațe scurte, de la vârf până puțin după arc */ arms: [[P, P], [P, P]];
  /** direcția bisectoarei (unitară), de la vârf spre mijlocul arcului */ bis: P;
}
/** Geometria cotei unghiulare: valoarea, sensul, capetele arcului și brațele. Se măsoară mereu unghiul mic (cel mult 180°). */
export function angleGeometry(an: Angle): AngleGeometry {
  const [vx, vz] = an.v, start = Math.atan2(an.a[1] - vz, an.a[0] - vx), end = Math.atan2(an.b[1] - vz, an.b[0] - vx);
  let sweep = end - start; while (sweep > Math.PI) sweep -= 2 * Math.PI; while (sweep <= -Math.PI) sweep += 2 * Math.PI;
  const at = (t: number, d: number): P => [vx + Math.cos(t) * d, vz + Math.sin(t) * d], L = armLength(an.r), tm = start + sweep / 2;
  return { deg: Math.abs(sweep) * 180 / Math.PI, start, sweep, p1: at(start, an.r), p2: at(start + sweep, an.r), mid: at(tm, an.r),
    arms: [[[vx, vz], at(start, L)], [[vx, vz], at(start + sweep, L)]], bis: [Math.cos(tm), Math.sin(tm)] };
}
/** Punctele arcului, de la brațul `a` la brațul `b` (un segment la cel mult `stepDeg` grade, cel puțin unul). */
export function angleArcPoints(an: Angle, stepDeg = 5): P[] {
  const g = angleGeometry(an), n = Math.max(1, Math.ceil(g.deg / stepDeg - 1e-9));
  return Array.from({ length: n + 1 }, (_, i): P => { const t = g.start + g.sweep * i / n; return [an.v[0] + Math.cos(t) * an.r, an.v[1] + Math.sin(t) * an.r]; });
}
/** Arcul ca atribut `d` de SVG: un singur arc de cerc, în sensul lui `sweep`. */
export function angleArcPath(an: Angle): string { const g = angleGeometry(an); return `M${f4(g.p1[0])} ${f4(g.p1[1])}A${f4(an.r)} ${f4(an.r)} 0 0 ${g.sweep > 0 ? 1 : 0} ${f4(g.p2[0])} ${f4(g.p2[1])}`; }
/** Centrul valorii: pe bisectoare, în afara arcului, la `gap` de el. */
export function angleLabelAt(an: Angle, gap: number): P { const g = angleGeometry(an), d = an.r + gap; return [an.v[0] + g.bis[0] * d, an.v[1] + g.bis[1] * d]; }
/** Valoarea în grade cu o zecimală: „90,0°” (virgulă în română, punct în engleză). */
export const formatAngle = (deg: number, decimal: ',' | '.' = ','): string => `${(Math.round(deg * 10) / 10).toFixed(1).replace('.', decimal)}°`;
/** Brațele unei cote unghiulare au cel puțin 1 cm fiecare (ca în sanitizator). */
export const angleOk = (v: P, a: P, b: P) => Math.hypot(a[0] - v[0], a[1] - v[1]) >= PLAN_LIMITS.annotation.dimMinM && Math.hypot(b[0] - v[0], b[1] - v[1]) >= PLAN_LIMITS.annotation.dimMinM && armsAngleDeg(v, a, b) >= ANGLE_MIN_DEG;
/** Raza arcului care trece prin punctul `pt` (al patrulea clic), adusă în interval și rotunjită la mm. */
export function angleRadiusAt(v: P, pt: P): number { const L = PLAN_LIMITS.annotation;
  return Math.round(Math.min(L.angleRadiusMaxM, Math.max(L.angleRadiusMinM, Math.hypot(pt[0] - v[0], pt[1] - v[1]))) * 1000) / 1000; }

// ---- Norul de revizie ----
type Rect = CloudAnnotation['rect'];
/** Coarda unui arc (m) cât timp norul e mic; peste CLOUD_MAX_ARCS arce coarda crește cu perimetrul, ca numărul lor să rămână limitat. */
export const CLOUD_ARC_M = .2, CLOUD_MAX_ARCS = 60;
/** Săgeata arcului ca fracțiune din coardă (arce sub un semicerc, bombate spre exterior). */
export const CLOUD_BULGE = .3;
/** Înălțimea etichetei de revizie (m). */
export const CLOUD_LABEL_M = .15;
/** Câte arce are norul pe latura orizontală, pe cea verticală și în total: după perimetru, cel puțin unul pe latură, cel mult CLOUD_MAX_ARCS. */
export function cloudArcCount(w: number, d: number): { nx: number; nz: number; total: number } {
  const chord = Math.max(CLOUD_ARC_M, 2 * (w + d) / CLOUD_MAX_ARCS), n = (side: number) => Math.max(1, Math.floor(side / chord + 1e-9)), nx = n(w), nz = n(d);
  return { nx, nz, total: 2 * (nx + nz) };
}
/** Capetele arcelor, în sensul acelor de ceas pe ecran, pornind din colțul (x0, z0); conturul se închide în primul punct. */
export function cloudNodes(r: Rect): P[] {
  const w = r.x1 - r.x0, d = r.z1 - r.z0, { nx, nz } = cloudArcCount(w, d), out: P[] = [];
  for (let i = 0; i < nx; i++) out.push([r.x0 + w * i / nx, r.z0]);
  for (let i = 0; i < nz; i++) out.push([r.x1, r.z0 + d * i / nz]);
  for (let i = 0; i < nx; i++) out.push([r.x1 - w * i / nx, r.z1]);
  for (let i = 0; i < nz; i++) out.push([r.x0, r.z1 - d * i / nz]);
  return out;
}
export interface CloudArc { from: P; to: P; /** vârful arcului, în afara dreptunghiului */ apex: P; radius: number }
/** Arcele norului: fiecare unește două capete vecine și e bombat spre exterior cu CLOUD_BULGE din coardă. */
export function cloudArcs(r: Rect): CloudArc[] {
  const n = cloudNodes(r);
  return n.map((from, i): CloudArc => { const to = n[(i + 1) % n.length]!, c = Math.hypot(to[0] - from[0], to[1] - from[1]) || 1, ux = (to[0] - from[0]) / c, uz = (to[1] - from[1]) / c, s = c * CLOUD_BULGE;
    // normala spre exterior a unui contur parcurs în sensul acelor de ceas (z în jos): (uz, −ux)
    return { from, to, apex: [(from[0] + to[0]) / 2 + uz * s, (from[1] + to[1]) / 2 - ux * s], radius: (c * c / 4 + s * s) / (2 * s) }; });
}
/** Cât iese norul în afara dreptunghiului (săgeata celui mai lat arc). */
export function cloudBulge(r: Rect): number { const w = r.x1 - r.x0, d = r.z1 - r.z0, { nx, nz } = cloudArcCount(w, d); return Math.max(w / nx, d / nz) * CLOUD_BULGE; }
/** Conturul norului ca poligon închis: fiecare arc aproximat cu `seg` segmente (DXF). */
export function cloudOutline(r: Rect, seg = 6): P[] {
  const out: P[] = [];
  for (const a of cloudArcs(r)){ const c = Math.hypot(a.to[0] - a.from[0], a.to[1] - a.from[1]) || 1, ux = (a.to[0] - a.from[0]) / c, uz = (a.to[1] - a.from[1]) / c;
    // centrul cercului e spre interior față de vârf; punctele merg de la `from` (−phi) spre `to` (+phi), fără capătul final
    const cx = a.apex[0] - uz * a.radius, cz = a.apex[1] + ux * a.radius, phi = Math.asin(Math.min(1, c / 2 / a.radius));
    for (let i = 0; i < seg; i++){ const t = -phi + 2 * phi * i / seg; out.push([cx + a.radius * (Math.sin(t) * ux + Math.cos(t) * uz), cz + a.radius * (Math.sin(t) * uz - Math.cos(t) * ux)]); } }
  return out;
}
/** Conturul norului ca atribut `d` de SVG: arce de cerc adevărate, închise. */
export function cloudPath(r: Rect): string {
  const arcs = cloudArcs(r); if (!arcs.length) return '';
  return `M${f4(arcs[0]!.from[0])} ${f4(arcs[0]!.from[1])}` + arcs.map(a => `A${f4(a.radius)} ${f4(a.radius)} 0 0 1 ${f4(a.to[0])} ${f4(a.to[1])}`).join('') + 'Z';
}
/** Locul etichetei de revizie: deasupra colțului din stânga-sus, cu linia de bază la 5 cm peste arce. */
export function cloudLabelAt(r: Rect): P { return [r.x0, r.z0 - cloudBulge(r) - .05]; }
/** Un nor are laturile de cel puțin 10 cm (ca în sanitizator). */
export const cloudOk = (r: Rect) => r.x1 - r.x0 >= PLAN_LIMITS.annotation.cloudMinM - 1e-9 && r.z1 - r.z0 >= PLAN_LIMITS.annotation.cloudMinM - 1e-9;
export const annotationCenter = (an: Annotation): P => { const b = annotationBox(an); return [(b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2]; };
