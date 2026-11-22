// „De urmărit azi”: ce cere atenție pe toate proiectele studioului, dintr-o privire, fără să deschizi fiecare lucrare.
// Funcție pură peste faptele fiecărui proiect (strânse de lib/attention.ts); regulile de restanță și întârziere rămân cele
// din modulele lor: paymentSchedule (tranșe), procurementSummary (comenzi), snagSummary (remedieri), scheduleIssues
// (grafic), statementIssues (situații). Aici se adaugă doar pragurile de „curând” și ordinea.
import { paymentSchedule, type Milestone } from './commercial';
import { procurementSummary, type ProcurementRow } from './procurement';
import { snagSummary, type Snag } from './site';
import { scheduleIssues, type Phase } from './schedule';
import { statementIssues, type StatementInput } from './progress';
import type { ProjectStatus } from './project-meta';
import type { StageId } from './stage-progress';

export type AttentionSeverity = 'urgent' | 'soon' | 'info';
export const ATTENTION_SEVERITIES = ['urgent', 'soon', 'info'] as const;
export type AttentionKind = 'payment' | 'order' | 'delivery' | 'snag' | 'schedule' | 'statement' | 'brief' | 'rfq' | 'share' | 'feedback';
/** Pragurile, în zile: „curând” = în următoarele 7 zile; o remediere deschisă de peste 14 zile urcă la „curând”. */
export const ATTENTION = { soonDays: 7, snagOldDays: 14, statementStaleDays: 35, max: 50 } as const;

export interface ProjectFacts {
  projectId: string; projectName: string; status?: ProjectStatus; dueDate?: string | null;
  /** tranșele de plată din datele comerciale */
  milestones?: Milestone[];
  /** liniile de comandă cu starea lor, calculate pentru `today` (core/procurement.ts procurementRows) */
  orders?: ProcurementRow[];
  snags?: Pick<Snag, 'status' | 'createdAt'>[];
  phases?: Phase[];
  statements?: Pick<StatementInput, 'number' | 'from' | 'to'>[];
  /** chestionarul clientului: trimis de client și încă nepreluat în brief */
  brief?: { submittedAt: string | null; importedAt: string | null } | null;
  /** cererile de ofertă către meseriași: `open` = nici închisă, nici atribuită; `offers` = oferte trimise */
  rfqs?: { id: string; title: string; dueDate: string | null; open: boolean; offers: number }[];
  /** cel mai nou link de prezentare activ (expiresAt null = fără expirare) */
  share?: { expiresAt: string | null } | null;
  /** părerile nerezolvate ale clientului: observații și întrebări (`open`), aprobări (`approvals`) */
  feedback?: { open: number; approvals: number; latest: string | null } | null;
}
export interface AttentionItem {
  /** stabil între încărcări: proiect + mesaj + referință */
  id: string; projectId: string; projectName: string; kind: AttentionKind; severity: AttentionSeverity;
  /** cheia textului (`attn.<msg>` în lib/locales), numărul pentru plural și variabilele lui */
  msg: string; n: number; vars: Record<string, string | number>;
  /** termenul sau data de la care așteaptă (AAAA-LL-ZZ) */
  due: string | null;
  /** etapa din proiect unde se rezolvă și adresa ei */
  stage: StageId; href: string;
}

const dayNum = (d: string) => Math.round(Date.parse(d.slice(0, 10) + 'T00:00:00Z') / 864e5);
/** Zile de la `a` până la `b` (negativ dacă `b` e înainte). */
export const daysBetween = (a: string, b: string) => dayNum(b) - dayNum(a);
const addDays = (d: string, n: number) => new Date(Date.parse(d + 'T00:00:00Z') + n * 864e5).toISOString().slice(0, 10);
const minDate = (xs: (string | null | undefined)[]) => xs.filter((x): x is string => !!x).sort()[0] ?? null;
const RANK: Record<AttentionSeverity, number> = { urgent: 0, soon: 1, info: 2 };
export const attentionHref = (projectId: string, stage: StageId) => `/p/${projectId}?stage=${stage}`;

/** Elementele unui singur proiect, nesortate. */
export function projectAttention(f: ProjectFacts, today: string): AttentionItem[] {
  const out: AttentionItem[] = [], soon = addDays(today, ATTENTION.soonDays);
  const add = (kind: AttentionKind, severity: AttentionSeverity, msg: string, stage: StageId, due: string | null, n = 0, vars: Record<string, string | number> = {}, ref = '') =>
    out.push({ id: `${f.projectId}:${msg}${ref ? ':' + ref : ''}`, projectId: f.projectId, projectName: f.projectName, kind, severity, msg, n, vars, due, stage, href: attentionHref(f.projectId, stage) });

  // tranșe de plată: restante (regula din paymentSchedule) sau scadente în următoarele 7 zile; totalul contractului nu
  // contează pentru date, deci suma apare doar la tranșele cu sumă fixă
  for (const r of paymentSchedule(f.milestones ?? [], 0, today).rows){
    const vars = { label: r.label, ...(r.fixed != null ? { amount: r.fixed } : {}), ...(r.pct != null ? { pct: r.pct } : {}) };
    if (r.overdue) add('payment', 'urgent', 'payment.late', 'quote', r.due!, r.daysLate!, vars, r.id);
    else if (r.status !== 'paid' && r.due && r.due <= soon) add('payment', 'soon', r.due === today ? 'payment.today' : 'payment.soon', 'quote', r.due, daysBetween(today, r.due), vars, r.id);
  }

  // comenzi: câte un element pe fel (de comandat cu termen depășit, de comandat curând, livrări întârziate), cu numărul lor
  const rows = f.orders ?? [], ps = procurementSummary(rows);
  if (ps.orderLate) add('order', 'urgent', 'order.late', 'orders', minDate(rows.filter(r => r.orderUrgency === 'late').map(r => r.orderBy)), ps.orderLate);
  if (ps.overdue) add('delivery', 'urgent', 'delivery.late', 'orders', minDate(rows.filter(r => r.overdue).map(r => r.state.eta)), ps.overdue);
  if (ps.orderSoon) add('order', 'soon', 'order.soon', 'orders', minDate(rows.filter(r => r.orderUrgency === 'soon').map(r => r.orderBy)), ps.orderSoon);

  // remedieri: cele deschise (cea mai veche dă data); cele rezolvate așteaptă doar verificarea
  const snags = f.snags ?? [], ss = snagSummary(snags);
  if (ss.open){ const oldest = minDate(snags.filter(s => s.status === 'open').map(s => s.createdAt.slice(0, 10)));
    add('snag', oldest && daysBetween(oldest, today) > ATTENTION.snagOldDays ? 'soon' : 'info', 'snag.open', 'site', oldest, ss.open); }
  if (ss.fixed) add('snag', 'info', 'snag.fixed', 'site', null, ss.fixed);

  // graficul de execuție: etape neterminate cu sfârșitul trecut, apoi nepotrivirile din scheduleIssues
  const phases = f.phases ?? [], late = phases.filter(p => !p.done && p.end && p.end < today), issues = scheduleIssues(phases, f.dueDate ?? null);
  if (late.length) add('schedule', 'urgent', 'schedule.late', 'site', minDate(late.map(p => p.end)), late.length, { label: late[0]!.title });
  const pastDue = issues.filter(i => i.code === 'PAST_DUE').length, order = issues.filter(i => i.code === 'BEFORE_PREDECESSOR').length;
  if (pastDue) add('schedule', 'soon', 'schedule.pastDue', 'site', f.dueDate ?? null, pastDue);
  if (order) add('schedule', 'info', 'schedule.order', 'site', null, order);

  // situații de lucrări: perioade suprapuse / numere lipsă; în execuție, ultima situație prea veche
  const st = f.statements ?? [];
  const stIssues = statementIssues(st).length;
  if (stIssues) add('statement', 'info', 'statement.issues', 'quote', null, stIssues);
  const last = st.map(s => s.to).sort().pop() ?? null;
  if (f.status === 'execution' && last && daysBetween(last, today) > ATTENTION.statementStaleDays) add('statement', 'info', 'statement.stale', 'quote', last, daysBetween(last, today));

  if (f.brief?.submittedAt && !f.brief.importedAt) add('brief', 'soon', 'brief.submitted', 'concept', f.brief.submittedAt.slice(0, 10));

  // cereri de ofertă: termenul trecut fără atribuire e urgent; altfel ofertele primite așteaptă evaluarea
  for (const r of f.rfqs ?? []){ if (!r.open) continue;
    if (r.dueDate && r.dueDate < today) add('rfq', 'urgent', r.offers ? 'rfq.dueOffers' : 'rfq.dueNone', 'quote', r.dueDate, r.offers, { label: r.title }, r.id);
    else if (r.offers) add('rfq', 'soon', 'rfq.offers', 'quote', r.dueDate, r.offers, { label: r.title }, r.id); }

  const exp = f.share?.expiresAt?.slice(0, 10);
  if (exp && exp >= today && exp <= soon) add('share', 'soon', exp === today ? 'share.today' : 'share.soon', 'present', exp, daysBetween(today, exp));

  if (f.feedback?.open) add('feedback', 'soon', 'feedback.open', 'present', f.feedback.latest?.slice(0, 10) ?? null, f.feedback.open);
  if (f.feedback?.approvals) add('feedback', 'info', 'feedback.approvals', 'present', f.feedback.latest?.slice(0, 10) ?? null, f.feedback.approvals);
  return out;
}

/** Ordinea listei: urgent → curând → de știut; în aceeași treaptă, termenul cel mai vechi întâi (fără termen la urmă). */
export function sortAttention(items: AttentionItem[]): AttentionItem[] {
  return [...items].sort((a, b) => RANK[a.severity] - RANK[b.severity] || (a.due && b.due ? a.due.localeCompare(b.due) : a.due ? -1 : b.due ? 1 : 0)
    || a.projectName.localeCompare(b.projectName) || a.id.localeCompare(b.id));
}
/** Lista sortată peste toate proiectele, cel mult `max` elemente (cele mai urgente rămân). */
export function attentionItems(facts: ProjectFacts[], today: string, max: number = ATTENTION.max): AttentionItem[] {
  return sortAttention(facts.flatMap(f => projectAttention(f, today))).slice(0, Math.max(0, max));
}
export type AttentionCounts = Record<AttentionSeverity, number>;
export const attentionCounts = (items: Pick<AttentionItem, 'severity'>[]): AttentionCounts =>
  ({ urgent: items.filter(i => i.severity === 'urgent').length, soon: items.filter(i => i.severity === 'soon').length, info: items.filter(i => i.severity === 'info').length });
/** Lista limitată împreună cu contoarele pe TOATE elementele (și cele tăiate de limită). */
export function attentionReport(facts: ProjectFacts[], today: string, max: number = ATTENTION.max){
  const all = attentionItems(facts, today, Infinity);
  return { items: all.slice(0, Math.max(0, max)), counts: attentionCounts(all), total: all.length };
}
/** Gruparea pe proiect, păstrând ordinea listei: primul proiect e cel cu elementul cel mai urgent. */
export function attentionByProject(items: AttentionItem[]): { projectId: string; projectName: string; severity: AttentionSeverity; items: AttentionItem[] }[] {
  const m = new Map<string, { projectId: string; projectName: string; severity: AttentionSeverity; items: AttentionItem[] }>();
  for (const i of items){ const g = m.get(i.projectId); if (g) g.items.push(i); else m.set(i.projectId, { projectId: i.projectId, projectName: i.projectName, severity: i.severity, items: [i] }); }
  return [...m.values()];
}
