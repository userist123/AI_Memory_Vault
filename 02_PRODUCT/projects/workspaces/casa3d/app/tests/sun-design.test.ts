import { test } from 'vitest';
import assert from 'node:assert/strict';
import { localToUtc } from '../core/solar';
import { insolationStudy, NORM_HOURS } from '../core/insolation';
import { sunRemedies, fits, exteriorSpans, END_CLEAR, GAP } from '../core/sun-design';
import { sunOnPieces, solarGain, clearSkyDni, spans, daySteps } from '../core/sun-effects';
import { validateFloor } from '../core/validate';
import type { Catalog, Floor, Opening, Wall } from '../core/types';
import type { SiteInfo } from '../core/site-location';

const TZ = 'Europe/Bucharest', toUtc = (d: string, m: number) => localToUtc(d, m, TZ);
const site = (northDeg = 0): SiteInfo => ({ lat: 44.43, lon: 26.10, northDeg, tz: TZ });
function box(side: 'S' | 'N' | 'E' | 'W', t = .3, extra: Partial<Opening> = {}): Floor {
  const W = 5, D = 4, win = (L: number): Opening => ({ id: 'w1', kind: 'window', offset: L / 2 - .7, width: 1.4, ...extra });
  const walls: Floor['walls'] = [
    { id: 'N', a: [0, 0], b: [W, 0], thickness: t, exterior: true, openings: side === 'N' ? [win(W)] : [] },
    { id: 'E', a: [W, 0], b: [W, D], thickness: t, exterior: true, openings: side === 'E' ? [win(D)] : [] },
    { id: 'S', a: [W, D], b: [0, D], thickness: t, exterior: true, openings: side === 'S' ? [win(W)] : [] },
    { id: 'W', a: [0, D], b: [0, 0], thickness: t, exterior: true, openings: side === 'W' ? [win(D)] : [{ id: 'd1', kind: 'door', offset: 1.5, width: .9, entrance: true }] }];
  return { id: 'f', name: 'Parter', ceilingHeight: 2.7, rooms: [{ id: 'r1', name: 'Dormitor', type: 'dormitor', rect: { x0: 0, z0: 0, x1: W, z1: D } }], walls };
}
const hours = (fl: Floor, s = site()) => insolationStudy(fl, s, toUtc, { date: '2026-12-21', stepMin: 5, floorGrid: false, roomId: 'r1' }).rooms[0]!.hours;

test('remediile pentru un dormitor cu fereastra spre nord: fiecare propunere chiar crește soarele, prima respectă norma', () => {
  const fl = box('N'); assert.equal(hours(fl), 0);
  let n = 0; const rem = sunRemedies(fl, site(), toUtc, 'r1', { max: 3, newId: () => `nou-${++n}` });
  assert.ok(rem.length >= 1); assert.ok(rem[0]!.meets);
  for (const r of rem){
    const real = hours(r.floor); assert.equal(real, r.afterH, 'cifra afișată e chiar rezultatul simulării'); assert.ok(r.afterH > r.beforeH);
    // geometria rezultată e validă: golul încape pe perete, departe de capete și de ușă
    assert.deepEqual(validateFloor(r.floor).filter(i => i.severity === 'ERROR'), []);
    const w = r.floor.walls.find(x => x.id === r.wallId)!, o = w.openings.find(x => x.id === r.openingId)!;
    assert.ok(fits(w, o.offset, o.offset + o.width, o.id));
  }
  assert.equal(new Set(rem.map(r => r.id)).size, rem.length);
  assert.equal(rem.filter(r => r.kind === 'new_window').every(r => r.openingId.startsWith('nou-')), true);
  // pe nord nu se propune nimic: o fereastră nouă trebuie să dea spre sud, est sau vest
  assert.ok(rem.every(r => r.facing !== 'N'));
  assert.equal(fl.walls.find(w => w.id === 'S')!.openings.length, 0, 'planul inițial rămâne neatins');
});
test('remediile preferă soluția mai ieftină: lărgirea ferestrei existente bate o fereastră nouă când ajunge', () => {
  // fereastră îngustă spre est într-un perete gros: glaful o umbrește iarna
  const fl = box('E', .6, { width: .6, offset: 1.7 }), before = hours(fl);
  const rem = sunRemedies(fl, site(), toUtc, 'r1', { max: 5 });
  assert.ok(before > 0 && before < NORM_HOURS, `înainte ${before}`);
  assert.equal(rem[0]!.kind, 'widen'); assert.ok(rem[0]!.meets); assert.equal(rem[0]!.widthM, 1);
  // o fereastră nouă dă mai mult soare, dar costă mai mult: vine după
  const nw = rem.find(r => r.kind === 'new_window')!; assert.ok(nw.afterH > rem[0]!.afterH && nw.glazingM2 > rem[0]!.glazingM2);
});
test('fits respectă capetele peretelui și distanța față de celelalte goluri', () => {
  const w: Wall = { id: 'x', a: [0, 0], b: [5, 0], thickness: .3, exterior: true, openings: [{ id: 'd', kind: 'door', offset: 2, width: 1 }] };
  assert.ok(fits(w, END_CLEAR, 1.5)); assert.ok(!fits(w, .1, 1)); assert.ok(!fits(w, 1, 2 - GAP + .05)); assert.ok(fits(w, 1, 2 - GAP));
  assert.ok(fits(w, 1.5, 3, 'd'), 'golul înlocuit nu se ciocnește cu el însuși'); assert.ok(!fits(w, 4, 4.9));
  // peretele dintre două camere nu are porțiune exterioară
  const fl = box('S'); const twoRooms: Floor = { ...fl, rooms: [...fl.rooms, { id: 'r2', name: 'Vecin', type: 'living', rect: { x0: 0, z0: 4, x1: 5, z1: 8 } }] };
  assert.deepEqual(exteriorSpans(twoRooms, twoRooms.walls.find(x => x.id === 'S')!, twoRooms.rooms[0]!), []);
  assert.equal(exteriorSpans(fl, fl.walls.find(x => x.id === 'S')!, fl.rooms[0]!).length, 1);
});

const cat: Catalog = { suppliers: [], products: [{ id: 'p1', group: 'pat', name: 'Pat', brand: '', category: '', model3d: '' }, { id: 'p2', group: 'comodaTv', name: 'Comodă TV', brand: '', category: '', model3d: '' }],
  variants: [{ id: 'pat-0', productId: 'p1', name: '', legacyIndex: 0, dimensionsCm: { w: 160, d: 200, h: 50 }, dimensionsConfidence: 'HIGH' as any, style: {} },
    { id: 'comodaTv-0', productId: 'p2', name: '', legacyIndex: 0, dimensionsCm: { w: 150, d: 40, h: 50 }, dimensionsConfidence: 'HIGH' as any, style: {} }], offers: [] };
test('soarele pe mobilier: patul din fundul camerei cu fereastră spre est prinde soarele de dimineață vara, nu și pe cel din colțul umbrit', () => {
  const fl = box('E', .2), bed = { id: 'b', roomId: 'r1', group: 'pat', variantId: 'pat-0', x: 3.8, z: 2, rotation: 0, source: 'manual' as const };
  const [hit] = sunOnPieces(fl, [bed], cat, site(), toUtc, '2026-06-21'); assert.ok(hit, 'patul din fața ferestrei de est e în soare');
  assert.equal(hit.effect, 'wake'); assert.ok(hit.firstMin! < 8 * 60, `primul soare la ${hit.firstMin}`); assert.ok(hit.spans.length >= 1 && hit.spans[0]!.from < '09:00');
  const corner = { ...bed, id: 'c', x: .9, z: 1.1 }; // lângă peretele de vest, departe de fereastră
  assert.deepEqual(sunOnPieces(fl, [corner], cat, site(), toUtc, '2026-06-21').filter(p => p.minutes > 60), []);
  assert.deepEqual(sunOnPieces(fl, [{ ...bed, group: 'masuta', variantId: 'pat-0' }], cat, site(), toUtc, '2026-06-21'), [], 'doar piesele sensibile');
});
test('intervalele: pașii consecutivi se unesc, o pauză desparte', () => {
  assert.deepEqual(spans([5, 15, 25, 45, 55], [true, true, false, true, true], 10), [{ from: '00:00', to: '00:20', minutes: 20 }, { from: '00:40', to: '01:00', minutes: 20 }]);
  assert.deepEqual(spans([5, 15], [false, false], 10), []);
  assert.deepEqual(spans([5, 25], [true, true], 10), [{ from: '00:00', to: '00:10', minutes: 10 }, { from: '00:20', to: '00:30', minutes: 10 }], 'un pas lipsă (soare sub orizont) rupe intervalul');
});
test('câștigul solar: soarele direct pe cer senin, iarna sudul câștigă, vara vestul încălzește mai mult decât nordul', () => {
  assert.equal(clearSkyDni(-1), 0); assert.ok(clearSkyDni(60) > 850 && clearSkyDni(60) < 1000, String(clearSkyDni(60))); assert.ok(clearSkyDni(5) < clearSkyDni(30));
  const g = (side: 'S' | 'N' | 'E' | 'W', date: string) => solarGain(box(side), site(), toUtc, date).rooms[0]!;
  const sWin = g('S', '2026-12-21'), nWin = g('N', '2026-12-21');
  assert.ok(sWin.kwh > 2, `sud iarna ${sWin.kwh}`);
  // nordul nu are soare direct iarna, doar difuza cerului și reflexia solului: puțin, dar nu zero
  assert.ok(nWin.kwh > 0 && nWin.kwh < sWin.kwh / 4, `nord iarna ${nWin.kwh}`); assert.ok(sWin.kwhTypical < sWin.kwh);
  assert.ok(g('W', '2026-06-21').kwh > g('N', '2026-06-21').kwh);
  // geamul mai mare primește proporțional mai mult
  const big = solarGain(box('S', .3, { width: 2.8, offset: 1.1 }), site(), toUtc, '2026-12-21').rooms[0]!; assert.ok(big.kwh > sWin.kwh * 1.6);
  assert.equal(sWin.kwhPerM2, Math.round(sWin.kwh / 20 * 1000) / 1000);
  assert.ok(daySteps(site(), toUtc, '2026-12-21').every(s => s.el > 0));
});
test('propunerile formează un front Pareto: niciuna nu e bătută de alta mai ieftină cu cel puțin același soare', async () => {
  const fl = box('E', .6, { width: .6, offset: 1.7 }), rem = sunRemedies(fl, site(), toUtc, 'r1', { max: 10 });
  const cost = (r: typeof rem[number]) => r.glazingM2 + (r.kind === 'new_window' ? 1.5 : 0);
  // dominanța standard: cel puțin la fel de bună pe ambele criterii și strict mai bună pe unul
  for (const c of rem) assert.ok(!rem.some(o => o !== c && o.afterH >= c.afterH && cost(o) <= cost(c) + 1e-9 && (o.afterH > c.afterH || cost(o) < cost(c) - 1e-9)), `${c.id} e dominată`);
});
test('ideea de reorganizare: dormitorul spre nord poate lua locul bucătăriei însorite de alături, de mărime potrivită', async () => {
  const { swapIdeas } = await import('../core/sun-design');
  // dormitor 5 × 4 cu fereastra spre nord; bucătărie 5 × 4 la sud, cu fereastra spre sud; peretele comun e interior
  const W = 5;
  const fl: Floor = { id: 'f', name: '', ceilingHeight: 2.7, rooms: [{ id: 'r1', name: 'Dormitor', type: 'dormitor', rect: { x0: 0, z0: 0, x1: W, z1: 4 } }, { id: 'k', name: 'Bucătărie', type: 'bucatarie', rect: { x0: 0, z0: 4, x1: W, z1: 8 } }],
    walls: [{ id: 'N', a: [0, 0], b: [W, 0], thickness: .3, exterior: true, openings: [{ id: 'w1', kind: 'window', offset: 1.8, width: 1.4 }] },
      { id: 'E', a: [W, 0], b: [W, 8], thickness: .3, exterior: true, openings: [] }, { id: 'S', a: [W, 8], b: [0, 8], thickness: .3, exterior: true, openings: [{ id: 'w2', kind: 'window', offset: 1.8, width: 1.4 }] },
      { id: 'W', a: [0, 8], b: [0, 0], thickness: .3, exterior: true, openings: [] }, { id: 'mid', a: [0, 4], b: [W, 4], thickness: .12, exterior: false, openings: [{ id: 'd', kind: 'door', offset: 2, width: .8 }] }] };
  const ideas = swapIdeas(fl, site(), toUtc, 'r1'); assert.equal(ideas.length, 1); assert.equal(ideas[0]!.withRoomId, 'k'); assert.ok(ideas[0]!.hours >= NORM_HOURS);
  // o cameră prea mică nu e propusă
  const small = { ...fl, rooms: [fl.rooms[0]!, { ...fl.rooms[1]!, rect: { x0: 0, z0: 4, x1: 2, z1: 8 } }] };
  assert.deepEqual(swapIdeas(small, site(), toUtc, 'r1'), []);
});

test('regresie: aplicarea pune doar peretele schimbat în planul curent; scările, stâlpii și cotele adăugate între timp rămân', async () => {
  const { applyRemedyWall } = await import('../core/sun-design');
  const fl = box('N'), [r] = sunRemedies(fl, site(), toUtc, 'r1'); assert.ok(r);
  const later: Floor = { ...fl, stairs: [{ id: 's1', x: 1, z: 1, width: .9, length: 3, rotation: 0 }], columns: [{ id: 'c1', x: 4, z: 3, w: .3, d: .3 } as any],
    annotations: [{ id: 'a1', kind: 'text', at: [1, 1], text: 'notă' } as any] };
  const applied = applyRemedyWall(later, r)!;
  assert.equal(applied.stairs?.length, 1); assert.equal(applied.columns?.length, 1); assert.equal(applied.annotations?.length, 1);
  assert.deepEqual(applied.walls.find(w => w.id === r.wallId), r.wall); assert.equal(hours(applied), r.afterH);
  // peretele mutat între timp: propunerea e depășită, nu se aplică pe o geometrie greșită
  const moved = { ...later, walls: later.walls.map(w => w.id === r.wallId ? { ...w, b: [w.b[0] + .5, w.b[1]] as [number, number] } : w) };
  assert.equal(applyRemedyWall(moved, r), null);
  // o ușă adăugată pe același perete după căutare nu e suprascrisă
  const door = { ...later, walls: later.walls.map(w => w.id === r.wallId ? { ...w, openings: [...w.openings, { id: 'd9', kind: 'door' as const, offset: 4, width: .8 }] } : w) };
  assert.equal(applyRemedyWall(door, r), null);
  // o nișă sau un calorifer pus pe acel perete după căutare face propunerea depășită
  const niche = { ...later, niches: [{ id: 'n9', wallId: r.wallId, offset: .3, width: 4.4, height: 1, depth: .1, elevation: 1 }] };
  assert.equal(applyRemedyWall(niche, r), null);
  const item = { ...later, wallItems: [{ id: 'i9', wallId: r.wallId, side: 1 as const, kind: 'shelf' as any, offset: 1, elevation: 1, width: 1, height: .3 }] };
  assert.equal(applyRemedyWall(item, r), null);
  const elsewhere = { ...later, niches: [{ id: 'n8', wallId: r.wallId === 'E' ? 'W' : 'E', offset: .5, width: .6, height: 1, depth: .1, elevation: 1 }] };
  assert.ok(applyRemedyWall(elsewhere, r), 'o nișă pe alt perete nu blochează');
});
test('regresie: fereastra propusă ocolește nișele și obiectele de pe perete', () => {
  const fl = box('N'), withNiche: Floor = { ...fl, niches: [{ id: 'n1', wallId: 'S', offset: .3, width: 4.4, height: 1, depth: .1, elevation: 1 }] };
  const wS = withNiche.walls.find(w => w.id === 'S')!;
  assert.ok(fits(wS, 1, 2.2)); assert.ok(!fits(wS, 1, 2.2, undefined, withNiche));
  for (const r of sunRemedies(withNiche, site(), toUtc, 'r1', { max: 10 })){
    assert.notEqual(r.wallId, 'S', 'peretele de sud e ocupat de nișă');
    assert.deepEqual(validateFloor(applyAll(withNiche, r)).filter(i => i.severity === 'ERROR'), []); }
});
function applyAll(fl: Floor, r: { wall: Wall }): Floor { return { ...fl, walls: fl.walls.map(w => w.id === r.wall.id ? r.wall : w) }; }
test('regresie: golurile propuse rămân sub 3 m și sub tavan, oricât de înalt e tavanul', () => {
  const fl = { ...box('N', .3, { sill: 1.5, height: 2.4 }), ceilingHeight: 4 };
  for (const r of sunRemedies(fl, site(), toUtc, 'r1', { max: 20 })){ const o = r.wall.openings.find(x => x.id === r.openingId)!;
    const h = o.height ?? 1.3, sill = o.sill ?? .9; assert.ok(h <= 3 + 1e-9, `înălțime ${h}`); assert.ok(sill + h <= fl.ceilingHeight + 1e-9); }
});
test('regresie: vârful camerei e suma la același moment, nu suma vârfurilor ferestrelor de pe fețe opuse', () => {
  const fl = box('E'), both: Floor = { ...fl, walls: fl.walls.map(w => w.id === 'W' ? { ...w, openings: [{ id: 'w2', kind: 'window' as const, offset: 1.3, width: 1.4 }] } : w) };
  const g = solarGain(both, site(), toUtc, '2026-06-21'), sum = g.windows.reduce((a, w) => a + w.peakW, 0), room = g.rooms[0]!;
  assert.equal(g.windows.length, 2); assert.ok(room.peakW < sum * .8, `${room.peakW} față de ${sum}`); assert.ok(room.peakW >= Math.max(...g.windows.map(w => w.peakW)));
});
