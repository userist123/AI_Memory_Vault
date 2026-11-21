// Ciclul de viață al constatărilor de coordonare (docs/bim/GHID_BIM.md §3.7): statusul se salvează în proiect pe id-ul stabil
// al constatării (`cf-xxxxxxxx`, core/coordination-review.ts findingId). „new” e implicit și nu se salvează. Acceptarea și
// fals-pozitivul cer justificare. Nicio constatare nu se scoate din listă sau din raport din cauza statusului: un conflict
// marcat „rezolvat” care apare din nou e semnalat (`stillPresent`).
import type { IssueRecord, IssueStatus } from './types';
import type { CoordinationFinding, CoordinationRun } from './coordination-review';
import { PlanLimitError } from './plan-limits';

export const ISSUE_STATUSES = ['new', 'reviewed', 'accepted', 'resolved', 'false_positive'] as const;
export const ISSUE_LIMITS = { max: 2000, noteMax: 500, byMax: 60 } as const;
const ID = /^cf-[0-9a-f]{8}$/;
const bad = (m: string): never => { throw new PlanLimitError(m); };
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f<>]/g, '').replace(/\s+/g, ' ').trim().slice(0, max) : '';

export function sanitizeIssues(v: unknown): Record<string, IssueRecord> {
  if (v == null) return {};
  if (typeof v !== 'object' || Array.isArray(v)) bad('Statusurile constatărilor sunt invalide.');
  const entries = Object.entries(v as Record<string, any>); if (entries.length > ISSUE_LIMITS.max) bad(`Cel mult ${ISSUE_LIMITS.max} statusuri de constatări.`);
  const out: Record<string, IssueRecord> = {};
  for (const [id, r] of entries) {
    if (!ID.test(id)) bad('Id-ul unei constatări este invalid.');
    if (!r || typeof r !== 'object' || !(ISSUE_STATUSES as readonly string[]).includes(r.status)) bad('Statusul unei constatări este invalid.');
    if (typeof r.at !== 'string' || Number.isNaN(Date.parse(r.at))) bad('Data statusului unei constatări este invalidă.');
    if (r.status === 'new') continue;
    const note = clean(r.note, ISSUE_LIMITS.noteMax), by = clean(r.by, ISSUE_LIMITS.byMax);
    if ((r.status === 'accepted' || r.status === 'false_positive') && !note) bad('Acceptarea sau marcarea ca fals pozitiv cere o justificare.');
    out[id] = { status: r.status, ...(note ? { note } : {}), ...(by ? { by } : {}), at: new Date(r.at).toISOString() };
  }
  return out;
}

export interface FindingWithStatus extends CoordinationFinding { status: IssueStatus; note?: string; by?: string; at?: string; stillPresent?: true }
export function withStatus(findings: CoordinationFinding[], issues: Record<string, IssueRecord> | undefined): FindingWithStatus[] {
  return findings.map(f => { const r = issues?.[f.id]; if (!r) return { ...f, status: 'new' as const };
    return { ...f, status: r.status, ...(r.note ? { note: r.note } : {}), ...(r.by ? { by: r.by } : {}), at: r.at, ...(r.status === 'resolved' ? { stillPresent: true as const } : {}) }; });
}

/** Înainte/după (ghidul §3.7 pasul 7): ce a apărut, ce a dispărut, ce a rămas, pe id. */
export function compareRuns(before: CoordinationFinding[], after: CoordinationFinding[]) {
  const b = new Set(before.map(f => f.id)), a = new Set(after.map(f => f.id));
  return { added: after.filter(f => !b.has(f.id)), gone: before.filter(f => !a.has(f.id)), kept: after.filter(f => b.has(f.id)) };
}

/** Celulă de tabel Markdown: backslash-ul întâi (altfel un \ final ar anula escaparea lui |), apoi |, apoi rândurile noi. */
export const cell = (s: string) => s.replace(/\\/g, '\\\\').replace(/\|/g, '\\|').replace(/[\r\n]+/g, ' ');
/** Raport Markdown reproductibil: aceleași intrări → același text (data vine din parametri, nu din ceas). */
export function coordinationReport(run: CoordinationRun, issues: Record<string, IssueRecord> | undefined, meta: { project: string; generatedAt: string }): string {
  const rows = withStatus(run.findings, issues);
  const byStatus = Object.fromEntries(ISSUE_STATUSES.map(s => [s, rows.filter(r => r.status === s).length]));
  const lines = [
    `# Raport de coordonare — ${cell(meta.project)}`, '',
    `Generat: ${meta.generatedAt} · ${run.revision === 'draft' ? 'proiectul curent' : `revizia ${run.revision}`}`, '',
    run.statement, '',
    `**Reguli rulate (${run.rulesChecked.length}):** ${run.rulesChecked.join(', ')}`, '',
    `**Nerulate:** ${run.rulesNotChecked.length ? run.rulesNotChecked.join(', ') : '—'}`, '',
    `**Pe severitate:** ERROR ${run.counts.ERROR} · WARNING ${run.counts.WARNING} · INFO ${run.counts.INFO} · UNKNOWN ${run.counts.UNKNOWN}`, '',
    `**Pe status:** ${ISSUE_STATUSES.map(s => `${s} ${byStatus[s]}`).join(' · ')}`, '',
    '| Id | Severitate | Cod | Elemente | Regula | Status | Justificare | Responsabil | Mesaj |', '|---|---|---|---|---|---|---|---|---|',
    ...rows.map(r => `| ${r.id} | ${r.severity} | ${r.code} | ${cell(r.refs.join(', '))} | ${r.rule} | ${r.status}${r.stillPresent ? ' (încă prezent)' : ''} | ${cell(r.note ?? '')} | ${cell(r.by ?? '')} | ${cell(r.message)} |`),
    '', 'Verificare asistivă (CONSTITUTION art. 12): nu certifică conformitatea și nu înlocuiește verificarea unui specialist autorizat.', ''];
  return lines.join('\n');
}
