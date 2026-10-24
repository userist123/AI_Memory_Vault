// Alegerea clientului între variantele unei camere, pe linkul de prezentare; designerul vede ultima alegere pe cameră.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare } from '../lib/share';
import { chooseOption, listChoicesForShare, listChoices } from '../lib/room-choices';
import { captureOption } from '../core/room-options';
beforeAll(() => resetDbForTests());
const A = '77777777-7777-4777-8777-777777777777', B = '88888888-8888-4888-8888-888888888888', NOW = '2026-10-11T20:00:00.000Z';

async function shared(){
  const id = await repo.createProject(A, 'Variante', 'demo'), p = await repo.getProject(A, id), room = p.draft.floor.rooms.find((r: any) => r.type === 'living')!.id;
  const snap = captureOption(captureOption(p.draft, room, 'Ca în ofertă', 'opt-a', NOW), room, 'Mai luminos', 'opt-b', NOW);
  await repo.saveDraft(A, id, snap);
  const saved = (await repo.getProject(A, id)).draft; assert.deepEqual(saved.roomOptions![room]!.map((o: any) => o.label), ['A', 'B'], 'variantele trec de validarea la salvare');
  const { number } = await repo.createRevision(A, id, 'Predat clientului'), { token } = await createShare(A, id, number);
  return { id, token, room };
}

test('clientul alege B, apoi se răzgândește pe A; designerul vede ultima alegere cu revizia', async () => {
  const { id, token, room } = await shared();
  const c = await chooseOption(token, { roomId: room, optionId: 'opt-b', author: ' Ana ', note: 'Îmi place parchetul.' });
  assert.equal(c.label, 'B'); assert.equal(c.author, 'Ana');
  await chooseOption(token, { roomId: room, optionId: 'opt-a', author: 'Ana', label: 'C' });   // eticheta o pune serverul
  const pub = await listChoicesForShare(token); assert.equal(pub.length, 1); assert.equal(pub[0]!.label, 'A');
  const own = await listChoices(A, id); assert.equal(own.length, 1); assert.equal(own[0]!.optionId, 'opt-a'); assert.equal(own[0]!.revisionNumber, 1);
});

test('variantă sau cameră necunoscută, fără nume, link revocat sau alt proprietar: refuzat', async () => {
  const { id, token, room } = await shared();
  await assert.rejects(chooseOption(token, { roomId: room, optionId: 'nu-exista', author: 'Ana' }), /Varianta nu există/);
  await assert.rejects(chooseOption(token, { roomId: 'fantoma', optionId: 'opt-a', author: 'Ana' }), /Camera nu există/);
  await assert.rejects(chooseOption(token, { roomId: room, optionId: 'opt-a', author: '   ' }), /numele/);
  await assert.rejects(listChoices(B, id), (e: any) => e.status === 404);
  await revokeShare(A, id, token);
  await assert.rejects(chooseOption(token, { roomId: room, optionId: 'opt-a', author: 'Ana' }), (e: any) => e.status === 404);
});
