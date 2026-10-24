// Coșul de proiecte: ștergerea mută în coș (linkurile clientului se opresc imediat), restaurarea readuce proiectul,
// ștergerea definitivă cere numele și șterge randările de pe disc, după 30 de zile coșul se golește singur; duplicarea nu copiază clientul.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const RD = mkdtempSync(path.join(os.tmpdir(), 'c3d-trash-')); process.env.CASA3D_RENDER_DIR = RD;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, resolveShare } from '../lib/share';
import { addTask, listTasks } from '../lib/tasks';
import { moveToTrash, restoreFromTrash, purgeProject, purgeExpired, listTrash, duplicateProject, TRASH_DAYS } from '../lib/trash';
beforeAll(() => resetDbForTests());
const A = '11111111-aaaa-4aaa-8aaa-111111111111', B = '22222222-bbbb-4bbb-8bbb-222222222222';
const fakeRender = (id: string) => { const d = path.join(RD, id, '1791700000000'); mkdirSync(d, { recursive: true }); writeFileSync(path.join(d, 'living.png'), 'x'); return path.join(RD, id); };

test('ștergerea mută în coș: proiectul dispare din listă, nu se deschide, linkul clientului se oprește; restaurarea îl readuce', async () => {
  const id = await repo.createProject(A, 'Apartament Popescu', 'demo'); await repo.createRevision(A, id, 'v1');
  const share = await createShare(A, id, 1); await addTask(A, { title: 'Sună clientul', projectId: id });
  assert.ok((await resolveShare(share.token)).snapshot);
  const r = await moveToTrash(A, id); assert.ok(r.ok && Date.parse(r.purgeAt) - Date.now() > (TRASH_DAYS - 1) * 864e5);
  assert.ok(!(await repo.listProjects(A)).some(p => p.id === id));
  await assert.rejects(repo.getProject(A, id), (e: any) => e.status === 404);
  await assert.rejects(resolveShare(share.token), (e: any) => e.status === 404 || e.status === 410, 'linkul clientului nu mai merge');
  assert.ok(!(await listTasks(A)).some(t => t.projectId === id), 'sarcinile proiectului din coș nu apar');
  assert.deepEqual((await listTrash(A)).map(x => x.name), ['Apartament Popescu']);
  await assert.rejects(moveToTrash(B, id), (e: any) => e.status === 404, 'alt proprietar nu-l poate atinge');
  await assert.rejects(restoreFromTrash(B, id), (e: any) => e.status === 404);
  await restoreFromTrash(A, id); assert.equal((await repo.getProject(A, id)).name, 'Apartament Popescu'); assert.equal((await listTrash(A)).length, 0);
  // linkul vechi rămâne oprit după restaurare: designerul îl retrimite conștient
  await assert.rejects(resolveShare(share.token));
});

test('ștergerea definitivă: doar din coș, doar cu numele exact, și ia randările de pe disc', async () => {
  const id = await repo.createProject(A, 'Casa Ionescu', 'demo'), dir = fakeRender(id);
  await assert.rejects(purgeProject(A, id, 'Casa Ionescu'), (e: any) => e.status === 404, 'nu direct, întâi în coș');
  await moveToTrash(A, id);
  await assert.rejects(purgeProject(A, id, 'casa'), (e: any) => e.status === 400);
  await assert.rejects(purgeProject(B, id, 'Casa Ionescu'), (e: any) => e.status === 404);
  assert.ok(existsSync(dir)); await purgeProject(A, id, ' Casa Ionescu '); assert.ok(!existsSync(dir), 'randările au fost șterse');
  const { q } = await getDb(); assert.equal((await q('select count(*)::int n from projects where id=$1', [id])).rows[0].n, 0);
  await assert.rejects(restoreFromTrash(A, id), (e: any) => e.status === 404);
});

test('după 30 de zile coșul se golește singur, cu randări cu tot; ce e mai nou rămâne', async () => {
  const old = await repo.createProject(A, 'Vechi', 'demo'), fresh = await repo.createProject(A, 'Nou', 'demo'), dir = fakeRender(old);
  await moveToTrash(A, old); await moveToTrash(A, fresh);
  const { q } = await getDb(); await q(`update projects set deleted_at = now() - interval '31 days' where id=$1`, [old]);
  assert.equal(await purgeExpired(A), 1); assert.ok(!existsSync(dir));
  assert.deepEqual((await listTrash(A)).map(x => x.name), ['Nou']);
});

test('duplicarea: planul și mobilierul da, clientul, linkurile și reviziile nu; alt proprietar nu poate duplica', async () => {
  const id = await repo.createProject(A, 'Garsonieră', 'demo');
  await repo.saveMeta(A, id, { status: 'design', client: { name: 'Maria Pop', phone: '0722 000 111' }, fee: { kind: 'fixed', amount: 5000 } });
  await repo.createRevision(A, id, 'v1'); await createShare(A, id, 1);
  const { id: copy } = await duplicateProject(A, id); const p = await repo.getProject(A, copy), src = await repo.getProject(A, id);
  assert.equal(p.name, 'Garsonieră (copie)'); assert.equal(p.draft.placements.length, src.draft.placements.length); assert.equal(p.currentRevision, 0);
  assert.ok(!p.meta.client && !p.meta.fee, 'datele clientului nu se copiază');
  assert.equal((await duplicateProject(A, id, 'Varianta B')).id.length, 36);
  await assert.rejects(duplicateProject(B, id), (e: any) => e.status === 404);
});
