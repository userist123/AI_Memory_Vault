// Geometry Engine: validates a Digital Twin and places products deterministically.
// ERROR blocks; WARNING needs explicit user confirmation (Constitution art. 5).
import {
  type Rect, EPS, mm, polygonProblems, rectInsidePolygon, rectsOverlap, rectIntersectionArea, polygonArea,
  rectArea, segmentIsAxisAligned, segmentLength, bandAlong, rectTouchesSegment, polygonBounds, rectOf,
} from './geometry.js';
import { type Twin, type Placement, type Opening, type Wall, type Rotation, footprint, wallSegment, linkWalls } from './twin.js';

export type Severity = 'ERROR' | 'WARNING';

export type IssueCode =
  | 'ROOM_POLYGON_INVALID' | 'ROOM_OVERLAP' | 'ROOM_HEIGHT_INVALID'
  | 'WALL_NOT_AXIS_ALIGNED' | 'WALL_ZERO_LENGTH' | 'WALL_UNLINKED'
  | 'OPENING_WALL_MISSING' | 'OPENING_OUTSIDE_WALL' | 'OPENING_OVERLAP'
  | 'PLACEMENT_ROOM_MISSING' | 'PLACEMENT_INVALID_SIZE' | 'DOES_NOT_FIT' | 'PLACEMENT_OVERLAP'
  | 'PLACEMENT_BLOCKS_DOOR' | 'PLACEMENT_COVERS_WINDOW' | 'CIRCULATION_LOW';

export interface Issue {
  severity: Severity;
  code: IssueCode;
  /** Ids of the elements involved, most specific first. */
  refs: string[];
  message: string;
}

export interface ValidationResult {
  ok: boolean;             // no ERROR
  needsConfirmation: boolean; // at least one WARNING
  issues: Issue[];
}

export const DOOR_CLEAR_DEPTH = 0.9;
export const MIN_FREE_AREA_RATIO = 0.3;
export const PLACEMENT_GRID = 0.05;

const err = (code: IssueCode, refs: string[], message: string): Issue => ({ severity: 'ERROR', code, refs, message });
const warn = (code: IssueCode, refs: string[], message: string): Issue => ({ severity: 'WARNING', code, refs, message });

export function openingZone(opening: Opening, wall: Wall): Rect {
  const depth = opening.kind === 'door' ? Math.max(opening.clearDepth, 0) : 0.05;
  return bandAlong(wallSegment(wall), opening.offset, opening.width, depth);
}

export function validate(input: Twin): ValidationResult {
  const twin = linkWalls(input);
  const issues: Issue[] = [];
  const roomById = new Map(twin.rooms.map(r => [r.id, r]));
  const wallById = new Map(twin.walls.map(w => [w.id, w]));

  for (const room of twin.rooms) {
    const problems = polygonProblems(room.polygon);
    if (problems.length) issues.push(err('ROOM_POLYGON_INVALID', [room.id], `Camera ${room.name}: ${problems.join(', ')}.`));
    if (!(room.height > 0)) issues.push(err('ROOM_HEIGHT_INVALID', [room.id], `Camera ${room.name}: inaltime invalida.`));
  }
  const validRooms = twin.rooms.filter(r => polygonProblems(r.polygon).length === 0);
  for (let i = 0; i < validRooms.length; i++) for (let j = i + 1; j < validRooms.length; j++) {
    const a = validRooms[i]!, b = validRooms[j]!;
    if (roomsOverlap(a.polygon, b.polygon)) issues.push(err('ROOM_OVERLAP', [a.id, b.id], `Camerele ${a.name} si ${b.name} se suprapun.`));
  }

  for (const wall of twin.walls) {
    const s = wallSegment(wall);
    if (segmentLength(s) <= EPS) issues.push(err('WALL_ZERO_LENGTH', [wall.id], 'Perete de lungime zero.'));
    else if (!segmentIsAxisAligned(s)) issues.push(err('WALL_NOT_AXIS_ALIGNED', [wall.id], 'Peretele nu este aliniat la axe.'));
    else if (wall.roomIds.length === 0) issues.push(warn('WALL_UNLINKED', [wall.id], 'Peretele nu margineste nicio camera.'));
  }

  const zones: { opening: Opening; zone: Rect; wall: Wall }[] = [];
  for (const o of twin.openings) {
    const wall = wallById.get(o.wallId);
    if (!wall) { issues.push(err('OPENING_WALL_MISSING', [o.id, o.wallId], 'Golul se refera la un perete inexistent.')); continue; }
    const len = segmentLength(wallSegment(wall));
    if (o.offset < -EPS || o.width <= EPS || o.offset + o.width > len + EPS) {
      issues.push(err('OPENING_OUTSIDE_WALL', [o.id, wall.id], 'Golul iese din perete.')); continue;
    }
    zones.push({ opening: o, zone: openingZone(o, wall), wall });
  }
  for (let i = 0; i < zones.length; i++) for (let j = i + 1; j < zones.length; j++) {
    const a = zones[i]!, b = zones[j]!;
    if (a.wall.id === b.wall.id) {
      const lo = Math.max(a.opening.offset, b.opening.offset), hi = Math.min(a.opening.offset + a.opening.width, b.opening.offset + b.opening.width);
      if (hi - lo > EPS) issues.push(err('OPENING_OVERLAP', [a.opening.id, b.opening.id], 'Doua goluri se suprapun pe acelasi perete.'));
    }
  }

  const feet = new Map<string, Rect>();
  for (const p of twin.placements) {
    if (!(p.w > EPS && p.d > EPS && p.h > EPS)) { issues.push(err('PLACEMENT_INVALID_SIZE', [p.id], 'Dimensiuni invalide.')); continue; }
    const room = roomById.get(p.roomId);
    if (!room) { issues.push(err('PLACEMENT_ROOM_MISSING', [p.id, p.roomId], 'Produsul se refera la o camera inexistenta.')); continue; }
    const f = footprint(p); feet.set(p.id, f);
    if (polygonProblems(room.polygon).length === 0 && !rectInsidePolygon(f, room.polygon)) {
      issues.push(err('DOES_NOT_FIT', [p.id, room.id], `Produsul ${p.catalogId} nu incape in camera ${room.name}.`));
    }
  }
  const placed = twin.placements.filter(p => feet.has(p.id));
  for (let i = 0; i < placed.length; i++) for (let j = i + 1; j < placed.length; j++) {
    const a = placed[i]!, b = placed[j]!;
    if (rectsOverlap(feet.get(a.id)!, feet.get(b.id)!)) issues.push(err('PLACEMENT_OVERLAP', [a.id, b.id], `Produsele ${a.catalogId} si ${b.catalogId} se suprapun.`));
  }
  for (const p of placed) {
    const f = feet.get(p.id)!;
    for (const { opening, zone, wall } of zones) {
      if (!wall.roomIds.includes(p.roomId)) continue;
      if (opening.kind === 'door' && rectsOverlap(f, zone)) issues.push(err('PLACEMENT_BLOCKS_DOOR', [p.id, opening.id], `Produsul ${p.catalogId} blocheaza usa.`));
      if (opening.kind === 'window' && rectsOverlap(f, zone) && p.h > opening.sillHeight + EPS) {
        issues.push(warn('PLACEMENT_COVERS_WINDOW', [p.id, opening.id], `Produsul ${p.catalogId} acopera fereastra.`));
      }
    }
  }
  for (const room of validRooms) {
    const area = polygonArea(room.polygon);
    const used = placed.filter(p => p.roomId === room.id).reduce((s, p) => s + rectArea(feet.get(p.id)!), 0);
    if (area > EPS && (area - used) / area < MIN_FREE_AREA_RATIO) {
      issues.push(warn('CIRCULATION_LOW', [room.id], `Camera ${room.name} are sub ${MIN_FREE_AREA_RATIO * 100}% suprafata libera.`));
    }
  }

  const ok = issues.every(i => i.severity !== 'ERROR');
  return { ok, needsConfirmation: issues.some(i => i.severity === 'WARNING'), issues };
}

function roomsOverlap(a: readonly { x: number; y: number }[], b: readonly { x: number; y: number }[]): boolean {
  // Rectilinear polygons overlap iff some cell of the grid induced by their vertices is inside both.
  const xs = Array.from(new Set([...a, ...b].map(p => p.x))).sort((p, q) => p - q);
  const ys = Array.from(new Set([...a, ...b].map(p => p.y))).sort((p, q) => p - q);
  for (let i = 0; i + 1 < xs.length; i++) for (let j = 0; j + 1 < ys.length; j++) {
    const cell = rectOf(xs[i]!, ys[j]!, xs[i + 1]! - xs[i]!, ys[j + 1]! - ys[j]!);
    if (cell.w <= EPS || cell.d <= EPS) continue;
    if (rectInsidePolygon(cell, a) && rectInsidePolygon(cell, b)) return true;
  }
  return false;
}

export interface PlacementRequest {
  id: string;
  catalogId: string;
  roomId: string;
  w: number; d: number; h: number;
  role?: string;
  /** Allowed rotations, tried in this order. Default [0, 90, 180, 270]. */
  rotations?: Rotation[];
  /** Prefer positions touching a wall (any wall of the room, or the given wall ids). */
  againstWall?: boolean | string[];
  /** Keep the footprint at least this far from the given placement ids' footprints. */
  keepClear?: { placementId: string; distance: number }[];
  /** Prefer positions close to this placement id (minimises centre distance). */
  near?: string;
  /** Align one edge with the given placement (same x or same y of the min corner). */
  alignedWith?: string;
}

export interface PlacementCandidate { placement: Placement; validation: ValidationResult; score: number }

/**
 * Finds a valid position for a product in a room, deterministically: scans a 5 cm grid inside the room's bounds,
 * tries rotations in order, rejects anything that produces an ERROR, and ranks the rest by the semantic
 * preferences (againstWall, near, alignedWith, keepClear). Ties are broken by (y, x, rotation).
 */
export function findPosition(twin: Twin, req: PlacementRequest): PlacementCandidate | null {
  const room = twin.rooms.find(r => r.id === req.roomId);
  if (!room || polygonProblems(room.polygon).length) return null;
  const linked = linkWalls(twin);
  const roomWalls = linked.walls.filter(w => w.roomIds.includes(room.id));
  const wallFilter = Array.isArray(req.againstWall) ? new Set(req.againstWall) : null;
  const bounds = polygonBounds(room.polygon);
  const others = new Map(twin.placements.map(p => [p.id, footprint(p)]));
  const centre = (r: Rect) => ({ x: r.x + r.w / 2, y: r.y + r.d / 2 });
  let best: PlacementCandidate | null = null;

  for (const rotation of req.rotations ?? [0, 90, 180, 270]) {
    const fw = rotation === 90 || rotation === 270 ? req.d : req.w;
    const fd = rotation === 90 || rotation === 270 ? req.w : req.d;
    for (let y = bounds.y; y + fd <= bounds.y + bounds.d + EPS; y = mm(y + PLACEMENT_GRID)) {
      for (let x = bounds.x; x + fw <= bounds.x + bounds.w + EPS; x = mm(x + PLACEMENT_GRID)) {
        const f = rectOf(x, y, fw, fd);
        if (!rectInsidePolygon(f, room.polygon)) continue;
        if ([...others.values()].some(o => rectsOverlap(f, o))) continue;
        let score = 0;
        if (req.againstWall) {
          const touching = roomWalls.filter(w => (!wallFilter || wallFilter.has(w.id)) && rectTouchesSegment(f, wallSegment(w)));
          if (touching.length === 0) continue;
          score -= 10;
        }
        if (req.keepClear) {
          let violated = false;
          for (const k of req.keepClear) {
            const o = others.get(k.placementId); if (!o) continue;
            const grown = rectOf(o.x - k.distance, o.y - k.distance, o.w + 2 * k.distance, o.d + 2 * k.distance);
            if (rectIntersectionArea(f, grown) > EPS) { violated = true; break; }
          }
          if (violated) continue;
        }
        if (req.near) {
          const o = others.get(req.near);
          if (o) { const c1 = centre(f), c2 = centre(o); score += Math.abs(c1.x - c2.x) + Math.abs(c1.y - c2.y); }
        }
        if (req.alignedWith) {
          const o = others.get(req.alignedWith);
          if (o && !(Math.abs(o.x - f.x) <= EPS || Math.abs(o.y - f.y) <= EPS)) score += 5;
        }
        const placement: Placement = { id: req.id, catalogId: req.catalogId, roomId: req.roomId, x, y, w: req.w, d: req.d, h: req.h, rotation };
        if (req.role !== undefined) placement.role = req.role;
        const validation = validate({ ...twin, placements: [...twin.placements, placement] });
        if (!validation.ok) continue;
        score += validation.needsConfirmation ? 1 : 0;
        if (!best || score < best.score - 1e-9) best = { placement, validation, score };
      }
    }
  }
  return best;
}
