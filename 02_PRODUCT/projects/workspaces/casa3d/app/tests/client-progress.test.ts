// Stadiul comenzilor pe linkul clientului: doar când designerul îl activează; clientul vede starea și datele de
// livrare, niciodată furnizorul, prețul de achiziție, nr. comenzii sau notele interne.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare } from '../lib/share';
import { getOrders, setOrderState } from '../lib/procurement';
import { progressForShare } from '../lib/client-progress';
import { clientProgress } from '../core/client-progress';
import { checkMeta } from '../core/project-meta';
beforeAll(() => resetDbForTests());
const A = 'aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa';

test('oprit implicit; pornit arată doar starea și datele, fără date interne', async () => {
  const id = await repo.createProject(A, 'Livrări', 'demo'), { number } = await repo.createRevision(A, id, 'v1'), { token } = await createShare(A, id, number);
  assert.equal(await progressForShare(token), null, 'implicit clientul nu vede comenzile');
  const o = await getOrders(A, id), [a, b, c] = o.rows;
  await setOrderState(A, id, a!.key, { status: 'ordered', ref: 'SECRET-REF-77', eta: '2026-10-01', orderedAt: '2026-09-20', note: 'adaos 30% nota internă' });
  await setOrderState(A, id, b!.key, { status: 'delivered', deliveredAt: '2026-10-05' });
  await setOrderState(A, id, c!.key, { status: 'cancelled' });
  await repo.saveMeta(A, id, { status: 'execution', shareOrders: true });
  const p = (await progressForShare(token, '2026-10-11'))!;
  assert.ok(p); assert.equal(p.items.length, o.rows.length - 1, 'anulatele nu apar');
  assert.equal(p.items[0]!.product, a!.product); assert.equal(p.items[0]!.late, true, 'întârziata e prima');
  assert.equal(p.late, 1); assert.equal(p.counts.delivered, 1); assert.ok(p.updatedAt);
  const json = JSON.stringify(p);
  assert.ok(!json.includes('SECRET-REF-77') && !json.includes('adaos 30%'), 'nr. comenzii și notele rămân la designer');
  for (const k of ['supplier', 'unitPrice', 'total', 'url', 'ref', 'note', 'code']) assert.ok(p.items.every(i => !(k in i)), k);
  await revokeShare(A, id, token); assert.equal(await progressForShare(token), null, 'link revocat');
  assert.equal(await progressForShare('x'.repeat(43)), null); assert.equal(await progressForShare('../etc'), null);
});

test('ordinea și numărătorile; opțiunea din fișă se validează', () => {
  const row = (product: string, status: string, extra: any = {}) => ({ key: product, kind: 'finish', product, supplier: 'S', code: null, url: null, qty: 1, unit: 'buc', packs: null, unitPrice: 1, total: 1, rooms: ['Hol'], state: { status, ...extra }, overdue: !!extra.late }) as any;
  const p = clientProgress([row('Montat', 'installed', { deliveredAt: '2026-09-01' }), row('Ofertă', 'quoted'), row('B', 'ordered', { eta: '2026-11-02' }), row('A', 'ordered', { eta: '2026-10-20' })], null);
  assert.deepEqual(p.items.map(i => i.product), ['A', 'B', 'Ofertă', 'Montat']);
  assert.deepEqual(p.counts, { todo: 1, ordered: 2, delivered: 0, installed: 1 });
  assert.equal(p.items[2]!.eta, null, 'o dată de livrare apare doar pe ce e comandat');
  assert.equal(checkMeta({ shareOrders: true }).shareOrders, true); assert.equal(checkMeta({ shareOrders: false }).shareOrders, undefined);
  assert.throws(() => checkMeta({ shareOrders: 'da' }));
});

test('numele produsului nu se repetă când varianta îl conține deja', async () => {
  const { productLabel } = await import('../core/procurement');
  assert.equal(productLabel('BESTĂ comodă', 'BESTĂ comodă TV cu uși, alb'), 'BESTĂ comodă TV cu uși, alb');
  assert.equal(productLabel('Canapea Kivik', '3 locuri, bej'), 'Canapea Kivik · 3 locuri, bej');
  assert.equal(productLabel('Masă', ''), 'Masă');
});

test('clientul vede până când trebuie decis ce nu e încă comandat', () => {
  const row = (product: string, status: string, orderBy: string | null, orderUrgency: any) => ({ key: product, kind: 'finish', product, supplier: 'S', code: null, url: null, qty: 1, unit: 'buc', packs: null, unitPrice: 1, total: 1, rooms: [], state: { status }, overdue: false, leadDays: 21, orderBy, orderUrgency }) as any;
  const p = clientProgress([row('Canapea', 'todo', '2026-10-09', 'late'), row('Masă', 'todo', '2026-11-10', null), row('Lampă', 'ordered', '2026-10-01', null)], null);
  assert.equal(p.decisionsDue, 1); const c = p.items.find(i => i.product === 'Canapea')!; assert.equal(c.decideBy, '2026-10-09'); assert.equal(c.decideUrgent, true); assert.equal(c.decideLate, true);
  assert.equal(p.items.find(i => i.product === 'Lampă')!.decideBy, null, 'ce e comandat nu mai cere decizie');
});
