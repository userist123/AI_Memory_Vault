// Soarele real (core/sun.ts): algoritmul NOAA, validarea localității, conversia la azimutul planului cu nordul declarat.
//
// De unde vin valorile de referință (București, 44,43° N / 26,10° E):
//  - identități geometrice: la amiaza solară azimutul e 180°, iar înălțimea e 90° − latitudine + declinație
//    (solstițiul de vară: 90 − 44,43 + 23,44 = 69,01°; de iarnă: 90 − 44,43 − 23,44 = 22,13° + refracție);
//  - o a doua formulă, independentă de cea NOAA: algoritmul de precizie redusă din „The Astronomical Almanac”
//    (USNO, secțiunea C, „Low precision formulas for the Sun”), implementat mai jos doar pentru verificare;
//  - orele de răsărit/apus din almanahurile publice pentru București (solstiții 2024), cu toleranța de ±5 minute.
// Valorile fixe de azimut/înălțime au fost calculate cu ecuațiile NOAA și confirmate de formula independentă; toleranța e ±1°.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { sunPosition, sunTimes, isDay, civilToUtc, siteTimeZone, timeOfDayFor, realSunLighting, LOW_SUN_DEG } from '../core/sun';
import { parseLocation, parseSite, siteNorth, siteLocation, planAzimuth } from '../core/north';
import { lighting, sunDirection } from '../core/lighting';
import { RO_CITIES, cityAt } from '../core/ro-cities';

const BUC = [44.43, 26.10] as const, R = Math.PI / 180;
const near = (a: number, b: number, tol: number, msg = '') => assert.ok(Math.abs(a - b) <= tol, `${msg} ${a} ≠ ${b} (±${tol})`);
const azNear = (a: number, b: number, tol: number, msg = '') => near(Math.abs(((a - b + 540) % 360) - 180), 0, tol, `${msg} azimut ${a} vs ${b}`);
const minutes = (d: Date | null, iso: string) => Math.abs(d!.getTime() - new Date(iso).getTime()) / 60000;

/** Formula independentă (Astronomical Almanac, precizie redusă ~0,01° în intervalul 1950–2050), fără refracție. */
function almanac(d: Date, lat: number, lon: number){ const n = d.getTime() / 86400000 + 2440587.5 - 2451545;
  const L = 280.460 + 0.9856474 * n, g = (357.528 + 0.9856003 * n) * R, lam = (L + 1.915 * Math.sin(g) + 0.020 * Math.sin(2 * g)) * R, eps = (23.439 - 0.0000004 * n) * R;
  const ra = Math.atan2(Math.cos(eps) * Math.sin(lam), Math.cos(lam)), dec = Math.asin(Math.sin(eps) * Math.sin(lam));
  const H = (18.697374558 + 24.06570982441908 * n) * 15 * R + lon * R - ra;
  const el = Math.asin(Math.sin(lat * R) * Math.sin(dec) + Math.cos(lat * R) * Math.cos(dec) * Math.cos(H));
  const az = Math.atan2(-Math.sin(H) * Math.cos(dec), Math.cos(lat * R) * Math.sin(dec) - Math.sin(lat * R) * Math.cos(dec) * Math.cos(H));
  return { az: ((az / R) % 360 + 360) % 360, el: el / R }; }

// ---------- algoritmul ----------
test('sunPosition: valori de referință pentru București (±1°)', () => {
  const ref: [string, number, number][] = [
    ['2024-06-21T09:00:00Z', 136.6, 63.7],   // 12:00 ora de vară, solstițiu: sus, spre sud-est
    ['2024-06-21T06:00:00Z', 89.6, 34.2],    // 09:00 ora de vară: aproape exact la est
    ['2024-06-21T15:00:00Z', 274.7, 29.8],   // 18:00 ora de vară: la vest
    ['2024-12-21T10:00:00Z', 176.6, 22.1],   // 12:00 iarna: jos, aproape de sud
    ['2024-03-20T10:00:00Z', 171.8, 45.4],   // echinocțiu, spre prânz
    ['2026-01-15T07:00:00Z', 132.5, 9.6],    // 09:00 în ianuarie: soare jos la sud-est
  ];
  for (const [iso, az, el] of ref){ const p = sunPosition(new Date(iso), ...BUC); azNear(p.azimuthDeg, az, 1, iso); near(p.elevationDeg, el, 1, iso); }
});
test('sunPosition: la amiaza solară soarele e la sud, la 90° − latitudine + declinație', () => {
  const june = sunPosition(sunTimes(new Date('2024-06-21T12:00:00Z'), ...BUC).solarNoon, ...BUC);
  azNear(june.azimuthDeg, 180, 0.5); near(june.elevationDeg, 90 - 44.43 + 23.44, 0.1);
  const dec = sunPosition(sunTimes(new Date('2024-12-21T12:00:00Z'), ...BUC).solarNoon, ...BUC);
  azNear(dec.azimuthDeg, 180, 0.5); near(dec.elevationDeg, 90 - 44.43 - 23.44, 0.1);
});
test('sunPosition: se potrivește cu formula independentă din Astronomical Almanac pe trei ani (±0,3°)', () => {
  let n = 0;
  for (let t = Date.UTC(2024, 0, 1); t < Date.UTC(2027, 0, 1); t += 3.7 * 3600000){ const d = new Date(t), a = almanac(d, ...BUC); if (a.el < 5) continue;   // lângă orizont diferă refracția
    const p = sunPosition(d, ...BUC); n++;
    near(Math.abs(((p.azimuthDeg - a.az + 540) % 360) - 180) * Math.cos(a.el * R), 0, 0.3, d.toISOString()); near(p.elevationDeg, a.el, 0.3, d.toISOString()); }
  assert.ok(n > 2000, String(n));
});
test('sunPosition: azimutul e în [0, 360), dimineața la est și seara la vest; emisfera sudică are soarele la nord', () => {
  for (let h = 0; h < 24; h++){ const p = sunPosition(new Date(Date.UTC(2025, 4, 10, h)), ...BUC); assert.ok(p.azimuthDeg >= 0 && p.azimuthDeg < 360 && Number.isFinite(p.elevationDeg)); }
  assert.ok(sunPosition(new Date('2025-05-10T05:00:00Z'), ...BUC).azimuthDeg < 180);
  assert.ok(sunPosition(new Date('2025-05-10T15:00:00Z'), ...BUC).azimuthDeg > 180);
  const syd = sunPosition(new Date('2024-06-21T02:00:00Z'), -33.87, 151.21); azNear(syd.azimuthDeg, 0, 10); near(syd.elevationDeg, 90 - 33.87 - 23.44, 1.5);
  // la pol azimutul nu e definit: rămâne un număr, nu NaN
  const pole = sunPosition(new Date('2024-06-21T12:00:00Z'), 90, 0); assert.ok(Number.isFinite(pole.azimuthDeg)); near(pole.elevationDeg, 23.44, 0.2);
});
test('sunTimes: răsăritul și apusul la București, față de almanahul public (±5 min)', () => {
  const s = sunTimes(new Date('2024-06-21T12:00:00Z'), ...BUC);       // 05:31 și 21:03 ora de vară (UTC+3)
  assert.ok(minutes(s.sunrise, '2024-06-21T02:31:00Z') <= 5); assert.ok(minutes(s.sunset, '2024-06-21T18:03:00Z') <= 5); assert.equal(s.polar, null);
  const w = sunTimes(new Date('2024-12-21T12:00:00Z'), ...BUC);       // 07:48 și 16:38 ora de iarnă (UTC+2)
  assert.ok(minutes(w.sunrise, '2024-12-21T05:48:00Z') <= 5); assert.ok(minutes(w.sunset, '2024-12-21T14:38:00Z') <= 5);
  // la echinocțiu ziua are ~12 ore (plus refracția), iar amiaza e la mijloc
  const e = sunTimes(new Date('2024-03-20T12:00:00Z'), ...BUC), len = (e.sunset!.getTime() - e.sunrise!.getTime()) / 3600000;
  near(len, 12.15, 0.1); near((e.sunrise!.getTime() + e.sunset!.getTime()) / 2, e.solarNoon.getTime(), 60000);
  // la momentul răsăritului soarele e chiar la orizont
  near(sunPosition(s.sunrise!, ...BUC).elevationDeg, 0, 0.6); near(sunPosition(w.sunset!, ...BUC).elevationDeg, 0, 0.6);
});
test('sunTimes: ziua și noaptea polară nu au răsărit/apus; isDay urmează înălțimea', () => {
  assert.deepEqual([sunTimes(new Date('2024-06-21T12:00:00Z'), 78, 15).polar, sunTimes(new Date('2024-12-21T12:00:00Z'), 78, 15).polar], ['day', 'night']);
  assert.equal(sunTimes(new Date('2024-06-21T12:00:00Z'), 78, 15).sunrise, null);
  assert.equal(isDay(new Date('2024-06-21T09:00:00Z'), ...BUC), true);
  assert.equal(isDay(new Date('2024-06-21T22:00:00Z'), ...BUC), false);   // 01:00 noaptea
  assert.equal(isDay(new Date('2024-12-21T15:30:00Z'), ...BUC), false);   // 17:30 iarna, după apus
});

// ---------- ora civilă ----------
test('civilToUtc: data și ora României, cu ora de vară; fus necunoscut = cel al dispozitivului; dată invalidă = null', () => {
  assert.equal(civilToUtc('2024-06-21', 12, 'Europe/Bucharest')!.toISOString(), '2024-06-21T09:00:00.000Z');
  assert.equal(civilToUtc('2024-12-21', 7.5, 'Europe/Bucharest')!.toISOString(), '2024-12-21T05:30:00.000Z');
  const local = civilToUtc('2024-06-21', 14.25, null)!; assert.deepEqual([local.getFullYear(), local.getMonth(), local.getDate(), local.getHours(), local.getMinutes()], [2024, 5, 21, 14, 15]);
  for (const bad of ['', '21.06.2024', '2024-13-01', '2024-06-00']) assert.equal(civilToUtc(bad, 12, 'Europe/Bucharest'), null, bad);
  assert.equal(civilToUtc('2024-06-21', NaN, 'Europe/Bucharest'), null);
  assert.equal(siteTimeZone({ lat: 44.43, lon: 26.1 }), 'Europe/Bucharest'); assert.equal(siteTimeZone({ lat: 45.76, lon: 21.23 }), 'Europe/Bucharest');
  assert.equal(siteTimeZone({ lat: 48.86, lon: 2.35 }), null);
});

// ---------- localitatea ----------
test('parseLocation: ambele coordonate finite, în intervalele geografice, la 0,01°; orice altceva = nedeclarată', () => {
  assert.deepEqual(parseLocation({ lat: 44.4268, lon: 26.1025 }), { lat: 44.43, lon: 26.1 });
  assert.deepEqual(parseLocation({ lat: -33.868, lon: 151.209, extra: 1 }), { lat: -33.87, lon: 151.21 });
  assert.deepEqual(parseLocation({ lat: 90, lon: -180 }), { lat: 90, lon: -180 });
  assert.deepEqual(parseLocation({ lat: -0.001, lon: 0 }), { lat: 0, lon: 0 });
  for (const bad of [null, undefined, 5, 'x', {}, { lat: 44.43 }, { lon: 26.1 }, { lat: '44.43', lon: 26.1 }, { lat: NaN, lon: 26.1 }, { lat: 44.43, lon: Infinity }, { lat: 90.01, lon: 0 }, { lat: -91, lon: 0 }, { lat: 0, lon: 180.5 }, { lat: 0, lon: -181 }])
    assert.equal(parseLocation(bad), null, JSON.stringify(bad));
});
test('parseSite: nordul și localitatea se păstrează separat, fiecare doar dacă e valid; nimic nu se presupune', () => {
  assert.deepEqual(parseSite({ northDeg: -45, lat: 44.4268, lon: 26.1025, city: 'x' }), { northDeg: 315, lat: 44.43, lon: 26.1 });
  assert.deepEqual(parseSite({ northDeg: 30 }), { northDeg: 30 });
  assert.deepEqual(parseSite({ lat: 46.77, lon: 23.6 }), { lat: 46.77, lon: 23.6 });
  assert.deepEqual(parseSite({ northDeg: 30, lat: 46.77 }), { northDeg: 30 }, 'o singură coordonată nu e o localitate');
  assert.deepEqual(parseSite({ northDeg: 'nord', lat: 46.77, lon: 23.6 }), { lat: 46.77, lon: 23.6 });
  for (const bad of [null, undefined, 7, {}, { lat: 200, lon: 0 }]) assert.equal(parseSite(bad), null, JSON.stringify(bad));
  assert.deepEqual(siteNorth({ lat: 46.77, lon: 23.6 }), null); assert.deepEqual(siteNorth({ northDeg: 0 }), { northDeg: 0 });
  assert.deepEqual(siteLocation({ northDeg: 30 }), null); assert.deepEqual(siteLocation({ northDeg: 30, lat: 0, lon: 0 }), { lat: 0, lon: 0 });
});
test('orașele: București și cele 40 de reședințe de județ, în România, fără dubluri, deja rotunjite la 0,01°', () => {
  assert.equal(RO_CITIES.length, 41); assert.equal(RO_CITIES[0]!.name, 'București');
  assert.equal(new Set(RO_CITIES.map(c => c.name)).size, 41); assert.equal(new Set(RO_CITIES.map(c => `${c.lat},${c.lon}`)).size, 41);
  for (const c of RO_CITIES){ assert.deepEqual(parseLocation(c), { lat: c.lat, lon: c.lon }, c.name); assert.equal(siteTimeZone(c), 'Europe/Bucharest', c.name);
    assert.ok(c.lat >= 43.6 && c.lat <= 48.3 && c.lon >= 20.2 && c.lon <= 29.7, c.name); assert.equal(cityAt(c)?.name, c.name); }
  assert.equal(cityAt({ lat: 44.43, lon: 26.1 })?.name, 'București'); assert.equal(cityAt({ lat: 45, lon: 25 }), null); assert.equal(cityAt(null), null);
});

// ---------- lumina din 3D ----------
test('conversia la azimutul planului: soarele real se rotește cu nordul declarat; fără nord rămâne convenția planului', () => {
  const pos = sunPosition(new Date('2024-06-21T06:00:00Z'), ...BUC);     // ~89,6°: la est
  near(planAzimuth(pos.azimuthDeg, { northDeg: 0 }), pos.azimuthDeg, 1e-9);
  near(planAzimuth(pos.azimuthDeg, { northDeg: 90 }), pos.azimuthDeg + 90, 1e-9);       // nordul spre dreapta planului: estul cade în jos
  near(planAzimuth(pos.azimuthDeg, { northDeg: 300, lat: 44.43, lon: 26.1 }), (pos.azimuthDeg + 300) % 360, 1e-9);
  assert.equal(planAzimuth(135, { lat: 44.43, lon: 26.1 }), 135, 'localitatea singură nu rotește nimic');
  // în scenă (x = est, z = sud pe convenția planului): cu nordul la 90°, soarele de la est vine dinspre +z (josul planului)
  const p = realSunLighting(pos, { northDeg: 90 }), dir = sunDirection(p.azimuthDeg, p.elevationDeg);
  near(p.azimuthDeg, pos.azimuthDeg + 90, 1e-9); near(p.elevationDeg, pos.elevationDeg, 1e-9);
  assert.ok(dir[2] > 0.8 && Math.abs(dir[0]) < 0.05 && dir[1] > 0.5, dir.join(','));
});
test('realSunLighting: zi, soare jos și noapte folosesc iluminatul existent; elevație ≤ 0 = noapte', () => {
  assert.deepEqual([timeOfDayFor(40), timeOfDayFor(LOW_SUN_DEG - 0.1), timeOfDayFor(0.1), timeOfDayFor(0), timeOfDayFor(-12)], ['day', 'evening', 'evening', 'night', 'night']);
  const day = realSunLighting({ azimuthDeg: 200, elevationDeg: 55 }, { northDeg: 10 });
  assert.deepEqual(day, { ...lighting('day', 210), elevationDeg: 55 });
  const low = realSunLighting({ azimuthDeg: 290, elevationDeg: 0.5 }, { northDeg: 0 });
  assert.deepEqual(low, { ...lighting('evening', 290), elevationDeg: 2 }, 'direcția luminii nu coboară sub 2°');
  // noaptea: exact iluminatul de noapte existent (cu înălțimea lui), nu un soare sub podea
  assert.deepEqual(realSunLighting({ azimuthDeg: 350, elevationDeg: -20 }, { northDeg: 0 }), lighting('night', 350));
  // o zi întreagă de vară la București: noapte → dimineață → zi → seară → noapte
  const seq = [2, 6, 12, 20, 23].map(h => timeOfDayFor(sunPosition(civilToUtc('2024-06-21', h, 'Europe/Bucharest')!, ...BUC).elevationDeg));
  assert.deepEqual(seq, ['night', 'evening', 'day', 'evening', 'night']);
});

// ---------- salvarea (lib/repo.ts) ----------
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import { checkSnapshot } from '../lib/repo';
beforeAll(() => resetDbForTests());
const base = () => ({ name: 'N', floor: { id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [], walls: [] }, placements: [], selections: {}, picked: [] });
test('checkSnapshot: localitatea trece curățată, cu sau fără nord; una invalidă dispare fără să oprească salvarea', () => {
  assert.deepEqual((checkSnapshot({ ...base(), site: { northDeg: 30, lat: 44.4268, lon: 26.1025, name: 'x' } }) as any).site, { northDeg: 30, lat: 44.43, lon: 26.1 });
  assert.deepEqual((checkSnapshot({ ...base(), site: { lat: 47.16, lon: 27.59 } }) as any).site, { lat: 47.16, lon: 27.59 });
  assert.deepEqual((checkSnapshot({ ...base(), site: { northDeg: 30, lat: 144, lon: 26.1 } }) as any).site, { northDeg: 30 });
  assert.equal('site' in (checkSnapshot({ ...base(), site: { lat: 'x', lon: 26.1 } }) as any), false);
  assert.equal('site' in (checkSnapshot(base()) as any), false);
});
