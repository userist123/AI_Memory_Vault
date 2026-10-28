// Phase 1 promise: Casa3D looks exactly as before. Every custom property of the original :root block must
// still resolve to the same value from tokens.css (generated) plus what globals.css keeps by hand.
import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { ROOT, CASA3D_APP, read, rootProps, resolve, normalize } from './lib.mjs';

const baseline = rootProps(read(path.join(ROOT, 'test/fixtures/casa3d-root-baseline.css')));
const generated = rootProps(read(path.join(CASA3D_APP, 'tokens.css')));
const kept = rootProps(read(path.join(CASA3D_APP, 'globals.css')));

test('baseline fixture is the full original block', () => {
  assert.equal(baseline.size, 50);
});

test('globals.css and tokens.css do not declare the same property twice', () => {
  const both = [...kept.keys()].filter((k) => generated.has(k));
  assert.deepEqual(both, []);
});

test('every original property resolves to its original value', () => {
  const merged = new Map([...generated, ...kept]);
  for (const [name, old] of baseline) {
    const now = resolve(merged, name);
    assert.notEqual(now, undefined, `${name} is no longer defined`);
    assert.equal(normalize(now), normalize(resolve(baseline, name)), `${name} changed (was ${old})`);
  }
});

test('layout.tsx imports tokens.css, before globals.css', () => {
  // After globals.css the token block would beat its `@media (pointer:coarse)` --hit override (touch targets 36 px).
  const src = read(path.join(CASA3D_APP, 'layout.tsx'));
  const t = src.search(/^import\s+['"]\.\/tokens\.css['"];?\s*$/m);
  const g = src.search(/^import\s+['"]\.\/globals\.css['"];?\s*$/m);
  assert.ok(t >= 0, 'layout.tsx does not import ./tokens.css');
  assert.ok(g >= 0, 'layout.tsx does not import ./globals.css');
  assert.ok(t < g, 'tokens.css must be imported before globals.css');
});

test('later :root blocks in globals.css only override --hit', () => {
  // Scoped re-themes (.home, .ws) are fine; another :root would silently change the migrated values.
  const css = read(path.join(CASA3D_APP, 'globals.css')).replace(/\/\*[\s\S]*?\*\//g, '');
  const blocks = [...css.matchAll(/:root\s*\{([^}]*)\}/g)].slice(1);
  for (const [, body] of blocks) {
    const names = [...body.matchAll(/(--[a-z0-9-]+)\s*:/gi)].map((m) => m[1]);
    assert.deepEqual(names.filter((n) => n !== '--hit'), [], `extra :root block redeclares ${names}`);
  }
});

test('motion tokens keep the values globals.css had before they moved to tokens.css', () => {
  // Added to globals.css on main after the baseline (UI/UX P2-13), then moved into brands/casa3d/motion.json.
  const motion = {
    '--dur-fast': '120ms', '--dur-base': '200ms', '--dur-slow': '320ms',
    '--ease-out': 'cubic-bezier(.2,.8,.2,1)', '--ease-std': 'cubic-bezier(.4,0,.2,1)',
  };
  const merged = new Map([...generated, ...kept]);
  for (const [name, old] of Object.entries(motion)) {
    const now = resolve(merged, name);
    assert.notEqual(now, undefined, `${name} is no longer defined`);
    assert.equal(normalize(now).replace(/\s/g, ''), normalize(old).replace(/\s/g, ''), `${name} changed (was ${old})`);
  }
});

test('migrated values live in tokens.css, not in globals.css', () => {
  const legacy = ['--paper', '--paper-2', '--graphite', '--lead', '--rule'];
  assert.deepEqual([...kept.keys()].sort(), legacy.sort());
});
