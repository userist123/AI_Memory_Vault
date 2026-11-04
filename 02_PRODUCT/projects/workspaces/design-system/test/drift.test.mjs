// Drift gate: the committed outputs must equal a fresh build, so nobody edits tokens.css by hand.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { ROOT, read } from './lib.mjs';
import { listBrands, renderBrand } from '../build.mjs';

test('at least one brand is built', () => {
  assert.ok(listBrands().includes('casa3d'));
});

for (const brand of listBrands()) {
  test(`${brand}: committed outputs equal a fresh build`, async () => {
    const { css, committed } = await renderBrand(brand);
    for (const t of committed) {
      assert.ok(fs.existsSync(t), `${path.relative(ROOT, t)} missing: run npm run build`);
      assert.equal(read(t), css, `${path.relative(ROOT, t)} is stale or hand-edited: run npm run build`);
    }
  });

  test(`${brand}: every alias points at an existing token`, async () => {
    const { css, brand: cfg } = await renderBrand(brand);
    for (const [alias, target] of Object.entries(cfg.aliases ?? {})) {
      const v = `--${target.replaceAll('.', '-')}`;
      assert.match(css, new RegExp(`\\n  ${v}: `), `${alias} -> ${target}: token missing`);
      if (v === `--${alias}`) continue; // the token already carries the old name
      assert.match(css, new RegExp(`\\n  --${alias}: var\\(${v}\\);`), `${alias} not emitted`);
    }
  });
}
