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

/** Coordonatele geografice ale casei, în grade zecimale (nord și est pozitive), la 0,01° (≈ 1 km: destul pentru soare, fără adresă exactă). */
export interface SiteLocation { lat: number; lon: number }
/** Valoare citită din date nesigure: ambele numere finite, în intervalele geografice, rotunjite la 0,01°; altfel null (nedeclarată). */
export function parseLocation(x: unknown): SiteLocation | null {
  if (!x || typeof x !== 'object') return null;
  const { lat, lon } = x as { lat?: unknown; lon?: unknown };
  if (typeof lat !== 'number' || typeof lon !== 'number' || !Number.isFinite(lat) || !Number.isFinite(lon)) return null;
  if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return null;
  const r = (v: number) => { const n = Math.round(v * 100) / 100; return n === 0 ? 0 : n; };
  return { lat: r(lat), lon: r(lon) };
}

/** Ce se păstrează pe Snapshot.site: nordul și/sau localitatea, fiecare doar dacă a fost declarată de utilizator (art. 4). */
export interface SiteInfo { northDeg?: number; lat?: number; lon?: number }
/** Curăță Snapshot.site din date nesigure: păstrează separat nordul valid și localitatea validă; nimic valid → null (nedeclarat). */
export function parseSite(x: unknown): SiteInfo | null {
  const n = parseNorth(x), l = parseLocation(x);
  return n || l ? { ...(n ?? {}), ...(l ?? {}) } : null;
}
/** Nordul declarat, dacă există (Snapshot.site poate purta doar localitatea). */
export const siteNorth = (site: SiteInfo | null | undefined): SiteOrientation | null => site && typeof site.northDeg === 'number' ? { northDeg: site.northDeg } : null;
/** Localitatea declarată, dacă există. */
export const siteLocation = (site: SiteInfo | null | undefined): SiteLocation | null => site && typeof site.lat === 'number' && typeof site.lon === 'number' ? { lat: site.lat, lon: site.lon } : null;

/** Azimutul real (0 = nord, 90 = est, în sensul acelor) trecut în azimutul planului, pe care îl înțeleg lumina și soarele din 3D.
 *  Fără nord declarat (chiar dacă localitatea e declarată) rămâne convenția planului. */
export const planAzimuth = (trueAzimuthDeg: number, site: SiteInfo | null | undefined) =>
  site && typeof site.northDeg === 'number' ? (((trueAzimuthDeg + site.northDeg) % 360) + 360) % 360 : trueAzimuthDeg;

const POINTS = ['N', 'NE', 'E', 'SE', 'S', 'SV', 'V', 'NV'] as const;
const POINTS_EN = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'] as const;
/** Punctul cardinal spre care privește „sus”-ul planului (rotunjit la 45°), ca text scurt: „sus = NE”. */
export function planUpFacing(site: SiteOrientation, lang: 'ro' | 'en' = 'ro'): string {
  // sus-ul planului are azimutul real 360 − northDeg
  const az = (360 - site.northDeg) % 360, k = Math.round(az / 45) % 8;
  return (lang === 'en' ? POINTS_EN : POINTS)[k]!;
}
