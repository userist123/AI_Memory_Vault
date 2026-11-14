import { test } from 'vitest';
import assert from 'node:assert/strict';
import { localToUtc } from '../core/solar';
import { insolationStudy, pointY, en17037Level, NORM_HOURS, NORM_REFERENCE_DATE, EN17037_DATE } from '../core/insolation';
import { hits, obstacle, obstaclesFor, houseMass, houseBox, placeNeighbour, moveTo, gap, corners, studyTriggers, neighbourStudy, targetsOf, checkNeighbours, type Neighbour } from '../core/neighbours';
import { summerCheck, shadeOptions, crossVentilated, ADO_LIMITS, SHADES, trueAz, orient4 } from '../core/overheating';
import { solarGain } from '../core/sun-effects';
import { checkSite, guessTz, SiteError, type SiteInfo } from '../core/site-location';
import { adviseProject } from '../core/advisor';
import type { Catalog, Floor, Opening, Snapshot } from '../core/types';

const TZ = 'Europe/Bucharest', toUtc = (d: string, m: number) => localToUtc(d, m, TZ);
const site = (extra: Partial<SiteInfo> = {}): SiteInfo => ({ lat: 44.43, lon: 26.10, northDeg: 0, tz: TZ, ...extra });
/** O cameră de 5 × 4 m cu o fereastră pe peretele dat (sudul e +z). */
function box(side: 'S' | 'N' | 'E' | 'W', extra: Partial<Opening> = {}, type = 'dormitor'): Floor {
  const W = 5, D = 4, win = (L: number): Opening => ({ id: `w-${side}`, kind: 'window', offset: L / 2 - .7, width: 1.4, ...extra });
  return { id: 'f0', name: 'Parter', ceilingHeight: 2.7, rooms: [{ id: 'r1', name: 'Dormitor', type, rect: { x0: 0, z0: 0, x1: W, z1: D } }], walls: [
    { id: 'N', a: [0, 0], b: [W, 0], thickness: .3, exterior: true, openings: side === 'N' ? [win(W)] : [] },
    { id: 'E', a: [W, 0], b: [W, D], thickness: .3, exterior: true, openings: side === 'E' ? [win(D)] : [] },
    { id: 'S', a: [W, D], b: [0, D], thickness: .3, exterior: true, openings: side === 'S' ? [win(W)] : [] },
    { id: 'W', a: [0, D], b: [0, 0], thickness: .3, exterior: true, openings: side === 'W' ? [win(D)] : [] }] };
}
const snapOf = (floor: Floor, s?: SiteInfo, levels?: Floor[]): Snapshot => ({ name: 'T', floor, ...(levels ? { levels } : {}), placements: [], selections: {}, picked: [], ...(s ? { site: s } : {}) });
const nb = (o: Partial<Neighbour> & Pick<Neighbour, 'x' | 'z'>): Neighbour => ({ id: 'n1', name: 'Bloc', w: 20, d: 10, rot: 180, h: 15, kind: 'home', older: true, calcan: false, ...o });
const hoursOf = (fl: Floor, s: SiteInfo, obs = obstaclesFor(snapOf(fl), 0, s)) => insolationStudy(fl, s, toUtc, { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false, obs }).rooms[0]!.hours;

test('raza și volumul: trece pe deasupra, e oprită prin el, funcționează și pe un poligon concav (în L)', () => {
  const o = obstacle([[0, 5], [10, 5], [10, 15], [0, 15]], 0, 10), up = Math.SQRT1_2;
  assert.ok(hits([5, 1, 0], [0, up, up], o), 'raza spre sud la 45° lovește blocul de 10 m de la 5 m');
  assert.ok(!hits([5, 1, 0], [0, .95, .31], o), 'raza abruptă trece peste el');
  assert.ok(!hits([5, 1, 0], [0, up, -up], o), 'raza spre nord nu îl atinge');
  assert.ok(!hits([20, 1, 0], [0, up, up], o), 'raza paralelă, pe lângă');
  // L: colțul gol (x 5–10, z 10–15) lasă raza să treacă
  const L = obstacle([[0, 5], [10, 5], [10, 10], [5, 10], [5, 15], [0, 15]], 0, 3);
  assert.ok(!hits([7.5, 0, 12.5], [0, .05, .9987], L), 'pornește din golul L-ului spre sud, nu lovește nimic');
  assert.ok(hits([7.5, 0, 0], [0, .05, .9987], L), 'aceeași rază de mai la nord trece prin brațul L-ului');
});
test('un bloc de 15 m la 8 m în fața ferestrei de sud scoate dormitorul de sub normă; mai departe, nu', () => {
  const fl = box('S'), free = hoursOf(fl, site()); assert.ok(free >= NORM_HOURS, `fără vecin ${free}`);
  const near = placeNeighbour(snapOf(fl), site(), { id: 'n1', name: 'Bloc', trueAz: 180, dist: 8, w: 30, d: 12, h: 15 });
  assert.ok(Math.abs(gap(near, houseBox(snapOf(fl))!) - 8) < .02, 'distanța liberă e cea cerută');
  const shaded = hoursOf(fl, site({ neighbors: [near] })); assert.ok(shaded < NORM_HOURS, `cu vecinul la 8 m: ${shaded}`);
  const far = moveTo(near, snapOf(fl), 60), ok = hoursOf(fl, site({ neighbors: [far] }));
  assert.ok(ok >= NORM_HOURS && ok > shaded, `la 60 m: ${ok}`);
  // un vecin la nord nu umbrește fereastra de sud
  const north = placeNeighbour(snapOf(fl), site(), { id: 'n2', name: 'N', trueAz: 0, dist: 3, w: 30, d: 12, h: 30 });
  assert.equal(hoursOf(fl, site({ neighbors: [north] })), free);
  // direcția e cea adevărată: cu nordul rotit la 180°, „sudul adevărat” e sus pe plan
  const rot = placeNeighbour(snapOf(fl), site({ northDeg: 180 }), { id: 'n3', name: 'R', trueAz: 180, dist: 5, w: 10, d: 10, h: 10 });
  assert.ok(rot.z < 0, `vecinul spre sudul adevărat stă la -z pe plan: ${rot.z}`);
});
test('etajul de deasupra umbrește parterul: o consolă peste terasă oprește soarele ferestrei de sud', () => {
  const fl = box('S'), up: Floor = { id: 'f1', name: 'Etaj', ceilingHeight: 2.7, walls: [], rooms: [{ id: 'u1', name: 'Dormitor 2', type: 'dormitor', rect: { x0: 0, z0: 0, x1: 5, z1: 7 } }] };
  const s = snapOf(fl, site(), [up]), obs = obstaclesFor(s, 0, site());
  assert.equal(obs.length, 1); assert.ok(Math.abs(obs[0]!.y0 - 2.7) < 1e-9, 'placa etajului începe la tavanul parterului');
  assert.ok(hoursOf(fl, site(), obs) < hoursOf(fl, site(), []), 'consola de 3 m umbrește');
  assert.equal(obstaclesFor(s, 1, site()).length, 0, 'nivelul de sus nu e umbrit de cel de jos');
  assert.equal(houseBox(s)!.h, 5.8); assert.equal(houseMass(s).length, 2);
});
test('alin. (2): studiul e obligatoriu când D ≤ H max, nu și când vecinul e mai departe decât cea mai înaltă clădire', () => {
  const s = snapOf(box('S')), n = placeNeighbour(s, site(), { id: 'n1', name: 'Bloc', trueAz: 180, dist: 10, w: 20, d: 10, h: 12 });
  assert.deepEqual(studyTriggers(s, site({ neighbors: [n] })).map(x => [x.distM, x.hMaxM, x.required]), [[10, 12, true]]);
  const far = moveTo(n, s, 12.5); assert.equal(studyTriggers(s, site({ neighbors: [far] }))[0]!.required, false);
  // vecinul pus „în cruce” peste casă (fără niciun colț înăuntru) are distanța 0
  assert.equal(gap(nb({ x: 2.5, z: 2, w: 1, d: 40, rot: 0 }), { x0: 0, x1: 5, z0: 0, z1: 4 }), 0);
});
test('efectul casei asupra vecinilor: o casă înaltă la sud de un bloc îi ia soarele; calcanul și clădirea fără locuințe nu se verifică', () => {
  // casa noastră: 4 niveluri (12 m), la sud de ea un bloc vechi de locuințe cu fațada spre noi
  const lv = (i: number): Floor => ({ id: `f${i}`, name: `E${i}`, ceilingHeight: 2.7, walls: [], rooms: [{ id: `r${i}`, name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 20, z1: 8 } }] });
  const s = snapOf(lv(0), site(), [lv(1), lv(2), lv(3)]);
  const block = placeNeighbour(s, site(), { id: 'n1', name: 'Bloc vechi', trueAz: 0, dist: 6, w: 20, d: 12, h: 9 });
  const st = neighbourStudy(s, site({ neighbors: [block] }), toUtc, NORM_REFERENCE_DATE, NORM_HOURS)[0]!;
  assert.equal(st.status, 'affected'); assert.ok(st.worstBefore >= NORM_HOURS && st.worstAfter < NORM_HOURS, `${st.worstBefore} → ${st.worstAfter}`);
  assert.ok(st.affected > 0 && st.affected <= st.targets.length); assert.ok(st.older);
  // ferestrele-țintă stau pe fațada spre noi, cu normala spre casă
  const t0 = targetsOf(block)[0]!; assert.ok(t0.nOut[1] > .99, 'fațada vecinului de la nord privește spre sud (+z)');
  const at = (o: Partial<Neighbour>) => neighbourStudy(s, site({ neighbors: [{ ...block, ...o }] }), toUtc, NORM_REFERENCE_DATE, NORM_HOURS)[0]!.status;
  assert.equal(at({ calcan: true }), 'calcan'); assert.equal(at({ kind: 'other' }), 'other');
  // un vecin departe nu e afectat
  assert.equal(neighbourStudy(s, site({ neighbors: [moveTo(block, s, 60)] }), toUtc, NORM_REFERENCE_DATE, NORM_HOURS)[0]!.status, 'ok');
  // o fațadă spre nord e sub minim oricum, fără vina noastră
  const back = { ...placeNeighbour(s, site(), { id: 'n2', name: 'Spate', trueAz: 180, dist: 6, w: 20, d: 12, h: 9 }) };
  assert.equal(neighbourStudy(s, site({ neighbors: [back] }), toUtc, NORM_REFERENCE_DATE, NORM_HOURS)[0]!.status, 'already');
});
test('validarea vecinilor la salvare: rotunjire, valori implicite, refuz pentru înălțimi absurde și id-uri dublate', () => {
  const s = checkSite({ ...site(), neighbors: [{ id: 'n1', name: 'Bloc', x: 1.234, z: -20.006, w: 20, d: 10, rot: -90, h: 15.333 }] });
  assert.deepEqual(s.neighbors, [{ id: 'n1', name: 'Bloc', x: 1.23, z: -20.01, w: 20, d: 10, rot: 270, h: 15.33, kind: 'home', older: true, calcan: false }]);
  assert.throws(() => checkSite({ ...site(), neighbors: [{ ...nb({ x: 0, z: 0 }), h: 900 }] }), SiteError);
  assert.throws(() => checkSite({ ...site(), neighbors: [nb({ x: 0, z: 0 }), nb({ x: 1, z: 1 })] }), SiteError);
  assert.throws(() => checkNeighbours(new Array(13).fill(0).map((_, i) => nb({ id: `n${i}`, x: i, z: 0 }))));
  assert.equal(checkSite({ ...site(), neighbors: [] }).neighbors, undefined);
  // protecțiile și geamul
  assert.deepEqual(checkSite({ ...site(), shades: { 'w-S': 'roller_al' }, glazing: 'triple' }).shades, { 'w-S': 'roller_al' });
  assert.throws(() => checkSite({ ...site(), shades: { 'w-S': 'jaluzea-magica' } }), SiteError);
  assert.throws(() => checkSite({ ...site(), glazing: 'cuantic' }), SiteError);
});
test('fusul orar din coordonate: România, Moldova, vecinii; în rest fusul de longitudine', () => {
  assert.equal(guessTz(45.75, 21.23), 'Europe/Bucharest'); assert.equal(guessTz(47.01, 28.86), 'Europe/Chisinau'); assert.equal(guessTz(44.18, 28.63), 'Europe/Bucharest');
  assert.equal(guessTz(47.5, 19.04), 'Europe/Budapest'); assert.equal(guessTz(42.7, 23.32), 'Europe/Sofia');
  assert.equal(guessTz(51.5, -0.12), 'Etc/GMT'); assert.equal(guessTz(40.7, -74), 'Etc/GMT+5'); assert.equal(guessTz(35.7, 139.7), 'Etc/GMT-9');
  for (const z of ['Etc/GMT', 'Etc/GMT+5', 'Etc/GMT-9']) assert.equal(checkSite({ lat: 1, lon: 1, northDeg: 0, tz: z }).tz, z);
});
test('SR EN 17037: punctul la 1,2 m / 0,3 m peste parapet, nivelurile 1,5 / 3 / 4 h', () => {
  assert.equal(pointY(.9, 2.2), 1.55); assert.equal(pointY(.9, 2.2, 'en17037'), 1.2); assert.ok(Math.abs(pointY(1.1, 2.4, 'en17037') - 1.4) < 1e-9); assert.equal(pointY(1.5, 1.6, 'en17037'), 1.55);
  assert.deepEqual([1, 1.5, 2.9, 3, 4.2].map(en17037Level), [0, 1, 1, 2, 3]);
  const st = insolationStudy(box('S'), site(), toUtc, { date: EN17037_DATE, stepMin: 5, floorGrid: false, point: 'en17037' });
  assert.ok(st.rooms[0]!.hours >= 4, `sudul în martie: ${st.rooms[0]!.hours}`);
});
test('vara după AD O: prea multă sticlă spre sud iese din limită; ruloul exterior acoperă cerința de protecție', () => {
  assert.equal(orient4(trueAz([0, 1], 0)), 'S'); assert.equal(orient4(trueAz([0, 1], 90)), 'W'); assert.equal(orient4(trueAz([1, 0], 0)), 'E');
  const small = summerCheck(box('S'), site()).rooms[0]!;
  assert.equal(small.orient, 'S'); assert.equal(small.cross, false); assert.equal(small.limitPct, ADO_LIMITS.single.high.S);
  // 1,4 × 1,3 m gol × 0,72 sticlă = 1,31 m² pe 20 m² = 6,6 % < 11 %
  assert.equal(small.verdict, 'ok'); assert.equal(small.pct, 6.6); assert.deepEqual(small.unshaded, ['w-S']);
  const big = summerCheck(box('S', { width: 4, offset: .5, sill: .1, height: 2.4 }), site()).rooms[0]!;
  assert.equal(big.verdict, 'over', `sticla ${big.pct} %`);
  const shaded = summerCheck(box('S'), site({ shades: { 'w-S': 'roller_al' } })).rooms[0]!; assert.deepEqual(shaded.unshaded, []);
  assert.deepEqual(summerCheck(box('S'), site({ shades: { 'w-S': 'awning' } })).rooms[0]!.unshaded, ['w-S'], 'marchiza (Fc 0,5) nu ajunge');
  assert.deepEqual(summerCheck(box('S'), site({ glazing: 'solar' })).rooms[0]!.unshaded, [], 'geamul de control solar ajunge');
  assert.deepEqual(summerCheck(box('N'), site()).rooms[0]!.unshaded, [], 'nordul nu cere protecție');
  assert.equal(summerCheck(box('S', {}, 'baie'), site()).rooms[0]!.verdict, 'na');
  // ventilarea transversală: ferestre pe fațade opuse
  const two = box('S'); two.walls[0]!.openings.push({ id: 'w-N2', kind: 'window', offset: 1, width: 1, sill: 1, height: 1 });
  assert.ok(crossVentilated(two)); assert.ok(!crossVentilated(box('S')));
});
test('comparatorul de umbrire: variantele eficiente întâi, cele cu preț înaintea celor fără; căldura evitată scade cu Fc', () => {
  const fl = box('S', { width: 2 }), sc = summerCheck(fl, site()), room = sc.rooms[0]!;
  const july = solarGain(fl, site(), toUtc, '2026-07-21'), bare = (id: string) => july.windows.find(w => w.openingId === id)!.kwhTypicalBare;
  const opts = shadeOptions(room, sc.windows, bare);
  assert.equal(opts.length, Object.keys(SHADES).length); assert.ok(opts.slice(0, 4).every(o => o.meets)); assert.equal(opts.at(-1)!.kind, 'awning');
  assert.equal(opts[0]!.kind, 'roller_pvc'); assert.deepEqual(opts[0]!.costLei, [Math.round(280 * 2.6), Math.round(400 * 2.6)]);
  const motor = opts.find(o => o.kind === 'roller_al_motor')!; assert.deepEqual(motor.costLei, [Math.round(450 * 2.6 + 650), Math.round(620 * 2.6 + 800)]);
  assert.equal(opts.find(o => o.kind === 'raffstore')!.costLei, null, 'fără sursă de preț nu inventăm unul');
  assert.ok(opts.find(o => o.kind === 'raffstore')!.kwhSaved > opts.find(o => o.kind === 'awning')!.kwhSaved);
  // protecția aplicată scade câștigul calculat la Fc din el
  const sh = solarGain(fl, site({ shades: { 'w-S': 'roller_al' } }), toUtc, '2026-07-21').windows[0]!, b = july.windows[0]!;
  assert.ok(Math.abs(sh.kwhTypical - b.kwhTypical * .3) < .02, `${sh.kwhTypical} față de ${b.kwhTypical}`); assert.equal(sh.kwhTypicalBare, b.kwhTypicalBare);
});
test('consilierul: avertizează când casa scade sub minim ferestrele unui vecin mai vechi; sfat pentru unul mai nou', () => {
  const cat: Catalog = { suppliers: [], products: [], variants: [], offers: [] };
  const lv = (i: number): Floor => ({ id: `f${i}`, name: `E${i}`, ceilingHeight: 2.7, walls: [], rooms: [{ id: `r${i}`, name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 20, z1: 8 } }] });
  const base = snapOf(lv(0), site(), [lv(1), lv(2), lv(3)]);
  const block = placeNeighbour(base, site(), { id: 'n1', name: 'Bloc vechi', trueAz: 0, dist: 6, w: 20, d: 12, h: 9 });
  const s = { ...base, site: site({ neighbors: [block] }) }, view = { ...s, levels: undefined };
  const a = adviseProject(view, cat, { sun: { house: s } }).filter(x => x.code.startsWith('SUN_NEIGHBOUR'));
  assert.equal(a.length, 1); assert.equal(a[0]!.code, 'SUN_NEIGHBOUR'); assert.equal(a[0]!.severity, 'WARNING'); assert.equal(a[0]!.vars.nm, 'Bloc vechi');
  const s2 = { ...s, site: site({ neighbors: [{ ...block, older: false }] }) };
  assert.equal(adviseProject({ ...s2, levels: undefined }, cat, { sun: { house: s2 } }).find(x => x.code.startsWith('SUN_NEIGHBOUR'))!.code, 'SUN_NEIGHBOUR_NEW');
  // vecinul care umbrește casa schimbă verdictul SUN_LOW prin `obs`
  const fl = box('S'), n2 = placeNeighbour(snapOf(fl), site(), { id: 'n9', name: 'Turn', trueAz: 180, dist: 5, w: 30, d: 10, h: 30 }), s3 = snapOf(fl, site({ neighbors: [n2] }));
  assert.ok(!adviseProject(s3, cat).some(x => x.code === 'SUN_LOW'), 'fără obs nu știe de vecin');
  assert.ok(adviseProject(s3, cat, { sun: { obs: obstaclesFor(s3, 0, s3.site!), house: s3 } }).some(x => x.code === 'SUN_LOW'));
  assert.ok(corners(n2).length === 4);
});
test('regresie: vecinul la distanța 0 (calcan pe limită) e lipit de casă, nu pus peste ea', () => {
  const fl = box('S'), s = snapOf(fl), b = houseBox(s)!;
  for (const az of [0, 90, 180, 225]){ const n = placeNeighbour(s, site(), { id: 'n1', name: 'Lipit', trueAz: az, dist: 0, w: 8, d: 8, h: 10 });
    assert.ok(gap(n, b) > 0 && gap(n, b) < .03, `az ${az}: gap ${gap(n, b)}`);
    // niciun colț al vecinului nu intră în casă
    assert.ok(corners(n).every(([x, z]) => !(x > b.x0 + .01 && x < b.x1 - .01 && z > b.z0 + .01 && z < b.z1 - .01)), `az ${az}`); }
  const n = moveTo(placeNeighbour(s, site(), { id: 'n1', name: 'x', trueAz: 0, dist: 10, w: 8, d: 8, h: 10 }), s, 0); assert.ok(gap(n, b) < .03 && gap(n, b) > 0);
});
test('regresie: numele moștenite din prototip („toString”, „constructor”) nu trec drept geam sau protecție și nu strică calculul', () => {
  assert.throws(() => checkSite({ ...site(), glazing: 'toString' }), SiteError);
  assert.throws(() => checkSite({ ...site(), shades: { w1: 'constructor' } }), SiteError);
  const fl = box('S'); fl.walls[2]!.openings[0]!.id = 'constructor';
  const r = summerCheck(fl, site()).rooms[0]!; assert.deepEqual(r.unshaded, ['constructor']);
  const g = solarGain(fl, site({ glazing: 'toString' as never }), toUtc, '2026-07-21').windows[0]!; assert.ok(Number.isFinite(g.kwh) && g.kwh > 0);
});
test('regresie: fusul orar pentru estul Ungariei și Serbia nu e cel românesc; nordul Moldovei e Chișinău', () => {
  assert.equal(guessTz(44.79, 20.45), 'Europe/Belgrade'); assert.equal(guessTz(47.53, 21.63), 'Europe/Budapest'); assert.equal(guessTz(48.1, 20.78), 'Europe/Budapest');
  assert.equal(guessTz(47.76, 27.93), 'Europe/Chisinau'); assert.equal(guessTz(43.85, 25.97), 'Europe/Sofia');
  // toate orașele din listă cad în fusul lor
  for (const [lat, lon, tz] of [[47.07, 21.92, 'Europe/Bucharest'], [47.79, 22.89, 'Europe/Bucharest'], [46.18, 21.31, 'Europe/Bucharest'], [44.63, 22.66, 'Europe/Bucharest'],
    [45.18, 28.8, 'Europe/Bucharest'], [45.44, 28.05, 'Europe/Bucharest'], [47.75, 26.67, 'Europe/Bucharest'], [47.16, 27.59, 'Europe/Bucharest'], [43.9, 25.97, 'Europe/Bucharest']] as const)
    assert.equal(guessTz(lat, lon), tz, `${lat}, ${lon}`);
});
test('regresie: un vecin de locuințe mai jos de un etaj are totuși un punct verificat (fără Infinity în raport)', () => {
  const s = snapOf(box('S')), n = placeNeighbour(s, site(), { id: 'n1', name: 'Garaj', trueAz: 0, dist: 3, w: 6, d: 4, h: 1.6 });
  assert.equal(targetsOf(n).length, 2); assert.ok(targetsOf(n).every(t => Math.abs(t.p[1] - .8) < 1e-9));
  const st = neighbourStudy(s, site({ neighbors: [n] }), toUtc, NORM_REFERENCE_DATE, NORM_HOURS)[0]!; assert.ok(Number.isFinite(st.worstBefore) && Number.isFinite(st.worstAfter));
});
test('regresie: fusul orar lângă granițele fără oră comună (vestul Bulgariei, Transcarpatia)', () => {
  assert.equal(guessTz(42.28, 22.69), 'Europe/Sofia'); assert.equal(guessTz(48.44, 22.72), 'Europe/Kyiv'); assert.equal(guessTz(48.1, 20.78), 'Europe/Budapest');
});
