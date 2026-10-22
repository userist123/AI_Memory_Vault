// P0 (SALES_READINESS_PLAN 3.1.4): linkurile noi expiră implicit după 90 de zile; un link expirat nu mai merge nicăieri
// (vizualizare, păreri, alegeri, modificări, progres, PIN), cu același 404 ca unul inexistent. Linkurile vechi (fără
// expires_at) rămân valabile. Designerul vede expirarea și ultima deschidere (rotunjită la oră, fără IP).
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, listShares, resolveShare, clientLink, SHARE_DEFAULT_DAYS } from '../lib/share';
import { addFeedback, listFeedbackForShare } from '../lib/feedback';
import { chooseOption } from '../lib/room-choices';
import { listChangesForShare } from '../lib/change-orders';
import { progressForShare } from '../lib/client-progress';
import { setSharePin, verifySharePin } from '../lib/share-pin';
beforeAll(() => resetDbForTests());
const A = '99999999-9999-4999-8999-999999999999';
const day = 864e5, is404 = (e: any) => e?.status === 404;

async function shared(days?: unknown){
  const id = await repo.createProject(A, 'Expirare', 'demo'), { number } = await repo.createRevision(A, id, 'Predat clientului');
  return { id, ...(await createShare(A, id, number, days)) };
}
const expire = async (token: string) => (await getDb()).q(`update shares set expires_at = now() - interval '1 minute' where token=$1`, [token]);

test('implicit 90 de zile; 0 = fără expirare; valori invalide → 400', async () => {
  const s = await shared(); const left = new Date(s.expiresAt!).getTime() - Date.now();
  assert.ok(Math.abs(left - SHARE_DEFAULT_DAYS * day) < 60_000, `expiră în ~90 de zile (${left / day})`);
  assert.equal((await shared(0)).expiresAt, null); assert.ok(Math.abs(new Date((await shared(7)).expiresAt!).getTime() - Date.now() - 7 * day) < 60_000);
  for (const v of [-1, 366, 1.5, 'x']) await assert.rejects(shared(v), (e: any) => e.status === 400, String(v));
});
test('linkul expirat dă 404 pe toate rutele publice, ca unul inexistent', async () => {
  const s = await shared(), room = (await resolveShare(s.token)).snapshot.floor.rooms[0].id;
  // martor: pe linkul activ aceleași apeluri merg (sau eșuează din alt motiv decât 404), deci 404-ul de mai jos vine din expirare
  await (await getDb()).q(`update projects set meta = coalesce(meta, '{}'::jsonb) || '{"shareOrders": true}'::jsonb where id=$1`, [s.id]);
  await listFeedbackForShare(s.token); await listChangesForShare(s.token); assert.ok(await progressForShare(s.token), 'progresul se vede pe linkul activ');
  await assert.rejects(chooseOption(s.token, { roomId: room, optionId: 'nu-exista' }), (e: any) => e.status !== 404, 'alegerea pe link activ nu dă 404');
  const { pin } = await setSharePin(A, s.id, s.token);
  await expire(s.token);
  await assert.rejects(resolveShare(s.token), is404);
  await assert.rejects(addFeedback(s.token, { author: 'Ana', text: 'Salut' }), is404);
  await assert.rejects(listFeedbackForShare(s.token), is404);
  await assert.rejects(chooseOption(s.token, { roomId: room, optionId: 'nu-exista' }), is404);
  await assert.rejects(listChangesForShare(s.token), is404);
  assert.equal(await progressForShare(s.token), null);
  await assert.rejects(verifySharePin(s.token, pin), is404);
  await assert.rejects(setSharePin(A, s.id, s.token), is404);
});
test('linkurile vechi (fără expires_at) rămân valabile', async () => {
  const s = await shared(); await (await getDb()).q('update shares set expires_at = null where token=$1', [s.token]);
  assert.equal((await resolveShare(s.token)).revisionNumber, 1);
});
test('designerul vede expirarea, starea activă și ultima deschidere (o scriere pe oră)', async () => {
  const s = await shared(); let row = (await listShares(A, s.id))[0];
  assert.equal(row.active, true); assert.equal(row.lastAccessAt, null); assert.ok(row.expiresAt);
  await resolveShare(s.token); row = (await listShares(A, s.id))[0];
  const t = new Date(row.lastAccessAt).getTime(); assert.ok(t <= Date.now() && Date.now() - t < 3600_000); assert.equal(new Date(t).getUTCMinutes(), 0, 'rotunjită la oră');
  await expire(s.token); assert.equal((await listShares(A, s.id))[0].active, false);
});
test('„Link pentru client” nu refolosește un link expirat', async () => {
  const s = await shared(), first = await clientLink(A, s.id); assert.equal(first.token, s.token, 'linkul activ se refolosește');
  await expire(s.token); const next = await clientLink(A, s.id);
  assert.notEqual(next.token, s.token); await resolveShare(next.token);
});
