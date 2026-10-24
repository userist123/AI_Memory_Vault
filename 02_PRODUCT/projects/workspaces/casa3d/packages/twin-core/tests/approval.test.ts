import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, wallsFromRooms, fingerprint, type Twin } from '../src/twin';
import { catalogFromItems } from '../src/catalog';
import { validateDsl } from '../src/dsl';
import { createProject, preview, accept, reject, refreshStatus, markApplied } from '../src/approval';

const catalog = catalogFromItems([
  { id: 'bed-160', name: 'Pat 160', w: 1.6, d: 2.0, h: 0.5, role: 'bed', price: { amount: 2299, currency: 'RON' } },
  { id: 'wardrobe-200', name: 'Dulap', w: 2.0, d: 0.6, h: 2.2, role: 'wardrobe', price: 'UNKNOWN' },
  { id: 'sofa-huge', name: 'Canapea uriasa', w: 4.5, d: 1.2, h: 0.8, price: 'UNKNOWN' },
]);

function bedroom(): Twin {
  const t = wallsFromRooms({ ...emptyTwin('b'), rooms: [rectangleRoom('bedroom', 'Dormitor', 0, 0, 4, 3)] });
  const south = t.walls.find(w => w.a.y === 0 && w.b.y === 0)!, north = t.walls.find(w => w.a.y === 3 && w.b.y === 3)!;
  t.openings = [
    { id: 'door', kind: 'door', wallId: south.id, offset: 0.2, width: 0.9, clearDepth: 0.9, sillHeight: 0 },
    { id: 'win', kind: 'window', wallId: north.id, offset: 0.5, width: 3, clearDepth: 0, sillHeight: 0.9 },
  ];
  return t;
}
const dslBed = (t: Twin) => validateDsl({ version: '1.1', title: 'Pat', operations: [{ op: 'ADD', ref: 'bed', roomId: 'bedroom', catalogId: 'bed-160', constraints: [{ type: 'againstWall' }] }] }, t, catalog).dsl!;

describe('approval', () => {
  it('preview does not touch the project; accept creates a revision after revalidation', () => {
    const project = createProject('p1', bedroom(), '2026-10-10T00:00:00Z');
    const before = fingerprint(project.twin);
    const prop = preview(project, 'prop-1', dslBed(project.twin), catalog, 'rules', '2026-10-10T00:01:00Z');
    expect(prop.status).toBe('PREVIEW');
    expect(fingerprint(project.twin)).toBe(before);
    expect(project.revisions).toHaveLength(1);
    const out = accept(project, prop, { now: '2026-10-10T00:02:00Z' });
    expect(out.accepted).toBe(true);
    if (!out.accepted) return;
    expect(out.revision.number).toBe(2);
    expect(out.revision.proposalId).toBe('prop-1');
    expect(out.revision.note).toBe('Pat');
    expect(out.project.twin.placements).toHaveLength(1);
    expect(fingerprint(out.project.twin)).toBe(prop.candidateFingerprint);
    expect(fingerprint(project.twin)).toBe(before); // the input project is immutable
  });
  it('rejects a stale proposal once the twin changed underneath it', () => {
    const project = createProject('p1', bedroom());
    const prop = preview(project, 'prop-1', dslBed(project.twin), catalog, 'ai');
    const other = preview(project, 'prop-2', dslBed(project.twin), catalog, 'ai');
    const first = accept(project, other); expect(first.accepted).toBe(true);
    const after = first.accepted ? first.project : project;
    const second = accept(after, prop);
    expect(second).toEqual({ accepted: false, reason: 'STALE' });
    expect(refreshStatus(after, prop).status).toBe('STALE');
    expect(refreshStatus(project, prop).status).toBe('PREVIEW');
  });
  it('a failed solve cannot be accepted; a rejected or applied proposal cannot be decided again', () => {
    const project = createProject('p1', bedroom());
    const bad = preview(project, 'x', validateDsl({ version: '1.1', operations: [{ op: 'ADD', ref: 's', roomId: 'bedroom', catalogId: 'sofa-huge' }] }, project.twin, catalog).dsl!, catalog, 'ai');
    expect(bad.status).toBe('FAILED');
    expect(accept(project, bad).accepted).toBe(false);
    expect((accept(project, bad) as { reason: string }).reason).toBe('FAILED');
    const prop = preview(project, 'y', dslBed(project.twin), catalog, 'ai');
    const r = reject(project, prop);
    expect(r.project).toBe(project);
    expect((accept(project, r.proposal) as { reason: string }).reason).toBe('ALREADY_DECIDED');
    expect((accept(project, markApplied(prop)) as { reason: string }).reason).toBe('ALREADY_DECIDED');
  });
  it('a WARNING needs explicit confirmation, an ERROR blocks even if the preview looked fine', () => {
    const project = createProject('p1', bedroom());
    const dsl = validateDsl({ version: '1.1', operations: [{ op: 'ADD', ref: 'w', roomId: 'bedroom', catalogId: 'wardrobe-200', constraints: [{ type: 'againstWall', wallId: project.twin.openings[1]!.wallId }] }] }, project.twin, catalog).dsl!;
    const prop = preview(project, 'w', dsl, catalog, 'ai');
    expect(prop.status).toBe('PREVIEW');
    expect(prop.result.validation.needsConfirmation).toBe(true); // tall wardrobe under the window
    const noConfirm = accept(project, prop);
    expect(noConfirm.accepted).toBe(false);
    expect((noConfirm as { reason: string }).reason).toBe('NEEDS_CONFIRMATION');
    const confirmed = accept(project, prop, { confirmWarnings: true });
    expect(confirmed.accepted).toBe(true);
    // Tamper with the stored candidate: the revalidation at accept time must catch it.
    const tampered = { ...prop, result: { ...prop.result, twin: { ...prop.result.twin, placements: [{ ...prop.result.twin.placements[0]!, x: 0, y: 0 }] } } };
    const blocked = accept(project, tampered, { confirmWarnings: true });
    expect(blocked.accepted).toBe(false);
    expect((blocked as { reason: string }).reason).toBe('ERROR');
  });
});
