// Dosarul de predare: doar ce s-a livrat sau montat, cu garanția (de pe linie sau implicită) până la o dată calculată,
// codurile finisajelor, îngrijire potrivită produselor, restul ca „rămas de făcut”; doar proprietarul îl vede.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getOrders, setOrderState } from '../lib/procurement';
import { addSnag } from '../lib/site';
import { getHandover } from '../lib/handover';
import { careKind, defaultWarrantyMonths } from '../core/handover';
beforeAll(() => resetDbForTests());
const A = 'eeeeeeee-1111-4111-8111-eeeeeeeeeeee', B = 'ffffffff-1111-4111-8111-ffffffffffff';

test('livrate și montate cu garanții; restul rămas de făcut; coduri de finisaje; remedieri deschise', async () => {
  const id = await repo.createProject(A, 'Predare', 'demo'), o = await getOrders(A, id), furn = o.rows.filter(r => r.kind === 'furniture'), fin = o.rows.find(r => /adeziv/i.test(r.product))!;
  await setOrderState(A, id, furn[0]!.key, { status: 'installed', deliveredAt: '2026-03-31', ref: 'IK-9', warrantyMonths: 120 });
  await setOrderState(A, id, furn[1]!.key, { status: 'delivered', deliveredAt: '2026-10-01' });
  await setOrderState(A, id, fin.key, { status: 'delivered', deliveredAt: '2026-10-01' });
  await setOrderState(A, id, furn[2]!.key, { status: 'cancelled' });
  await addSnag(A, id, { title: 'Ușa de la baie freacă', trade: 'carpentry' });
  const h = await getHandover(A, id);
  assert.equal(h.items.length, 3);
  const a = h.items.find(i => i.product === furn[0]!.product)!; assert.equal(a.ref, 'IK-9'); assert.equal(a.warrantyUntil, '2036-03-31'); assert.equal(a.status, 'installed');
  assert.equal(h.items.find(i => i.product === furn[1]!.product)!.warrantyUntil, '2028-10-01', 'implicit 24 de luni');
  assert.equal(h.items.find(i => i.product === fin.product)!.warrantyMonths, null, 'consumabilele nu au garanție afișată');
  assert.equal(h.notDelivered.length, o.rows.length - 4, 'anulatele nu apar nicăieri');
  assert.ok(h.finishes.length > 0 && h.finishes.some(f => f.element === 'paint' || f.element === 'floor'));
  assert.deepEqual(h.openSnags.map(s => s.title), ['Ușa de la baie freacă']); assert.ok(h.care.length > 0 && h.items.every(i => i.care == null || h.care.includes(i.care)), 'îngrijire doar pentru ce s-a montat');
  await assert.rejects(getHandover(B, id));
});

test('îngrijirea și garanția implicită după tipul produsului', () => {
  assert.equal(careKind('Parchet laminat 8 mm Egger', 'finish'), 'laminate'); assert.equal(careKind('Gresie portelanată 60x60', 'finish'), 'tile');
  assert.equal(careKind('Adeziv flexibil gresie/faianță', 'finish'), null); assert.equal(careKind('Vopsea lavabilă albă', 'finish'), 'paint');
  assert.equal(careKind('KIVIK canapea 3 locuri', 'furniture'), 'furniture'); assert.equal(careKind('Set rezervor încastrat + vas WC', 'furniture'), 'sanitary');
  assert.equal(defaultWarrantyMonths('Chit de rosturi', 'finish'), null); assert.equal(defaultWarrantyMonths('Masă', 'furniture'), 24);
});
