// Fișa proiectului: client, etapă, termene, onorariu — validată, curățată, în afara planului (nu ajunge la client).
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { checkMeta, readMeta, feeEstimate, daysLeft } from '../core/project-meta';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { clientLink, resolveShare } from '../lib/share';
beforeAll(() => resetDbForTests());
const A = '88888888-8888-4888-8888-888888888888', B = '99999999-9999-4999-8999-999999999999';

test('validare: câmpuri curățate, goale eliminate, valori greșite refuzate', () => {
  const m = checkMeta({ status: 'offer', client: { name: '  Ana Pop‮ ', phone: '+40 722 123 456', email: 'Ana@Exemplu.RO', address: '', notes: 'vrea lemn' }, dueDate: '2026-12-15', fee: { kind: 'per_m2', amount: 45.555 } });
  assert.deepEqual(m, { status: 'offer', client: { name: 'Ana Pop', phone: '+40 722 123 456', email: 'ana@exemplu.ro', notes: 'vrea lemn' }, dueDate: '2026-12-15', fee: { kind: 'per_m2', amount: 45.56 } });
  assert.deepEqual(checkMeta({}), { status: 'design' });
  for (const bad of [{ status: 'x' }, { client: { email: 'nu-e-mail' } }, { client: { phone: 'sună-mă' } }, { dueDate: '2026-02-30' }, { dueDate: '15.12.2026' },
    { startDate: '2026-12-20', dueDate: '2026-12-01' }, { fee: { kind: 'fixed', amount: -1 } }, { fee: { kind: 'lunar', amount: 5 } }, null, [], 'x'])
    assert.throws(() => checkMeta(bad), JSON.stringify(bad));
  assert.deepEqual(readMeta({ status: 'stricat' }), { status: 'design' }, 'citirea din bază nu aruncă');
});
test('onorariu estimat și zile până la termen', () => {
  assert.equal(feeEstimate(checkMeta({ fee: { kind: 'per_m2', amount: 50 } }), 61.6), 3080);
  assert.equal(feeEstimate(checkMeta({ fee: { kind: 'fixed', amount: 9000 } }), 61.6), 9000);
  assert.equal(feeEstimate(checkMeta({ fee: { kind: 'hourly', amount: 150 } }), 61.6), null);
  assert.equal(daysLeft(checkMeta({ dueDate: '2026-10-21' }), '2026-10-11'), 10);
  assert.equal(daysLeft(checkMeta({ dueDate: '2026-10-01' }), '2026-10-11'), -10);
  assert.equal(daysLeft(checkMeta({ dueDate: '2026-10-01', status: 'done' }), '2026-10-11'), null);
});
test('se salvează lângă proiect, apare în listă, doar pentru proprietar, și nu ajunge pe linkul clientului', async () => {
  const id = await repo.createProject(A, 'Casa Pop', 'demo');
  assert.deepEqual((await repo.getProject(A, id)).meta, { status: 'design' });
  await repo.saveMeta(A, id, { status: 'execution', client: { name: 'Ana Pop', phone: '0722 123 456' }, dueDate: '2026-12-15' });
  const p = await repo.getProject(A, id); assert.equal(p.meta.status, 'execution'); assert.equal(p.meta.client?.name, 'Ana Pop');
  const row = (await repo.listProjects(A)).find((r: any) => r.id === id)!; assert.equal(row.status, 'execution'); assert.equal(row.client, 'Ana Pop'); assert.equal(row.dueDate, '2026-12-15');
  await assert.rejects(repo.saveMeta(B, id, { status: 'done' })); await assert.rejects(repo.saveMeta(A, id, { status: 'nu' }), (e: any) => e.status === 400);
  const link = await clientLink(A, id), shared = await resolveShare(link.token);
  assert.ok(!JSON.stringify(shared).includes('0722 123 456'), 'telefonul clientului nu e pe linkul public');   // numărul întreg: '0722' singur poate apărea întâmplător într-un id
  assert.ok(!('meta' in (shared.snapshot as any)));
});
