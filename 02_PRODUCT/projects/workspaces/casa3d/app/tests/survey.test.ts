// Releveul de pe șantier: laturi opuse, diagonale → unghi, diferența față de plan; doar camere existente, doar proprietarul.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { listSurveys, saveSurvey, deleteSurvey } from '../lib/surveys';
import { analyzeSurvey, checkSurvey } from '../core/survey';
beforeAll(() => resetDbForTests());
const A = 'abababab-1111-4111-8111-abababababab', B = 'cdcdcdcd-1111-4111-8111-cdcdcdcdcdcd';

test('o cameră în echer, una strâmbă cu 1°, una mai mică decât în plan', () => {
  const rect = { x0: 0, z0: 0, x1: 4, z1: 3 };
  const ok = analyzeSurvey(rect, { n: 400, s: 400.5, e: 300, w: 300, d1: 500, d2: 500.4 }); assert.equal(ok.level, 'ok'); assert.ok(ok.angleOffDeg! < .1);
  // paralelogram 400×300 cu unghiul de 89°: diagonalele √(a²+b²±2ab·cos89°)
  const c = Math.cos(89 * Math.PI / 180), p = Math.sqrt(400 ** 2 + 300 ** 2 + 2 * 400 * 300 * c), q = Math.sqrt(400 ** 2 + 300 ** 2 - 2 * 400 * 300 * c);
  const skew = analyzeSurvey(rect, { n: 400, s: 400, e: 300, w: 300, d1: p, d2: q }); assert.ok(Math.abs(skew.angleOffDeg! - 1) < .05, String(skew.angleOffDeg)); assert.equal(skew.squareLevel, 'bad');
  const small = analyzeSurvey(rect, { n: 396, s: 396, e: 300, w: 300 }); assert.equal(small.vsPlan.width, -4); assert.equal(small.level, 'bad'); assert.equal(small.squareLevel, 'ok'); assert.equal(small.diagDiff, null);
});

test('salvare, înlocuire, validare și proprietar', async () => {
  const id = await repo.createProject(A, 'Releveu', 'demo'), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!.id;
  const s1 = await saveSurvey(A, id, room, { n: 412, e: 300, s: 411, w: 301, d1: 512, d2: 513, note: 'peretele N are glaf' });
  assert.equal(s1.roomId, room); assert.equal(s1.note, 'peretele N are glaf'); assert.ok(s1.measuredAt);
  await saveSurvey(A, id, room, { n: 410, e: 300, s: 410, w: 300 }); const list = await listSurveys(A, id);
  assert.equal(list.length, 1); assert.equal(list[0]!.n, 410); assert.equal(list[0]!.d1, undefined, 'înlocuită, nu amestecată');
  for (const bad of [{ n: 410, e: 300, s: 410 }, { n: 5, e: 300, s: 410, w: 300 }, { n: 410, e: 300, s: 410, w: 300, d1: 500 }, { n: '410', e: 300, s: 410, w: 300 }, null])
    await assert.rejects(saveSurvey(A, id, room, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  await assert.rejects(saveSurvey(A, id, 'nu-exista', { n: 410, e: 300, s: 410, w: 300 }), (e: any) => e.status === 400);
  await assert.rejects(saveSurvey(B, id, room, { n: 410, e: 300, s: 410, w: 300 })); await assert.rejects(listSurveys(B, id));
  await deleteSurvey(A, id, room); assert.equal((await listSurveys(A, id)).length, 0); await assert.rejects(deleteSurvey(A, id, room), (e: any) => e.status === 404);
  assert.equal(checkSurvey({ n: 410.27, e: 300, s: 410, w: 300 }).n, 410.3);
});
