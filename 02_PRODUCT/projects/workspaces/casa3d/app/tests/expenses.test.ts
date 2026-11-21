// Cheltuieli reale față de deviz (cerute de cercetarea pe piață: urmărirea costurilor reale și a depășirilor).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { checkExpense, expenseSummary, EXPENSE_CATEGORIES, ExpenseError, type Expense } from '../core/expenses';

const e = (category: Expense['category'], amount: number, date = '2026-11-20'): Expense => ({ id: 'x', projectId: 'p', date, category, amount, supplier: '', doc: '', note: '', createdAt: '' });

test('checkExpense: dată, categorie, sumă în bani (2 zecimale), texte curățate și limitate', () => {
  assert.deepEqual(checkExpense({ date: '2026-11-20', category: 'finishes', amount: 1234.567, supplier: '  Dedeman\n', doc: 'FCT 123', note: 'gresie baie' }),
    { date: '2026-11-20', category: 'finishes', amount: 1234.57, supplier: 'Dedeman', doc: 'FCT 123', note: 'gresie baie' });
  assert.deepEqual([...EXPENSE_CATEGORIES], ['furniture', 'finishes', 'lighting', 'appliances', 'sanitary', 'textiles', 'labor', 'other']);
  for (const bad of [{ date: '2026-02-30', category: 'labor', amount: 10 }, { date: '2026-11-20', category: 'mobila', amount: 10 }, { date: '2026-11-20', category: 'labor', amount: 0 },
    { date: '2026-11-20', category: 'labor', amount: -5 }, { date: '2026-11-20', category: 'labor', amount: 1e9 }, { date: '2026-11-20', category: 'labor', amount: '100' }, null])
    assert.throws(() => checkExpense(bad), ExpenseError, JSON.stringify(bad));
  assert.equal(checkExpense({ date: '2026-11-20', category: 'other', amount: 5, supplier: 'x'.repeat(500) }).supplier.length, 120);
});
test('expenseSummary: pe categorii față de deviz, diferența și procentul; depășirea marcată; categoriile fără estimare nu dau procent', () => {
  const est = { furniture: 10000, finishes: 5000, lighting: 0, appliances: 2000, sanitary: 1000, textiles: 500, labor: 8000, other: 0 };
  const s = expenseSummary([e('furniture', 6000), e('furniture', 5000), e('finishes', 2000), e('other', 300), e('labor', 4000)], est);
  const by = new Map(s.rows.map(r => [r.category, r]));
  assert.deepEqual(by.get('furniture'), { category: 'furniture', estimated: 10000, actual: 11000, diff: 1000, pct: 110, over: true });
  assert.deepEqual(by.get('finishes'), { category: 'finishes', estimated: 5000, actual: 2000, diff: -3000, pct: 40, over: false });
  assert.deepEqual(by.get('other'), { category: 'other', estimated: 0, actual: 300, diff: 300, pct: null, over: true });
  assert.equal(by.has('lighting'), false, 'categoriile fără estimare și fără cheltuieli nu apar');
  assert.equal(s.actual, 17300); assert.equal(s.estimated, 26500); assert.equal(s.diff, -9200); assert.equal(s.count, 5);
  assert.deepEqual(s.overCategories, ['furniture', 'other']);
});
test('expenseSummary: fără deviz cunoscut, doar totalurile reale', () => {
  const s = expenseSummary([e('labor', 100.1), e('labor', 200.2)], null);
  assert.equal(s.actual, 300.3); assert.equal(s.estimated, null); assert.equal(s.diff, null);
  assert.deepEqual(s.rows, [{ category: 'labor', estimated: null, actual: 300.3, diff: null, pct: null, over: false }]);
});

// ---------- server: proprietarul, validarea, ștergerea ----------
import { beforeAll } from 'vitest';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getExpenses, addExpense, deleteExpense } from '../lib/expenses';
beforeAll(() => resetDbForTests());
const OWNER = '11111111-1111-4111-8111-111111111111', OTHER = '22222222-2222-4222-8222-222222222222';
test('server: cheltuielile se adaugă, se listează și se șterg doar de proprietar; datele invalide dau 400', async () => {
  const id = await repo.createProject(OWNER, 'Cheltuieli', 'blank', 'ro');
  const a = await addExpense(OWNER, id, { date: '2026-11-20', category: 'labor', amount: 1500.5, supplier: 'Echipa', doc: 'CH 7' });
  assert.equal(a.amount, 1500.5); assert.equal(a.category, 'labor');
  assert.deepEqual((await getExpenses(OWNER, id)).entries.map(e => e.id), [a.id]);
  await assert.rejects(() => getExpenses(OTHER, id), (e: any) => e.status === 404);
  await assert.rejects(() => addExpense(OTHER, id, { date: '2026-11-20', category: 'labor', amount: 10 }), (e: any) => e.status === 404);
  await assert.rejects(() => deleteExpense(OTHER, id, a.id), (e: any) => e.status === 404);
  await assert.rejects(() => addExpense(OWNER, id, { date: '2026-11-20', category: 'labor', amount: -1 }), (e: any) => e.status === 400);
  assert.deepEqual(await deleteExpense(OWNER, id, a.id), { ok: true });
  assert.equal((await getExpenses(OWNER, id)).entries.length, 0);
  await assert.rejects(() => deleteExpense(OWNER, id, 'nu-e-uuid'), (e: any) => e.status === 404);
});
