// Walks the DTCG files and yields every token with its inherited $type and its scope (null outside brands/*/scopes).
import fs from 'node:fs';
import path from 'node:path';
import { ROOT } from './lib.mjs';

export function tokenFiles() {
  const files = [];
  const walk = (dir) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name.endsWith('.json') && e.name !== 'brand.json') files.push(p);
    }
  };
  walk(path.join(ROOT, 'tokens'));
  return files.sort();
}

export function allTokens() {
  const out = [];
  const visit = (node, trail, type, file) => {
    const t = node.$type ?? type;
    if ('$value' in node) {
      out.push({ path: trail.join('.'), type: t, value: node.$value, file, scope: file.match(/scopes[\\/]([^\\/]+)[\\/]/)?.[1] ?? null });
      return;
    }
    for (const [k, v] of Object.entries(node)) {
      if (!k.startsWith('$') && v && typeof v === 'object') visit(v, [...trail, k], t, file);
    }
  };
  for (const f of tokenFiles()) visit(JSON.parse(fs.readFileSync(f, 'utf8')), [], undefined, path.relative(ROOT, f));
  return out;
}

/** `{color.bg.canvas}` -> 'color.bg.canvas' for a DTCG reference value, else null. */
export const refTarget = (v) => (typeof v === 'string' ? v.match(/^\{([^{}]+)\}$/)?.[1] ?? null : null);
