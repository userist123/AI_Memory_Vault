// Deterministic 2D geometry for the Digital Twin. Units: metres. Tolerance: 1 mm.
// Rooms are rectilinear polygons (every edge axis-aligned), which covers rectangles and L/U shapes.

export interface Point { x: number; y: number }
export interface Rect { x: number; y: number; w: number; d: number } // x,y = min corner; w along x, d along y

export const EPS = 0.0005; // half a millimetre: everything is rounded to mm before comparison
export const mm = (v: number): number => Math.round(v * 1000) / 1000;

export const near = (a: number, b: number): boolean => Math.abs(a - b) <= EPS;

export function rectOf(x: number, y: number, w: number, d: number): Rect {
  return { x: mm(x), y: mm(y), w: mm(w), d: mm(d) };
}
export const rectMaxX = (r: Rect): number => r.x + r.w;
export const rectMaxY = (r: Rect): number => r.y + r.d;
export const rectArea = (r: Rect): number => r.w * r.d;

/** Overlap of the open interiors (touching edges do not count). */
export function rectsOverlap(a: Rect, b: Rect): boolean {
  return a.x < rectMaxX(b) - EPS && b.x < rectMaxX(a) - EPS && a.y < rectMaxY(b) - EPS && b.y < rectMaxY(a) - EPS;
}

/** Area of the intersection of two rectangles (0 when they only touch). */
export function rectIntersectionArea(a: Rect, b: Rect): number {
  const w = Math.min(rectMaxX(a), rectMaxX(b)) - Math.max(a.x, b.x);
  const d = Math.min(rectMaxY(a), rectMaxY(b)) - Math.max(a.y, b.y);
  return w > EPS && d > EPS ? w * d : 0;
}

export function rectContainsRect(outer: Rect, inner: Rect): boolean {
  return inner.x >= outer.x - EPS && inner.y >= outer.y - EPS &&
    rectMaxX(inner) <= rectMaxX(outer) + EPS && rectMaxY(inner) <= rectMaxY(outer) + EPS;
}

export type PolygonProblem =
  | 'TOO_FEW_VERTICES' | 'EDGE_NOT_AXIS_ALIGNED' | 'ZERO_LENGTH_EDGE' | 'CONSECUTIVE_COLLINEAR'
  | 'SELF_INTERSECTING' | 'ZERO_AREA';

/** Signed area (shoelace). Positive = counter-clockwise. */
export function signedArea(poly: readonly Point[]): number {
  let s = 0;
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i]!, b = poly[(i + 1) % poly.length]!;
    s += a.x * b.y - b.x * a.y;
  }
  return s / 2;
}

export const polygonArea = (poly: readonly Point[]): number => Math.abs(signedArea(poly));

/** Validates a rectilinear simple polygon. Returns [] when valid. */
export function polygonProblems(poly: readonly Point[]): PolygonProblem[] {
  const problems: PolygonProblem[] = [];
  if (poly.length < 4) return ['TOO_FEW_VERTICES'];
  const n = poly.length;
  for (let i = 0; i < n; i++) {
    const a = poly[i]!, b = poly[(i + 1) % n]!;
    const dx = near(a.x, b.x), dy = near(a.y, b.y);
    if (dx && dy) problems.push('ZERO_LENGTH_EDGE');
    else if (!dx && !dy) problems.push('EDGE_NOT_AXIS_ALIGNED');
  }
  if (problems.length) return Array.from(new Set(problems));
  for (let i = 0; i < n; i++) {
    const a = poly[i]!, b = poly[(i + 1) % n]!, c = poly[(i + 2) % n]!;
    const abVertical = near(a.x, b.x), bcVertical = near(b.x, c.x);
    if (abVertical === bcVertical) { problems.push('CONSECUTIVE_COLLINEAR'); break; }
  }
  if (problems.length) return problems;
  if (polygonArea(poly) <= EPS) return ['ZERO_AREA'];
  // Self-intersection: any two non-adjacent edges that touch (rectilinear edges only).
  for (let i = 0; i < n; i++) {
    for (let j = i + 1; j < n; j++) {
      if (j === i + 1 || (i === 0 && j === n - 1)) continue;
      if (segmentsTouch(poly[i]!, poly[(i + 1) % n]!, poly[j]!, poly[(j + 1) % n]!)) return ['SELF_INTERSECTING'];
    }
  }
  return [];
}

function segmentsTouch(a: Point, b: Point, c: Point, d: Point): boolean {
  const r1 = segmentRect(a, b), r2 = segmentRect(c, d);
  return r1.x <= rectMaxX(r2) + EPS && r2.x <= rectMaxX(r1) + EPS && r1.y <= rectMaxY(r2) + EPS && r2.y <= rectMaxY(r1) + EPS;
}
function segmentRect(a: Point, b: Point): Rect {
  return { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), w: Math.abs(a.x - b.x), d: Math.abs(a.y - b.y) };
}

/** Counter-clockwise copy of the polygon, vertices rounded to mm. */
export function normalizePolygon(poly: readonly Point[]): Point[] {
  const out = poly.map(p => ({ x: mm(p.x), y: mm(p.y) }));
  return signedArea(out) < 0 ? out.reverse() : out;
}

export function polygonBounds(poly: readonly Point[]): Rect {
  const xs = poly.map(p => p.x), ys = poly.map(p => p.y);
  const x = Math.min(...xs), y = Math.min(...ys);
  return { x, y, w: Math.max(...xs) - x, d: Math.max(...ys) - y };
}

/** Point in polygon; points on the boundary count as inside. */
export function pointInPolygon(p: Point, poly: readonly Point[]): boolean {
  const n = poly.length;
  for (let i = 0; i < n; i++) if (pointOnSegment(p, poly[i]!, poly[(i + 1) % n]!)) return true;
  let inside = false;
  for (let i = 0, j = n - 1; i < n; j = i++) {
    const a = poly[i]!, b = poly[j]!;
    const crosses = (a.y > p.y) !== (b.y > p.y);
    if (crosses) {
      const xAt = a.x + ((p.y - a.y) * (b.x - a.x)) / (b.y - a.y);
      if (p.x < xAt) inside = !inside;
    }
  }
  return inside;
}

export function pointOnSegment(p: Point, a: Point, b: Point): boolean {
  const r = segmentRect(a, b);
  return p.x >= r.x - EPS && p.x <= rectMaxX(r) + EPS && p.y >= r.y - EPS && p.y <= rectMaxY(r) + EPS;
}

/** True when an axis-aligned rectangle lies fully inside a rectilinear polygon (boundary allowed). */
export function rectInsidePolygon(r: Rect, poly: readonly Point[]): boolean {
  const corners: Point[] = [
    { x: r.x, y: r.y }, { x: rectMaxX(r), y: r.y }, { x: rectMaxX(r), y: rectMaxY(r) }, { x: r.x, y: rectMaxY(r) },
  ];
  if (!corners.every(c => pointInPolygon(c, poly))) return false;
  // The boundary must not pass through the open interior of the rectangle.
  const n = poly.length;
  for (let i = 0; i < n; i++) {
    const a = poly[i]!, b = poly[(i + 1) % n]!;
    const s = segmentRect(a, b);
    const crossesX = s.x < rectMaxX(r) - EPS && rectMaxX(s) > r.x + EPS;
    const crossesY = s.y < rectMaxY(r) - EPS && rectMaxY(s) > r.y + EPS;
    if (near(a.x, b.x)) { // vertical edge
      if (a.x > r.x + EPS && a.x < rectMaxX(r) - EPS && crossesY) return false;
    } else { // horizontal edge
      if (a.y > r.y + EPS && a.y < rectMaxY(r) - EPS && crossesX) return false;
    }
  }
  return true;
}

export interface Segment { a: Point; b: Point }

export const segmentLength = (s: Segment): number => Math.abs(s.a.x - s.b.x) + Math.abs(s.a.y - s.b.y);
export const segmentIsVertical = (s: Segment): boolean => near(s.a.x, s.b.x);
export const segmentIsAxisAligned = (s: Segment): boolean => near(s.a.x, s.b.x) || near(s.a.y, s.b.y);

/** Length of the overlap of two collinear axis-aligned segments; 0 when not collinear or disjoint. */
export function collinearOverlap(s: Segment, t: Segment): number {
  if (segmentIsVertical(s) !== segmentIsVertical(t)) return 0;
  if (segmentIsVertical(s)) {
    if (!near(s.a.x, t.a.x)) return 0;
    const lo = Math.max(Math.min(s.a.y, s.b.y), Math.min(t.a.y, t.b.y));
    const hi = Math.min(Math.max(s.a.y, s.b.y), Math.max(t.a.y, t.b.y));
    return hi - lo > EPS ? hi - lo : 0;
  }
  if (!near(s.a.y, t.a.y)) return 0;
  const lo = Math.max(Math.min(s.a.x, s.b.x), Math.min(t.a.x, t.b.x));
  const hi = Math.min(Math.max(s.a.x, s.b.x), Math.max(t.a.x, t.b.x));
  return hi - lo > EPS ? hi - lo : 0;
}

/** Point at `offset` metres from `a` along the segment. */
export function pointAlong(s: Segment, offset: number): Point {
  const len = segmentLength(s);
  if (len <= EPS) return { ...s.a };
  const t = offset / len;
  return { x: mm(s.a.x + (s.b.x - s.a.x) * t), y: mm(s.a.y + (s.b.y - s.a.y) * t) };
}

/** Rectangle covering `[offset, offset+width]` along the segment, extended `depth` on both sides. */
export function bandAlong(s: Segment, offset: number, width: number, depth: number): Rect {
  const p = pointAlong(s, offset), q = pointAlong(s, offset + width);
  if (segmentIsVertical(s)) return rectOf(s.a.x - depth, Math.min(p.y, q.y), 2 * depth, Math.abs(q.y - p.y));
  return rectOf(Math.min(p.x, q.x), s.a.y - depth, Math.abs(q.x - p.x), 2 * depth);
}

/** Distance from a rectangle edge to a segment, 0 when the rectangle touches the segment line within its span. */
export function rectTouchesSegment(r: Rect, s: Segment): boolean {
  if (segmentIsVertical(s)) {
    const touchesLine = near(r.x, s.a.x) || near(rectMaxX(r), s.a.x);
    const span = Math.min(rectMaxY(r), Math.max(s.a.y, s.b.y)) - Math.max(r.y, Math.min(s.a.y, s.b.y));
    return touchesLine && span > EPS;
  }
  const touchesLine = near(r.y, s.a.y) || near(rectMaxY(r), s.a.y);
  const span = Math.min(rectMaxX(r), Math.max(s.a.x, s.b.x)) - Math.max(r.x, Math.min(s.a.x, s.b.x));
  return touchesLine && span > EPS;
}
