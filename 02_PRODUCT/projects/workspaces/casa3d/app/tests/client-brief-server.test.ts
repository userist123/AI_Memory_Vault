// Chestionarul clientului, pe server (lib/client-brief.ts + rutele /api/brief/**): un singur link activ, salvare cu versiune
// (409 la conflict), trimitere care închide linkul, revocare/expirare → 404, limita de poze sub încărcări simultane, poze
// fără EXIF, doar proprietarul vede răspunsurile, „Preia în brief” cu versiunea proiectului, poarta publică și coșul.
import { test, beforeAll, beforeEach } from 'vitest';
import assert from 'node:assert/strict';
import path from 'node:path';
import { readdirSync, statSync } from 'node:fs';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
process.env.CASA3D_AUTH_FILE = path.join(import.meta.dirname, '__no_auth_file__.json');
import { NextRequest } from 'next/server';
import { proxy as middleware } from '../proxy';
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import * as cb from '../lib/client-brief';
import { resetShareLimits } from '../lib/rate-limit';
import { isPublicPath } from '../lib/public-gate';
import { policyFor } from '../lib/security-headers';
import { moveToTrash, restoreFromTrash, purgeProject } from '../lib/trash';
import { exportProject } from '../lib/project-export';
import { UNSURE, type ClientBrief } from '../core/client-brief';
import { DEFAULT_BRIEF } from '../core/brief';
import { baseline, exif, ascii, indexOfBytes, markers } from './jpeg-helper';

beforeAll(() => resetDbForTests());
beforeEach(() => resetShareLimits());
const A = '77777777-7777-4777-8777-777777777777', B = '88888888-8888-4888-8888-888888888888';
const status = (n: number) => (e: any) => e.status === n;
const full: ClientBrief = { v: 1, propertyType: 'casa', adults: 2, children: 1, styles: ['natural'], rooms: ['living', 'dormitor'], budgetRange: '10_20k', colorsLike: 'alb, stejar', avoidMaterials: ['sticla'] };
const SECRET = '44.4268N-26.1025E', photo = (w = 640) => baseline({ w, extra: [exif(SECRET)] });
async function fresh(name: string){ const id = await repo.createProject(A, name, 'demo'); const { brief } = await cb.createBriefLink(A, id); return { id, token: brief.token! }; }

test('creare: token ca la linkurile de prezentare, expiră implicit în 30 de zile; un singur link activ pe proiect; linkul nou preia răspunsurile și pozele', async () => {
  const id = await repo.createProject(A, 'Chestionar creare', 'demo'), { q } = await getDb();
  assert.equal((await cb.getClientBrief(A, id)).brief, null);
  const a = (await cb.createBriefLink(A, id)).brief;
  assert.match(a.token!, /^[A-Za-z0-9_-]{43}$/); assert.equal(a.path, `/brief/${a.token}`); assert.equal(a.active, true); assert.equal(a.submittedAt, null); assert.equal(a.answered, false);
  const days = (new Date(a.expiresAt!).getTime() - new Date(a.createdAt).getTime()) / 864e5; assert.ok(days > 29.9 && days < 30.1, String(days));
  await assert.rejects(() => cb.createBriefLink(A, id, 400), status(400)); await assert.rejects(() => cb.createBriefLink(A, id, 'x'), status(400));
  await cb.savePublicBrief(a.token, { rev: 0, data: { adults: 3 } }); const ph = await cb.addPublicPhoto(a.token, photo());
  // linkul nou: cel vechi nu mai există, răspunsurile și poza au trecut pe cel nou
  const b = (await cb.createBriefLink(A, id, 7)).brief;
  assert.notEqual(b.token, a.token); assert.deepEqual(b.data, { v: 1, adults: 3 }); assert.deepEqual(b.photos.map(p => p.id), [ph.id]);
  await assert.rejects(() => cb.getPublicBrief(a.token), status(404)); await assert.rejects(() => cb.savePublicBrief(a.token, { rev: 1, data: {} }), status(404));
  assert.deepEqual((await cb.getPublicBrief(b.token) as any).data, { v: 1, adults: 3 });
  assert.equal((await q('select count(*)::int as n from client_briefs where project_id=$1', [id])).rows[0].n, 1);
  // indexul unic: baza însăși refuză un al doilea link nerevocat
  await assert.rejects(() => q(`insert into client_briefs(token, project_id) values($1,$2)`, ['Z'.repeat(43), id]));
  // două creări simultane lasă tot un singur link
  await Promise.all([cb.createBriefLink(A, id), cb.createBriefLink(A, id), cb.createBriefLink(A, id)]);
  assert.equal((await q('select count(*)::int as n from client_briefs where project_id=$1 and revoked_at is null', [id])).rows[0].n, 1);
  assert.equal((await q('select count(*)::int as n from brief_photos p join client_briefs b on b.token=p.token where b.project_id=$1', [id])).rows[0].n, 1);
});

test('salvarea automată: document întreg, validat strict, cu versiune; versiune veche → 409 și nimic suprascris', async () => {
  const { token } = await fresh('Chestionar salvare');
  const g0 = await cb.getPublicBrief(token) as any; assert.deepEqual({ submitted: g0.submitted, data: g0.data, rev: g0.rev, photos: g0.photos, currency: g0.currency }, { submitted: false, data: {}, rev: 0, photos: [], currency: 'RON' });
  assert.deepEqual(await cb.savePublicBrief(token, { rev: 0, data: { v: 1, adults: 2, styles: ['modern'] } }), { rev: 1 });
  await assert.rejects(() => cb.savePublicBrief(token, { rev: 0, data: { v: 1, adults: 5 } }), (e: any) => e.status === 409 && e.details?.conflict === true, 'a doua filă, cu versiunea veche');
  assert.deepEqual((await cb.getPublicBrief(token) as any).data, { v: 1, adults: 2, styles: ['modern'] });
  for (const body of [null, [], { data: {} }, { rev: '1', data: {} }, { rev: 1 }, { rev: 1, data: { necunoscut: 1 } }, { rev: 1, data: { adults: 99 } }, { rev: 1, data: {}, extra: 1 }, { rev: 1, data: { styles: ['modern', UNSURE] } }])
    await assert.rejects(() => cb.savePublicBrief(token, body), status(400), JSON.stringify(body));
  assert.equal((await cb.getPublicBrief(token) as any).rev, 1, 'cererile respinse nu schimbă versiunea');
  // ruta: corp peste 24 KB → 413; fără application/json → 415; token cu formă greșită → 404
  const { PUT, GET } = await import('../app/api/brief/[token]/route'), params = Promise.resolve({ token });
  const put = (body: string, type = 'application/json', tk = token) => PUT(new Request(`http://localhost/api/brief/${tk}`, { method: 'PUT', headers: { 'content-type': type }, body }), { params: Promise.resolve({ token: tk }) });
  assert.equal((await put(JSON.stringify({ rev: 1, data: { notes: 'x'.repeat(30000) } }))).status, 413);
  assert.equal((await put(JSON.stringify({ rev: 1, data: {} }), 'text/plain')).status, 415);
  assert.equal((await put('{}', 'application/json', 'scurt')).status, 404);
  const ok = await put(JSON.stringify({ rev: 1, data: { v: 1, adults: 4 } })); assert.equal(ok.status, 200); assert.deepEqual(await ok.json(), { rev: 2 });
  assert.equal(ok.headers.get('cache-control'), 'no-store'); assert.equal(ok.headers.get('x-robots-tag'), 'noindex');
  const stale = await put(JSON.stringify({ rev: 1, data: { v: 1, adults: 1 } })); assert.equal(stale.status, 409); assert.equal((await stale.json()).conflict, true);
  assert.equal((await (await GET(new Request('http://localhost/x'), { params })).json()).data.adults, 4);
});

test('trimiterea: validare completă; după ea scrierile dau 409, iar GET-ul public întoarce doar { submitted: true }', async () => {
  const { id, token } = await fresh('Chestionar trimitere');
  await cb.savePublicBrief(token, { rev: 0, data: { v: 1, adults: 2 } });
  await assert.rejects(() => cb.submitPublicBrief(token, { rev: 1 }), (e: any) => e.status === 400 && e.details.missing.includes('rooms'), 'incomplet');
  await assert.rejects(() => cb.submitPublicBrief(token, {}), status(400));
  const { rev } = await cb.savePublicBrief(token, { rev: 1, data: full }); const ph = await cb.addPublicPhoto(token, photo());
  await assert.rejects(() => cb.submitPublicBrief(token, { rev: rev - 1 }), status(409), 'versiune veche');
  assert.deepEqual(await cb.submitPublicBrief(token, { rev }), { submitted: true });
  assert.deepEqual(await cb.getPublicBrief(token), { submitted: true }, 'niciun răspuns, nicio poză, nicio versiune');
  for (const f of [() => cb.savePublicBrief(token, { rev, data: full }), () => cb.submitPublicBrief(token, { rev }), () => cb.addPublicPhoto(token, photo()), () => cb.patchPublicPhoto(token, ph.id, { verdict: 'love' }), () => cb.deletePublicPhoto(token, ph.id)])
    await assert.rejects(f, status(409));
  await assert.rejects(() => cb.readPublicPhoto(token, ph.id), status(404), 'nici poza proprie nu mai e servită public');
  const { GET } = await import('../app/api/brief/[token]/route'); const res = await GET(new Request('http://localhost/x'), { params: Promise.resolve({ token }) });
  assert.deepEqual(await res.json(), { submitted: true });
  // designerul vede tot; redeschiderea lasă clientul să continue pe același link
  const st = (await cb.getClientBrief(A, id)).brief!; assert.ok(st.submittedAt); assert.deepEqual(st.data, full); assert.equal(st.photos.length, 1); assert.deepEqual({ w: st.photos[0]!.width, h: st.photos[0]!.height }, { w: 640, h: 480 });
  assert.equal((await cb.reopenClientBrief(A, id)).brief!.submittedAt, null);
  const again = await cb.getPublicBrief(token) as any; assert.equal(again.submitted, false); assert.deepEqual(again.data, full); assert.equal(again.photos.length, 1);
  assert.deepEqual(await cb.savePublicBrief(token, { rev: again.rev, data: { ...full, adults: 3 } }), { rev: again.rev + 1 });
});

test('revocat, expirat, șters, în coș sau token necunoscut: același 404 „Link inexistent.” pe toate operațiile publice', async () => {
  const { q } = await getDb();
  const all = (token: string, ph: string) => [() => cb.getPublicBrief(token), () => cb.savePublicBrief(token, { rev: 0, data: {} }), () => cb.submitPublicBrief(token, { rev: 0 }), () => cb.addPublicPhoto(token, photo()),
    () => cb.patchPublicPhoto(token, ph, { verdict: 'ok' }), () => cb.deletePublicPhoto(token, ph), () => cb.readPublicPhoto(token, ph)];
  const gone = async (token: string, ph: string, why: string) => { for (const f of all(token, ph)) await assert.rejects(f, (e: any) => e.status === 404 && e.message === 'Link inexistent.', why); };
  await gone('A'.repeat(43), '12345678-1234-4234-8234-123456789abc', 'necunoscut'); await gone('scurt', 'x', 'formă greșită'); await gone(null as any, 'x', 'null');
  { const { id, token } = await fresh('Chestionar revocat'); const ph = await cb.addPublicPhoto(token, photo()); await cb.savePublicBrief(token, { rev: 0, data: full });
    const st = (await cb.revokeBriefLink(A, id)).brief!; assert.equal(st.active, false); assert.equal(st.token, undefined, 'cheia unui link oprit nu se mai întoarce'); assert.ok(st.revokedAt);
    assert.deepEqual(st.data, full, 'revocarea păstrează răspunsurile'); assert.equal(st.photos.length, 1);
    await gone(token, ph.id, 'revocat');
    assert.ok((await cb.readBriefPhoto(A, id, ph.id)).data.length > 0, 'designerul vede în continuare poza'); }
  { const { token } = await fresh('Chestionar expirat'); const ph = await cb.addPublicPhoto(token, photo());
    await q(`update client_briefs set expires_at = now() - interval '1 minute' where token=$1`, [token]); await gone(token, ph.id, 'expirat'); }
  { const { id, token } = await fresh('Chestionar in cos'); const ph = await cb.addPublicPhoto(token, photo());
    await moveToTrash(A, id); await gone(token, ph.id, 'proiect în coș');
    await restoreFromTrash(A, id); await gone(token, ph.id, 'restaurarea nu repornește linkul');
    assert.equal((await cb.getClientBrief(A, id)).brief!.photos.length, 1, 'răspunsurile rămân la designer după restaurare');
    await moveToTrash(A, id); await purgeProject(A, id, 'Chestionar in cos');
    assert.equal((await q('select count(*)::int as n from brief_photos where id=$1', [ph.id])).rows[0].n, 0, 'ștergerea definitivă a proiectului șterge și pozele'); }
  { const { id, token } = await fresh('Chestionar sters'); const ph = await cb.addPublicPhoto(token, photo());
    assert.deepEqual(await cb.deleteClientBrief(A, id), { brief: null }); await gone(token, ph.id, 'șters');
    assert.equal((await q('select count(*)::int as n from brief_photos where id=$1', [ph.id])).rows[0].n, 0, 'ștergerea chestionarului șterge pozele');
    assert.equal((await cb.getClientBrief(A, id)).brief, null); await assert.rejects(() => cb.deleteClientBrief(A, id), status(404)); await assert.rejects(() => cb.reopenClientBrief(A, id), status(404)); }
});

test('pozele: doar JPEG, fără EXIF în ce se stochează și se servește; verdict în trei trepte și notă; ștergere', async () => {
  const { id, token } = await fresh('Chestionar poze'), { q } = await getDb();
  const p = await cb.addPublicPhoto(token, photo()); assert.deepEqual({ verdict: p.verdict, note: p.note }, { verdict: null, note: '' });
  const stored = new Uint8Array((await q('select data, bytes, mime, width, height from brief_photos where id=$1', [p.id])).rows[0].data);
  assert.deepEqual(stored, baseline()); assert.equal(indexOfBytes(stored, ascii(SECRET)), -1); assert.equal(indexOfBytes(stored, ascii('Exif')), -1);
  assert.ok(markers(stored).every(m => m === 0xe0 || m < 0xe0 || m > 0xef));
  // ruta publică și cea a designerului servesc aceiași octeți curați, cu antetele de fișier sigur
  const pub = await import('../app/api/brief/[token]/photos/[photo]/route');
  const res = await pub.GET(new Request('http://localhost/x'), { params: Promise.resolve({ token, photo: p.id }) });
  assert.equal(res.status, 200); const served = new Uint8Array(await res.arrayBuffer()); assert.deepEqual(served, stored); assert.equal(indexOfBytes(served, ascii(SECRET)), -1);
  assert.deepEqual([res.headers.get('content-type'), res.headers.get('x-content-type-options'), res.headers.get('cache-control')], ['image/jpeg', 'nosniff', 'private, no-store']);
  assert.deepEqual((await cb.readBriefPhoto(A, id, p.id)).data, stored);
  for (const [tk, ph] of [[token, '12345678-1234-4234-8234-123456789abc'], [token, 'x'], ['A'.repeat(43), p.id]] as const){
    const r = await pub.GET(new Request('http://localhost/x'), { params: Promise.resolve({ token: tk, photo: ph }) }); assert.equal(r.status, 404); assert.deepEqual(await r.json(), { error: 'Link inexistent.' }); }
  // ce nu e JPEG obișnuit e refuzat și nu se stochează
  for (const bad of [ascii('GIF89a...........'), Uint8Array.of(0x89, 0x50, 0x4e, 0x47, 1, 2, 3, 4), baseline({ comps: 4 }), baseline().subarray(0, 60), baseline({ w: 5000 }), new Uint8Array(0)]) await assert.rejects(() => cb.addPublicPhoto(token, bad), status(400));
  await assert.rejects(() => cb.addPublicPhoto(token, new Uint8Array(cb.BRIEF_LIMITS.photoBytes + 1)), status(413));
  const { POST } = await import('../app/api/brief/[token]/photos/route');
  const post = (body: Uint8Array, type = 'image/jpeg') => POST(new Request('http://localhost/x', { method: 'POST', headers: { 'content-type': type }, body: Buffer.from(body) }), { params: Promise.resolve({ token }) });
  assert.equal((await post(photo(), 'image/png')).status, 415); assert.equal((await post(photo(), 'application/json')).status, 415);
  assert.equal((await post(new Uint8Array(cb.BRIEF_LIMITS.photoBytes + 10))).status, 413);
  const up = await post(photo(320)); assert.equal(up.status, 201); const p2 = await up.json(); assert.match(p2.id, /^[0-9a-f-]{36}$/);
  assert.equal((await q('select count(*)::int as n from brief_photos where token=$1', [token])).rows[0].n, 2);
  // părerea despre poză
  assert.deepEqual(await cb.patchPublicPhoto(token, p.id, { verdict: 'love', note: ' canapeaua ‮<b>verde</b> ' }), { id: p.id, verdict: 'love', note: 'canapeaua bverde/b' });
  assert.equal((await cb.patchPublicPhoto(token, p.id, { verdict: 'ok' })).note, 'canapeaua bverde/b'); assert.equal((await cb.patchPublicPhoto(token, p.id, { verdict: 'dislike' })).verdict, 'dislike');
  assert.equal((await cb.patchPublicPhoto(token, p.id, { verdict: null })).verdict, null);
  for (const body of [{}, { verdict: 'like' }, { verdict: 5 }, { note: 'x'.repeat(201) }, { note: 5 }, { verdict: 'ok', extra: 1 }, null]) await assert.rejects(() => cb.patchPublicPhoto(token, p.id, body), status(400), JSON.stringify(body));
  await assert.rejects(() => q(`update brief_photos set verdict='like' where id=$1`, [p.id]), 'constrângerea din bază');
  await assert.rejects(() => cb.patchPublicPhoto(token, '12345678-1234-4234-8234-123456789abc', { verdict: 'ok' }), status(404));
  // poza altui chestionar nu poate fi atinsă cu alt link
  const other = await fresh('Chestionar poze 2');
  for (const f of [() => cb.patchPublicPhoto(other.token, p.id, { verdict: 'ok' }), () => cb.deletePublicPhoto(other.token, p.id), () => cb.readPublicPhoto(other.token, p.id), () => cb.readBriefPhoto(A, other.id, p.id)]) await assert.rejects(f, status(404));
  assert.deepEqual((await cb.getPublicBrief(token) as any).photos, [{ id: p.id, verdict: null, note: 'canapeaua bverde/b' }, { id: p2.id, verdict: null, note: '' }]);
  assert.deepEqual(await cb.deletePublicPhoto(token, p.id), { ok: true }); await assert.rejects(() => cb.deletePublicPhoto(token, p.id), status(404));
  assert.deepEqual(((await cb.getPublicBrief(token)) as any).photos.map((x: any) => x.id), [p2.id]);
});

test('20 de încărcări simultane nu depășesc 12 poze; limita de spațiu total', async () => {
  const { token } = await fresh('Chestionar concurenta'), { q } = await getDb();
  const res = await Promise.allSettled(Array.from({ length: 20 }, (_, i) => cb.addPublicPhoto(token, photo(100 + i))));
  assert.equal(res.filter(r => r.status === 'fulfilled').length, cb.BRIEF_LIMITS.photos);
  assert.ok(res.filter(r => r.status === 'rejected').every(r => (r as PromiseRejectedResult).reason.status === 409 && (r as PromiseRejectedResult).reason.details.limit));
  assert.equal((await q('select count(*)::int as n from brief_photos where token=$1', [token])).rows[0].n, 12);
  await assert.rejects(() => cb.addPublicPhoto(token, photo()), status(409));
  // spațiul total: cu pozele existente declarate aproape de limită, următoarea e refuzată chiar dacă numărul permite
  const b = await fresh('Chestionar spatiu'); const p = await cb.addPublicPhoto(b.token, photo());
  await q('update brief_photos set bytes=$1 where id=$2', [cb.BRIEF_LIMITS.totalBytes - 10, p.id]);
  await assert.rejects(() => cb.addPublicPhoto(b.token, photo()), (e: any) => e.status === 409 && /spațiul/.test(e.message));
});

test('doar proprietarul: alt proprietar primește 404 pe orice operație a designerului', async () => {
  const { id, token } = await fresh('Chestionar proprietar'); const ph = await cb.addPublicPhoto(token, photo());
  for (const f of [() => cb.getClientBrief(B, id), () => cb.createBriefLink(B, id), () => cb.revokeBriefLink(B, id), () => cb.reopenClientBrief(B, id), () => cb.deleteClientBrief(B, id), () => cb.readBriefPhoto(B, id, ph.id),
    () => cb.importClientBrief(B, id, { fields: ['style'], rev: 0 }), () => cb.getClientBrief(A, 'nu-e-uuid')]) await assert.rejects(f, status(404));
  const mine = await repo.createProject(B, 'Al lui B', 'demo');
  await assert.rejects(() => cb.readBriefPhoto(B, mine, ph.id), status(404), 'poza altui proiect, cerută prin proiectul propriu');
  assert.equal((await cb.getClientBrief(A, id)).brief!.photos.length, 1, 'nimic nu s-a schimbat');
  // ruta designerului fără cookie de proprietar: 404
  const { GET } = await import('../app/api/projects/[id]/client-brief/photos/[photo]/route');
  const r = await GET(new Request('http://localhost/x'), { params: Promise.resolve({ id, photo: ph.id }) }).catch(() => null); if (r) assert.notEqual(r.status, 200);
});

test('„Preia în brief”: doar câmpurile bifate, recalculate pe server; versiune veche a proiectului → 409; „nu știu încă” nu schimbă stilul', async () => {
  const { id, token } = await fresh('Chestionar preluare');
  await cb.savePublicBrief(token, { rev: 0, data: { ...full, styles: [UNSURE], budgetRange: UNSURE, wfh: 'zilnic', notes: 'Fără covoare.' } });
  const p0 = await repo.getProject(A, id), st = await cb.getClientBrief(A, id); assert.equal(st.projectRev, p0.draftRev); assert.equal(st.currency, 'RON'); assert.equal(st.brief!.importedAt, null);
  for (const body of [null, {}, { fields: [], rev: p0.draftRev }, { fields: ['style'] }, { fields: ['necunoscut'], rev: p0.draftRev }, { fields: ['style', 'style'], rev: p0.draftRev }, { fields: 'style', rev: p0.draftRev }, { fields: ['style'], rev: -1 }])
    await assert.rejects(() => cb.importClientBrief(A, id, body), status(400), JSON.stringify(body));
  await assert.rejects(() => cb.importClientBrief(A, id, { fields: ['propertyType'], rev: p0.draftRev + 1 }), (e: any) => e.status === 409 && e.details.conflict === true);
  assert.equal((await repo.getProject(A, id)).draft.brief, undefined, 'conflictul nu scrie nimic');
  // stilul și bugetul sunt cerute, dar clientul a răspuns „nu știu încă”: rămân cele ale designerului
  const r = await cb.importClientBrief(A, id, { fields: ['propertyType', 'style', 'budget', 'occupants', 'priorities', 'notes'], rev: p0.draftRev });
  assert.deepEqual(r.applied.sort(), ['notes', 'occupants', 'priorities', 'propertyType']); assert.equal(r.draftRev, p0.draftRev + 1);
  assert.deepEqual(r.brief, { ...DEFAULT_BRIEF, propertyType: 'casa', occupants: 3, priorities: ['birou'], notes: 'Alte detalii: Fără covoare.' });
  const p1 = await repo.getProject(A, id); assert.deepEqual(p1.draft.brief, r.brief); assert.equal(p1.draftRev, r.draftRev); assert.equal(p1.draft.brief!.style, DEFAULT_BRIEF.style); assert.equal(p1.draft.brief!.budget, null);
  assert.equal(p1.draft.brief!.children, false, 'câmpul nebifat (copii) nu s-a schimbat'); assert.equal(p1.draft.brief!.colors, '');
  assert.deepEqual(p1.draft.floor, p0.draft.floor, 'restul proiectului e neatins'); assert.ok((await cb.getClientBrief(A, id)).brief!.importedAt);
  // aceeași cerere cu versiunea veche (două file ale designerului): 409
  await assert.rejects(() => cb.importClientBrief(A, id, { fields: ['colors'], rev: p0.draftRev }), status(409));
  // după ce clientul alege un stil, preluarea îl aduce; editorul care salvează cu versiunea veche primește conflict
  const g = await cb.getPublicBrief(token) as any; await cb.savePublicBrief(token, { rev: g.rev, data: { ...full, budgetRange: '20_40k' } });
  const r2 = await cb.importClientBrief(A, id, { fields: ['style', 'budget', 'avoid', 'colors', 'children'], rev: r.draftRev });
  assert.deepEqual({ style: r2.brief.style, budget: r2.brief.budget, avoid: r2.brief.avoid, colors: r2.brief.colors, children: r2.brief.children }, { style: 'natural', budget: 150000, avoid: 'sticlă', colors: 'alb, stejar', children: true });
  await assert.rejects(() => repo.saveDraft(A, id, p1.draft, r.draftRev), status(409));
  assert.equal((await cb.createBriefLink(A, id)).brief.importedAt !== null, true, 'linkul nou păstrează data preluării');
});

test('poarta publică: paginile și rutele chestionarului trec prin tunel fără sesiune; restul rămâne închis; fără Referer', async () => {
  const tok = 'A'.repeat(43), uuid = '12345678-1234-4234-8234-123456789abc', root = path.resolve(import.meta.dirname, '..');
  const walk = (d: string): string[] => readdirSync(d).flatMap(f => { const p = path.join(d, f); return statSync(p).isDirectory() ? walk(p) : f === 'route.ts' ? [p] : []; });
  const routes = walk(path.join(root, 'app', 'api', 'brief')).map(f => ({ file: f, url: '/' + path.relative(path.join(root, 'app'), path.dirname(f)).split(path.sep).join('/').replace('[token]', tok).replace(/\[[^\]]+\]/g, uuid) }));
  assert.deepEqual(routes.map(r => r.url.replace(tok, 'T').replace(uuid, 'U')).sort(), ['/api/brief/T', '/api/brief/T/photos', '/api/brief/T/photos/U', '/api/brief/T/submit']);
  const remote = (url: string, method = 'GET') => new NextRequest(`http://localhost:3123${url}`, { method, headers: { host: 'localhost:3123', 'cf-connecting-ip': '203.0.113.7' } });
  for (const r of routes){ const mod = await import(r.file); for (const m of ['GET', 'POST', 'PATCH', 'PUT', 'DELETE'].filter(m => typeof mod[m] === 'function')){ const res = middleware(remote(r.url, m)); assert.equal(res.headers.get('x-middleware-next'), '1', `${m} ${r.url} → ${res.status}`); } }
  assert.equal(middleware(remote(`/brief/${tok}`)).headers.get('x-middleware-next'), '1');
  for (const p of [`/brief/${tok}`, `/brief/${tok}/`, `/api/brief/${tok}`, `/api/brief/${tok}/submit`, `/api/brief/${tok}/photos`, `/api/brief/${tok}/photos/${uuid}`]) assert.equal(isPublicPath(p), true, p);
  for (const p of ['/brief', '/brief/scurt', `/brief/${tok}/x`, `/api/brief/${tok}/photos/x`, `/api/brief/${tok}/photos/${uuid}/x`, `/api/brief/${tok}/import`, `/api/brief/${tok}/../projects`, `/api/brief/${tok}x`, '/api/brief', '/api/projects/x/client-brief', '/api/projects/x/client-brief/import', `/api/projects/x/client-brief/photos/${uuid}`])
    assert.equal(isPublicPath(p), false, p);
  assert.equal(middleware(remote('/api/projects/x/client-brief')).status, 401); assert.equal(middleware(remote(`/api/projects/x/client-brief/photos/${uuid}`)).status, 401);
  for (const p of [`/brief/${tok}`, `/api/brief/${tok}`, `/api/brief/${tok}/photos/${uuid}`]){ assert.equal(policyFor(p, 'n').referrer, 'no-referrer', p); assert.equal(middleware(remote(p)).headers.get('referrer-policy'), 'no-referrer'); }
  assert.match(policyFor(`/api/brief/${tok}/photos/${uuid}`, 'n').csp, /default-src 'none'/);
  // scriere de pe altă origine: refuzată de proxy (CSRF), ca la celelalte rute publice
  const cross = new NextRequest(`http://localhost:3123/api/brief/${tok}`, { method: 'PUT', headers: { host: 'localhost:3123', origin: 'https://alt-site.example', 'sec-fetch-site': 'cross-site', 'cf-connecting-ip': '203.0.113.7' } });
  assert.equal(middleware(cross).status, 403);
});

test('limite de cereri pe link + IP: 429 în română; un token cu formă greșită primește 404 fără să consume limita', async () => {
  const { token } = await fresh('Chestionar limite');
  const { PUT } = await import('../app/api/brief/[token]/route'), { POST } = await import('../app/api/brief/[token]/photos/route');
  const put = (tk: string, ip: string) => PUT(new Request('http://localhost/x', { method: 'PUT', headers: { 'content-type': 'application/json', 'cf-connecting-ip': ip }, body: '{}' }), { params: Promise.resolve({ token: tk }) });
  for (let i = 0; i < 200; i++) assert.equal((await put('nu-e-token', '198.51.100.9')).status, 404);
  for (let i = 0; i < 120; i++) assert.equal((await put(token, '198.51.100.1')).status, 400, `#${i + 1}`);
  const lim = await put(token, '198.51.100.1'); assert.equal(lim.status, 429); assert.ok(Number(lim.headers.get('retry-after')) > 0); assert.match((await lim.json()).error, /Prea multe trimiteri/);
  assert.equal((await put(token, '198.51.100.2')).status, 400, 'altă adresă nu e blocată');
  const up = (ip: string) => POST(new Request('http://localhost/x', { method: 'POST', headers: { 'content-type': 'image/jpeg', 'cf-connecting-ip': ip }, body: Buffer.from(ascii('nu e jpeg')) }), { params: Promise.resolve({ token }) });
  for (let i = 0; i < 30; i++) assert.equal((await up('198.51.100.3')).status, 400);
  assert.equal((await up('198.51.100.3')).status, 429);
});

test('exportul proiectului nu conține chestionarul clientului (nici răspunsurile, nici pozele)', async () => {
  const { id, token } = await fresh('Chestionar export');
  await cb.savePublicBrief(token, { rev: 0, data: { ...full, notes: 'TEXT-UNIC-DIN-CHESTIONAR' } }); await cb.addPublicPhoto(token, baseline({ w: 777 }));
  const zip = (await exportProject(A, id)).data;
  // fișierele arhivei sunt comprimate, deci conținutul se verifică după despachetare (numele stau necomprimate în arhivă)
  const zlib = await import('node:zlib'), names: string[] = [], bodies: Buffer[] = [];
  for (let p = 0; zip.readUInt32LE(p) === 0x04034b50;){ const method = zip.readUInt16LE(p + 8), size = zip.readUInt32LE(p + 18), nl = zip.readUInt16LE(p + 26), xl = zip.readUInt16LE(p + 28), start = p + 30 + nl + xl;
    names.push(zip.subarray(p + 30, p + 30 + nl).toString('utf8')); const raw = zip.subarray(start, start + size); bodies.push(method === 8 ? zlib.inflateRawSync(raw) : raw); p = start + size; }
  assert.ok(names.includes('proiect.json') && names.length > 8, names.join(' ')); assert.ok(!names.some(n => /brief|chestionar|inspir/i.test(n)), names.join(' '));
  const all = Buffer.concat(bodies);
  assert.equal(all.indexOf('TEXT-UNIC-DIN-CHESTIONAR'), -1); assert.equal(all.indexOf(token), -1); assert.equal(all.indexOf(Buffer.from(baseline({ w: 777 }))), -1);
  assert.ok(all.indexOf('casa3d-project-export') > 0, 'martor: conținutul despachetat chiar e citit');
});

test('plasa de siguranță: proiect șters fără revocarea linkului → scrierile publice dau 404; versiune uriașă → 400, nu eroare SQL', async () => {
  const { id, token } = await fresh('Chestionar plasă'), { q } = await getDb(), ph = await cb.addPublicPhoto(token, photo());
  await assert.rejects(() => cb.savePublicBrief(token, { rev: 1e21, data: {} }), status(400));
  await assert.rejects(() => cb.submitPublicBrief(token, { rev: 2 ** 31 }), status(400));
  await assert.rejects(() => cb.importClientBrief(A, id, { fields: ['style'], rev: 1e21 }), status(400));
  // ca o cursă între „mută în coș” și „link nou”: proiectul marcat șters, linkul rămas nerevocat
  await q('update projects set deleted_at=now() where id=$1', [id]);
  await assert.rejects(() => cb.savePublicBrief(token, { rev: 0, data: { adults: 2 } }), status(404));
  await assert.rejects(() => cb.patchPublicPhoto(token, ph.id, { verdict: 'love' }), status(404));
  await assert.rejects(() => cb.deletePublicPhoto(token, ph.id), status(404));
  await assert.rejects(() => cb.addPublicPhoto(token, photo()), status(404));
  await assert.rejects(() => cb.createBriefLink(A, id), status(404));
});

test('„Preia în brief” cu brief-ul de referință: salvările editorului fără schimbare de brief nu dau conflict; un brief schimbat între timp → 409', async () => {
  const { id, token } = await fresh('Chestionar referință');
  await cb.savePublicBrief(token, { rev: 0, data: { ...full } });
  const p0 = await repo.getProject(A, id), seen = { ...DEFAULT_BRIEF, ...(p0.draft.brief ?? {}) };
  // editorul își salvează singur ciorna (alt nivel de zoom, altă etapă): versiunea crește, brief-ul nu
  const s1 = await repo.saveDraft(A, id, p0.draft, p0.draftRev) as any;
  const r = await cb.importClientBrief(A, id, { fields: ['colors'], base: seen }); assert.equal(r.brief.colors, 'alb, stejar');
  // brief-ul s-a schimbat față de ce s-a văzut în previzualizare: conflict, nimic scris
  await assert.rejects(() => cb.importClientBrief(A, id, { fields: ['avoid'], base: seen }), (e: any) => e.status === 409 && e.details.conflict === true);
  assert.equal((await repo.getProject(A, id)).draft.brief!.avoid, DEFAULT_BRIEF.avoid);
  await assert.rejects(() => cb.importClientBrief(A, id, { fields: ['avoid'], base: { style: 'necunoscut' } }), status(400));
  void s1;
});
