// Pontajul: înregistrări validate, rezumat pe etape, tarif efectiv față de onorariu; doar proprietarul.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getTimesheet, addTime, deleteTime } from '../lib/timesheet';
import { parseDuration, timeSummary } from '../core/timesheet';
beforeAll(() => resetDbForTests());
const A = '34343434-1111-4111-8111-343434343434', B = '56565656-1111-4111-8111-565656565656';

test('durata scrisă liber', () => {
  assert.equal(parseDuration('1,5'), 90); assert.equal(parseDuration('1:30'), 90); assert.equal(parseDuration('45m'), 45); assert.equal(parseDuration('2h'), 120); assert.equal(parseDuration('2'), 120);
  for (const bad of ['', 'abc', '1:75', '-1']) assert.equal(parseDuration(bad), null, bad);
});
test('rezumat: pe etape, tarif efectiv la onorariu fix, acumulat la onorariu pe oră', () => {
  const e = [{ minutes: 120, kind: 'concept' as const }, { minutes: 180, kind: 'docs' as const }, { minutes: 60, kind: 'concept' as const }];
  const fixed = timeSummary(e, { kind: 'fixed', amount: 3000 }, 3000); assert.equal(fixed.hours, 6); assert.equal(fixed.byKind.concept, 180); assert.equal(fixed.effectiveRate, 500); assert.equal(fixed.billable, null);
  const hourly = timeSummary(e, { kind: 'hourly', amount: 150 }, null); assert.equal(hourly.billable, 900); assert.equal(hourly.effectiveRate, null);
  assert.equal(timeSummary([], { kind: 'fixed', amount: 3000 }, 3000).effectiveRate, null, 'fără ore nu se împarte la zero');
});
test('înregistrări: adăugare, validare, ștergere, proprietar', async () => {
  const id = await repo.createProject(A, 'Pontaj', 'demo'); await repo.saveMeta(A, id, { status: 'design', fee: { kind: 'fixed', amount: 4000 } });
  await addTime(A, id, { date: '2026-10-10', minutes: 150, kind: 'concept', note: 'moodboard' }); const e2 = await addTime(A, id, { date: '2026-10-11', minutes: 90, kind: 'site' });
  let ts = await getTimesheet(A, id); assert.equal(ts.entries.length, 2); assert.equal(ts.entries[0]!.date, '2026-10-11'); assert.equal(ts.summary.hours, 4); assert.equal(ts.summary.effectiveRate, 1000);
  for (const bad of [{ date: '2026-13-01', minutes: 60, kind: 'concept' }, { date: '2026-10-10', minutes: 2, kind: 'concept' }, { date: '2026-10-10', minutes: 60.5, kind: 'concept' }, { date: '2026-10-10', minutes: 60, kind: 'nap' }, null])
    await assert.rejects(addTime(A, id, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  await assert.rejects(getTimesheet(B, id)); await assert.rejects(addTime(B, id, { date: '2026-10-10', minutes: 60, kind: 'concept' })); await assert.rejects(deleteTime(B, id, e2.id));
  await deleteTime(A, id, e2.id); ts = await getTimesheet(A, id); assert.equal(ts.entries.length, 1); await assert.rejects(deleteTime(A, id, e2.id), (e: any) => e.status === 404);
});
