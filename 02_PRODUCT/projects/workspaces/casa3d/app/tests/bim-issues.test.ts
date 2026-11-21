// Faza 5 din ghidul BIM (§3.7 flux 1–7): statusul constatărilor pe id-ul stabil, justificare obligatorie, comparație
// înainte/după, raport reproductibil; nimic nu dispare din raport ca să iasă „verde”.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { sanitizeIssues, compareRuns, withStatus, coordinationReport, ISSUE_STATUSES } from '../core/issues';
import { runCoordination } from '../core/coordination-review';
import { checkSnapshot, SNAPSHOT_KEYS } from '../lib/repo';
import type { Catalog, Floor, Snapshot } from '../core/types';

const cat: Catalog = { suppliers: [], products: [], variants: [], offers: [] };
const AT = '2026-11-21T10:00:00.000Z';
const floor = (): Floor => ({ id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [{ id: 'r1', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 6, z1: 4 } }],
  walls: [{ id: 'w1', a: [0, 0], b: [6, 0], thickness: .2, exterior: true, openings: [] }], columns: [{ id: 'c1', x: 3, z: 2, w: .4, d: .4, shape: 'rect' }],
  mepRuns: [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[3.1, 1, 1], [3.1, 1, 3]] }] });
const snap = (): Snapshot => ({ name: 'T', floor: floor(), placements: [], selections: {}, picked: [] });

test('sanitizeIssues: doar id-uri de constatare, statusuri cunoscute; acceptarea și fals-pozitivul cer justificare', () => {
  assert.deepEqual([...ISSUE_STATUSES], ['new', 'reviewed', 'accepted', 'resolved', 'false_positive']);
  const ok = sanitizeIssues({ 'cf-0000000a': { status: 'accepted', note: '  traversare cu manșon aprobată  ', by: 'ing. X', at: AT, extra: 1 } });
  assert.deepEqual(ok, { 'cf-0000000a': { status: 'accepted', note: 'traversare cu manșon aprobată', by: 'ing. X', at: AT } });
  assert.throws(() => sanitizeIssues({ 'cf-0000000a': { status: 'accepted', at: AT } }), /justificare/i);
  assert.throws(() => sanitizeIssues({ 'cf-0000000a': { status: 'false_positive', note: ' ', at: AT } }), /justificare/i);
  assert.throws(() => sanitizeIssues({ 'x': { status: 'reviewed', at: AT } }), /id/i);
  assert.throws(() => sanitizeIssues({ 'cf-0000000a': { status: 'ignorat', at: AT } }), /status/i);
  assert.deepEqual(sanitizeIssues({ 'cf-0000000a': { status: 'new', at: AT } }), {}, '„new” e implicit: nu se salvează');
});
test('checkSnapshot păstrează statusurile valide; cheia issues e în lista modelului', () => {
  assert.ok((SNAPSHOT_KEYS as readonly string[]).includes('issues'));
  const s: any = snap(); s.issues = { 'cf-0000000b': { status: 'reviewed', at: AT } };
  assert.deepEqual(checkSnapshot(structuredClone(s)).issues, { 'cf-0000000b': { status: 'reviewed', at: AT } });
  s.issues = { 'cf-0000000b': { status: 'accepted', at: AT } }; assert.throws(() => checkSnapshot(structuredClone(s)));
});
test('withStatus: statusul salvat se leagă de constatare; „rezolvat” care încă apare e semnalat, nu ascuns', () => {
  const s = snap(), run = runCoordination(s, cat, { revision: 1 });
  const hard = run.findings.find(f => f.code === 'CLASH_HARD')!;
  const out = withStatus(run.findings, { [hard.id]: { status: 'resolved', at: AT } });
  const h = out.find(f => f.id === hard.id)!;
  assert.equal(h.status, 'resolved'); assert.equal(h.stillPresent, true);
  assert.equal(out.length, run.findings.length, 'nicio constatare nu dispare din listă');
  assert.ok(out.filter(f => f.id !== hard.id).every(f => f.status === 'new'));
});
test('compareRuns: constatări noi, dispărute și rămase între două rulări', () => {
  const before = runCoordination(snap(), cat, { revision: 1 });
  const s = snap(); s.floor.mepRuns![0]!.points = [[4, 1, 1], [4, 1, 3]];
  const after = runCoordination(s, cat, { revision: 2 });
  const c = compareRuns(before.findings, after.findings);
  assert.ok(c.gone.some(f => f.code === 'CLASH_HARD'), 'traseul mutat nu mai trece prin stâlp');
  assert.equal(c.added.filter(f => f.code === 'CLASH_HARD').length, 0);
  assert.equal(c.gone.length + c.kept.length, before.findings.length);
  assert.equal(c.added.length + c.kept.length, after.findings.length);
});
test('raportul: revizia, regulile rulate și nerulate, fiecare constatare cu id, elemente, status și justificare; reproductibil', () => {
  const s = snap(), run = runCoordination(s, cat, { revision: 4 });
  const hard = run.findings.find(f => f.code === 'CLASH_HARD')!;
  const issues = { [hard.id]: { status: 'accepted' as const, note: 'aprobat de structurist', by: 'ing. X', at: AT } };
  const md = coordinationReport(run, issues, { project: 'Test', generatedAt: AT });
  assert.match(md, /revizia 4/); assert.match(md, /casa3d\.clash\.mep-structure/); assert.match(md, /Nerulate/);
  assert.match(md, new RegExp(`${hard.id}.*c1.*m1.*accepted.*aprobat de structurist`));
  assert.equal(coordinationReport(run, issues, { project: 'Test', generatedAt: AT }), md);
});

import { cell } from '../core/issues';
test('raport: celula escapează backslash-ul înaintea barei, ca tabelul Markdown să nu se rupă', () => {
  assert.equal(cell('a|b'), 'a\\|b');
  assert.equal(cell('cale\\'), 'cale\\\\');
  assert.equal(cell('x\\|y'), 'x\\\\\\|y');
  assert.equal(cell('r1\r\nr2'), 'r1 r2');
});
