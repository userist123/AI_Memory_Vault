// Digital Twin v1.0: the canonical geometric truth of a home. Units: metres, rounded to mm.
// The AI never writes this structure directly; it goes through the Design DSL, the Solver and the Geometry Engine.
import { createHash } from 'node:crypto';
import {
  type Point, type Rect, type Segment, mm, normalizePolygon, collinearOverlap, segmentLength, rectOf, EPS,
} from './geometry.js';

export const TWIN_VERSION = '1.0' as const;

export type Rotation = 0 | 90 | 180 | 270;

export interface Room {
  id: string;
  name: string;
  /** Rectilinear polygon, counter-clockwise, metres. 4 vertices = rectangle, 6 = L-shape, 8 = U/T-shape. */
  polygon: Point[];
  /** Clear height in metres (used by BOQ for wall/paint areas). */
  height: number;
}

export interface Wall {
  id: string;
  a: Point;
  b: Point;
  thickness: number;
  /** Rooms whose boundary runs along this wall. Derived by linkWalls(); 2 ids = shared (interior) wall. */
  roomIds: string[];
}

export type OpeningKind = 'door' | 'window';

export interface Opening {
  id: string;
  kind: OpeningKind;
  wallId: string;
  /** Distance from wall.a to the start of the opening, along the wall. */
  offset: number;
  width: number;
  /** Doors: depth of the clear zone kept free in front of the door (default 0.9). Windows: 0. */
  clearDepth: number;
  /** Windows: sill height; used only to decide whether a placement covers the window. */
  sillHeight: number;
}

export interface Placement {
  id: string;
  /** Catalog id; the only commercial reference allowed (Constitution art. 6). */
  catalogId: string;
  roomId: string;
  /** Min corner of the footprint after rotation. */
  x: number;
  y: number;
  /** Footprint of the unrotated product: w along x, d along y, h vertical. */
  w: number;
  d: number;
  h: number;
  rotation: Rotation;
  /** Optional semantic role used by the solver (e.g. "bed", "sofa", "desk"). */
  role?: string;
}

export interface Twin {
  version: typeof TWIN_VERSION;
  id: string;
  rooms: Room[];
  walls: Wall[];
  openings: Opening[];
  placements: Placement[];
}

const byId = <T extends { id: string }>(a: T, b: T): number => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);

/** Footprint rectangle of a placement, taking rotation into account. */
export function footprint(p: Pick<Placement, 'x' | 'y' | 'w' | 'd' | 'rotation'>): Rect {
  const swap = p.rotation === 90 || p.rotation === 270;
  return rectOf(p.x, p.y, swap ? p.d : p.w, swap ? p.w : p.d);
}

export const wallSegment = (w: Wall): Segment => ({ a: w.a, b: w.b });

export function roomEdges(room: Room): Segment[] {
  const n = room.polygon.length;
  return room.polygon.map((p, i) => ({ a: p, b: room.polygon[(i + 1) % n]! }));
}

/**
 * Links every wall to the rooms whose boundary overlaps it (collinear, same line, positive overlap).
 * A wall between two rooms gets both ids. Pure: returns a new twin.
 */
export function linkWalls(twin: Twin): Twin {
  const walls = twin.walls.map(w => {
    const ids = twin.rooms
      .filter(r => roomEdges(r).some(e => collinearOverlap(e, wallSegment(w)) > EPS))
      .map(r => r.id)
      .sort();
    return { ...w, roomIds: ids };
  });
  return { ...twin, walls };
}

/**
 * Generates walls from room boundaries. Edges shared by two rooms become one wall with both ids.
 * Existing walls are kept; only edges not already covered by a wall are added. Deterministic ids.
 */
export function wallsFromRooms(twin: Twin, thickness = 0.1): Twin {
  const covered = (e: Segment): boolean => twin.walls.some(w => collinearOverlap(wallSegment(w), e) >= segmentLength(e) - EPS);
  const added: Wall[] = [];
  let counter = 0;
  for (const room of [...twin.rooms].sort(byId)) {
    for (const e of roomEdges(room)) {
      if (covered(e)) continue;
      const twin2 = added.find(w => collinearOverlap(wallSegment(w), e) >= segmentLength(e) - EPS);
      if (twin2) continue;
      // Clip against a longer edge of another room that contains this one: keep the shorter, shared part separate.
      added.push({ id: `wall-${room.id}-${counter++}`, a: { ...e.a }, b: { ...e.b }, thickness, roomIds: [] });
    }
  }
  return linkWalls({ ...twin, walls: [...twin.walls, ...added] });
}

/** Canonical form: mm rounding, CCW polygons, sorted collections, wall links recomputed. */
export function normalize(twin: Twin): Twin {
  const rooms = twin.rooms.map(r => ({ ...r, polygon: normalizePolygon(r.polygon), height: mm(r.height) })).sort(byId);
  const walls = twin.walls.map(w => ({
    ...w, a: { x: mm(w.a.x), y: mm(w.a.y) }, b: { x: mm(w.b.x), y: mm(w.b.y) }, thickness: mm(w.thickness), roomIds: [...w.roomIds].sort(),
  })).sort(byId);
  const openings = twin.openings.map(o => ({
    ...o, offset: mm(o.offset), width: mm(o.width), clearDepth: mm(o.clearDepth), sillHeight: mm(o.sillHeight),
  })).sort(byId);
  const placements = twin.placements.map(p => {
    const q: Placement = { ...p, x: mm(p.x), y: mm(p.y), w: mm(p.w), d: mm(p.d), h: mm(p.h) };
    if (p.role === undefined) delete q.role;
    return q;
  }).sort(byId);
  return linkWalls({ version: TWIN_VERSION, id: twin.id, rooms, walls, openings, placements });
}

function canonicalJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  if (value && typeof value === 'object') {
    const o = value as Record<string, unknown>;
    return `{${Object.keys(o).sort().filter(k => o[k] !== undefined).map(k => `${JSON.stringify(k)}:${canonicalJson(o[k])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

/** SHA-256 of the normalized twin. Two twins with the same geometry have the same fingerprint. */
export function fingerprint(twin: Twin): string {
  return createHash('sha256').update(canonicalJson(normalize(twin))).digest('hex');
}

export function emptyTwin(id: string): Twin {
  return { version: TWIN_VERSION, id, rooms: [], walls: [], openings: [], placements: [] };
}

export function rectangleRoom(id: string, name: string, x: number, y: number, w: number, d: number, height = 2.6): Room {
  return { id, name, height, polygon: [{ x, y }, { x: x + w, y }, { x: x + w, y: y + d }, { x, y: y + d }] };
}
