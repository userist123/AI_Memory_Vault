// Graficul de execuție (cerut de cercetarea pe piață: fazarea lucrărilor și alocarea echipelor). Ordinea propusă vine din
// proiect; datele și duratele le dă utilizatorul (fără valori inventate, CONSTITUTION art. 4).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { checkSchedule, proposeSchedule, scheduleIssues, scheduleRange, ScheduleError, type Phase } from '../core/schedule';
import type { Snapshot } from '../core/types';

const ph = (id: string, start: string | null, end: string | null, after: string | null = null, trade: Phase['trade'] = 'general'): Phase => ({ id, title: id, trade, start, end, after, done: false });
const snap = (): Snapshot => ({ name: 'T', floor: { id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [{ id: 'r1', name: 'Baie', type: 'baie', rect: { x0: 0, z0: 0, x1: 3, z1: 2 } }],
  walls: [{ id: 'w1', a: [0, 0], b: [3, 0], thickness: .2, exterior: true, openings: [{ id: 'o1', kind: 'door', offset: 1, width: .8 }] }, { id: 'w2', a: [0, 2], b: [3, 2], thickness: .1, exterior: false, openings: [], status: 'demolish' }] },
  placements: [{ id: 'p1', roomId: 'r1', group: 'lavoar', variantId: 'lavoar-0', x: 1, z: 1, rotation: 0, source: 'manual' }], selections: {}, picked: [],
  tech: [{ id: 't1', kind: 'outlet', roomId: 'r1', x: 0, z: 1, height: .3, reason: '' }, { id: 't2', kind: 'water_cold', roomId: 'r1', x: 0, z: 1.5, height: .6, reason: '' }] });

test('checkSchedule: etape valide, date în ordine, predecesor existent, fără cicluri; texte curățate', () => {
  const ok = checkSchedule({ phases: [{ id: 'a', title: ' Demolări ', trade: 'general', start: '2026-12-01', end: '2026-12-03', after: null, extra: 1 }, { id: 'b', title: 'Instalații', trade: 'plumbing', start: null, end: null, after: 'a', done: true }] });
  assert.deepEqual(ok.phases, [ph('a', '2026-12-01', '2026-12-03'), { ...ph('b', null, null, 'a', 'plumbing'), title: 'Instalații', done: true }].map((p, i) => i === 0 ? { ...p, title: 'Demolări' } : p));
  const bad = (phases: unknown) => assert.throws(() => checkSchedule({ phases }), ScheduleError, JSON.stringify(phases));
  bad([{ id: 'a', title: 'x', trade: 'general', start: '2026-12-05', end: '2026-12-01' }]);
  bad([{ id: 'a', title: 'x', trade: 'zidar', start: null, end: null }]);
  bad([{ id: 'a', title: 'x', trade: 'general', start: null, end: null, after: 'nu-exista' }]);
  bad([{ id: 'a', title: 'x', trade: 'general', start: null, end: null, after: 'b' }, { id: 'b', title: 'y', trade: 'general', start: null, end: null, after: 'a' }]);
  bad([{ id: 'a', title: 'x', trade: 'general', start: null, end: null }, { id: 'a', title: 'y', trade: 'general', start: null, end: null }]);
  bad([{ id: 'a', title: '', trade: 'general', start: null, end: null }]);
  bad(Array.from({ length: 81 }, (_, i) => ({ id: `p${i}`, title: 'x', trade: 'general', start: null, end: null })));
});
test('proposeSchedule: ordinea uzuală, doar etapele pe care proiectul le cere, legate în lanț, fără date', () => {
  const p = proposeSchedule(snap(), k => k);
  const keys = p.map(x => x.title);
  assert.deepEqual(keys, ['works.ph.demolition', 'works.ph.electricRough', 'works.ph.plumbingRough', 'works.ph.plaster', 'works.ph.tiles', 'works.ph.paint', 'works.ph.flooring', 'works.ph.doors', 'works.ph.furniture', 'works.ph.electricFinish', 'works.ph.cleaning']);
  assert.ok(p.every(x => x.start === null && x.end === null), 'fără date inventate');
  p.forEach((x, i) => assert.equal(x.after, i === 0 ? null : p[i - 1]!.id));
  assert.equal(new Set(p.map(x => x.id)).size, p.length);
});
test('scheduleIssues: etapă care începe înaintea predecesorului, depășirea termenului, etape fără date', () => {
  const phases = [ph('a', '2026-12-01', '2026-12-05'), ph('b', '2026-12-04', '2026-12-10', 'a'), ph('c', null, null, 'b'), ph('d', '2026-12-11', '2027-01-20', 'b')];
  const is = scheduleIssues(phases, '2026-12-31');
  assert.deepEqual(is.map(i => `${i.code}:${i.phase ?? ''}`).sort(), ['BEFORE_PREDECESSOR:b', 'MISSING_DATES:', 'PAST_DUE:d'].sort());
  assert.equal(is.find(i => i.code === 'MISSING_DATES')!.count, 1);
  assert.deepEqual(scheduleIssues([ph('a', '2026-12-01', '2026-12-05'), ph('b', '2026-12-05', '2026-12-08', 'a')], null), [], 'începerea în ziua terminării predecesorului e permisă');
});
test('scheduleRange: de la primul început la ultimul sfârșit, inclusiv termenul; null fără date', () => {
  assert.deepEqual(scheduleRange([ph('a', '2026-12-03', '2026-12-05'), ph('b', '2026-12-01', '2026-12-20')], '2026-12-31'), { from: '2026-12-01', to: '2026-12-31', days: 31 });
  assert.equal(scheduleRange([ph('a', null, null)], null), null);
});

// ---------- server ----------
import { beforeAll } from 'vitest';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getSchedule, saveSchedule } from '../lib/schedule';
beforeAll(() => resetDbForTests());
const OWNER = '33333333-3333-4333-8333-333333333333', OTHER = '44444444-4444-4444-8444-444444444444';
test('server: graficul se salvează și se citește doar de proprietar; datele invalide dau 400; proiect nou = gol', async () => {
  const id = await repo.createProject(OWNER, 'Grafic', 'blank', 'ro');
  assert.deepEqual((await getSchedule(OWNER, id)).phases, []);
  const saved = await saveSchedule(OWNER, id, { phases: [ph('a', '2026-12-01', '2026-12-03'), ph('b', '2026-12-04', '2026-12-09', 'a', 'paint')] });
  assert.deepEqual(saved.phases.map(p => p.id), ['a', 'b']); assert.equal(saved.due, null);
  await assert.rejects(() => getSchedule(OTHER, id), (e: any) => e.status === 404);
  await assert.rejects(() => saveSchedule(OTHER, id, { phases: [] }), (e: any) => e.status === 404);
  await assert.rejects(() => saveSchedule(OWNER, id, { phases: [ph('a', '2026-12-05', '2026-12-01')] }), (e: any) => e.status === 400);
  assert.equal((await getSchedule(OWNER, id)).phases.length, 2, 'salvarea respinsă nu atinge graficul');
});
