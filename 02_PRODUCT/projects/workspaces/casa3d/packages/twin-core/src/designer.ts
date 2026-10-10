// Design providers: anything that turns a Brief into Design DSL. The AI provider is one of them; the rules
// engine is the always-available fallback. Both emit DSL only; neither sees or writes coordinates.
import type { Twin } from './twin.js';
import type { Catalog, CatalogItem } from './catalog.js';
import { validateDsl, type Constraint, type DesignDsl, type DslError, type Operation } from './dsl.js';

export interface Brief {
  roomId: string;
  style?: 'economic' | 'echilibrat' | 'premium';
  occupants?: number;
  children?: boolean;
  pets?: boolean;
  accessibility?: boolean;
  /** Target budget in RON; undefined = no target. */
  budget?: number;
  /** Retailers the user accepts; empty = any. */
  retailers?: string[];
  /** Roles wanted in the room, in priority order (bed, nightstand, wardrobe, desk, sofa, table, chair...). */
  wants: string[];
  excludes?: string[]; // catalog ids or roles the user refuses
}

export interface DesignContext { twin: Twin; catalog: Catalog; catalogItems: readonly CatalogItem[] }

export interface DesignProvider {
  readonly name: string;
  /** Returns raw DSL (untrusted) or null when it cannot propose. Never throws for content reasons. */
  propose(brief: Brief, context: DesignContext, variant: 0 | 1 | 2): Promise<unknown>;
}

export interface ProposalDraft { provider: string; variant: number; dsl?: DesignDsl; errors: DslError[]; raw: unknown }

/** Runs a provider and validates its output; the result is never trusted before validateDsl(). */
export async function draft(provider: DesignProvider, brief: Brief, context: DesignContext, variant: 0 | 1 | 2): Promise<ProposalDraft> {
  let raw: unknown = null;
  try { raw = await provider.propose(brief, context, variant); }
  catch (e) { return { provider: provider.name, variant, errors: [{ path: '$', code: 'NOT_AN_OBJECT', message: `Furnizorul a esuat: ${(e as Error).message}` }], raw: null }; }
  const v = validateDsl(raw, context.twin, context.catalog);
  const out: ProposalDraft = { provider: provider.name, variant, errors: v.errors, raw };
  if (v.dsl) out.dsl = v.dsl;
  return out;
}

const VARIANT_TITLES = ['Economic', 'Echilibrat', 'Premium'] as const;

/**
 * Rules engine: deterministic DSL from the brief and the catalog. Picks, per wanted role, the cheapest known
 * item (Economic), the median (Echilibrat) or the largest (Premium) among the allowed retailers. Items with
 * UNKNOWN price are allowed but sort last for Economic. Never invents products: only catalog ids.
 */
export type CanonicalRole = 'bed' | 'nightstand' | 'wardrobe' | 'sofa' | 'desk' | 'chair' | 'table' | 'bookcase' | 'dresser';

export class RulesDesignProvider implements DesignProvider {
  readonly name = 'rules';
  /** Maps the catalog's own role names (e.g. 'pat', 'dulap') to the canonical roles the layout rules know. */
  constructor(private readonly roleAliases: Readonly<Record<string, CanonicalRole>> = {}) {}
  private canonical(role: string): string { return this.roleAliases[role] ?? role; }
  async propose(brief: Brief, context: DesignContext, variant: 0 | 1 | 2): Promise<unknown> {
    const ops: Operation[] = [];
    const used = new Set<string>();
    const excludes = new Set(brief.excludes ?? []);
    const retailers = new Set(brief.retailers ?? []);
    const anchorRef: Record<string, string> = {};
    for (const role of brief.wants) {
      if (excludes.has(role)) continue;
      const pool = context.catalogItems
        .filter(i => i.role === role && !excludes.has(i.id) && (retailers.size === 0 || (i.retailer !== undefined && retailers.has(i.retailer))))
        .sort((a, b) => a.id < b.id ? -1 : 1);
      if (pool.length === 0) continue;
      const pick = choose(pool, variant);
      const ref = `${role}-${used.size + 1}`;
      used.add(ref);
      const constraints: Constraint[] = [], kind = this.canonical(role);
      if (['bed', 'wardrobe', 'sofa', 'desk', 'bookcase', 'dresser'].includes(kind)) constraints.push({ type: 'againstWall' });
      if (kind === 'nightstand' && anchorRef['bed']) constraints.push({ type: 'near', ref: anchorRef['bed'] });
      if (kind === 'chair' && anchorRef['desk']) constraints.push({ type: 'near', ref: anchorRef['desk'] });
      if (kind === 'table' && anchorRef['sofa']) constraints.push({ type: 'keepClear', ref: anchorRef['sofa'], distance: brief.accessibility ? 0.9 : 0.6 });
      if (brief.accessibility && anchorRef['bed'] && kind !== 'nightstand') constraints.push({ type: 'keepClear', ref: anchorRef['bed'], distance: 0.9 });
      ops.push({ op: 'ADD', ref, roomId: brief.roomId, catalogId: pick.id, role, constraints, reason: `${VARIANT_TITLES[variant]}: ${pick.name}` });
      anchorRef[kind] ??= ref;
    }
    if (ops.length === 0) return null;
    return { version: '1.1', title: VARIANT_TITLES[variant], operations: ops };
  }
}

function choose(pool: CatalogItem[], variant: 0 | 1 | 2): CatalogItem {
  const known = pool.filter(i => i.price !== 'UNKNOWN').sort((a, b) => (a.price as { amount: number }).amount - (b.price as { amount: number }).amount || (a.id < b.id ? -1 : 1));
  if (variant === 0) return known[0] ?? pool[0]!;
  if (variant === 2) return [...pool].sort((a, b) => b.w * b.d - a.w * a.d || (a.id < b.id ? -1 : 1))[0]!;
  // Balanced = the upper median by price, so with two options it is the better one, not the cheapest again.
  const mid = known.length ? known[Math.ceil((known.length - 1) / 2)]! : pool[Math.ceil((pool.length - 1) / 2)]!;
  return mid;
}
