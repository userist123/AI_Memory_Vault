// Jurnalul zilnic de șantier: intrări interne până când designerul le face vizibile clientului. Pe link ajung doar cele
// vizibile, fără numărul de oameni și fără meserii; o poză se servește pe link doar dacă e a unei intrări vizibile.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { checkLogEntry, clientLog, isLogDate, localToday, sortLog, LOG_LIMITS, SiteLogError, type SiteLogEntry } from '../core/site-log';

const U = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
test('checkLogEntry: dată reală, text obligatoriu și curățat, câmpuri opționale validate, implicit invizibil', () => {
  const ok = checkLogEntry({ date: '2026-11-20', text: '  Turnat șapă‮ în living\nși hol  ', weather: ' ploaie ', workers: 4, trades: ['tiles', 'general'], photos: [U(1)], extra: 1 });
  assert.deepEqual(ok, { date: '2026-11-20', text: 'Turnat șapă în living\nși hol', weather: 'ploaie', workers: 4, trades: ['tiles', 'general'], photos: [U(1)], clientVisible: false });
  assert.deepEqual(checkLogEntry({ date: '2026-11-20', text: 'x' }), { date: '2026-11-20', text: 'x', clientVisible: false }, 'fără câmpul de vizibilitate = intern');
  assert.equal(checkLogEntry({ date: '2026-11-20', text: 'x', clientVisible: true }).clientVisible, true);
  assert.equal(checkLogEntry({ date: '2026-11-20', text: 'a'.repeat(LOG_LIMITS.text + 50) }).text!.length, LOG_LIMITS.text);
  assert.equal(checkLogEntry({ date: '2026-11-20', text: 'x', workers: null, weather: null }).workers, null);
  const bad = (x: unknown, partial = false) => assert.throws(() => checkLogEntry(x, partial), SiteLogError, JSON.stringify(x));
  for (const x of [null, [], 'text', { text: 'x' }, { date: '2026-02-30', text: 'x' }, { date: '20-11-2026', text: 'x' }, { date: '2026-11-20' }, { date: '2026-11-20', text: '   ' },
    { date: '2026-11-20', text: 'x', workers: -1 }, { date: '2026-11-20', text: 'x', workers: 2.5 }, { date: '2026-11-20', text: 'x', workers: '3' }, { date: '2026-11-20', text: 'x', workers: LOG_LIMITS.workers + 1 },
    { date: '2026-11-20', text: 'x', trades: ['astronaut'] }, { date: '2026-11-20', text: 'x', trades: ['paint', 'paint'] }, { date: '2026-11-20', text: 'x', trades: 'paint' },
    { date: '2026-11-20', text: 'x', photos: ['nu-e-uuid'] }, { date: '2026-11-20', text: 'x', photos: [U(1), U(1)] }, { date: '2026-11-20', text: 'x', photos: Array.from({ length: LOG_LIMITS.photos + 1 }, (_, i) => U(i)) },
    { date: '2026-11-20', text: 'x', clientVisible: 'da' }, { date: '2026-11-20', text: 'x', clientVisible: 1 }, { date: '2026-11-20', text: 'x', weather: 7 }]) bad(x);
  // modificare parțială: doar ce s-a trimis, fără valori implicite; un text gol trimis explicit e respins
  assert.deepEqual(checkLogEntry({ clientVisible: true }, true), { clientVisible: true }); assert.deepEqual(checkLogEntry({}, true), {});
  bad({ text: '' }, true); bad({ date: 'ieri' }, true);
});
test('isLogDate, localToday, sortLog, clientLog', () => {
  assert.ok(isLogDate('2028-02-29')); assert.ok(!isLogDate('2027-02-29')); assert.ok(!isLogDate('2026-13-01')); assert.ok(!isLogDate(20261120));
  assert.equal(localToday(new Date(2026, 0, 5, 23, 59)), '2026-01-05', 'după ceasul local, nu UTC');
  const e = (id: string, date: string, createdAt: string, clientVisible: boolean): SiteLogEntry => ({ id, date, text: id, weather: 'soare', workers: 3, trades: ['paint'], photos: [U(1)], clientVisible, createdAt });
  const all = [e('a', '2026-11-18', '2026-11-18T10:00:00.000Z', true), e('b', '2026-11-20', '2026-11-20T08:00:00.000Z', false), e('c', '2026-11-20', '2026-11-20T17:00:00.000Z', true)];
  assert.deepEqual(sortLog(all).map(x => x.id), ['c', 'b', 'a']);
  const pub = clientLog(all); assert.deepEqual(pub.map(x => x.id), ['c', 'a'], 'intrarea internă lipsește');
  assert.deepEqual(Object.keys(pub[0]!).sort(), ['date', 'id', 'photos', 'text', 'weather'], 'fără oameni, meserii, vizibilitate');
});

// ---------- server ----------
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare } from '../lib/share';
import { moveToTrash, purgeProject } from '../lib/trash';
import { addPhoto, readPhoto, addSnag, deleteSnag } from '../lib/site';
import { listSiteLog, addSiteLog, updateSiteLog, deleteSiteLog, siteLogForShare, shareLogPhoto } from '../lib/site-log';
import { GET as sharePhotoGET } from '../app/api/share/[token]/render/[key]/route';
beforeAll(() => resetDbForTests());
const A = 'eeeeeeee-5555-4555-8555-555555555555', B = 'ffffffff-6666-4666-8666-666666666666';
const JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16, 0x4a, 0x46, 0x49, 0x46, 0, 1]), PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13]);
const is = (status: number) => (e: any) => e.status === status;
const photoOnLink = (token: string, photo: string, q = '?src=log') => sharePhotoGET(new Request(`http://localhost/api/share/${token}/render/${photo}${q}`), { params: Promise.resolve({ token, key: photo }) });
async function project(owner: string, name: string){ const id = await repo.createProject(owner, name, 'demo'); await repo.createRevision(owner, id, 'r1'); return { id, token: (await createShare(owner, id, 1)).token }; }

test('server: CRUD doar pentru proprietar, validare 400, implicit invizibil, poze doar ale proiectului', async () => {
  const { id } = await project(A, 'Jurnal');
  assert.deepEqual(await listSiteLog(A, id), []);
  const ph = await addPhoto(A, id, JPEG);
  const e1 = await addSiteLog(A, id, { date: '2026-11-19', text: ' Tencuit dormitor ', weather: 'ger', workers: 3, trades: ['paint'], photos: [ph.id] });
  assert.equal(e1.clientVisible, false, 'implicit invizibil'); assert.equal(e1.text, 'Tencuit dormitor'); assert.equal(e1.date, '2026-11-19'); assert.equal(e1.workers, 3); assert.deepEqual(e1.trades, ['paint']); assert.deepEqual(e1.photos, [ph.id]);
  const e2 = await addSiteLog(A, id, { date: '2026-11-21', text: 'Montat uși' });
  assert.equal(e2.workers, null); assert.equal(e2.weather, ''); assert.deepEqual(e2.trades, []); assert.equal(e2.clientVisible, false);
  assert.deepEqual((await listSiteLog(A, id)).map(e => e.date), ['2026-11-21', '2026-11-19'], 'cele mai noi primele');
  const { q } = await getDb(); assert.equal((await q('select client_visible from site_log where id=$1', [e2.id])).rows[0].client_visible, false, 'în bază: implicit false');
  // validare
  for (const bad of [{ text: 'x' }, { date: '2026-02-31', text: 'x' }, { date: '2026-11-20', text: '' }, { date: '2026-11-20', text: 'x', workers: -2 }, { date: '2026-11-20', text: 'x', trades: ['zidar'] },
    { date: '2026-11-20', text: 'x', photos: ['x'] }, { date: '2026-11-20', text: 'x', clientVisible: 'true' }, 'nu e obiect'])
    await assert.rejects(addSiteLog(A, id, bad), is(400), JSON.stringify(bad));
  await assert.rejects(updateSiteLog(A, id, e1.id, { workers: 1.5 }), is(400)); await assert.rejects(updateSiteLog(A, id, e1.id, {}), is(400));
  assert.equal((await listSiteLog(A, id)).length, 2, 'intrările respinse nu se scriu');
  // poza altui proiect nu poate fi atașată
  const other = await project(A, 'Alt proiect'), foreign = await addPhoto(A, other.id, PNG);
  await assert.rejects(addSiteLog(A, id, { date: '2026-11-20', text: 'x', photos: [foreign.id] }), is(400));
  await assert.rejects(updateSiteLog(A, id, e1.id, { photos: [foreign.id] }), is(400));
  // modificare parțială; intrarea altui proiect nu se atinge prin acest proiect
  const up = await updateSiteLog(A, id, e1.id, { text: 'Tencuit și gletuit', clientVisible: true });
  assert.equal(up.text, 'Tencuit și gletuit'); assert.equal(up.clientVisible, true); assert.equal(up.workers, 3, 'câmpurile netrimise rămân'); assert.deepEqual(up.photos, [ph.id]);
  await assert.rejects(updateSiteLog(A, other.id, e1.id, { text: 'x' }), is(404)); await assert.rejects(deleteSiteLog(A, other.id, e1.id), is(404));
  await assert.rejects(updateSiteLog(A, id, 'nu-e-uuid', { text: 'x' }), is(404));
  // alt proprietar: 404 peste tot, nimic schimbat
  await assert.rejects(listSiteLog(B, id), is(404)); await assert.rejects(addSiteLog(B, id, { date: '2026-11-20', text: 'x' }), is(404));
  await assert.rejects(updateSiteLog(B, id, e1.id, { clientVisible: false }), is(404)); await assert.rejects(deleteSiteLog(B, id, e1.id), is(404));
  assert.equal((await listSiteLog(A, id)).find(e => e.id === e1.id)!.clientVisible, true);
  // ștergere: intrarea dispare, poza ei se șterge; o a doua ștergere → 404
  await deleteSiteLog(A, id, e1.id); assert.deepEqual((await listSiteLog(A, id)).map(e => e.id), [e2.id]);
  await assert.rejects(readPhoto(A, id, ph.id), is(404), 'poza intrării șterse dispare'); await assert.rejects(deleteSiteLog(A, id, e1.id), is(404));
});

test('poze: aceeași stocare ca remedierile — o poză folosită și de o remediere rămâne; scoasă din intrare se șterge', async () => {
  const { id } = await project(A, 'Poze comune');
  const p1 = await addPhoto(A, id, JPEG), p2 = await addPhoto(A, id, PNG);
  const e = await addSiteLog(A, id, { date: '2026-11-20', text: 'x', photos: [p1.id, p2.id] }), s = await addSnag(A, id, { title: 'fisură', photos: [p1.id] });
  await updateSiteLog(A, id, e.id, { photos: [p1.id] }); await assert.rejects(readPhoto(A, id, p2.id), is(404), 'poza scoasă din intrare e ștearsă');
  await deleteSiteLog(A, id, e.id); assert.deepEqual((await readPhoto(A, id, p1.id)).data, JPEG, 'poza rămâne: o folosește remedierea');
  const e2 = await addSiteLog(A, id, { date: '2026-11-21', text: 'y', photos: [p1.id] }); await deleteSnag(A, id, s.id);
  assert.deepEqual((await readPhoto(A, id, p1.id)).data, JPEG, 'ștergerea remedierii nu ia poza jurnalului');
  // o încărcare nouă (curățarea formularelor abandonate) nu atinge pozele jurnalului
  await addPhoto(A, id, PNG); assert.deepEqual((await listSiteLog(A, id))[0]!.photos, [p1.id]); assert.equal(e2.photos.length, 1); await readPhoto(A, id, p1.id);
});

test('linkul clientului: doar intrările vizibile, fără câmpurile interne; pozele doar ale intrărilor vizibile ale proiectului linkului', async () => {
  const { id, token } = await project(A, 'Pe link'), other = await project(A, 'Vecin');
  const pIn = await addPhoto(A, id, JPEG), pOut = await addPhoto(A, id, PNG), pSnag = await addPhoto(A, id, PNG), pFree = await addPhoto(A, id, JPEG);
  const internal = await addSiteLog(A, id, { date: '2026-11-21', text: 'Ceartă cu instalatorul', weather: 'nor', workers: 2, trades: ['plumbing'], photos: [pIn.id] });
  const visible = await addSiteLog(A, id, { date: '2026-11-20', text: 'Montat parchet', weather: 'soare', workers: 5, trades: ['flooring'], photos: [pOut.id], clientVisible: true });
  await addSnag(A, id, { title: 'zgârietură', photos: [pSnag.id] });
  const pub = await siteLogForShare(token);
  assert.deepEqual(pub, [{ id: visible.id, date: '2026-11-20', text: 'Montat parchet', weather: 'soare', photos: [pOut.id] }], 'exact câmpurile publice ale intrării vizibile');
  const raw = JSON.stringify(pub); for (const leak of ['Ceartă', internal.id, pIn.id, 'workers', 'trades', 'flooring', 'clientVisible', 'client_visible']) assert.ok(!raw.includes(leak), leak);
  // pozele pe link
  const okRes = await photoOnLink(token, pOut.id); assert.equal(okRes.status, 200); assert.equal(okRes.headers.get('content-type'), 'image/png'); assert.equal(okRes.headers.get('x-content-type-options'), 'nosniff');
  assert.deepEqual(new Uint8Array(await okRes.arrayBuffer()), PNG);
  assert.equal((await photoOnLink(token, pIn.id)).status, 404, 'poza unei intrări interne');
  assert.equal((await photoOnLink(token, pSnag.id)).status, 404, 'poza unei remedieri');
  assert.equal((await photoOnLink(token, pFree.id)).status, 404, 'poză neatașată');
  assert.equal((await photoOnLink(token, 'nu-e-uuid')).status, 404); assert.equal((await photoOnLink('A'.repeat(43), pOut.id)).status, 404, 'link necunoscut');
  assert.equal((await photoOnLink(other.token, pOut.id)).status, 404, 'linkul altui proiect nu vede poza');
  assert.equal((await photoOnLink(token, pOut.id, '')).status, 404, 'fără ?src=log ruta caută doar imagini 3D');
  assert.deepEqual(await siteLogForShare(other.token), [], 'alt proiect: jurnal gol');
  // comutatorul: ce devine vizibil apare, ce redevine intern dispare, cu tot cu poze
  await updateSiteLog(A, id, internal.id, { clientVisible: true }); await updateSiteLog(A, id, visible.id, { clientVisible: false });
  assert.deepEqual((await siteLogForShare(token)).map(e => e.id), [internal.id]);
  assert.equal((await photoOnLink(token, pIn.id)).status, 200); assert.equal((await photoOnLink(token, pOut.id)).status, 404);
  assert.equal(await shareLogPhoto(token, pOut.id), null);
});

test('link revocat sau expirat → refuz (jurnal și poze); ștergerea proiectului șterge jurnalul', async () => {
  const { id, token } = await project(A, 'Revocat');
  const ph = await addPhoto(A, id, JPEG), e = await addSiteLog(A, id, { date: '2026-11-20', text: 'Vizibil', photos: [ph.id], clientVisible: true });
  assert.equal((await siteLogForShare(token)).length, 1); assert.equal((await photoOnLink(token, ph.id)).status, 200);
  await revokeShare(A, id, token);
  await assert.rejects(siteLogForShare(token), is(404)); assert.equal((await photoOnLink(token, ph.id)).status, 404); assert.equal(await shareLogPhoto(token, ph.id), null);
  const { q } = await getDb(), second = (await createShare(A, id, 1)).token;
  assert.equal((await siteLogForShare(second)).length, 1); await q(`update shares set expires_at = now() - interval '1 minute' where token=$1`, [second]);
  await assert.rejects(siteLogForShare(second), is(404), 'expirat'); assert.equal((await photoOnLink(second, ph.id)).status, 404);
  await assert.rejects(siteLogForShare('scurt'), is(404)); await assert.rejects(siteLogForShare(null), is(404));
  // ștergerea definitivă a proiectului ia și jurnalul (on delete cascade)
  assert.equal((await q('select count(*)::int as n from site_log where project_id=$1', [id])).rows[0].n, 1);
  const name = (await repo.getProject(A, id)).name; await moveToTrash(A, id); await purgeProject(A, id, name);
  assert.equal((await q('select count(*)::int as n from site_log where project_id=$1', [id])).rows[0].n, 0);
  assert.equal((await q('select count(*)::int as n from site_log where id=$1', [e.id])).rows[0].n, 0);
});
import { checkLogEntry as checkEntry2 } from '../core/site-log';
import { test as t3 } from 'vitest';
import assert3 from 'node:assert/strict';
t3('id-urile pozelor se aduc la litere mici (o variantă cu majuscule nu mai e luată drept poză nefolosită)', () => {
  const up = 'ABCDEF12-3456-4789-8ABC-DEF012345678', e = checkEntry2({ date: '2026-11-22', text: 'x', photos: [up] } as any);
  assert3.deepEqual(e.photos, [up.toLowerCase()]);
  assert3.throws(() => checkEntry2({ date: '2026-11-22', text: 'x', photos: [up, up.toLowerCase()] } as any), 'dublura după normalizare e refuzată');
});
