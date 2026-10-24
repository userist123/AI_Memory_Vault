// AI Design Approval: a proposal is separate from the project. Preview never persists; Accept revalidates the
// current twin, rejects stale proposals, blocks on ERROR and requires confirmation on WARNING.
import { type Twin, fingerprint, normalize } from './twin.js';
import { validate, type ValidationResult, type Issue } from './engine.js';
import type { DesignDsl } from './dsl.js';
import { solve, type SolveResult } from './solver.js';
import type { Catalog } from './catalog.js';

export type ProposalStatus = 'PREVIEW' | 'APPLIED' | 'REJECTED' | 'STALE' | 'FAILED';

export interface Proposal {
  id: string;
  projectId: string;
  /** Fingerprint of the twin the proposal was solved against. */
  baseFingerprint: string;
  dsl: DesignDsl;
  result: SolveResult;
  /** Fingerprint of the candidate twin (equal to base when the solve failed). */
  candidateFingerprint: string;
  createdAt: string;
  status: ProposalStatus;
  source: 'ai' | 'rules' | 'user';
}

export interface Revision {
  number: number;
  twin: Twin;
  fingerprint: string;
  proposalId?: string;
  createdAt: string;
  note?: string;
}

export interface Project {
  id: string;
  twin: Twin;
  revisions: Revision[];
}

export function createProject(id: string, twin: Twin, now = new Date().toISOString()): Project {
  const t = normalize(twin);
  return { id, twin: t, revisions: [{ number: 1, twin: t, fingerprint: fingerprint(t), createdAt: now, note: 'initial' }] };
}

/** Solves the DSL against the project's current twin. Nothing is persisted: the project is not touched. */
export function preview(project: Project, proposalId: string, dsl: DesignDsl, catalog: Catalog, source: Proposal['source'], now = new Date().toISOString()): Proposal {
  const base = project.twin;
  const result = solve(dsl, base, catalog);
  return {
    id: proposalId, projectId: project.id, baseFingerprint: fingerprint(base), dsl, result,
    candidateFingerprint: fingerprint(result.twin), createdAt: now, status: result.ok ? 'PREVIEW' : 'FAILED', source,
  };
}

export type AcceptOutcome =
  | { accepted: true; project: Project; revision: Revision; validation: ValidationResult }
  | { accepted: false; reason: 'STALE' | 'FAILED' | 'ERROR' | 'NEEDS_CONFIRMATION' | 'ALREADY_DECIDED'; validation?: ValidationResult; issues?: Issue[] };

/**
 * Applies a previewed proposal to the project, creating a revision. Pure: returns a new project.
 * - the proposal must have been solved against the project's current twin (same fingerprint), else STALE;
 * - the candidate is revalidated now, against the current state, not trusted from the preview;
 * - ERROR blocks; WARNING requires `confirmWarnings: true`.
 */
export function accept(project: Project, proposal: Proposal, opts: { confirmWarnings?: boolean; now?: string } = {}): AcceptOutcome {
  if (proposal.status === 'APPLIED' || proposal.status === 'REJECTED') return { accepted: false, reason: 'ALREADY_DECIDED' };
  if (proposal.status === 'FAILED' || !proposal.result.ok) return { accepted: false, reason: 'FAILED', issues: proposal.result.validation.issues };
  if (proposal.projectId !== project.id || proposal.baseFingerprint !== fingerprint(project.twin)) return { accepted: false, reason: 'STALE' };
  const candidate = normalize(proposal.result.twin);
  const validation = validate(candidate);
  if (!validation.ok) return { accepted: false, reason: 'ERROR', validation };
  if (validation.needsConfirmation && !opts.confirmWarnings) return { accepted: false, reason: 'NEEDS_CONFIRMATION', validation };
  const now = opts.now ?? new Date().toISOString();
  const last = project.revisions[project.revisions.length - 1];
  const revision: Revision = { number: (last?.number ?? 0) + 1, twin: candidate, fingerprint: fingerprint(candidate), proposalId: proposal.id, createdAt: now };
  if (proposal.dsl.title) revision.note = proposal.dsl.title;
  return { accepted: true, project: { ...project, twin: candidate, revisions: [...project.revisions, revision] }, revision, validation };
}

/** Marks the proposal rejected. The project is returned unchanged, by design. */
export function reject(project: Project, proposal: Proposal): { project: Project; proposal: Proposal } {
  return { project, proposal: { ...proposal, status: 'REJECTED' } };
}

export function markApplied(proposal: Proposal): Proposal { return { ...proposal, status: 'APPLIED' }; }

/** Recomputes the status of a stored proposal against the current project (for lists in the UI). */
export function refreshStatus(project: Project, proposal: Proposal): Proposal {
  if (proposal.status !== 'PREVIEW') return proposal;
  return proposal.baseFingerprint === fingerprint(project.twin) ? proposal : { ...proposal, status: 'STALE' };
}
