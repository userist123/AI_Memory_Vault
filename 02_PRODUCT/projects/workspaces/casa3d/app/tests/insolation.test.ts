import { test } from 'vitest';
import assert from 'node:assert/strict';
import { sunPosition, sunDay, localToUtc, utcToLocalMin, tzOffsetMin } from '../core/solar';
import { checkSite, readSite, planAzimuth, facing, PLACES, SiteError, type SiteInfo } from '../core/site-location';
import { insolationStudy, sunPatches, clipConvex, seesSky, slabsOf, NORM_HOURS } from '../core/insolation';
import type { Floor, Opening } from '../core/types';

const BUC = { lat: 44.43, lon: 26.10 }, TZ = 'Europe/Bucharest';
const toUtc = (d: string, m: number) => localToUtc(d, m, TZ);
const near = (a: number, b: number, tol: number, what: string) => assert.ok(Math.abs(a - b) <= tol, `${what}: ${a} față de ${b} (±${tol})`);
const hm = (d: Date | null) => d ? utcToLocalMin(d, TZ) : NaN;

test('soarele la amiază: înălțimea la solstiții și la echinocțiu urmează 90 − φ ± δ', () => {
  near(sunDay(new Date('2026-06-21T12:00:00Z'), BUC.lat, BUC.lon).noonElevationDeg, 90 - 44.43 + 23.44, .1, 'solstițiul de vară');
  near(sunDay(new Date('2026-12-21T12:00:00Z'), BUC.lat, BUC.lon).noonElevationDeg, 90 - 44.43 - 23.44, .15, 'solstițiul de iarnă');
  near(sunDay(new Date('2026-03-20T12:00:00Z'), BUC.lat, BUC.lon).noonElevationDeg, 90 - 44.43, .5, 'echinocțiul');
  // la amiaza solară soarele e exact la sud
  const d = sunDay(new Date('2026-12-21T12:00:00Z'), BUC.lat, BUC.lon); near(sunPosition(d.solarNoon, BUC.lat, BUC.lon).azimuthDeg, 180, .2, 'azimutul la amiază');
});
test('răsăritul, apusul și durata zilei în București (ora locală, cu ora de vară)', () => {
  const w = sunDay(toUtc('2026-12-21', 720), BUC.lat, BUC.lon), s = sunDay(toUtc('2026-06-21', 720), BUC.lat, BUC.lon);
  near(hm(w.sunrise), 7 * 60 + 49, 5, 'răsărit 21 dec'); near(hm(w.sunset), 16 * 60 + 39, 5, 'apus 21 dec');
  near(hm(s.sunrise), 5 * 60 + 32, 5, 'răsărit 21 iun'); near(hm(s.sunset), 21 * 60 + 3, 5, 'apus 21 iun');
  near(w.dayLengthH, 8.83, .1, 'ziua de iarnă'); near(s.dayLengthH, 15.52, .1, 'ziua de vară');
  near(hm(w.solarNoon), 12 * 60 + 14, 3, 'amiaza solară iarna');
  // vara soarele răsare la nord-est, iarna la sud-est
  assert.ok(sunPosition(s.sunrise!, BUC.lat, BUC.lon).azimuthDeg < 60); assert.ok(sunPosition(w.sunrise!, BUC.lat, BUC.lon).azimuthDeg > 120);
});
test('ora locală: EET iarna, EEST vara, conversie dus-întors exactă', () => {
  assert.equal(tzOffsetMin(new Date('2026-01-15T10:00:00Z'), TZ), 120); assert.equal(tzOffsetMin(new Date('2026-07-15T10:00:00Z'), TZ), 180);
  for (const [d, m] of [['2026-03-29', 600], ['2026-10-25', 600], ['2026-12-21', 735]] as const) assert.equal(utcToLocalMin(toUtc(d, m), TZ), m);
  assert.equal(toUtc('2026-12-21', 720).toISOString(), '2026-12-21T10:00:00.000Z');
  assert.throws(() => tzOffsetMin(new Date(), 'Nu/Exista'), RangeError);
});
test('amplasamentul: validare, rotunjire la 0,01° (fără adresă exactă pe link) și nord normalizat', () => {
  const s = checkSite({ lat: 44.426767, lon: 26.102538, northDeg: -30, tz: TZ, place: 'bucuresti' });
  assert.deepEqual(s, { lat: 44.43, lon: 26.1, northDeg: 330, tz: TZ, place: 'bucuresti' });
  assert.throws(() => checkSite({ lat: 95, lon: 0, northDeg: 0 }), SiteError);
  assert.throws(() => checkSite({ lat: 'x', lon: 0, northDeg: 0 }), SiteError);
  assert.throws(() => checkSite({ lat: 1, lon: 1, northDeg: 0, tz: '<script>' }), SiteError);
  assert.throws(() => checkSite({ lat: 1, lon: 1, northDeg: 0, tz: 'Mars/Base' }), SiteError);
  assert.equal(checkSite({ lat: 1, lon: 1, northDeg: 0 }).tz, 'Europe/Bucharest');
  assert.equal(checkSite({ lat: 1, lon: 1, northDeg: 0, place: 'nicaieri' }).place, undefined);
  assert.equal(readSite({ lat: 'stricat' }), null); assert.equal(readSite(undefined), null);
  assert.equal(new Set(PLACES.map(p => p.id)).size, PLACES.length);
  for (const p of PLACES) assert.doesNotThrow(() => checkSite({ ...p, northDeg: 0 }));
  assert.equal(planAzimuth(180, { northDeg: 90 }), 90);
  assert.equal(facing(0, 1, { northDeg: 0 }), 'S'); assert.equal(facing(0, 1, { northDeg: 180 }), 'N'); assert.equal(facing(1, 0, { northDeg: 90 }), 'S');
});

// o cameră 5 × 4 m, pereți exteriori groși de `t`, o fereastră de 1,4 m pe latura dată
function box(side: 'S' | 'N' | 'E' | 'W', t = .3, type = 'living', extra: Partial<Opening> = {}): Floor {
  const W = 5, D = 4, win = (L: number): Opening => ({ id: 'w1', kind: 'window', offset: L / 2 - .7, width: 1.4, ...extra });
  const walls: Floor['walls'] = [
    { id: 'N', a: [0, 0], b: [W, 0], thickness: t, exterior: true, openings: side === 'N' ? [win(W)] : [] },
    { id: 'E', a: [W, 0], b: [W, D], thickness: t, exterior: true, openings: side === 'E' ? [win(D)] : [] },
    { id: 'S', a: [W, D], b: [0, D], thickness: t, exterior: true, openings: side === 'S' ? [win(W)] : [] },
    { id: 'W', a: [0, D], b: [0, 0], thickness: t, exterior: true, openings: side === 'W' ? [win(D)] : [{ id: 'd1', kind: 'door', offset: 1.5, width: .9, entrance: true }] }];
  return { id: 'f', name: 'Parter', ceilingHeight: 2.7, rooms: [{ id: 'r1', name: 'Living', type, rect: { x0: 0, z0: 0, x1: W, z1: D } }], walls };
}
const site = (northDeg = 0): SiteInfo => ({ ...BUC, northDeg, tz: TZ });
const study = (fl: Floor, s: SiteInfo, date = '2026-12-21', floorGrid = false) => insolationStudy(fl, s, toUtc, { date, stepMin: 5, floorGrid });

test('fereastra spre sud are soare toată ziua de iarnă; aceeași fereastră, cu nordul întors, are zero', () => {
  const south = study(box('S'), site(0)), north = study(box('S'), site(180));
  const rs = south.rooms[0]!, rn = north.rooms[0]!;
  assert.ok(rs.hours >= 5, `sud: ${rs.hours} h`); assert.equal(rs.verdict, 'ok'); assert.equal(south.windows[0]!.facing, 'S');
  assert.equal(rn.hours, 0); assert.equal(rn.verdict, 'low'); assert.equal(north.windows[0]!.facing, 'N');
  // vara, fereastra de nord prinde soarele dimineața devreme și seara târziu (soarele răsare la NE)
  assert.ok(study(box('N'), site(0), '2026-06-21').rooms[0]!.hours > 0);
});
test('glaful gros umbrește: la est, peretele de 60 cm dă mai puține ore decât cel de 10 cm', () => {
  const thin = study(box('E', .1), site(0), '2026-03-20').rooms[0]!.hours, thick = study(box('E', .6), site(0), '2026-03-20').rooms[0]!.hours;
  assert.ok(thin > thick, `${thin} > ${thick}`); assert.ok(thick > 0);
});
test('o aripă a casei din fața ferestrei o umbrește', () => {
  const fl = box('S'); // un perete înalt, paralel, la 1,5 m în fața ferestrei de sud
  const shaded = { ...fl, walls: [...fl.walls, { id: 'wing', a: [-3, 5.5] as [number, number], b: [8, 5.5] as [number, number], thickness: .3, exterior: true, openings: [] }] };
  const a = study(fl, site(0)).rooms[0]!.hours, b = study(shaded, site(0)).rooms[0]!.hours;
  assert.ok(b < a - 3, `${b} față de ${a}`);
});
test('peretele de demolat nu mai umbrește; ușa e opacă, pasajul lasă lumina', () => {
  const fl = box('S'), wing = { id: 'wing', a: [-3, 5.5] as [number, number], b: [8, 5.5] as [number, number], thickness: .3, exterior: true, openings: [], status: 'demolish' as const };
  assert.equal(study({ ...fl, walls: [...fl.walls, wing] }, site(0)).rooms[0]!.hours, study(fl, site(0)).rooms[0]!.hours);
  const slabs = slabsOf(fl), d: [number, number, number] = [-.8, .3, 0]; // spre vest, prin ușa de pe peretele W
  const p: [number, number, number] = [.6, .5, 1.95];
  assert.equal(seesSky(p, d, slabs, fl.rooms, 2.7), false);
  const pass = { ...fl, walls: fl.walls.map(w => w.id === 'W' ? { ...w, openings: [{ id: 'd1', kind: 'door' as const, offset: 1.5, width: .9, passage: true as const }] } : w) };
  assert.equal(seesSky(p, d, slabsOf(pass), pass.rooms, 2.7), true);
});
test('verdictul normei se dă doar încăperilor de locuit; baia nu primește verdict', () => {
  const bath = study(box('S', .3, 'baie'), site(180)).rooms[0]!; assert.equal(bath.verdict, 'na'); assert.equal(bath.habitable, false);
  const noWin = study({ ...box('S'), walls: box('S').walls.map(w => ({ ...w, openings: w.openings.filter(o => o.kind !== 'window') })) }, site(0)).rooms[0]!;
  assert.equal(noWin.verdict, 'none'); assert.equal(NORM_HOURS, 1.5);
});
test('harta pardoselii: iarna soarele jos pătrunde în fundul camerei, vara rămâne lângă fereastra de sud', () => {
  const w = study(box('S'), site(0), '2026-12-21', true), s = study(box('S'), site(0), '2026-06-21', true);
  const strip = (st: typeof w, f: (z: number) => boolean) => Math.max(...st.cells.filter(c => Math.abs(c.x - 2.5) < .4 && f(c.z)).map(c => c.hours));
  assert.ok(strip(w, z => z < 2) > strip(s, z => z < 2), 'iarna, fundul camerei');
  assert.ok(strip(s, z => z > 3) > strip(w, z => z > 3), 'vara, lângă fereastră');
  assert.ok(w.rooms[0]!.floorShare > 0 && w.rooms[0]!.floorShare < 1); assert.ok(w.rooms[0]!.peak);
  assert.ok(w.cells.every(c => c.roomId === 'r1'));
});
test('pata de soare a ferestrei de sud la amiază cade în cameră, spre nord de fereastră, și e mai îngustă cu glaful gros', () => {
  const noon = sunDay(toUtc('2026-12-21', 720), BUC.lat, BUC.lon).solarNoon, p = sunPosition(noon, BUC.lat, BUC.lon);
  const d = [Math.sin(p.azimuthDeg * Math.PI / 180) * Math.cos(p.elevationDeg * Math.PI / 180), Math.sin(p.elevationDeg * Math.PI / 180), -Math.cos(p.azimuthDeg * Math.PI / 180) * Math.cos(p.elevationDeg * Math.PI / 180)] as [number, number, number];
  const [patch] = sunPatches(box('S', .3), d); assert.ok(patch); assert.equal(patch.roomId, 'r1');
  assert.ok(patch.pts.every(q => q[1] < 4 && q[1] >= 0 && q[0] >= 0 && q[0] <= 5));
  const area = (pts: [number, number][]) => Math.abs(pts.reduce((s, q, i) => { const r = pts[(i + 1) % pts.length]!; return s + q[0] * r[1] - r[0] * q[1]; }, 0) / 2);
  const thick = sunPatches(box('S', .6), d)[0]!; assert.ok(area(thick.pts) < area(patch.pts));
  assert.deepEqual(sunPatches(box('S'), [0, -1, 0]), []);
});
test('clipConvex taie corect două pătrate suprapuse, indiferent de sensul de parcurgere', () => {
  const a: [number, number][] = [[0, 0], [2, 0], [2, 2], [0, 2]], b: [number, number][] = [[1, 1], [1, 3], [3, 3], [3, 1]];
  const c = clipConvex(a, b), area = Math.abs(c.reduce((s, q, i) => { const r = c[(i + 1) % c.length]!; return s + q[0] * r[1] - r[0] * q[1]; }, 0) / 2);
  assert.ok(Math.abs(area - 1) < 1e-9); assert.deepEqual(clipConvex(a, [[5, 5], [6, 5], [6, 6]]), []);
});
test('verificările proiectului semnalează camera de locuit sub norma de însorire, doar când există amplasament', async () => {
  const { adviseProject } = await import('../core/advisor');
  const cat = { suppliers: [], products: [], variants: [], offers: [] };
  const fl = box('S'), snap = { name: 'x', floor: fl, placements: [], selections: {}, picked: [] } as any;
  assert.ok(!adviseProject(snap, cat).some(a => a.code === 'SUN_LOW'));
  const dark = adviseProject({ ...snap, site: site(180) }, cat).filter(a => a.code === 'SUN_LOW');
  assert.equal(dark.length, 1); assert.equal(dark[0]!.refs.roomId, 'r1'); assert.equal(dark[0]!.severity, 'WARNING'); assert.equal(dark[0]!.vars.got, '0 h 00');
  assert.ok(!adviseProject({ ...snap, site: site(0) }, cat).some(a => a.code === 'SUN_LOW'));
});
test('serverul păstrează amplasamentul rotunjit și refuză unul invalid', async () => {
  const { checkSnapshot } = await import('../lib/repo');
  const base = () => ({ name: 'x', floor: box('S'), placements: [], selections: {}, picked: [] } as any);
  const ok = checkSnapshot({ ...base(), site: { lat: 45.7512, lon: 21.2289, northDeg: 370, tz: TZ, extra: 'x' } });
  assert.deepEqual(ok.site, { lat: 45.75, lon: 21.23, northDeg: 10, tz: TZ });
  assert.throws(() => checkSnapshot({ ...base(), site: { lat: 120, lon: 0, northDeg: 0 } }), (e: any) => e.status === 400);
  assert.equal('site' in checkSnapshot(base()), false);
});
test('lumina 3D a soarelui real: noapte sub orizont, mai slabă la răsărit decât la amiază', async () => {
  const { solarLighting, lighting } = await import('../core/lighting');
  assert.deepEqual(solarLighting(-5, 90), { ...lighting('night', 90) });
  const low = solarLighting(3, 100), high = solarLighting(60, 180);
  assert.ok(low.sun < high.sun && low.exposure > high.exposure); assert.equal(high.azimuthDeg, 180); assert.equal(high.elevationDeg, 60);
  assert.match(low.sunColor, /^#[0-9a-f]{6}$/);
});

test('regresie: terasa din fața ferestrei e „afară” (fără acoperiș), nu o cameră care stinge fereastra', () => {
  const fl = box('S'), terrace = { id: 't', name: 'Terasă', type: 'living', spaceKind: 'terrace' as const, rect: { x0: 0, z0: 4, x1: 5, z1: 7 } };
  const withT = { ...fl, rooms: [...fl.rooms, terrace] }, a = study(fl, site(0)).rooms[0]!, b = study(withT, site(0)).rooms[0]!;
  assert.equal(b.windows, 1); assert.equal(b.hours, a.hours); assert.equal(b.verdict, 'ok');
  // balconul acoperit are tavan: umbrește fereastra, dar fereastra rămâne exterioară
  const covered = { ...withT, rooms: [...fl.rooms, { ...terrace, spaceKind: 'covered_balcony' as const }] }, c = study(covered, site(0), '2026-06-21').rooms[0]!;
  assert.equal(c.windows, 1); assert.ok(c.hours < study(fl, site(0), '2026-06-21').rooms[0]!.hours);
  assert.equal(study(withT, site(0)).rooms.find(r => r.roomId === 't')!.verdict, 'none');
});
test('regresie: răsăritul și apusul în ora de pe ceas în zilele cu schimbarea orei și în fusurile îndepărtate', () => {
  const spring = study(box('S'), site(0), '2026-03-29'), autumn = study(box('S'), site(0), '2026-10-25');
  const m = (x: string | null) => { const [h, mm] = x!.split(':').map(Number); return h! * 60 + mm!; };
  near(m(spring.sunrise), 7 * 60 + 2, 5, 'răsărit 29 mar'); near(m(spring.sunset), 19 * 60 + 39, 5, 'apus 29 mar');
  near(m(autumn.sunrise), 6 * 60 + 43, 5, 'răsărit 25 oct'); near(m(autumn.sunset), 17 * 60 + 15, 5, 'apus 25 oct');
  const akl: SiteInfo = { lat: -36.85, lon: 174.76, northDeg: 0, tz: 'Pacific/Auckland' };
  const nz = insolationStudy(box('N'), akl, (d, mm) => localToUtc(d, mm, akl.tz), { date: '2026-12-21', stepMin: 5, floorGrid: false });
  assert.match(nz.sunrise!, /^0[5-6]:\d\d$/); assert.match(nz.sunset!, /^(20|21):\d\d$/); near(nz.dayLengthH, 14.6, .3, 'ziua lungă la Auckland');
  // iarna emisferei sudice (iunie): soarele jos stă la nord, deci fereastra de nord e cea însorită, cea de sud nu
  const nzW = (b: Floor) => insolationStudy(b, akl, (d, mm) => localToUtc(d, mm, akl.tz), { date: '2026-06-21', stepMin: 5, floorGrid: false }).rooms[0]!.hours;
  assert.ok(nzW(box('N')) > 5, 'fereastra de nord în iunie'); assert.equal(nzW(box('S')), 0);
  const odd = insolationStudy(box('S'), site(0), toUtc, { date: '2026-12-21', stepMin: 7, floorGrid: false });
  assert.ok(odd.times.every(x => /^([01]\d|2[0-3]):[0-5]\d$/.test(x)), 'etichete de oră valide la orice pas');
});
test('regresie: peretele împărțit în linie dreaptă nu umbrește fereastra de lângă îmbinare', () => {
  const fl = box('S'), S = fl.walls.find(w => w.id === 'S')!;
  // peretele de sud în două bucăți, îmbinate la x = 3,5; fereastra (1,8–3,2) stă lângă îmbinare, în prima bucată
  const split = { ...fl, walls: [...fl.walls.filter(w => w.id !== 'S'), { ...S, id: 'S1', a: [5, 4] as [number, number], b: [3.5, 4] as [number, number], openings: [] },
    { ...S, id: 'S2', a: [3.5, 4] as [number, number], b: [0, 4] as [number, number], openings: [{ id: 'w1', kind: 'window' as const, offset: .3, width: 1.4 }] }] };
  for (const date of ['2026-03-20', '2026-12-21']) assert.equal(study(split, site(0), date).rooms[0]!.hours, study(fl, site(0), date).rooms[0]!.hours, date);
});
