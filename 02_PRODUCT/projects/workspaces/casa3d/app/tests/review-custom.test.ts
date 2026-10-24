// Regresii din review-ul independent: piesele pe comandă au preț necunoscut peste tot (diff, export, totaluri),
// iar serverul păstrează doar date plauzibile (dimensiuni, goluri sub tavan, chei și materiale exacte).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { pricedOffer, furnitureTotal, resolve } from '../core/catalog';
import { diffSnapshots } from '../core/diff';
import { roomWorks } from '../core/room-works';
import { checkSnapshot } from '../lib/repo';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Test', 'demo');

test('o piesă devenită pe comandă apare în diff: dimensiune schimbată, preț trecut la necunoscut', () => {
  const a = demo(), b = structuredClone(a), p = b.placements.find(x => x.group === 'canapea')!, price = resolve(cat, p.variantId)!.offer!.price;
  p.size = { w: 250, d: 100, h: 85 };
  const d = diffSnapshots(a, b, cat), c = d.cost.RON!;
  assert.equal(d.isEmpty, false); assert.deepEqual(d.furniture.resized.map(x => x.id), [p.id]); assert.match(d.furniture.resized[0]!.detail!, /din catalog → 250×100×85 cm/);
  assert.equal(c.delta, -price); assert.equal(c.unknownAfter, c.unknownBefore + 1);
  assert.equal(Number.isInteger(c.delta * 100), true, 'delta rotunjit la bani');
});
test('schimbarea de culoare sau material apare în diff', () => {
  const a = demo(), b = structuredClone(a); b.appearance = { items: { [b.placements[0]!.id]: { color: '#2f6f73' } }, rooms: { living: { walls: { color: '#c5d8e0' } } } };
  const d = diffSnapshots(a, b, cat); assert.equal(d.looks, 2); assert.equal(d.isEmpty, false);
  assert.equal(diffSnapshots(b, structuredClone(b), cat).isEmpty, true);
});
test('exportul pe cameră: piesa pe comandă nu are preț, magazin sau link, și se numără ca necunoscută', () => {
  const s = demo(), p = s.placements.find(x => x.group === 'canapea')!; p.size = { w: 250, d: 100, h: 85 };
  assert.equal(pricedOffer(cat, p), null); assert.equal(furnitureTotal(cat, [p]).unknown, 1);
  const w = roomWorks(s, cat, mc, p.roomId), f = w.furniture.find(x => x.id === p.id)!;
  assert.equal(f.price, null); assert.equal(f.offerId, null); assert.equal(f.retailer, null); assert.match(f.name, /pe comandă 250×100×85 cm/);
  const t = Object.values(w.totals)[0]!; assert.ok(t.unknown >= 1);
  const known = w.furniture.filter(x => x.price != null).reduce((a, x) => a + x.price!, 0); assert.ok(Math.abs(t.known - known) < 1e-6 || t.known >= known, 'prețul de catalog al piesei pe comandă nu intră în total');
});
test('serverul: dimensiuni doar w/d/h întregi, goluri sub tavan, chei de perete exacte, materiale permise pe model', () => {
  const s = demo(); (s.placements[0] as any).size = { w: 120.4, d: 60, h: 45, note: '<img onerror=alert(1)>' };
  assert.deepEqual(checkSnapshot(structuredClone(s)).placements[0]!.size, { w: 120, d: 60, h: 45 });
  const w = s.floor.walls.find(x => x.openings.some(o => o.kind === 'window'))!, win = w.openings.find(o => o.kind === 'window')!;
  win.sill = 2; win.height = 1; assert.throws(() => checkSnapshot(structuredClone(s)), (e: any) => e.status === 400 && /tavanul/.test(e.message));
  win.sill = 0.9; win.height = 1.5; assert.doesNotThrow(() => checkSnapshot(structuredClone(s)));
  const sofa = s.placements.find(x => x.group === 'canapea')!, kit = s.placements.find(x => x.group === 'bucatarie')!;
  s.appearance = { wallFaces: { [`${s.floor.walls[0]!.id}@hol@x`]: { color: '#ffffff' } }, items: { [sofa.id]: { material: 'velvet' }, [kit.id]: { color: '#ffffff', material: 'leather' } } };
  const c = checkSnapshot(structuredClone(s));
  assert.equal(c.appearance?.wallFaces, undefined, 'cheie cu trei părți respinsă');
  assert.deepEqual(c.appearance?.items, { [sofa.id]: { material: 'velvet' }, [kit.id]: { color: '#ffffff' } }, 'pielea nu e material de bucătărie');
});
