// UI/UX P2-11: vederile 3D salvate: validare tolerantă, înlocuire după nume, limită, tăiere în interval.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { parseView, parseViews, addView, removeView, clampCut, MAX_VIEWS, type EngineView } from '../core/saved-views';
const H: EngineView = { mode: 'house', th: -.7, ph: .9, r: 12, cut: .42 }, W: EngineView = { mode: 'walk', x: 2, z: 3, yaw: 1, pitch: -.1, cut: 1 };

test('vederile: machetă și tur; tăierea rămâne în [0,12; 1]; restul → null', () => {
  assert.deepEqual(parseView(H), H); assert.deepEqual(parseView(W), W);
  assert.equal(parseView({ ...H, cut: 5 })!.cut, 1); assert.equal(parseView({ ...H, cut: 0 })!.cut, .12); assert.equal(parseView({ mode: 'house', th: 0, ph: 1, r: 5 })!.cut, .42);
  for (const bad of [null, 1, 'x', {}, { ...H, r: 0 }, { ...H, th: NaN }, { ...W, x: '2' }, { ...H, mode: 'top' }]) assert.equal(parseView(bad), null, JSON.stringify(bad));
  assert.equal(clampCut(Infinity), .42);
});
test('lista din stocare: ce e stricat se ignoră, restul rămâne; id-uri unice; nume tăiate', () => {
  assert.deepEqual(parseViews('nu e json'), []); assert.deepEqual(parseViews('{"a":1}'), []);
  const l = parseViews(JSON.stringify([{ id: 'a', name: ' Living ', view: H }, { id: 'b', name: '', view: H }, { id: 'c', name: 'Rău', view: { mode: 'x' } }, { id: 'a', name: 'Dublură', view: W }, { id: 'd', name: 'x'.repeat(90), view: W }]));
  assert.deepEqual(l.map(v => [v.id, v.name.length]), [['a', 6], ['d', 40]]);
});
test('adăugare: același nume înlocuiește; cel mult 12, ies cele vechi; ștergere după id', () => {
  let l = addView([], 'Living', H, '1'); l = addView(l, 'living', W, '2'); assert.deepEqual(l.map(v => [v.id, v.view.mode]), [['2', 'walk']]);
  assert.equal(addView(l, '   ', H, '3').length, 1);
  for (let i = 0; i < 20; i++) l = addView(l, `V${i}`, H, `k${i}`);
  assert.equal(l.length, MAX_VIEWS); assert.equal(l[0]!.name, 'V8'); assert.deepEqual(removeView(l, 'k19').map(v => v.name).at(-1), 'V18');
});
