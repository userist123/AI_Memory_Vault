// Pure 3D measuring maths (meters, Y-up). Formatting reuses core/format.ts.
import { formatLength, formatArea, type Units, type Locale } from './format';

export type P3 = { x: number; y: number; z: number };

export const distance3 = (a: P3, b: P3): number => Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z);
/** Distance in the horizontal (XZ) plane. */
export const horizontalDistance = (a: P3, b: P3): number => Math.hypot(b.x - a.x, b.z - a.z);
/** Signed height difference b - a along Y. */
export const heightDifference = (a: P3, b: P3): number => b.y - a.y;

/** Angle a-vertex-c in degrees (0..180). Returns NaN when either arm has zero length. */
export function angleAt(a: P3, vertex: P3, c: P3): number {
  const ux = a.x - vertex.x, uy = a.y - vertex.y, uz = a.z - vertex.z;
  const vx = c.x - vertex.x, vy = c.y - vertex.y, vz = c.z - vertex.z;
  const lu = Math.hypot(ux, uy, uz), lv = Math.hypot(vx, vy, vz);
  if (lu === 0 || lv === 0) return NaN;
  const cos = Math.max(-1, Math.min(1, (ux * vx + uy * vy + uz * vz) / (lu * lv)));
  return Math.acos(cos) * 180 / Math.PI;
}

/** Open polyline length; 0 for fewer than 2 points. */
export function polylineLength(points: readonly P3[]): number {
  let s = 0;
  for (let i = 1; i < points.length; i++) s += distance3(points[i - 1], points[i]);
  return s;
}

/** Shoelace area on the XZ plane, absolute; 0 for fewer than 3 points. */
export function polygonAreaXZ(points: readonly P3[]): number {
  if (points.length < 3) return 0;
  let s = 0;
  for (let i = 0; i < points.length; i++) {
    const p = points[i], q = points[(i + 1) % points.length];
    s += p.x * q.z - q.x * p.z;
  }
  return Math.abs(s) / 2;
}

/** Perimeter on the XZ plane; closed adds the last->first edge (only for 3+ points). */
export function perimeterXZ(points: readonly P3[], closed = true): number {
  let s = 0;
  for (let i = 1; i < points.length; i++) s += horizontalDistance(points[i - 1], points[i]);
  if (closed && points.length >= 3) s += horizontalDistance(points[points.length - 1], points[0]);
  return s;
}

export function formatMeasure(kind: 'length' | 'area' | 'angle', value: number, units: Units = 'metric', locale: Locale = 'ro'): string {
  if (kind === 'length') return formatLength(value, units, locale);
  if (kind === 'area') return formatArea(value, units, locale);
  // unghiul cu separatorul zecimal al limbii, ca lungimile și ariile din core/format.ts
  const tag = locale === 'ro' ? 'ro-RO' : locale === 'en' ? 'en-GB' : locale;
  return `${new Intl.NumberFormat(tag, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value)}°`;
}
