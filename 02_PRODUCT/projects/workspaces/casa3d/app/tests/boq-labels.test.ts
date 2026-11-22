// Etichetele liniilor din deviz (core/boq.ts) sunt în română: zecimalele se scriu cu virgulă.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { roDecimals } from '../core/boq';

test('roDecimals: punctul zecimal devine virgulă; fracțiile, dimensiunile și textul rămân', () => {
  assert.equal(roDecimals('Bandă LED sub suspendate · Bucătărie (2.43 m)'), 'Bandă LED sub suspendate · Bucătărie (2,43 m)');
  assert.equal(roDecimals('Vopsea pereți + tavan · Baie (11.6 m², 2 straturi)'), 'Vopsea pereți + tavan · Baie (11,6 m², 2 straturi)');
  assert.equal(roDecimals('Pardoseală · Hol (decalat 1/3)'), 'Pardoseală · Hol (decalat 1/3)');
  assert.equal(roDecimals('BESTÅ comodă TV, 180x42x38. Alb'), 'BESTÅ comodă TV, 180x42x38. Alb');
});
