// Uneltele de plan din lotul 1: gol de trecere, ghenă / casetare, nișă, cote și texte persistente.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { doorOpenings } from '../core/doors';
import { doorZones } from '../core/validate';
import { computeBOQ, roomGeometry } from '../core/boq';
import { floorToPlan, planToFloor } from '../features/migration/legacy.js';
import { viewerInput } from '../lib/viewer-input';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { planToDxf } from '../core/dxf';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
// camera 5 × 4 m cu pereți exteriori (wall-1 = latura de nord, de la [0,0] la [5,0], fereastră la 1,8–3,2 m) și ușa de intrare pe wall-4
const blank = (): Snapshot => newSnapshot(cat, 'Unelte', 'blank');
const desc = (s: Snapshot) => describePlan(s, cat, { t: translator('ro'), units: 'metric', lang: 'ro' }).flatMap(g => g.items);
const ents = (dxf: string, type: string, layer: string) => dxf.split(`0\n${type}\n8\n${layer}\n`).length - 1;

describe('gol de trecere', () => {
  const withPassage = () => { const s = blank(); s.floor.walls[0]!.openings.push({ id: 'tr-1', kind: 'door', offset: .3, width: .9, passage: true }); return s; };
  test('rămâne ușă pentru suprafață (se scade ca ușa), dar nu cere produs de ușă și nu e fereastră', () => {
    const s = withPassage(), g = roomGeometry(s, s.floor.rooms[0]!), g0 = roomGeometry(blank(), blank().floor.rooms[0]!);
    assert.equal(g.windowWidth, g0.windowWidth); assert.ok(Math.abs(g.doorWidth - g0.doorWidth - .9) < 1e-9);
    assert.ok(!doorOpenings(s).some(d => d.id === 'tr-1'));
    s.doors = { 'tr-1': { product: mc.materials.find(m => m.category === 'door')!.id } };
    assert.ok(!computeBOQ(s, cat, mc).items.some(i => i.key === 'door:tr-1'));
  });
  test('zona din fața golului e de 60 cm (circulație), nu de 95 cm (arcul foii)', () => {
    const s = withPassage(), z = doorZones(s.floor, s.floor.rooms[0]!).find(r => r.x0 < .7 && r.x1 > .7 && r.z0 === 0)!;
    assert.ok(Math.abs(z.z1 - .6) < 1e-9);
  });
  test('3D: `trecere` ajunge la motor; adaptorul legacy face drumul dus-întors; proiectul vechi nu primește câmpul', () => {
    const s = withPassage(), pl = floorToPlan(s.floor, 'x');
    assert.equal((pl.pereti[0]!.goluri.find(g => g.la === .3) as any).trecere, true);
    assert.equal(planToFloor(pl).walls[0]!.openings.find(o => o.offset === .3)!.passage, true);
    assert.ok(!JSON.stringify(floorToPlan(blank().floor, 'x')).includes('trecere'));
    assert.ok(viewerInput(s, cat).plan.pereti[0].goluri.some((g: any) => g.trecere));
  });
  test('descriere, DXF: fără arc și fără foaie, doar tocurile', () => {
    const s = withPassage(); assert.match(desc(s).find(i => i.id === 'tr-1')!.label, /^Gol de trecere · 90/);
    const arcs = (x: Snapshot) => ents(planToDxf(x, cat, { lang: 'ro' }), 'ARC', 'DOORS'), lines = (x: Snapshot) => ents(planToDxf(x, cat, { lang: 'ro' }), 'LINE', 'DOORS');
    assert.equal(arcs(s), arcs(blank())); assert.equal(lines(s), lines(blank()) + 2);
  });
});
