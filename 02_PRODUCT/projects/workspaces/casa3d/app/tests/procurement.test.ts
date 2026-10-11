// Comenzile: un produs = o linie (cu camerele lui), grupate pe furnizor; starea se ține pe server; livrarea întârziată se vede.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getOrders, setOrderState } from '../lib/procurement';
import { procurementRows, checkOrderState, bySupplier, procurementSummary, type ProcurementLine } from '../core/procurement';
beforeAll(() => resetDbForTests());
const A = 'aaaaaaaa-1111-4111-8111-111111111111', B = 'bbbbbbbb-2222-4222-8222-222222222222';

test('liniile proiectului demo: mobilier și finisaje, fără dubluri, grupate pe furnizor', async () => {
  const id = await repo.createProject(A, 'Comenzi', 'demo'), o = await getOrders(A, id, '2026-10-11');
  assert.ok(o.rows.length > 5); assert.equal(new Set(o.rows.map(r => r.key)).size, o.rows.length, 'chei unice');
  assert.ok(o.rows.some(r => r.kind === 'furniture') && o.rows.some(r => r.kind === 'finish'));
  assert.ok(o.rows.every(r => r.state.status === 'todo'));
  const p = await repo.getProject(A, id); assert.equal(o.rows.filter(r => r.kind === 'furniture').reduce((a, r) => a + r.qty, 0), p.draft.placements.length, 'fiecare piesă e numărată o dată');
  assert.deepEqual(o.groups.map(g => g.supplier), [...new Set(o.rows.map(r => r.supplier))]);
  assert.equal(o.summary.todo, o.rows.length);
});
test('starea comenzii: salvată, validată, doar de proprietar; întârzierea se calculează', async () => {
  const id = await repo.createProject(A, 'Stare', 'demo'), key = (await getOrders(A, id)).rows[0]!.key;
  await setOrderState(A, id, key, { status: 'ordered', ref: 'CMD-123', orderedAt: '2026-10-01', eta: '2026-10-08' });
  const o = await getOrders(A, id, '2026-10-11'), r = o.rows.find(x => x.key === key)!;
  assert.equal(r.state.ref, 'CMD-123'); assert.equal(r.overdue, true); assert.equal(o.summary.overdue, 1); assert.equal(o.summary.ordered, 1);
  assert.equal((await getOrders(A, id, '2026-10-05')).rows.find(x => x.key === key)!.overdue, false, 'înainte de termen nu e întârziere');
  await setOrderState(A, id, key, { status: 'delivered', deliveredAt: '2026-10-10' }); assert.equal((await getOrders(A, id, '2026-10-11')).summary.overdue, 0);
  await assert.rejects(setOrderState(B, id, key, { status: 'ordered' }));
  for (const [k, s] of [[key, { status: 'pierdut' }], [key, { status: 'ordered', eta: '10.10.2026' }], ['x|nu', { status: 'ordered' }], [key, null]] as const)
    await assert.rejects(setOrderState(A, id, k, s), (e: any) => e.status === 400, JSON.stringify([k, s]));
});
test('pur: anulatele nu intră în totaluri, o linie fără preț face totalul furnizorului necunoscut', () => {
  const L = (key: string, supplier: string, total: number | null): ProcurementLine => ({ key, kind: 'finish', product: key, supplier, code: null, url: null, qty: 1, unit: 'buc', packs: null, unitPrice: total, total, rooms: [] });
  const rows = procurementRows([L('m|a', 'Dedeman', 100), L('m|b', 'Dedeman', 50), L('m|c', 'IKEA', null)], { 'm|b': { status: 'cancelled' }, 'm|a': { status: 'ordered' } }, '2026-10-11');
  const g = bySupplier(rows); assert.equal(g[0]!.total, 100); assert.equal(g[1]!.total, null);
  const s = procurementSummary(rows); assert.equal(s.lines, 2); assert.equal(s.ordersValue, 100);
  assert.deepEqual(checkOrderState({ status: 'ordered', ref: '  A1‮ ', note: '' }), { status: 'ordered', ref: 'A1' });
});

test('regresii: ambalajele se adună pe camere, datele imposibile și liniile inventate sunt refuzate', async () => {
  const { addPacks } = await import('../core/procurement');
  assert.equal(addPacks('5 × pachet', '12 × pachet'), '17 × pachet'); assert.equal(addPacks('5 × pachet', '2 × cutie'), null); assert.equal(addPacks(null, '2 × cutie'), null);
  const id = await repo.createProject(A, 'Ambalaje', 'demo'), o = await getOrders(A, id);
  const multi = o.rows.find(r => r.kind === 'finish' && r.rooms.length > 1 && r.packs);
  if (multi){ const n = Number(multi.packs!.split(' × ')[0]); assert.ok(n >= multi.rooms.length, 'cel puțin un ambalaj pe cameră'); }
  assert.throws(() => checkOrderState({ status: 'ordered', eta: '2026-02-31' }));
  await assert.rejects(setOrderState(A, id, 'm|inventat|x', { status: 'ordered' }), (e: any) => e.status === 400);
});
