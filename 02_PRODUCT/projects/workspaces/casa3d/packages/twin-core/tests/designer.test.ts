import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, wallsFromRooms, type Twin } from '../src/twin';
import { catalogFromItems, type CatalogItem } from '../src/catalog';
import { RulesDesignProvider, draft, type DesignProvider } from '../src/designer';
import { solve } from '../src/solver';

const items: CatalogItem[] = [
  { id: 'bed-140', name: 'Pat 140', w: 1.4, d: 2, h: 0.5, role: 'bed', price: { amount: 1499, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'bed-160', name: 'Pat 160', w: 1.6, d: 2, h: 0.5, role: 'bed', price: { amount: 2299, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'bed-180', name: 'Pat 180', w: 1.8, d: 2.1, h: 0.5, role: 'bed', price: 'UNKNOWN', retailer: 'dedeman' },
  { id: 'night-45', name: 'Noptiera', w: 0.45, d: 0.4, h: 0.5, role: 'nightstand', price: { amount: 199, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'wardrobe-150', name: 'Dulap 150', w: 1.5, d: 0.6, h: 2.2, role: 'wardrobe', price: { amount: 1299, currency: 'RON' }, retailer: 'ikea-ro' },
];
const catalog = catalogFromItems(items);
function bedroom(): Twin {
  const t = wallsFromRooms({ ...emptyTwin('b'), rooms: [rectangleRoom('bedroom', 'Dormitor', 0, 0, 4, 3)] });
  const south = t.walls.find(w => w.a.y === 0 && w.b.y === 0)!;
  t.openings = [{ id: 'door', kind: 'door', wallId: south.id, offset: 0.2, width: 0.9, clearDepth: 0.9, sillHeight: 0 }];
  return t;
}
const ctx = () => ({ twin: bedroom(), catalog, catalogItems: items });

describe('rules design provider', () => {
  it('emits valid DSL with catalog ids only, three distinct variants, no coordinates', async () => {
    const p = new RulesDesignProvider();
    const brief = { roomId: 'bedroom', wants: ['bed', 'nightstand', 'wardrobe'] };
    const ds = await Promise.all(([0, 1, 2] as const).map(v => draft(p, brief, ctx(), v)));
    expect(ds.every(d => d.errors.length === 0 && d.dsl)).toBe(true);
    expect(ds.map(d => d.dsl!.title)).toEqual(['Economic', 'Echilibrat', 'Premium']);
    const beds = ds.map(d => (d.dsl!.operations[0] as { catalogId: string }).catalogId);
    expect(beds).toEqual(['bed-140', 'bed-160', 'bed-180']);
    expect(JSON.stringify(ds[0]!.raw)).not.toMatch(/"x"|"y"|position/);
    const night = ds[0]!.dsl!.operations[1] as { constraints: { type: string; ref?: string }[] };
    expect(night.constraints).toEqual([{ type: 'near', ref: 'bed-1' }]);
    for (const d of ds) expect(solve(d.dsl!, ctx().twin, catalog).ok).toBe(true);
  });
  it('respects retailers, excludes and accessibility clearances; returns null when nothing matches', async () => {
    const p = new RulesDesignProvider();
    const d = await draft(p, { roomId: 'bedroom', wants: ['bed', 'wardrobe'], retailers: ['dedeman'], accessibility: true }, ctx(), 0);
    expect((d.dsl!.operations[0] as { catalogId: string }).catalogId).toBe('bed-180');
    expect(d.dsl!.operations).toHaveLength(1); // wardrobe not sold by dedeman
    const e = await draft(p, { roomId: 'bedroom', wants: ['bed'], excludes: ['bed-140', 'bed-160', 'bed-180'] }, ctx(), 0);
    expect(e.dsl).toBeUndefined(); expect(e.errors[0]!.code).toBe('NOT_AN_OBJECT');
    const a = await draft(p, { roomId: 'bedroom', wants: ['bed', 'wardrobe'], accessibility: true }, ctx(), 1);
    const w = a.dsl!.operations[1] as { constraints: { type: string; distance?: number }[] };
    expect(w.constraints).toEqual([{ type: 'againstWall' }, { type: 'keepClear', ref: 'bed-1', distance: 0.9 }]);
  });
  it('a provider that returns coordinates or throws is rejected, not trusted', async () => {
    const bad: DesignProvider = { name: 'bad-ai', propose: async () => ({ version: '1.1', operations: [{ op: 'ADD', ref: 'a', roomId: 'bedroom', catalogId: 'bed-160', x: 1, y: 2 }] }) };
    const d = await draft(bad, { roomId: 'bedroom', wants: ['bed'] }, ctx(), 0);
    expect(d.dsl).toBeUndefined(); expect(d.errors.map(e => e.code)).toEqual(['COORDINATES_FORBIDDEN', 'COORDINATES_FORBIDDEN']);
    const boom: DesignProvider = { name: 'boom', propose: async () => { throw new Error('network'); } };
    const b = await draft(boom, { roomId: 'bedroom', wants: ['bed'] }, ctx(), 0);
    expect(b.errors[0]!.message).toMatch(/network/);
  });
});

describe('role aliases', () => {
  it('applies the layout rules to catalog-specific role names', async () => {
    const ren: Record<string, string> = { bed: 'pat', nightstand: 'noptiera', wardrobe: 'dulap' };
    const ro: CatalogItem[] = items.map(i => ({ ...i, role: ren[i.role!] ?? i.role! }));
    const p = new RulesDesignProvider({ pat: 'bed', noptiera: 'nightstand', dulap: 'wardrobe' });
    const d = await draft(p, { roomId: 'bedroom', wants: ['pat', 'noptiera'] }, { twin: bedroom(), catalog: catalogFromItems(ro), catalogItems: ro }, 0);
    expect((d.dsl!.operations[0] as { constraints: unknown[] }).constraints).toEqual([{ type: 'againstWall' }]);
    expect((d.dsl!.operations[1] as { constraints: unknown[] }).constraints).toEqual([{ type: 'near', ref: 'pat-1' }]);
    const plain = await draft(new RulesDesignProvider(), { roomId: 'bedroom', wants: ['pat'] }, { twin: bedroom(), catalog: catalogFromItems(ro), catalogItems: ro }, 0);
    expect((plain.dsl!.operations[0] as { constraints: unknown[] }).constraints).toEqual([]);
  });
});
