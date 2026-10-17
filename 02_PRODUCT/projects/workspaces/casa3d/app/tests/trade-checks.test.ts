// Verificări de meserie: triunghiul de lucru (NKBA G3), spațiul lateral la WC/lavoar (B20/B5), dușul minim (B9),
// prizele lângă duș (volumul 2, I7) și ventilarea (I5-2022 tab. 6.1.1.1).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { adviseProject } from '../core/advisor';
import { i5ExtractFlow } from '../core/trade-checks';
import type { Catalog, Snapshot } from '../core/types';

const cat = catalogV1 as unknown as Catalog;
const demo = (): Snapshot => newSnapshot(cat, 'Verificări', 'demo');
const codes = (s: Snapshot) => adviseProject(s, cat).map(a => a.code);
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const tp = (id: string, kind: any, roomId: string, x: number, z: number) => ({ id, kind, roomId, x, z, height: .3, reason: 'test' });

test('ventilarea după I5-2022: debitul crește cu numărul de camere principale', () => {
  assert.deepEqual(i5ExtractFlow(1), { kitchen: 75, bath: 15 }); assert.deepEqual(i5ExtractFlow(3), { kitchen: 105, bath: 30 }); assert.deepEqual(i5ExtractFlow(9), { kitchen: 135, bath: 30 });
  const s = demo(); assert.ok(codes(s).includes('VENT_KITCHEN'));
});

test('triunghiul de lucru: prea mare → avertisment; în limite → nimic', () => {
  const s = demo(), k = room(s, 'bucatarie'), fridge = s.placements.find(p => p.roomId === 'bucatarie' && p.group === 'frigider');
  assert.ok(fridge, 'demo are frigider'); const { x0, z0, x1, z1 } = k.rect;
  s.tech = [tp('s', 'water_cold', 'bucatarie', fridge!.x + 1.3, fridge!.z), tp('h', 'cooker', 'bucatarie', fridge!.x + 2.6, fridge!.z)];
  assert.ok(!codes(s).some(c => c.startsWith('KITCHEN_TRIANGLE')), 'laturi 1,3 / 1,3 / 2,6 m: în limite');
  s.tech = [tp('s', 'water_cold', 'bucatarie', x0 + .1, z0 + .1), tp('h', 'cooker', 'bucatarie', x1 - .1, z1 - .1)];
  const far = Math.hypot(x1 - x0 - .2, z1 - z0 - .2);
  if (far > 2.7) assert.ok(codes(s).includes('KITCHEN_TRIANGLE_LONG'));
  s.tech = [tp('s', 'water_cold', 'bucatarie', fridge!.x + .3, fridge!.z), tp('h', 'cooker', 'bucatarie', fridge!.x + 1.6, fridge!.z)];
  assert.ok(codes(s).includes('KITCHEN_TRIANGLE_SHORT'), 'frigider la 30 cm de chiuvetă');
});

test('baie: priză în volumul 2 al dușului, WC lipit de perete, baie fără fereastră', async () => {
  const s = demo(), b = room(s, 'baie'), shower = s.placements.find(p => p.roomId === 'baie' && p.group === 'dus')!, wc = s.placements.find(p => p.roomId === 'baie' && p.group === 'wc')!;
  s.tech = [tp('o1', 'outlet', 'baie', shower.x, shower.z)];
  assert.ok(codes(s).includes('BATH_ZONE'), 'priză chiar lângă duș');
  s.tech = [tp('o1', 'outlet', 'baie', b.rect.x1 - .05, b.rect.z1 - .05)];
  const { footprintOf } = await import('../core/validate'); const f = footprintOf(cat, shower)!, px = b.rect.x1 - .05, pz = b.rect.z1 - .05;
  const dist = Math.hypot(Math.max(f.x0 - px, 0, px - f.x1), Math.max(f.z0 - pz, 0, pz - f.z1));
  assert.equal(codes(s).includes('BATH_ZONE'), dist < .6, `priza la ${dist.toFixed(2)} m de duș`);
  const wcClose = { ...s, placements: s.placements.map(p => p.id === wc.id ? (Math.round(p.rotation / (Math.PI / 2)) % 2 === 0 ? { ...p, x: b.rect.x0 + .2 } : { ...p, z: b.rect.z0 + .2 }) : p) };
  assert.ok(codes(wcClose).includes('FIXTURE_SIDE'), 'WC cu axa la 20 cm de perete');
  const hasWindow = s.floor.walls.some(w => w.openings.some(o => o.kind === 'window'));
  assert.ok(hasWindow); const noWin = { ...s, floor: { ...s.floor, walls: s.floor.walls.map(w => ({ ...w, openings: w.openings.filter(o => o.kind !== 'window') })) } };
  assert.ok(codes(noWin).includes('VENT_BATH'), 'fără ferestre deloc, baia cere ventilator');
});
