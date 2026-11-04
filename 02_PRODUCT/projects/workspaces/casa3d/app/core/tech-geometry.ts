// Geometria punctelor tehnice care stau pe perete și au corp (caloriferul cu mărimi, unitatea interioară de aer condiționat):
// latura camerei, normala spre cameră, fața peretelui și dreptunghiul din plan. Pur și fără importuri grele (validate.ts îl
// folosește, iar technical.ts îl importă la rândul lui).
import type { Floor, RoomRect, Wall } from './types';
import { PLAN_DEFAULTS } from './plan-limits';

export type Side = 'N' | 'E' | 'S' | 'W';
export type RadiatorModel = 'panel' | 'towel' | 'decorative';
export interface TechBody { kind: string; roomId: string; x: number; z: number; height: number; widthM?: number; heightM?: number; model?: RadiatorModel }
export interface TechFrame { side: Side; u: [number, number]; n: [number, number]; along: number; wall: Wall | null; thickness: number; face: [number, number] }

/** Latura camerei cea mai apropiată de punct, normala spre interior și fața peretelui de pe latura aceea (un perete demolat nu contează). */
export function techFrame(floor: Pick<Floor, 'walls' | 'rooms'>, p: Pick<TechBody, 'roomId' | 'x' | 'z'>): TechFrame | null {
  const room = floor.rooms.find(r => r.id === p.roomId); if (!room) return null; const r = room.rect;
  const side = ([['N', Math.abs(p.z - r.z0)], ['E', Math.abs(p.x - r.x1)], ['S', Math.abs(p.z - r.z1)], ['W', Math.abs(p.x - r.x0)]] as [Side, number][]).sort((a, b) => a[1] - b[1])[0]![0];
  const horiz = side === 'N' || side === 'S', edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[side], along = horiz ? p.x : p.z;
  const n: [number, number] = side === 'N' ? [0, 1] : side === 'S' ? [0, -1] : side === 'W' ? [1, 0] : [-1, 0], u: [number, number] = horiz ? [1, 0] : [0, 1];
  const wall = floor.walls.find(w => { if (w.status === 'demolish') return false; const h = Math.abs(w.a[1] - w.b[1]) < 1e-6, v = Math.abs(w.a[0] - w.b[0]) < 1e-6;
    if (horiz ? !(h && Math.abs(w.a[1] - edge) < 1e-6) : !(v && Math.abs(w.a[0] - edge) < 1e-6)) return false;
    const lo = horiz ? Math.min(w.a[0], w.b[0]) : Math.min(w.a[1], w.b[1]), hi = horiz ? Math.max(w.a[0], w.b[0]) : Math.max(w.a[1], w.b[1]); return along >= lo - 1e-6 && along <= hi + 1e-6; }) ?? null;
  const th = wall ? wall.thickness : .15, face: [number, number] = horiz ? [along, edge + n[1] * th / 2] : [edge + n[0] * th / 2, along];
  return { side, u, n, along, wall, thickness: th, face };
}
/** Caloriferul „nou” are mărimi (`widthM`); punctul vechi de racord (fără mărimi) rămâne un simplu simbol, ca înainte. */
export const isSizedRadiator = (p: Pick<TechBody, 'kind' | 'widthM'>) => p.kind === 'radiator' && p.widthM != null;
/** Valoarea tastată în centimetri → metri, adusă în intervalul [lo, hi] (m) și rotunjită la mm; text gol sau nenumeric = null (rămâne valoarea veche). */
export function cmToClampedM(v: string, lo: number, hi: number): number | null {
  const x = Number(v); if (v.trim() === '' || !Number.isFinite(x)) return null; return Math.round(Math.min(hi, Math.max(lo, x / 100)) * 1000) / 1000;
}
/** Mărimile caloriferului (m): lățime, înălțime, adâncime (port-prosopul e subțire). */
export const radiatorSize = (p: Pick<TechBody, 'widthM' | 'heightM' | 'model'>) => ({ w: p.widthM ?? PLAN_DEFAULTS.radiator.width, h: p.heightM ?? PLAN_DEFAULTS.radiator.height, d: p.model === 'towel' ? .05 : .1 });
/** Mărimile unității de aer condiționat (m): 80 × 29 × 20 cm implicit, lățimea se poate schimba. */
export const hvacSize = (p: Pick<TechBody, 'widthM'>) => ({ w: p.widthM ?? PLAN_DEFAULTS.hvac.width, h: PLAN_DEFAULTS.hvac.height, d: PLAN_DEFAULTS.hvac.depth });
/** Dreptunghiul din plan al unui corp de pe perete: de lățime `w` pe latura lui, de la `gap` la `gap + d` în fața peretelui. */
export function bodyRect(f: TechFrame, w: number, d: number, gap = .03): RoomRect {
  const a0 = f.along - w / 2, a1 = f.along + w / 2;
  if (f.side === 'N' || f.side === 'S'){ const z0 = f.face[1] + f.n[1] * gap, z1 = f.face[1] + f.n[1] * (gap + d); return { x0: a0, x1: a1, z0: Math.min(z0, z1), z1: Math.max(z0, z1) }; }
  const x0 = f.face[0] + f.n[0] * gap, x1 = f.face[0] + f.n[0] * (gap + d); return { x0: Math.min(x0, x1), x1: Math.max(x0, x1), z0: a0, z1: a1 };
}
/** Dreptunghiul caloriferului în plan, sau null (punct fără mărimi sau cameră inexistentă). */
export function radiatorRect(floor: Pick<Floor, 'walls' | 'rooms'>, p: TechBody): RoomRect | null {
  if (!isSizedRadiator(p)) return null; const f = techFrame(floor, p); if (!f) return null; const s = radiatorSize(p); return bodyRect(f, s.w, s.d);
}
/** Zona din fața caloriferului (30 cm) în care o piesă înaltă oprește circulația aerului cald. */
export const RADIATOR_FRONT_M = .3;
export function radiatorFront(floor: Pick<Floor, 'walls' | 'rooms'>, p: TechBody): RoomRect | null {
  if (!isSizedRadiator(p)) return null; const f = techFrame(floor, p); if (!f) return null; const s = radiatorSize(p); return bodyRect(f, s.w, s.d + RADIATOR_FRONT_M);
}
