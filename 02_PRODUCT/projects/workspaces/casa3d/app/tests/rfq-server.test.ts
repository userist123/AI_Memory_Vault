// Cererile de ofertă către meseriași, pe server (lib/rfq.ts + rutele /api/oferta/** și /api/projects/[id]/rfq/**): rânduri
// înghețate la creare, câte un link pe meseriaș, confidențialitate (nimic despre client, buget sau celelalte oferte), salvare cu
// versiune, trimitere care blochează, revocat / expirat / coș → 404 uniform, alt proprietar → 404, atribuire → prețuri contractate
// pe proiect cu 409 la conflict, poarta publică, limite de cereri, export fără oferte și trimiteri simultane.
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
import * as rfq from '../lib/rfq';
import { resetShareLimits } from '../lib/rate-limit';
import { isPublicPath } from '../lib/public-gate';
import { policyFor } from '../lib/security-headers';
import { moveToTrash, restoreFromTrash, duplicateProject } from '../lib/trash';
import { exportProject } from '../lib/project-export';
import { saveProfile } from '../lib/studio-profile';
import { getCommercial } from '../lib/commercial';
import { createShare, resolveShare } from '../lib/share';
import { INCLUSIONS, type RfqOffer, type LaborContractEntry } from '../core/rfq';

beforeAll(() => resetDbForTests());
beforeEach(() => resetShareLimits());
const A = '77777777-7777-4777-8777-777777777777', B = '88888888-8888-4888-8888-888888888888';
const status = (n: number) => (e: any) => e.status === n;
const CLIENT = { name: 'Popescu-Unic Andrei', phone: '0722000111', email: 'andrei.unic@example.ro', address: 'Str. Secretă 17, ap. 4' }, SITE = 'Bd. Ascuns 99, București';
const INC = Object.fromEntries(INCLUSIONS.map(k => [k, { mode: 'included' }])) as RfqOffer['inclusions'];
type State = Awaited<ReturnType<typeof rfq.getRfq>>;
const reqOf = (s: State, rid: string) => s.requests.find(r => r.id === rid)!;
/** Proiect demo cu client completat, o cerere pe toate meseriile cu rânduri și `n` invitații. */
async function fresh(name: string, n = 1){
  const id = await repo.createProject(A, name, 'demo'); await repo.saveMeta(A, id, { status: 'design', client: CLIENT, siteAddress: SITE });
  const st0 = await rfq.getRfq(A, id), trades = [...new Set(st0.available.map(l => l.trade))];
  const made = await rfq.createRfq(A, id, { title: 'Finisaje apartament', trades, note: 'Materialele le aduce beneficiarul.', siteCity: 'Cluj-Napoca', dueDate: '2026-12-15' });
  let st: State = made; const tokens: string[] = [], keys: string[] = [];
  for (let i = 0; i < n; i++){ const r = await rfq.addInvite(A, id, made.id, { label: `Echipa ${String.fromCharCode(65 + i)}` }); st = r; keys.push(r.key); tokens.push(reqOf(r, made.id).invites.find(x => x.key === r.key)!.token!); }
  return { id, rid: made.id, lines: reqOf(st, made.id).lines, tokens, keys, token: tokens[0]!, key: keys[0]! };
}
const offer = (lines: { rateId: string }[], price: number, extra: Partial<RfqOffer> = {}): RfqOffer => ({ v: 1, prices: Object.fromEntries(lines.map(l => [l.rateId, price])), inclusions: INC, company: { form: 'pfa', vatPayer: false },
  advancePct: 20, startDate: '2026-12-07', durationDays: 15, warrantyMonths: 12, validDays: 30, ...extra });
async function send(token: string, o: RfqOffer){ const g = await rfq.getPublicOffer(token), s = await rfq.savePublicOffer(token, { rev: g.rev, data: o }); return rfq.submitPublicOffer(token, { rev: s.rev }); }

test('creare: rândurile se îngheață la creare (schimbările ulterioare ale proiectului nu le ating); validare; limite', async () => {
  const id = await repo.createProject(A, 'Ofertă creare', 'demo'), { q } = await getDb(), st0 = await rfq.getRfq(A, id);
  assert.deepEqual(st0.requests, []); assert.ok(st0.available.length >= 3, 'proiectul demo are manoperă'); assert.deepEqual(st0.laborContract, {});
  const trades = [...new Set(st0.available.map(l => l.trade))];
  for (const body of [null, {}, { title: '', trades }, { title: 'x', trades: [] }, { title: 'x', trades: ['nu-exista'] }, { title: 'x', trades: [trades[0], trades[0]] }, { title: 'x'.repeat(121), trades }, { title: 'x', trades, dueDate: '2026-13-01' },
    { title: 'x', trades, extra: 1 }, { title: 'x', trades, note: 'n'.repeat(1001) }, { title: 'x', trades: ['electric'] }])
    await assert.rejects(() => rfq.createRfq(A, id, body), status(400), JSON.stringify(body));
  const one = await rfq.createRfq(A, id, { title: ' Gresie  și <b>faianță</b> ', trades: [trades[0]], siteCity: ' Brașov ', dueDate: '2026-12-01', note: 'rând 1\r\nrând 2' }), r = reqOf(one, one.id);
  assert.equal(r.title, 'Gresie și b faianță /b'); assert.equal(r.siteCity, 'Brașov'); assert.equal(r.dueDate, '2026-12-01'); assert.equal(r.note, 'rând 1\nrând 2'); assert.deepEqual(r.trades, [trades[0]]);
  assert.deepEqual(r.lines, st0.available.filter(l => l.trade === trades[0]), 'rândurile cererii = rândurile devizului de acum, pe meseria aleasă');
  assert.ok(r.lines.every(l => l.qty > 0 && l.unit && l.label && l.rooms.length && l.ref.expected > 0)); assert.equal(r.closedAt, null); assert.equal(r.awarded, null); assert.deepEqual(r.invites, []);
  // proiectul se schimbă (camerele se micșorează): cererea rămâne cum a fost trimisă
  const p = await repo.getProject(A, id), cut = JSON.parse(JSON.stringify(p.draft)); for (const r of cut.floor.rooms) r.rect.x1 = r.rect.x0 + (r.rect.x1 - r.rect.x0) / 2;
  await repo.saveDraft(A, id, cut, p.draftRev);
  const after = await rfq.getRfq(A, id); assert.notDeepEqual(after.available, st0.available, 'martor: devizul chiar s-a schimbat'); assert.deepEqual(reqOf(after, one.id).lines, r.lines);
  // cel mult 20 de cereri pe proiect
  const id2 = await repo.createProject(A, 'Ofertă limită', 'demo'), t2 = [...new Set((await rfq.getRfq(A, id2)).available.map(l => l.trade))];
  for (let i = 0; i < rfq.RFQ_MAX.requests; i++) await rfq.createRfq(A, id2, { title: `Cererea ${i + 1}`, trades: t2 });
  await assert.rejects(() => rfq.createRfq(A, id2, { title: 'una în plus', trades: t2 }), status(409));
  assert.equal((await q('select count(*)::int as n from rfq_requests where project_id=$1', [id2])).rows[0].n, rfq.RFQ_MAX.requests);
});

test('invitații: câte un link unic pe meseriaș, expiră implicit în 30 de zile; cel mult 12 pe cerere; cheia linkului nu e numele din API', async () => {
  const { id, rid } = await fresh('Ofertă invitații', 0);
  for (const body of [null, {}, { label: '' }, { label: '   ' }, { label: 'x'.repeat(81) }, { label: 'x', extra: 1 }, { label: 'x', expiresInDays: 400 }]) await assert.rejects(() => rfq.addInvite(A, id, rid, body), status(400), JSON.stringify(body));
  await assert.rejects(() => rfq.addInvite(A, id, '12345678-1234-4234-8234-123456789abc', { label: 'x' }), status(404)); await assert.rejects(() => rfq.addInvite(A, id, 'nu-e-uuid', { label: 'x' }), status(404));
  const a = await rfq.addInvite(A, id, rid, { label: ' Echipa  <Ion> ' }), b = await rfq.addInvite(A, id, rid, { label: 'Echipa Ion' }), inv = reqOf(b, rid).invites;
  assert.equal(inv.length, 2); assert.notEqual(inv[0]!.token, inv[1]!.token); assert.notEqual(a.key, b.key);
  for (const i of inv){ assert.match(i.token!, /^[A-Za-z0-9_-]{43}$/); assert.equal(i.path, `/oferta/${i.token}`); assert.equal(i.active, true); assert.equal(i.submittedAt, null); assert.equal(i.started, false); assert.equal(i.offer, null);
    assert.match(i.key, /^[0-9a-f]{16}$/); assert.ok(!i.token!.includes(i.key)); assert.equal(i.key, rfq.inviteKey(i.token!));
    const days = (new Date(i.expiresAt!).getTime() - new Date(i.createdAt).getTime()) / 864e5; assert.ok(days > 29.9 && days < 30.1, String(days)); }
  assert.equal(inv[0]!.label, 'Echipa Ion');
  // două adăugări simultane + limita
  await Promise.all(Array.from({ length: 10 }, (_, k) => rfq.addInvite(A, id, rid, { label: `M${k}` })));
  await assert.rejects(() => rfq.addInvite(A, id, rid, { label: 'al 13-lea' }), status(409));
  const all = reqOf(await rfq.getRfq(A, id), rid).invites; assert.equal(all.length, 12); assert.equal(new Set(all.map(i => i.token)).size, 12);
});

test('confidențialitate: răspunsul public nu conține clientul, adresa, bugetul, reperul de piață, celelalte oferte sau ceilalți meseriași', async () => {
  const { id, rid, lines, tokens } = await fresh('Proiect-Nume-Intern Popescu', 2);
  await saveProfile(A, { name: 'Studio Atelier Test', phone: '0733999888', email: 'studio.privat@example.ro', address: 'Str. Studioului 5' });
  await send(tokens[1]!, offer(lines, 77.77, { company: { form: 'srl', vatPayer: true, name: 'Concurent Faianță SRL', cui: 'RO998877' }, notes: 'NOTA-SECRETA-A-ECHIPEI-B' }));
  const pub = await rfq.getPublicOffer(tokens[0]), json = JSON.stringify(pub);
  assert.equal(pub.studio, 'Studio Atelier Test'); assert.equal(pub.title, 'Finisaje apartament'); assert.equal(pub.city, 'Cluj-Napoca'); assert.equal(pub.dueDate, '2026-12-15'); assert.equal(pub.note, 'Materialele le aduce beneficiarul.');
  assert.deepEqual(pub.lines.map(l => [l.rateId, l.label, l.unit, l.qty]), lines.map(l => [l.rateId, l.label, l.unit, l.qty]));
  assert.deepEqual(Object.keys(pub).sort(), ['city', 'closed', 'dueDate', 'lines', 'note', 'offer', 'rev', 'studio', 'submitted', 'submittedAt', 'title', 'vatRate']);
  assert.deepEqual(Object.keys(pub.lines[0]!).sort(), ['label', 'qty', 'rateId', 'rooms', 'trade', 'unit']); assert.deepEqual(Object.keys(pub.lines[0]!.rooms[0]!).sort(), ['name', 'qty']);
  // martori: aceleași șiruri chiar există în datele designerului
  const p = await repo.getProject(A, id), st = await rfq.getRfq(A, id); assert.equal(p.meta.client?.name, CLIENT.name); assert.ok(JSON.stringify(st).includes('NOTA-SECRETA-A-ECHIPEI-B') && JSON.stringify(st).includes('"ref"'));
  const com = await getCommercial(A, id); assert.ok(com.report.totals.client > 0);
  for (const secret of [CLIENT.name, 'Popescu', CLIENT.phone, CLIENT.email, CLIENT.address, 'Secretă', SITE, 'Ascuns', 'Proiect-Nume-Intern', id, rid, 'Echipa A', 'Echipa B', 'Concurent', 'RO998877', 'NOTA-SECRETA', '77.77', tokens[1]!,
    '0733999888', 'studio.privat@example.ro', 'Str. Studioului', '"ref"', 'expected', 'roomId', 'owner', A, String(com.report.totals.client), String(com.chain.total)])
    assert.equal(json.includes(secret), false, `răspunsul public conține „${secret}”`);
  // la fel prin rută, și după ce meseriașul își trimite oferta (își vede doar oferta lui)
  await send(tokens[0]!, offer(lines, 55, { notes: 'oferta mea' }));
  const { GET } = await import('../app/api/oferta/[token]/route'), res = await GET(new Request('http://localhost/x'), { params: Promise.resolve({ token: tokens[0]! }) }), body = await res.text();
  assert.equal(res.status, 200); assert.equal(res.headers.get('cache-control'), 'no-store'); assert.equal(res.headers.get('x-robots-tag'), 'noindex');
  for (const secret of [CLIENT.name, CLIENT.phone, CLIENT.email, 'Secretă', 'Ascuns', 'Echipa B', 'Concurent', 'NOTA-SECRETA', '77.77', tokens[1]!, '"ref"']) assert.equal(body.includes(secret), false, secret);
  assert.ok(body.includes('oferta mea') && JSON.parse(body).submitted === true);
  // fără localitate și fără profil de studio: câmpurile sunt null, nu adresa proiectului
  const idB = await repo.createProject(B, 'Fără localitate', 'demo'); await repo.saveMeta(B, idB, { status: 'design', client: CLIENT, siteAddress: SITE });
  const tB = [...new Set((await rfq.getRfq(B, idB)).available.map(l => l.trade))], mB = await rfq.createRfq(B, idB, { title: 'T', trades: tB }), iB = await rfq.addInvite(B, idB, mB.id, { label: 'E' });
  const pB = await rfq.getPublicOffer(reqOf(iB, mB.id).invites[0]!.token); assert.equal(pB.city, null); assert.equal(pB.studio, null); assert.equal(pB.dueDate, null); assert.equal(pB.note, '');
});

test('salvarea automată: oferta întreagă, validată strict, cu versiune; versiune veche → 409 și nimic suprascris; designerul nu vede ciorna', async () => {
  const { id, rid, lines, token } = await fresh('Ofertă salvare'), l0 = lines[0]!.rateId;
  const g0 = await rfq.getPublicOffer(token); assert.deepEqual({ offer: g0.offer, rev: g0.rev, submitted: g0.submitted, closed: g0.closed }, { offer: { v: 1, prices: {}, inclusions: {}, company: {} }, rev: 0, submitted: false, closed: false });
  assert.deepEqual(await rfq.savePublicOffer(token, { rev: 0, data: { v: 1, prices: { [l0]: 61.5 }, company: { vatPayer: true } } }), { rev: 1 });
  await assert.rejects(() => rfq.savePublicOffer(token, { rev: 0, data: { prices: { [l0]: 99 } } }), (e: any) => e.status === 409 && e.details?.conflict === true, 'a doua filă, cu versiunea veche');
  assert.deepEqual((await rfq.getPublicOffer(token)).offer, { v: 1, prices: { [l0]: 61.5 }, inclusions: {}, company: { vatPayer: true } });
  for (const body of [null, [], { data: {} }, { rev: '1', data: {} }, { rev: 1 }, { rev: 1, data: { necunoscut: 1 } }, { rev: 1, data: { prices: { 'alt-rand': 5 } } }, { rev: 1, data: { prices: { [l0]: -1 } } }, { rev: 1, data: {}, extra: 1 }, { rev: 1e21, data: {} }])
    await assert.rejects(() => rfq.savePublicOffer(token, body), status(400), JSON.stringify(body));
  assert.equal((await rfq.getPublicOffer(token)).rev, 1, 'cererile respinse nu schimbă versiunea');
  const inv = reqOf(await rfq.getRfq(A, id), rid).invites[0]!; assert.equal(inv.started, true); assert.equal(inv.offer, null, 'ciorna netrimisă nu ajunge la designer'); assert.equal(inv.submittedAt, null);
  // ruta: corp peste 16 KB → 413; fără application/json → 415; token cu formă greșită → 404
  const { PUT } = await import('../app/api/oferta/[token]/route');
  const put = (body: string, type = 'application/json', tk = token) => PUT(new Request(`http://localhost/api/oferta/${tk}`, { method: 'PUT', headers: { 'content-type': type }, body }), { params: Promise.resolve({ token: tk }) });
  assert.equal((await put(JSON.stringify({ rev: 1, data: { notes: 'x'.repeat(20000) } }))).status, 413);
  assert.equal((await put(JSON.stringify({ rev: 1, data: {} }), 'text/plain')).status, 415);
  assert.equal((await put('{}', 'application/json', 'scurt')).status, 404);
  const ok = await put(JSON.stringify({ rev: 1, data: { v: 1, prices: { [l0]: 70 } } })); assert.equal(ok.status, 200); assert.deepEqual(await ok.json(), { rev: 2 });
  const conflict = await put(JSON.stringify({ rev: 1, data: {} })); assert.equal(conflict.status, 409); assert.equal((await conflict.json()).conflict, true);
});

test('trimiterea: validare completă cu lipsurile numite; după ea scrierile dau 409, meseriașul își vede oferta read-only, designerul o primește; redeschidere', async () => {
  const { id, rid, lines, token, key } = await fresh('Ofertă trimitere'), o = offer(lines, 60);
  await rfq.savePublicOffer(token, { rev: 0, data: { v: 1, prices: { [lines[0]!.rateId]: 60 } } });
  await assert.rejects(() => rfq.submitPublicOffer(token, { rev: 1 }), (e: any) => e.status === 400 && e.details.missing.includes('vatPayer') && e.details.missing.includes('inclusion:debris') && (lines.length < 2 || e.details.missing.includes(`price:${lines[1]!.rateId}`)));
  await rfq.savePublicOffer(token, { rev: 1, data: { ...o, prices: { ...o.prices, [lines[0]!.rateId]: null } } });
  await assert.rejects(() => rfq.submitPublicOffer(token, { rev: 1 }), (e: any) => e.status === 409 && e.details?.conflict === true, 'versiune veche');
  for (const body of [null, {}, { rev: '2' }, { rev: 2, data: {} }, { rev: 2 ** 31 }]) await assert.rejects(() => rfq.submitPublicOffer(token, body), status(400));
  assert.deepEqual(await rfq.submitPublicOffer(token, { rev: 2 }), { submitted: true, partial: true }, 'un rând marcat „nu ofertez” → ofertă parțială');
  const sub = (e: any) => e.status === 409 && e.details?.submitted === true;
  await assert.rejects(() => rfq.savePublicOffer(token, { rev: 2, data: o }), sub); await assert.rejects(() => rfq.submitPublicOffer(token, { rev: 2 }), sub);
  const g = await rfq.getPublicOffer(token); assert.equal(g.submitted, true); assert.ok(g.submittedAt); assert.equal(g.offer.prices[lines[0]!.rateId], null); assert.equal(g.offer.advancePct, 20);
  const inv = reqOf(await rfq.getRfq(A, id), rid).invites[0]!; assert.ok(inv.submittedAt); assert.deepEqual(inv.offer, g.offer);
  // designerul redeschide: meseriașul poate schimba și retrimite; până atunci designerul nu mai vede oferta ca trimisă
  const re = reqOf(await rfq.reopenInvite(A, id, rid, key), rid).invites[0]!; assert.equal(re.submittedAt, null); assert.equal(re.offer, null);
  const g2 = await rfq.getPublicOffer(token); assert.equal(g2.submitted, false); assert.deepEqual(g2.offer, g.offer, 'oferta veche rămâne ca punct de plecare');
  await rfq.savePublicOffer(token, { rev: g2.rev, data: o }); assert.deepEqual(await rfq.submitPublicOffer(token, { rev: g2.rev + 1 }), { submitted: true, partial: false });
  // ruta de trimitere
  const { POST } = await import('../app/api/oferta/[token]/submit/route');
  const post = (body: string, type = 'application/json') => POST(new Request('http://localhost/x', { method: 'POST', headers: { 'content-type': type }, body }), { params: Promise.resolve({ token }) });
  assert.equal((await post('{"rev":0}', 'text/plain')).status, 415); const again = await post(JSON.stringify({ rev: g2.rev + 1 })); assert.equal(again.status, 409); assert.equal((await again.json()).submitted, true);
});

test('trimiteri simultane: o singură trimitere reușește, restul primesc 409; salvări simultane cu aceeași versiune: una singură trece', async () => {
  const { lines, token } = await fresh('Ofertă concurență');
  const saves = await Promise.allSettled(Array.from({ length: 8 }, (_, k) => rfq.savePublicOffer(token, { rev: 0, data: offer(lines, 50 + k) })));
  assert.equal(saves.filter(s => s.status === 'fulfilled').length, 1); assert.ok(saves.filter(s => s.status === 'rejected').every((s: any) => s.reason.status === 409 && s.reason.details.conflict));
  assert.equal((await rfq.getPublicOffer(token)).rev, 1);
  const subs = await Promise.allSettled(Array.from({ length: 8 }, () => rfq.submitPublicOffer(token, { rev: 1 })));
  assert.equal(subs.filter(s => s.status === 'fulfilled').length, 1); assert.ok(subs.filter(s => s.status === 'rejected').every((s: any) => s.reason.status === 409 && s.reason.details.submitted));
  // trimitere și salvare în același timp: după trimitere nimic nu se mai schimbă
  const two = await fresh('Ofertă concurență 2'); await rfq.savePublicOffer(two.token, { rev: 0, data: offer(two.lines, 40) });
  const race = await Promise.allSettled([rfq.submitPublicOffer(two.token, { rev: 1 }), rfq.savePublicOffer(two.token, { rev: 1, data: offer(two.lines, 1) })]);
  const g = await rfq.getPublicOffer(two.token), price = g.offer.prices[two.lines[0]!.rateId];
  if (race[0].status === 'fulfilled'){ assert.equal(g.submitted, true); assert.equal(price, 40, 'oferta trimisă e cea validată la trimitere'); } else { assert.equal(g.submitted, false); assert.equal(price, 1); }
});

test('revocat, expirat, șters, în coș sau token necunoscut: același 404 „Link inexistent.” pe toate operațiile publice', async () => {
  const { q } = await getDb();
  const all = (token: any) => [() => rfq.getPublicOffer(token), () => rfq.savePublicOffer(token, { rev: 0, data: {} }), () => rfq.submitPublicOffer(token, { rev: 0 })];
  const gone = async (token: any, why: string) => { for (const f of all(token)) await assert.rejects(f, (e: any) => e.status === 404 && e.message === 'Link inexistent.', why); };
  await gone('A'.repeat(43), 'necunoscut'); await gone('scurt', 'formă greșită'); await gone(null, 'null'); await gone({ $ne: 1 }, 'obiect');
  { const { id, rid, lines, tokens, keys } = await fresh('Ofertă revocată', 2); await send(tokens[0]!, offer(lines, 60));
    const st = reqOf(await rfq.revokeInvite(A, id, rid, keys[0]), rid).invites[0]!; assert.equal(st.active, false); assert.equal(st.token, undefined, 'cheia unui link oprit nu se mai întoarce'); assert.ok(st.revokedAt);
    assert.ok(st.offer && st.submittedAt, 'revocarea păstrează oferta trimisă'); await gone(tokens[0], 'revocat');
    assert.equal((await rfq.getPublicOffer(tokens[1])).submitted, false, 'celălalt link merge mai departe'); }
  { const { token } = await fresh('Ofertă expirată'); await q(`update rfq_invites set expires_at = now() - interval '1 minute' where token=$1`, [token]); await gone(token, 'expirat'); }
  { const { id, rid, lines, token } = await fresh('Ofertă în coș'); await send(token, offer(lines, 60));
    await moveToTrash(A, id); await gone(token, 'proiect în coș'); await restoreFromTrash(A, id); await gone(token, 'restaurarea nu repornește linkul');
    assert.ok(reqOf(await rfq.getRfq(A, id), rid).invites[0]!.offer, 'oferta rămâne la designer după restaurare'); }
  { const { id, rid, token, key } = await fresh('Ofertă ștearsă'); await rfq.deleteInvite(A, id, rid, key); await gone(token, 'invitație ștearsă');
    await assert.rejects(() => rfq.deleteInvite(A, id, rid, key), status(404)); await assert.rejects(() => rfq.revokeInvite(A, id, rid, 'f'.repeat(16)), status(404)); await assert.rejects(() => rfq.reopenInvite(A, id, rid, 'x'), status(404)); }
  { const { id, rid, token } = await fresh('Cerere ștearsă'); assert.deepEqual((await rfq.deleteRfq(A, id, rid)).requests, []); await gone(token, 'cerere ștearsă');
    assert.equal((await q('select count(*)::int as n from rfq_invites where token=$1', [token])).rows[0].n, 0, 'ștergerea cererii șterge și ofertele'); await assert.rejects(() => rfq.deleteRfq(A, id, rid), status(404)); }
  // plasa de siguranță: proiect marcat șters fără revocarea linkului
  { const { id, rid, token } = await fresh('Ofertă plasă'); await q('update projects set deleted_at=now() where id=$1', [id]); await gone(token, 'proiect șters, link nerevocat');
    await assert.rejects(() => rfq.addInvite(A, id, rid, { label: 'x' }), status(404)); }
});

test('doar proprietarul: alt proprietar primește 404 pe orice operație a designerului', async () => {
  const { id, rid, lines, token, key } = await fresh('Ofertă proprietar'); await send(token, offer(lines, 60));
  const rev = (await rfq.getRfq(A, id)).projectRev;
  for (const f of [() => rfq.getRfq(B, id), () => rfq.createRfq(B, id, { title: 'x', trades: ['zugraveli'] }), () => rfq.addInvite(B, id, rid, { label: 'x' }), () => rfq.revokeInvite(B, id, rid, key), () => rfq.reopenInvite(B, id, rid, key),
    () => rfq.deleteInvite(B, id, rid, key), () => rfq.deleteRfq(B, id, rid), () => rfq.awardRfq(B, id, rid, { invite: key, rev }), () => rfq.unawardRfq(B, id, rid, { rev }), () => rfq.clearLaborContract(B, id, { rev })])
    await assert.rejects(f, status(404));
  // cererea altui proiect al aceluiași proprietar nu se poate folosi prin alt id de proiect
  const other = await repo.createProject(A, 'Alt proiect', 'demo'), orev = (await rfq.getRfq(A, other)).projectRev;
  for (const f of [() => rfq.addInvite(A, other, rid, { label: 'x' }), () => rfq.revokeInvite(A, other, rid, key), () => rfq.deleteRfq(A, other, rid), () => rfq.awardRfq(A, other, rid, { invite: key, rev: orev })]) await assert.rejects(f, status(404));
  assert.ok(reqOf(await rfq.getRfq(A, id), rid).invites[0]!.offer, 'nimic nu s-a schimbat');
});

test('„Atribuie”: prețurile contractate ajung pe proiect cu versiunea verificată (409 la conflict); devizul schimbă doar costul manoperei; anulare', async () => {
  const { id, rid, lines, tokens, keys } = await fresh('Ofertă atribuire', 3);
  const before = await getCommercial(A, id); assert.equal(before.report.laborCost, before.report.labor, 'fără contract: cost = preț client'); assert.ok(before.report.labor > 0);
  await send(tokens[0]!, offer(lines, 20, { company: { form: 'srl', vatPayer: true, name: 'Faianțari SRL' } }));
  await send(tokens[1]!, offer(lines, 25, { prices: { ...Object.fromEntries(lines.map(l => [l.rateId, 25])), [lines[0]!.rateId]: null } }));
  const st = await rfq.getRfq(A, id), rev = st.projectRev;
  // validare: versiune lipsă / veche, invitație necunoscută, ofertă netrimisă
  await assert.rejects(() => rfq.awardRfq(A, id, rid, { invite: keys[0] }), status(400)); await assert.rejects(() => rfq.awardRfq(A, id, rid, { invite: keys[0], rev: 1e21 }), status(400));
  await assert.rejects(() => rfq.awardRfq(A, id, rid, { invite: keys[0], rev: rev + 1 }), (e: any) => e.status === 409 && e.details?.conflict === true);
  await assert.rejects(() => rfq.awardRfq(A, id, rid, { invite: 'f'.repeat(16), rev }), status(404)); await assert.rejects(() => rfq.awardRfq(A, id, rid, { invite: keys[2], rev }), status(409));
  // editorul salvează între timp (alt dispozitiv): versiunea văzută de designer nu mai e cea curentă → 409, nimic scris
  const p0 = await repo.getProject(A, id); await repo.saveDraft(A, id, p0.draft, p0.draftRev);
  await assert.rejects(() => rfq.awardRfq(A, id, rid, { invite: keys[0], rev }), (e: any) => e.status === 409 && e.details?.conflict === true);
  assert.equal((await repo.getProject(A, id)).draft.laborContract, undefined);
  const won = await rfq.awardRfq(A, id, rid, { invite: keys[0], rev: rev + 1 });
  assert.equal(won.draftRev, rev + 2); assert.deepEqual(won.applied.sort(), lines.map(l => l.rateId).sort()); assert.equal(won.partial, false);
  const p1 = await repo.getProject(A, id); assert.equal(p1.draftRev, rev + 2); assert.deepEqual(p1.draft.laborContract, won.laborContract);
  for (const l of lines){ const c: LaborContractEntry = p1.draft.laborContract![l.rateId]!; assert.equal(c.unitPrice, 20); assert.equal(c.vatPayer, true); assert.equal(c.label, 'Echipa A'); assert.equal(c.requestId, rid); assert.ok(!Number.isNaN(Date.parse(c.at))); }
  const r1 = reqOf(won, rid); assert.equal(r1.awarded, keys[0]); assert.ok(r1.closedAt);
  // restul proiectului e neatins, iar salvarea editorului păstrează prețurile contractate
  const { laborContract: _c, ...rest } = p1.draft as any; assert.deepEqual(rest, p0.draft);
  await repo.saveDraft(A, id, p1.draft, p1.draftRev); assert.deepEqual((await repo.getProject(A, id)).draft.laborContract, won.laborContract);
  // devizul: prețul către client și totalul de facturat rămân; costul manoperei = cantitatea curentă × 20 × 1,21
  const after = await getCommercial(A, id);
  assert.equal(after.report.labor, before.report.labor); assert.equal(after.report.totals.client, before.report.totals.client); assert.deepEqual(after.chain, before.chain);
  const expectCost = Math.round(after.labor.reduce((a, l) => a + Math.round(l.qty * 20 * 1.21 * 100) / 100, 0) * 100) / 100;
  assert.equal(after.report.laborCost, expectCost); assert.ok(after.labor.every(l => l.contract?.unitPrice === 20 && l.contract.label === 'Echipa A'));
  assert.equal(after.report.totals.profit, Math.round((before.report.totals.profit + before.report.labor - expectCost) * 100) / 100); assert.notEqual(after.report.totals.profit, before.report.totals.profit);
  // cererea închisă: ceilalți nu-și mai pot schimba ofertele, nici designerul nu mai invită / redeschide / șterge oferta aleasă
  const g = await rfq.getPublicOffer(tokens[2]); assert.equal(g.closed, true); assert.equal(JSON.stringify(g).includes('Echipa A'), false, 'ceilalți nu află cine a fost ales');
  await assert.rejects(() => rfq.savePublicOffer(tokens[2], { rev: 0, data: {} }), (e: any) => e.status === 409 && e.details?.closed === true);
  await assert.rejects(() => rfq.submitPublicOffer(tokens[2], { rev: 0 }), (e: any) => e.status === 409 && e.details?.closed === true);
  for (const f of [() => rfq.awardRfq(A, id, rid, { invite: keys[1], rev: rev + 2 }), () => rfq.addInvite(A, id, rid, { label: 'x' }), () => rfq.reopenInvite(A, id, rid, keys[0]), () => rfq.deleteInvite(A, id, rid, keys[0]), () => rfq.deleteRfq(A, id, rid)]) await assert.rejects(f, status(409));
  // linkul de prezentare al clientului și copia proiectului nu poartă prețurile contractate
  const revn = await repo.createRevision(A, id, 'cu contract'), sh = await createShare(A, id, revn.number), view = await resolveShare(sh.token);
  assert.equal(JSON.stringify(view).includes('laborContract'), false); assert.equal(JSON.stringify(view).includes('Echipa A'), false);
  const dup = await duplicateProject(A, id); assert.equal((await repo.getProject(A, dup.id)).draft.laborContract, undefined);
  // anularea: prețurile din această cerere ies din proiect, cererea se redeschide; apoi oferta parțială → doar rândurile cu preț
  const rev2 = (await rfq.getRfq(A, id)).projectRev;
  await assert.rejects(() => rfq.unawardRfq(A, id, rid, { rev: rev2 - 1 }), status(409));
  const un = await rfq.unawardRfq(A, id, rid, { rev: rev2 }); assert.deepEqual(un.laborContract, {}); assert.equal((await repo.getProject(A, id)).draft.laborContract, undefined); assert.equal(reqOf(un, rid).awarded, null); assert.equal(reqOf(un, rid).closedAt, null);
  assert.deepEqual((await getCommercial(A, id)).report, before.report, 'fără contract, raportul e exact cel dinainte');
  await assert.rejects(() => rfq.unawardRfq(A, id, rid, { rev: un.draftRev }), status(409));
  const part = await rfq.awardRfq(A, id, rid, { invite: keys[1], rev: un.draftRev }); assert.equal(part.partial, true);
  assert.deepEqual(Object.keys(part.laborContract).sort(), lines.slice(1).map(l => l.rateId).sort()); assert.ok(Object.values(part.laborContract).every(c => c.unitPrice === 25 && c.vatPayer === false && c.label === 'Echipa B'));
  const cl = await rfq.clearLaborContract(A, id, { rev: part.draftRev }); assert.deepEqual(cl.laborContract, {}); assert.equal(reqOf(cl, rid).awarded, null);
  await assert.rejects(() => rfq.clearLaborContract(A, id, { rev: part.draftRev }), status(409));
  // un laborContract invalid trimis de editor e curățat la salvare, nu oprește salvarea
  const p2 = await repo.getProject(A, id); await repo.saveDraft(A, id, { ...p2.draft, laborContract: { 'a b': { unitPrice: 1 }, 'manopera-gresie': { unitPrice: -5, vatPayer: false, label: 'x', at: '2026-11-22', requestId: rid } } }, p2.draftRev);
  assert.equal((await repo.getProject(A, id)).draft.laborContract, undefined);
});

test('poarta publică: pagina și rutele ofertei trec prin tunel fără sesiune; rutele designerului rămân închise; fără Referer; CSRF', async () => {
  const tok = 'A'.repeat(43), uuid = '12345678-1234-4234-8234-123456789abc', root = path.resolve(import.meta.dirname, '..');
  const walk = (d: string): string[] => readdirSync(d).flatMap(f => { const p = path.join(d, f); return statSync(p).isDirectory() ? walk(p) : f === 'route.ts' ? [p] : []; });
  const routes = walk(path.join(root, 'app', 'api', 'oferta')).map(f => ({ file: f, url: '/' + path.relative(path.join(root, 'app'), path.dirname(f)).split(path.sep).join('/').replace('[token]', tok) }));
  assert.deepEqual(routes.map(r => r.url.replace(tok, 'T')).sort(), ['/api/oferta/T', '/api/oferta/T/submit']);
  const remote = (url: string, method = 'GET') => new NextRequest(`http://localhost:3123${url}`, { method, headers: { host: 'localhost:3123', 'cf-connecting-ip': '203.0.113.7' } });
  for (const r of routes){ const mod = await import(r.file); for (const m of ['GET', 'POST', 'PATCH', 'PUT', 'DELETE'].filter(m => typeof mod[m] === 'function')){ const res = middleware(remote(r.url, m)); assert.equal(res.headers.get('x-middleware-next'), '1', `${m} ${r.url} → ${res.status}`); } }
  assert.equal(middleware(remote(`/oferta/${tok}`)).headers.get('x-middleware-next'), '1');
  for (const p of [`/oferta/${tok}`, `/oferta/${tok}/`, `/api/oferta/${tok}`, `/api/oferta/${tok}/submit`]) assert.equal(isPublicPath(p), true, p);
  for (const p of ['/oferta', '/oferta/scurt', `/oferta/${tok}/x`, `/oferta/${tok}x`, `/api/oferta/${tok}/award`, `/api/oferta/${tok}/submit/x`, `/api/oferta/${tok}/../projects`, `/api/oferta/${tok}x`, '/api/oferta', `/api/projects/${uuid}/rfq`,
    `/api/projects/${uuid}/rfq/${uuid}`, `/api/projects/${uuid}/rfq/${uuid}/invites`, `/api/projects/${uuid}/rfq/${uuid}/invites/${'f'.repeat(16)}`]) assert.equal(isPublicPath(p), false, p);
  for (const [p, m] of [[`/api/projects/${uuid}/rfq`, 'GET'], [`/api/projects/${uuid}/rfq`, 'POST'], [`/api/projects/${uuid}/rfq/${uuid}`, 'PATCH'], [`/api/projects/${uuid}/rfq/${uuid}/invites`, 'POST'], [`/api/projects/${uuid}/rfq/${uuid}/invites/${'f'.repeat(16)}`, 'DELETE']] as const)
    assert.equal(middleware(remote(p, m)).status, 401, `${m} ${p}`);
  for (const p of [`/oferta/${tok}`, `/api/oferta/${tok}`, `/api/oferta/${tok}/submit`]){ assert.equal(policyFor(p, 'n').referrer, 'no-referrer', p); assert.equal(middleware(remote(p)).headers.get('referrer-policy'), 'no-referrer'); }
  assert.match(policyFor(`/api/oferta/${tok}`, 'n').csp, /default-src 'none'/);
  const cross = new NextRequest(`http://localhost:3123/api/oferta/${tok}`, { method: 'PUT', headers: { host: 'localhost:3123', origin: 'https://alt-site.example', 'sec-fetch-site': 'cross-site', 'cf-connecting-ip': '203.0.113.7' } });
  assert.equal(middleware(cross).status, 403);
});

test('limite de cereri pe link + IP: 429 în română la salvare și la trimitere; un token cu formă greșită primește 404 fără să consume limita', async () => {
  const { token } = await fresh('Ofertă limite');
  const { PUT } = await import('../app/api/oferta/[token]/route'), { POST } = await import('../app/api/oferta/[token]/submit/route');
  const put = (tk: string, ip: string) => PUT(new Request('http://localhost/x', { method: 'PUT', headers: { 'content-type': 'application/json', 'cf-connecting-ip': ip }, body: '{}' }), { params: Promise.resolve({ token: tk }) });
  for (let i = 0; i < 200; i++) assert.equal((await put('nu-e-token', '198.51.100.9')).status, 404);
  for (let i = 0; i < 120; i++) assert.equal((await put(token, '198.51.100.1')).status, 400, `#${i + 1}`);
  const lim = await put(token, '198.51.100.1'); assert.equal(lim.status, 429); assert.ok(Number(lim.headers.get('retry-after')) > 0); assert.match((await lim.json()).error, /Prea multe trimiteri/);
  assert.equal((await put(token, '198.51.100.2')).status, 400, 'altă adresă nu e blocată');
  const post = (ip: string) => POST(new Request('http://localhost/x', { method: 'POST', headers: { 'content-type': 'application/json', 'cf-connecting-ip': ip }, body: '{}' }), { params: Promise.resolve({ token }) });
  for (let i = 0; i < 20; i++) assert.equal((await post('198.51.100.3')).status, 400);
  assert.equal((await post('198.51.100.3')).status, 429); assert.equal((await put(token, '198.51.100.3')).status, 400, 'limita trimiterii e separată de a salvării');
});

test('exportul proiectului nu conține ofertele meseriașilor (nici cererea, nici linkurile); prețurile contractate sunt în proiect.json', async () => {
  const { id, rid, lines, tokens, keys } = await fresh('Ofertă export', 2);
  await send(tokens[0]!, offer(lines, 33.33, { company: { form: 'srl', vatPayer: true, name: 'FIRMA-UNICA-DIN-OFERTA SRL', cui: 'RO445566' }, notes: 'OBSERVATIE-UNICA-DIN-OFERTA' }));
  await send(tokens[1]!, offer(lines, 44.44, { notes: 'A-DOUA-OFERTA-UNICA' }));
  const unzip = async () => { const zip = (await exportProject(A, id)).data, zlib = await import('node:zlib'), names: string[] = [], bodies: Buffer[] = [];
    for (let p = 0; zip.readUInt32LE(p) === 0x04034b50;){ const method = zip.readUInt16LE(p + 8), size = zip.readUInt32LE(p + 18), nl = zip.readUInt16LE(p + 26), xl = zip.readUInt16LE(p + 28), start = p + 30 + nl + xl;
      names.push(zip.subarray(p + 30, p + 30 + nl).toString('utf8')); const raw = zip.subarray(start, start + size); bodies.push(method === 8 ? zlib.inflateRawSync(raw) : raw); p = start + size; }
    return { names, all: Buffer.concat(bodies) }; };
  const a = await unzip(); assert.ok(a.names.includes('proiect.json') && a.names.length > 8, a.names.join(' ')); assert.ok(!a.names.some(n => /rfq|ofert[ae]-mes|meseria/i.test(n)), a.names.join(' '));
  for (const s of ['FIRMA-UNICA-DIN-OFERTA', 'RO445566', 'OBSERVATIE-UNICA-DIN-OFERTA', 'A-DOUA-OFERTA-UNICA', '33.33', '44.44', tokens[0]!, tokens[1]!, 'Finisaje apartament', 'Materialele le aduce beneficiarul']) assert.equal(a.all.indexOf(s), -1, s);
  assert.ok(a.all.indexOf('casa3d-project-export') > 0, 'martor: conținutul despachetat chiar e citit');
  // după atribuire: doar prețurile contractate (parte din proiect) apar, nu oferta
  await rfq.awardRfq(A, id, rid, { invite: keys[0], rev: (await rfq.getRfq(A, id)).projectRev });
  const b = await unzip(); assert.ok(b.all.indexOf('laborContract') > 0 && b.all.indexOf('33.33') > 0);
  for (const s of ['FIRMA-UNICA-DIN-OFERTA', 'RO445566', 'OBSERVATIE-UNICA-DIN-OFERTA', 'A-DOUA-OFERTA-UNICA', '44.44', tokens[0]!, tokens[1]!]) assert.equal(b.all.indexOf(s), -1, s);
});
