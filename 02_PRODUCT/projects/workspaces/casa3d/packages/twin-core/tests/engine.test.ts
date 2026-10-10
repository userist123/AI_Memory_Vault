import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, wallsFromRooms, type Twin, type Room, type Placement } from '../src/twin';
import { validate, findPosition } from '../src/engine';

const codes = (t: Twin) => validate(t).issues.map(i => i.code);

function bedroom(): Twin {
  const t = wallsFromRooms({ ...emptyTwin('b'), rooms: [rectangleRoom('bedroom', 'Dormitor', 0, 0, 4, 3)] });
  const south = t.walls.find(w => w.a.y === 0 && w.b.y === 0)!; // y = 0 edge
  t.openings = [
    { id: 'door', kind: 'door', wallId: south.id, offset: 0.2, width: 0.9, clearDepth: 0.9, sillHeight: 0 },
    { id: 'win', kind: 'window', wallId: t.walls.find(w => w.a.y === 3 && w.b.y === 3)!.id, offset: 1, width: 1.2, clearDepth: 0, sillHeight: 0.9 },
  ];
  return t;
}
const bed = (over: Partial<Placement> = {}): Placement =>
  ({ id: 'bed', catalogId: 'bed-160', roomId: 'bedroom', x: 2, y: 0.5, w: 1.6, d: 2, h: 0.5, rotation: 0, ...over });

describe('validate', () => {
  it('accepts a clean bedroom', () => {
    const r = validate({ ...bedroom(), placements: [bed()] });
    expect(r.ok).toBe(true); expect(r.needsConfirmation).toBe(false); expect(r.issues).toEqual([]);
  });
  it('rejects an invalid room polygon and a placement outside the room', () => {
    const t = bedroom();
    t.rooms.push({ id: 'bad', name: 'Rau', height: 2.6, polygon: [{ x: 9, y: 9 }, { x: 10, y: 10 }, { x: 9, y: 10 }, { x: 10, y: 9 }] });
    t.placements = [bed({ x: 3 })];
    const c = codes(t);
    expect(c).toContain('ROOM_POLYGON_INVALID');
    expect(c).toContain('DOES_NOT_FIT');
    expect(validate(t).ok).toBe(false);
  });
  it('a product spanning the notch of an L-shaped room does not fit', () => {
    const L: Room = { id: 'living', name: 'Living', height: 2.6,
      polygon: [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 1.5 }, { x: 2, y: 1.5 }, { x: 2, y: 3 }, { x: 0, y: 3 }] };
    const t: Twin = { ...emptyTwin('l'), rooms: [L] };
    const sofa: Placement = { id: 's', catalogId: 'sofa-3', roomId: 'living', x: 1.5, y: 1, w: 2.2, d: 0.9, h: 0.8, rotation: 0 };
    expect(codes({ ...t, placements: [sofa] })).toEqual(['DOES_NOT_FIT']);
    expect(codes({ ...t, placements: [{ ...sofa, x: 0, y: 0 }] })).toEqual([]);
  });
  it('reports overlaps between products, blocked doors (ERROR) and covered windows (WARNING)', () => {
    const t = bedroom();
    t.placements = [bed(), bed({ id: 'bed2', x: 2.5 })];
    expect(codes(t)).toContain('PLACEMENT_OVERLAP');
    t.placements = [bed({ id: 'wardrobe', catalogId: 'wd', x: 0, y: 0, w: 1, d: 0.6, h: 2 })];
    const r = validate(t);
    expect(r.ok).toBe(false);
    expect(r.issues.map(i => i.code)).toEqual(['PLACEMENT_BLOCKS_DOOR']);
    t.placements = [bed({ id: 'wardrobe', catalogId: 'wd', x: 1, y: 2.4, w: 1, d: 0.6, h: 2 })];
    const w = validate(t);
    expect(w.ok).toBe(true); expect(w.needsConfirmation).toBe(true);
    expect(w.issues.map(i => i.code)).toEqual(['PLACEMENT_COVERS_WINDOW']);
    t.placements = [bed({ id: 'low', catalogId: 'desk', x: 1, y: 2.4, w: 1, d: 0.6, h: 0.75 })]; // below the sill
    expect(codes(t)).toEqual([]);
  });
  it('flags openings outside their wall, overlapping openings and missing walls', () => {
    const t = bedroom();
    t.openings.push({ id: 'd2', kind: 'door', wallId: t.openings[0]!.wallId, offset: 0.8, width: 0.9, clearDepth: 0.9, sillHeight: 0 });
    t.openings.push({ id: 'd3', kind: 'door', wallId: t.openings[0]!.wallId, offset: 3.5, width: 0.9, clearDepth: 0.9, sillHeight: 0 });
    t.openings.push({ id: 'd4', kind: 'door', wallId: 'nope', offset: 0, width: 0.9, clearDepth: 0.9, sillHeight: 0 });
    const c = codes(t);
    expect(c).toContain('OPENING_OVERLAP'); expect(c).toContain('OPENING_OUTSIDE_WALL'); expect(c).toContain('OPENING_WALL_MISSING');
  });
  it('warns when the free area drops below 30% and errors on overlapping rooms', () => {
    const t = bedroom();
    t.placements = [bed({ x: 0, y: 0, w: 4, d: 2.2, h: 0.5, id: 'big' })];
    t.openings = [];
    expect(codes(t)).toEqual(['CIRCULATION_LOW']);
    const u = bedroom(); u.rooms.push(rectangleRoom('x', 'X', 3, 1, 2, 2));
    expect(codes(u)).toContain('ROOM_OVERLAP');
  });
});

describe('findPosition', () => {
  it('is deterministic and puts a bed against a wall away from the door', () => {
    const t = bedroom();
    const a = findPosition(t, { id: 'bed', catalogId: 'bed-160', roomId: 'bedroom', w: 1.6, d: 2, h: 0.5, againstWall: true });
    const b = findPosition(t, { id: 'bed', catalogId: 'bed-160', roomId: 'bedroom', w: 1.6, d: 2, h: 0.5, againstWall: true });
    expect(a).not.toBeNull();
    expect(a!.placement).toEqual(b!.placement);
    expect(a!.validation.ok).toBe(true);
    expect(validate({ ...t, placements: [a!.placement] }).ok).toBe(true);
  });
  it('honours near, keepClear and alignedWith, and returns null when nothing fits', () => {
    const t = bedroom();
    t.placements = [bed({ x: 2.3, y: 1, rotation: 0 })];
    const night = findPosition(t, { id: 'n', catalogId: 'nightstand', roomId: 'bedroom', w: 0.45, d: 0.4, h: 0.5, near: 'bed', rotations: [0] });
    expect(night).not.toBeNull();
    const nf = night!.placement;
    expect(Math.abs((nf.x + 0.225) - (2.3 + 0.8)) + Math.abs((nf.y + 0.2) - (1 + 1))).toBeLessThan(1.3);
    const far = findPosition(t, { id: 'c', catalogId: 'chair', roomId: 'bedroom', w: 0.5, d: 0.5, h: 0.9, keepClear: [{ placementId: 'bed', distance: 0.6 }], rotations: [0] });
    expect(far).not.toBeNull();
    const f = far!.placement;
    expect(f.x + 0.5 <= 2.3 - 0.6 + 1e-9 || f.x >= 2.3 + 1.6 + 0.6 - 1e-9 || f.y + 0.5 <= 1 - 0.6 + 1e-9 || f.y >= 1 + 2 + 0.6 - 1e-9).toBe(true);
    const aligned = findPosition(t, { id: 'd', catalogId: 'dresser', roomId: 'bedroom', w: 0.8, d: 0.4, h: 0.9, alignedWith: 'bed', rotations: [0] });
    expect(aligned!.placement.x === 2.3 || aligned!.placement.y === 1).toBe(true);
    expect(findPosition(t, { id: 'huge', catalogId: 'x', roomId: 'bedroom', w: 5, d: 5, h: 1 })).toBeNull();
    expect(findPosition(t, { id: 'z', catalogId: 'x', roomId: 'missing', w: 1, d: 1, h: 1 })).toBeNull();
  });
  it('never returns a position that blocks the door, even when the room is tight', () => {
    const t = bedroom();
    const r = findPosition(t, { id: 'w', catalogId: 'wardrobe', roomId: 'bedroom', w: 3.8, d: 0.6, h: 2.2, againstWall: true, rotations: [0] });
    expect(r).not.toBeNull();
    expect(r!.placement.y).toBeGreaterThan(0.9 - 1e-9);
  });
});
