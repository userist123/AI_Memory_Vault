import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
beforeAll(() => resetDbForTests());
const A = '11111111-1111-4111-8111-111111111111', B = '22222222-2222-4222-8222-222222222222';

test('catalogul e încărcat în baza de date cu proveniență', async () => {
  const c = await repo.getCatalog(); assert.equal(c.variants.length, 68); assert.equal(c.offers.length, 68);
  assert.ok(c.offers.every(o => o.provenance.sourceUrl && o.provenance.verifiedAt === '2026-09-30' && o.availability === 'UNKNOWN'));
});
test('flux complet: proiect nou → salvare → reîncărcare → revizie → modificare → restaurare', async () => {
  const id = await repo.createProject(A, 'Apartament test', 'demo');
  let p = await repo.getProject(A, id); assert.equal(p.draft.placements.length, 19);
  const r1 = await repo.createRevision(A, id, 'amenajare inițială'); assert.equal(r1.number, 1);
  const s = p.draft; s.floor.rooms.find(r => r.id === 'hol')!.name = 'Antreu'; s.floor.walls[5].openings[0].width = .9;
  await repo.saveDraft(A, id, s);
  p = await repo.getProject(A, id); assert.equal(p.draft.floor.rooms.find(r => r.id === 'hol')!.name, 'Antreu', 'modificarea camerei persistă după reîncărcare');
  assert.equal(p.draft.floor.walls[5].openings[0].width, .9, 'modificarea peretelui persistă');
  const ids = p.draft.placements.map(x => x.id);
  await repo.createRevision(A, id, 'antreu'); const back = await repo.restoreRevision(A, id, 1);
  assert.equal(back.floor.rooms.find(r => r.id === 'hol')!.name, 'Hol'); assert.deepEqual(back.placements.map(x => x.id), ids, 'ID-urile pieselor rămân stabile între revizii');
  assert.deepEqual((await repo.listRevisions(A, id)).map(r => r.number), [2, 1]);
});
test('revizia e refuzată dacă proiectul are erori', async () => {
  const id = await repo.createProject(A, 'Cu erori', 'demo'), p = await repo.getProject(A, id);
  const m = p.draft.placements.find(x => x.group === 'masuta')!; m.x = -3; await repo.saveDraft(A, id, p.draft);
  await assert.rejects(repo.createRevision(A, id, ''), (e: any) => e.status === 409);
});
test('proiectele sunt private: alt proprietar primește 404', async () => {
  const id = await repo.createProject(A, 'Privat', 'blank');
  await assert.rejects(repo.getProject(B, id), (e: any) => e.status === 404);
  assert.ok(!(await repo.listProjects(B)).some((r: any) => r.id === id));
});
test('date invalide sunt respinse', async () => {
  const id = await repo.createProject(A, 'Validare', 'blank');
  await assert.rejects(repo.saveDraft(A, id, { floor: {} }), (e: any) => e.status === 400);
  await assert.rejects(repo.createProject(A, '   ', 'blank'), (e: any) => e.status === 400);
  const p = await repo.getProject(A, id); p.draft.name = '<script>alert(1)</script>Casa'; await repo.saveDraft(A, id, p.draft);
  assert.ok(!(await repo.getProject(A, id)).name.includes('<'));
});

test('Faza 2: materialele, manopera și serviciile sunt în baza de date cu proveniență și dau același buget ca fișierul sursă', async () => {
  const mc = await repo.getMaterials(); assert.equal(mc.materials.length, 16); assert.equal(mc.labor.length, 6);
  assert.ok(mc.materials.every(m => m.verificationType && m.confidence)); assert.equal(mc.verifiedAt, '2026-10-01');
  const { computeBudget } = await import('../core/boq'); const json = (await import('../data/materials.v1.json')).default as any;
  const id = await repo.createProject(A, 'Buget DB', 'demo'), p = await repo.getProject(A, id), cat = await repo.getCatalog();
  assert.equal(computeBudget(p.draft, cat, mc).chosen.total, computeBudget(p.draft, cat, json).chosen.total);
  p.draft.budget = { target: 50000, contingencyPct: 15, includeLabor: true, laborScenario: 'high', deliveryIkea: true, deliveryDedeman: 200, furnitureAssembly: null, kitchenAssembly: true, design: 0 };
  await repo.saveDraft(A, id, p.draft); assert.equal((await repo.getProject(A, id)).draft.budget!.contingencyPct, 15);
  p.draft.budget.contingencyPct = 500; await assert.rejects(repo.saveDraft(A, id, p.draft), (e: any) => e.status === 400);
});
