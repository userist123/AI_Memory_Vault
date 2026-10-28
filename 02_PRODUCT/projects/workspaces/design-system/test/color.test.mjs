// OKLCH is the design source and hex the exact output; this keeps the two from drifting apart.
import test from 'node:test';
import assert from 'node:assert/strict';
import { allTokens } from './tokens.mjs';

// Björn Ottosson, "A perceptual color space for image processing" (OKLab -> linear sRGB).
function oklchToSrgb([L, C, H]) {
  const h = (H * Math.PI) / 180;
  const a = C * Math.cos(h), b = C * Math.sin(h);
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3;
  const lin = [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ];
  const gamma = (x) => (x <= 0.0031308 ? 12.92 * x : 1.055 * x ** (1 / 2.4) - 0.055);
  return lin.map((x) => Math.round(Math.min(1, Math.max(0, gamma(x))) * 255));
}

function colours() {
  const out = [];
  for (const t of allTokens()) {
    if (t.type === 'color') out.push([t.path, t.value]);
    if (t.type === 'shadow') for (const s of [t.value].flat()) out.push([`${t.path}.color`, s.color]);
  }
  return out;
}

test('there are colour tokens to check', () => {
  assert.ok(colours().length >= 19);
});

test('oklch components match hex within 1/255 per channel', () => {
  for (const [p, c] of colours()) {
    assert.equal(c.colorSpace, 'oklch', p);
    const want = c.hex.slice(1).match(/../g).map((x) => parseInt(x, 16));
    const got = oklchToSrgb(c.components);
    got.forEach((v, i) => assert.ok(Math.abs(v - want[i]) <= 1, `${p}: ${got} vs ${c.hex}`));
  }
});
