'use client';
// Cheltuieli reale față de deviz: adaugi rapid factura sau bonul (data, categoria, suma cu TVA, furnizorul, nr. documentului),
// vezi pe categorii cât s-a cheltuit față de estimare și unde se depășește. Estimarea vine din devizul de pe ecran.
import FoldSection from './FoldSection';
import { useEffect, useMemo, useState } from 'react';
import { EXPENSE_CATEGORIES, expenseSummary, type Expense, type ExpenseCategory } from '@/core/expenses';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

const today = () => new Date().toISOString().slice(0, 10);
const BUDGET_CATS = new Set(['furniture', 'finishes', 'lighting', 'appliances', 'sanitary', 'textiles']);
export default function ExpensesPanel({ id, cur, estimate }: { id: string; cur: string; estimate: Record<ExpenseCategory, number> | null }){
  const { t, lang } = usePrefs(), money = (v: number) => formatMoney(v, cur, lang);
  const [list, setList] = useState<Expense[] | null>(null), [busy, setBusy] = useState(false), [err, setErr] = useState(''), [all, setAll] = useState(false);
  const [f, setF] = useState({ date: today(), category: 'finishes' as ExpenseCategory, amount: '', supplier: '', doc: '', note: '' });
  const load = () => fetch(`/api/projects/${id}/expenses`).then(r => r.ok ? r.json() : null).then(j => j && setList(j.entries)).catch(() => {});
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  const label = (c: ExpenseCategory) => BUDGET_CATS.has(c) ? t(`budget.cat.${c}`) : t(`exp.cat.${c}`);
  const amount = Number(f.amount.replace(/\s/g, '').replace(',', '.'));
  const sum = useMemo(() => list ? expenseSummary(list, estimate) : null, [list, estimate]);
  async function add(){ setErr(''); if (!(amount > 0)) return; setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/expenses`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ ...f, amount }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setErr(j.error || t('exp.error')); return; } setF(x => ({ ...x, amount: '', doc: '', note: '' })); load(); } finally { setBusy(false); } }
  async function del(e: Expense){ const r = await fetch(`/api/projects/${id}/expenses?entry=${e.id}`, { method: 'DELETE' }); if (r.ok) load(); else setErr(t('exp.error')); }
  const over = sum?.overCategories.length ?? 0;
  return (<FoldSection id="expenses" className="expp" title={<h3>{t('exp.title')}{sum && sum.count > 0 && <span className={`pill ${over ? 'warn' : ''}`}>{money(sum.actual)}</span>}</h3>}>
    <p className="prov">{t('exp.hint')}</p>
    {sum && sum.count > 0 && <>
      <p className="expp-total">{sum.estimated != null ? t('exp.total', { actual: money(sum.actual), est: money(sum.estimated), pct: sum.estimated ? Math.round(sum.actual / sum.estimated * 100) : 0 }) : t('exp.totalOnly', { actual: money(sum.actual) })}</p>
      {over > 0 && <p className="issue WARNING" role="status">{t('exp.over', { cats: sum.overCategories.map(label).join(', ') })}</p>}
      <table className="expp-table"><thead><tr><th>{t('exp.category')}</th><th>{t('exp.estimated')}</th><th>{t('exp.actual')}</th><th>{t('exp.diff')}</th></tr></thead>
        <tbody>{sum.rows.map(r => <tr key={r.category} className={r.over ? 'over' : ''}>
          <td>{label(r.category)}</td><td>{r.estimated != null ? money(r.estimated) : '—'}</td><td>{money(r.actual)}{r.pct != null ? <small> · {r.pct}%</small> : null}</td>
          <td>{r.diff != null ? `${r.diff > 0 ? '+' : ''}${money(r.diff)}` : '—'}</td></tr>)}</tbody></table>
    </>}
    {err && <p role="alert" className="issue ERROR">{err}</p>}
    <div className="expp-add">
      <input type="date" value={f.date} onChange={e => setF(x => ({ ...x, date: e.target.value }))} aria-label={t('exp.date')} />
      <select value={f.category} onChange={e => setF(x => ({ ...x, category: e.target.value as ExpenseCategory }))} aria-label={t('exp.category')}>{EXPENSE_CATEGORIES.map(c => <option key={c} value={c}>{label(c)}</option>)}</select>
      <input inputMode="decimal" value={f.amount} onChange={e => setF(x => ({ ...x, amount: e.target.value }))} placeholder={t('exp.amountPh', { cur })} aria-label={t('exp.amount')} className={f.amount && !(amount > 0) ? 'bad' : ''} />
      <input value={f.supplier} maxLength={120} onChange={e => setF(x => ({ ...x, supplier: e.target.value }))} placeholder={t('exp.supplier')} aria-label={t('exp.supplier')} />
      <input value={f.doc} maxLength={60} onChange={e => setF(x => ({ ...x, doc: e.target.value }))} placeholder={t('exp.doc')} aria-label={t('exp.doc')} />
      <input value={f.note} maxLength={300} onChange={e => setF(x => ({ ...x, note: e.target.value }))} placeholder={t('exp.note')} aria-label={t('exp.note')} />
      <button className="btn" disabled={busy || !(amount > 0)} onClick={add}>{t('exp.add')}</button></div>
    {list && list.length > 0 && <ul className="expp-list">{(all ? list : list.slice(0, 6)).map(e => <li key={e.id}>
      <span>{new Date(e.date + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' })} · <b>{money(e.amount)}</b> · {label(e.category)}{e.supplier ? ` · ${e.supplier}` : ''}{e.doc ? ` · ${e.doc}` : ''}{e.note ? ` — ${e.note}` : ''}</span>
      <button className="btn-x" aria-label={t('exp.delete')} onClick={() => del(e)}>×</button></li>)}</ul>}
    {list && list.length > 6 && <button className="btn link" onClick={() => setAll(x => !x)}>{all ? t('exp.less') : t('exp.more', { n: list.length - 6 })}</button>}
  </FoldSection>);
}
