// Baia ca sistem: zona de faianță, armături de aceeași culoare, duș walk-in, WC suspendat, buget, 3D și validare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { bathIssues, sanitizeBath, bathVisual, bathTileHeight, METAL_COLOR } from '../core/bath';
import { sanitizeFinishes } from '../core/finishes';
import { viewerInput } from '../lib/viewer-input';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes, Material } from '../core/types';

const cat = catalogV1 as unknown as Catalog, base = materials as unknown as MaterialsCatalog;
const prod = (id: string, category: string, price: number, specs: object = {}): Material => ({ id, category, name: id, supplier: 'Test', unit: 'buc', unitPrice: price, sourceUrl: null, verificationType: 'product_page', confidence: 'HIGH', specs, verifiedAt: '2026-10-10' } as any);
const mc: MaterialsCatalog = { ...base, materials: [...base.materials, prod('tap-negru', 'bath_tap', 300, { finish: 'negru mat' }), prod('set-dus', 'shower_set', 500),
  prod('rad', 'towel_radiator', 400), prod('oglinda-ip20', 'led_mirror', 350, { ip: 'IP20' })] };
const demo = (): Snapshot => newSnapshot(cat, 'Baie', 'demo');
const room = (s: Snapshot) => s.floor.rooms.find(r => r.id === 'baie')!;
const set = (s: Snapshot, p: Partial<RoomFinishes>) => { s.finishes = { ...(s.finishes || {}), baie: { ...finishesOf(s, room(s)), ...p } }; return s; };
const tiles = (s: Snapshot) => computeBOQ(s, cat, mc).items.find(i => i.key === 'baie:walltile')!.netQty;

test('zona de faianță: 1,2 m < 2,1 m (implicit) < până la tavan', () => {
  const lo = tiles(set(demo(), { bath: { tileZone: 'h120' } })), mid = tiles(set(demo(), { bath: {} })), full = tiles(set(demo(), { bath: { tileZone: 'full' } }));
  assert.ok(lo < mid && mid < full, `${lo} < ${mid} < ${full}`); assert.equal(mid, tiles(demo()), 'fără alegeri rămâne 2,1 m');
  assert.equal(bathTileHeight({ floor: '', wallPaint: '', light: '' }), 2.1); assert.equal(bathTileHeight({ floor: '', wallPaint: '', light: '', bath: { tileZone: 'full' } }), undefined);
});
test('buget: armăturile alese apar ca linii, cadrul WC și dușul walk-in ca necunoscute', () => {
  const b = computeBOQ(set(demo(), { bath: { tap: 'tap-negru', shower: 'set-dus', towelRadiator: 'rad', mirror: 'oglinda-ip20', wc: 'wall', showerType: 'walkin' } }), cat, mc);
  for (const k of ['bath-tap', 'shower-set', 'towel-radiator', 'led-mirror']) assert.equal(b.items.find(i => i.key === `baie:${k}`)?.orderedQty, 1, k);
  assert.ok(b.unknown.some(u => /Cadru WC suspendat/.test(u)) && b.unknown.some(u => /walk-in/.test(u)));
  const plain = computeBOQ(demo(), cat, mc); assert.ok(!plain.items.some(i => i.key.startsWith('baie:bath-')) && !plain.unknown.some(u => /walk-in|WC suspendat/.test(u)));
});
test('avertismente: walk-in, cadru WC, faianță joasă la duș, oglindă sub IP44, armături nepotrivite', () => {
  const s = set(demo(), { bath: { showerType: 'walkin', wc: 'wall', tileZone: 'h120', mirror: 'oglinda-ip20', tap: 'tap-negru', metal: 'chrome' } });
  assert.deepEqual(bathIssues(s, mc, room(s), finishesOf(s, room(s))).map(i => i.key).sort(), ['bath.metalMismatch', 'bath.mirrorIp', 'bath.tileLow', 'bath.walkin', 'bath.wallFrame']);
  const ok = set(demo(), { bath: { tap: 'tap-negru', metal: 'black' } }); assert.deepEqual(bathIssues(ok, mc, room(ok), finishesOf(ok, room(ok))), []);
});
test('3D: culoarea armăturilor și tipurile de duș și WC ajung la viewer doar când sunt alese', () => {
  assert.equal(bathVisual({ floor: '', wallPaint: '', light: '' }), null);
  const s = set(demo(), { bath: { metal: 'brass', showerType: 'walkin', wc: 'wall' } }), v = viewerInput(s, cat, undefined, { mc });
  const b = (v.items.find(i => i.group === 'dus')!.variant as any).s.b; assert.deepEqual(b, { metal: METAL_COLOR.brass, walkin: true, wallHung: true });
  assert.equal((viewerInput(demo(), cat, undefined, { mc }).items.find(i => i.group === 'dus')!.variant as any).s?.b, undefined);
});
test('validare', () => {
  assert.equal(sanitizeBath(undefined), null); assert.equal(sanitizeBath({ metal: 'black', tileZone: 'full' }), null);
  for (const bad of [[], 'x', { metal: 'gold' }, { tileZone: 'h300' }, { wc: 'x' }, { tap: 5 }, { mirror: 'x'.repeat(81) }]) assert.equal(sanitizeBath(bad), 'Baia este invalidă.', JSON.stringify(bad));
  const s = set(demo(), { bath: { metal: 'gold' } as any }); assert.equal(sanitizeFinishes(s.finishes as any), 'Baia este invalidă.');
});
