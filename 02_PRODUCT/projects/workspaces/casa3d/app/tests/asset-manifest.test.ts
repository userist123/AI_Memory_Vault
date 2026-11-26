import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
// @ts-ignore plain ESM script without types
import { buildManifest } from '../scripts/assets-manifest.mjs';

const root = path.resolve(import.meta.dirname, '..');
const modelsDir = path.join(root, 'public', 'models');
const manifest = JSON.parse(readFileSync(path.join(root, 'data', 'model-manifest.v1.json'), 'utf8')) as Record<string, any>;
const keys = Object.keys(manifest).sort();
const recomputed = buildManifest(modelsDir) as Record<string, any>;

describe('model asset manifest', () => {
  it('model dirs == manifest keys', () => {
    const dirs = readdirSync(modelsDir, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => e.name).sort();
    expect(dirs).toEqual(keys);
  });
  it('every file sha256 + size match', () => {
    for (const k of keys) expect(recomputed[k].files, k).toEqual(manifest[k].files);
  });
  it('triangles and positionBounds match recomputation', () => {
    for (const k of keys) {
      expect(recomputed[k].triangles, k).toBe(manifest[k].triangles);
      expect(recomputed[k].positionBounds, k).toEqual(manifest[k].positionBounds);
      expect(manifest[k].triangles).toBeGreaterThan(0);
    }
  });
  it('licence and source per entry', () => {
    for (const k of keys) {
      expect(manifest[k].license).toBe('CC0-1.0');
      expect(manifest[k].source).toBe(`https://polyhaven.com/a/${k}`);
    }
  });
  it('names in LICENSE.txt == manifest keys', () => {
    const txt = readFileSync(path.join(modelsDir, 'LICENSE.txt'), 'utf8');
    const names = [...txt.matchAll(/^- (\S+) \S+ https:\/\/polyhaven\.com\/a\/(\S+)$/gm)].map((m) => { expect(m[2]).toBe(m[1]); return m[1]; }).sort();
    expect(names).toEqual(keys);
  });
  it('engine and blender script only reference manifest models', () => {
    // viewer3d-engine.js builds the path dynamically (/models/${name}/${name}.gltf), so we assert the names
    // in its DECO table (key: [modelName, size, axis]) instead, plus any literal /models/<x>/<x>.gltf paths.
    const engine = readFileSync(path.join(root, 'components', 'viewer3d-engine.js'), 'utf8');
    expect(engine).toContain('/models/${name}/${name}.gltf');
    const decoLine = engine.split('\n').find((l) => l.startsWith('const DECO = '))!;
    expect(decoLine).toBeTruthy();
    const decoNames = [...decoLine.matchAll(/\w+:\s*\['([a-z0-9_]+)'/g)].map((m) => m[1]);
    expect(decoNames.length).toBeGreaterThan(0);
    for (const n of decoNames) expect(keys, n).toContain(n);
    // casa3d_render.py also builds the path dynamically (os.path.join(models_dir, name, f'{name}.gltf')): only literals are checked.
    const py = readFileSync(path.join(root, 'tools', 'blender', 'casa3d_render.py'), 'utf8');
    for (const src of [engine, py])
      for (const m of src.matchAll(/\/?models\/([a-z0-9_]+)\/\1\.gltf/g)) expect(keys).toContain(m[1]);
    expect(py).toContain("os.path.join(models_dir, name, f'{name}.gltf')");
  });
});
