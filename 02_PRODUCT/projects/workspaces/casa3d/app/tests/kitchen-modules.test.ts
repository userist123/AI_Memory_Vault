// Bucătăria pe module: propunere, verificări NKBA, blat cu îmbinări și decupaje.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { defaultModules, moduleIssues, worktop, checkModules } from '../core/kitchen-modules';

test('propunerea umple exact lungimea și trece verificările', () => {
  for (const run of [320, 360, 420, 243]){ const m = defaultModules(run); assert.ok(Math.abs(m.reduce((a, x) => a + x.w, 0) - run) < .01, `suma ${run}`);
    if (run >= 305){ assert.deepEqual(moduleIssues(m, run), [], `fără probleme la ${run}`); const i = m.findIndex(x => x.type === 'sink'); assert.equal(m[i + 1]!.type, 'dishwasher'); } }
});
test('greșeli tipice: sumă greșită, mașina de vase departe, frigider între chiuvetă și plită, plita la capăt', () => {
  const keys = (m: any[], run: number) => moduleIssues(m, run).map(i => i.key);
  assert.ok(keys([{ type: 'sink', w: 80 }, { type: 'base', w: 60 }], 200).includes('kmod.sum'));
  assert.ok(keys([{ type: 'dishwasher', w: 60 }, { type: 'base', w: 60 }, { type: 'base', w: 60 }, { type: 'sink', w: 80 }, { type: 'base', w: 60 }], 320).includes('kmod.dwFar'));
  assert.ok(keys([{ type: 'base', w: 60 }, { type: 'sink', w: 80 }, { type: 'fridge', w: 60 }, { type: 'base', w: 40 }, { type: 'hob', w: 60 }, { type: 'base', w: 40 }], 340).includes('kmod.tallBetween'));
  const end = keys([{ type: 'base', w: 60 }, { type: 'sink', w: 80 }, { type: 'dishwasher', w: 60 }, { type: 'drawers', w: 60 }, { type: 'hob', w: 60 }], 320);
  assert.ok(end.includes('kmod.hobLanding'), 'plita lipită de capăt: nu are blat pe o parte');
});
test('blatul: o bucată sub 4,1 m, îmbinare pe granița de corp peste, decupaje la chiuvetă și plită, rupt de corpurile înalte', () => {
  const short = worktop(defaultModules(320)); assert.equal(short.length, 1); assert.equal(short[0]!.stock, 410); assert.deepEqual(short[0]!.cutouts.map(c => c.type).sort(), ['hob', 'sink']);
  const long = worktop([{ type: 'base', w: 60 }, { type: 'sink', w: 80 }, { type: 'dishwasher', w: 60 }, { type: 'base', w: 60 }, { type: 'drawers', w: 60 }, { type: 'hob', w: 60 }, { type: 'base', w: 60 }, { type: 'base', w: 60 }]);
  assert.equal(long.length, 2); assert.equal(long[0]!.length + long[1]!.length, 500); assert.ok(long.every(p => p.length <= 410));
  const tall = worktop([{ type: 'base', w: 60 }, { type: 'fridge', w: 60 }, { type: 'sink', w: 80 }, { type: 'base', w: 60 }]); assert.equal(tall.length, 2, 'frigiderul rupe blatul');
  assert.throws(() => checkModules([{ type: 'x', w: 60 }])); assert.throws(() => checkModules([{ type: 'base', w: 500 }])); assert.equal(checkModules([{ type: 'base', w: 59.96 }])[0]!.w, 60);
});
