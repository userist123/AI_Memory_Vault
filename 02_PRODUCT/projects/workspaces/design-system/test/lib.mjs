// Helpers shared by the tests: read the first :root block of a stylesheet and compare CSS values.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const CASA3D_APP = path.resolve(ROOT, '../casa3d/app/app');

const GENERIC = new Set(['serif', 'sans-serif', 'monospace', 'cursive', 'fantasy', 'system-ui', 'ui-serif', 'ui-sans-serif', 'ui-monospace', 'math', 'emoji']);

export function read(p) {
  return fs.readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
}

/** Custom properties declared in the first `:root{...}` block (comments removed). */
export function rootProps(css) {
  const clean = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const m = clean.match(/:root\s*\{([^}]*)\}/);
  if (!m) throw new Error('no :root block');
  const props = new Map();
  for (const decl of m[1].split(';')) {
    const i = decl.indexOf(':');
    if (i < 0) continue;
    const name = decl.slice(0, i).trim();
    if (!name.startsWith('--')) continue;
    if (props.has(name)) throw new Error(`duplicate ${name}`);
    props.set(name, decl.slice(i + 1).trim());
  }
  return props;
}

/** Follow `var(--x)` chains (whole-value only, as the token files use them). */
export function resolve(props, name, seen = new Set()) {
  if (seen.has(name)) throw new Error(`var() cycle at ${name}`);
  seen.add(name);
  const v = props.get(name);
  if (v === undefined) return undefined;
  const m = v.match(/^var\((--[a-z0-9-]+)\)$/i);
  return m ? resolve(props, m[1], seen) : v;
}

/** Equality modulo whitespace, case, leading zeros, `0px`/`0` and quotes around non-generic font names. */
export function normalize(value) {
  const parts = value.split(',').map((p) => {
    let s = p.trim().replace(/\s+/g, ' ').toLowerCase();
    const q = s.match(/^(['"])(.*)\1$/);
    if (q && !GENERIC.has(q[2])) s = q[2];
    return s
      .replace(/(^|[^0-9.])\.(\d)/g, '$10.$2')
      .replace(/(^|[\s(])0px\b/g, '$10')
      .replace(/\s*\/\s*/g, ' / ');
  });
  return parts.join(',');
}

/** Custom properties of every rule whose selector is exactly `selector` (comments removed; later rules win). */
export function ruleProps(css, selector) {
  const clean = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const props = new Map();
  for (const m of clean.matchAll(/(^|[}\s])([^{}]+?)\s*\{([^{}]*)\}/g)) {
    if (m[2].trim().replace(/\s+/g, ' ') !== selector) continue;
    for (const decl of m[3].split(';')) {
      const i = decl.indexOf(':');
      const name = decl.slice(0, i).trim();
      if (i > 0 && name.startsWith('--')) props.set(name, decl.slice(i + 1).trim());
    }
  }
  return props;
}

/**
 * Computed value of `name` on an element matched by the last scope of `chain` (outermost first, :root at 0).
 * As in CSS: a var() is substituted on the element that declares it, and an undeclared property inherits the
 * parent's computed value. So `--bg: var(--color-bg-canvas)` on :root does not follow a later scope's canvas.
 */
export function computed(chain, name, seen = new Set()) {
  for (let i = chain.length - 1; i >= 0; i--) {
    if (!chain[i].has(name)) continue;
    const key = `${i}:${name}`;
    if (seen.has(key)) throw new Error(`var() cycle at ${name}`);
    const m = chain[i].get(name).match(/^var\((--[a-z0-9-]+)\)$/i);
    return m ? computed(chain.slice(0, i + 1), m[1], new Set([...seen, key])) : chain[i].get(name);
  }
  return undefined;
}
