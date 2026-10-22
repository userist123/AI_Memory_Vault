// Every token has a DTCG 2025.10 type and a value of the right shape.
import test from 'node:test';
import assert from 'node:assert/strict';
import { allTokens, refTarget } from './tokens.mjs';

const isNum = (x) => typeof x === 'number' && Number.isFinite(x);
const dim = (v) => v && isNum(v.value) && ['px', 'rem'].includes(v.unit);
const color = (v) =>
  v && v.colorSpace === 'oklch' && v.components?.length === 3 && v.components.every(isNum) &&
  /^#[0-9A-F]{6}$/.test(v.hex) && (v.alpha === undefined || (v.alpha >= 0 && v.alpha <= 1));

const CHECK = {
  color,
  dimension: dim,
  duration: (v) => v && isNum(v.value) && v.value >= 0 && ['ms', 's'].includes(v.unit),
  cubicBezier: (v) => Array.isArray(v) && v.length === 4 && v.every(isNum) && [v[0], v[2]].every((x) => x >= 0 && x <= 1),
  fontFamily: (v) => typeof v === 'string' || (Array.isArray(v) && v.length > 0 && v.every((s) => typeof s === 'string')),
  shadow: (v) => [v].flat().every((s) => color(s.color) && ['offsetX', 'offsetY', 'blur', 'spread'].every((k) => dim(s[k]))),
};

test('every token has a known $type and a valid value', () => {
  const tokens = allTokens();
  assert.ok(tokens.length > 40);
  const base = new Map(tokens.filter((t) => !t.scope).map((t) => [t.path, t]));
  for (const t of tokens) {
    assert.ok(t.type in CHECK, `${t.file} ${t.path}: $type ${t.type}`);
    const ref = refTarget(t.value);
    if (ref) {
      // A reference must point at a real token of the same type (Style Dictionary resolves it).
      assert.equal(base.get(ref)?.type, t.type, `${t.file} ${t.path}: reference {${ref}} is not a ${t.type} token`);
      continue;
    }
    assert.ok(CHECK[t.type](t.value), `${t.file} ${t.path}: bad ${t.type} value ${JSON.stringify(t.value)}`);
  }
});

test('token paths are unique across files (once per scope)', () => {
  const seen = new Map();
  for (const t of allTokens()) {
    const key = `${t.scope ?? ''}:${t.path}`;
    assert.ok(!seen.has(key), `${t.path} in ${t.file} and ${seen.get(key)}`);
    seen.set(key, t.file);
  }
});

test('scopes only re-theme tokens the brand defines', () => {
  const tokens = allTokens();
  const base = new Map(tokens.filter((t) => !t.scope).map((t) => [t.path, t.type]));
  for (const t of tokens.filter((x) => x.scope)) {
    assert.equal(base.get(t.path), t.type, `${t.file} ${t.path}: not a base ${t.type} token`);
  }
});
