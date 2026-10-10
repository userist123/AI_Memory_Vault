// Semantic Solver: turns a validated Design DSL into candidate geometry through the Geometry Engine.
// Deterministic: same twin + same DSL + same catalog = same result. Never trusts the AI with positions.
import { type Twin, type Placement, type Rotation, footprint, normalize } from './twin.js';
import { findPosition, validate, type ValidationResult, type PlacementRequest, type Issue } from './engine.js';
import type { Catalog } from './catalog.js';
import type { DesignDsl, Operation, Constraint } from './dsl.js';

export interface OperationOutcome {
  index: number;
  op: Operation['op'];
  ref: string;              // ADD ref or placementId
  status: 'APPLIED' | 'FAILED';
  placementId?: string;
  reason?: string;
}

export interface SolveResult {
  ok: boolean;                    // every operation applied and the final twin has no ERROR
  twin: Twin;                     // candidate twin (normalized); equals the input when !ok
  outcomes: OperationOutcome[];
  validation: ValidationResult;   // of the candidate twin
}

const PLACEMENT_ID_PREFIX = 'ai-';

function requestFrom(id: string, catalogId: string, roomId: string, role: string | undefined, constraints: Constraint[],
  refMap: Map<string, string>, catalog: Catalog): PlacementRequest | string {
  const item = catalog.get(catalogId);
  if (!item) return `Produs inexistent in catalog: ${catalogId}.`;
  const req: PlacementRequest = { id, catalogId, roomId, w: item.w, d: item.d, h: item.h };
  const effectiveRole = role ?? item.role; if (effectiveRole) req.role = effectiveRole;
  const keepClear: { placementId: string; distance: number }[] = [];
  for (const c of constraints) {
    switch (c.type) {
      case 'near': req.near = refMap.get(c.ref) ?? c.ref; break;
      case 'alignedWith': req.alignedWith = refMap.get(c.ref) ?? c.ref; break;
      case 'keepClear': keepClear.push({ placementId: refMap.get(c.ref) ?? c.ref, distance: c.distance }); break;
      case 'againstWall': req.againstWall = c.wallId ? [c.wallId] : true; break;
      case 'orientation': req.rotations = [c.rotation]; break;
    }
  }
  if (keepClear.length) req.keepClear = keepClear;
  return req;
}

export function solve(dsl: DesignDsl, base: Twin, catalog: Catalog): SolveResult {
  let twin: Twin = normalize(base);
  const outcomes: OperationOutcome[] = [];
  const refMap = new Map<string, string>(); // DSL ref -> placement id
  let counter = 0;
  const fail = (index: number, op: Operation, ref: string, reason: string): SolveResult => {
    outcomes.push({ index, op: op.op, ref, status: 'FAILED', reason });
    const normalized = normalize(base);
    return { ok: false, twin: normalized, outcomes, validation: validate(normalized) };
  };

  for (const [index, op] of dsl.operations.entries()) {
    switch (op.op) {
      case 'ADD': {
        const id = `${PLACEMENT_ID_PREFIX}${slug(op.ref)}-${++counter}`;
        const req = requestFrom(id, op.catalogId, op.roomId, op.role, op.constraints ?? [], refMap, catalog);
        if (typeof req === 'string') return fail(index, op, op.ref, req);
        const found = findPosition(twin, req);
        if (!found) return fail(index, op, op.ref, `DOES_NOT_FIT: ${op.catalogId} nu are loc valid in ${op.roomId}.`);
        twin = { ...twin, placements: [...twin.placements, found.placement] };
        refMap.set(op.ref, id);
        outcomes.push({ index, op: 'ADD', ref: op.ref, status: 'APPLIED', placementId: id });
        break;
      }
      case 'REMOVE': {
        twin = { ...twin, placements: twin.placements.filter(p => p.id !== op.placementId) };
        outcomes.push({ index, op: 'REMOVE', ref: op.placementId, status: 'APPLIED', placementId: op.placementId });
        break;
      }
      case 'REPLACE': {
        const old = twin.placements.find(p => p.id === op.placementId)!;
        const item = catalog.get(op.catalogId);
        if (!item) return fail(index, op, op.placementId, `Produs inexistent in catalog: ${op.catalogId}.`);
        // Keep the position and rotation when the new footprint still fits; otherwise re-solve near the old spot.
        const kept: Placement = { ...old, catalogId: item.id, w: item.w, d: item.d, h: item.h };
        const without = { ...twin, placements: twin.placements.filter(p => p.id !== old.id) };
        const direct = validate({ ...without, placements: [...without.placements, kept] });
        if (direct.ok) { twin = { ...without, placements: [...without.placements, kept] }; }
        else {
          const anchor: Placement = { ...old, id: `${old.id}__anchor` };
          const req = requestFrom(old.id, item.id, old.roomId, old.role, [{ type: 'near', ref: anchor.id }], refMap, catalog);
          if (typeof req === 'string') return fail(index, op, op.placementId, req);
          const found = findPosition({ ...without, placements: [...without.placements, anchor] }, req);
          if (!found) return fail(index, op, op.placementId, `DOES_NOT_FIT: ${item.id} nu incape in locul lui ${old.catalogId}.`);
          twin = { ...without, placements: [...without.placements, found.placement] };
        }
        outcomes.push({ index, op: 'REPLACE', ref: op.placementId, status: 'APPLIED', placementId: old.id });
        break;
      }
      case 'MOVE': {
        const old = twin.placements.find(p => p.id === op.placementId)!;
        const without = { ...twin, placements: twin.placements.filter(p => p.id !== old.id) };
        const req = requestFrom(old.id, old.catalogId, old.roomId, old.role, op.constraints, refMap, catalog);
        if (typeof req === 'string') return fail(index, op, op.placementId, req);
        if (!req.rotations) req.rotations = [old.rotation, ...([0, 90, 180, 270] as Rotation[]).filter(r => r !== old.rotation)];
        const found = findPosition(without, req);
        if (!found) return fail(index, op, op.placementId, `DOES_NOT_FIT: ${old.catalogId} nu poate fi mutat cu aceste constrangeri.`);
        twin = { ...without, placements: [...without.placements, found.placement] };
        outcomes.push({ index, op: 'MOVE', ref: op.placementId, status: 'APPLIED', placementId: old.id });
        break;
      }
    }
  }
  const candidate = normalize(twin);
  const validation = validate(candidate);
  return { ok: validation.ok, twin: candidate, outcomes, validation };
}

const slug = (s: string): string => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 24) || 'item';

export interface RoomMeasure { roomId: string; area: number; usedArea: number; freeRatio: number; items: number }

export interface Measures {
  items: number;
  usedArea: number;
  rooms: RoomMeasure[];
  warnings: number;
  errors: number;
}

export function measure(twin: Twin, validation: ValidationResult = validate(twin)): Measures {
  const rooms: RoomMeasure[] = twin.rooms.map(r => {
    const area = Math.abs(polygonAreaOf(r.polygon));
    const inRoom = twin.placements.filter(p => p.roomId === r.id);
    const used = inRoom.reduce((s, p) => { const f = footprint(p); return s + f.w * f.d; }, 0);
    return { roomId: r.id, area: round3(area), usedArea: round3(used), freeRatio: area > 0 ? round3((area - used) / area) : 0, items: inRoom.length };
  });
  return {
    items: twin.placements.length,
    usedArea: round3(rooms.reduce((s, r) => s + r.usedArea, 0)),
    rooms,
    warnings: validation.issues.filter(i => i.severity === 'WARNING').length,
    errors: validation.issues.filter(i => i.severity === 'ERROR').length,
  };
}

function polygonAreaOf(poly: readonly { x: number; y: number }[]): number {
  let s = 0;
  for (let i = 0; i < poly.length; i++) { const a = poly[i]!, b = poly[(i + 1) % poly.length]!; s += a.x * b.y - b.x * a.y; }
  return s / 2;
}
const round3 = (v: number): number => Math.round(v * 1000) / 1000;

export const MAX_ALTERNATIVES = 3;

export interface Alternative {
  index: number;
  title: string;
  result: SolveResult;
  measures: Measures;
  issues: Issue[];
}

/**
 * Solves up to three DSL alternatives for the same base twin and reports measures and issues for each.
 * It never picks a winner: the comparison is for the user (Constitution art. 4).
 */
export function solveAlternatives(dsls: readonly DesignDsl[], base: Twin, catalog: Catalog): Alternative[] {
  if (dsls.length === 0 || dsls.length > MAX_ALTERNATIVES) throw new RangeError(`Intre 1 si ${MAX_ALTERNATIVES} alternative.`);
  return dsls.map((dsl, index) => {
    const result = solve(dsl, base, catalog);
    return { index, title: dsl.title ?? `Varianta ${index + 1}`, result, measures: measure(result.twin, result.validation), issues: result.validation.issues };
  });
}
