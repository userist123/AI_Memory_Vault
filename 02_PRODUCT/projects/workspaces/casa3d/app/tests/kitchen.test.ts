// Bucătăria ca sistem: lungimea frontului, fronturi și mânere, blat, placare, LED, buget, 3D și validare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { kitchenRun, kitchenQuantities, kitchenOf, kitchenIssues, sanitizeKitchen, kitchenVisual } from '../core/kitchen';
import { viewerInput } from '../lib/viewer-input';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Bucătărie', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const set = (s: Snapshot, p: Partial<RoomFinishes>) => { s.finishes = { ...(s.finishes || {}), bucatarie: { ...finishesOf(s, room(s, 'bucatarie')), ...p } }; return s; };

test('lungimea frontului din varianta din catalog; fronturi și mânere după suspendate', () => {
  const s = demo(), run = kitchenRun(s, cat, 'bucatarie')!; assert.ok(run.lengthM > 1); assert.equal(run.modules, Math.round(run.lengthM / .6));
  const q = (p: object) => kitchenQuantities(run, { ...kitchenOf({ floor: '', wallPaint: '', light: '' }), ...p } as any);
  assert.equal(q({ upper: 'closed' }).fronts, 2 * run.modules); assert.equal(q({ upper: 'none' }).fronts, run.modules); assert.equal(q({ handle: 'none' }).handles, 0);
  assert.equal(kitchenRun(s, cat, 'living'), null);
});
test('buget: placarea e faianță doar dacă așa s-a ales; blatul la comandă, mânerele și fronturile schimbate apar ca necunoscute; LED-ul se cumpără pe lungime', () => {
  const base = computeBOQ(demo(), cat, mc); assert.ok(base.items.some(i => i.key === 'bucatarie:walltile'), 'fără sistem: faianța implicită rămâne');
  const s = set(demo(), { kitchen: { backsplash: 'glass', frontColor: '#1f3a33', frontFinish: 'gloss', handle: 'profile', underLed: 'banda-led-hoff-3000k' } }), b = computeBOQ(s, cat, mc);
  assert.ok(!b.items.some(i => i.key === 'bucatarie:walltile')); const run = kitchenRun(s, cat, 'bucatarie')!;
  assert.ok(b.unknown.some(u => /Blat bucătărie/.test(u)) && b.unknown.some(u => /Placare sticlă/.test(u)) && b.unknown.some(u => /Mânere bucătărie/.test(u)) && b.unknown.some(u => /Fronturi bucătărie/.test(u)));
  const led = b.items.find(i => i.key === 'bucatarie:kitchen-led')!; assert.equal(led.netQty, Math.round(run.lengthM * 100) / 100);
});
test('avertismente și 3D', () => {
  const s = set(demo(), { wallTile: null, kitchen: { backsplash: 'tile', upper: 'none', underLed: 'banda-led-hoff-3000k', frontFinish: 'gloss', handle: 'none' } });
  const keys = kitchenIssues(s, cat, mc, 'bucatarie', finishesOf(s, room(s, 'bucatarie'))).map(i => i.key);
  assert.deepEqual(keys.sort(), ['kit.glossNoHandle', 'kit.ledNoUpper', 'kit.noTile']);
  const t = set(demo(), { kitchen: { frontColor: '#1f3a33', countertopColor: '#2b2b2b', countertopMm: 20, backsplash: 'countertop' } });
  const v = kitchenVisual(mc, finishesOf(t, room(t, 'bucatarie')))!; assert.equal(v.topM, .02); assert.equal(v.backsplashColor, '#2b2b2b');
  const it = viewerInput(t, cat, undefined, { mc }).items.find(i => i.group === 'bucatarie')!; assert.equal((it.variant as any).s.k.frontColor, '#1f3a33');
  assert.equal((viewerInput(demo(), cat, undefined, { mc }).items.find(i => i.group === 'bucatarie')!.variant as any).s.k, undefined, 'fără alegeri, bucătăria rămâne ca în catalog');
});
test('validare', () => {
  assert.equal(sanitizeKitchen({ handle: 'magnet' }), 'Bucătăria este invalidă.'); assert.equal(sanitizeKitchen({ countertopMm: 300 }), 'Bucătăria este invalidă.');
  assert.equal(sanitizeKitchen(null), 'Bucătăria este invalidă.'); assert.equal(sanitizeKitchen({ frontColor: '#112233', upper: 'closed' }), null);
});
