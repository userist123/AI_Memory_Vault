import { describe, expect, it } from 'vitest';
import type { Room } from '../core/types';
import { pointInRoom, polygonArea, polygonPerimeter, roomArea, roomBoundary, roomCentroid, transformBoundary } from '../core/room-geometry';

const legacy: Room = { id: 'rect', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 3 } };
const L: Room = { id: 'l', name: 'Living L', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 3 }, boundary: [[0,0],[4,0],[4,1.5],[2,1.5],[2,3],[0,3]] };

describe('polygon room geometry', () => {
  it('keeps legacy rectangular rooms backward-compatible', () => {
    expect(roomBoundary(legacy)).toEqual([[0,0],[4,0],[4,3],[0,3]]);
    expect(roomArea(legacy)).toBe(12);
  });
  it('uses the actual area and perimeter of a notched room', () => {
    expect(polygonArea(roomBoundary(L))).toBe(9);
    expect(roomArea(L)).toBe(9);
    expect(polygonPerimeter(roomBoundary(L))).toBe(14);
  });
  it('uses a centroid inside a concave room and rejects the missing corner', () => {
    const [x,z] = roomCentroid(L);
    expect(pointInRoom(L,x,z)).toBe(true);
    expect(pointInRoom(L,3,2)).toBe(false);
    expect(pointInRoom(L,1,2)).toBe(true);
  });
  it('transforms the full contour when its bounding box is resized', () => {
    const next = transformBoundary(L.boundary, L.rect, { x0: 0, z0: 0, x1: 8, z1: 6 });
    expect(next).toEqual([[0,0],[8,0],[8,3],[4,3],[4,6],[0,6]]);
  });
});
