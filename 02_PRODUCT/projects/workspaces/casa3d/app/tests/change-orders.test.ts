// Modificările cu preț: designerul scrie, trimite; clientul decide pe link o singură dată; ciornele nu ajung la client;
// bugetul actual = oferta + aprobatele.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare } from '../lib/share';
import { listChanges, addChange, updateChange, deleteChange, listChangesForShare, decideChange } from '../lib/change-orders';
import { budgetWithChanges, checkChange, CHANGE_LIMITS } from '../core/change-orders';
beforeAll(() => resetDbForTests());
const A = 'cccccccc-1111-4111-8111-cccccccccccc', B = 'dddddddd-1111-4111-8111-dddddddddddd';
async function setup(){ const id = await repo.createProject(A, 'Modificări', 'demo'), { number } = await repo.createRevision(A, id, 'v1'), { token } = await createShare(A, id, number); return { id, token }; }

test('ciornă → trimisă → decisă de client; clientul nu vede ciornele; nimeni nu decide de două ori', async () => {
  const { id, token } = await setup();
  const c1 = await addChange(A, id, { title: ' Gresie mare în baie ', body: '60×120 în loc de 30×60', amount: 1850.456, days: 5 });
  const c2 = await addChange(A, id, { title: 'Fără corp suspendat', amount: -900 });
  assert.equal(c1.number, 1); assert.equal(c2.number, 2); assert.equal(c1.title, 'Gresie mare în baie'); assert.equal(c1.amount, 1850.46); assert.equal(c1.status, 'draft');
  assert.deepEqual(await listChangesForShare(token), [], 'ciornele nu ajung la client');
  await assert.rejects(decideChange(token, c1.id, { decision: 'approved', author: 'Ana', seen: new Date().toISOString() }), (e: any) => e.status === 404, 'o ciornă nu se poate decide');
  await updateChange(A, id, c1.id, { amount: 2000 }); const sent = await updateChange(A, id, c1.id, { action: 'send' });
  assert.equal(sent.status, 'sent'); assert.ok(sent.sentAt);
  await assert.rejects(updateChange(A, id, c1.id, { amount: 1 }), (e: any) => e.status === 409, 'trimisă = nu se mai editează');
  await assert.rejects(deleteChange(A, id, c1.id), (e: any) => e.status === 409);
  assert.deepEqual((await listChangesForShare(token)).map(c => c.number), [1]);
  // două decizii simultane: doar una câștigă
  const rs = await Promise.allSettled([decideChange(token, c1.id, { decision: 'approved', author: 'Ana', seen: sent.sentAt }), decideChange(token, c1.id, { decision: 'rejected', author: 'Ion', seen: sent.sentAt })]);
  assert.equal(rs.filter(r => r.status === 'fulfilled').length, 1); assert.ok(rs.some(r => r.status === 'rejected' && (r.reason as any).status === 409));
  await assert.rejects(updateChange(A, id, c1.id, { action: 'withdraw' }), (e: any) => e.status === 409, 'o decizie nu se retrage');
  await updateChange(A, id, c2.id, { action: 'send' }); await updateChange(A, id, c2.id, { action: 'withdraw' });
  assert.equal((await listChanges(A, id)).find(c => c.id === c2.id)!.status, 'draft'); await deleteChange(A, id, c2.id);
  assert.equal((await listChanges(A, id)).length, 1);
});

test('alți proprietari, linkuri revocate, alte proiecte și intrări greșite sunt refuzate', async () => {
  const { id, token } = await setup(), other = await setup();
  const c = await addChange(A, id, { title: 'Prize în plus', amount: 300 }), seen = (await updateChange(A, id, c.id, { action: 'send' })).sentAt;
  await assert.rejects(listChanges(B, id)); await assert.rejects(addChange(B, id, { title: 'x', amount: 1 })); await assert.rejects(updateChange(B, id, c.id, { action: 'withdraw' }));
  await assert.rejects(decideChange(other.token, c.id, { decision: 'approved', author: 'Ana', seen }), (e: any) => e.status === 404, 'linkul altui proiect');
  for (const bad of [{ decision: 'maybe', author: 'Ana', seen }, { decision: 'approved', author: '', seen }, { decision: 'approved', author: 'A'.repeat(61), seen }, { decision: 'rejected', author: 'Ana', note: 'x'.repeat(1001), seen }, null])
    await assert.rejects(decideChange(token, c.id, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  for (const bad of [{ title: '', amount: 1 }, { title: 'x', amount: '100' }, { title: 'x', amount: 1e7 }, { title: 'x', amount: NaN }, { title: 'x', amount: 1, days: 1.5 }, { title: 'x'.repeat(161), amount: 1 }])
    await assert.rejects(addChange(A, id, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  await assert.rejects(decideChange(token, 'nu-e-uuid', { decision: 'approved', author: 'Ana', seen }), (e: any) => e.status === 404);
  await revokeShare(A, id, token); await assert.rejects(listChangesForShare(token), (e: any) => e.status === 404);
  await assert.rejects(decideChange(token, c.id, { decision: 'approved', author: 'Ana', seen }), (e: any) => e.status === 404);
  assert.equal(checkChange({ title: 'A‮B', amount: 1 }).title, 'AB'); assert.ok(CHANGE_LIMITS.perProject >= 50);
});

test('bugetul: oferta + aprobatele; trimisele nedecise separat', () => {
  const b = budgetWithChanges(100000, [{ amount: 2000, days: 5, status: 'approved' }, { amount: -900, days: 0, status: 'approved' }, { amount: 500, days: 2, status: 'sent' }, { amount: 7000, days: 9, status: 'rejected' }, { amount: 50, days: 0, status: 'draft' }]);
  assert.deepEqual(b, { offered: 100000, approved: 1100, pending: 500, current: 101100, ifAllApproved: 101600, days: 5, pendingCount: 1 });
});

test('aprobarea e legată de varianta văzută: după retragere și retrimitere, o pagină veche nu mai poate aproba', async () => {
  const { id, token } = await setup();
  const c = await addChange(A, id, { title: 'Prize în plus', amount: 1000 }), v1 = await updateChange(A, id, c.id, { action: 'send' });
  await updateChange(A, id, c.id, { action: 'withdraw' }); await updateChange(A, id, c.id, { amount: 5000 }); const v2 = await updateChange(A, id, c.id, { action: 'send' });
  assert.notEqual(v1.sentAt, v2.sentAt);
  await assert.rejects(decideChange(token, c.id, { decision: 'approved', author: 'Ana', seen: v1.sentAt }), (e: any) => e.status === 409, 'clientul a văzut +1000, nu +5000');
  await assert.rejects(decideChange(token, c.id, { decision: 'approved', author: 'Ana' }), (e: any) => e.status === 400, 'fără versiunea văzută nu se decide');
  const ok = await decideChange(token, c.id, { decision: 'approved', author: 'Ana', seen: v2.sentAt }); assert.equal(ok.status, 'approved'); assert.equal(ok.amount, 5000);
});
