// Planșele pentru echipe: elevații (goluri, benzi, faianță, aplice), plan de tavan/iluminat, plan de pardoseli.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { finishesOf } from '../core/boq';
import { wallElevations, ceilingPlan, lightingLegend, floorPlan, alongWall, spotGrid } from '../core/drawings';
import { roomWindows } from '../core/textiles';
import { applyStyle, STYLE_PACKAGES } from '../core/styles';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Planșe', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const set = (s: Snapshot, id: string, p: Partial<RoomFinishes>) => { s.finishes = { ...(s.finishes || {}), [id]: { ...finishesOf(s, room(s, id)), ...p } }; return s; };

test('elevațiile: fiecare fereastră apare o dată, pe peretele ei, cu parapetul și lățimea reale', () => {
  const s = demo(), r = room(s, 'living'), el = wallElevations(s, mc, cat, s.floor, r), wins = roomWindows(s.floor, r);
  assert.equal(el.flatMap(e => e.openings).filter(o => o.kind === 'window').length, wins.length);
  for (const w of wins){ const e = el.find(x => x.side === w.side)!, o = e.openings.find(x => x.kind === 'window' && Math.abs(x.u1 - x.u0 - w.widthM) < 1e-6)!;
    assert.ok(o, w.side); assert.equal(o.y0, w.sillM); assert.ok(o.u0 >= 0 && o.u1 <= e.lengthM); }
  assert.ok(el.every(e => e.heightM === s.floor.ceilingHeight && e.baseboardM! > 0), 'plinta în camerele uscate');
});
test('privit din cameră, de la stânga la dreapta: N crește cu x, S scade, E crește cu z, W scade', () => {
  const r = { x0: 0, z0: 0, x1: 4, z1: 3 }; assert.equal(alongWall(r, 'N', 1, 0), 1); assert.equal(alongWall(r, 'S', 1, 3), 3); assert.equal(alongWall(r, 'E', 4, 1), 1); assert.equal(alongWall(r, 'W', 0, 1), 2);
});
test('benzile: lambriu + baghetă + vopsea în stilul clasic; faianța băii până la 1,2 m; aplicele de pat pe peretele lor', () => {
  const s = applyStyle(demo(), cat, mc, STYLE_PACKAGES.find(p => p.id === 'classic')!), liv = wallElevations(s, mc, cat, s.floor, room(s, 'living')).find(e => e.bands.length)!;
  assert.deepEqual(liv.bands.map(b => b.kind), ['panel', 'rail', 'paint']); assert.equal(liv.bands[0]!.y1, 1); assert.ok(liv.bands[1]!.y1 > 1 && liv.bands[1]!.y1 < 1.1);
  const bath = wallElevations(s, mc, cat, s.floor, room(s, 'baie')); assert.ok(bath.every(e => e.bands.every(b => b.kind === 'walltile' && b.y1 === 1.2)) && bath.some(e => e.bands.length));
  const bed = wallElevations(s, mc, cat, s.floor, room(s, 'dormitor')), withS = bed.filter(e => e.sconces.length); assert.equal(withS.length, 1); assert.equal(withS[0]!.sconces.length, 2);
  assert.ok(withS[0]!.sconces.every(x => x.u > 0 && x.u < withS[0]!.lengthM && x.y === 1.1));
});
test('tavan și iluminat: scafă, spoturi pe grilă, legendă cu bucăți; pardoseli cu model și direcție', () => {
  const s = set(demo(), 'living', { ceiling: { type: 'cove', dropCm: 10, coveCm: 30, spot: 'spot-mt143-9w', spots: 6 }, fixtures: [{ kind: 'pendant', anchor: 'table', count: 2 }] });
  const c = ceilingPlan(s, mc, cat, s.floor).find(x => x.roomId === 'living')!; assert.equal(c.spots.length, 6); assert.ok(c.cove && c.ledM! > 0); assert.equal(c.fixtures.length, 2); assert.equal(c.heightM, Math.round((s.floor.ceilingHeight - .1) * 100) / 100);
  const lg = lightingLegend(s, mc, s.floor); assert.equal(lg.find(l => l.kind === 'spot')!.count, 6); assert.equal(lg.find(l => l.kind === 'pendant')!.count, 2); assert.equal(lg.find(l => l.kind === 'led')!.unit, 'm');
  assert.deepEqual(spotGrid({ x0: 0, z0: 0, x1: 4, z1: 2 }, 2), [[1, 1], [3, 1]]);
  const t = set(demo(), 'living', { floor: 'parchet-krono-herringbone-k450', floorLayout: { pattern: 'herringbone', angle: 90 } }), f = floorPlan(t, mc, t.floor).find(x => x.roomId === 'living')!;
  assert.equal(f.pattern, 'herringbone'); assert.equal(f.angle, 90); assert.equal(f.kind, 'parquet'); assert.equal(f.groutMm, null);
  assert.equal(floorPlan(t, mc, t.floor).find(x => x.roomId === 'baie')!.kind, 'tile');
});
