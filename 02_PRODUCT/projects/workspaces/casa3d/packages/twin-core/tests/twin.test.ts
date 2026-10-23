import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, normalize, fingerprint, linkWalls, wallsFromRooms, footprint, type Twin, type Room } from '../src/twin';

const L: Room = { id: 'living', name: 'Living', height: 2.6,
  polygon: [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 1.5 }, { x: 2, y: 1.5 }, { x: 2, y: 3 }, { x: 0, y: 3 }] };

function apartment(): Twin {
  const t = emptyTwin('apt-1');
  t.rooms = [rectangleRoom('bedroom', 'Dormitor', 0, 0, 4, 3), rectangleRoom('hall', 'Hol', 4, 0, 1.2, 3)];
  return t;
}

describe('fingerprint', () => {
  it('is stable across ordering, rounding noise and undefined fields', () => {
    const a = apartment();
    const b: Twin = { ...a, rooms: [...a.rooms].reverse().map(r => ({ ...r, polygon: [...r.polygon].reverse().map(p => ({ x: p.x + 0.0004, y: p.y })) })) };
    expect(fingerprint(a)).toBe(fingerprint(b));
    expect(fingerprint(a)).toMatch(/^[0-9a-f]{64}$/);
  });
  it('changes when geometry changes', () => {
    const a = apartment();
    const b: Twin = { ...a, placements: [{ id: 'p1', catalogId: 'bed-1', roomId: 'bedroom', x: 0, y: 0, w: 1.6, d: 2, h: 0.5, rotation: 0 }] };
    expect(fingerprint(a)).not.toBe(fingerprint(b));
    const c: Twin = { ...b, placements: [{ ...b.placements[0]!, x: 0.001 }] }; // one millimetre
    expect(fingerprint(b)).not.toBe(fingerprint(c));
  });
});

describe('walls and rooms', () => {
  it('generates walls from rooms and shares the wall between adjacent rooms', () => {
    const t = wallsFromRooms(apartment());
    const shared = t.walls.filter(w => w.roomIds.length === 2);
    expect(shared).toHaveLength(1);
    expect(shared[0]!.roomIds).toEqual(['bedroom', 'hall']);
    expect(shared[0]!.a).toEqual({ x: 4, y: 0 });
    expect(shared[0]!.b).toEqual({ x: 4, y: 3 });
    expect(t.walls.filter(w => w.roomIds.length === 1)).toHaveLength(6);
    expect(t.walls.every(w => w.roomIds.length > 0)).toBe(true);
  });
  it('links an existing wall that runs along part of a room edge', () => {
    const t = apartment();
    t.walls = [{ id: 'w', a: { x: 1, y: 3 }, b: { x: 3, y: 3 }, thickness: 0.1, roomIds: [] },
               { id: 'far', a: { x: 10, y: 10 }, b: { x: 12, y: 10 }, thickness: 0.1, roomIds: ['bedroom'] }];
    const linked = linkWalls(t);
    expect(linked.walls[0]!.roomIds).toEqual(['bedroom']);
    expect(linked.walls[1]!.roomIds).toEqual([]); // stale link removed
  });
  it('an L-shaped room gets six walls', () => {
    const t = emptyTwin('l'); t.rooms = [L];
    expect(wallsFromRooms(t).walls).toHaveLength(6);
  });
  it('normalize keeps the data canonical and idempotent', () => {
    const t = wallsFromRooms(apartment());
    const n1 = normalize(t), n2 = normalize(n1);
    expect(n2).toEqual(n1);
    expect(n1.rooms.map(r => r.id)).toEqual(['bedroom', 'hall']);
  });
});

describe('footprint', () => {
  it('swaps width and depth for 90 and 270 degrees', () => {
    expect(footprint({ x: 1, y: 1, w: 2, d: 0.5, rotation: 0 })).toEqual({ x: 1, y: 1, w: 2, d: 0.5 });
    expect(footprint({ x: 1, y: 1, w: 2, d: 0.5, rotation: 90 })).toEqual({ x: 1, y: 1, w: 0.5, d: 2 });
  });
});
