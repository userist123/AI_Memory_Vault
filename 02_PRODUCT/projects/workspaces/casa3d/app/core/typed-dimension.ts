// Cote tastate în timpul desenării (UI/UX P1-6, modelul SketchUp): după primul clic, cifrele tastate merg într-un „chip”
// lângă cursor, iar Enter construiește elementul exact. Reguli previzibile pentru un designer care lucrează în cm:
// întreg = cm („320”), zecimală = m („3,20”; virgula e zecimală), sufixele au prioritate (m, cm, mm; imperial ft/′, in/″).
import type { Units } from './format';

const M_PER_IN = .0254, MIN_M = .2, MAX_M = 1000, ROOM_MIN_M = .6;
const UNIT: Record<string, number> = { m: 1, cm: .01, mm: .001, ft: .3048, "'": .3048, '′': .3048, in: M_PER_IN, '"': M_PER_IN, '″': M_PER_IN };

/** Lungimea tastată, în metri; null dacă nu e o lungime plauzibilă (20 cm – 1 km). */
export function parseTypedLength(text: string, units: Units): number | null {
  const m = /^\s*(\d+(?:[.,]\d+)?)\s*(m|cm|mm|ft|in|'|′|"|″)?\s*$/i.exec(text); if (!m) return null;
  const raw = m[1]!, n = Number(raw.replace(',', '.')), unit = m[2]?.toLowerCase();
  const metres = unit ? n * UNIT[unit]! : units === 'imperial' ? n * M_PER_IN : /[.,]/.test(raw) ? n : n / 100;
  return Number.isFinite(metres) && metres >= MIN_M - 1e-9 && metres <= MAX_M ? metres : null;
}
/** Dimensiunile unei camere: „lățime;adâncime” (sau x, ×, *, spațiu); fiecare cu regulile de mai sus și cel puțin
 *  0,6 m, ca la camera trasă cu mouse-ul. */
export function parseTypedSize(text: string, units: Units): { w: number; d: number } | null {
  const parts = text.trim().split(/\s*[;x×*]\s*|\s+(?=\d)/i).filter(Boolean); if (parts.length !== 2) return null;
  const w = parseTypedLength(parts[0]!, units), d = parseTypedLength(parts[1]!, units);
  return w != null && d != null && w >= ROOM_MIN_M - 1e-9 && d >= ROOM_MIN_M - 1e-9 ? { w, d } : null;
}
/** Capătul peretelui: din `a`, pe direcția spre `toward` (mouse-ul), la lungimea `len`; fără direcție, spre dreapta. */
export function typedEndpoint(a: [number, number], toward: [number, number], len: number): [number, number] {
  const dx = toward[0] - a[0], dz = toward[1] - a[1], l = Math.hypot(dx, dz);
  return l < 1e-9 ? [a[0] + len, a[1]] : [a[0] + dx / l * len, a[1] + dz / l * len];
}
/** Camera tastată: din colțul fixat `a`, spre cadranul în care e mouse-ul. */
export function typedRect(a: [number, number], toward: [number, number], s: { w: number; d: number }){
  const sx = toward[0] < a[0] ? -1 : 1, sz = toward[1] < a[1] ? -1 : 1, x1 = a[0] + sx * s.w, z1 = a[1] + sz * s.d;
  return { x0: Math.min(a[0], x1), z0: Math.min(a[1], z1), x1: Math.max(a[0], x1), z1: Math.max(a[1], z1) };
}
/** Tasta intră în chip? Cifrele pornesc textul; separatorii și unitățile doar după o cifră (R, M etc. rămân scurtături). */
export function acceptsTypedKey(key: string, current: string): boolean {
  if (key.length !== 1) return false;
  if (/\d/.test(key)) return true;
  if (!current) return false;
  return /[.,;x×* ]|[mcift'"′″n]/i.test(key);
}
