import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// Validates the bundled glTF models with the Khronos validator, installed in isolation under tools/assets
// (`npm --prefix tools/assets ci`). The package is used only inside the .mjs script, never imported here.
const appDir = path.resolve(import.meta.dirname, '..');
const script = path.join(appDir, 'tools', 'assets', 'validate-models.mjs');
const modelsDir = path.join(appDir, 'public', 'models');
const validatorInstalled = fs.existsSync(path.join(appDir, 'tools', 'assets', 'node_modules', 'gltf-validator', 'package.json'));
const EXPECTED = [
  'ceramic_vase_01',
  'ceramic_vase_04',
  'hanging_picture_frame_02',
  'potted_plant_02',
  'potted_plant_04',
  'standing_picture_frame_01',
];

function run(args: string[]) {
  const r = spawnSync(process.execPath, [script, ...args], { cwd: appDir, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return { status: r.status, stdout: r.stdout ?? '', stderr: r.stderr ?? '' };
}

const suite = validatorInstalled ? describe : describe.skip;

suite(validatorInstalled ? 'glTF validation of bundled models' : 'glTF validation of bundled models (skipped: run `npm --prefix tools/assets ci` to install gltf-validator)', () => {
  let tmpRoot = '';

  beforeAll(() => {
    tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'casa3d-gltf-'));
  });

  afterAll(() => {
    const tmp = path.resolve(os.tmpdir());
    if (tmpRoot && path.resolve(tmpRoot).startsWith(tmp + path.sep)) fs.rmSync(tmpRoot, { recursive: true, force: true });
  });

  it('validates exactly the 6 bundled models with 0 errors', () => {
    const r = run([]);
    expect(r.status, r.stdout + r.stderr).toBe(0);
    const lines = r.stdout.split(/\r?\n/).filter((l) => /\serrors=\d+/.test(l));
    const names = lines.map((l) => l.split(' ')[0]).sort();
    expect(names).toEqual(EXPECTED);
    for (const l of lines) expect(l).toMatch(/\serrors=0\s/);
  });

  it('fails (exit 1) on a model with a broken accessor count', () => {
    const name = 'ceramic_vase_01';
    const dst = path.join(tmpRoot, name);
    fs.cpSync(path.join(modelsDir, name), dst, { recursive: true });
    const gltfPath = path.join(dst, `${name}.gltf`);
    const gltf = JSON.parse(fs.readFileSync(gltfPath, 'utf8'));
    gltf.accessors[0].count += 1_000_000;
    fs.writeFileSync(gltfPath, JSON.stringify(gltf));
    const r = run(['--dir', tmpRoot]);
    expect(r.status, r.stdout + r.stderr).toBe(1);
    expect(r.stdout).toMatch(new RegExp(`^${name} errors=[1-9]`, 'm'));
  });

  it('fails (exit 1) when no models are found', () => {
    const empty = fs.mkdtempSync(path.join(tmpRoot, 'empty-'));
    const r = run(['--dir', empty]);
    expect(r.status).toBe(1);
  });
});
