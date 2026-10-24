// Editor stages grouped into the four phases a design studio works in, and a "done" state per stage derived only from
// project data that already exists (no manual flag). The editor shows a dot per stage and a "Continue" button.
import type { Snapshot } from './types';
import type { ProjectStatus } from './project-meta';
import { floors } from './levels';
import { DEFAULT_BRIEF } from './brief';

/** Client links that still work (revoked ones do not count as "sent"). */
export function activeShares(list: { revokedAt?: string | null }[]): number { return list.filter(s => !s.revokedAt).length; }
/** The brief holds real client input: some field other than the style differs from the defaults. Applying a style
 *  package fills the brief from DEFAULT_BRIEF with only the style changed (core/styles.ts), which is not client input. */
export function briefFilled(b: unknown): boolean {
  if (!b || typeof b !== 'object') return false; const o = b as Record<string, unknown>, d = DEFAULT_BRIEF as unknown as Record<string, unknown>;
  return Object.keys(o).some(k => k !== 'style' && JSON.stringify(o[k] ?? null) !== JSON.stringify(d[k] ?? null));
}

export const STAGE_IDS = ['survey', 'concept', 'finishes', 'ffe', 'mep', 'docs', 'quote', 'present', 'orders', 'site'] as const;
export type StageId = typeof STAGE_IDS[number];
export type PhaseId = 'survey' | 'concept' | 'detail' | 'delivery';
/** Releveu (measure) → Concept (concept, finishes, furniture) → Detaliere (services, drawings, quote) → Livrare (client, orders, site). */
export const PHASES: { id: PhaseId; stages: StageId[] }[] = [
  { id: 'survey', stages: ['survey'] },
  { id: 'concept', stages: ['concept', 'finishes', 'ffe'] },
  { id: 'detail', stages: ['mep', 'docs', 'quote'] },
  { id: 'delivery', stages: ['present', 'orders', 'site'] },
];
export const phaseOf = (s: StageId): PhaseId => PHASES.find(p => p.stages.includes(s))!.id;

export interface ProgressInput {
  house: Snapshot;
  /** advisor findings that are not tips (the same number the Drawings stage shows) */
  warnings: number;
  /** client links issued for this project (active shares) */
  shares: number;
  status: ProjectStatus;
}

/** What "done" means for each stage, from data the project already has. Kept deliberately simple and explainable. */
export function stageDone(i: ProgressInput): Record<StageId, boolean> {
  const fl = floors(i.house), rooms = fl.flatMap(f => f.rooms), walls = fl.reduce((n, f) => n + f.walls.length, 0);
  const target = i.house.budget?.target;
  return {
    // measured: at least one room and its walls drawn
    survey: rooms.length > 0 && walls > 0,
    // concept: the designer recorded the client brief (more than the style a package wrote)
    concept: briefFilled(i.house.brief),
    // finishes: every room has finishes chosen explicitly (not only the defaults)
    finishes: rooms.length > 0 && rooms.every(r => !!i.house.finishes?.[r.id]),
    // furniture: at least one piece placed
    ffe: i.house.placements.length > 0,
    // services: at least one electrical / plumbing point placed
    mep: (i.house.tech?.length ?? 0) > 0,
    // drawings: there is a plan and the advisor has no open warnings
    docs: rooms.length > 0 && i.warnings === 0,
    // quote: a budget target was set
    quote: typeof target === 'number' && target > 0,
    // client: a link was sent to the client
    present: i.shares > 0,
    // orders: the project moved to execution (or finished)
    orders: i.status === 'execution' || i.status === 'done',
    // site: the project was handed over
    site: i.status === 'done',
  };
}

/** The stage after `s`, or null on the last one. */
export function nextStage(s: StageId): StageId | null { const k = STAGE_IDS.indexOf(s); return k >= 0 && k < STAGE_IDS.length - 1 ? STAGE_IDS[k + 1]! : null; }
/** How many stages are done, for the progress label. */
export function doneCount(d: Record<StageId, boolean>): number { return STAGE_IDS.filter(s => d[s]).length; }
