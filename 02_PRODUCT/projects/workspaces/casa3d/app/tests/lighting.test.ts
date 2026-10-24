import { test } from 'vitest';
import assert from 'node:assert/strict';
import { lighting, sunDirection, captureFileName } from '../core/lighting';
test('momentele zilei schimbă cerul, soarele și luminile interioare', () => {
  const d = lighting('day'), e = lighting('evening'), n = lighting('night');
  assert.ok(d.sun > e.sun && e.sun > n.sun, 'soarele scade spre noapte');
  assert.ok(n.interior > d.interior, 'noaptea luminile din casă sunt mai puternice');
  assert.equal(lighting('day', -90).azimuthDeg, 270); assert.equal(lighting('day', 450).azimuthDeg, 90);
});
test('direcția soarelui: est, sud și înălțimea', () => {
  const close = (a: number[], b: number[]) => a.every((v, i) => Math.abs(v - b[i]!) < 1e-9);
  assert.ok(close(sunDirection(90, 0), [1, 0, 0]), 'azimut 90 = est (+x)');
  assert.ok(close(sunDirection(180, 0), [0, 0, 1]), 'azimut 180 = sud (+z)');
  assert.ok(close(sunDirection(0, 90), [0, 1, 0]), 'la zenit, sus');
  const v = sunDirection(135, 50); assert.ok(Math.abs(Math.hypot(...v) - 1) < 1e-9, 'vector unitate');
});
test('numele capturii e sigur pentru orice sistem de fișiere', () => {
  assert.equal(captureFileName('Apartament demo', new Date('2026-10-10T12:00:00Z')), 'Apartament-demo-3d-2026-10-10.png');
  assert.equal(captureFileName('Casă / Ștefan: P+1', new Date('2026-10-10T12:00:00Z')), 'Casa-Stefan-P-1-3d-2026-10-10.png');
  assert.equal(captureFileName('***', new Date('2026-10-10T12:00:00Z')), 'proiect-3d-2026-10-10.png');
});
