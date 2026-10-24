// Exportul complet: o arhivă ZIP validă cu proiectul, reviziile, comenzile și devizul (CSV), pozele, randările;
// fără cheile linkurilor clientului; doar proprietarul îl poate descărca.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const RD = mkdtempSync(path.join(os.tmpdir(), 'c3d-exp-')); process.env.CASA3D_RENDER_DIR = RD;
// Python pe Windows scrie implicit în cp1252 și nu poate afișa diacriticele din arhivă
const PY = { env: { ...process.env, PYTHONIOENCODING: 'utf-8', PYTHONUTF8: '1' } };
const PY_BIN = process.platform === 'win32' ? 'python' : 'python3';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare } from '../lib/share';
import { exportProject } from '../lib/project-export';
import { zip } from '../lib/zip';
beforeAll(() => resetDbForTests());
const A = '33333333-aaaa-4aaa-8aaa-333333333333', B = '44444444-bbbb-4bbb-8bbb-444444444444';

test('arhiva ZIP se deschide cu unzip-ul sistemului și păstrează conținutul exact', () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'c3d-zip-')), f = path.join(dir, 't.zip'), big = 'ă'.repeat(5000);
  writeFileSync(f, zip([{ name: 'a.txt', data: 'salut' }, { name: 'd/b.txt', data: big }, { name: 'p.bin', data: Buffer.from([0, 1, 2, 255]) }]));
  const list = execFileSync(PY_BIN, ['-c', 'import zipfile,sys;z=zipfile.ZipFile(sys.argv[1]);assert z.testzip() is None;print("|".join(z.namelist()));print(z.read("d/b.txt").decode()==sys.argv[2])', f, big], PY).toString('utf8');
  assert.match(list, /a\.txt\|d\/b\.txt\|p\.bin/); assert.match(list, /True/);
});

test('exportul proiectului: tot ce trebuie, fără cheile linkurilor, doar pentru proprietar', async () => {
  const id = await repo.createProject(A, 'Apartament Ștefănescu', 'demo'); await repo.createRevision(A, id, 'v1'); const sh = await createShare(A, id, 1);
  mkdirSync(path.join(RD, id, '1791700000000'), { recursive: true }); writeFileSync(path.join(RD, id, '1791700000000', 'living.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47]));
  const { filename, data } = await exportProject(A, id); assert.match(filename, /^Apartament-Stefanescu-casa3d-\d{4}-\d{2}-\d{2}\.zip$/);
  const dir = mkdtempSync(path.join(os.tmpdir(), 'c3d-exp2-')), f = path.join(dir, 'e.zip'); writeFileSync(f, data);
  const out = execFileSync(PY_BIN, ['-c', 'import zipfile,sys,json;z=zipfile.ZipFile(sys.argv[1]);assert z.testzip() is None;n=z.namelist();print(json.dumps(n));print(z.read("linkuri-client.json").decode());print(z.read("deviz.csv").decode("utf-8-sig")[:200])', f], PY).toString('utf8');
  for (const n of ['proiect.json', 'revizii/revizia-1.json', 'comenzi.csv', 'deviz.csv', 'randari/1791700000000/living.png', 'CITESTE-MA.txt', 'alegeri-variante.json']) assert.ok(out.includes(`"${n}"`), n);
  assert.ok(!out.includes(sh.token), 'cheia linkului clientului nu intră în arhivă'); assert.match(out, /Categorie;Cameră;Articol/);
  await assert.rejects(exportProject(B, id), (e: any) => e.status === 404);
});
