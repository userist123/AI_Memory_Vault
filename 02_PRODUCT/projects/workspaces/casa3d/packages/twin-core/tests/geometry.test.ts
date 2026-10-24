import { describe, expect, it } from 'vitest';
import {
  polygonProblems, polygonArea, normalizePolygon, pointInPolygon, rectInsidePolygon, rectsOverlap,
  rectOf, collinearOverlap, bandAlong, rectTouchesSegment, rectIntersectionArea,
} from '../src/geometry';

const rect = [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 3 }, { x: 0, y: 3 }];
// L-shape: 4x3 with the top-right 2x1.5 corner removed.
const L = [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 1.5 }, { x: 2, y: 1.5 }, { x: 2, y: 3 }, { x: 0, y: 3 }];

describe('rectilinear polygons', () => {
  it('accepts a rectangle and an L-shape', () => {
    expect(polygonProblems(rect)).toEqual([]);
    expect(polygonProblems(L)).toEqual([]);
    expect(polygonArea(rect)).toBe(12);
    expect(polygonArea(L)).toBe(9);
  });
  it('rejects non axis-aligned, degenerate and self-intersecting shapes', () => {
    expect(polygonProblems([{ x: 0, y: 0 }, { x: 4, y: 1 }, { x: 4, y: 3 }, { x: 0, y: 3 }])).toContain('EDGE_NOT_AXIS_ALIGNED');
    expect(polygonProblems([{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 3 }])).toEqual(['TOO_FEW_VERTICES']);
    expect(polygonProblems([{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 3 }, { x: 0, y: 3 }, { x: 0, y: 1 }])).toEqual(['CONSECUTIVE_COLLINEAR']);
    // bow-tie made rectilinear: edges cross
    const bow = [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 3 }, { x: 2, y: 3 }, { x: 2, y: -1 }, { x: 0, y: -1 }];
    expect(polygonProblems(bow)).toEqual(['SELF_INTERSECTING']);
  });
  it('normalizes to counter-clockwise and millimetres', () => {
    const cw = [...rect].reverse().map(p => ({ x: p.x + 0.00049, y: p.y }));
    const n = normalizePolygon(cw);
    expect(n[0]).toEqual({ x: 0, y: 0 });
    expect(polygonProblems(n)).toEqual([]);
  });
  it('point in polygon counts the boundary as inside', () => {
    expect(pointInPolygon({ x: 1, y: 1 }, L)).toBe(true);
    expect(pointInPolygon({ x: 3, y: 2 }, L)).toBe(false); // in the removed corner
    expect(pointInPolygon({ x: 4, y: 1.5 }, L)).toBe(true); // vertex
    expect(pointInPolygon({ x: 3, y: 1.5 }, L)).toBe(true); // on the notch edge
  });
});

describe('rectangles against the room', () => {
  it('a rectangle must lie fully inside the L, the notch is not allowed', () => {
    expect(rectInsidePolygon(rectOf(0.5, 0.5, 1, 1), L)).toBe(true);
    expect(rectInsidePolygon(rectOf(2, 1.5, 2, 1.5), L)).toBe(false); // exactly the removed corner
    expect(rectInsidePolygon(rectOf(1, 1, 2, 1), L)).toBe(false); // spans the notch: corners inside, edge crosses
    expect(rectInsidePolygon(rectOf(0, 0, 4, 1.5), L)).toBe(true); // along the long bottom wall, boundary allowed
    expect(rectInsidePolygon(rectOf(3.5, 0, 1, 1), L)).toBe(false); // pokes out
  });
  it('overlap ignores touching edges', () => {
    expect(rectsOverlap(rectOf(0, 0, 1, 1), rectOf(1, 0, 1, 1))).toBe(false);
    expect(rectsOverlap(rectOf(0, 0, 1, 1), rectOf(0.999, 0, 1, 1))).toBe(true);
    expect(rectIntersectionArea(rectOf(0, 0, 2, 2), rectOf(1, 1, 2, 2))).toBe(1);
  });
});

describe('segments', () => {
  it('measures collinear overlap and bands along a wall', () => {
    const wall = { a: { x: 0, y: 0 }, b: { x: 4, y: 0 } };
    expect(collinearOverlap(wall, { a: { x: 3, y: 0 }, b: { x: 6, y: 0 } })).toBe(1);
    expect(collinearOverlap(wall, { a: { x: 3, y: 0.1 }, b: { x: 6, y: 0.1 } })).toBe(0);
    expect(collinearOverlap(wall, { a: { x: 0, y: 0 }, b: { x: 0, y: 3 } })).toBe(0);
    expect(bandAlong(wall, 1, 0.9, 0.9)).toEqual(rectOf(1, -0.9, 0.9, 1.8));
    const reversed = { a: { x: 4, y: 0 }, b: { x: 0, y: 0 } };
    expect(bandAlong(reversed, 1, 0.9, 0.5)).toEqual(rectOf(2.1, -0.5, 0.9, 1));
  });
  it('knows when a rectangle rests on a wall', () => {
    const wall = { a: { x: 0, y: 0 }, b: { x: 0, y: 3 } };
    expect(rectTouchesSegment(rectOf(0, 1, 0.5, 1), wall)).toBe(true);
    expect(rectTouchesSegment(rectOf(0.2, 1, 0.5, 1), wall)).toBe(false);
    expect(rectTouchesSegment(rectOf(0, 3, 0.5, 1), wall)).toBe(false); // beyond the wall's end
  });
});
