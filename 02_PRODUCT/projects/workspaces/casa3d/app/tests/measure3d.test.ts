import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import { distance3, horizontalDistance, heightDifference, angleAt, polylineLength, polygonAreaXZ, perimeterXZ, formatMeasure, type P3 } from '../core/measure3d';
import { formatLength, formatArea } from '../core/format';

const p = (x: number, y: number, z: number): P3 => ({ x, y, z });
const near = (a: number, b: number) => assert.ok(Math.abs(a - b) < 1e-9, `${a} != ${b}`);

describe('measure3d', () => {
  test('3-4-5 distance', () => { near(distance3(p(0, 0, 0), p(3, 4, 0)), 5); near(distance3(p(0, 0, 0), p(0, 3, 4)), 5); });
  test('horizontal and height', () => {
    near(horizontalDistance(p(0, 0, 0), p(3, 9, 4)), 5);
    near(heightDifference(p(0, 1, 0), p(5, 3.5, 5)), 2.5);
    near(heightDifference(p(0, 3, 0), p(0, 1, 0)), -2);
  });
  test('angles', () => {
    near(angleAt(p(1, 0, 0), p(0, 0, 0), p(0, 0, 1)), 90);
    near(angleAt(p(-1, 0, 0), p(0, 0, 0), p(1, 0, 0)), 180);
    near(angleAt(p(1, 0, 0), p(0, 0, 0), p(2, 0, 0)), 0);
    near(angleAt(p(1, 0, 0), p(0, 0, 0), p(1, 1, 0)), 45);
  });
  test('zero-length arm gives NaN', () => {
    assert.ok(Number.isNaN(angleAt(p(0, 0, 0), p(0, 0, 0), p(1, 0, 0))));
    assert.ok(Number.isNaN(angleAt(p(1, 0, 0), p(0, 0, 0), p(0, 0, 0))));
  });
  test('polyline length', () => {
    near(polylineLength([p(0, 0, 0), p(3, 4, 0), p(3, 4, 2)]), 7);
    assert.equal(polylineLength([]), 0);
    assert.equal(polylineLength([p(1, 1, 1)]), 0);
  });
  const rect = [p(0, 0, 0), p(4, 0, 0), p(4, 0, 5), p(0, 0, 5)];
  test('rectangle 4x5', () => { near(polygonAreaXZ(rect), 20); near(perimeterXZ(rect), 18); near(perimeterXZ(rect, false), 13); });
  test('winding independent area', () => { near(polygonAreaXZ([...rect].reverse()), 20); });
  test('degenerate polygons', () => {
    for (const pts of [[], [p(1, 0, 1)], [p(0, 0, 0), p(1, 0, 1)]]) assert.equal(polygonAreaXZ(pts), 0);
    assert.equal(perimeterXZ([]), 0);
    assert.equal(perimeterXZ([p(0, 0, 0)]), 0);
    near(perimeterXZ([p(0, 0, 0), p(3, 0, 4)]), 5);
  });
  test('formatMeasure matches format.ts', () => {
    for (const u of ['metric', 'imperial'] as const) {
      assert.equal(formatMeasure('length', 5, u), formatLength(5, u));
      assert.equal(formatMeasure('length', 0.5, u), formatLength(0.5, u));
      assert.equal(formatMeasure('area', 20, u), formatArea(20, u));
    }
    assert.equal(formatMeasure('length', 5, 'metric', 'en'), formatLength(5, 'metric', 'en'));
    assert.equal(formatMeasure('angle', 90, 'metric'), '90,0°');
    assert.equal(formatMeasure('angle', 44.96, 'imperial'), '45,0°');
    assert.equal(formatMeasure('angle', 90, 'metric', 'en'), '90.0°');
  });
});
