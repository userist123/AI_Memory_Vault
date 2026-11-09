import { test } from 'vitest';
import assert from 'node:assert/strict';
import { viewOut, VIEW_LEVELS } from '../core/view-out';
import { obstaclesFor, placeNeighbour } from '../core/neighbours';
import { clearOfWalls, slabsOf } from '../core/insolation';
import type { Floor, Opening, Snapshot } from '../core/types';
import type { SiteInfo } from '../core/site-location';

const site = (extra: Partial<SiteInfo> = {}): SiteInfo => ({ lat: 44.43, lon: 26.10, northDeg: 0, tz: 'Europe/Bucharest', ...extra });
function box(extra: Partial<Opening> = {}, door = false, type = 'living', D = 4): Floor {
  const W = 5, win: Opening = door ? { id: 'd1', kind: 'door', offset: 2, width: .9, passage: true } : { id: 'w1', kind: 'window', offset: 1.8, width: 1.4, ...extra };
  return { id: 'f0', name: 'P', ceilingHeight: 2.7, rooms: [{ id: 'r1', name: 'Living', type, rect: { x0: 0, z0: 0, x1: W, z1: D } }], walls: [
    { id: 'N', a: [0, 0], b: [W, 0], thickness: .3, exterior: true, openings: [] }, { id: 'E', a: [W, 0], b: [W, D], thickness: .3, exterior: true, openings: [] },
    { id: 'S', a: [W, D], b: [0, D], thickness: .3, exterior: true, openings: [win] }, { id: 'W', a: [0, D], b: [0, 0], thickness: .3, exterior: true, openings: [] }] };
}
const snapOf = (floor: Floor): Snapshot => ({ name: 'T', floor, placements: [], selections: {}, picked: [] });

test('raza orizontală prin perete: trece doar prin fereastră (la înălțimea ei), nu prin zid; pasajul nu e vedere afară', () => {
  const s = slabsOf(box()); assert.ok(clearOfWalls([2.5, 1.2, 2], [0, 0, 1], s, 3, true)); assert.ok(!clearOfWalls([1, 1.2, 2], [0, 0, 1], s, 3, true));
  assert.ok(!clearOfWalls([2.5, .5, 2], [0, 0, 1], s, 3, true), 'sub parapet');
  const d = slabsOf(box({}, true)); assert.ok(clearOfWalls([2.45, 1.2, 2], [0, 0, 1], d, 3, false)); assert.ok(!clearOfWalls([2.45, 1.2, 2], [0, 0, 1], d, 3, true));
});
test('vederea spre exterior: fereastra spre un câmp liber dă unghi, distanță mare și toate trei straturile', () => {
  const t0 = performance.now(), v = viewOut(box()), ms = performance.now() - t0, r = v.rooms[0]!;
  assert.ok(ms < 500, `${ms} ms`); assert.equal(r.windows, 1, JSON.stringify(r)); assert.ok(r.angle >= VIEW_LEVELS.angle[0], `unghi ${r.angle}`); assert.equal(r.dist, 100);
  assert.ok(r.layers.land > .8, `peisaj ${r.layers.land}`); assert.ok(r.layers.sky > .5 && r.layers.ground > .3, JSON.stringify(r.layers)); assert.ok(r.level >= 1);
  // lângă fereastră se vede mai larg decât în fund
  const near = v.cells.filter(c => c.z > 3), far = v.cells.filter(c => c.z < 1), avg = (a: { angle: number }[]) => a.reduce((x, c) => x + c.angle, 0) / a.length;
  assert.ok(avg(near) > avg(far) * 1.5, `${avg(near)} față de ${avg(far)}`);
  // fereastră lată → nivel mai bun
  assert.ok(viewOut(box({ width: 4, offset: .5, sill: .4, height: 2 })).rooms[0]!.level > r.level);
});
test('vederea: un vecin la 8 m scurtează distanța; parapetul înalt ascunde solul; ușa nu e fereastră', () => {
  const fl = box(), nb = placeNeighbour(snapOf(fl), site(), { id: 'n1', name: 'B', trueAz: 180, dist: 8, w: 40, d: 10, h: 15 });
  const r = viewOut(fl, { obs: obstaclesFor(snapOf(fl), 0, site({ neighbors: [nb] })) }).rooms[0]!;
  assert.ok(r.dist >= 7.5 && r.dist <= 10, `distanța ${r.dist}`); assert.ok(r.level <= 1);
  // parapetul aproape de ochi: solul apare doar departe, deci mai rar decât la un parapet jos
  const wall20 = (f: Floor) => obstaclesFor(snapOf(f), 0, site({ neighbors: [placeNeighbour(snapOf(f), site(), { id: 'n4', name: 'B', trueAz: 180, dist: 20, w: 60, d: 10, h: 30 })] }));
  const hi = box({ sill: 1.1, height: 1.2 }), lo = box({ sill: .4, height: 1.9 });
  assert.ok(viewOut(hi, { obs: wall20(hi) }).rooms[0]!.layers.ground < viewOut(lo, { obs: wall20(lo) }).rooms[0]!.layers.ground);
  // un vecin aproape ascunde solul (îl vezi abia după el)
  const close = placeNeighbour(snapOf(fl), site(), { id: 'n2', name: 'Zid', trueAz: 180, dist: 2, w: 40, d: 5, h: 20 });
  // (doar lângă geam, privind în jos, se mai vede fâșia de 2 m dintre casă și vecin)
  const g = viewOut(fl, { obs: obstaclesFor(snapOf(fl), 0, site({ neighbors: [close] })) }).rooms[0]!.layers.ground; assert.ok(g < .15 && g < viewOut(fl).rooms[0]!.layers.ground * .3, String(g));
  const d = viewOut(box({}, true)).rooms[0]!; assert.equal(d.windows, 0); assert.equal(d.level, 0);
  // la etaj terenul e mai jos și mai departe: cu un vecin la 20 m, solul se vede de la parter, de sus doar de lângă geam
  const far = placeNeighbour(snapOf(fl), site(), { id: 'n3', name: 'B', trueAz: 180, dist: 20, w: 60, d: 10, h: 30 }), o = (e: number) => ({ obs: obstaclesFor(snapOf(fl), 0, site({ neighbors: [far] })).map(x => ({ ...x, y0: x.y0 - e, y1: x.y1 - e })), elev: e });
  const g0 = viewOut(fl, o(0)).rooms[0]!.layers.ground, g6 = viewOut(fl, o(6)).rooms[0]!.layers.ground; assert.ok(g0 > .3 && g6 < g0 * .5, `${g0} / ${g6}`);
});
test('lumina la birou: piesa din fundul camerei are mult mai puțină lumină decât locul de lângă fereastră; consilierul o spune', async () => {
  const { daylightStudy, seatLight } = await import('../core/daylight'), { adviseProject } = await import('../core/advisor');
  const fl = box({ width: 3, offset: 1 }, false, 'living', 7), df = daylightStudy(fl), far = seatLight(df, { x: 2.5, z: .5, roomId: 'r1' })!, near = seatLight(df, { x: 2.5, z: 6.5, roomId: 'r1' })!;
  assert.ok(far.ratio < .4 && near.ratio > .6, `${far.ratio} / ${near.ratio}`); assert.ok(far.best >= 1.5);
  const cat = { suppliers: [], products: [{ id: 'p1', group: 'birou', name: 'Birou', brand: 'x', category: 'x', model3d: 'x' }], variants: [{ id: 'v1', productId: 'p1', name: 'v', legacyIndex: 0, dimensionsCm: { w: 120, d: 60, h: 75 }, dimensionsConfidence: 'HIGH' as const, style: {} }], offers: [] };
  const s: Snapshot = { ...snapOf(fl), site: site(), placements: [{ id: 'b1', roomId: 'r1', group: 'birou', variantId: 'v1', x: 2.5, z: .6, rotation: 0, source: 'manual' }] };
  const a = adviseProject(s, cat).find(x => x.code === 'DESK_DARK'); assert.ok(a, 'sfatul DESK_DARK'); assert.equal(typeof a!.vars.at_n, 'number');
  const ok = { ...s, placements: [{ ...s.placements[0]!, z: 6.4 }] }; assert.ok(!adviseProject(ok, cat).some(x => x.code === 'DESK_DARK'));
});

test('regresie: fereastra altei camere nu e „afară”; piesa din afara camerei nu primește lumina celulei celei mai apropiate', async () => {
  // casă în U: camera A (sud) privește spre aripa B peste o curte de 6 m; B are o fereastră lată spre curte
  const W = 12, wall = (id: string, a: [number, number], b: [number, number], openings: Opening[] = []) => ({ id, a, b, thickness: .3, exterior: true, openings });
  const fl: Floor = { id: 'f', name: 'P', ceilingHeight: 2.7, rooms: [{ id: 'A', name: 'A', type: 'living', rect: { x0: 0, z0: 0, x1: W, z1: 4 } }, { id: 'B', name: 'B', type: 'dormitor', rect: { x0: 0, z0: 10, x1: W, z1: 22 } }],
    walls: [wall('a1', [0, 0], [W, 0]), wall('a2', [W, 0], [W, 4]), wall('a3', [W, 4], [0, 4], [{ id: 'wa', kind: 'window', offset: 4, width: 4 }]), wall('a4', [0, 4], [0, 0]),
      wall('b1', [0, 10], [W, 10], [{ id: 'wb', kind: 'window', offset: 2, width: 8 }]), wall('b2', [W, 10], [W, 22]), wall('b3', [W, 22], [0, 22]), wall('b4', [0, 22], [0, 10])] };
  const a = viewOut(fl, { roomId: 'A' }).rooms[0]!; assert.ok(a.dist > 5 && a.dist < 12, `distanța până la aripa B (adâncă de 12 m: prin geamul ei s-ar „vedea” 18 m): ${a.dist}`); assert.ok(a.level <= 1);
  const { daylightStudy, seatLight } = await import('../core/daylight');
  assert.equal(seatLight(daylightStudy(box()), { x: 50, z: 50, roomId: 'r1' }), null);
});
