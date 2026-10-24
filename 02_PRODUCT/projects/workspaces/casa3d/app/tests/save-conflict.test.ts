// Salvarea de pe două dispozitive: fiecare pornește de la o versiune; a doua salvare de la aceeași versiune nu mai trece
// tăcut (409 conflict); restaurarea unei revizii mută și ea versiunea; fără versiune (alegerea „păstrează ce am”) trece.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
beforeAll(() => resetDbForTests());
const A = '55555555-aaaa-4aaa-8aaa-555555555555';

test('două dispozitive, aceeași versiune: al doilea primește conflict, nu suprascrie', async () => {
  const id = await repo.createProject(A, 'Casa', 'demo'); const p = await repo.getProject(A, id), v0 = p.draftRev;
  const pc = { ...p.draft, name: 'Casa (PC)' }, phone = { ...p.draft, name: 'Casa (telefon)' };
  const r1 = await repo.saveDraft(A, id, pc, v0); assert.equal(r1.draftRev, v0 + 1);
  await assert.rejects(repo.saveDraft(A, id, phone, v0), (e: any) => e.status === 409 && e.details?.conflict === true);
  assert.equal((await repo.getProject(A, id)).name, 'Casa (PC)', 'salvarea de pe PC a rămas');
  // același dispozitiv continuă de la versiunea primită
  const r2 = await repo.saveDraft(A, id, { ...pc, name: 'Casa (PC 2)' }, r1.draftRev); assert.equal(r2.draftRev, v0 + 2);
  // restaurarea unei revizii schimbă desenul: o salvare veche nu mai trece
  await repo.createRevision(A, id, 'v1'); await repo.restoreRevision(A, id, 1);
  await assert.rejects(repo.saveDraft(A, id, pc, r2.draftRev), (e: any) => e.status === 409);
  // „păstrează ce am aici”: salvare fără versiune
  const r3 = await repo.saveDraft(A, id, phone); assert.equal((await repo.getProject(A, id)).name, 'Casa (telefon)'); assert.ok(r3.draftRev > r2.draftRev);
});
