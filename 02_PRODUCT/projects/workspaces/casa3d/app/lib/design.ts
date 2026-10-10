// Faza 5: design pe Digital Twin. Brief → motor de reguli (DSL, fără coordonate) → Solver → până la 3 alternative,
// evaluate BOQ-aware pe camera vizată → previzualizare nepersistentă → aplicare revalidată pe proiectul curent.
// Aplicarea e atomică: o singură alternativă pe propunere (claim condiționat), iar draftul se scrie doar dacă
// e exact cel pe care s-a validat (compare-and-swap), altfel 409.
import { RulesDesignProvider, draft, solveAlternatives, evaluateBoq, catalogFromItems, createProject, preview, accept, fingerprint,
  type Brief, type DesignDsl, type EvaluatedAlternative, type Issue } from '@casa3d/twin-core';
import { getDb } from './db';
import { HttpError, getCatalog, getProject, checkSnapshot, projectErrors } from './repo';
import { snapshotToTwin, twinCatalogItems, placementsFromTwin, untwinnablePlacements, ROLE_ALIASES } from './twin';
import { groups } from '../core/catalog';
import { validatePlacement } from '../core/validate';
import { floors, levelView, mergeLevel, levelOfRoom } from '../core/levels';
import { createHash } from 'node:crypto';
import type { Catalog, Snapshot, FurniturePlacement } from '../core/types';

/** O problemă raportată fie de Geometry Engine (coduri twin-core), fie de validatorul aplicației (prefix APP_). */
export type DesignIssue = Omit<Issue, 'code'> & { code: Issue['code'] | `APP_${string}` };

export interface TwinBrief { roomId: string; wants: string[]; budget?: number; retailers?: ('IKEA' | 'Dedeman')[]; accessibility: boolean; replace: boolean }
export const MAX_WANTS = 12;
/** Camerele mai mari decât atât nu sunt locuințe: refuzăm, ca o cerere să nu țină serverul ocupat minute întregi. */
export const MAX_ROOM_SIDE_M = 30;
export const MAX_ROOM_AREA_M2 = 400;

export function checkTwinBrief(input: unknown, snap: Snapshot, groupKeys: string[]): TwinBrief {
  const b = input as any; if (!b || typeof b !== 'object') throw new HttpError(400, 'Brief invalid.');
  const room = typeof b.roomId === 'string' ? floors(snap).flatMap(f => f.rooms).find(r => r.id === b.roomId) : undefined;
  if (!room) throw new HttpError(400, 'Camera nu există în proiect.');
  const w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0;
  if (w > MAX_ROOM_SIDE_M || d > MAX_ROOM_SIDE_M || w * d > MAX_ROOM_AREA_M2) throw new HttpError(422, `Camera e prea mare pentru proiectare automată (maximum ${MAX_ROOM_SIDE_M} m pe latură și ${MAX_ROOM_AREA_M2} m²).`);
  const wants: string[] = Array.isArray(b.wants) ? b.wants.filter((x: unknown): x is string => typeof x === 'string' && groupKeys.includes(x)).slice(0, MAX_WANTS) : [];
  if (!wants.length) throw new HttpError(400, 'Alege cel puțin o categorie de mobilier din catalog.');
  const out: TwinBrief = { roomId: room.id, wants: Array.from(new Set(wants)), accessibility: !!b.accessibility, replace: !!b.replace };
  if (b.budget != null && b.budget !== ''){ const n = Number(b.budget); if (!Number.isFinite(n) || n < 0) throw new HttpError(400, 'Buget invalid.'); out.budget = n; }
  if (Array.isArray(b.retailers)){ const r = b.retailers.filter((s: unknown): s is 'IKEA' | 'Dedeman' => s === 'IKEA' || s === 'Dedeman'); if (r.length) out.retailers = r; }
  return out;
}

export interface PublicAlternative { index: number; title: string; ok: boolean; outcomes: EvaluatedAlternative['result']['outcomes']; measures: EvaluatedAlternative['measures']; boq: EvaluatedAlternative['boq']; issues: DesignIssue[]; placements: FurniturePlacement[]; dsl: DesignDsl }
interface StoredAlternative { index: number; title: string; dsl: DesignDsl; ok: boolean; issues: DesignIssue[] }

/** Amprenta întregii case: la un singur nivel e chiar amprenta twin-ului (propunerile vechi rămân valabile); cu etaje,
 *  cuprinde fiecare nivel și scările, ca orice schimbare, pe orice nivel, să facă propunerea stale. */
export function houseFingerprint(snap: Snapshot, cat: Catalog): string {
  if (!snap.levels?.length) return fingerprint(snapshotToTwin(snap, cat));
  const parts = floors(snap).map((f, i) => ({ twin: fingerprint(snapshotToTwin(levelView(snap, i), cat)), stairs: f.stairs ?? [], name: f.name }));
  return createHash('sha256').update(JSON.stringify(parts)).digest('hex');
}
/** Proiectarea lucrează pe nivelul camerei vizate; restul casei se pune la loc neschimbat (core/levels.ts). */
const levelFor = (snap: Snapshot, roomId: string) => Math.max(0, levelOfRoom(snap, roomId));

/** Cu `replace`, piesele din camera vizată sunt scoase ca solver-ul să re-aşeze camera; altfel se adaugă peste ele. */
const baseFor = (snap: Snapshot, brief: TwinBrief): Snapshot => brief.replace ? { ...snap, placements: snap.placements.filter(p => p.roomId !== brief.roomId) } : snap;

/** Proiectul complet după o alternativă: piesele twin-ului plus cele pe care twin-ul nu le poate reprezenta (păstrate). */
function nextSnapshot(snap: Snapshot, base: Snapshot, cat: Catalog, twinPlacements: Parameters<typeof placementsFromTwin>[2], ids?: Map<string, string>): Snapshot {
  return { ...snap, placements: [...placementsFromTwin(base, cat, twinPlacements, ids), ...untwinnablePlacements(base, cat)] };
}

/** Validatorul aplicației (uși, ferestre, spațiu de circulație cu direcția feței) rămâne poartă și pentru design. */
function appIssues(next: Snapshot, cat: Catalog, roomId: string): DesignIssue[] {
  const out: DesignIssue[] = [], seen = new Set<string>();
  for (const p of next.placements.filter(x => x.roomId === roomId)) for (const i of validatePlacement(next, cat, p)){
    const key = `${p.id}:${i.code}:${i.message}`; if (seen.has(key)) continue; seen.add(key);
    out.push({ severity: i.severity, code: `APP_${i.code}`, refs: [p.id], message: i.message });
  }
  return out;
}

async function solveBrief(snap: Snapshot, cat: Catalog, brief: TwinBrief){
  const base = baseFor(snap, brief), twin = snapshotToTwin(base, cat), items = twinCatalogItems(cat), catalog = catalogFromItems(items);
  const coreBrief: Brief = { roomId: brief.roomId, wants: brief.wants, accessibility: brief.accessibility, ...(brief.budget != null ? { budget: brief.budget } : {}), ...(brief.retailers ? { retailers: brief.retailers } : {}) };
  const provider = new RulesDesignProvider(ROLE_ALIASES);
  const drafts = await Promise.all(([0, 1, 2] as const).map(v => draft(provider, coreBrief, { twin, catalog, catalogItems: items }, v)));
  const dsls = drafts.flatMap(d => d.dsl ? [d.dsl] : []);
  if (!dsls.length) throw new HttpError(422, 'Nicio variantă posibilă: catalogul nu are produse pentru categoriile cerute la magazinele alese.');
  // Bugetul din brief e al camerei vizate: BOQ-ul variantei se calculează doar pe piesele din acea cameră.
  const alts: EvaluatedAlternative[] = solveAlternatives(dsls, twin, catalog).map(a => ({ ...a,
    boq: evaluateBoq({ ...a.result.twin, placements: a.result.twin.placements.filter(p => p.roomId === brief.roomId) }, catalog, brief.budget) }));
  return { base, dsls, alts };
}

export async function generateDesign(owner: string, id: string, input: unknown){
  const p = await getProject(owner, id), cat = await getCatalog();
  const brief = checkTwinBrief(input, p.draft, Object.keys(groups(cat))), L = levelFor(p.draft, brief.roomId), view = levelView(p.draft, L);
  const { base, dsls, alts } = await solveBrief(view, cat, brief);
  // Amprenta de bază acoperă tot proiectul: orice modificare ulterioară, în orice cameră, face propunerea stale.
  const baseFingerprint = houseFingerprint(p.draft, cat);
  const alternatives: PublicAlternative[] = alts.map(a => {
    const ids = new Map<string, string>(), nextView = a.result.ok ? nextSnapshot(view, base, cat, a.result.twin, ids) : null;
    const next = nextView ? mergeLevel(p.draft, L, nextView) : null;
    // Clientul vede doar id-urile din proiect: BOQ-ul și referințele problemelor trec prin aceeași corespondență.
    const idOf = (x: string) => ids.get(x) ?? x;
    const coreIssues = a.issues.map(i => ({ ...i, refs: i.refs.map(idOf) }));
    const issues: DesignIssue[] = nextView ? [...coreIssues, ...appIssues(nextView, cat, brief.roomId)] : coreIssues;
    const ok = a.result.ok && !issues.some(i => i.severity === 'ERROR');
    const boq = { ...a.boq, items: a.boq.items.map(i => ({ ...i, placementId: idOf(i.placementId) })) };
    return { index: a.index, title: a.title, ok, outcomes: a.result.outcomes, measures: a.measures, boq, issues, placements: next ? next.placements : [], dsl: dsls[a.index]! };
  });
  const stored: StoredAlternative[] = alternatives.map(a => ({ index: a.index, title: a.title, dsl: a.dsl, ok: a.ok, issues: a.issues }));
  const { q } = await getDb();
  const { rows } = await q('insert into design_proposals(project_id, base_fingerprint, brief, alternatives) values($1,$2,$3,$4) returning id, created_at', [id, baseFingerprint, JSON.stringify(brief), JSON.stringify(stored)]);
  return { id: rows[0].id as string, createdAt: rows[0].created_at, baseFingerprint, brief, source: 'rules', aiGenerated: false,
    notes: ['Variantele sunt generate de motorul de reguli pe Digital Twin (fără AI). Pozițiile sunt calculate de Geometry Engine și verificate și de validatorul aplicației; AI-ul, când va fi activat, va produce doar DSL.'], alternatives };
}

async function proposalOf(id: string, pid: string){
  if (!/^[0-9a-f-]{36}$/i.test(pid)) throw new HttpError(404, 'Propunere inexistentă.');
  const { q } = await getDb(); const { rows } = await q('select * from design_proposals where id=$1 and project_id=$2', [pid, id]);
  if (!rows[0]) throw new HttpError(404, 'Propunere inexistentă.'); return rows[0];
}

export async function listDesigns(owner: string, id: string){
  const p = await getProject(owner, id), cat = await getCatalog(); const { q } = await getDb();
  const { rows } = await q('select id, base_fingerprint, brief, decisions, applied_index, created_at from design_proposals where project_id=$1 order by created_at desc limit 20', [id]);
  const current = houseFingerprint(p.draft, cat);
  return rows.map(r => ({ id: r.id, createdAt: r.created_at, brief: r.brief as TwinBrief, decisions: r.decisions, appliedIndex: r.applied_index,
    status: r.applied_index != null ? 'APPLIED' : r.base_fingerprint === current ? 'PREVIEW' : 'STALE' }));
}

/** Aplică sau respinge o alternativă. Aplicarea revalidează pe proiectul curent și refuză propunerile stale (409). */
export async function decideDesign(owner: string, id: string, pid: string, index: number, action: 'apply' | 'reject', confirmWarnings: boolean){
  const p = await getProject(owner, id), cat = await getCatalog(), row = await proposalOf(id, pid);
  const alt = (row.alternatives as StoredAlternative[]).find(a => a.index === index);
  if (!alt) throw new HttpError(404, 'Varianta nu există în propunere.');
  const { q } = await getDb(), key = String(index), now = new Date().toISOString();
  if (action === 'reject'){
    const r = await q("update design_proposals set decisions = decisions || $1::jsonb where id=$2 and not (decisions ? $3) and coalesce(applied_index, -1) <> $4 returning id",
      [JSON.stringify({ [key]: { decision: 'rejected', at: now } }), pid, key, index]);
    if (!r.rows[0]) throw new HttpError(409, 'Varianta a fost deja decisă.');
    return { ok: true };
  }
  if (row.applied_index != null || (row.decisions ?? {})[key]) throw new HttpError(409, 'Varianta a fost deja decisă.');
  if (houseFingerprint(p.draft, cat) !== row.base_fingerprint) throw new HttpError(409, 'Proiectul s-a schimbat de când a fost generată propunerea; generează din nou.', [{ code: 'STALE', severity: 'ERROR', message: 'Propunere stale.' }]);
  const brief = row.brief as TwinBrief, L = levelFor(p.draft, brief.roomId), view = levelView(p.draft, L), base = baseFor(view, brief);
  const twin = snapshotToTwin(base, cat), catalog = catalogFromItems(twinCatalogItems(cat));
  const core = createProject(id, twin);
  const prop = preview(core, pid, alt.dsl, catalog, 'rules');
  const out = accept(core, prop, { confirmWarnings: true }); // poarta de avertismente e aplicată mai jos, pe ambele validatoare
  if (!out.accepted) throw new HttpError(409, 'Varianta nu poate fi aplicată pe proiectul curent.', out.validation?.issues ?? out.issues ?? []);
  const ids = new Map<string, string>(), idOf = (x: string) => ids.get(x) ?? x;
  const next = checkSnapshot(structuredClone(mergeLevel(p.draft, L, nextSnapshot(view, base, cat, out.project.twin, ids))));
  const coreIssues = out.validation.issues.map(i => ({ ...i, refs: i.refs.map(idOf) }));
  const issues: DesignIssue[] = [...coreIssues, ...appIssues(levelView(next, L), cat, brief.roomId)];
  if (issues.some(i => i.severity === 'ERROR')) throw new HttpError(409, 'Varianta nu poate fi aplicată pe proiectul curent.', issues);
  // Revizia de după aplicare cere un proiect fără erori în toate camerele: verificăm înainte de a scrie ceva.
  const blocking = projectErrors(next, cat);
  if (blocking.length) throw new HttpError(409, 'Proiectul are erori în afara variantei (de ex. produse scoase din catalog); rezolvă-le înainte de a aplica.', blocking);
  if (issues.length && !confirmWarnings) throw new HttpError(409, 'Varianta are avertismente; confirmă-le înainte de aplicare.', issues);
  // O singură tranzacție: claim pe propunere, compare-and-swap pe draft (care dă și numărul reviziei), decizia și
  // revizia scrisă din `next`. Orice eșec face rollback complet: niciun draft schimbat fără revizie, niciun claim rămas.
  const { tx } = await getDb();
  const revision = await tx(async q => {
    const claim = await q('update design_proposals set applied_index=$1 where id=$2 and applied_index is null and not (decisions ? $3) returning id', [index, pid, key]);
    if (!claim.rows[0]) throw new HttpError(409, 'Varianta a fost deja decisă.');
    const cas = await q('update projects set draft=$1, name=$2, current_revision=current_revision+1, updated_at=now() where id=$3 and owner=$4 and draft = $5::jsonb returning current_revision',
      [JSON.stringify(next), next.name, id, owner, JSON.stringify(p.draft)]);
    if (!cas.rows[0]) throw new HttpError(409, 'Proiectul s-a schimbat de când a fost generată propunerea; generează din nou.', [{ code: 'STALE', severity: 'ERROR', message: 'Propunere stale.' }]);
    const n = cas.rows[0].current_revision as number;
    await q('update design_proposals set decisions = decisions || $1::jsonb where id=$2', [JSON.stringify({ [key]: { decision: 'approved', at: now } }), pid]);
    await q('insert into revisions(project_id, number, note, snapshot) values($1,$2,$3,$4)', [id, n, `Varianta ${alt.title} (Digital Twin, motor de reguli) aplicată`, JSON.stringify(next)]);
    return n;
  });
  return { snapshot: next, revision, issues };
}
