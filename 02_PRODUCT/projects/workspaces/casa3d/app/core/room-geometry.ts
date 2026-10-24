import type { Room, RoomRect } from './types';

export type RoomPoint = [number, number];

/** Boundary points are [x,z] metres, without a repeated closing point. Legacy rooms fall back to rect. */
export function roomBoundary(room: Pick<Room, 'rect' | 'boundary'>): RoomPoint[] {
  if (room.boundary && room.boundary.length >= 3) return room.boundary.map(([x, z]) => [x, z]);
  const r = room.rect;
  return [[r.x0, r.z0], [r.x1, r.z0], [r.x1, r.z1], [r.x0, r.z1]];
}

export function polygonArea(points: readonly RoomPoint[]): number {
  if (points.length < 3) return 0;
  let s = 0;
  for (let i = 0; i < points.length; i++) {
    const a = points[i]!, b = points[(i + 1) % points.length]!;
    s += a[0] * b[1] - b[0] * a[1];
  }
  return Math.abs(s) / 2;
}

export function polygonPerimeter(points: readonly RoomPoint[]): number {
  if (points.length < 2) return 0;
  return points.reduce((sum, a, i) => {
    const b = points[(i + 1) % points.length]!;
    return sum + Math.hypot(b[0] - a[0], b[1] - a[1]);
  }, 0);
}

export function roomArea(room: Pick<Room, 'rect' | 'boundary'>): number {
  return room.boundary?.length && room.boundary.length >= 3 ? polygonArea(room.boundary) : (room.rect.x1 - room.rect.x0) * (room.rect.z1 - room.rect.z0);
}

export function roomPerimeter(room: Pick<Room, 'rect' | 'boundary'>): number {
  return room.boundary?.length && room.boundary.length >= 3 ? polygonPerimeter(room.boundary) : 2 * ((room.rect.x1 - room.rect.x0) + (room.rect.z1 - room.rect.z0));
}

export function roomCentroid(room: Pick<Room, 'rect' | 'boundary'>): RoomPoint {
  const pts = roomBoundary(room);
  const signed = pts.reduce((s, p, i) => { const q = pts[(i + 1) % pts.length]!; return s + p[0] * q[1] - q[0] * p[1]; }, 0);
  if (Math.abs(signed) < 1e-9) return [(room.rect.x0 + room.rect.x1) / 2, (room.rect.z0 + room.rect.z1) / 2];
  let x = 0, z = 0;
  for (let i = 0; i < pts.length; i++) { const p = pts[i]!, q = pts[(i + 1) % pts.length]!, c = p[0] * q[1] - q[0] * p[1]; x += (p[0] + q[0]) * c; z += (p[1] + q[1]) * c; }
  return [x / (3 * signed), z / (3 * signed)];
}

export function roomBoundaryPath(room: Pick<Room, 'rect' | 'boundary'>): string {
  return roomBoundary(room).map(([x, z], i) => `${i ? 'L' : 'M'}${x} ${z}`).join(' ') + ' Z';
}

/** Move/resize a boundary by affine scaling from its old bounding rectangle to the new rectangle. */
export function transformBoundary(boundary: readonly RoomPoint[] | undefined, oldRect: RoomRect, nextRect: RoomRect): RoomPoint[] | undefined {
  if (!boundary?.length) return undefined;
  const sx = (nextRect.x1 - nextRect.x0) / Math.max(1e-9, oldRect.x1 - oldRect.x0);
  const sz = (nextRect.z1 - nextRect.z0) / Math.max(1e-9, oldRect.z1 - oldRect.z0);
  return boundary.map(([x, z]) => [
    Math.round((nextRect.x0 + (x - oldRect.x0) * sx) * 1000) / 1000,
    Math.round((nextRect.z0 + (z - oldRect.z0) * sz) * 1000) / 1000,
  ]);
}

/** Even-odd point-in-polygon test, with points on edges counted as inside. */
export function pointInRoom(room: Pick<Room, 'rect' | 'boundary'>, x: number, z: number): boolean {
  const pts = roomBoundary(room);
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const a = pts[i]!, b = pts[j]!;
    const cross = (x - a[0]) * (b[1] - a[1]) - (z - a[1]) * (b[0] - a[0]);
    const dot = (x - a[0]) * (x - b[0]) + (z - a[1]) * (z - b[1]);
    if (Math.abs(cross) < 1e-8 && dot <= 1e-8) return true;
    if ((a[1] > z) !== (b[1] > z) && x < ((b[0] - a[0]) * (z - a[1])) / (b[1] - a[1]) + a[0]) inside = !inside;
  }
  return inside;
}
