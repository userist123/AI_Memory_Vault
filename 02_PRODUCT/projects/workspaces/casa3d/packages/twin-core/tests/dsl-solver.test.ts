import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, wallsFromRooms, fingerprint, type Twin } from '../src/twin';
import { catalogFromItems } from '../src/catalog';
import { validateDsl, type DesignDsl } from '../src/dsl';
import { solve, solveAlternatives, measure } from '../src/solver';
import { validate } from '../src/engine';

const catalog = catalogFromItems([
  { id: 'bed-160', name: 'Pat 160', w: 1.6, d: 2.0, h: 0.5, role: 'bed', price: { amount: 2299, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'bed-180', name: 'Pat 180', w: 1.8, d: 2.1, h: 0.5, role: 'bed', price: 'UNKNOWN' },
  { id: 'night-45', name: 'Noptiera', w: 0.45, d: 0.4, h: 0.5, role: 'nightstand', price: { amount: 199, currency: 'RON' } },
  { id: 'wardrobe-200', name: 'Dulap 200', w: 2.0, d: 0.6, h: 2.2, role: 'wardrobe', price: 'UNKNOWN' },
  { id: 'sofa-huge', name: 'Canapea uriasa', w: 4.5, d: 1.2, h: 0.8, role: 'sofa', price: 'UNKNOWN' },
]);

function bedroom(): Twin {
  const t = wallsFromRooms({ ...emptyTwin('b'), rooms: [rectangleRoom('bedroom', 'Dormitor', 0, 0, 4, 3)] });
  const south = t.walls.find(w => w.a.y === 0 && w.b.y === 0)!;
  t.openings = [{ id: 'door', kind: 'door', wallId: south.id, offset: 0.2, width: 0.9, clearDepth: 0.9, sillHeight: 0 }];
  return t;
}

const okDsl = (): unknown => ({
  version: '1.1', title: 'Dormitor simplu',
  operations: [
    { op: 'ADD', ref: 'bed', roomId: 'bedroom', catalogId: 'bed-160', constraints: [{ type: 'againstWall' }], reason: 'patul la perete' },
    { op: 'ADD', ref: 'n1', roomId: 'bedroom', catalogId: 'night-45', constraints: [{ type: 'near', ref: 'bed' }, { type: 'orientation', rotation: 0 }] },
  ],
});

describe('validateDsl', () => {
  it('accepts a well-formed proposal', () => {
    const r = validateDsl(okDsl(), bedroom(), catalog);
    expect(r.errors).toEqual([]);
    expect(r.dsl!.operations).toHaveLength(2);
    expect(r.dsl!.title).toBe('Dormitor simplu');
  });
  it('rejects coordinates anywhere in the proposal', () => {
    const d = okDsl() as any; d.operations[0].x = 1.2; d.operations[1].constraints[0].position = [1, 2];
    const r = validateDsl(d, bedroom(), catalog);
    expect(r.dsl).toBeUndefined();
    expect(r.errors.map(e => e.code)).toEqual(['COORDINATES_FORBIDDEN', 'COORDINATES_FORBIDDEN']);
  });
  it('rejects invented products, unknown rooms, unknown refs and duplicate refs', () => {
    const d: any = { version: '1.1', operations: [
      { op: 'ADD', ref: 'a', roomId: 'bedroom', catalogId: 'bed-999' },
      { op: 'ADD', ref: 'b', roomId: 'garage', catalogId: 'bed-160' },
      { op: 'ADD', ref: 'b', roomId: 'bedroom', catalogId: 'bed-160', constraints: [{ type: 'near', ref: 'ghost' }] },
      { op: 'MOVE', placementId: 'nope', constraints: [] },
      { op: 'FLY', ref: 'z' },
    ] };
    const codes = validateDsl(d, bedroom(), catalog).errors.map(e => e.code);
    expect(codes).toEqual(expect.arrayContaining(['UNKNOWN_CATALOG_ID', 'UNKNOWN_ROOM', 'DUPLICATE_REF', 'UNKNOWN_REF', 'UNKNOWN_PLACEMENT', 'BAD_OPERATION']));
  });
  it('rejects bad structure, versions and constraint values', () => {
    expect(validateDsl(null, bedroom(), catalog).errors[0]!.code).toBe('NOT_AN_OBJECT');
    expect(validateDsl({ version: '1.0', operations: [] }, bedroom(), catalog).errors.map(e => e.code)).toEqual(['BAD_VERSION', 'NO_OPERATIONS']);
    const t = bedroom(); t.placements = [{ id: 'p', catalogId: 'bed-160', roomId: 'bedroom', x: 2, y: 0.5, w: 1.6, d: 2, h: 0.5, rotation: 0 }];
    const d = { version: '1.1', operations: [{ op: 'MOVE', placementId: 'p', constraints: [{ type: 'keepClear', ref: 'p', distance: 9 }, { type: 'orientation', rotation: 45 }, { type: 'againstWall', wallId: 'w-x' }] }] };
    const codes = validateDsl(d, t, catalog).errors.map(e => e.code);
    expect(codes).toEqual(['BAD_CONSTRAINT', 'BAD_CONSTRAINT', 'BAD_CONSTRAINT']);
  });
});

describe('solve', () => {
  it('places products deterministically and the result validates', () => {
    const t = bedroom();
    const dsl = validateDsl(okDsl(), t, catalog).dsl!;
    const a = solve(dsl, t, catalog), b = solve(dsl, t, catalog);
    expect(a.ok).toBe(true);
    expect(fingerprint(a.twin)).toBe(fingerprint(b.twin));
    expect(a.outcomes.map(o => o.status)).toEqual(['APPLIED', 'APPLIED']);
    expect(a.twin.placements.map(p => p.catalogId).sort()).toEqual(['bed-160', 'night-45']);
    expect(validate(a.twin).ok).toBe(true);
    expect(fingerprint(t)).not.toBe(fingerprint(a.twin));
  });
  it('fails cleanly when a product does not fit and leaves the base untouched', () => {
    const t = bedroom();
    const dsl = validateDsl({ version: '1.1', operations: [{ op: 'ADD', ref: 's', roomId: 'bedroom', catalogId: 'sofa-huge' }] }, t, catalog).dsl!;
    const r = solve(dsl, t, catalog);
    expect(r.ok).toBe(false);
    expect(r.outcomes[0]).toMatchObject({ status: 'FAILED', op: 'ADD', ref: 's' });
    expect(r.outcomes[0]!.reason).toMatch(/DOES_NOT_FIT/);
    expect(fingerprint(r.twin)).toBe(fingerprint(t));
  });
  it('REPLACE keeps the spot when the new product fits, re-solves when it does not; REMOVE and MOVE work', () => {
    const t = bedroom();
    t.placements = [{ id: 'bed', catalogId: 'bed-160', roomId: 'bedroom', x: 2.4, y: 1, w: 1.6, d: 2, h: 0.5, rotation: 0 }];
    const rep = solve(validateDsl({ version: '1.1', operations: [{ op: 'REPLACE', placementId: 'bed', catalogId: 'bed-180' }] }, t, catalog).dsl!, t, catalog);
    expect(rep.ok).toBe(true);
    const nb = rep.twin.placements.find(p => p.id === 'bed')!;
    expect(nb.catalogId).toBe('bed-180'); expect(nb.w).toBe(1.8);
    expect(validate(rep.twin).ok).toBe(true);
    const mv = solve(validateDsl({ version: '1.1', operations: [{ op: 'MOVE', placementId: 'bed', constraints: [{ type: 'againstWall' }] }] }, t, catalog).dsl!, t, catalog);
    expect(mv.ok).toBe(true);
    expect(mv.twin.placements).toHaveLength(1);
    const rm = solve(validateDsl({ version: '1.1', operations: [{ op: 'REMOVE', placementId: 'bed' }] }, t, catalog).dsl!, t, catalog);
    expect(rm.ok).toBe(true); expect(rm.twin.placements).toEqual([]);
  });
});

describe('alternatives', () => {
  it('solves up to three variants and reports measures without picking a winner', () => {
    const t = bedroom();
    const v = (title: string, ops: unknown[]): DesignDsl => validateDsl({ version: '1.1', title, operations: ops }, t, catalog).dsl!;
    const alts = solveAlternatives([
      v('Economic', [{ op: 'ADD', ref: 'bed', roomId: 'bedroom', catalogId: 'bed-160', constraints: [{ type: 'againstWall' }] }]),
      v('Echilibrat', [{ op: 'ADD', ref: 'bed', roomId: 'bedroom', catalogId: 'bed-160', constraints: [{ type: 'againstWall' }] },
                       { op: 'ADD', ref: 'w', roomId: 'bedroom', catalogId: 'wardrobe-200', constraints: [{ type: 'againstWall' }, { type: 'keepClear', ref: 'bed', distance: 0.6 }] }]),
      v('Imposibil', [{ op: 'ADD', ref: 's', roomId: 'bedroom', catalogId: 'sofa-huge' }]),
    ], t, catalog);
    expect(alts.map(a => a.title)).toEqual(['Economic', 'Echilibrat', 'Imposibil']);
    expect(alts.map(a => a.result.ok)).toEqual([true, true, false]);
    expect(alts[0]!.measures.items).toBe(1);
    expect(alts[1]!.measures.items).toBe(2);
    expect(alts[1]!.measures.rooms[0]!.freeRatio).toBeLessThan(alts[0]!.measures.rooms[0]!.freeRatio);
    expect(alts[2]!.measures.items).toBe(0);
    expect(Object.keys(alts[0]!)).not.toContain('winner');
    expect(() => solveAlternatives([], t, catalog)).toThrow(RangeError);
  });
  it('measure reports free area per room', () => {
    const t = bedroom();
    expect(measure(t).rooms[0]).toEqual({ roomId: 'bedroom', area: 12, usedArea: 0, freeRatio: 1, items: 0 });
  });
});
