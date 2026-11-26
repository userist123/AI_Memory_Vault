// Randările locale: pornesc doar cu Blender configurat, un job odată, fișierele se citesc doar cu nume valide și doar de proprietar.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { startRender, listRenders, readRender } from '../lib/renders';
beforeAll(() => resetDbForTests());
const A = '33333333-3333-4333-8333-333333333333', B = '44444444-4444-4444-8444-444444444444';
const scene = JSON.stringify({ format: 'casa3d-scene', version: 1, rooms: [{ id: 'living' }], meshes: [], materials: [] });
const wait = (ms: number) => new Promise(r => setTimeout(r, ms));

test('fără Blender: 501; cu un „Blender” de test: job → imagine, un singur job odată, nume validate, alt proprietar refuzat', async () => {
  const id = await repo.createProject(A, 'Randări', 'demo'), dir = mkdtempSync(path.join(tmpdir(), 'casa3d-r-'));
  process.env.CASA3D_RENDER_DIR = path.join(dir, 'out'); delete process.env.CASA3D_BLENDER;
  await assert.rejects(startRender(A, id, scene, {}), (e: any) => e.status === 501);
  // „Blender” de test: scrie <out>/living.png (argumentele sunt: -b -P script -- scene out ...)
  // (un script Node, ca să meargă și pe Windows, unde un .sh nu se poate porni)
  // scrie imaginea doar dacă primește --python-exit-code 1 (altfel o eroare din script ar ieși cu 0)
  const fake = path.join(dir, 'blender.mjs'); writeFileSync(fake, "import { writeFileSync } from 'node:fs'; import path from 'node:path';\nconst a = process.argv.slice(2); const i = a.indexOf('--python-exit-code'); if (i < 0 || a[i + 1] !== '1' || i > a.indexOf('-P')) process.exit(0);\nsetTimeout(() => writeFileSync(path.join(a[a.indexOf('--') + 2], 'living.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47])), 300);\n"); process.env.CASA3D_BLENDER = fake;
  await assert.rejects(startRender(A, id, '{"nu":"e scena"}', {}), (e: any) => e.status === 400);
  const job = await startRender(A, id, scene, { samples: 64 }); assert.equal(job.status, 'running');
  await assert.rejects(startRender(A, id, scene, {}), (e: any) => e.status === 409);
  for (let i = 0; i < 40 && (await listRenders(A, id)).jobs[0]!.status === 'running'; i++) await wait(100);
  const l = await listRenders(A, id); assert.equal(l.available, true); assert.equal(l.jobs[0]!.status, 'done'); assert.deepEqual(l.jobs[0]!.images, ['living.png']);
  assert.equal((await readRender(A, id, job.job, 'living.png')).subarray(0, 4).toString('latin1'), '\x89PNG');
  for (const bad of ['../scene.casa3d.json', 'blender.log', 'x.png/../../a.png']) await assert.rejects(readRender(A, id, job.job, bad), (e: any) => e.status === 404);
  await assert.rejects(readRender(A, id, '../..', 'living.png'), (e: any) => e.status === 404);
  await assert.rejects(listRenders(B, id));
});

test('Blender iese cu 0 dar nu scrie nicio imagine (script oprit): jobul e „failed”, nu „done”', async () => {
  const id = await repo.createProject(A, 'Randare goală', 'demo'), dir = mkdtempSync(path.join(tmpdir(), 'casa3d-r0-'));
  process.env.CASA3D_RENDER_DIR = path.join(dir, 'out');
  const fake = path.join(dir, 'blender.mjs'); writeFileSync(fake, 'setTimeout(() => process.exit(0), 100);\n'); process.env.CASA3D_BLENDER = fake;
  await startRender(A, id, scene, {});
  for (let i = 0; i < 40 && (await listRenders(A, id)).jobs[0]!.status === 'running'; i++) await wait(100);
  const l = await listRenders(A, id); assert.equal(l.jobs[0]!.status, 'failed'); assert.deepEqual(l.jobs[0]!.images, []);
});

test('două cereri simultane: doar una pornește Blender, cealaltă primește 409', async () => {
  const id = await repo.createProject(A, 'Randări simultane', 'demo'), dir = mkdtempSync(path.join(tmpdir(), 'casa3d-rc-'));
  process.env.CASA3D_RENDER_DIR = path.join(dir, 'out');
  const fake = path.join(dir, 'blender.mjs'); writeFileSync(fake, 'setTimeout(() => process.exit(0), 300);\n'); process.env.CASA3D_BLENDER = fake;
  const r = await Promise.allSettled([startRender(A, id, scene, {}), startRender(A, id, scene, {})]);
  assert.equal(r.filter(x => x.status === 'fulfilled').length, 1);
  assert.equal((r.find(x => x.status === 'rejected') as PromiseRejectedResult).reason.status, 409);
  for (let i = 0; i < 40 && (await listRenders(A, id)).jobs.some(j => j.status === 'running'); i++) await wait(100);
});

test('camerele: un id care începe cu „-” (ex. --export) nu ajunge în argumentele Blender', async () => {
  const id = await repo.createProject(A, 'Randare camere', 'demo'), dir = mkdtempSync(path.join(tmpdir(), 'casa3d-rr-'));
  process.env.CASA3D_RENDER_DIR = path.join(dir, 'out');
  // „Blender” de test: scrie argumentele primite în <out>/args.json
  const fake = path.join(dir, 'blender.mjs'); writeFileSync(fake, "import { writeFileSync } from 'node:fs'; import path from 'node:path';\nconst a = process.argv.slice(2); writeFileSync(path.join(a[a.indexOf('--') + 2], 'args.json'), JSON.stringify(a));\n"); process.env.CASA3D_BLENDER = fake;
  for (const [rooms, expected] of [['--export', 'all'], ['living,-x', 'all'], ['living,baie-2', 'living,baie-2']] as const) {
    const job = await startRender(A, id, scene, { rooms });
    const out = path.join(process.env.CASA3D_RENDER_DIR!, id, job.job, 'args.json');
    for (let i = 0; i < 40 && (await listRenders(A, id)).jobs[0]!.status === 'running'; i++) await wait(100);
    const a: string[] = JSON.parse(readFileSync(out, 'utf8'));
    assert.equal(a[a.indexOf('--rooms') + 1], expected);
    await wait(5);   // job-urile se numesc după milisecundă
  }
});
