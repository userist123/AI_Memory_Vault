// Cote și tabele pentru planul tipărit: funcții pure, fără React.
import type { Floor, Snapshot } from './types';
import { area, wallLength } from './geometry';

const r2 = (v: number) => Math.round(v * 100) / 100;
export const LABEL_OFFSET = 0.45;   // distanța (m) a etichetei de cotă față de perete, spre exterior
export interface WallDim { wallId: string; exterior: boolean; lengthM: number; lengthCm: number; mid: [number, number]; normal: [number, number]; label: [number, number] }
export interface Bounds { x0: number; x1: number; z0: number; z1: number }

/** Dreptunghiul care cuprinde pereții și camerele; plan gol => null. */
export function floorBounds(floor: Floor): Bounds | null {
  const xs = floor.walls.flatMap(w => [w.a[0], w.b[0]]).concat(floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1]));
  const zs = floor.walls.flatMap(w => [w.a[1], w.b[1]]).concat(floor.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  return xs.length ? { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) } : null;
}

/** Lungime, mijloc, normala unitară spre exterior (departe de centrul planului) și punctul etichetei pentru fiecare perete. */
export function wallDimensions(floor: Floor): WallDim[] {
  const b = floorBounds(floor), cx = b ? (b.x0 + b.x1) / 2 : 0, cz = b ? (b.z0 + b.z1) / 2 : 0;
  return floor.walls.map(w => {
    const L = wallLength(w.a, w.b), mid: [number, number] = [(w.a[0] + w.b[0]) / 2, (w.a[1] + w.b[1]) / 2];
    let n: [number, number] = L > 0 ? [-(w.b[1] - w.a[1]) / L, (w.b[0] - w.a[0]) / L] : [0, -1];
    if (n[0] * (mid[0] - cx) + n[1] * (mid[1] - cz) < 0) n = [-n[0], -n[1]];
    return { wallId: w.id, exterior: w.exterior, lengthM: r2(L), lengthCm: Math.round(L * 100), mid, normal: n, label: [mid[0] + n[0] * LABEL_OFFSET, mid[1] + n[1] * LABEL_OFFSET] };
  });
}

export interface ScheduleRow { id: string; name: string; type: string; width: number; depth: number; area: number; perimeter: number }
/** Tabelul camerelor (metri, 2 zecimale) și totalurile. */
export function roomSchedule(snap: Snapshot): { rows: ScheduleRow[]; totals: { area: number; rooms: number } } {
  const rows = snap.floor.rooms.map(r => { const w = r.rect.x1 - r.rect.x0, d = r.rect.z1 - r.rect.z0;
    return { id: r.id, name: r.name, type: r.type, width: r2(w), depth: r2(d), area: r2(area(r.rect)), perimeter: r2(2 * (w + d)) }; });
  return { rows, totals: { area: r2(rows.reduce((a, x) => a + x.area, 0)), rooms: rows.length } };
}

export const PRINT_SCALES = [50, 75, 100, 200] as const;
/** Cea mai mare scară (1:50 … 1:200) la care planul (m) încape în zona imprimabilă (mm); dacă nu încape deloc, 1:200. */
export function printScale(extentW: number, extentD: number, paperW = 277, paperH = 160): { denominator: number; widthMm: number; heightMm: number } {
  const fit = (den: number) => (extentW * 1000) / den <= paperW + 1e-6 && (extentD * 1000) / den <= paperH + 1e-6;
  const denominator: number = PRINT_SCALES.find(fit) ?? 200;
  return { denominator, widthMm: r2((extentW * 1000) / denominator), heightMm: r2((extentD * 1000) / denominator) };
}
