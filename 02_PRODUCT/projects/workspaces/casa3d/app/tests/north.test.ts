// Orientarea casei (core/north.ts): declarată de utilizator, nu inventată; trece prin salvare și mută soarele din 3D.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { parseNorth, planAzimuth, planUpFacing } from '../core/north';

test('parseNorth: număr finit adus în [0, 360) la 0,1°; orice altceva = nedeclarat', () => {
  assert.deepEqual(parseNorth({ northDeg: 30 }), { northDeg: 30 });
  assert.deepEqual(parseNorth({ northDeg: -90 }), { northDeg: 270 });
  assert.deepEqual(parseNorth({ northDeg: 725.04 }), { northDeg: 5 });
  assert.deepEqual(parseNorth({ northDeg: 359.97 }), { northDeg: 0 });
  for (const bad of [null, undefined, 5, 'x', {}, { northDeg: '30' }, { northDeg: NaN }, { northDeg: Infinity }]) assert.equal(parseNorth(bad), null, JSON.stringify(bad));
});
test('planAzimuth: fără orientare rămâne convenția planului; cu orientare, soarele real se rotește cu nordul', () => {
  assert.equal(planAzimuth(135, null), 135);
  assert.equal(planAzimuth(180, { northDeg: 0 }), 180);
  // nordul real e la 90° (spre dreapta planului): sudul real cade spre stânga planului (270)
  assert.equal(planAzimuth(180, { northDeg: 90 }), 270);
  assert.equal(planAzimuth(300, { northDeg: 90 }), 30);
});
test('planUpFacing: spre ce punct cardinal privește sus-ul planului', () => {
  assert.equal(planUpFacing({ northDeg: 0 }), 'N');
  assert.equal(planUpFacing({ northDeg: 90 }), 'V');
  assert.equal(planUpFacing({ northDeg: 90 }, 'en'), 'W');
  assert.equal(planUpFacing({ northDeg: 315 }), 'NE');
  assert.equal(planUpFacing({ northDeg: 180 }), 'S');
});

// ---------- salvarea (lib/repo.ts) ----------
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import { checkSnapshot } from '../lib/repo';
beforeAll(() => resetDbForTests());
const base = () => ({ name: 'N', floor: { id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [], walls: [] }, placements: [], selections: {}, picked: [] });
test('checkSnapshot: orientarea declarată se păstrează curățată; una invalidă dispare fără să oprească salvarea', () => {
  assert.deepEqual((checkSnapshot({ ...base(), site: { northDeg: -45, extra: 1 } }) as any).site, { northDeg: 315 });
  assert.equal('site' in (checkSnapshot({ ...base(), site: { northDeg: 'nord' } }) as any), false);
  assert.equal('site' in (checkSnapshot(base()) as any), false);
});
