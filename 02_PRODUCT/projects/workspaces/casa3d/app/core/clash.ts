// Verificarea interferențelor 3D (docs/bim/GHID_BIM.md §3.7, Faza 5). Pur, determinist, fără dependențe.
// Volumele vin din model, la fel cum le construiește vederea 3D: pereții tăiați la goluri, stâlpi, grinzi sub tavan, fundații,
// plăcile tavanelor false, mobilierul (amprenta și înălțimea din catalog) și traseele (cilindru pe segment, raza declarată;
// fără diametru = linie). Tipuri: hard (volume care se pătrund mai adânc decât toleranța; suprapunerile mai mici se ignoră), străpungere (traseu prin perete obișnuit,
// intenționată, de informare), clearance (doar cu distanțele minime definite de proiect; nu există valori implicite).
// Limite cunoscute (raportate în docs/bim/FAZA0_INVENTAR.md): stâlpul rotund e aproximat cu cutia lui, rotația stâlpului și a
// piesei de mobilier se ia ca în vederea 3D (multipli de 90°), scările și corpurile tehnice (calorifer, AC) nu intră în verificare.
import type { Catalog, Floor, MepSystem, Snapshot } from './types';
import { floors } from './levels';
import { footprintOf } from './validate';
import { resolve } from './catalog';
import { elementIndex, type Discipline } from './elements';
import { findingId } from './coordination-review';
import { DOOR_HEIGHT } from './rules.boq';

export type Vec3 = [number, number, number];
/** Cutie cu rotație doar în plan: centrul, semi-laturile (x și z locale, y vertical) și unghiul axei locale x față de axa x. */
export interface Box { cx: number; cy: number; cz: number; hx: number; hy: number; hz: number; yaw: number }

export const CLASH_RULES = ['casa3d.clash.mep-structure', 'casa3d.clash.mep-wall', 'casa3d.clash.mep-mep', 'casa3d.clash.mep-furniture',
  'casa3d.clash.mep-ceiling', 'casa3d.clash.structure-furniture'] as const;
export const CLEARANCE_RULE = 'casa3d.clash.clearance';
export const DEFAULT_TOLERANCE_M = 0.005;
export const CEILING_BOARD_M = 0.0125;

export interface ClearanceRule { a: MepSystem; b: MepSystem; minM: number }
export interface ClashOptions { toleranceM?: number; clearances?: ClearanceRule[] }
export type ClashType = 'hard' | 'penetration' | 'clearance';
export interface ClashFinding {
  id: string; type: ClashType; rule: string; refs: [string, string]; disciplines: [Discipline, Discipline]; levelId: string;
  /** punctul cel mai apropiat de interferență (m), pentru vedere și raport */
  point: Vec3;
  /** cât se suprapun volumele (hard, străpungere) sau distanța liberă rămasă (clearance), în m */
  depthM?: number; distanceM?: number; toleranceM: number; severity: 'ERROR' | 'WARNING' | 'INFO'; message: string;
}

// ---------- geometrie ----------
const sub = (a: Vec3, b: Vec3): Vec3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const dot = (a: Vec3, b: Vec3) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const lerp = (a: Vec3, b: Vec3, t: number): Vec3 => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
const toLocal = (p: Vec3, b: Box): Vec3 => { const dx = p[0] - b.cx, dz = p[2] - b.cz, c = Math.cos(b.yaw), s = Math.sin(b.yaw); return [dx * c + dz * s, p[1] - b.cy, -dx * s + dz * c]; };
/** Distanța cu semn punct–cutie: pozitivă în afară, negativă înăuntru (cât de adânc e punctul față de cea mai apropiată față). */
export function pointBoxSigned(p: Vec3, b: Box): number {
  const q = toLocal(p, b), dx = Math.abs(q[0]) - b.hx, dy = Math.abs(q[1]) - b.hy, dz = Math.abs(q[2]) - b.hz;
  return Math.hypot(Math.max(dx, 0), Math.max(dy, 0), Math.max(dz, 0)) + Math.min(Math.max(dx, dy, dz), 0);
}
export const pointBoxDistance = (p: Vec3, b: Box) => Math.max(0, pointBoxSigned(p, b));
/** Minimul distanței cu semn pe segment: funcția de distanță cu semn a unei mulțimi convexe e convexă, deci căutarea secțiunii
 *  de aur e exactă până la precizia iterațiilor; întoarce și parametrul punctului. `d` < 0 = segmentul intră în cutie. */
export function segBoxClosest(a: Vec3, b: Vec3, box: Box): { d: number; t: number } {
  const f = (t: number) => pointBoxSigned(lerp(a, b, t), box), g = (Math.sqrt(5) - 1) / 2;
  let lo = 0, hi = 1, x1 = hi - g * (hi - lo), x2 = lo + g * (hi - lo), f1 = f(x1), f2 = f(x2);
  for (let i = 0; i < 80; i++) { if (f1 <= f2) { hi = x2; x2 = x1; f2 = f1; x1 = hi - g * (hi - lo); f1 = f(x1); } else { lo = x1; x1 = x2; f1 = f2; x2 = lo + g * (hi - lo); f2 = f(x2); } }
  const cands = [0, 1, (lo + hi) / 2].map(t => ({ t, d: f(t) })); return cands.reduce((m, c) => c.d < m.d ? c : m);
}
export const segBoxDistance = (a: Vec3, b: Vec3, box: Box) => Math.max(0, segBoxClosest(a, b, box).d);
/** Distanța dintre două segmente (formula clasică, cu cazurile degenerate). */
export function segSegClosest(p1: Vec3, q1: Vec3, p2: Vec3, q2: Vec3): { d: number; s: number; t: number } {
  const d1 = sub(q1, p1), d2 = sub(q2, p2), r = sub(p1, p2), a = dot(d1, d1), e = dot(d2, d2), f = dot(d2, r), EPS = 1e-12;
  let s: number, t: number;
  if (a <= EPS && e <= EPS) { s = 0; t = 0; }
  else if (a <= EPS) { s = 0; t = Math.min(1, Math.max(0, f / e)); }
  else { const c = dot(d1, r);
    if (e <= EPS) { t = 0; s = Math.min(1, Math.max(0, -c / a)); }
    else { const b = dot(d1, d2), den = a * e - b * b; s = den > EPS ? Math.min(1, Math.max(0, (b * f - c * e) / den)) : 0; t = (b * s + f) / e;
      if (t < 0) { t = 0; s = Math.min(1, Math.max(0, -c / a)); } else if (t > 1) { t = 1; s = Math.min(1, Math.max(0, (b - c) / a)); } } }
  const c1 = lerp(p1, q1, s), c2 = lerp(p2, q2, t); return { d: Math.hypot(...sub(c1, c2)), s, t };
}
export const segSegDistance = (p1: Vec3, q1: Vec3, p2: Vec3, q2: Vec3) => segSegClosest(p1, q1, p2, q2).d;
/** Adâncimea de suprapunere a două cutii rotite doar în plan (SAT pe cele 4 axe din plan + intervalul vertical); 0 = separate. */
export function boxBoxOverlap(A: Box, B: Box): number {
  const ov = Math.min(A.cy + A.hy, B.cy + B.hy) - Math.max(A.cy - A.hy, B.cy - B.hy); if (ov <= 0) return 0;
  let best = ov; const d = [B.cx - A.cx, B.cz - A.cz];
  const axes = (bx: Box) => { const c = Math.cos(bx.yaw), s = Math.sin(bx.yaw); return [[c, s], [-s, c]]; };
  const radius = (bx: Box, ax: number[]) => { const [u, v] = axes(bx); return bx.hx * Math.abs(u![0]! * ax[0]! + u![1]! * ax[1]!) + bx.hz * Math.abs(v![0]! * ax[0]! + v![1]! * ax[1]!); };
  for (const ax of [...axes(A), ...axes(B)]) { const o = radius(A, ax) + radius(B, ax) - Math.abs(d[0]! * ax[0]! + d[1]! * ax[1]!); if (o <= 0) return 0; best = Math.min(best, o); }
  return best;
}

// ---------- volumele modelului ----------
export interface Solid { id: string; kind: 'wall' | 'structural_wall' | 'column' | 'beam' | 'footing' | 'ceiling' | 'furniture'; box: Box }
export interface RunSeg { id: string; system: MepSystem; r: number; a: Vec3; b: Vec3; first: boolean; last: boolean }
export interface LevelSolids { levelId: string; solids: Solid[]; runs: RunSeg[] }

const boxAlong = (a: [number, number], b: [number, number], width: number, y0: number, y1: number, s0 = 0, s1?: number): Box | null => {
  const L = Math.hypot(b[0] - a[0], b[1] - a[1]); if (L < 1e-6) return null; const ux = (b[0] - a[0]) / L, uz = (b[1] - a[1]) / L, e = s1 ?? L, m = (s0 + e) / 2;
  return { cx: a[0] + ux * m, cy: (y0 + y1) / 2, cz: a[1] + uz * m, hx: (e - s0) / 2, hy: (y1 - y0) / 2, hz: width / 2, yaw: Math.atan2(uz, ux) };
};
const rectBox = (x0: number, z0: number, x1: number, z1: number, y0: number, y1: number): Box => ({ cx: (x0 + x1) / 2, cy: (y0 + y1) / 2, cz: (z0 + z1) / 2, hx: Math.abs(x1 - x0) / 2, hy: (y1 - y0) / 2, hz: Math.abs(z1 - z0) / 2, yaw: 0 });

function levelSolids(f: Floor, s: Snapshot, cat: Catalog, skipped: { id: string; reason: string }[]): LevelSolids {
  const H = f.ceilingHeight, solids: Solid[] = [], rooms = new Set(f.rooms.map(r => r.id));
  for (const w of f.walls) {
    if (w.status === 'demolish') continue;
    const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]); if (L < 1e-6) continue;
    const kind = w.structural ? 'structural_wall' : 'wall', put = (s0: number, s1: number, y0: number, y1: number) => { if (s1 - s0 < .005 || y1 - y0 < .005) return; const bx = boxAlong(w.a, w.b, w.thickness, y0, y1, s0, s1); if (bx) solids.push({ id: w.id, kind, box: bx }); };
    // ca în vederea 3D: bucăți pline între goluri, deasupra ușii, sub și deasupra ferestrei
    let q = 0; for (const o of [...w.openings].sort((x, y) => x.offset - y.offset)) {
      const s0 = Math.max(0, o.offset), s1 = Math.min(L, o.offset + o.width); put(q, s0, 0, H);
      const y0 = o.kind === 'door' ? 0 : o.sill ?? .9, y1 = o.kind === 'door' ? Math.min(H, o.height ?? DOOR_HEIGHT) : Math.min(H, (o.sill ?? .9) + (o.height ?? 1.3));
      put(s0, s1, 0, y0); put(s0, s1, y1, H); q = Math.max(q, s1); }
    put(q, L, 0, H);
  }
  for (const c of f.columns ?? []) if ((c.role ?? 'structural') === 'structural') solids.push({ id: c.id, kind: 'column', box: rectBox(c.x - c.w / 2, c.z - c.d / 2, c.x + c.w / 2, c.z + c.d / 2, 0, Math.min(H, c.height ?? H)) });
  for (const b of f.beams ?? []) { const bx = boxAlong(b.a, b.b, b.width, H - b.depth, H); if (bx) solids.push({ id: b.id, kind: 'beam', box: bx }); }
  for (const x of f.footings ?? []) solids.push({ id: x.id, kind: 'footing', box: rectBox(x.x - x.w / 2, x.z - x.d / 2, x.x + x.w / 2, x.z + x.d / 2, -x.depth, 0) });
  for (const z of f.ceilingZones ?? []) { const y = H - z.dropCm / 100; solids.push({ id: z.id, kind: 'ceiling', box: rectBox(z.rect.x0, z.rect.z0, z.rect.x1, z.rect.z1, y - CEILING_BOARD_M, y) }); }
  for (const p of s.placements) { if (!rooms.has(p.roomId)) continue;
    const fp = footprintOf(cat, p), h = p.size ? p.size.h / 100 : resolve(cat, p.variantId)?.h ?? 0;
    if (!fp || !(h > 0) || fp.x1 - fp.x0 <= 0 || fp.z1 - fp.z0 <= 0) { skipped.push({ id: p.id, reason: 'unknown-size' }); continue; }
    solids.push({ id: p.id, kind: 'furniture', box: rectBox(fp.x0, fp.z0, fp.x1, fp.z1, 0, h) }); }
  const runs: RunSeg[] = [];
  for (const r of f.mepRuns ?? []) for (let i = 1; i < r.points.length; i++)
    runs.push({ id: r.id, system: r.system, r: (r.diameterMm ?? 0) / 2000, a: r.points[i - 1]!, b: r.points[i]!, first: i === 1, last: i === r.points.length - 1 });
  return { levelId: f.id, solids, runs };
}

export function solidsOf(s: Snapshot, cat: Catalog): { levels: LevelSolids[]; skipped: { id: string; reason: string }[] } {
  const skipped: { id: string; reason: string }[] = [];
  return { levels: floors(s).map(f => levelSolids(f, s, cat, skipped)), skipped };
}

// ---------- reguli ----------
const STRUCT = new Set<Solid['kind']>(['structural_wall', 'column', 'beam', 'footing']);
const label = (t: ClashType) => t === 'hard' ? 'Interferență' : t === 'penetration' ? 'Străpungere' : 'Distanță insuficientă';

/** Toate interferențele, pe niveluri, grupate pe (regulă, pereche de elemente) cu cea mai mare adâncime / cea mai mică distanță. */
export function detectClashes(s: Snapshot, cat: Catalog, o: ClashOptions = {}): ClashFinding[] {
  const tol = o.toleranceM ?? DEFAULT_TOLERANCE_M, disc = new Map(elementIndex(s).map(e => [e.elementId, e.discipline]));
  const best = new Map<string, ClashFinding>();
  const add = (type: ClashType, rule: string, a: string, b: string, levelId: string, point: Vec3, m: { depthM?: number; distanceM?: number }, severity: ClashFinding['severity']) => {
    const refs = [a, b].sort() as [string, string], key = `${rule}|${refs.join(',')}|${levelId}`, prev = best.get(key);
    if (prev && ((m.depthM ?? -1) <= (prev.depthM ?? -1)) && ((m.distanceM ?? Infinity) >= (prev.distanceM ?? Infinity))) return;
    const r3 = (v: number) => Math.round(v * 1000) / 1000, pt = point.map(r3) as Vec3;
    const what = m.depthM != null ? `suprapunere ${Math.round(m.depthM * 1000)} mm` : `distanță ${Math.round((m.distanceM ?? 0) * 1000)} mm`;
    best.set(key, { id: findingId(type === 'hard' ? 'CLASH_HARD' : type === 'penetration' ? 'WALL_PENETRATION' : 'CLASH_CLEARANCE', rule, refs), type, rule, refs,
      disciplines: [disc.get(refs[0]) ?? 'architecture', disc.get(refs[1]) ?? 'architecture'], levelId, point: pt,
      ...(m.depthM != null ? { depthM: r3(m.depthM) } : {}), ...(m.distanceM != null ? { distanceM: r3(m.distanceM) } : {}), toleranceM: tol, severity,
      message: `${label(type)} ${refs[0]} × ${refs[1]} la (${pt.join(', ')}) m: ${what}, toleranță ${Math.round(tol * 1000)} mm.` });
  };
  const { levels } = solidsOf(s, cat);
  for (const L of levels) {
    // traseu × volum
    for (const r of L.runs) for (const so of L.solids) {
      // pătrunderea = raza minus distanța cu semn a axei; o suprapunere până la toleranță se ignoră (convenția uzuală)
      const c = segBoxClosest(r.a, r.b, so.box), pen = r.r - c.d, at = lerp(r.a, r.b, c.t);
      if (pen <= tol) continue;
      if (so.kind === 'wall') add('penetration', 'casa3d.clash.mep-wall', r.id, so.id, L.levelId, at, { depthM: pen }, 'INFO');
      else if (STRUCT.has(so.kind)) add('hard', 'casa3d.clash.mep-structure', r.id, so.id, L.levelId, at, { depthM: pen }, 'ERROR');
      else if (so.kind === 'furniture') add('hard', 'casa3d.clash.mep-furniture', r.id, so.id, L.levelId, at, { depthM: pen }, 'WARNING');
      else if (so.kind === 'ceiling') add('hard', 'casa3d.clash.mep-ceiling', r.id, so.id, L.levelId, at, { depthM: pen }, 'WARNING');
    }
    // traseu × traseu (traseele legate cap la cap nu sunt interferență)
    for (let i = 0; i < L.runs.length; i++) for (let j = i + 1; j < L.runs.length; j++) {
      const A = L.runs[i]!, B = L.runs[j]!; if (A.id === B.id) continue;
      const c = segSegClosest(A.a, A.b, B.a, B.b), gap = c.d - A.r - B.r, at = lerp(A.a, A.b, c.s);
      const ends = (x: RunSeg) => [...(x.first ? [x.a] : []), ...(x.last ? [x.b] : [])];
      const joined = ends(A).some(p => ends(B).some(q => Math.hypot(...sub(p, q)) <= A.r + B.r + tol));
      if (joined) continue;
      if (-gap > tol) { add('hard', 'casa3d.clash.mep-mep', A.id, B.id, L.levelId, at, { depthM: -gap }, 'WARNING'); continue; }
      for (const cr of o.clearances ?? []) if ((cr.a === A.system && cr.b === B.system) || (cr.a === B.system && cr.b === A.system))
        if (gap < cr.minM) add('clearance', CLEARANCE_RULE, A.id, B.id, L.levelId, at, { distanceM: gap }, 'WARNING');
    }
    // structură × mobilier (dulap înalt sub grindă, piesă în stâlp)
    for (const so of L.solids) if (STRUCT.has(so.kind) && so.kind !== 'structural_wall') for (const fu of L.solids) if (fu.kind === 'furniture') {
      const d = boxBoxOverlap(so.box, fu.box); if (d > tol) add('hard', 'casa3d.clash.structure-furniture', so.id, fu.id, L.levelId, [fu.box.cx, Math.min(so.box.cy + so.box.hy, fu.box.cy + fu.box.hy), fu.box.cz], { depthM: d }, 'WARNING');
    }
  }
  return [...best.values()].sort((x, y) => x.levelId.localeCompare(y.levelId) || x.rule.localeCompare(y.rule) || x.refs.join().localeCompare(y.refs.join()));
}
