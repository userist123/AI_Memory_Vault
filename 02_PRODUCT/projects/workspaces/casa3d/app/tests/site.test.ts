// Șantierul: remedieri numerotate, cu cameră, meserie, stare și poze; pozele verificate după conținut și doar ale proiectului.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { addSnag, listSnags, updateSnag, deleteSnag, addPhoto, readPhoto } from '../lib/site';
import { sniffImage, SITE_LIMITS } from '../core/site';
beforeAll(() => resetDbForTests());
const A = 'cccccccc-3333-4333-8333-333333333333', B = 'dddddddd-4444-4444-8444-444444444444';
const JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16, 0x4a, 0x46, 0x49, 0x46, 0, 1]), PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13]);

test('tipul imaginii după conținut, nu după nume', () => {
  assert.equal(sniffImage(JPEG), 'image/jpeg'); assert.equal(sniffImage(PNG), 'image/png');
  assert.equal(sniffImage(new TextEncoder().encode('RIFF\0\0\0\0WEBPVP8 ')), 'image/webp');
  for (const bad of ['<svg onload=alert(1)>', '<html>', 'GIF89a......', '%PDF-1.7']) assert.equal(sniffImage(new TextEncoder().encode(bad)), null, bad);
});
test('remedieri: numerotare, cameră și meserie validate, stare cu data rezolvării, poze proprii', async () => {
  const id = await repo.createProject(A, 'Șantier', 'demo'), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!.id;
  const ph = await addPhoto(A, id, JPEG); assert.equal(ph.mime, 'image/jpeg');
  const s1 = await addSnag(A, id, { title: ' Priză strâmbă‮ ', roomId: room, trade: 'electric', photos: [ph.id] });
  const s2 = await addSnag(A, id, { title: 'Rost gresie lipsă', trade: 'tiles' });
  assert.equal(s1.number, 1); assert.equal(s2.number, 2); assert.equal(s1.title, 'Priză strâmbă'); assert.equal(s1.status, 'open'); assert.deepEqual(s1.photos, [ph.id]);
  const f = await updateSnag(A, id, s1.id, { status: 'fixed' }); assert.ok(f.resolvedAt);
  const f2 = await updateSnag(A, id, s1.id, { status: 'fixed', body: 'refăcut' }); assert.equal(f2.resolvedAt, f.resolvedAt, 'aceeași stare nu schimbă data rezolvării');
  assert.equal((await updateSnag(A, id, s1.id, { status: 'open' })).resolvedAt, null);
  for (const bad of [{ title: '' }, { title: 'x', trade: 'astronaut' }, { title: 'x', roomId: 'nu-exista' }, { title: 'x', status: 'gata' }, { title: 'x', photos: ['nu-e-uuid'] },
    { title: 'x', photos: Array.from({ length: SITE_LIMITS.photosPerSnag + 1 }, () => ph.id) }])
    await assert.rejects(addSnag(A, id, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  // poza altui proiect nu poate fi atașată
  const other = await repo.createProject(A, 'Alt proiect', 'demo'), foreign = await addPhoto(A, other, PNG);
  await assert.rejects(addSnag(A, id, { title: 'x', photos: [foreign.id] }), (e: any) => e.status === 400);
  await assert.rejects(readPhoto(A, id, foreign.id), (e: any) => e.status === 404);
  // alt proprietar nu vede nimic
  await assert.rejects(listSnags(B, id)); await assert.rejects(readPhoto(B, id, ph.id)); await assert.rejects(addPhoto(B, id, JPEG)); await assert.rejects(updateSnag(B, id, s1.id, { status: 'fixed' }));
  assert.deepEqual((await readPhoto(A, id, ph.id)).data, JPEG);
  await deleteSnag(A, id, s1.id); assert.deepEqual((await listSnags(A, id)).map(s => s.number), [2]);
  await assert.rejects(readPhoto(A, id, ph.id), (e: any) => e.status === 404, 'poza remedierii șterse dispare');
  assert.equal((await addSnag(A, id, { title: 'nou' })).number, 3, 'numerele nu se refolosesc');
});
test('poze: doar imagini, cu mărime limitată', async () => {
  const id = await repo.createProject(A, 'Poze', 'demo');
  await assert.rejects(addPhoto(A, id, new TextEncoder().encode('<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>')), (e: any) => e.status === 415);
  const big = new Uint8Array(SITE_LIMITS.photoBytes + 1); big.set(JPEG); await assert.rejects(addPhoto(A, id, big), (e: any) => e.status === 413);
});

test('regresii: numerele nu se refolosesc după ștergerea ultimei, data rezolvării rămâne la verificare, pozele scoase se șterg', async () => {
  const id = await repo.createProject(A, 'Regresii', 'demo');
  const s1 = await addSnag(A, id, { title: 'unu' }), s2 = await addSnag(A, id, { title: 'doi' }); await deleteSnag(A, id, s2.id);
  assert.equal((await addSnag(A, id, { title: 'trei' })).number, 3, 'după ștergerea lui #2, următorul e #3');
  const fixed = await updateSnag(A, id, s1.id, { status: 'fixed' }); const ver = await updateSnag(A, id, s1.id, { status: 'verified' });
  assert.equal(ver.resolvedAt, fixed.resolvedAt, 'verificarea nu schimbă data rezolvării');
  const ph = await addPhoto(A, id, JPEG); await updateSnag(A, id, s1.id, { photos: [ph.id] }); await updateSnag(A, id, s1.id, { photos: [] });
  await assert.rejects(readPhoto(A, id, ph.id), (e: any) => e.status === 404, 'poza scoasă din remediere e ștearsă');
});
