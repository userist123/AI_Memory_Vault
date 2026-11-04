// Runda 1 de review pentru lotul 2b: faianța băii pe porțiunile neplacate, numele obiectelor cu spații, tipurile
// câmpurilor text pe server, banda de deasupra unui tavan coborât.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { bathTiles, insertBand, bandsOverlap, sideSpan } from '../core/finishes';
import { stripPlanText, cleanPlanText, sanitizeFloorElements } from '../core/plan-limits';
import type { RoomFinishes, WallFeature } from '../core/types';

const room = { rect: { x0: 0, z0: 0, x1: 3.2, z1: 2 } };
const f = (wf: WallFeature[]): RoomFinishes => ({ wallTile: 'tile-x', wallFeatures: wf } as any);

test('faianța: o placare pe o porțiune scoate faianța doar de pe porțiunea ei', () => {
  const t = bathTiles(f([{ side: 'N', kind: 'stone', material: 'st', alongFromM: 1, alongToM: 1.6 } as WallFeature]), 'baie', room).filter(w => w.side === 'N');
  assert.deepEqual(t.map(w => [w.alongFromM, w.alongToM]), [[0, 1], [1.6, 3.2]]);
  assert.equal(bathTiles(f([]), 'baie', room).length, 4);
});
test('faianța: o placare pe toată latura sau fără cameră scoate latura întreagă (ca înainte)', () => {
  assert.equal(bathTiles(f([{ side: 'N', kind: 'paint', material: 'p' } as WallFeature]), 'baie', room).filter(w => w.side === 'N').length, 0);
  assert.equal(bathTiles(f([{ side: 'N', kind: 'stone', material: 'st', alongFromM: 1, alongToM: 1.6 } as WallFeature]), 'baie').filter(w => w.side === 'N').length, 0);
  assert.equal(sideSpan(room, 'N'), 3.2);
});
test('numele: la tastare spațiile rămân; la final se curăță marginile', () => {
  assert.equal(stripPlanText('Tablou '), 'Tablou ');
  assert.equal(stripPlanText('Tablou mare'), 'Tablou mare');
  assert.equal(cleanPlanText('  Tablou   mare '), 'Tablou mare');
});
test('server: un nume sau un text care nu e șir dă eroare de validare (400), nu TypeError', () => {
  const fl: any = { id: 'f', name: 'P', ceilingHeight: 2.6, rooms: [], walls: [{ id: 'w1', a: [0, 0], b: [4, 0], thickness: .15, exterior: false, openings: [] }] };
  fl.wallItems = [{ id: 'wi1', wallId: 'w1', side: 1, kind: 'art', offset: 1, width: .6, height: .4, elevation: 1.45, label: { toString: 'x' } }];
  assert.throws(() => sanitizeFloorElements(structuredClone(fl)), (e: any) => e.constructor.name === 'PlanLimitError');
  const fa: any = { ...fl, wallItems: undefined, annotations: [{ id: 't1', kind: 'text', at: [1, 1], text: { toString: 'x' } }] };
  assert.throws(() => sanitizeFloorElements(fa), (e: any) => e.constructor.name === 'PlanLimitError');
});
test('placare: o bandă peste tavanul coborât se taie la fel cum verifică serverul (10 m)', () => {
  const old: WallFeature = { side: 'N', kind: 'paint', material: 'p', fromM: 2.65 } as WallFeature;
  const out = insertBand([old], { side: 'N', kind: 'slats', material: 's', fromM: 2.7, alongFromM: 1, alongToM: 2 } as WallFeature, 3.2, 2.6);
  assert.equal(bandsOverlap(out, 10), false);
});

test('faianța: o bandă de deasupra faianței sau un brâu nu o scot; pragul de 5 cm nu depinde de rotunjire', () => {
  const g = (wf: WallFeature[]) => bathTiles({ wallTile: 'tile-x', bath: { tileZone: 'h120' }, wallFeatures: wf } as any, 'baie', room).filter(w => w.side === 'N').map(w => [w.alongFromM ?? 0, w.alongToM ?? 3.2]);
  const hi = bathTiles({ wallTile: 'tile-x', bath: { tileZone: 'h120' }, wallFeatures: [] } as any, 'baie', room)[0]!.heightM; assert.ok(hi !== undefined);
  if (hi !== undefined) assert.deepEqual(g([{ side: 'N', kind: 'paint', material: 'p', fromM: hi + .5, alongFromM: 1, alongToM: 2 } as WallFeature]), [[0, 3.2]]);
  assert.deepEqual(g([{ side: 'N', kind: 'rail', material: 'r', heightM: 1, alongFromM: 1, alongToM: 2 } as WallFeature]), [[0, 3.2]]);
  assert.deepEqual(g([{ side: 'N', kind: 'stone', material: 's', alongFromM: 0, alongToM: 2 } as WallFeature, { side: 'N', kind: 'stone', material: 's', alongFromM: 2.05, alongToM: 3.2 } as WallFeature]), [[2, 2.05]]);
});

test('faianța: vopseaua de sus și brâul pe toată latura nu scot faianța; latura neîntreruptă rămâne fără interval', () => {
  const g = (wf: WallFeature[], r: any = room) => bathTiles({ wallTile: 'tile-x', bath: { tileZone: 'h120' }, wallFeatures: wf } as any, 'baie', r).filter(w => w.side === 'N');
  for (const wf of [[{ side: 'N', kind: 'paint', material: 'p', fromM: 2.2 }], [{ side: 'N', kind: 'rail', material: 'r', heightM: 1.2 }]] as WallFeature[][]){
    const t = g(wf); assert.equal(t.length, 1); assert.equal(t[0]!.alongFromM, undefined); assert.equal(g(wf, undefined).length, 1); }
  const t = g([{ side: 'N', kind: 'paint', material: 'p', fromM: 2.2, alongFromM: 1, alongToM: 2 } as WallFeature]); assert.equal(t.length, 1); assert.equal(t[0]!.alongFromM, undefined);
  assert.equal(g([{ side: 'N', kind: 'paint', material: 'p' } as WallFeature]).length, 0);
});
