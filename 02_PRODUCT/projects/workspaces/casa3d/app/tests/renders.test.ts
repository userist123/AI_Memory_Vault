// Randările locale: pornesc doar cu Blender configurat, un job odată, fișierele se citesc doar cu nume valide și doar de proprietar.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
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
  const fake = path.join(dir, 'blender.mjs'); writeFileSync(fake, "import { writeFileSync } from 'node:fs'; import path from 'node:path';\nconst a = process.argv.slice(2); setTimeout(() => writeFileSync(path.join(a[a.indexOf('--') + 2], 'living.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47])), 300);\n"); process.env.CASA3D_BLENDER = fake;
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
