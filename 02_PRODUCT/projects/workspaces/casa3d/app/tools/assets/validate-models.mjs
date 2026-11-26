#!/usr/bin/env node
// Validates every bundled glTF model with the Khronos glTF-Validator (npm "gltf-validator", pure JS).
// Usage: node tools/assets/validate-models.mjs [--dir <modelsDir>] [--exe <gltf_validator.exe>] [--json]
// Each model lives in <dir>/<name>/<name>.gltf with external .bin and textures.
// Exit 1 if any model has errors, if no models were found, or if --exe disagrees on the error count.
import { readFile, readdir, stat } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import * as validator from 'gltf-validator';

const here = dirname(fileURLToPath(import.meta.url));

function parseArgs(argv) {
  const out = { dir: resolve(here, '../../public/models'), exe: null, json: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--dir') out.dir = resolve(argv[++i] ?? '');
    else if (a === '--exe') out.exe = resolve(argv[++i] ?? '');
    else if (a === '--json') out.json = true;
    else throw new Error(`Unknown argument: ${a}`);
  }
  return out;
}

async function findModels(dir) {
  const models = [];
  let entries;
  try {
    entries = await readdir(dir, { withFileTypes: true });
  } catch {
    return models;
  }
  for (const e of entries) {
    if (!e.isDirectory()) continue;
    const gltf = join(dir, e.name, `${e.name}.gltf`);
    try {
      if ((await stat(gltf)).isFile()) models.push({ name: e.name, modelDir: join(dir, e.name), gltf });
    } catch {
      // directory without a matching .gltf: not a model
    }
  }
  return models.sort((a, b) => a.name.localeCompare(b.name));
}

function summarize(report) {
  const issues = report.issues ?? {};
  const messages = issues.messages ?? [];
  const warningCodes = [...new Set(messages.filter((m) => m.severity === 1).map((m) => m.code))].sort();
  const errorCodes = [...new Set(messages.filter((m) => m.severity === 0).map((m) => m.code))].sort();
  return {
    errors: issues.numErrors ?? 0,
    warnings: issues.numWarnings ?? 0,
    infos: issues.numInfos ?? 0,
    hints: issues.numHints ?? 0,
    errorCodes,
    warningCodes,
  };
}

async function validateModel(model) {
  const bytes = await readFile(model.gltf);
  const report = await validator.validateBytes(new Uint8Array(bytes), {
    uri: `${model.name}.gltf`,
    format: 'gltf',
    maxIssues: 0,
    writeTimestamp: false,
    externalResourceFunction: async (uri) =>
      new Uint8Array(await readFile(resolve(model.modelDir, decodeURIComponent(uri)))),
  });
  return summarize(report);
}

function runExe(exe, gltfPath) {
  const r = spawnSync(exe, ['-o', gltfPath], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error) return { error: String(r.error) };
  try {
    return summarize(JSON.parse(r.stdout));
  } catch (err) {
    return { error: `cannot parse exe output (exit ${r.status}): ${String(err)}` };
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const models = await findModels(args.dir);
  if (models.length === 0) {
    console.error(`No models found under ${args.dir}`);
    process.exit(1);
  }
  let failed = false;
  const results = [];
  for (const model of models) {
    let res;
    try {
      res = await validateModel(model);
    } catch (err) {
      res = { errors: 1, warnings: 0, infos: 0, hints: 0, errorCodes: ['VALIDATOR_EXCEPTION'], warningCodes: [], exception: String(err) };
    }
    if (res.errors > 0) failed = true;
    const entry = { name: model.name, ...res };
    let exeNote = '';
    if (args.exe) {
      const ex = runExe(args.exe, model.gltf);
      entry.exe = ex;
      if (ex.error) {
        failed = true;
        exeNote = ` exe=ERROR(${ex.error})`;
      } else if (ex.errors !== res.errors) {
        failed = true;
        exeNote = ` exe=MISMATCH(errors ${ex.errors} vs ${res.errors})`;
      } else {
        exeNote = ` exe=match(errors ${ex.errors}, warnings ${ex.warnings})`;
      }
    }
    results.push(entry);
    if (!args.json) {
      const codes = res.warningCodes.length ? res.warningCodes.join(',') : '-';
      const errCodes = res.errorCodes.length ? ` errorCodes=${res.errorCodes.join(',')}` : '';
      console.log(`${model.name} errors=${res.errors} warnings=${res.warnings} infos=${res.infos} warningCodes=${codes}${errCodes}${exeNote}`);
    }
  }
  if (args.json) {
    console.log(JSON.stringify({ validator: validator.version(), dir: args.dir, ok: !failed, models: results }, null, 2));
  } else {
    console.log(`${failed ? 'FAIL' : 'OK'}: ${models.length} models, validator ${validator.version()}`);
  }
  process.exit(failed ? 1 : 0);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
