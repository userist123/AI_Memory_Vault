import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import { snapDraw, pointAtLength } from '../core/snap';

const floor = { walls: [{ id: 'w1', a: [5, 5], b: [8, 5] }] } as never;
const none = { walls: [] } as never;
const near = (a: number, b: number, e = 1e-9) => assert.ok(Math.abs(a - b) <= e, `${a} != ${b}`);

describe('snapDraw', () => {
  test('capătul de perete bate unghiul', () => {
    const r = snapDraw([8.05, 5.04], { floor, anchor: [2, 2] });
    assert.equal(r.kind, 'endpoint'); assert.deepEqual(r.p, [8, 5]);
    assert.ok(r.length! > 0 && r.angleDeg !== null);
  });
  test('capătul egal cu anchor e ignorat', () => {
    const r = snapDraw([5.02, 5.02], { floor, anchor: [5, 5] });
    assert.notEqual(r.kind, 'endpoint');
  });
  test('unghi 0, 90, 45, 30', () => {
    const a: [number, number] = [1, 1];
    let r = snapDraw([4.02, 1.02], { floor: none, anchor: a });
    assert.equal(r.kind, 'angle'); assert.deepEqual(r.p, [4, 1]); assert.equal(r.angleDeg, 0); assert.equal(r.length, 3);
    r = snapDraw([1.01, 5.02], { floor: none, anchor: a });
    assert.deepEqual(r.p, [1, 5]); assert.equal(r.angleDeg, 90);
    r = snapDraw([1 + 2.1, 1 + 2.1], { floor: none, anchor: a });
    assert.equal(r.angleDeg, 45); near(r.length!, Math.round(Math.hypot(2.1, 2.1) / .05) * .05, 1e-3);
    r = snapDraw([1 + 3 * Math.cos(Math.PI / 6), 1 + 3 * Math.sin(Math.PI / 6)], { floor: none, anchor: a });
    assert.equal(r.angleDeg, 30); near(r.length!, 3, 1e-3);
    near(r.p[0], 1 + 3 * Math.cos(Math.PI / 6), 1e-3); near(r.p[1], 2.5, 1e-3);
    assert.ok(Math.abs(r.length! / .05 - Math.round(r.length! / .05)) < 1e-6);
  });
  test('cursor la 7° de axă -> 0°', () => {
    const t = 7 * Math.PI / 180, r = snapDraw([3 * Math.cos(t), 3 * Math.sin(t)], { floor: none, anchor: [0, 0] });
    assert.equal(r.angleDeg, 0); assert.equal(r.p[1], 0);
  });
  test('Shift dă punctul liber la mm', () => {
    const r = snapDraw([1.23456, 2.34567], { floor, anchor: [0, 0], free: true });
    assert.equal(r.kind, 'free'); assert.deepEqual(r.p, [1.235, 2.346]); assert.notEqual(r.length, null);
  });
  test('fără anchor -> grilă, length/angle null', () => {
    const r = snapDraw([1.23, 2.38], { floor: none });
    assert.equal(r.kind, 'grid'); assert.deepEqual(r.p, [1.25, 2.4]); assert.equal(r.length, null); assert.equal(r.angleDeg, null);
  });
  test('determinist: 4 m la 90° -> exact', () => {
    const r = snapDraw([3.01, 7.02 + 0.0001], { floor: none, anchor: [3, 3] });
    assert.deepEqual(r.p, [3, 7]); assert.equal(r.length, 4); assert.equal(r.angleDeg, 90);
  });
  test('unghi 180 și 270, normalizat 0..360', () => {
    assert.equal(snapDraw([-2, 0], { floor: none, anchor: [0, 0] }).angleDeg, 180);
    assert.equal(snapDraw([0, -2], { floor: none, anchor: [0, 0] }).angleDeg, 270);
  });
  test('lungime 0 după rotunjire -> anchor', () => {
    const r = snapDraw([1.01, 1], { floor: none, anchor: [1, 1] });
    assert.deepEqual(r.p, [1, 1]); assert.equal(r.length, 0);
  });
});

describe('pointAtLength', () => {
  test('anchor [1,2] spre [1,9], 4 -> [1,6]', () => assert.deepEqual(pointAtLength([1, 2], [1, 9], 4), [1, 6]));
  test('45°: lungime exactă la mm', () => {
    const p = pointAtLength([0, 0], [5, 5.2], 3), L = Math.hypot(p[0], p[1]);
    near(L, 3, 1e-3); assert.equal(p[0], p[1]);
  });
  test('free: direcția nerotunjită', () => {
    const p = pointAtLength([0, 0], [3, 1], 2, 15, true); near(Math.hypot(...p), 2, 1e-3); assert.ok(p[1] > 0.5 && p[1] < 0.7);
  });
});
