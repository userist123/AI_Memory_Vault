// Cheltuielile reale ale proiectului (doar proprietarul): înregistrări și sumarul pe categorii. Comparația cu devizul se face
// în client, cu același deviz pe care îl vede utilizatorul (components/BudgetPanel.tsx estimateOf).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { checkExpense, ExpenseError, EXPENSE_LIMITS, type Expense } from '../core/expenses';

const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const day = (d: any) => typeof d === 'string' ? d.slice(0, 10) : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
const row = (r: any): Expense => ({ id: r.id, projectId: r.project_id, date: day(r.date), category: r.category, amount: Number(r.amount), supplier: r.supplier, doc: r.doc, note: r.note, createdAt: new Date(r.created_at).toISOString() });
export async function getExpenses(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb();
  return { entries: (await q('select * from expenses where project_id=$1 order by date desc, created_at desc', [id])).rows.map(row) };
}
export async function addExpense(owner: string, id: string, input: unknown): Promise<Expense> {
  await getProject(owner, id); let x; try { x = checkExpense(input); } catch (e: any){ if (e instanceof ExpenseError) throw new HttpError(400, e.message); throw e; }
  const { tx } = await getDb();
  return tx(async q => { await q('select id from projects where id=$1 for update', [id]);
    const { rows: [c] } = await q('select count(*)::int as n from expenses where project_id=$1', [id]); if (c.n >= EXPENSE_LIMITS.maxPerProject) throw new HttpError(429, 'Prea multe cheltuieli.');
    const { rows } = await q('insert into expenses(project_id, date, category, amount, supplier, doc, note) values($1,$2,$3,$4,$5,$6,$7) returning *', [id, x.date, x.category, x.amount, x.supplier, x.doc, x.note]); return row(rows[0]); });
}
export async function deleteExpense(owner: string, id: string, entry: unknown){
  await getProject(owner, id); if (typeof entry !== 'string' || !ID_RE.test(entry)) throw new HttpError(404, 'Cheltuială inexistentă.');
  const { q } = await getDb(), { rows } = await q('delete from expenses where id=$1 and project_id=$2 returning id', [entry, id]);
  if (!rows[0]) throw new HttpError(404, 'Cheltuială inexistentă.'); return { ok: true };
}
