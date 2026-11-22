// Situațiile de lucrări (core/progress.ts): deviz de bază înghețat, cantități pe perioade, cumulate, rest, procente,
// lucrări suplimentare pe linii proprii, avans recuperat și reținere; validare strictă.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { checkBaseline, checkStatement, computeStatement, statementIssues, ProgressError, type Baseline, type StatementInput } from '../core/progress';

const base: Baseline = checkBaseline({ createdAt: '2026-11-01T10:00:00Z', lines: [
  { key: 'lab:plaster', label: 'Tencuială', unit: 'mp', qty: 100, unitPrice: 40, kind: 'labor' },
  { key: 'lab:tiles', label: 'Placare gresie', unit: 'mp', qty: 20, unitPrice: 90, kind: 'labor' },
  { key: 'mat:adeziv', label: 'Adeziv', unit: 'sac', qty: 10, unitPrice: 55.5, kind: 'material' }] });
const st = (number: number, from: string, to: string, qty: Record<string, number>, extra: any[] = [], p: Partial<StatementInput> = {}) => checkStatement({ id: `s${number}`, number, from, to, qty, extra, ...p }, base);

test('situația 1 și 2: cumulat, rest, procente, valori pe perioadă și cumulate', () => {
  const all = [st(1, '2026-11-01', '2026-11-15', { 'lab:plaster': 60, 'mat:adeziv': 4 }), st(2, '2026-11-16', '2026-11-30', { 'lab:plaster': 40, 'lab:tiles': 5 })];
  const s2 = computeStatement(base, all, 2), p = s2.rows.find(r => r.key === 'lab:plaster')!;
  assert.deepEqual([p.prevQty, p.thisQty, p.cumQty, p.remainingQty, p.pct, p.thisValue, p.cumValue], [60, 40, 100, 0, 100, 1600, 4000]);
  const t = s2.rows.find(r => r.key === 'lab:tiles')!; assert.deepEqual([t.cumQty, t.remainingQty, t.pct], [5, 15, 25]);
  assert.equal(s2.totals.contractValue, 100 * 40 + 20 * 90 + 10 * 55.5);
  assert.equal(s2.totals.thisValue, 1600 + 450); assert.equal(s2.totals.cumValue, 4000 + 450 + 222);
  assert.equal(s2.totals.prevValue, 60 * 40 + 4 * 55.5);
  assert.deepEqual(s2.warnings, []);
});
test('depășirea cantității din deviz e semnalată (nu blocată); lucrările suplimentare intră în valoarea perioadei, nu în deviz', () => {
  const all = [st(1, '2026-11-01', '2026-11-15', { 'lab:tiles': 25 }, [{ key: 'x:nisa', label: 'Nișă suplimentară', unit: 'buc', qty: 1, unitPrice: 300 }])];
  const s = computeStatement(base, all, 1); assert.deepEqual(s.warnings, [{ key: 'lab:tiles', code: 'OVER' }]);
  assert.equal(s.extra.length, 1); assert.equal(s.totals.extraThis, 300); assert.equal(s.totals.thisValue, 25 * 90 + 300);
  assert.equal(s.totals.cumValue, 25 * 90, 'cumulatul pe deviz nu include suplimentarele');
});
test('avans recuperat și reținere: procente din valoarea perioadei; de plată = valoare − avans − reținere', () => {
  const s = computeStatement(base, [st(1, '2026-11-01', '2026-11-15', { 'lab:plaster': 50 }, [], { advancePct: 30, retentionPct: 5 })], 1);
  assert.deepEqual([s.totals.thisValue, s.totals.advance, s.totals.retention, s.totals.vat, s.totals.payable], [2000, 600, 100, null, 1300]);
  const v = computeStatement(base, [st(1, '2026-11-01', '2026-11-15', { 'lab:plaster': 50 }, [], { advancePct: 30, retentionPct: 5, vatPct: 21 })], 1);
  assert.deepEqual([v.totals.vat, v.totals.payable], [294, 1594], 'TVA pe valoarea după avans; reținerea nu scade baza TVA');
});
test('validare: cod necunoscut ignorat, cantitate negativă refuzată, perioadă inversă, procente peste 100, linii dublate', () => {
  assert.deepEqual(st(1, '2026-11-01', '2026-11-02', { 'nu-exista': 5, 'lab:plaster': 0 }).qty, {});
  assert.throws(() => st(1, '2026-11-01', '2026-11-02', { 'lab:plaster': -1 }), ProgressError);
  assert.throws(() => st(1, '2026-11-05', '2026-11-02', {}), ProgressError);
  assert.throws(() => st(1, '2026-11-01', '2026-11-02', {}, [], { retentionPct: 120 }), ProgressError);
  assert.throws(() => st(1, '2026-11-01', '2026-11-02', {}, [], { advancePct: 90, retentionPct: 50 }), ProgressError, 'avans + garanție > 100% ar da „de plată” negativ');
  assert.throws(() => checkStatement({ id: 'a/b', number: 1, from: '2026-11-01', to: '2026-11-02', qty: {}, extra: [] }, base), ProgressError, 'id-ul intră în URL');
  assert.throws(() => checkBaseline({ createdAt: '2026-11-01', lines: [{ key: 'a', label: 'A', unit: 'mp', qty: 1, unitPrice: 1, kind: 'labor' }, { key: 'a', label: 'B', unit: 'mp', qty: 1, unitPrice: 1, kind: 'labor' }] }), ProgressError);
  assert.equal(checkBaseline({ createdAt: '2026-11-01', lines: [{ key: 'a', label: '<b>A</b>', unit: 'mp', qty: 1, unitPrice: 1, kind: 'labor' }] }).lines[0]!.label, 'b A /b');
});
test('statementIssues: numere lipsă și perioade suprapuse', () => {
  assert.deepEqual(statementIssues([st(1, '2026-11-01', '2026-11-15', {}), st(3, '2026-11-10', '2026-11-30', {})]).sort((a, b) => a.code.localeCompare(b.code)), [{ code: 'GAP', number: 3 }, { code: 'OVERLAP', number: 3 }]);
});
test('foaia de atașament: măsurătorile se păstrează curățate; suma diferită de cantitatea perioadei e semnalată', () => {
  const ok = st(1, '2026-11-01', '2026-11-15', { 'lab:plaster': 30 }, [], { measures: { 'lab:plaster': [{ label: 'Living perete N', qty: 12.5 }, { label: 'Living perete E', qty: 17.5 }], 'nu-exista': [{ label: 'x', qty: 1 }] } } as any);
  assert.deepEqual(Object.keys(ok.measures!), ['lab:plaster']); assert.deepEqual(computeStatement(base, [ok], 1).warnings, []);
  const bad = st(1, '2026-11-01', '2026-11-15', { 'lab:plaster': 30 }, [], { measures: { 'lab:plaster': [{ label: 'Living', qty: 10 }] } } as any);
  assert.deepEqual(computeStatement(base, [bad], 1).warnings, [{ key: 'lab:plaster', code: 'MEASURES' }]);
  assert.throws(() => st(1, '2026-11-01', '2026-11-15', {}, [], { measures: { 'lab:plaster': [{ label: '', qty: 1 }] } } as any), ProgressError);
});
test('devizul cu prețuri care includ TVA: situația nu mai adaugă TVA, chiar dacă procentul e completat', () => {
  const b2 = checkBaseline({ createdAt: '2026-11-01', vatIncluded: true, lines: [{ key: 'a', label: 'A', unit: 'mp', qty: 10, unitPrice: 100, kind: 'labor' }] });
  assert.equal(b2.vatIncluded, true);
  const s = computeStatement(b2, [checkStatement({ id: 's1', number: 1, from: '2026-11-01', to: '2026-11-02', qty: { a: 5 }, extra: [], vatPct: 21 }, b2)], 1);
  assert.equal(s.totals.vat, null); assert.equal(s.totals.payable, 500);
});
