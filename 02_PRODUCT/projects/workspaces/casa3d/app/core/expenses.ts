// Cheltuielile reale ale proiectului (facturi, bonuri, plăți către meseriași), pe aceleași categorii ca devizul, ca firma
// și clientul să vadă din timp unde se depășește. Sumele sunt cele plătite, cu TVA (ca prețurile din deviz). Funcții pure.
export const EXPENSE_CATEGORIES = ['furniture', 'finishes', 'lighting', 'appliances', 'sanitary', 'textiles', 'labor', 'other'] as const;
export type ExpenseCategory = typeof EXPENSE_CATEGORIES[number];
export interface Expense { id: string; projectId: string; date: string; category: ExpenseCategory; amount: number; supplier: string; doc: string; note: string; createdAt: string }
export class ExpenseError extends Error {}
export const EXPENSE_LIMITS = { maxAmount: 10_000_000, supplierMax: 120, docMax: 60, noteMax: 300, maxPerProject: 5000 } as const;

const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const validDate = (v: string) => DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max) : '';
const r2 = (v: number) => Math.round(v * 100) / 100;

/** O cheltuială: data, categoria, suma (lei, cu TVA, > 0), furnizorul, documentul (factură/bon), nota. */
export function checkExpense(x: unknown): Pick<Expense, 'date' | 'category' | 'amount' | 'supplier' | 'doc' | 'note'> {
  const o = x as any; if (!o || typeof o !== 'object') throw new ExpenseError('Cheltuiala nu e validă.');
  if (typeof o.date !== 'string' || !validDate(o.date)) throw new ExpenseError('Data nu e validă (AAAA-LL-ZZ).');
  if (!(EXPENSE_CATEGORIES as readonly string[]).includes(o.category)) throw new ExpenseError('Categorie necunoscută.');
  if (typeof o.amount !== 'number' || !Number.isFinite(o.amount) || o.amount <= 0 || o.amount > EXPENSE_LIMITS.maxAmount) throw new ExpenseError('Suma trebuie să fie pozitivă (cel mult 10 milioane).');
  return { date: o.date, category: o.category, amount: r2(o.amount), supplier: clean(o.supplier, EXPENSE_LIMITS.supplierMax), doc: clean(o.doc, EXPENSE_LIMITS.docMax), note: clean(o.note, EXPENSE_LIMITS.noteMax) };
}

export interface ExpenseRow { category: ExpenseCategory; estimated: number | null; actual: number; diff: number | null; pct: number | null; over: boolean }
/** Real față de deviz, pe categorii. `estimated` null = devizul nu e cunoscut (doar totalurile reale). O categorie cu estimare 0
 *  și cheltuieli e o depășire fără procent (nu se împarte la zero). Categoriile fără estimare și fără cheltuieli nu apar. */
export function expenseSummary(entries: Pick<Expense, 'category' | 'amount'>[], estimated: Record<ExpenseCategory, number> | null) {
  const actualBy = new Map<ExpenseCategory, number>(); for (const e of entries) actualBy.set(e.category, r2((actualBy.get(e.category) ?? 0) + e.amount));
  const rows: ExpenseRow[] = [];
  for (const c of EXPENSE_CATEGORIES) {
    const actual = actualBy.get(c) ?? 0, est = estimated ? r2(estimated[c] ?? 0) : null;
    if (!actual && !est) continue;
    const diff = est == null ? null : r2(actual - est);
    rows.push({ category: c, estimated: est, actual, diff, pct: est ? Math.round(actual / est * 100) : null, over: est != null && actual > est + 0.005 });
  }
  const actual = r2(rows.reduce((a, r) => a + r.actual, 0)), est = estimated ? r2(EXPENSE_CATEGORIES.reduce((a, c) => a + (estimated[c] ?? 0), 0)) : null;
  return { rows, actual, estimated: est, diff: est == null ? null : r2(actual - est), count: entries.length, overCategories: rows.filter(r => r.over).map(r => r.category) };
}
export type ExpenseSummary = ReturnType<typeof expenseSummary>;
