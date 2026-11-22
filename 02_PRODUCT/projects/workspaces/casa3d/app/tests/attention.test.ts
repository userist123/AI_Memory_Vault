// „De urmărit azi” (core/attention.ts + lib/attention.ts): severități și praguri de dată, sortare, limită; pe server: doar
// proiectele proprietarului, fără cele din coș, doar cele active, câte un element pentru fiecare fel de date, rezultat ținut
// în memorie până la prima scriere; timpul pe 20 de proiecte demo.
import { test, beforeAll, vi } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const who = vi.hoisted(() => ({ id: '' }));
vi.mock('@/lib/owner', () => ({ ownerId: async () => who.id }));
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import * as rfq from '../lib/rfq';
import { saveCommercial } from '../lib/commercial';
import { getOrders, setOrderState } from '../lib/procurement';
import { addSnag, updateSnag } from '../lib/site';
import { saveSchedule } from '../lib/schedule';
import { createBriefLink } from '../lib/client-brief';
import { createShare } from '../lib/share';
import { addFeedback } from '../lib/feedback';
import { moveToTrash, restoreFromTrash } from '../lib/trash';
import { getAttention, attentionFacts, ORDER_PROJECTS } from '../lib/attention';
import { GET } from '../app/api/attention/route';
import { attentionItems, attentionReport, attentionByProject, attentionCounts, projectAttention, sortAttention, daysBetween, ATTENTION, type ProjectFacts, type AttentionItem } from '../core/attention';
import type { ProcurementRow } from '../core/procurement';
import { INCLUSIONS, type RfqOffer } from '../core/rfq';
import { ro } from '../lib/locales/ro';
import { en } from '../lib/locales/en';

beforeAll(() => resetDbForTests());
const A = 'aaaaaaaa-1111-4111-8111-111111111111', B = 'bbbbbbbb-2222-4222-8222-222222222222', P = 'cccccccc-3333-4333-8333-333333333333';
const TODAY = '2026-11-20', NOW = new Date().toISOString().slice(0, 10);
const plus = (d: string, n: number) => new Date(Date.parse(d + 'T00:00:00Z') + n * 864e5).toISOString().slice(0, 10);
const F = (x: Partial<ProjectFacts> = {}): ProjectFacts => ({ projectId: 'p1', projectName: 'Apartament Unirii', ...x });
const msgs = (xs: AttentionItem[]) => xs.map(i => i.msg);
const row = (x: Partial<ProcurementRow>): ProcurementRow => ({ key: 'f|x', kind: 'furniture', product: 'Canapea', supplier: 'IKEA', code: null, url: null, qty: 1, unit: 'buc', packs: null, unitPrice: 1, total: 1, rooms: [],
  state: { status: 'todo' }, overdue: false, leadDays: 21, orderBy: null, orderUrgency: null, ...x });

// ---------- funcția pură ----------
test('fără date → nimic; fapte fără probleme → nimic', () => {
  assert.deepEqual(attentionItems([], TODAY), []); assert.deepEqual(attentionItems([F()], TODAY), []);
  assert.deepEqual(attentionReport([], TODAY), { items: [], counts: { urgent: 0, soon: 0, info: 0 }, total: 0 });
  const quiet = F({ milestones: [{ id: 'a', label: 'Avans', fixed: 100, due: plus(TODAY, -30), status: 'paid', paidAt: plus(TODAY, -31) }, { id: 'b', label: 'Final', pct: 100, due: plus(TODAY, 40), status: 'pending' }, { id: 'c', label: 'Fără dată', fixed: 5, status: 'pending' }],
    orders: [row({ state: { status: 'delivered' } })], snags: [{ status: 'verified', createdAt: '2026-01-01T00:00:00Z' }], phases: [{ id: 'x', title: 'Gata', trade: 'paint', start: plus(TODAY, -9), end: plus(TODAY, -5), after: null, done: true }],
    brief: { submittedAt: '2026-11-01T10:00:00Z', importedAt: '2026-11-02T10:00:00Z' }, rfqs: [{ id: 'r', title: 'Închisă', dueDate: plus(TODAY, -9), open: false, offers: 3 }, { id: 'r2', title: 'Fără oferte', dueDate: plus(TODAY, 5), open: true, offers: 0 }],
    share: { expiresAt: null }, feedback: { open: 0, approvals: 0, latest: null } });
  assert.deepEqual(attentionItems([quiet], TODAY), []);
});
test('tranșe: restantă = urgent cu zilele de întârziere; azi și în 7 zile = curând; a 8-a zi și cele plătite nu apar', () => {
  const ms = (due: string, status: 'pending' | 'paid' = 'pending') => ({ id: due, label: `T ${due}`, fixed: 1000, due, status, ...(status === 'paid' ? { paidAt: due } : {}) });
  const it = attentionItems([F({ milestones: [ms(plus(TODAY, -3)), ms(plus(TODAY, -1)), ms(TODAY), ms(plus(TODAY, 7)), ms(plus(TODAY, 8)), ms(plus(TODAY, -20), 'paid')] })], TODAY);
  assert.deepEqual(it.map(i => [i.msg, i.severity, i.n, i.due]), [['payment.late', 'urgent', 3, plus(TODAY, -3)], ['payment.late', 'urgent', 1, plus(TODAY, -1)], ['payment.today', 'soon', 0, TODAY], ['payment.soon', 'soon', 7, plus(TODAY, 7)]]);
  assert.ok(it.every(i => i.kind === 'payment' && i.stage === 'quote' && i.href === '/p/p1?stage=quote' && i.vars.amount === 1000 && i.projectName === 'Apartament Unirii'));
  assert.equal(new Set(it.map(i => i.id)).size, it.length, 'id-uri unice');
  // tranșa pe procent nu are sumă (totalul contractului nu se calculează aici)
  assert.deepEqual(attentionItems([F({ milestones: [{ id: 'p', label: 'Final', pct: 100, due: plus(TODAY, -2), status: 'pending' }] })], TODAY)[0]!.vars, { label: 'Final', pct: 100 });
});
test('comenzi: de comandat cu termen depășit și livrări întârziate = urgent, de comandat curând = curând, cu numărul și cea mai veche dată', () => {
  const it = attentionItems([F({ orders: [row({ key: 'a', orderUrgency: 'late', orderBy: plus(TODAY, -4) }), row({ key: 'b', orderUrgency: 'late', orderBy: plus(TODAY, -9) }), row({ key: 'c', orderUrgency: 'soon', orderBy: plus(TODAY, 2) }),
    row({ key: 'd', state: { status: 'ordered', eta: plus(TODAY, -2) }, overdue: true }), row({ key: 'e', state: { status: 'ordered', eta: plus(TODAY, 9) } })] })], TODAY);
  assert.deepEqual(it.map(i => [i.msg, i.kind, i.severity, i.n, i.due, i.stage]), [['order.late', 'order', 'urgent', 2, plus(TODAY, -9), 'orders'], ['delivery.late', 'delivery', 'urgent', 1, plus(TODAY, -2), 'orders'], ['order.soon', 'order', 'soon', 1, plus(TODAY, 2), 'orders']]);
});
test('remedieri: deschise = de știut, peste 14 zile = curând; cele rezolvate așteaptă verificarea', () => {
  const s = (status: 'open' | 'fixed' | 'verified', ago: number) => ({ status, createdAt: plus(TODAY, -ago) + 'T09:00:00.000Z' });
  const fresh = attentionItems([F({ snags: [s('open', 14), s('open', 2), s('fixed', 40), s('verified', 90)] })], TODAY);
  assert.deepEqual(fresh.map(i => [i.msg, i.severity, i.n, i.due]), [['snag.open', 'info', 2, plus(TODAY, -14)], ['snag.fixed', 'info', 1, null]]);
  const old = attentionItems([F({ snags: [s('open', 15)] })], TODAY);
  assert.deepEqual(old.map(i => [i.msg, i.severity, i.stage]), [['snag.open', 'soon', 'site']]);
});
test('grafic: etapă neterminată cu sfârșitul trecut = urgent; după termenul proiectului = curând; ordine greșită = de știut', () => {
  const ph = (id: string, start: string, end: string, after: string | null = null, done = false) => ({ id, title: `Etapa ${id}`, trade: 'general' as const, start, end, after, done });
  const it = attentionItems([F({ dueDate: plus(TODAY, 10), phases: [ph('a', plus(TODAY, -20), plus(TODAY, -6)), ph('b', plus(TODAY, -8), plus(TODAY, 3), 'a'), ph('c', plus(TODAY, 4), plus(TODAY, 12), 'b'), ph('d', plus(TODAY, -30), plus(TODAY, -25), null, true)] })], TODAY);
  assert.deepEqual(it.map(i => [i.msg, i.severity, i.n, i.due]), [['schedule.late', 'urgent', 1, plus(TODAY, -6)], ['schedule.pastDue', 'soon', 1, plus(TODAY, 10)], ['schedule.order', 'info', 1, null]]);
  assert.equal(it[0]!.vars.label, 'Etapa a'); assert.ok(it.every(i => i.stage === 'site'));
  // sfârșitul chiar azi nu e întârziere
  assert.deepEqual(attentionItems([F({ phases: [ph('a', plus(TODAY, -3), TODAY)] })], TODAY), []);
});
test('situații de lucrări: numere lipsă / perioade suprapuse; în execuție, ultima situație mai veche de 35 de zile', () => {
  const st = [{ number: 1, from: '2026-08-01', to: '2026-08-31' }, { number: 3, from: '2026-08-20', to: '2026-09-30' }];
  assert.deepEqual(attentionItems([F({ statements: st })], TODAY).map(i => [i.msg, i.severity, i.n]), [['statement.issues', 'info', 2]]);
  const ok = [{ number: 1, from: '2026-09-01', to: plus(TODAY, -36) }];
  assert.deepEqual(attentionItems([F({ status: 'execution', statements: ok })], TODAY).map(i => [i.msg, i.n, i.due]), [['statement.stale', 36, plus(TODAY, -36)]]);
  assert.deepEqual(attentionItems([F({ status: 'design', statements: ok })], TODAY), []);
  assert.deepEqual(attentionItems([F({ status: 'execution', statements: [{ number: 1, from: '2026-09-01', to: plus(TODAY, -35) }] })], TODAY), []);
});
test('chestionar, cereri de ofertă, link, păreri', () => {
  assert.deepEqual(attentionItems([F({ brief: { submittedAt: '2026-11-18T10:00:00Z', importedAt: null } })], TODAY).map(i => [i.msg, i.severity, i.due, i.stage]), [['brief.submitted', 'soon', '2026-11-18', 'concept']]);
  assert.deepEqual(attentionItems([F({ brief: { submittedAt: null, importedAt: null } })], TODAY), []);
  const r = attentionItems([F({ rfqs: [{ id: 'a', title: 'Finisaje', dueDate: plus(TODAY, 5), open: true, offers: 2 }, { id: 'b', title: 'Electric', dueDate: plus(TODAY, -1), open: true, offers: 0 }, { id: 'c', title: 'Sanitar', dueDate: plus(TODAY, -2), open: true, offers: 1 },
    { id: 'd', title: 'Azi', dueDate: TODAY, open: true, offers: 0 }] })], TODAY);
  assert.deepEqual(r.map(i => [i.msg, i.severity, i.n, i.vars.label]), [['rfq.dueOffers', 'urgent', 1, 'Sanitar'], ['rfq.dueNone', 'urgent', 0, 'Electric'], ['rfq.offers', 'soon', 2, 'Finisaje']]);
  const sh = (d: number) => attentionItems([F({ share: { expiresAt: plus(TODAY, d) + 'T15:00:00.000Z' } })], TODAY).map(i => [i.msg, i.severity, i.n, i.stage]);
  assert.deepEqual(sh(0), [['share.today', 'soon', 0, 'present']]); assert.deepEqual(sh(7), [['share.soon', 'soon', 7, 'present']]); assert.deepEqual(sh(8), []); assert.deepEqual(sh(-1), []);
  assert.deepEqual(attentionItems([F({ feedback: { open: 2, approvals: 5, latest: '2026-11-19T08:00:00Z' } })], TODAY).map(i => [i.msg, i.severity, i.n, i.due]), [['feedback.open', 'soon', 2, '2026-11-19'], ['feedback.approvals', 'info', 5, '2026-11-19']]);
});
test('sortare (urgent → curând → de știut, apoi data, fără dată la urmă), limită, contoare pe tot, grupare pe proiect', () => {
  const facts = [F({ projectId: 'p1', projectName: 'Zorilor', snags: [{ status: 'open', createdAt: plus(TODAY, -1) + 'T00:00:00Z' }, { status: 'fixed', createdAt: plus(TODAY, -1) + 'T00:00:00Z' }], milestones: [{ id: 'm', label: 'Avans', fixed: 1, due: plus(TODAY, -2), status: 'pending' }] }),
    F({ projectId: 'p2', projectName: 'Aviatorilor', milestones: [{ id: 'm', label: 'Avans', fixed: 1, due: plus(TODAY, -10), status: 'pending' }, { id: 'n', label: 'Final', fixed: 1, due: plus(TODAY, 3), status: 'pending' }], brief: { submittedAt: plus(TODAY, -5) + 'T00:00:00Z', importedAt: null } })];
  const all = attentionItems(facts, TODAY);
  assert.deepEqual(all.map(i => [i.projectId, i.msg]), [['p2', 'payment.late'], ['p1', 'payment.late'], ['p2', 'brief.submitted'], ['p2', 'payment.soon'], ['p1', 'snag.open'], ['p1', 'snag.fixed']]);
  assert.deepEqual(sortAttention([...all].reverse()), all, 'ordinea nu depinde de ordinea de intrare');
  assert.deepEqual(attentionCounts(all), { urgent: 2, soon: 2, info: 2 });
  const rep = attentionReport(facts, TODAY, 3); assert.equal(rep.items.length, 3); assert.equal(rep.total, 6); assert.deepEqual(rep.counts, { urgent: 2, soon: 2, info: 2 }); assert.deepEqual(msgs(rep.items), msgs(all).slice(0, 3));
  assert.deepEqual(attentionItems(facts, TODAY, 0), []);
  const g = attentionByProject(all); assert.deepEqual(g.map(x => [x.projectId, x.severity, x.items.length]), [['p2', 'urgent', 3], ['p1', 'urgent', 3]]);
  // limita implicită: 50
  const many = Array.from({ length: 80 }, (_, k) => F({ projectId: `q${k}`, projectName: `P${k}`, brief: { submittedAt: '2026-11-01T00:00:00Z', importedAt: null } }));
  assert.equal(attentionItems(many, TODAY).length, ATTENTION.max); assert.equal(attentionReport(many, TODAY).total, 80);
  assert.equal(daysBetween('2026-02-27', '2026-03-01'), 2);
});
test('fiecare mesaj are text în română și în engleză (cu forma de singular)', () => {
  const everything = F({ status: 'execution', dueDate: plus(TODAY, 1), milestones: [{ id: 'a', label: 'A', fixed: 1, due: plus(TODAY, -1), status: 'pending' }, { id: 'b', label: 'B', fixed: 1, due: TODAY, status: 'pending' }, { id: 'c', label: 'C', fixed: 1, due: plus(TODAY, 2), status: 'pending' }],
    orders: [row({ orderUrgency: 'late', orderBy: plus(TODAY, -1) }), row({ orderUrgency: 'soon', orderBy: plus(TODAY, 1) }), row({ overdue: true, state: { status: 'ordered', eta: plus(TODAY, -1) } })],
    snags: [{ status: 'open', createdAt: TODAY + 'T00:00:00Z' }, { status: 'fixed', createdAt: TODAY + 'T00:00:00Z' }],
    phases: [{ id: 'a', title: 'A', trade: 'general', start: plus(TODAY, -9), end: plus(TODAY, -2), after: null, done: false }, { id: 'b', title: 'B', trade: 'general', start: plus(TODAY, -5), end: plus(TODAY, 5), after: 'a', done: false }],
    statements: [{ number: 2, from: '2026-01-01', to: '2026-01-31' }], brief: { submittedAt: TODAY + 'T00:00:00Z', importedAt: null },
    rfqs: [{ id: 'a', title: 'A', dueDate: null, open: true, offers: 1 }, { id: 'b', title: 'B', dueDate: plus(TODAY, -1), open: true, offers: 0 }, { id: 'c', title: 'C', dueDate: plus(TODAY, -1), open: true, offers: 2 }],
    share: { expiresAt: plus(TODAY, 2) + 'T00:00:00Z' }, feedback: { open: 1, approvals: 1, latest: null } });
  const keys = new Set([...msgs(projectAttention(everything, TODAY)), ...msgs(projectAttention({ ...everything, share: { expiresAt: TODAY + 'T23:00:00Z' } }, TODAY))]);
  assert.equal(keys.size, 21, [...keys].join(' '));
  for (const k of keys) for (const d of [ro, en] as Record<string, string>[]){ assert.ok(d[`attn.${k}`], `attn.${k}`); }
  for (const k of ['payment.late', 'payment.soon', 'order.late', 'order.soon', 'delivery.late', 'snag.open', 'snag.fixed', 'schedule.late', 'rfq.offers', 'rfq.dueOffers', 'share.soon', 'feedback.open', 'feedback.approvals'])
    for (const d of [ro, en] as Record<string, string>[]) assert.ok(d[`attn.${k}.one`], `attn.${k}.one`);
});

// ---------- serverul ----------
const INC = Object.fromEntries(INCLUSIONS.map(k => [k, { mode: 'included' }])) as RfqOffer['inclusions'];
const find = (xs: AttentionItem[], id: string, msg: string) => xs.find(i => i.projectId === id && i.msg === msg);
test('server: fiecare fel de element apare când datele o cer; alt proprietar nu vede nimic; coșul și proiectele inactive lipsesc', async () => {
  const { q } = await getDb();
  assert.deepEqual((await getAttention(A)).items, [], 'fără proiecte');
  const id = await repo.createProject(A, 'Apartament Dorobanți', 'demo'), quiet = await repo.createProject(A, 'Fără probleme', 'demo'), other = await repo.createProject(B, 'Al altcuiva', 'demo');
  assert.deepEqual((await getAttention(A)).items, [], 'proiecte noi, nimic de urmărit'); assert.equal((await getAttention(A)).projects, 2);

  // tranșă restantă + una scadentă în 3 zile
  await saveCommercial(A, id, { milestones: [{ id: 'avans', label: 'Avans', fixed: 5000, due: plus(NOW, -6), status: 'pending' }, { id: 'final', label: 'Final', fixed: 9000, due: plus(NOW, 3), status: 'pending' }] });
  // comenzi: lucrările au început acum 5 zile → tot ce nu e comandat e întârziat; o comandă cu livrarea promisă trecută
  await repo.saveMeta(A, id, { status: 'execution', startDate: plus(NOW, -5), dueDate: plus(NOW, 60) });
  const rows = (await getOrders(A, id, NOW)).rows; assert.ok(rows.length >= 3, 'proiectul demo are linii de comandă');
  await setOrderState(A, id, rows[0]!.key, { status: 'ordered', orderedAt: plus(NOW, -20), eta: plus(NOW, -2) });
  // remediere deschisă, etapă de execuție depășită
  const snag = await addSnag(A, id, { title: 'Priză strâmbă în living', trade: 'electric' });
  await saveSchedule(A, id, { phases: [{ id: 'ph-1', title: 'Tencuieli', trade: 'general', start: plus(NOW, -20), end: plus(NOW, -4), after: null, done: false }] });
  // chestionar trimis de client și nepreluat
  const brief = await createBriefLink(A, id); await q('update client_briefs set submitted_at=now() where project_id=$1', [id]); assert.ok(brief);
  // ofertă de la meseriaș, trimisă pe linkul lui
  const st0 = await rfq.getRfq(A, id), made = await rfq.createRfq(A, id, { title: 'Finisaje', trades: [...new Set(st0.available.map(l => l.trade))], dueDate: plus(NOW, 10) }), inv = await rfq.addInvite(A, id, made.id, { label: 'Echipa A' });
  const req = inv.requests.find(r => r.id === made.id)!, token = req.invites[0]!.token!, pub = await rfq.getPublicOffer(token);
  const offer: RfqOffer = { v: 1, prices: Object.fromEntries(req.lines.map(l => [l.rateId, 50])), inclusions: INC, company: { form: 'pfa', vatPayer: false }, advancePct: 20, startDate: plus(NOW, 14), durationDays: 15, warrantyMonths: 12, validDays: 30 };
  const saved = await rfq.savePublicOffer(token, { rev: pub.rev, data: offer }); await rfq.submitPublicOffer(token, { rev: saved.rev });
  // link de prezentare care expiră în 3 zile, cu o observație a clientului
  await repo.createRevision(A, id, 'v1'); const share = await createShare(A, id, 1, 3);
  await addFeedback(share.token, { verdict: 'change', author: 'Clientul', text: 'Aș vrea altă culoare în dormitor.' });
  await addFeedback(share.token, { verdict: 'like', author: 'Clientul' });

  const r = await getAttention(A, { today: NOW }), mine = r.items.filter(i => i.projectId === id);
  assert.equal(r.projects, 2); assert.equal(r.today, NOW); assert.ok(r.items.every(i => i.projectId === id), 'doar proiectul cu probleme');
  const pay = find(mine, id, 'payment.late')!; assert.deepEqual([pay.severity, pay.n, pay.due, pay.vars.label, pay.vars.amount, pay.href], ['urgent', 6, plus(NOW, -6), 'Avans', 5000, `/p/${id}?stage=quote`]);
  assert.deepEqual([find(mine, id, 'payment.soon')!.severity, find(mine, id, 'payment.soon')!.n], ['soon', 3]);
  const late = find(mine, id, 'order.late')!; assert.equal(late.severity, 'urgent'); assert.equal(late.n, rows.length - 1, 'toate liniile necomandate'); assert.equal(late.stage, 'orders');
  assert.deepEqual([find(mine, id, 'delivery.late')!.severity, find(mine, id, 'delivery.late')!.n, find(mine, id, 'delivery.late')!.due], ['urgent', 1, plus(NOW, -2)]);
  assert.deepEqual([find(mine, id, 'snag.open')!.n, find(mine, id, 'snag.open')!.stage], [1, 'site']);
  assert.deepEqual([find(mine, id, 'schedule.late')!.severity, find(mine, id, 'schedule.late')!.vars.label], ['urgent', 'Tencuieli']);
  assert.deepEqual([find(mine, id, 'brief.submitted')!.severity, find(mine, id, 'brief.submitted')!.stage], ['soon', 'concept']);
  assert.deepEqual([find(mine, id, 'rfq.offers')!.severity, find(mine, id, 'rfq.offers')!.n, find(mine, id, 'rfq.offers')!.vars.label], ['soon', 1, 'Finisaje']);
  const sh = find(mine, id, 'share.soon')!; assert.equal(sh.severity, 'soon'); assert.ok(sh.n === 3 || sh.n === 2, 'expiră în 3 zile (2 dacă testul trece de miezul nopții)');
  assert.deepEqual([find(mine, id, 'feedback.open')!.n, find(mine, id, 'feedback.approvals')!.n], [1, 1]);
  assert.deepEqual(r.counts, attentionCounts(r.items)); assert.equal(r.total, r.items.length);
  assert.deepEqual(r.items.map(i => i.severity), [...r.items.map(i => i.severity)].sort((a, b) => ['urgent', 'soon', 'info'].indexOf(a) - ['urgent', 'soon', 'info'].indexOf(b)), 'urgentele întâi');
  // nimic intern în plus: fără prețuri de achiziție, fără chei de linkuri
  assert.ok(!JSON.stringify(r).includes(token) && !JSON.stringify(r).includes(share.token));

  // izolare: alt proprietar (cu proiectul lui) și un proprietar fără proiecte nu văd nimic
  assert.deepEqual((await getAttention(B, { today: NOW })).items, []); assert.equal((await getAttention(B)).projects, 1); assert.deepEqual((await getAttention(P)).items, []);
  await saveCommercial(B, other, { milestones: [{ id: 'x', label: 'Avansul lui B', fixed: 1, due: plus(NOW, -1), status: 'pending' }] });
  assert.deepEqual((await getAttention(B, { today: NOW })).items.map(i => [i.projectId, i.msg]), [[other, 'payment.late']]);
  assert.ok((await getAttention(A, { today: NOW })).items.every(i => i.projectId === id), 'A nu vede tranșa lui B');

  // rezolvarea scoate elementul imediat (rezultatul ținut în memorie cade la scriere)
  await updateSnag(A, id, snag.id, { status: 'verified' }); assert.equal(find((await getAttention(A, { today: NOW })).items, id, 'snag.open'), undefined);
  await rfq.awardRfq(A, id, made.id, { invite: req.invites[0]!.key, rev: (await repo.getProject(A, id)).draftRev });
  assert.equal(find((await getAttention(A, { today: NOW })).items, id, 'rfq.offers'), undefined, 'cererea atribuită nu mai așteaptă');

  // coș: proiectul dispare cu totul; restaurat, revine
  await moveToTrash(A, id); assert.deepEqual((await getAttention(A, { today: NOW })).items, []); assert.equal((await getAttention(A, { today: NOW })).projects, 1);
  await restoreFromTrash(A, id); assert.ok(find((await getAttention(A, { today: NOW })).items, id, 'payment.late'));

  // activ = modificat în ultimele 120 de zile sau cu ceva nefinalizat: un proiect vechi cu un chestionar trimis nu apare…
  await createBriefLink(A, quiet); await q('update client_briefs set submitted_at=now() where project_id=$1', [quiet]);
  assert.ok(find((await getAttention(A, { today: NOW })).items, quiet, 'brief.submitted'), 'proiect recent');
  await q(`update projects set updated_at = now() - interval '121 days' where id=$1`, [quiet]);
  assert.equal(find((await getAttention(A, { today: NOW })).items, quiet, 'brief.submitted'), undefined, 'proiect vechi, fără nimic nefinalizat');
  // …dar o remediere neverificată sau o tranșă neplătită cu scadență îl ține activ
  const s2 = await addSnag(A, quiet, { title: 'Silicon la cadă', trade: 'plumbing' });
  assert.ok(find((await getAttention(A, { today: NOW })).items, quiet, 'brief.submitted')); assert.ok(find((await getAttention(A, { today: NOW })).items, quiet, 'snag.open'));
  await updateSnag(A, quiet, s2.id, { status: 'verified' }); assert.equal(find((await getAttention(A, { today: NOW })).items, quiet, 'brief.submitted'), undefined);
  await saveCommercial(A, quiet, { milestones: [{ id: 'v', label: 'Tranșa veche', fixed: 700, due: plus(NOW, -200), status: 'pending' }] });
  assert.equal(find((await getAttention(A, { today: NOW })).items, quiet, 'payment.late')!.n, 200);
});
test('server: rezultatul se ține în memorie până la prima scriere în bază; ?fresh și altă zi recalculează; ruta răspunde proprietarului', async () => {
  const O = 'dddddddd-4444-4444-8444-444444444444', id = await repo.createProject(O, 'Cache', 'demo');
  await saveCommercial(O, id, { milestones: [{ id: 'a', label: 'Avans', fixed: 10, due: plus(NOW, -1), status: 'pending' }] });
  const a = await getAttention(O), b = await getAttention(O); assert.equal(a, b, 'același obiect: fără recalculare'); assert.equal(a.items.length, 1);
  assert.notEqual(await getAttention(O, { fresh: true }), a); assert.notEqual(await getAttention(O, { today: plus(NOW, 1) }), a);
  const c = await getAttention(O); await saveCommercial(O, id, { milestones: [{ id: 'a', label: 'Avans', fixed: 10, due: plus(NOW, -1), status: 'paid', paidAt: NOW }] });
  const d = await getAttention(O); assert.notEqual(d, c); assert.deepEqual(d.items, [], 'tranșa plătită dispare fără așteptare');
  // limita cerută explicit nu atinge rezultatul ținut în memorie
  assert.equal((await getAttention(O, { max: 0 })).items.length, 0); assert.equal(await getAttention(O), d);
  // ruta: răspunde cu lista proprietarului din cookie, nu a altuia
  await saveCommercial(O, id, { milestones: [{ id: 'a', label: 'Avans', fixed: 10, due: plus(NOW, -1), status: 'pending' }] });
  who.id = O; const res = await GET(new Request('http://localhost/api/attention?fresh=1')); assert.equal(res.status, 200);
  const j = await res.json(); assert.deepEqual(j.items.map((i: AttentionItem) => [i.projectId, i.msg]), [[id, 'payment.late']]); assert.deepEqual(j.counts, { urgent: 1, soon: 0, info: 0 });
  who.id = 'ffffffff-6666-4666-8666-666666666666'; assert.deepEqual((await (await GET(new Request('http://localhost/api/attention'))).json()).items, []);
});
test('performanță: 20 de proiecte demo cu comenzi de calculat — o singură trecere, interogări grupate', async () => {
  const O = 'eeeeeeee-5555-4555-8555-555555555555', N = 20, ids: string[] = [];
  for (let k = 0; k < N; k++){ const id = await repo.createProject(O, `Lucrarea ${k + 1}`, 'demo'); ids.push(id);
    await repo.saveMeta(O, id, { status: 'execution', startDate: plus(NOW, -3 - k) });
    await saveCommercial(O, id, { milestones: [{ id: 'a', label: 'Avans', fixed: 1000 + k, due: plus(NOW, -1 - k), status: 'pending' }] });
    if (k % 2 === 0) await addSnag(O, id, { title: `Remediere ${k}`, trade: 'paint' }); }
  // numărăm interogările: nu cresc cu numărul de proiecte
  const db = await getDb(), q0 = db.q; let queries = 0; (db as any).q = (s: string, p?: unknown[]) => { queries++; return q0(s, p); };
  let cold: number, facts: Awaited<ReturnType<typeof attentionFacts>>;
  try { const t0 = performance.now(); facts = await attentionFacts(O, NOW); cold = performance.now() - t0; } finally { (db as any).q = q0; }
  assert.equal(facts.facts.length, N); assert.equal(facts.ordersSkipped, 0); assert.ok(facts.facts.every(f => (f.orders ?? []).length >= 3), 'liniile de comandă sunt calculate pentru toate');
  assert.ok(queries <= 20, `interogări: ${queries} (nu câte una pe proiect)`);
  const t1 = performance.now(), r = await getAttention(O, { fresh: true }), full = performance.now() - t1, t2 = performance.now(); await getAttention(O); const cached = performance.now() - t2;
  assert.equal(r.projects, N); assert.equal(r.counts.urgent, 2 * N, 'o tranșă restantă și comenzi întârziate pe fiecare proiect'); assert.equal(r.items.length, ATTENTION.max); assert.equal(r.total, 2 * N + N / 2);
  console.log(`[attention] ${N} proiecte demo: fapte ${cold.toFixed(0)} ms (${queries} interogări), cerere completă ${full.toFixed(0)} ms, din memorie ${cached.toFixed(2)} ms`);
  assert.ok(full < 15000, `prea lent: ${full.toFixed(0)} ms`); assert.ok(cached < 50, `rezultatul din memorie: ${cached.toFixed(2)} ms`);
  // peste limita de calcul al comenzilor: proiectele în plus sunt numărate, nu calculate
  assert.ok(ORDER_PROJECTS >= N);
});
