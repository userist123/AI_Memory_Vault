// Blocarea pieselor (core/lock.ts + operațiile din core/arrange.ts și core/edit-ops.ts): o piesă blocată nu se mută, nu se
// rotește și nu se aliniază, singură sau în grup; dublarea și repetarea merg, iar copiile pornesc deblocate; salvarea păstrează
// doar `locked: true`.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { isLocked, firstLocked, toggleLock, unlockedCopy } from '../core/lock';
import { nudgeGroup, rotateGroup, applyMoves, duplicateGroup, arrayGroup } from '../core/arrange';
import { nudgePlacement, duplicatePlacement } from '../core/edit-ops';
import type { Catalog, FurniturePlacement, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog;
const put = (id: string, variantId: string, x: number, z: number, locked = false): FurniturePlacement => ({ id, roomId: 'cam', group: variantId.slice(0, variantId.lastIndexOf('-')), variantId, x, z, rotation: 0, source: 'auto', ...(locked ? { locked: true as const } : {}) });
const fixture = (): Snapshot => ({ name: 'Blocare', floor: { id: 'f', name: 'P', ceilingHeight: 2.6, walls: [], rooms: [{ id: 'cam', name: 'Sala', type: 'living', rect: { x0: 0, z0: 0, x1: 12, z1: 10 } }] },
  placements: [put('masuta', 'masuta-0', 2, 2, true), put('birou', 'birou-0', 6, 6)], selections: {}, picked: [] });

test('isLocked, firstLocked, toggleLock: comutarea pe grup blochează tot dacă vreo piesă e deblocată, altfel deblochează tot', () => {
  const s = fixture(); assert.equal(isLocked(s, 'masuta'), true); assert.equal(isLocked(s, 'birou'), false);
  assert.equal(firstLocked(s, ['birou', 'masuta']), 'masuta'); assert.equal(firstLocked(s, ['birou']), null);
  const a = toggleLock(s, ['masuta', 'birou']); assert.equal(a.locked, true); assert.ok(a.snapshot.placements.every(p => p.locked === true));
  const b = toggleLock(a.snapshot, ['masuta', 'birou']); assert.equal(b.locked, false); assert.ok(b.snapshot.placements.every(p => !('locked' in p)));
  assert.equal(s.placements[1]!.locked, undefined, 'intrarea nu se schimbă');
  const c = unlockedCopy({ ...s.placements[0]!, mirrored: true }); assert.equal('locked' in c, false); assert.equal(c.mirrored, true);
});
test('piesa blocată nu se mută, nu se rotește și nu se aliniază — nici singură, nici în grup; piesa deblocată merge', () => {
  const s = fixture();
  const one = nudgePlacement(s, cat, 'masuta', 5, 0); assert.equal(one.ok, false); if (!one.ok) assert.equal(one.key, 'edit.locked');
  assert.equal(nudgePlacement(s, cat, 'birou', 5, 0).ok, true);
  for (const r of [nudgeGroup(s, cat, ['birou', 'masuta'], 5, 0), rotateGroup(s, cat, ['birou', 'masuta']), applyMoves(s, cat, [{ id: 'masuta', x: 3, z: 2 }])]){
    assert.equal(r.ok, false); if (!r.ok){ assert.equal(r.key, 'edit.locked'); assert.equal(r.id, 'masuta'); } }
  // alinierea care nu mută piesa blocată (ea e deja pe linie) e permisă
  assert.equal(applyMoves(s, cat, [{ id: 'masuta', x: 2, z: 2 }, { id: 'birou', x: 6.5, z: 6 }]).ok, true);
});
test('dublarea și repetarea merg pe piese blocate; copiile pornesc deblocate, originalul rămâne blocat', () => {
  const s = fixture();
  const d = duplicatePlacement(s, cat, 'masuta', 'copie'); assert.equal(d.ok, true); if (d.ok){ const c = d.snapshot.placements.find(p => p.id === 'copie')!; assert.equal('locked' in c, false); assert.equal(d.snapshot.placements.find(p => p.id === 'masuta')!.locked, true); }
  const g = duplicateGroup(s, cat, ['masuta'], i => `g${i}`); assert.equal(g.ok, true); if (g.ok) assert.ok(g.ids.every(id => !('locked' in g.snapshot.placements.find(p => p.id === id)!)));
  const a = arrayGroup(s, cat, ['masuta'], 2, 150, 0, i => `a${i}`); assert.equal(a.ok, true); if (a.ok) assert.ok(a.ids.every(id => !('locked' in a.snapshot.placements.find(p => p.id === id)!)));
});

// ---------- salvarea ----------
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import { checkSnapshot } from '../lib/repo';
beforeAll(() => resetDbForTests());
test('checkSnapshot păstrează doar `locked: true` și `mirrored: true`; alte valori dispar', () => {
  const s: any = fixture(); s.placements[1].locked = 'da'; s.placements[1].mirrored = 1; s.placements[0].mirrored = true;
  const out: any = checkSnapshot(s); assert.equal(out.placements[0].locked, true); assert.equal(out.placements[0].mirrored, true);
  assert.equal('locked' in out.placements[1], false); assert.equal('mirrored' in out.placements[1], false);
});
