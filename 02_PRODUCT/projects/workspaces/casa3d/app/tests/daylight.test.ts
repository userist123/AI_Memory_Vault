import { test } from 'vitest';
import assert from 'node:assert/strict';
import { localToUtc } from '../core/solar';
import { daylightStudy, SKY, DF_TARGETS } from '../core/daylight';
import { sunCalendar, lightScore } from '../core/light-class';
import { insolationStudy, NORM_REFERENCE_DATE, EN17037_DATE } from '../core/insolation';
import { obstaclesFor, placeNeighbour, neighbourStudy } from '../core/neighbours';
import { summerCheck } from '../core/overheating';
import type { Floor, Opening, Snapshot } from '../core/types';
import type { SiteInfo } from '../core/site-location';

const TZ = 'Europe/Bucharest', toUtc = (d: string, m: number) => localToUtc(d, m, TZ);
const site = (extra: Partial<SiteInfo> = {}): SiteInfo => ({ lat: 44.43, lon: 26.10, northDeg: 0, tz: TZ, ...extra });
function box(side: 'S' | 'N' | 'E' | 'W' | null, extra: Partial<Opening> = {}, type = 'living', D = 4): Floor {
  const W = 5, win = (L: number): Opening => ({ id: `w-${side}`, kind: 'window', offset: L / 2 - .7, width: 1.4, ...extra });
  return { id: 'f0', name: 'Parter', ceilingHeight: 2.7, rooms: [{ id: 'r1', name: 'Living', type, rect: { x0: 0, z0: 0, x1: W, z1: D } }], walls: [
    { id: 'N', a: [0, 0], b: [W, 0], thickness: .3, exterior: true, openings: side === 'N' ? [win(W)] : [] },
    { id: 'E', a: [W, 0], b: [W, D], thickness: .3, exterior: true, openings: side === 'E' ? [win(D)] : [] },
    { id: 'S', a: [W, D], b: [0, D], thickness: .3, exterior: true, openings: side === 'S' ? [win(W)] : [] },
    { id: 'W', a: [0, D], b: [0, 0], thickness: .3, exterior: true, openings: side === 'W' ? [win(D)] : [] }] };
}
const snapOf = (floor: Floor, s?: SiteInfo): Snapshot => ({ name: 'T', floor, placements: [], selections: {}, picked: [], ...(s ? { site: s } : {}) });

test('cerul CIE acoperit: 145 de petice care dau iluminarea orizontală 7π/9 · Lz', () => {
  assert.equal(SKY.patches.length, 145); assert.ok(Math.abs(SKY.total - 7 * Math.PI / 9) / (7 * Math.PI / 9) < .02, String(SKY.total));
  assert.ok(SKY.patches.every(p => p.dir[1] > 0 && Math.abs(Math.hypot(...p.dir) - 1) < 1e-9));
});
test('FLN: scade spre fundul camerei, nu depinde de orientare, crește cu fereastra, scade cu un vecin în față', () => {
  const t0 = performance.now(), s = daylightStudy(box('S')), ms = performance.now() - t0, r = s.rooms[0]!;
  assert.ok(ms < 400, `${ms} ms`); assert.ok(r.mean > .3 && r.mean < 3, `FLN mediu ${r.mean}`); assert.ok(r.irc > 0 && r.sky > 0);
  const near = s.cells.filter(c => c.z > 3.3), far = s.cells.filter(c => c.z < .7), avg = (a: { df: number }[]) => a.reduce((x, c) => x + c.df, 0) / a.length;
  assert.ok(avg(near) > 2 * avg(far), `lângă fereastră ${avg(near)}, în fund ${avg(far)}`);
  const n = daylightStudy(box('N')).rooms[0]!; assert.ok(Math.abs(n.mean - r.mean) < .02, `cer acoperit simetric: ${n.mean} față de ${r.mean}`);
  assert.ok(daylightStudy(box('S', { width: 3, offset: 1 })).rooms[0]!.mean > r.mean * 1.6);
  const fl = box('S'), nb = placeNeighbour(snapOf(fl), site(), { id: 'n1', name: 'Bloc', trueAz: 180, dist: 6, w: 30, d: 10, h: 18 });
  const shaded = daylightStudy(fl, { obs: obstaclesFor(snapOf(fl), 0, site({ neighbors: [nb] })) }).rooms[0]!;
  assert.ok(shaded.sky < r.sky * .7 && shaded.irc < r.irc, `cu bloc în față: cer ${shaded.sky} față de ${r.sky}, reflectat ${shaded.irc} față de ${r.irc}`);
});
test('FLN: ținte pe tip de cameră, cameră fără ferestre, tipuri fără țintă', () => {
  assert.equal(daylightStudy(box(null)).rooms[0]!.verdict, 'none'); assert.equal(daylightStudy(box(null)).cells.length, 0);
  assert.equal(daylightStudy(box('S', {}, 'baie')).rooms[0]!.verdict, 'na');
  const big = daylightStudy(box('S', { width: 4.2, offset: .4, sill: .3, height: 2.2 }, 'dormitor')).rooms[0]!;
  assert.equal(big.target, DF_TARGETS.dormitor); assert.equal(big.verdict, 'ok', `fereastră mare: ${big.mean}`);
  const deep = daylightStudy(box('S', {}, 'bucatarie', 7)).rooms[0]!; assert.equal(deep.verdict, 'low', `bucătărie adâncă: ${deep.mean}`);
});
test('calendarul anual: sudul are soare iarna la amiază, totalul pe lună e suma orelor', () => {
  const cal = sunCalendar(box('S'), site(), toUtc, { year: 2026 }), c = cal[0]!;
  assert.equal(c.grid.length, 12); assert.ok(c.grid.every(m => m.length === 24));
  assert.ok(c.grid[11]![12]! > 30, `decembrie la 12: ${c.grid[11]![12]}`); assert.equal(c.grid[11]![3], 0);
  c.grid.forEach((m, i) => assert.ok(Math.abs(m.reduce((a, v) => a + v, 0) / 60 - c.hours[i]!) < .01, `luna ${i}`));
  assert.ok(c.hours[11]! > c.hours[5]!, 'fereastra spre sud are mai mult soare iarna decât vara (soarele sus trece peste glaf)');
  assert.equal(sunCalendar(box(null), site(), toUtc, { year: 2026 }).length, 0);
});
test('clasa de lumină: casa bună e A, cea fără soare și fără lumină e E; vecinii necunoscuți nu scad nota', () => {
  const good = box('S', { width: 4.2, offset: .4, sill: .3, height: 2.2 }), s = site({ shades: { 'w-S': 'roller_al' } });
  const parts = (fl: Floor, st: SiteInfo) => ({ norm: insolationStudy(fl, st, toUtc, { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false }).rooms,
    en: insolationStudy(fl, st, toUtc, { date: EN17037_DATE, stepMin: 5, floorGrid: false, point: 'en17037' }).rooms, daylight: daylightStudy(fl).rooms, summer: summerCheck(fl, st).rooms });
  const A = lightScore({ ...parts(good, s), nb: null }); assert.equal(A.cls, 'A', JSON.stringify(A.parts)); assert.equal(A.max, 8, 'vecinii neintroduși nu intră în punctaj'); assert.equal(A.capped, false);
  assert.equal(A.parts.find(p => p.key === 'neighbours')!.known, false);
  const dark = box('N', {}, 'living', 7), E = lightScore({ ...parts(dark, site()), nb: null }); assert.ok(['D', 'E'].includes(E.cls ?? ''), JSON.stringify(E));
  // vecinul afectat scade nota
  const fl = good, nb = placeNeighbour(snapOf(fl), s, { id: 'n1', name: 'B', trueAz: 0, dist: 1, w: 10, d: 10, h: 1.6 });
  const st = neighbourStudy(snapOf(fl), { ...s, neighbors: [nb] }, toUtc, NORM_REFERENCE_DATE, 1.5);
  const hurt = lightScore({ ...parts(good, s), nb: st.map(x => ({ ...x, status: 'affected' as const })) }); assert.equal(hurt.points, A.points); assert.equal(hurt.max, A.max + 1);
  assert.equal(lightScore({ ...parts(good, s), nb: st.map(x => ({ ...x, status: 'ok' as const })) }).max, 9);
});

test('regresie: fără norma de la solstițiu clasa nu trece de C; nicio cameră de locuit = fără puncte inventate', () => {
  const norm = [{ roomId: 'a', name: 'L', type: 'living', habitable: true, hours: 3, longestH: 3, windows: 1, verdict: 'ok' as const, floorShare: 0, peak: null },
    { roomId: 'b', name: 'D', type: 'dormitor', habitable: true, hours: .5, longestH: .5, windows: 1, verdict: 'low' as const, floorShare: 0, peak: null }];
  const en = norm.map(r => ({ ...r, hours: 5 })), daylight = norm.map(r => ({ roomId: r.roomId, name: r.name, type: r.type, mean: 3, median: 3, min: 1, uniformity: .3, sky: 2, irc: 1, target: 1, verdict: 'ok' as const }));
  const sc = lightScore({ norm, en, daylight, summer: [], nb: null });
  assert.equal(sc.parts.find(p => p.key === 'art3')!.got, 1); assert.equal(sc.cls, 'C'); assert.ok(sc.capped);
  const none = lightScore({ norm: [], en: [], daylight: [], summer: [], nb: null }); assert.equal(none.max, 0); assert.equal(none.cls, null);
  assert.equal(lightScore({ norm: [], en: [], daylight, summer: [], nb: null }).cls, null, 'fără camere de locuit nu iese „A” din două verificări');
});
test('regresie: componenta cerului nu e negativă; orizontul ascuns de un turn coboară C sub 14; aripa casei contează', () => {
  const fl = box('S'), tower = placeNeighbour(snapOf(fl), site(), { id: 'n1', name: 'Turn', trueAz: 180, dist: 2, w: 60, d: 10, h: 60 });
  const r = daylightStudy(fl, { obs: obstaclesFor(snapOf(fl), 0, site({ neighbors: [tower] })) }).rooms[0]!;
  assert.ok(r.sky >= 0 && !Object.is(r.sky, -0), String(r.sky));
  const free = daylightStudy(fl).rooms[0]!; assert.ok(r.irc < free.irc * (14 / 39) * 1.05, `CRI cu turnul ${r.irc}, fără ${free.irc}`);
  // o aripă a casei în fața ferestrei (cameră acoperită a aceluiași nivel) scade și ea partea reflectată
  const wing: Floor = { ...fl, rooms: [...fl.rooms, { id: 'r2', name: 'Aripa', type: 'birou', rect: { x0: -2, z0: 4.3, x1: 7, z1: 8 } }] };
  assert.ok(daylightStudy(wing, { roomId: 'r1' }).rooms[0]!.irc < free.irc);
});
