// Etichetele chestionarului clientului (întrebări, opțiuni, răspunsuri), comune formularului public și panoului designerului.
import { budgetBounds, UNSURE, QUESTIONS, type Question, type BudgetRange, type ClientBrief } from '../core/client-brief';
import { formatMoney } from '../core/format';
import type { Lang, Translate } from './i18n';

export const questionLabel = (t: Translate, key: string) => t(`cbrief.q.${key}`);
/** Eticheta unei opțiuni. Intervalele de buget arată sumele în moneda proiectului; stilurile și prioritățile folosesc numele din brief-ul designerului. */
export function optionLabel(t: Translate, lang: Lang, key: string, opt: string, cur: string): string {
  if (opt === UNSURE) return t('cbrief.unsure');
  if (key === 'styles') return t(`design.styles.${opt}`);
  if (key === 'priorities') return t(`design.priorities.${opt}`);
  if (key === 'priorityRoom') return t(`cbrief.o.rooms.${opt}`);
  if (key === 'suppliers' && opt !== 'la_comanda') return opt;
  if (key === 'budgetRange'){ const { min, max } = budgetBounds(opt as BudgetRange, cur), m = (v: number) => formatMoney(v, cur, lang);
    return max == null ? t('cbrief.budget.over', { min: m(min) }) : min === 0 ? t('cbrief.budget.under', { max: m(max) }) : t('cbrief.budget.between', { min: m(min), max: m(max) }); }
  return t(`cbrief.o.${key}.${opt}`);
}
/** Răspunsul la o întrebare, ca text (null = fără răspuns). */
export function answerText(t: Translate, lang: Lang, q: Question, data: Partial<ClientBrief>, cur: string): string | null {
  const v = (data as Record<string, unknown>)[q.key]; if (v === undefined || v === null || v === '') return null;
  if (q.kind === 'bool') return t(v ? 'cbrief.yes' : 'cbrief.no');
  if (q.kind === 'enum') return optionLabel(t, lang, q.key, String(v), cur);
  if (q.kind === 'multi') return Array.isArray(v) && v.length ? v.map(o => optionLabel(t, lang, q.key, String(o), cur)).join(', ') : null;
  return String(v);
}
export const questionsOf = (section: string) => (QUESTIONS as readonly Question[]).filter(q => q.section === section);
