// UI/UX P2-13: tranzițiile de vedere au detectare de suport și respectă prefers-reduced-motion.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { canViewTransition, withViewTransition } from '../lib/motion';
const mm = (reduce: boolean) => ({ matchMedia: (q: string) => ({ matches: reduce && q.includes('reduce') }) });

test('fără API sau cu mișcare redusă: schimbarea se face direct', () => {
  let n = 0; withViewTransition(() => n++, {}, mm(false)); assert.equal(n, 1, 'browser fără View Transitions');
  let vt = 0; const d = { startViewTransition: (cb: () => void) => { vt++; cb(); } };
  withViewTransition(() => n++, d, mm(true)); assert.equal(vt, 0); assert.equal(n, 2, 'mișcare redusă');
  withViewTransition(() => n++, d, mm(false)); assert.equal(vt, 1); assert.equal(n, 3, 'cu tranziție');
  assert.equal(canViewTransition(undefined, undefined), false);
  const broken = { startViewTransition: () => { throw new Error('InvalidStateError'); } };
  withViewTransition(() => n++, broken, mm(false)); assert.equal(n, 4, 'o eroare a API-ului nu pierde schimbarea');
});
