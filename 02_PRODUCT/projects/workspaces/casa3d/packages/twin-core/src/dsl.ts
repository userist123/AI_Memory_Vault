// Design DSL 1.1: how the AI (or the rules engine) describes a design change. Declarative, no coordinates.
// Positions are decided by the Solver + Geometry Engine (Constitution art. 2, 3, 7).
import type { Rotation, Twin } from './twin.js';
import type { Catalog } from './catalog.js';

export const DSL_VERSION = '1.1' as const;

export type Constraint =
  | { type: 'near'; ref: string }
  | { type: 'againstWall'; wallId?: string }
  | { type: 'alignedWith'; ref: string }
  | { type: 'keepClear'; ref: string; distance: number }
  | { type: 'orientation'; rotation: Rotation };

export type Operation =
  | { op: 'ADD'; ref: string; roomId: string; catalogId: string; role?: string; constraints?: Constraint[]; reason?: string }
  | { op: 'REMOVE'; placementId: string; reason?: string }
  | { op: 'REPLACE'; placementId: string; catalogId: string; reason?: string }
  | { op: 'MOVE'; placementId: string; constraints: Constraint[]; reason?: string };

export interface DesignDsl {
  version: typeof DSL_VERSION;
  title?: string;
  operations: Operation[];
}

export interface DslError { path: string; code: DslErrorCode; message: string }
export type DslErrorCode =
  | 'NOT_AN_OBJECT' | 'BAD_VERSION' | 'NO_OPERATIONS' | 'TOO_MANY_OPERATIONS' | 'BAD_OPERATION'
  | 'COORDINATES_FORBIDDEN' | 'UNKNOWN_CATALOG_ID' | 'UNKNOWN_PLACEMENT' | 'UNKNOWN_ROOM' | 'UNKNOWN_REF'
  | 'DUPLICATE_REF' | 'BAD_CONSTRAINT';

export const MAX_OPERATIONS = 40;
const FORBIDDEN_KEYS = new Set(['x', 'y', 'z', 'position', 'coordinates', 'coords', 'left', 'top', 'rotationDeg']);
const ROTATIONS = new Set([0, 90, 180, 270]);

/**
 * Validates raw (untrusted) DSL JSON against the structure, the twin and the catalog.
 * Returns the typed DSL when valid; the AI's text is data, never instructions, so nothing here is executed.
 */
export function validateDsl(raw: unknown, twin: Twin, catalog: Catalog): { dsl?: DesignDsl; errors: DslError[] } {
  const errors: DslError[] = [];
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return { errors: [{ path: '$', code: 'NOT_AN_OBJECT', message: 'DSL-ul trebuie sa fie un obiect.' }] };
  const r = raw as Record<string, unknown>;
  if (r['version'] !== DSL_VERSION) errors.push({ path: '$.version', code: 'BAD_VERSION', message: `Versiune asteptata ${DSL_VERSION}.` });
  const ops = r['operations'];
  if (!Array.isArray(ops) || ops.length === 0) return { errors: [...errors, { path: '$.operations', code: 'NO_OPERATIONS', message: 'Lipsesc operatiile.' }] };
  if (ops.length > MAX_OPERATIONS) errors.push({ path: '$.operations', code: 'TOO_MANY_OPERATIONS', message: `Maximum ${MAX_OPERATIONS} operatii.` });

  const placementIds = new Set(twin.placements.map(p => p.id));
  const roomIds = new Set(twin.rooms.map(r => r.id));
  const wallIds = new Set(twin.walls.map(w => w.id));
  const refs = new Set<string>(placementIds);
  const removed = new Set<string>();
  const out: Operation[] = [];

  ops.forEach((o, i) => {
    const path = `$.operations[${i}]`;
    if (!o || typeof o !== 'object') { errors.push({ path, code: 'BAD_OPERATION', message: 'Operatie invalida.' }); return; }
    const op = o as Record<string, unknown>;
    for (const k of Object.keys(op)) if (FORBIDDEN_KEYS.has(k)) errors.push({ path: `${path}.${k}`, code: 'COORDINATES_FORBIDDEN', message: 'AI-ul nu furnizeaza coordonate.' });
    const constraints = parseConstraints(op['constraints'], path, refs, wallIds, errors);
    const reason = typeof op['reason'] === 'string' ? op['reason'] : undefined;
    switch (op['op']) {
      case 'ADD': {
        const ref = str(op['ref']), roomId = str(op['roomId']), catalogId = str(op['catalogId']);
        if (!ref) errors.push({ path: `${path}.ref`, code: 'BAD_OPERATION', message: 'ADD cere ref.' });
        else if (refs.has(ref)) errors.push({ path: `${path}.ref`, code: 'DUPLICATE_REF', message: `Ref duplicat: ${ref}.` });
        if (!roomId || !roomIds.has(roomId)) errors.push({ path: `${path}.roomId`, code: 'UNKNOWN_ROOM', message: `Camera necunoscuta: ${roomId}.` });
        if (!catalogId || !catalog.get(catalogId)) errors.push({ path: `${path}.catalogId`, code: 'UNKNOWN_CATALOG_ID', message: `Produs inexistent in catalog: ${catalogId}.` });
        if (ref && roomId && catalogId) {
          refs.add(ref);
          const item: Operation = { op: 'ADD', ref, roomId, catalogId, constraints };
          const role = str(op['role']); if (role) item.role = role; if (reason) item.reason = reason;
          out.push(item);
        }
        return;
      }
      case 'REMOVE': case 'REPLACE': case 'MOVE': {
        const placementId = str(op['placementId']);
        if (!placementId || !placementIds.has(placementId) || removed.has(placementId)) {
          errors.push({ path: `${path}.placementId`, code: 'UNKNOWN_PLACEMENT', message: `Produs inexistent in proiect: ${placementId}.` }); return;
        }
        if (op['op'] === 'REMOVE') { removed.add(placementId); const it: Operation = { op: 'REMOVE', placementId }; if (reason) it.reason = reason; out.push(it); return; }
        if (op['op'] === 'REPLACE') {
          const catalogId = str(op['catalogId']);
          if (!catalogId || !catalog.get(catalogId)) { errors.push({ path: `${path}.catalogId`, code: 'UNKNOWN_CATALOG_ID', message: `Produs inexistent in catalog: ${catalogId}.` }); return; }
          const it: Operation = { op: 'REPLACE', placementId, catalogId }; if (reason) it.reason = reason; out.push(it); return;
        }
        const it: Operation = { op: 'MOVE', placementId, constraints }; if (reason) it.reason = reason; out.push(it); return;
      }
      default:
        errors.push({ path: `${path}.op`, code: 'BAD_OPERATION', message: `Operatie necunoscuta: ${String(op['op'])}.` });
    }
  });
  if (errors.length) return { errors };
  const dsl: DesignDsl = { version: DSL_VERSION, operations: out };
  if (typeof r['title'] === 'string') dsl.title = r['title'];
  return { dsl, errors: [] };
}

const str = (v: unknown): string | undefined => (typeof v === 'string' && v.length > 0 && v.length <= 120 ? v : undefined);

function parseConstraints(raw: unknown, path: string, refs: Set<string>, wallIds: Set<string>, errors: DslError[]): Constraint[] {
  if (raw === undefined) return [];
  if (!Array.isArray(raw)) { errors.push({ path: `${path}.constraints`, code: 'BAD_CONSTRAINT', message: 'constraints trebuie sa fie o lista.' }); return []; }
  const out: Constraint[] = [];
  raw.forEach((c, j) => {
    const p = `${path}.constraints[${j}]`;
    if (!c || typeof c !== 'object') { errors.push({ path: p, code: 'BAD_CONSTRAINT', message: 'Constrangere invalida.' }); return; }
    const k = c as Record<string, unknown>;
    for (const key of Object.keys(k)) if (FORBIDDEN_KEYS.has(key)) errors.push({ path: `${p}.${key}`, code: 'COORDINATES_FORBIDDEN', message: 'AI-ul nu furnizeaza coordonate.' });
    switch (k['type']) {
      case 'near': case 'alignedWith': {
        const ref = str(k['ref']);
        if (!ref || !refs.has(ref)) { errors.push({ path: `${p}.ref`, code: 'UNKNOWN_REF', message: `Referinta necunoscuta: ${ref}.` }); return; }
        out.push({ type: k['type'], ref }); return;
      }
      case 'keepClear': {
        const ref = str(k['ref']), distance = k['distance'];
        if (!ref || !refs.has(ref)) { errors.push({ path: `${p}.ref`, code: 'UNKNOWN_REF', message: `Referinta necunoscuta: ${ref}.` }); return; }
        if (typeof distance !== 'number' || !(distance > 0) || distance > 5) { errors.push({ path: `${p}.distance`, code: 'BAD_CONSTRAINT', message: 'distance trebuie sa fie intre 0 si 5 m.' }); return; }
        out.push({ type: 'keepClear', ref, distance }); return;
      }
      case 'againstWall': {
        const wallId = str(k['wallId']);
        if (wallId !== undefined && !wallIds.has(wallId)) { errors.push({ path: `${p}.wallId`, code: 'BAD_CONSTRAINT', message: `Perete necunoscut: ${wallId}.` }); return; }
        out.push(wallId ? { type: 'againstWall', wallId } : { type: 'againstWall' }); return;
      }
      case 'orientation': {
        const rot = k['rotation'];
        if (typeof rot !== 'number' || !ROTATIONS.has(rot)) { errors.push({ path: `${p}.rotation`, code: 'BAD_CONSTRAINT', message: 'rotation trebuie sa fie 0, 90, 180 sau 270.' }); return; }
        out.push({ type: 'orientation', rotation: rot as Rotation }); return;
      }
      default:
        errors.push({ path: `${p}.type`, code: 'BAD_CONSTRAINT', message: `Constrangere necunoscuta: ${String(k['type'])}.` });
    }
  });
  return out;
}
