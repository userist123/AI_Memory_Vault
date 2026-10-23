// Sarcinile: termen, bifare, lista „Azi” corectă, doar ale proprietarului, proiect doar al lui.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { addTask, listTasks, updateTask, deleteTask } from '../lib/tasks';
import { agenda, type Task } from '../core/tasks';
beforeAll(() => resetDbForTests());
const A = 'eeeeeeee-5555-4555-8555-555555555555', B = 'ffffffff-6666-4666-8666-666666666666';

test('agenda: întârziate, azi, săptămâna, fără dată; cele făcute lipsesc', () => {
  const T = (id: string, due: string | null, done = false): Task => ({ id, projectId: null, title: id, due, doneAt: done ? '2026-10-10T10:00:00Z' : null, createdAt: '2026-10-01T00:00:00Z' });
  const a = agenda([T('a', '2026-10-09'), T('b', '2026-10-11'), T('c', '2026-10-15'), T('d', '2026-10-30'), T('e', null), T('f', '2026-10-01', true)], '2026-10-11');
  assert.deepEqual([a.overdue, a.today, a.week, a.later, a.noDate].map(x => x.map(t => t.id)), [['a'], ['b'], ['c'], ['d'], ['e']]);
});
test('sarcini: creare, bifare, termen, proiect doar al proprietarului, izolare', async () => {
  const pid = await repo.createProject(A, 'Casa', 'demo'), other = await repo.createProject(B, 'Străin', 'demo');
  const t1 = await addTask(A, { title: '  Comandă gresia‮ ', due: '2026-10-12', projectId: pid }); assert.equal(t1.title, 'Comandă gresia'); assert.equal(t1.due, '2026-10-12');
  await addTask(A, { title: 'Sună electricianul' });
  assert.equal((await listTasks(A)).length, 2); assert.equal((await listTasks(A, pid)).length, 1); assert.equal((await listTasks(A))[0]!.projectName, 'Casa');
  const d = await updateTask(A, t1.id, { done: true }); assert.ok(d.doneAt); assert.equal((await updateTask(A, t1.id, { done: false })).doneAt, null);
  assert.equal((await updateTask(A, t1.id, { due: null })).due, null);
  await assert.rejects(addTask(A, { title: 'x', projectId: other }), 'proiectul altcuiva');
  for (const bad of [{ title: '' }, { title: 'x', due: '2026-02-30' }, { title: 'x', projectId: 'nu' }]) await assert.rejects(addTask(A, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  await assert.rejects(updateTask(B, t1.id, { done: true }), (e: any) => e.status === 404); await assert.rejects(deleteTask(B, t1.id), (e: any) => e.status === 404);
  assert.equal((await listTasks(B)).length, 0);
  await deleteTask(A, t1.id); assert.equal((await listTasks(A)).length, 1);
});

test('limita de sarcini deschise ține și la cereri simultane (review de securitate)', async () => {
  const { TASK_LIMITS } = await import('../core/tasks'); const O = '12121212-1212-4212-8212-121212121212';
  for (let i = 0; i < TASK_LIMITS.perOwner - 3; i++) await addTask(O, { title: `t${i}` });
  const rs = await Promise.allSettled(Array.from({ length: 20 }, (_, i) => addTask(O, { title: `p${i}` })));
  assert.equal(rs.filter(r => r.status === 'fulfilled').length, 3);
  assert.ok(rs.filter(r => r.status === 'rejected').every(r => (r as PromiseRejectedResult).reason.status === 429));
});
