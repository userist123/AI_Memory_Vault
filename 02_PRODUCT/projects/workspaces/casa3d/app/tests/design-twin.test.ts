import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import * as design from '../lib/design';
import * as share from '../lib/share';
beforeAll(() => resetDbForTests());
const A = '44444444-4444-4444-8444-444444444444', B = '55555555-5555-4555-8555-555555555555';

test('brief-ul twin e validat strict', async () => {
  const id = await repo.createProject(A, 'Brief', 'demo'), p = await repo.getProject(A, id);
  const keys = ['pat', 'noptiera', 'dulap'];
  assert.throws(() => design.checkTwinBrief({ roomId: 'nope', wants: ['pat'] }, p.draft, keys), (e: any) => e.status === 400);
  assert.throws(() => design.checkTwinBrief({ roomId: 'dormitor', wants: ['tanc'] }, p.draft, keys), (e: any) => e.status === 400);
  assert.throws(() => design.checkTwinBrief({ roomId: 'dormitor', wants: ['pat'], budget: -1 }, p.draft, keys), (e: any) => e.status === 400);
  const b = design.checkTwinBrief({ roomId: 'dormitor', wants: ['pat', 'pat', 'tanc', 'dulap'], budget: '5000', retailers: ['IKEA', 'x'], accessibility: 1 }, p.draft, keys);
  assert.deepEqual(b, { roomId: 'dormitor', wants: ['pat', 'dulap'], accessibility: true, replace: false, budget: 5000, retailers: ['IKEA'] });
});
test('generare → 3 variante deterministe, BOQ-aware, fără coordonate în DSL; previzualizarea nu atinge proiectul', async () => {
  const id = await repo.createProject(A, 'Dormitor twin', 'demo'); const before = (await repo.getProject(A, id)).draft;
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat', 'noptiera', 'dulap'], budget: 6000, replace: true });
  assert.equal(r.aiGenerated, false); assert.equal(r.alternatives.length, 3);
  assert.deepEqual(r.alternatives.map(a => a.title), ['Economic', 'Echilibrat', 'Premium']);
  for (const a of r.alternatives){
    assert.ok(a.ok, `${a.title}: ${JSON.stringify(a.outcomes)}`);
    assert.equal(a.placements.filter(p => p.roomId === 'dormitor').length, 3);
    assert.equal(a.placements.filter(p => p.roomId !== 'dormitor').length, before.placements.filter(p => p.roomId !== 'dormitor').length, 'cu replace, celelalte camere rămân neatinse');
    assert.ok(!JSON.stringify(a.dsl).match(/"x":|"y":|"position"/), 'DSL-ul nu conține coordonate');
    assert.ok(a.boq.budget && ['UNDER', 'OVER', 'UNKNOWN'].includes(a.boq.budget.status));
    assert.equal(a.boq.budget!.target, 6000);
  }
  assert.ok(r.alternatives[0].boq.knownTotal <= r.alternatives[2].boq.knownTotal || r.alternatives[2].boq.unknownCount > 0, 'Economic nu costă mai mult decât Premium la prețuri cunoscute');
  for (const a of r.alternatives) assert.deepEqual(a.boq.items.map(i => i.placementId).sort(), a.placements.filter(p => p.roomId === 'dormitor').map(p => p.id).sort(), `${a.title}: BOQ-ul conține doar piesele camerei vizate`);
  assert.equal(r.alternatives[0].boq.budget!.status, 'UNDER', 'un pat și două piese de dormitor încap în 6000 lei la prețuri de catalog');
  const again = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat', 'noptiera', 'dulap'], budget: 6000, replace: true });
  assert.deepEqual(again.alternatives.map(a => a.placements.map(p => [p.variantId, p.x, p.z, p.rotation])), r.alternatives.map(a => a.placements.map(p => [p.variantId, p.x, p.z, p.rotation])), 'același brief, aceleași poziții');
  assert.equal(again.baseFingerprint, r.baseFingerprint);
  assert.deepEqual((await repo.getProject(A, id)).draft.placements, before.placements, 'generarea nu modifică proiectul');
  const list = await design.listDesigns(A, id); assert.equal(list.length, 2); assert.ok(list.every(l => l.status === 'PREVIEW'));
});
test('aplicarea creează revizie și revalidează; o propunere stale e refuzată cu 409; deciziile nu se repetă', async () => {
  const id = await repo.createProject(A, 'Aplicare', 'demo');
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat', 'noptiera'], replace: true });
  await assert.rejects(design.decideDesign(B, id, r.id, 0, 'apply', false), (e: any) => e.status === 404, 'alt proprietar nu vede proiectul');
  await assert.rejects(design.decideDesign(A, id, r.id, 7, 'apply', false), (e: any) => e.status === 404);
  const applied = await design.decideDesign(A, id, r.id, 1, 'apply', true);
  assert.equal(applied.revision, 1);
  const p = await repo.getProject(A, id); assert.equal(p.currentRevision, 1);
  assert.equal(p.draft.placements.filter(x => x.roomId === 'dormitor').length, 2);
  assert.ok(p.draft.placements.some(x => x.roomId === 'living'), 'celelalte camere rămân neatinse');
  await assert.rejects(design.decideDesign(A, id, r.id, 1, 'apply', true), (e: any) => e.status === 409 && /deja/.test(e.message));
  // varianta 0 a fost generată pe twin-ul de dinainte de aplicare → stale
  await assert.rejects(design.decideDesign(A, id, r.id, 0, 'apply', true), (e: any) => e.status === 409 && /schimbat/.test(e.message));
  const list = await design.listDesigns(A, id); assert.equal(list[0].status, 'STALE');
  const r2 = await design.generateDesign(A, id, { roomId: 'living', wants: ['canapea', 'masuta'], replace: true });
  const rej = await design.decideDesign(A, id, r2.id, 2, 'reject', false); assert.deepEqual(rej, { ok: true });
  assert.equal((await repo.getProject(A, id)).currentRevision, 1, 'respingerea nu creează revizie');
});
test('partajarea arată exact revizia, fără proprietar; revocarea și tokenul greșit dau 404', async () => {
  const id = await repo.createProject(A, 'Share', 'demo');
  await assert.rejects(share.createShare(A, id, 1), (e: any) => e.status === 404, 'nu există încă revizia 1');
  await repo.createRevision(A, id, 'prima');
  const s = await share.createShare(A, id, 1); assert.match(s.token, /^[A-Za-z0-9_-]{43}$/); assert.equal(s.path, `/share/${s.token}`);
  const p = await repo.getProject(A, id); p.draft.placements = p.draft.placements.slice(0, 3); await repo.saveDraft(A, id, p.draft); await repo.createRevision(A, id, 'a doua');
  const v = await share.resolveShare(s.token);
  assert.equal(v.revisionNumber, 1); assert.equal(v.note, 'prima'); assert.equal(v.snapshot.placements.length, 19, 'revizia partajată nu se schimbă cu proiectul'); assert.equal(v.readOnly, true);
  await assert.rejects(share.createShare(B, id, 1), (e: any) => e.status === 404);
  await assert.rejects(share.resolveShare('short'), (e: any) => e.status === 404);
  await assert.rejects(share.resolveShare(s.token.slice(0, -1) + (s.token.endsWith('A') ? 'B' : 'A')), (e: any) => e.status === 404);
  assert.equal((await share.listShares(A, id)).length, 1);
  await share.revokeShare(A, id, s.token);
  await assert.rejects(share.resolveShare(s.token), (e: any) => e.status === 404);
  await assert.rejects(share.revokeShare(B, id, s.token), (e: any) => e.status === 404);
});
