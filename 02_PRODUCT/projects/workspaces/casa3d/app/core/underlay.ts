// Imaginea de calc: calibrarea scării și dimensiunile ei pe plan. Funcții pure.
import { r3 } from './geometry';
import type { Underlay } from './types';

export const UNDERLAY_LIMITS = { minWidthM: 1, maxWidthM: 100, minOpacity: 0.1, maxOpacity: 1, maxSide: 2000, jpegQuality: 0.85 } as const;
/** Punct în metri pe plan. */
type Pt = [number, number];

/** Noua lățime a imaginii (m) astfel încât distanța dintre `a` și `b` (puncte de pe imagine, în metri pe plan, cu scara
 *  actuală `widthM`) să devină `realCm`. Întoarce null dacă punctele coincid sau valorile sunt invalide. Se limitează la 1–100 m. */
export function scaleFromPoints(a: Pt, b: Pt, realCm: number, widthM: number): number | null {
  const d = Math.hypot(b[0] - a[0], b[1] - a[1]);
  if (![d, realCm, widthM].every(Number.isFinite) || d < 1e-6 || realCm <= 0 || widthM <= 0) return null;
  const next = widthM * (realCm / 100) / d;
  return r3(Math.min(UNDERLAY_LIMITS.maxWidthM, Math.max(UNDERLAY_LIMITS.minWidthM, next)));
}
/** Noua poziție (x, z) după recalibrare, astfel încât punctul `a` să rămână pe loc (scalarea se face față de el). */
export function anchorAfterScale(u: Pick<Underlay, 'x' | 'z' | 'widthM'>, a: Pt, newWidthM: number): Pt {
  const k = newWidthM / u.widthM; return [r3(a[0] - (a[0] - u.x) * k), r3(a[1] - (a[1] - u.z) * k)];
}
/** Dimensiunile redimensionate pentru încărcare: latura lungă ≤ maxSide, fără mărire. */
export function fitWithin(w: number, h: number, maxSide: number = UNDERLAY_LIMITS.maxSide): { w: number; h: number } {
  const k = Math.min(1, maxSide / Math.max(w, h)); return { w: Math.max(1, Math.round(w * k)), h: Math.max(1, Math.round(h * k)) };
}

/**
 * Convertește un dreptunghi trasat peste releveul/imaginea de calc în cameră + pereți autoritativi,
 * doar după ce scara imaginii a fost calibrată și confirmată explicit (`u.confirmed !== false`).
 */
export function traceRoomFromUnderlay(
  u: Underlay,
  bounds: import('./types').RoomRect,
  roomSpec: { id: string; name: string; type: string },
  wallIdPrefix = 'trace-wall',
): { room: import('./types').Room; walls: import('./types').Wall[] } {
  if (u.confirmed === false || !(u.widthM >= UNDERLAY_LIMITS.minWidthM && u.widthM <= UNDERLAY_LIMITS.maxWidthM)) {
    throw new Error('UNDERLAY_SCALE_UNCONFIRMED: Calibrează și confirmă scara releveului înainte de a genera pereți.');
  }
  const x0 = r3(Math.min(bounds.x0, bounds.x1)), x1 = r3(Math.max(bounds.x0, bounds.x1));
  const z0 = r3(Math.min(bounds.z0, bounds.z1)), z1 = r3(Math.max(bounds.z0, bounds.z1));
  if (x1 - x0 < 0.6 || z1 - z0 < 0.6) throw new Error('Camera trasată trebuie să aibă cel puțin 60 cm pe fiecare latură.');
  const room: import('./types').Room = { id: roomSpec.id, name: roomSpec.name, type: roomSpec.type, rect: { x0, z0, x1, z1 } };
  const pts: [[number, number], [number, number]][] = [[[x0, z0], [x1, z0]], [[x1, z0], [x1, z1]], [[x1, z1], [x0, z1]], [[x0, z1], [x0, z0]]];
  const walls: import('./types').Wall[] = pts.map(([a, b], i) => ({
    id: `${wallIdPrefix}-${i + 1}`,
    a,
    b,
    thickness: 0.25,
    exterior: true,
    openings: [],
  }));
  return { room, walls };
}
