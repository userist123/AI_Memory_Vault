import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, type Twin } from '../src/twin';
import { catalogFromItems } from '../src/catalog';
import { evaluateBoq, evaluateAlternatives } from '../src/boq-eval';
import { solveAlternatives } from '../src/solver';
import { validateDsl } from '../src/dsl';

const catalog = catalogFromItems([
  { id: 'bed-160', name: 'Pat 160', w: 1.6, d: 2, h: 0.5, price: { amount: 2299, currency: 'RON' }, retailer: 'ikea-ro', provenance: { source: 'catalog.v1', verifiedAt: '2026-09-30', verificationType: 'manual' } },
  { id: 'night-45', name: 'Noptiera', w: 0.45, d: 0.4, h: 0.5, price: { amount: 199, currency: 'RON' }, retailer: 'dedeman' },
  { id: 'lamp-eur', name: 'Lampa', w: 0.2, d: 0.2, h: 0.4, price: { amount: 30, currency: 'EUR' } },
  { id: 'wardrobe-200', name: 'Dulap', w: 2, d: 0.6, h: 2.2, price: 'UNKNOWN' },
]);
const room = (): Twin => ({ ...emptyTwin('t'), rooms: [rectangleRoom('r', 'R', 0, 0, 4, 3, 2.6)] });
const place = (id: string, catalogId: string, x: number, y: number) => {
  const c = catalog.get(catalogId)!; return { id, catalogId, roomId: 'r', x, y, w: c.w, d: c.d, h: c.h, rotation: 0 as const };
};

describe('evaluateBoq', () => {
  it('sums only known RON prices, counts UNKNOWN and keeps provenance', () => {
    const t = { ...room(), placements: [place('b', 'bed-160', 0, 0), place('n', 'night-45', 2, 0), place('l', 'lamp-eur', 3, 0), place('w', 'wardrobe-200', 0, 2.4)] };
    const e = evaluateBoq(t, catalog, 3000);
    expect(e.knownTotal).toBe(2498);
    expect(e.unknownCount).toBe(2);
    expect(e.unknownIds).toEqual(['l', 'w']);
    expect(e.items.find(i => i.placementId === 'b')!.provenance).toMatchObject({ source: 'catalog.v1', verificationType: 'manual' });
    expect(e.retailers).toEqual(['dedeman', 'ikea-ro']);
    expect(e.budget).toEqual({ target: 3000, delta: 502, status: 'UNKNOWN' }); // unknowns make the verdict unknown
    expect(e.quantities[0]).toEqual({ roomId: 'r', floorArea: 12, perimeter: 14, wallArea: 36.4, ceilingArea: 12 });
  });
  it('gives UNDER/OVER only when every price is known, and no budget block without a target', () => {
    const t = { ...room(), placements: [place('b', 'bed-160', 0, 0), place('n', 'night-45', 2, 0)] };
    expect(evaluateBoq(t, catalog, 3000).budget).toEqual({ target: 3000, delta: 502, status: 'UNDER' });
    expect(evaluateBoq(t, catalog, 2000).budget).toEqual({ target: 2000, delta: -498, status: 'OVER' });
    expect(evaluateBoq(t, catalog).budget).toBeUndefined();
    expect(evaluateBoq({ ...room(), placements: [{ id: 'x', catalogId: 'ghost', roomId: 'r', x: 0, y: 0, w: 1, d: 1, h: 1, rotation: 0 }] }, catalog).unknownIds).toEqual(['x']);
  });
  it('evaluates solved alternatives', () => {
    const t = room();
    const alts = solveAlternatives([
      validateDsl({ version: '1.1', title: 'A', operations: [{ op: 'ADD', ref: 'b', roomId: 'r', catalogId: 'bed-160' }] }, t, catalog).dsl!,
      validateDsl({ version: '1.1', title: 'B', operations: [{ op: 'ADD', ref: 'b', roomId: 'r', catalogId: 'bed-160' }, { op: 'ADD', ref: 'w', roomId: 'r', catalogId: 'wardrobe-200' }] }, t, catalog).dsl!,
    ], t, catalog);
    const ev = evaluateAlternatives(alts, catalog, 2500);
    expect(ev.map(a => [a.title, a.boq.knownTotal, a.boq.unknownCount, a.boq.budget!.status])).toEqual([['A', 2299, 0, 'UNDER'], ['B', 2299, 1, 'UNKNOWN']]);
  });
});
