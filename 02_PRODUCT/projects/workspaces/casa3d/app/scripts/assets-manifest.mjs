#!/usr/bin/env node
// Asset manifest for public/models: sha256 + size per file, triangles, positionBounds, source/license.
// Usage: node scripts/assets-manifest.mjs [--write | --check] [--dir <models dir>] [--out <manifest path>]
import { createHash } from 'node:crypto';
import { readFileSync, readdirSync, statSync, writeFileSync, existsSync } from 'node:fs';
import { join, dirname, resolve, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const opt = (n) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : undefined; };
const modelsDir = resolve(opt('--dir') ?? join(root, 'public', 'models'));
const outPath = resolve(opt('--out') ?? join(root, 'data', 'model-manifest.v1.json'));
const doWrite = args.includes('--write');

// Mirrors public/models/LICENSE.txt
const LICENSE = 'CC0-1.0', RETRIEVED = '2026-10-11', NOTE = 'textures reduced to 512 px';

function walk(dir) {
  const out = [];
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) out.push(...walk(p)); else out.push(p);
  }
  return out;
}

function analyze(gltf) {
  let triangles = 0;
  const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  const acc = gltf.accessors ?? [];
  for (const mesh of gltf.meshes ?? []) {
    for (const prim of mesh.primitives ?? []) {
      const pos = acc[prim.attributes?.POSITION];
      if (prim.mode === undefined || prim.mode === 4) {
        const n = prim.indices !== undefined ? acc[prim.indices].count : pos?.count ?? 0;
        triangles += n / 3;
      }
      if (pos?.min && pos?.max) for (let i = 0; i < 3; i++) { min[i] = Math.min(min[i], pos.min[i]); max[i] = Math.max(max[i], pos.max[i]); }
    }
  }
  return { triangles, positionBounds: { min, max } };
}

export function buildManifest(dir) {
  const manifest = {};
  for (const name of readdirSync(dir, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => e.name).sort()) {
    const mdir = join(dir, name);
    const files = {};
    for (const f of walk(mdir).sort()) {
      const buf = readFileSync(f);
      files[relative(mdir, f).split(sep).join('/')] = { bytes: statSync(f).size, sha256: createHash('sha256').update(buf).digest('hex') };
    }
    const gltfPath = join(mdir, `${name}.gltf`);
    const a = existsSync(gltfPath) ? analyze(JSON.parse(readFileSync(gltfPath, 'utf8'))) : { triangles: null, positionBounds: null };
    manifest[name] = { files, license: LICENSE, note: NOTE, positionBounds: a.positionBounds, retrieved: RETRIEVED, source: `https://polyhaven.com/a/${name}`, triangles: a.triangles };
  }
  return manifest;
}

function sortKeys(v) {
  if (Array.isArray(v)) return v.map(sortKeys);
  if (v && typeof v === 'object') return Object.fromEntries(Object.keys(v).sort().map((k) => [k, sortKeys(v[k])]));
  return v;
}

function flatten(v, prefix = '', out = {}) {
  if (v && typeof v === 'object') for (const k of Object.keys(v)) flatten(v[k], prefix ? `${prefix}.${k}` : k, out);
  else out[prefix] = JSON.stringify(v);
  return out;
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
  const current = sortKeys(buildManifest(modelsDir));
  if (doWrite) {
    writeFileSync(outPath, JSON.stringify(current, null, 2) + '\n');
    console.log(`written ${outPath} (${Object.keys(current).length} models)`);
  } else {
    const expected = JSON.parse(readFileSync(outPath, 'utf8'));
    const a = flatten(expected), b = flatten(current), diffs = [];
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
      if (!(k in b)) diffs.push(`missing on disk: ${k}`);
      else if (!(k in a)) diffs.push(`not in manifest: ${k}`);
      else if (a[k] !== b[k]) diffs.push(`changed: ${k} manifest=${a[k]} disk=${b[k]}`);
    }
    if (diffs.length) { console.error(`manifest drift (${diffs.length}):\n` + diffs.map((d) => '  ' + d).join('\n')); process.exit(1); }
    console.log(`manifest OK (${Object.keys(current).length} models)`);
  }
}
