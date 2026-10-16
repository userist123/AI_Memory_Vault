// Editor stages: phases cover every stage once and in order; "done" comes from project data, never from a manual flag.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { PHASES, STAGE_IDS, stageDone, nextStage, doneCount, phaseOf, type ProgressInput } from '../core/stage-progress';
import type { Snapshot } from '../core/types';

const room = (id: string) => ({ id, name: id, type: 'living' as const, rect: { x0: 0, x1: 3, z0: 0, z1: 3 } });
const wall = (id: string) => ({ id, a: [0, 0] as [number, number], b: [3, 0] as [number, number], thickness: .15, exterior: true, openings: [] });
const blank = (): Snapshot => ({ name: 'p', floor: { id: 'f0', name: 'Parter', ceilingHeight: 2.6, rooms: [], walls: [] }, placements: [], selections: {}, picked: [] });
const input = (house: Snapshot, o: Partial<ProgressInput> = {}): ProgressInput => ({ house, warnings: 0, shares: 0, status: 'design', ...o });

test('fazele acoperă toate etapele o singură dată, în ordinea editorului', () => {
  assert.deepEqual(PHASES.flatMap(p => p.stages), [...STAGE_IDS]);
  assert.deepEqual(PHASES.map(p => p.id), ['survey', 'concept', 'detail', 'delivery']);
  assert.equal(phaseOf('quote'), 'detail'); assert.equal(phaseOf('site'), 'delivery');
});

test('plan gol: nimic gata, nici măcar planșele (nu există plan)', () => {
  const d = stageDone(input(blank()));
  assert.equal(doneCount(d), 0);
});

test('fiecare etapă devine gata din datele ei', () => {
  const h = blank(); h.floor.rooms.push(room('r1'), room('r2')); h.floor.walls.push(wall('w1'));
  let d = stageDone(input(h, { warnings: 2 }));
  assert.equal(d.survey, true); assert.equal(d.docs, false, 'avertismente deschise');
  assert.equal(stageDone(input(h)).docs, true);
  h.finishes = { r1: { floor: 'a', wallPaint: 'b', light: 'c' } } as any;
  assert.equal(stageDone(input(h)).finishes, false, 'o cameră fără finisaje alese');
  h.finishes!.r2 = { floor: 'a', wallPaint: 'b', light: 'c' } as any;
  assert.equal(stageDone(input(h)).finishes, true);
  h.placements.push({ id: 'p1', group: 'canapea', variantId: 'v', roomId: 'r1', x: 1, z: 1, rotation: 0 } as any);
  h.tech = [{ id: 't', kind: 'outlet_double', roomId: 'r1', x: 0, z: 0 } as any];
  h.budget = { target: 50000 } as any; h.brief = { notes: 'tema clientului' } as any;
  d = stageDone(input(h, { shares: 1, status: 'execution' }));
  assert.deepEqual(STAGE_IDS.filter(s => !d[s]), ['site']);
  assert.equal(stageDone(input(h, { shares: 1, status: 'done' })).site, true);
  h.budget = { target: null } as any; assert.equal(stageDone(input(h)).quote, false, 'fără țintă de buget');
});

test('camerele de la etaj contează la finisaje', () => {
  const h = blank(); h.floor.rooms.push(room('r1')); h.floor.walls.push(wall('w1')); h.finishes = { r1: {} as any };
  h.levels = [{ id: 'f1', name: 'Etaj', ceilingHeight: 2.6, rooms: [room('r9')], walls: [] }];
  assert.equal(stageDone(input(h)).finishes, false);
});

test('Continuă duce la etapa următoare; pe ultima nu mai are unde', () => {
  assert.equal(nextStage('survey'), 'concept'); assert.equal(nextStage('ffe'), 'mep'); assert.equal(nextStage('site'), null);
});

test('linkurile revocate nu fac etapa Prezentare „gata”', async () => {
  const { activeShares } = await import('../core/stage-progress');
  assert.equal(activeShares([{ revokedAt: '2026-10-16T10:00:00Z' }]), 0);
  assert.equal(activeShares([{ revokedAt: null }, { revokedAt: '2026-10-16T10:00:00Z' }, {}]), 2);
  const h = blank(); h.floor.rooms.push(room('r1')); h.floor.walls.push(wall('w1'));
  assert.equal(stageDone(input(h, { shares: activeShares([{ revokedAt: '2026-10-01' }]) })).present, false);
});

test('Concept nu e „gata” doar pentru că un stil a umplut tema din valorile implicite', async () => {
  const { DEFAULT_BRIEF } = await import('../core/brief');
  const h = blank();
  h.brief = { ...DEFAULT_BRIEF, style: 'natural' }; // ce scrie core/styles.ts la aplicarea unui pachet de stil
  assert.equal(stageDone(input(h)).concept, false);
  h.brief = { ...DEFAULT_BRIEF, notes: 'Clientul lucrează de acasă' };
  assert.equal(stageDone(input(h)).concept, true);
  h.brief = { ...DEFAULT_BRIEF, occupants: 4 };
  assert.equal(stageDone(input(h)).concept, true);
  h.brief = { ...DEFAULT_BRIEF, priorities: ['depozitare' as any] };
  assert.equal(stageDone(input(h)).concept, true);
});
