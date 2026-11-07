// Poziția reală a soarelui pentru un loc și un moment (algoritmul NOAA, după Meeus, „Astronomical Algorithms”).
// Precizie de ordinul 0,01° pentru anii 1950–2050, mai mult decât suficient pentru un studiu de însorire.
// Pur: fără DOM, fără ceas implicit; ora locală se transformă în UTC cu Intl (fusul orar al amplasamentului).

export interface SunPosition { /** azimut adevărat, grade de la nord în sensul acelor de ceasornic (90 = est) */ azimuthDeg: number;
  /** înălțimea deasupra orizontului, grade, cu refracția atmosferică */ elevationDeg: number }
export interface SunDay { sunrise: Date | null; sunset: Date | null; solarNoon: Date; dayLengthH: number; noonElevationDeg: number }

const RAD = Math.PI / 180, DEG = 180 / Math.PI;
const norm360 = (a: number) => ((a % 360) + 360) % 360;

function julianCentury(d: Date){ return (d.getTime() / 864e5 + 2440587.5 - 2451545) / 36525; }
/** Declinația (°) și ecuația timpului (minute) la momentul dat. */
function sunCore(T: number){
  const L0 = norm360(280.46646 + T * (36000.76983 + T * 0.0003032));
  const M = 357.52911 + T * (35999.05029 - 0.0001537 * T);
  const e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T);
  const C = Math.sin(M * RAD) * (1.914602 - T * (0.004817 + 0.000014 * T)) + Math.sin(2 * M * RAD) * (0.019993 - 0.000101 * T) + Math.sin(3 * M * RAD) * 0.000289;
  const trueLong = L0 + C, omega = 125.04 - 1934.136 * T;
  const lambda = trueLong - 0.00569 - 0.00478 * Math.sin(omega * RAD);
  const eps0 = 23 + (26 + (21.448 - T * (46.815 + T * (0.00059 - T * 0.001813))) / 60) / 60;
  const eps = eps0 + 0.00256 * Math.cos(omega * RAD);
  const decl = Math.asin(Math.sin(eps * RAD) * Math.sin(lambda * RAD)) * DEG;
  const y = Math.tan(eps / 2 * RAD) ** 2;
  const eqTime = 4 * DEG * (y * Math.sin(2 * L0 * RAD) - 2 * e * Math.sin(M * RAD) + 4 * e * y * Math.sin(M * RAD) * Math.cos(2 * L0 * RAD)
    - 0.5 * y * y * Math.sin(4 * L0 * RAD) - 1.25 * e * e * Math.sin(2 * M * RAD));
  return { decl, eqTime };
}
/** Refracția atmosferică (°) pentru înălțimea geometrică dată (formula NOAA). */
function refraction(el: number){
  if (el > 85) return 0;
  const te = Math.tan(el * RAD);
  const arcsec = el > 5 ? 58.1 / te - 0.07 / te ** 3 + 0.000086 / te ** 5 : el > -0.575 ? 1735 + el * (-518.2 + el * (103.4 + el * (-12.79 + el * 0.711))) : -20.772 / te;
  return arcsec / 3600;
}
/** Poziția soarelui la momentul `when` (UTC) pentru latitudinea și longitudinea date (°, est pozitiv). */
export function sunPosition(when: Date, lat: number, lon: number): SunPosition {
  const { decl, eqTime } = sunCore(julianCentury(when));
  const utcMin = when.getUTCHours() * 60 + when.getUTCMinutes() + when.getUTCSeconds() / 60 + when.getUTCMilliseconds() / 6e4;
  let ha = (utcMin + eqTime + 4 * lon) / 4 - 180; if (ha < -180) ha += 360;
  const cz = Math.min(1, Math.max(-1, Math.sin(lat * RAD) * Math.sin(decl * RAD) + Math.cos(lat * RAD) * Math.cos(decl * RAD) * Math.cos(ha * RAD)));
  const zen = Math.acos(cz) * DEG, el = 90 - zen;
  const az = Math.abs(Math.sin(zen * RAD)) < 1e-9 ? 180 : norm360(Math.atan2(Math.sin(ha * RAD), Math.cos(ha * RAD) * Math.sin(lat * RAD) - Math.tan(decl * RAD) * Math.cos(lat * RAD)) * DEG + 180);
  return { azimuthDeg: az, elevationDeg: el + refraction(el) };
}
/** Răsăritul, apusul și amiaza solară a zilei solare cea mai apropiată de momentul `day` (dați amiaza locală);
 *  merge și în fusurile departe de Greenwich, unde amiaza locală cade în altă zi UTC. Calcul iterativ, două treceri. */
export function sunDay(day: Date, lat: number, lon: number): SunDay {
  let mid = Date.UTC(day.getUTCFullYear(), day.getUTCMonth(), day.getUTCDate());
  const approxNoon = mid + (720 - 4 * lon) * 6e4; mid += Math.round((day.getTime() - approxNoon) / 864e5) * 864e5;
  const at = (min: number) => new Date(mid + min * 6e4);
  let noonMin = 720 - 4 * lon - sunCore(julianCentury(at(720))).eqTime;
  noonMin = 720 - 4 * lon - sunCore(julianCentury(at(noonMin))).eqTime;
  const solarNoon = at(noonMin), noonElevationDeg = sunPosition(solarNoon, lat, lon).elevationDeg;
  const event = (sign: 1 | -1) => { let m = noonMin;
    for (let i = 0; i < 2; i++){ const { decl } = sunCore(julianCentury(at(m)));
      const c = (Math.cos(90.833 * RAD) - Math.sin(lat * RAD) * Math.sin(decl * RAD)) / (Math.cos(lat * RAD) * Math.cos(decl * RAD));
      if (c > 1 || c < -1) return { m: null, polar: c > 1 ? 'night' as const : 'day' as const };
      m = noonMin + sign * 4 * Math.acos(c) * DEG; }
    return { m, polar: null }; };
  const r = event(-1), s = event(1);
  const dayLengthH = r.m != null && s.m != null ? (s.m - r.m) / 60 : r.polar === 'day' ? 24 : 0;
  return { sunrise: r.m != null ? at(r.m) : null, sunset: s.m != null ? at(s.m) : null, solarNoon, dayLengthH, noonElevationDeg };
}

/** Fusul orar e cunoscut de motorul Intl (ex. „Europe/Bucharest”). */
export function validTimeZone(tz: string): boolean { try { new Intl.DateTimeFormat('en-US', { timeZone: tz }); return true; } catch { return false; } }
/** Diferența (minute) dintre ora locală din fusul `tz` și UTC la momentul dat. Fusul necunoscut aruncă RangeError (fără
 *  întoarcere tăcută la UTC: amplasamentul se validează la salvare cu validTimeZone). */
const FMT = new Map<string, Intl.DateTimeFormat>();
export function tzOffsetMin(when: Date, tz: string): number {
  { let f = FMT.get(tz); if (!f){ f = new Intl.DateTimeFormat('en-US', { timeZone: tz, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' }); FMT.set(tz, f); }
    const p: Record<string, number> = {}; for (const x of f.formatToParts(when)) if (x.type !== 'literal') p[x.type] = Number(x.value);
    return Math.round((Date.UTC(p.year!, p.month! - 1, p.day!, p.hour! % 24, p.minute!, p.second!) - Math.floor(when.getTime() / 1000) * 1000) / 6e4); }
}
/** Momentul UTC pentru data (AAAA-LL-ZZ) și ora locală (minute de la miezul nopții) din fusul `tz`. Ora de vară inclusă. */
export function localToUtc(date: string, minutes: number, tz: string): Date {
  const [y, mo, d] = date.split('-').map(Number), guess = Date.UTC(y!, mo! - 1, d!, 0, minutes);
  let t = guess - tzOffsetMin(new Date(guess), tz) * 6e4;
  t = guess - tzOffsetMin(new Date(t), tz) * 6e4; // a doua trecere: corect și în ziua schimbării orei
  return new Date(t);
}
/** Ora locală (minute de la miezul nopții) a momentului `when` în fusul `tz`. */
export function utcToLocalMin(when: Date, tz: string): number { const m = Math.round(when.getTime() / 6e4 + tzOffsetMin(when, tz)); return ((m % 1440) + 1440) % 1440; }
