// Orientarea casei: unde e nordul real față de „sus”-ul planului. O declară utilizatorul (din releveu, din extrasul de carte
// funciară sau cu o busolă); fără declarație nu se desenează nicio săgeată de nord și soarele rămâne pe convenția planului
// (nordul spre −z), ca să nu apară o orientare inventată (CONSTITUTION art. 4). Funcții pure.

/** Unghiul nordului real măsurat în sensul acelor de ceasornic de la „sus”-ul planului, în grade [0, 360), la 0,1°. */
export interface SiteOrientation { northDeg: number }

/** Valoare citită din date nesigure: număr finit, adus în [0, 360) și rotunjit la 0,1°; altfel null (nedeclarat). */
export function parseNorth(x: unknown): SiteOrientation | null {
  const v = x && typeof x === 'object' ? (x as { northDeg?: unknown }).northDeg : undefined;
  if (typeof v !== 'number' || !Number.isFinite(v)) return null;
  const n = Math.round((((v % 360) + 360) % 360) * 10) / 10;
  return { northDeg: n >= 360 ? 0 : n };
}

/** Azimutul real (0 = nord, 90 = est, în sensul acelor) trecut în azimutul planului, pe care îl înțeleg lumina și soarele din 3D. */
export const planAzimuth = (trueAzimuthDeg: number, site: SiteOrientation | null | undefined) =>
  site ? (((trueAzimuthDeg + site.northDeg) % 360) + 360) % 360 : trueAzimuthDeg;

const POINTS = ['N', 'NE', 'E', 'SE', 'S', 'SV', 'V', 'NV'] as const;
const POINTS_EN = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'] as const;
/** Punctul cardinal spre care privește „sus”-ul planului (rotunjit la 45°), ca text scurt: „sus = NE”. */
export function planUpFacing(site: SiteOrientation, lang: 'ro' | 'en' = 'ro'): string {
  // sus-ul planului are azimutul real 360 − northDeg
  const az = (360 - site.northDeg) % 360, k = Math.round(az / 45) % 8;
  return (lang === 'en' ? POINTS_EN : POINTS)[k]!;
}
