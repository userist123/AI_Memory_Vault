// End to end: brief -> rules engine -> 3 alternatives -> BOQ -> preview -> accept -> revision -> share.
import { describe, expect, it } from 'vitest';
import {
  emptyTwin, rectangleRoom, wallsFromRooms, fingerprint, catalogFromItems, RulesDesignProvider, draft,
  solveAlternatives, evaluateAlternatives, createProject, preview, accept, createShare, resolveShare,
  initialViewerState, reduceViewer, renderItems, validate, type CatalogItem, type Twin, type Room,
} from '../src/index';

const items: CatalogItem[] = [
  { id: 'bed-140', name: 'Pat 140', w: 1.4, d: 2, h: 0.5, role: 'bed', price: { amount: 1499, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'bed-160', name: 'Pat 160', w: 1.6, d: 2, h: 0.5, role: 'bed', price: { amount: 2299, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'bed-180', name: 'Pat 180', w: 1.8, d: 2.1, h: 0.5, role: 'bed', price: 'UNKNOWN', retailer: 'dedeman' },
  { id: 'night-45', name: 'Noptiera', w: 0.45, d: 0.4, h: 0.5, role: 'nightstand', price: { amount: 199, currency: 'RON' }, retailer: 'ikea-ro' },
  { id: 'wardrobe-150', name: 'Dulap 150', w: 1.5, d: 0.6, h: 2.2, role: 'wardrobe', price: { amount: 1299, currency: 'RON' }, retailer: 'ikea-ro' },
];
const catalog = catalogFromItems(items);

/** L-shaped bedroom 4x3.5 with a 1.5x1.5 notch, door on the south wall, window on the north wall. */
function lBedroom(): Twin {
  const room: Room = { id: 'bedroom', name: 'Dormitor', height: 2.6,
    polygon: [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 2 }, { x: 2.5, y: 2 }, { x: 2.5, y: 3.5 }, { x: 0, y: 3.5 }] };
  const t = wallsFromRooms({ ...emptyTwin('apt'), rooms: [room] });
  const south = t.walls.find(w => w.a.y === 0 && w.b.y === 0)!, north = t.walls.find(w => w.a.y === 3.5 && w.b.y === 3.5)!;
  t.openings = [
    { id: 'door', kind: 'door', wallId: south.id, offset: 3, width: 0.9, clearDepth: 0.9, sillHeight: 0 },
    { id: 'win', kind: 'window', wallId: north.id, offset: 0.5, width: 1.2, clearDepth: 0, sillHeight: 0.9 },
  ];
  return t;
}

describe('end to end', () => {
  it('runs the whole pipeline on an L-shaped room without the AI', async () => {
    let project = createProject('p', lBedroom(), '2026-10-10T12:00:00Z');
    expect(validate(project.twin).ok).toBe(true);
    const ctx = { twin: project.twin, catalog, catalogItems: items };
    const brief = { roomId: 'bedroom', wants: ['bed', 'nightstand', 'wardrobe'], budget: 3000 };
    const drafts = await Promise.all(([0, 1, 2] as const).map(v => draft(new RulesDesignProvider(), brief, ctx, v)));
    expect(drafts.every(d => d.dsl)).toBe(true);

    const alts = evaluateAlternatives(solveAlternatives(drafts.map(d => d.dsl!), project.twin, catalog), catalog, brief.budget);
    expect(alts.map(a => a.result.ok)).toEqual([true, true, true]);
    for (const a of alts) {
      expect(a.measures.items).toBe(3);
      expect(a.result.twin.placements.every(p => validate(a.result.twin).ok)).toBe(true);
      expect(a.boq.items.map(i => i.catalogId)).toContain('night-45');
    }
    expect(alts[0]!.boq.budget).toEqual({ target: 3000, delta: 3000 - (1499 + 199 + 1299), status: 'UNDER' });
    expect(alts[2]!.boq.budget!.status).toBe('UNKNOWN'); // bed-180 has no price

    // The user previews the balanced one, the base is untouched, then accepts.
    const prop = preview(project, 'prop-1', drafts[1]!.dsl!, catalog, 'rules', '2026-10-10T12:01:00Z');
    expect(fingerprint(project.twin)).toBe(prop.baseFingerprint);
    let view = reduceViewer(initialViewerState(fingerprint(project.twin)), { type: 'preview', proposalId: prop.id });
    const overlay = renderItems(project.twin, view, prop.result.twin);
    expect(overlay.filter(i => i.preview)).toHaveLength(3);

    const out = accept(project, prop, { now: '2026-10-10T12:02:00Z' });
    expect(out.accepted).toBe(true);
    if (!out.accepted) return;
    project = out.project;
    expect(project.revisions.map(r => r.number)).toEqual([1, 2]);
    view = reduceViewer(view, { type: 'twinChanged', fingerprint: out.revision.fingerprint });
    expect(view.previewProposalId).toBeUndefined();
    expect(renderItems(project.twin, view).every(i => !i.preview)).toBe(true);

    // The same proposal cannot be applied twice, and the other alternatives are now stale.
    const other = preview({ ...project, twin: out.revision.twin, revisions: project.revisions.slice(0, 1) }, 'x', drafts[0]!.dsl!, catalog, 'rules');
    expect(accept(project, { ...other, baseFingerprint: prop.baseFingerprint }).accepted).toBe(false);

    // Share revision 2 read-only; the next edit does not leak into the share.
    const share = createShare(project, 2, { now: '2026-10-10T12:03:00Z' });
    const edited = { ...project, twin: { ...project.twin, placements: [] } };
    const shared = resolveShare([share], [edited], share.token);
    expect(shared!.twin.placements).toHaveLength(3);
    expect(shared!.fingerprint).toBe(out.revision.fingerprint);
  });
});
