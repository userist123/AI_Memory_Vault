// Poziția reală a soarelui după dată, oră și localitate. Funcții pure, fără dependențe.
//
// Algoritmul: ecuațiile din „NOAA Solar Calculator” (NOAA Global Monitoring Laboratory, „Solar Calculation Details”,
// https://gml.noaa.gov/grad/solcalc/calcdetails.html și foile de calcul NOAA_Solar_Calculations_day.xls), care urmează
// Jean Meeus, „Astronomical Algorithms” (1991/1998), cap. 25 (coordonatele Soarelui) și cap. 28 (ecuația timpului).
// Precizia declarată de NOAA: răsărit/apus în ±1 minut între latitudinile ±72°. Refracția atmosferică e aproximarea NOAA.
//
// Folosirea în aplicație e ILUSTRATIVĂ: arată de unde vine lumina la o dată și o oră, fără umbrele clădirilor vecine,
// fără relief și fără nori; nu e un studiu de însorire.
import { lighting, type LightingParams, type TimeOfDay } from './lighting';
import { planAzimuth, type SiteInfo, type SiteLocation } from './north';

const RAD = Math.PI / 180, DEG = 180 / Math.PI;
const mod = (x: number, m: number) => ((x % m) + m) % m;
const clamp1 = (x: number) => Math.max(-1, Math.min(1, x));

export interface SunPosition { /** 0 = nord, 90 = est, în sensul acelor de ceasornic, [0, 360) */ azimuthDeg: number; /** înălțimea deasupra orizontului, cu refracție; negativă = sub orizont */ elevationDeg: number }
export interface SunTimes { /** null în ziua sau noaptea polară */ sunrise: Date | null; sunset: Date | null; solarNoon: Date; polar: 'day' | 'night' | null }

/** Declinația (grade) și ecuația timpului (minute) la un moment dat. */
function solar(ms: number): { decl: number; eot: number } {
  const T = (ms / 86400000 + 2440587.5 - 2451545) / 36525;
  const L0 = mod(280.46646 + T * (36000.76983 + T * 0.0003032), 360);
  const M = 357.52911 + T * (35999.05029 - 0.0001537 * T);
  const e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T);
  const C = Math.sin(M * RAD) * (1.914602 - T * (0.004817 + 0.000014 * T)) + Math.sin(2 * M * RAD) * (0.019993 - 0.000101 * T) + Math.sin(3 * M * RAD) * 0.000289;
  const omega = 125.04 - 1934.136 * T;
  const lambda = L0 + C - 0.00569 - 0.00478 * Math.sin(omega * RAD);
  const eps = 23 + (26 + (21.448 - T * (46.815 + T * (0.00059 - T * 0.001813))) / 60) / 60 + 0.00256 * Math.cos(omega * RAD);
  const decl = Math.asin(clamp1(Math.sin(eps * RAD) * Math.sin(lambda * RAD))) * DEG;
  const y = Math.tan(eps * RAD / 2) ** 2;
  const eot = 4 * DEG * (y * Math.sin(2 * L0 * RAD) - 2 * e * Math.sin(M * RAD) + 4 * e * y * Math.sin(M * RAD) * Math.cos(2 * L0 * RAD)
    - 0.5 * y * y * Math.sin(4 * L0 * RAD) - 1.25 * e * e * Math.sin(2 * M * RAD));
  return { decl, eot };
}

/** Corecția de refracție (grade) pentru înălțimea geometrică h (grade) — aproximarea NOAA. */
function refraction(h: number): number {
  if (h > 85) return 0;
  const t = Math.tan(h * RAD);
  if (h > 5) return (58.1 / t - 0.07 / t ** 3 + 0.000086 / t ** 5) / 3600;
  if (h > -0.575) return (1735 + h * (-518.2 + h * (103.4 + h * (-12.79 + h * 0.711)))) / 3600;
  return (-20.772 / t) / 3600;
}

/** Azimutul (0 = nord, în sensul acelor) și înălțimea soarelui la momentul UTC dat, pentru latitudinea și longitudinea date (grade; est pozitiv). */
export function sunPosition(dateUtc: Date, latDeg: number, lonDeg: number): SunPosition {
  const ms = dateUtc.getTime(), { decl, eot } = solar(ms);
  const minutes = mod(ms, 86400000) / 60000;
  const tst = mod(minutes + eot + 4 * lonDeg, 1440);                 // timpul solar adevărat, minute
  const ha = tst / 4 < 0 ? tst / 4 + 180 : tst / 4 - 180;            // unghiul orar, grade (negativ dimineața)
  const sl = Math.sin(latDeg * RAD), cl = Math.cos(latDeg * RAD), sd = Math.sin(decl * RAD), cd = Math.cos(decl * RAD);
  const cz = clamp1(sl * sd + cl * cd * Math.cos(ha * RAD)), zen = Math.acos(cz) * DEG, h = 90 - zen;
  const den = cl * Math.sin(zen * RAD);
  let az: number;
  if (Math.abs(den) < 1e-9) az = latDeg > decl ? 180 : 0;            // la zenit sau la poli azimutul nu e definit: convenție
  else { const a = Math.acos(clamp1((sl * cz - sd) / den)) * DEG; az = ha > 0 ? mod(a + 180, 360) : mod(540 - a, 360); }
  return { azimuthDeg: az, elevationDeg: h + refraction(h) };
}

/** Soarele e deasupra orizontului (cu refracție)? */
export const isDay = (dateUtc: Date, latDeg: number, lonDeg: number) => sunPosition(dateUtc, latDeg, lonDeg).elevationDeg > 0;

/** Răsăritul, apusul și amiaza solară pentru ziua UTC a datei primite (zenit 90,833°: refracție + raza discului). Aproximativ, ±câteva minute. */
export function sunTimes(date: Date, latDeg: number, lonDeg: number): SunTimes {
  const day0 = Math.floor(date.getTime() / 86400000) * 86400000;
  const at = (min: number) => new Date(day0 + Math.round(min * 60000));
  const noonMin = (ms: number) => 720 - 4 * lonDeg - solar(ms).eot;
  let noon = noonMin(day0 + 43200000); noon = noonMin(day0 + noon * 60000);
  const edge = (sign: -1 | 1): number | 'day' | 'night' => { let m = noon;
    for (let i = 0; i < 3; i++){ const { decl, eot } = solar(day0 + m * 60000);
      const c = Math.cos(90.833 * RAD) / (Math.cos(latDeg * RAD) * Math.cos(decl * RAD)) - Math.tan(latDeg * RAD) * Math.tan(decl * RAD);
      if (!Number.isFinite(c) || c < -1) return 'day'; if (c > 1) return 'night';
      m = 720 - 4 * lonDeg - eot + sign * 4 * Math.acos(c) * DEG; }
    return m; };
  const up = edge(-1), down = edge(1);
  if (typeof up !== 'number' || typeof down !== 'number') return { sunrise: null, sunset: null, solarNoon: at(noon), polar: typeof up !== 'number' ? up : down as 'day' | 'night' };
  return { sunrise: at(up), sunset: at(down), solarNoon: at(noon), polar: null };
}

// ---------- localitatea ----------
// Coordonatele și validarea lor stau în core/north.ts (lângă nord, pe Snapshot.site).
export { parseLocation, type SiteLocation } from './north';

/** Fusul orar al localității, când îl putem numi fără bază de date: dreptunghiul României (și vecinii estici cu aceeași oră).
 *  În afara lui întoarce null, iar ora se citește în fusul dispozitivului. Aproximare: la vest de graniță (Ungaria, Serbia) ar fi cu o oră în minus. */
export const siteTimeZone = (loc: SiteLocation): string | null =>
  loc.lat >= 43.5 && loc.lat <= 48.4 && loc.lon >= 20.2 && loc.lon <= 30 ? 'Europe/Bucharest' : null;

/** Decalajul față de UTC (ms) al fusului dat la un moment dat, citit din Intl (ține cont de ora de vară). */
function zoneOffsetMs(ms: number, timeZone: string): number {
  const p = new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' }).formatToParts(new Date(ms));
  const g = (k: string) => Number(p.find(x => x.type === k)?.value);
  return Date.UTC(g('year'), g('month') - 1, g('day'), g('hour') % 24, g('minute'), g('second')) - Math.floor(ms / 1000) * 1000;
}
/** Momentul UTC pentru o dată „AAAA-LL-ZZ” și o oră zecimală (14.5 = 14:30) citite în fusul dat; fără fus, în cel al dispozitivului. Dată invalidă → null. */
export function civilToUtc(dateIso: string, hour: number, timeZone?: string | null): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateIso); if (!m || !Number.isFinite(hour)) return null;
  const y = Number(m[1]), mo = Number(m[2]), d = Number(m[3]), mins = Math.round(hour * 60);
  if (mo < 1 || mo > 12 || d < 1 || d > 31) return null;
  if (!timeZone){ const l = new Date(y, mo - 1, d, 0, mins); return Number.isFinite(l.getTime()) ? l : null; }
  const wall = Date.UTC(y, mo - 1, d, 0, mins);
  let utc = wall - zoneOffsetMs(wall, timeZone); utc = wall - zoneOffsetMs(utc, timeZone);
  return new Date(utc);
}

// ---------- lumina din 3D ----------
/** Sub această înălțime lumina e cea de seară (soare jos, cald). */
export const LOW_SUN_DEG = 10;
/** Momentul zilei dedus din înălțimea reală: ≤ 0 = noapte, jos = seară (și dimineață), altfel zi. */
export const timeOfDayFor = (elevationDeg: number): TimeOfDay => elevationDeg <= 0 ? 'night' : elevationDeg < LOW_SUN_DEG ? 'evening' : 'day';

/** Parametrii de lumină pentru soarele real: aceeași cale ca lumina obișnuită (lighting + planAzimuth), cu azimutul și înălțimea reale.
 *  Noaptea rămâne iluminatul de noapte existent, neschimbat. Direcția luminii nu coboară sub 2° (umbre degenerate la răsărit/apus). */
export function realSunLighting(pos: SunPosition, site: SiteInfo | null | undefined): LightingParams {
  const time = timeOfDayFor(pos.elevationDeg), p = lighting(time, planAzimuth(pos.azimuthDeg, site));
  return time === 'night' ? p : { ...p, elevationDeg: Math.max(2, pos.elevationDeg) };
}
