// UI/UX P1-3: imaginile 3D ale linkului, randate o dată de designer și servite ca fișiere. Doar WebP/JPEG verificate după
// octeți, doar camere din revizie, doar proprietarul scrie; linkul inactiv (revocat, expirat) nu mai dă nicio imagine.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare } from '../lib/share';
import { parseRenderImage, isRenderKey, renderKeys, revisionRenderState, saveRevisionRender, shareRenderSet, shareRender, RENDER_MAX_BYTES } from '../lib/share-renders';
import { missingRenders } from '../lib/share-images';
import { isPublicPath } from '../lib/public-gate';
beforeAll(() => resetDbForTests());
const A = '11111111-1111-4111-8111-111111111111', B = '22222222-2222-4222-8222-222222222222';
const webp = (n = 64) => { const b = Buffer.alloc(n); b.write('RIFF', 0, 'latin1'); b.write('WEBP', 8, 'latin1'); return `data:image/webp;base64,${b.toString('base64')}`; };
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4]).toString('base64');

test('imaginea: doar WebP sau JPEG adevărate, cel mult 1,2 MB', () => {
  assert.equal(parseRenderImage(webp())?.mime, 'image/webp');
  assert.equal(parseRenderImage(`data:image/jpeg;base64,${jpeg}`)?.mime, 'image/jpeg');
  assert.equal(parseRenderImage(`data:image/webp;base64,${jpeg}`), null, 'tipul declarat trebuie să se potrivească octeților');
  assert.equal(parseRenderImage('data:image/png;base64,iVBORw0KGgo='), null, 'PNG nu');
  assert.equal(parseRenderImage('data:image/svg+xml;base64,PHN2Zz4='), null, 'SVG nu (script)');
  assert.equal(parseRenderImage(webp(RENDER_MAX_BYTES + 1)), null, 'prea mare');
  for (const bad of [null, 42, '', 'data:image/webp;base64,!!!', webp() + '\n']) assert.equal(parseRenderImage(bad), null, String(bad).slice(0, 30));
});
test('cheile: vederea de ansamblu și camerele; ce lipsește = fără ambele lățimi', async () => {
  for (const k of ['overview', 'living', 'r-1_a']) assert.ok(isRenderKey(k), k);
  for (const k of ['', '../x', 'a/b', 'x'.repeat(81), 'a b', 1]) assert.ok(!isRenderKey(k), String(k));
  assert.deepEqual(missingRenders(['overview', 'a', 'b'], { overview: [800, 1600], a: [1600] }), ['a', 'b']);
  const id = await repo.createProject(A, 'Chei', 'demo'), snap = (await repo.getProject(A, id)).draft;
  assert.deepEqual(renderKeys(snap), ['overview', ...snap.floor.rooms.map(r => r.id)]);
});
test('poarta publică: imaginea linkului e publică, căile ciudate nu', () => {
  const t = 'a'.repeat(43);
  assert.ok(isPublicPath(`/api/share/${t}/render/overview`)); assert.ok(isPublicPath(`/api/share/${t}/render/living`));
  for (const p of [`/api/share/${t}/render/`, `/api/share/${t}/render/a/b`, `/api/share/${t}/render/..`, `/api/share/${t}/renders`]) assert.ok(!isPublicPath(p), p);
});
test('doar proprietarul scrie; camera trebuie să existe; linkul activ servește, cel revocat nu', async () => {
  const id = await repo.createProject(A, 'Imagini', 'demo'), { number } = await repo.createRevision(A, id, 'Predat'), s = await createShare(A, id, number);
  const room = (await revisionRenderState(A, id, number)).keys[1]!;
  await assert.rejects(saveRevisionRender(B, id, number, { key: 'overview', width: 1600, image: webp() }), (e: any) => e.status === 404, 'alt proprietar');
  await assert.rejects(saveRevisionRender(A, id, number, { key: 'nu-exista', width: 1600, image: webp() }), (e: any) => e.status === 400);
  await assert.rejects(saveRevisionRender(A, id, number, { key: 'overview', width: 1234, image: webp() }), (e: any) => e.status === 400);
  await assert.rejects(saveRevisionRender(A, id, 99, { key: 'overview', width: 1600, image: webp() }), (e: any) => e.status === 404);
  await saveRevisionRender(A, id, String(number), { key: 'overview', width: 1600, image: webp() });
  await saveRevisionRender(A, id, number, { key: 'overview', width: 800, image: `data:image/jpeg;base64,${jpeg}` });
  await saveRevisionRender(A, id, number, { key: room, width: 800, image: webp() });
  await saveRevisionRender(A, id, number, { key: room, width: 800, image: webp(80) }); // înlocuire, nu dublură
  assert.deepEqual(await shareRenderSet(s.token), { overview: [800, 1600], [room]: [800] });
  assert.deepEqual((await revisionRenderState(A, id, number)).have, { overview: [800, 1600], [room]: [800] });
  const img = await shareRender(s.token, room, 800); assert.equal(img?.mime, 'image/webp'); assert.equal(img?.bytes.length, 80);
  assert.equal(await shareRender(s.token, room, 1600), null); assert.equal(await shareRender(s.token, '../x', 800), null);
  await revokeShare(A, id, s.token);
  assert.deepEqual(await shareRenderSet(s.token), {}); assert.equal(await shareRender(s.token, 'overview', 1600), null);
  const { rows } = await (await getDb()).q('select count(*)::int as n from revision_renders where project_id=$1', [id]); assert.equal(rows[0].n, 3);
});
