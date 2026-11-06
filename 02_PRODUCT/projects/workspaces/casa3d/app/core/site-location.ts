// Amplasamentul proiectului pentru studiul de însorire: coordonatele (rotunjite la 0,01°, ~1 km: destul pentru soare,
// fără să dea adresa exactă pe linkul clientului), fusul orar și orientarea nordului față de plan.
// Convenția planului: x = est, z = sud când `northDeg` = 0 (nordul „în sus” pe plan, spre -z).
// `northDeg` = unghiul, în sensul acelor de ceasornic, de la „sus pe plan” la nordul adevărat.
import { sunDirection } from './lighting';
import { validTimeZone } from './solar';

export interface SiteInfo { lat: number; lon: number; northDeg: number; tz: string; /** id din PLACES, dacă a fost ales un oraș */ place?: string }
export interface Place { id: string; name: string; lat: number; lon: number; tz: string }
/** Centrele orașelor, coordonate aproximative (±0,05°): suficient pentru poziția soarelui (eroare sub 0,1°). */
export const PLACES: Place[] = ([
  ['bucuresti', 'București', 44.43, 26.10], ['cluj', 'Cluj-Napoca', 46.77, 23.59], ['timisoara', 'Timișoara', 45.75, 21.23], ['iasi', 'Iași', 47.16, 27.59],
  ['constanta', 'Constanța', 44.18, 28.63], ['brasov', 'Brașov', 45.65, 25.61], ['craiova', 'Craiova', 44.32, 23.80], ['galati', 'Galați', 45.44, 28.05],
  ['ploiesti', 'Ploiești', 44.94, 26.03], ['oradea', 'Oradea', 47.07, 21.92], ['sibiu', 'Sibiu', 45.79, 24.15], ['arad', 'Arad', 46.18, 21.31],
  ['pitesti', 'Pitești', 44.86, 24.87], ['bacau', 'Bacău', 46.57, 26.91], ['suceava', 'Suceava', 47.65, 26.26], ['baiamare', 'Baia Mare', 47.66, 23.58],
  ['targumures', 'Târgu Mureș', 46.54, 24.56], ['buzau', 'Buzău', 45.15, 26.82], ['satumare', 'Satu Mare', 47.79, 22.89], ['botosani', 'Botoșani', 47.75, 26.67],
  ['valcea', 'Râmnicu Vâlcea', 45.10, 24.37], ['severin', 'Drobeta-Turnu Severin', 44.63, 22.66], ['piatraneamt', 'Piatra Neamț', 46.93, 26.37],
  ['albaiulia', 'Alba Iulia', 46.07, 23.58], ['deva', 'Deva', 45.88, 22.90], ['tulcea', 'Tulcea', 45.18, 28.80], ['focsani', 'Focșani', 45.70, 27.18],
  ['braila', 'Brăila', 45.27, 27.96], ['targoviste', 'Târgoviște', 44.93, 25.46], ['zalau', 'Zalău', 47.19, 23.06], ['chisinau', 'Chișinău', 47.01, 28.86, 'Europe/Chisinau'],
] as [string, string, number, number, string?][]).map(([id, name, lat, lon, tz]) => ({ id, name, lat, lon, tz: tz ?? 'Europe/Bucharest' }));
export const DEFAULT_SITE: SiteInfo = { lat: 44.43, lon: 26.10, northDeg: 0, tz: 'Europe/Bucharest', place: 'bucuresti' };
const TZ_RE = /^[A-Za-z]+(?:\/[A-Za-z0-9_+-]+){1,2}$/;
const r2 = (v: number) => Math.round(v * 100) / 100;

export class SiteError extends Error {}
/** Validează amplasamentul venit din interfață sau din API; aruncă SiteError cu un mesaj clar. */
export function checkSite(x: unknown): SiteInfo {
  if (!x || typeof x !== 'object' || Array.isArray(x)) throw new SiteError('Amplasamentul e invalid.');
  const o = x as any, num = (v: unknown, lo: number, hi: number, what: string) => { if (typeof v !== 'number' || !Number.isFinite(v) || v < lo || v > hi) throw new SiteError(`${what} e în afara intervalului.`); return v; };
  const lat = r2(num(o.lat, -66, 66, 'Latitudinea')), lon = r2(num(o.lon, -180, 180, 'Longitudinea'));
  const northDeg = Math.round(((num(o.northDeg, -360, 720, 'Orientarea nordului') % 360) + 360) % 360);
  // fusul lipsă = România; un fus dat dar necunoscut e refuzat (altfel orele ar ieși greșite fără niciun semn)
  if (o.tz != null && (typeof o.tz !== 'string' || o.tz.length > 64 || !TZ_RE.test(o.tz) || !validTimeZone(o.tz))) throw new SiteError('Fusul orar e necunoscut.');
  const tz: string = o.tz ?? 'Europe/Bucharest';
  const out: SiteInfo = { lat, lon, northDeg, tz };
  if (typeof o.place === 'string' && PLACES.some(p => p.id === o.place)) out.place = o.place;
  return out;
}
/** Citire tolerantă: date vechi sau stricate nu opresc editorul (fără amplasament = studiul e oprit). */
export function readSite(x: unknown): SiteInfo | null { if (x == null) return null; try { return checkSite(x); } catch { return null; } }
/** Azimutul față de plan (0 = sus pe plan, 90 = dreapta) pentru un azimut adevărat (0 = nord). */
export const planAzimuth = (trueAzimuthDeg: number, site: Pick<SiteInfo, 'northDeg'>) => ((trueAzimuthDeg - site.northDeg) % 360 + 360) % 360;
/** Direcția spre soare în coordonatele planului/scenei (x, y = sus, z). */
export const sunVector = (trueAzimuthDeg: number, elevationDeg: number, site: Pick<SiteInfo, 'northDeg'>) => sunDirection(planAzimuth(trueAzimuthDeg, site), elevationDeg);
/** Punctul cardinal (N, NE, E…) spre care privește o normală de pe plan, ținând cont de nord. */
export function facing(nx: number, nz: number, site: Pick<SiteInfo, 'northDeg'>): 'N' | 'NE' | 'E' | 'SE' | 'S' | 'SW' | 'W' | 'NW' {
  const planAz = Math.atan2(nx, -nz) * 180 / Math.PI, az = ((planAz + site.northDeg) % 360 + 360) % 360;
  return (['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'] as const)[Math.round(az / 45) % 8]!;
}
