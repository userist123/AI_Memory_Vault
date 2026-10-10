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
