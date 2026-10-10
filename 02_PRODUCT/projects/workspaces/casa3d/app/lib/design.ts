// Faza 5: design pe Digital Twin. Brief → motor de reguli (DSL, fără coordonate) → Solver → până la 3 alternative,
// evaluate BOQ-aware → previzualizare nepersistentă → aplicare revalidată pe proiectul curent, cu protecție la stale.
import { RulesDesignProvider, draft, solveAlternatives, evaluateBoq, catalogFromItems, createProject, preview, accept, fingerprint,
  type Brief, type DesignDsl, type EvaluatedAlternative, type Issue } from '@casa3d/twin-core';
import { getDb } from './db';
import { HttpError, getCatalog, getProject, saveDraft, createRevision } from './repo';
import { snapshotToTwin, twinCatalogItems, placementsFromTwin } from './twin';
import { groups } from '../core/catalog';
import type { Catalog, Snapshot, FurniturePlacement } from '../core/types';

export interface TwinBrief { roomId: string; wants: string[]; budget?: number; retailers?: ('IKEA' | 'Dedeman')[]; accessibility: boolean; replace: boolean }
export const MAX_WANTS = 12;

export function checkTwinBrief(input: unknown, snap: Snapshot, groupKeys: string[]): TwinBrief {
  const b = input as any; if (!b || typeof b !== 'object') throw new HttpError(400, 'Brief invalid.');
  if (typeof b.roomId !== 'string' || !snap.floor.rooms.some(r => r.id === b.roomId)) throw new HttpError(400, 'Camera nu există în proiect.');
  const wants: string[] = Array.isArray(b.wants) ? b.wants.filter((w: unknown): w is string => typeof w === 'string' && groupKeys.includes(w)).slice(0, MAX_WANTS) : [];
  if (!wants.length) throw new HttpError(400, 'Alege cel puțin o categorie de mobilier din catalog.');
  const out: TwinBrief = { roomId: b.roomId, wants: Array.from(new Set(wants)), accessibility: !!b.accessibility, replace: !!b.replace };
  if (b.budget != null && b.budget !== ''){ const n = Number(b.budget); if (!Number.isFinite(n) || n < 0) throw new HttpError(400, 'Buget invalid.'); out.budget = n; }
  if (Array.isArray(b.retailers)){ const r = b.retailers.filter((s: unknown): s is 'IKEA' | 'Dedeman' => s === 'IKEA' || s === 'Dedeman'); if (r.length) out.retailers = r; }
  return out;
}

export interface PublicAlternative { index: number; title: string; ok: boolean; outcomes: EvaluatedAlternative['result']['outcomes']; measures: EvaluatedAlternative['measures']; boq: EvaluatedAlternative['boq']; issues: Issue[]; placements: FurniturePlacement[]; dsl: DesignDsl }
interface StoredAlternative { index: number; title: string; dsl: DesignDsl; ok: boolean; issues: Issue[] }

/** Baza de lucru: cu `replace`, piesele din camera vizată sunt scoase ca solver-ul să re-aşeze camera; altfel se adaugă peste ele. */
const baseFor = (snap: Snapshot, brief: TwinBrief): Snapshot => brief.replace ? { ...snap, placements: snap.placements.filter(p => p.roomId !== brief.roomId) } : snap;

async function solveBrief(snap: Snapshot, cat: Catalog, brief: TwinBrief){
  const base = baseFor(snap, brief), twin = snapshotToTwin(base, cat), items = twinCatalogItems(cat), catalog = catalogFromItems(items);
  const coreBrief: Brief = { roomId: brief.roomId, wants: brief.wants, accessibility: brief.accessibility, ...(brief.budget != null ? { budget: brief.budget } : {}), ...(brief.retailers ? { retailers: brief.retailers } : {}) };
  const provider = new RulesDesignProvider();
  const drafts = await Promise.all(([0, 1, 2] as const).map(v => draft(provider, coreBrief, { twin, catalog, catalogItems: items }, v)));
  const dsls = drafts.flatMap(d => d.dsl ? [d.dsl] : []);
  if (!dsls.length) throw new HttpError(422, 'Nicio variantă posibilă: catalogul nu are produse pentru categoriile cerute la magazinele alese.');
  // Bugetul din brief e al camerei vizate: BOQ-ul variantei se calculează doar pe piesele din acea cameră, nu pe toată casa.
  const alts: EvaluatedAlternative[] = solveAlternatives(dsls, twin, catalog).map(a => ({ ...a,
    boq: evaluateBoq({ ...a.result.twin, placements: a.result.twin.placements.filter(p => p.roomId === brief.roomId) }, catalog, brief.budget) }));
  return { base, twin, dsls, alts };
}

export async function generateDesign(owner: string, id: string, input: unknown){
  const p = await getProject(owner, id), cat = await getCatalog();
  const brief = checkTwinBrief(input, p.draft, Object.keys(groups(cat)));
  const { base, dsls, alts } = await solveBrief(p.draft, cat, brief);
  // Amprenta de bază acoperă tot proiectul: orice modificare ulterioară, în orice cameră, face propunerea stale.
  const baseFingerprint = fingerprint(snapshotToTwin(p.draft, cat));
  const stored: StoredAlternative[] = alts.map(a => ({ index: a.index, title: a.title, dsl: dsls[a.index]!, ok: a.result.ok, issues: a.issues }));
  const { q } = await getDb();
  const { rows } = await q('insert into design_proposals(project_id, base_fingerprint, brief, alternatives) values($1,$2,$3,$4) returning id, created_at', [id, baseFingerprint, JSON.stringify(brief), JSON.stringify(stored)]);
  const alternatives: PublicAlternative[] = alts.map(a => ({ index: a.index, title: a.title, ok: a.result.ok, outcomes: a.result.outcomes, measures: a.measures, boq: a.boq, issues: a.issues,
    placements: a.result.ok ? placementsFromTwin(base, cat, a.result.twin) : [], dsl: dsls[a.index]! }));
  return { id: rows[0].id as string, createdAt: rows[0].created_at, baseFingerprint, source: 'rules', aiGenerated: false,
    notes: ['Variantele sunt generate de motorul de reguli pe Digital Twin (fără AI). Pozițiile sunt calculate de Geometry Engine; AI-ul, când va fi activat, va produce doar DSL.'], alternatives };
}

async function proposalOf(id: string, pid: string){
  if (!/^[0-9a-f-]{36}$/i.test(pid)) throw new HttpError(404, 'Propunere inexistentă.');
  const { q } = await getDb(); const { rows } = await q('select * from design_proposals where id=$1 and project_id=$2', [pid, id]);
  if (!rows[0]) throw new HttpError(404, 'Propunere inexistentă.'); return rows[0];
}

export async function listDesigns(owner: string, id: string){
  const p = await getProject(owner, id), cat = await getCatalog(); const { q } = await getDb();
  const { rows } = await q('select id, base_fingerprint, brief, decisions, created_at from design_proposals where project_id=$1 order by created_at desc limit 20', [id]);
  const current = fingerprint(snapshotToTwin(p.draft, cat));
  return rows.map(r => ({ id: r.id, createdAt: r.created_at, brief: r.brief as TwinBrief, decisions: r.decisions, status: r.base_fingerprint === current ? 'PREVIEW' : 'STALE' }));
}

/** Aplică sau respinge o alternativă. Aplicarea revalidează pe proiectul curent și refuză propunerile stale (409). */
export async function decideDesign(owner: string, id: string, pid: string, index: number, action: 'apply' | 'reject', confirmWarnings: boolean){
  const p = await getProject(owner, id), cat = await getCatalog(), row = await proposalOf(id, pid);
  const alt = (row.alternatives as StoredAlternative[]).find(a => a.index === index);
  if (!alt) throw new HttpError(404, 'Varianta nu există în propunere.');
  const { q } = await getDb();
  const decided = (row.decisions ?? {})[String(index)];
  if (decided) throw new HttpError(409, 'Varianta a fost deja decisă.');
  if (action === 'reject'){ await q("update design_proposals set decisions = decisions || $1::jsonb where id=$2", [JSON.stringify({ [index]: { decision: 'rejected', at: new Date().toISOString() } }), pid]); return { ok: true }; }
  if (fingerprint(snapshotToTwin(p.draft, cat)) !== row.base_fingerprint) throw new HttpError(409, 'Proiectul s-a schimbat de când a fost generată propunerea; generează din nou.', [{ code: 'STALE', severity: 'ERROR', message: 'Propunere stale.' }]);
  const brief = row.brief as TwinBrief, base = baseFor(p.draft, brief);
  const twin = snapshotToTwin(base, cat), catalog = catalogFromItems(twinCatalogItems(cat));
  const core = createProject(id, twin);
  const prop = preview(core, pid, alt.dsl, catalog, 'rules');
  const out = accept(core, prop, { confirmWarnings });
  if (!out.accepted){
    const issues = out.validation?.issues ?? out.issues ?? [];
    const msg = out.reason === 'NEEDS_CONFIRMATION' ? 'Varianta are avertismente; confirmă-le înainte de aplicare.' : 'Varianta nu poate fi aplicată pe proiectul curent.';
    throw new HttpError(409, msg, issues);
  }
  const next: Snapshot = { ...p.draft, placements: placementsFromTwin(base, cat, out.project.twin) };
  await saveDraft(owner, id, next);
  await q("update design_proposals set decisions = decisions || $1::jsonb where id=$2", [JSON.stringify({ [index]: { decision: 'approved', at: new Date().toISOString() } }), pid]);
  const rev = await createRevision(owner, id, `Varianta ${alt.title} (Digital Twin, motor de reguli) aplicată`);
  return { snapshot: next, revision: rev.number, validation: out.validation };
}
