import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import { hexToRgb, relativeLuminance, contrastRatio, rgbToHsl, hueDistance, isNeutral } from '../core/color';

const near = (a: number, b: number, e = 0.01) => assert.ok(Math.abs(a - b) <= e, `${a} != ${b}`);
describe('color', () => {
  test('hexToRgb: #rrggbb, #rgb, invalid', () => {
    assert.deepEqual(hexToRgb('#ff8000'), { r: 255, g: 128, b: 0 });
    assert.deepEqual(hexToRgb('#fff'), { r: 255, g: 255, b: 255 });
    assert.equal(hexToRgb('nope'), null); assert.equal(hexToRgb('#12345'), null);
  });
  test('luminanță WCAG: negru 0, alb 1', () => { assert.equal(relativeLuminance('#000000'), 0); near(relativeLuminance('#ffffff'), 1, 1e-9); near(relativeLuminance('#808080'), 0.2159, 0.001); });
  test('contrast: alb/negru = 21, simetric, identic = 1', () => {
    near(contrastRatio('#ffffff', '#000000'), 21, 1e-6); assert.equal(contrastRatio('#000', '#fff'), contrastRatio('#fff', '#000')); near(contrastRatio('#abcdef', '#abcdef'), 1, 1e-9);
  });
  test('rgbToHsl: primare și gri', () => {
    assert.deepEqual(rgbToHsl({ r: 255, g: 0, b: 0 }), { h: 0, s: 1, l: 0.5 });
    near(rgbToHsl({ r: 0, g: 255, b: 0 }).h, 120, 1e-6); near(rgbToHsl({ r: 0, g: 0, b: 255 }).h, 240, 1e-6);
    assert.equal(rgbToHsl({ r: 128, g: 128, b: 128 }).s, 0);
  });
  test('hueDistance: ocolește cercul', () => { assert.equal(hueDistance(350, 10), 20); assert.equal(hueDistance(0, 180), 180); assert.equal(hueDistance(90, 90), 0); assert.equal(hueDistance(10, 350), 20); });
  test('isNeutral: gri/alb/negru da, culori vii nu', () => {
    for (const h of ['#808080', '#f5f5f3', '#ffffff', '#050505', '#f6f6f4']) assert.equal(isNeutral(h), true, h);
    for (const h of ['#cc2222', '#2255cc', '#aea395'.replace('aea395', 'b08040')]) assert.equal(isNeutral(h), false, h);
  });
});
