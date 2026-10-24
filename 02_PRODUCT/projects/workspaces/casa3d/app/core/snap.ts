// Desenarea precisă a pereților (PLAN_A1, felia 2): magnetul cursorului (capăt de perete → unghi în trepte → grilă),
// punctul la o lungime tastată (lungimea se citește în core/typed-dimension.ts). Doar logică pură, fără DOM; PlanView o aplică.
import type { Floor } from './types';
import { r3 } from './geometry';

export type SnapKind = 'endpoint' | 'angle' | 'grid' | 'free';
export interface SnapResult { p: [number, number]; kind: SnapKind; length: number | null; angleDeg: number | null }
export interface SnapOpts { floor: Pick<Floor, 'walls'>; anchor?: [number, number] | null; free?: boolean; grid?: number; radius?: number; stepDeg?: number }

const DEG = Math.PI / 180;
const angleOf = (a: [number, number], p: [number, number]) => { const d = Math.atan2(p[1] - a[1], p[0] - a[0]) / DEG; return r3(((d % 360) + 360) % 360) % 360; };
const along = (a: [number, number], deg: number, len: number): [number, number] => [r3(a[0] + len * Math.cos(deg * DEG)), r3(a[1] + len * Math.sin(deg * DEG))];
const snapDir = (a: [number, number], to: [number, number], step: number) => Math.round(Math.atan2(to[1] - a[1], to[0] - a[0]) / DEG / step) * step;

/** Magnetul cursorului la desenarea unui perete: capăt de perete în rază › unghi rotunjit la `stepDeg` din `anchor` (lungime pe grilă) › grilă; `free` (Shift) = fără magnet, doar mm. */
export function snapDraw(cursor: [number, number], o: SnapOpts): SnapResult {
  const { anchor = null, free = false, grid = .05, radius = .2, stepDeg = 15 } = o;
  const fin = (p: [number, number], kind: SnapKind, deg?: number): SnapResult => {
    if (!anchor) return { p, kind, length: null, angleDeg: null };
    const len = Math.hypot(p[0] - anchor[0], p[1] - anchor[1]);
    return { p, kind, length: r3(len), angleDeg: len < 1e-9 ? null : deg ?? angleOf(anchor, p) };
  };
  if (free) return fin([r3(cursor[0]), r3(cursor[1])], 'free');
  let best: [number, number] | null = null, bd = radius;
  for (const w of o.floor.walls) for (const e of [w.a, w.b]) {
    if (anchor && Math.hypot(e[0] - anchor[0], e[1] - anchor[1]) < 1e-3) continue;
    const d = Math.hypot(e[0] - cursor[0], e[1] - cursor[1]); if (d < bd) { bd = d; best = [e[0], e[1]]; }
  }
  if (best) return fin(best, 'endpoint');
  if (anchor && Math.hypot(cursor[0] - anchor[0], cursor[1] - anchor[1]) > 1e-3) {
    const deg = snapDir(anchor, cursor, stepDeg), rad = deg * DEG;
    const proj = (cursor[0] - anchor[0]) * Math.cos(rad) + (cursor[1] - anchor[1]) * Math.sin(rad);
    const len = Math.max(0, Math.round(proj / grid) * grid);
    return fin(len === 0 ? [anchor[0], anchor[1]] : along(anchor, deg, len), 'angle', ((deg % 360) + 360) % 360);
  }
  return fin([r3(Math.round(cursor[0] / grid) * grid), r3(Math.round(cursor[1] / grid) * grid)], 'grid');
}

/** Punctul aflat la exact `lengthM` de `anchor` spre `toward` (direcția rotunjită la `stepDeg` dacă nu e `free`), rotunjit la mm. */
export function pointAtLength(anchor: [number, number], toward: [number, number], lengthM: number, stepDeg = 15, free = false): [number, number] {
  const deg = free ? Math.atan2(toward[1] - anchor[1], toward[0] - anchor[0]) / DEG : snapDir(anchor, toward, stepDeg);
  return along(anchor, deg, lengthM);
}
