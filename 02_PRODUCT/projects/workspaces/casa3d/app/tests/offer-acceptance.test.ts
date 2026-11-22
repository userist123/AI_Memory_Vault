// Acceptarea ofertei pe linkul clientului (core/offer-acceptance.ts + lib/offer-acceptance.ts + rutele /api/share/[token]/offer):
// instantaneu canonic și amprentă stabile, validarea cererii, activarea doar cu PIN, acceptarea înregistrează exact
// instantaneul serverului, 409 la ofertă schimbată, idempotență, alt link / alt proiect / link oprit → 404, limite, fără IP.
import { test, beforeAll, beforeEach } from 'vitest';
import assert from 'node:assert/strict';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
process.env.CASA3D_AUTH_FILE = path.join(import.meta.dirname, '__no_auth_file__.json');
import { NextRequest } from 'next/server';
import { proxy } from '../proxy';
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare, listShares } from '../lib/share';
import { setSharePin, clearSharePin, verifySharePin, PIN_COOKIE } from '../lib/share-pin';
import { saveContract } from '../lib/contract';
import { saveProfile } from '../lib/studio-profile';
import { moveToTrash } from '../lib/trash';
import { resetShareLimits } from '../lib/rate-limit';
import { isPublicPath } from '../lib/public-gate';
import { getAttention } from '../lib/attention';
import { exportProject } from '../lib/project-export';
import { sha256, listAcceptanceRecords, acceptanceAdmin, setOfferAccept, listAcceptances, getAcceptance, withdrawAcceptance, offerForShare, acceptOffer, confirmationForShare } from '../lib/offer-acceptance';
import { GET as offerGET, POST as offerPOST } from '../app/api/share/[token]/offer/route';
import { GET as confirmGET } from '../app/api/share/[token]/offer/confirmation/route';
import { buildOfferSnapshot, canonicalJson, offerHash, isOfferHash, offerAcceptable, checkAcceptance, acceptanceState, offerDiff, confirmationDoc, confirmationText, roDateTime, roDay, withdrawalDeadline,
  legalTexts, personKey, AcceptError, acceptErrorStatus, STALE_MESSAGE, ACCEPT_LIMITS, type Acceptance, type AcceptanceRecord } from '../core/offer-acceptance';
import { DEFAULT_CONTRACT, type DocContext } from '../core/contract';
import { projectAttention, ATTENTION } from '../core/attention';
beforeAll(() => resetDbForTests());
beforeEach(() => resetShareLimits());
const A = 'aaaaaaaa-2222-4222-8222-aaaaaaaaaaaa', B = 'bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb';

// ---------- pur ----------
const ctx = (over: Partial<DocContext> = {}): DocContext => ({ studio: { name: 'Studio Linia', cui: 'RO123', address: 'Str. Lungă 1, Cluj', email: 'office@linia.ro', phone: '0740 000 000', iban: 'RO49 AAAA 1B31 0075 9384 0000', bank: 'Banca X', logo: 'data:image/png;base64,AAAA' },
  client: { name: 'Ana Pop', address: 'Str. Scurtă 2', phone: '0722 111 222', email: 'ana@example.com', notes: 'notă internă' }, siteAddress: 'Str. Lucrării 3', projectName: 'Apartament Ana',
  rooms: [{ name: 'Living', areaM2: 24.5 }, { name: 'Baie', areaM2: 5.2 }], areaM2: 29.7, meta: { startDate: '2026-12-01', dueDate: '2027-02-15' },
  data: { ...DEFAULT_CONTRACT, phases: [...DEFAULT_CONTRACT.phases], fee: { kind: 'fixed', amount: 6000 }, vatPayer: true, validUntil: '2026-12-31' }, budgetBase: 40000, today: '2026-11-22', ...over });
const shuffle = (v: any): any => Array.isArray(v) ? v.map(shuffle) : v && typeof v === 'object' ? Object.fromEntries(Object.keys(v).reverse().map(k => [k, shuffle(v[k])])) : v;

test('instantaneul: oferta, prețul cu TVA, graficul, studioul, clientul și textele v2; fără logo, fără notele interne, fără data zilei', () => {
  const s = buildOfferSnapshot(ctx());
  assert.equal(s.format, 'casa3d-offer-acceptance'); assert.equal(s.version, 1); assert.equal(s.texts.version, 'v2');
  assert.deepEqual(s.price, { currency: 'RON', net: 6000, vat: 1260, gross: 7260, vatPayer: true, vatRate: 0.21 });
  assert.deepEqual(s.schedule.map(p => [p.key, p.pct, p.amount]), [['deposit', 30, 2178], ['installment', 35, 2541], ['final', 35, 2541]]);
  assert.equal(s.schedule.reduce((a, p) => a + p.amount, 0), 7260); assert.match(s.schedule[0]!.label, /^Avans/); assert.ok(s.schedule.every(p => p.label));
  assert.equal((s.studio as any).logo, undefined); assert.equal((s.client as any).notes, undefined); assert.equal(s.client.name, 'Ana Pop');
  assert.deepEqual(s.terms, { startDate: '2026-12-01', dueDate: '2027-02-15', validUntil: '2026-12-31' });
  assert.deepEqual(s.offer.sections.map(x => x.id), ['for', 'scope', 'phases', 'fee', 'payment', 'revisions', 'validity']);
  // textele cerute de cercetare, exact
  assert.equal(s.texts.nature, 'Confirmare online a acceptării ofertei (nu este semnătură electronică calificată).');
  assert.equal(s.texts.buttonLabel, 'Accept oferta — cu obligație de plată');
  assert.equal(s.texts.acceptLabel, 'Am citit și accept oferta și graficul de plăți.');
  assert.equal(s.texts.earlyStartLabel, 'Cer începerea lucrărilor înainte de expirarea termenului de retragere de 14 zile, înțeleg că, dacă mă retrag, plătesc proporțional ce s-a executat și confirm că am luat cunoștință că îmi pierd dreptul de retragere după executarea completă a contractului.');
  assert.match(s.texts.disclaimer, /nu constituie consultanță juridică/); 
  // dreptul de retragere: textul oficial al anexei OUG 34/2014, completat pentru servicii și cu datele studioului
  const w = s.texts.withdrawal;
  assert.equal(w.title, 'Dreptul de retragere'); assert.equal(w.consequencesTitle, 'Consecințele retragerii'); assert.equal(w.form.title, 'Model de formular de retragere');
  assert.equal(w.info[0], 'Aveți dreptul de a vă retrage din prezentul contract, fără a preciza motivele, în termen de 14 zile. Perioada de retragere expiră după 14 zile începând de la ziua încheierii contractului.');
  assert.equal(w.info[1], 'Pentru a vă exercita dreptul de retragere trebuie să ne informați (Studio Linia, Str. Lungă 1, Cluj, telefon 0740 000 000, e-mail office@linia.ro) cu privire la decizia dumneavoastră de a vă retrage din prezentul contract, utilizând o declarație neechivocă, de exemplu, o scrisoare trimisă prin poștă, fax sau e-mail. În acest scop, puteți folosi modelul de retragere alăturat; folosirea lui nu este însă obligatorie.');
  assert.equal(w.info[2], 'Pentru a respecta termenul-limită de retragere este suficient să trimiteți comunicarea privind exercitarea dreptului de retragere înainte de expirarea perioadei de retragere.');
  assert.equal(w.consequences.length, 3); assert.match(w.consequences[0]!, /^Dacă vă retrageți, vom rambursa orice sumă pe care am primit-o de la dumneavoastră, inclusiv costurile livrării.*nu mai târziu de 14 zile de la data la care suntem informați/);
  assert.match(w.consequences[1]!, /^Vom efectua această rambursare folosind aceeași modalitate de plată.*nu vi se vor percepe comisioane ca urmare a unei astfel de rambursări\.$/);
  assert.equal(w.consequences[2], 'Dacă ați solicitat începerea prestării serviciilor în perioada de retragere, ne datorați o sumă proporțională cu serviciile furnizate până în momentul în care ne-ați comunicat exercitarea dreptului de retragere din prezentul contract, în raport cu acoperirea totală a contractului.');
  assert.match(w.loss, /executate integral.*acordul dumneavoastră prealabil expres.*pierdeți dreptul de retragere după executarea completă.*art\. 16 lit\. a/);
  assert.match(w.scope, /consumatori.*art\. 9/); assert.match(w.source, /anexa la OUG nr\. 34\/2014 \(forma consolidată la 27\.03\.2026\).*de verificat cu avocatul/);
  assert.equal(w.form.note, '(Prezentul formular se trimite înapoi completat doar dacă doriți să vă retrageți din contract.)'); assert.equal(w.form.footnote, '(*) A se elimina mențiunea inutilă, după caz.');
  assert.deepEqual(w.form.lines, ['Către Studio Linia, Str. Lungă 1, Cluj, office@linia.ro:',
    'Vă informez/Vă informăm (*) prin prezenta cu privire la retragerea mea/noastră (*) din contractul referitor la vânzarea următoarelor bunuri (*)/prestarea următoarelor servicii (*)',
    'Comandate la data (*)/Primite la data (*)', 'Numele consumatorului (consumatorilor)', 'Adresa consumatorului (consumatorilor)',
    'Semnătura consumatorului (consumatorilor) (doar în cazul în care acest formular este notificat pe hârtie)', 'Data']);
  // oferta nu mai spune că acceptarea se face doar în scris, după semnarea unui contract
  const validity = s.offer.sections.find(x => x.id === 'validity')!;
  assert.equal(validity.title, 'Valabilitatea și acceptarea ofertei'); assert.match(validity.paras[1]!, /confirmată online pe linkul de prezentare sau în scris, încheie contractul între părți pe baza acestei oferte/);
  assert.match(validity.paras[2]!, /de verificat cu avocatul/); assert.doesNotMatch(JSON.stringify(s.offer), /se confirmă în scris|după semnarea contractului/);
  assert.match(s.texts.retention, /Nu se păstrează adresa IP/); assert.match(s.texts.binds, /Ana Pop/); assert.match(s.texts.changes, /devize suplimentare/);
  // data zilei nu intră în instantaneu: aceeași ofertă are aceeași amprentă și mâine
  assert.equal(offerHash(buildOfferSnapshot(ctx({ today: '2026-11-23' })), sha256), offerHash(s, sha256));
  assert.doesNotMatch(canonicalJson(s), /2026-11-22|22\.11\.2026/);
});

test('serializarea canonică: chei sortate la orice adâncime, stabilă la ordinea cheilor și la trecerea prin JSON; amprentă SHA-256', () => {
  assert.equal(canonicalJson({ b: 1, a: [{ d: null, c: 'x' }, 2.5], z: undefined, e: true }), '{"a":[{"c":"x","d":null},2.5],"b":1,"e":true}');
  assert.equal(canonicalJson('é'), canonicalJson('é'), 'forma NFC');
  assert.throws(() => canonicalJson({ a: NaN })); assert.throws(() => canonicalJson({ a: () => 1 }));
  const s = buildOfferSnapshot(ctx()), h = offerHash(s, sha256);
  assert.ok(isOfferHash(h)); assert.equal(sha256('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
  assert.equal(canonicalJson(shuffle(s)), canonicalJson(s)); assert.equal(offerHash(JSON.parse(JSON.stringify(shuffle(s))), sha256), h);
  assert.equal(offerHash(buildOfferSnapshot(ctx()), sha256), h, 'determinist');
  // orice schimbare de conținut schimbă amprenta
  for (const over of [{ data: { ...ctx().data, fee: { kind: 'fixed' as const, amount: 6001 } } }, { data: { ...ctx().data, depositPct: 40 } }, { client: { name: 'Ion Pop' } }, { studio: { ...ctx().studio, phone: '0741' } }, { meta: { dueDate: '2027-03-01' } }])
    assert.notEqual(offerHash(buildOfferSnapshot(ctx(over)), sha256), h, JSON.stringify(over));
  assert.ok(!isOfferHash(h.toUpperCase())); assert.ok(!isOfferHash(h.slice(1))); assert.ok(!isOfferHash(null));
});

test('oferta se poate accepta doar cu preț și grafic, cu studioul identificat și în valabilitate', () => {
  assert.deepEqual(offerAcceptable(buildOfferSnapshot(ctx()), '2026-12-31'), { ok: true });
  assert.deepEqual(offerAcceptable(buildOfferSnapshot(ctx()), '2027-01-01'), { ok: false, reason: 'expired' });
  assert.deepEqual(offerAcceptable(buildOfferSnapshot(ctx({ data: { ...DEFAULT_CONTRACT } })), '2026-11-22'), { ok: false, reason: 'no_price' });
  assert.deepEqual(offerAcceptable(buildOfferSnapshot(ctx({ studio: {} })), '2026-11-22'), { ok: false, reason: 'no_studio' });
  assert.deepEqual(offerAcceptable(buildOfferSnapshot(ctx({ studio: { name: 'Doar nume' } })), '2026-11-22'), { ok: false, reason: 'no_studio' });
});

test('validarea cererii: PIN, nume 2–120, bifa de acceptare obligatorie, bifa de începere opțională dar înregistrată, amprenta curentă', () => {
  const H = 'a'.repeat(64), ok = { pinVerified: true, currentHash: H }, base = { hash: H, name: '  Ana   Pop ', accept: true };
  const code = (input: unknown, c = ok) => { try { checkAcceptance(input, c); return null; } catch (e) { assert.ok(e instanceof AcceptError); return e.code; } };
  assert.deepEqual(checkAcceptance(base, ok), { hash: H, name: 'Ana Pop', contact: null, onBehalf: null, earlyStart: false });
  assert.deepEqual(checkAcceptance({ ...base, earlyStart: true, contact: ' ana@example.com ', onBehalf: 'SC Casa SRL' }, ok), { hash: H, name: 'Ana Pop', contact: 'ana@example.com', onBehalf: 'SC Casa SRL', earlyStart: true });
  assert.equal(checkAcceptance({ ...base, earlyStart: false, contact: '+40 722 111 222' }, ok).contact, '+40 722 111 222');
  assert.equal(code(base, { pinVerified: false, currentHash: H }), 'pin'); assert.equal(acceptErrorStatus('pin'), 401);
  for (const bad of [null, [], 'x', 5]) assert.equal(code(bad), 'input');
  for (const name of ['', 'A', ' a ', '12345', 'x'.repeat(121), 7, undefined]) assert.equal(code({ ...base, name }), 'name', String(name));
  assert.equal(checkAcceptance({ ...base, name: 'x'.repeat(120) }, ok).name.length, 120); assert.equal(checkAcceptance({ ...base, name: 'Li' }, ok).name, 'Li');
  assert.equal(checkAcceptance({ ...base, name: 'Ana​‮Pop' }, ok).name, 'Ana Pop', 'fără caractere invizibile');
  for (const accept of [false, undefined, 'true', 1]) assert.equal(code({ ...base, accept }), 'accept', String(accept));
  for (const earlyStart of ['true', 1, null]) assert.equal(code({ ...base, earlyStart }), 'earlyStart');
  for (const contact of ['nu e contact', 'a@b', 'x'.repeat(161), 12]) assert.equal(code({ ...base, contact }), 'contact', String(contact));
  assert.equal(code({ ...base, onBehalf: 'x'.repeat(161) }), 'onBehalf');
  assert.equal(code({ ...base, hash: 'b'.repeat(64) }), 'stale'); assert.equal(acceptErrorStatus('stale'), 409); assert.equal(acceptErrorStatus('name'), 400);
  assert.equal(code({ ...base, hash: 'xyz' }), 'input'); assert.equal(code({ name: 'Ana Pop', accept: true }), 'input');
  try { checkAcceptance({ ...base, hash: 'b'.repeat(64) }, ok); } catch (e: any){ assert.equal(e.message, STALE_MESSAGE); }
});

test('starea ofertei față de acceptări și diferența dintre versiunea acceptată și cea curentă', () => {
  const acc = (hash: string, at: string, withdrawnAt: string | null = null): Acceptance => ({ id: hash + at, hash, revisionNumber: 1, name: 'Ana', contact: null, onBehalf: null, earlyStart: false, acceptedAt: at, withdrawnAt });
  assert.deepEqual(acceptanceState('h2', []), { state: 'none', acceptance: null });
  const a1 = acc('h1', '2026-11-01T10:00:00.000Z'), a2 = acc('h2', '2026-11-05T10:00:00.000Z');
  assert.equal(acceptanceState('h2', [a1, a2]).state, 'accepted'); assert.equal(acceptanceState('h2', [a1, a2]).acceptance, a2);
  assert.deepEqual(acceptanceState('h3', [a1, a2]), { state: 'changed', acceptance: a2 }, 'cea mai nouă acceptare în vigoare');
  assert.equal(acceptanceState('h1', [a1, a2]).acceptance, a1, 'oferta revenită la o versiune deja acceptată');
  const w = acc('h2', '2026-11-05T10:00:00.000Z', '2026-11-08T12:00:00.000Z');
  assert.deepEqual(acceptanceState('h2', [w]), { state: 'withdrawn', acceptance: w }); assert.deepEqual(acceptanceState('h3', [a1, w]), { state: 'changed', acceptance: a1 });

  const s = buildOfferSnapshot(ctx());
  assert.deepEqual(offerDiff(s, buildOfferSnapshot(ctx())), []); assert.deepEqual(offerDiff(s, JSON.parse(JSON.stringify(shuffle(s)))), []);
  const d1 = offerDiff(s, buildOfferSnapshot(ctx({ data: { ...ctx().data, fee: { kind: 'fixed', amount: 7000 } } })));
  assert.deepEqual(d1.map(c => c.part), ['price', 'schedule']); assert.match(d1[0]!.before!, /7\.260/); assert.match(d1[0]!.after!, /8\.470/);
  assert.deepEqual(offerDiff(s, buildOfferSnapshot(ctx({ data: { ...ctx().data, installments: 3 } }))).map(c => c.part), ['schedule']);
  assert.deepEqual(offerDiff(s, buildOfferSnapshot(ctx({ data: { ...ctx().data, revisions: 5 } }))), [{ part: 'section', title: 'Runde de modificări incluse' }]);
  assert.deepEqual(offerDiff(s, buildOfferSnapshot(ctx({ meta: { startDate: '2026-12-01', dueDate: '2027-03-01' } }))).map(c => c.part), ['terms']);
  const d2 = offerDiff(s, buildOfferSnapshot(ctx({ client: { name: 'Ion Pop' } }))).map(c => c.part);
  assert.ok(d2.includes('client') && d2.includes('texts') && d2.includes('section'), d2.join());
});

test('confirmarea: datele acceptării, amprenta, tot conținutul înghețat, textele și bifele; ora României și UTC', () => {
  const snapshot = buildOfferSnapshot(ctx()), hash = offerHash(snapshot, sha256);
  const r: AcceptanceRecord = { id: '11111111-1111-4111-8111-111111111111', hash, revisionNumber: 3, name: 'Ana Pop', contact: 'ana@example.com', onBehalf: 'SC Casa SRL', earlyStart: false, acceptedAt: '2026-11-22T21:30:05.123Z', withdrawnAt: null, snapshot };
  assert.equal(roDateTime(r.acceptedAt), '22.11.2026, 23:30:05'); assert.equal(roDay('2026-11-22T22:30:00Z'), '2026-11-23'); assert.equal(withdrawalDeadline(r.acceptedAt), '2026-12-06');
  const d = confirmationDoc(r), fact = (k: string) => d.facts.find(f => f[0] === k)?.[1];
  assert.equal(d.title, 'Confirmarea acceptării ofertei'); assert.equal(d.nature, snapshot.texts.nature);
  assert.equal(fact('Acceptată de'), 'Ana Pop'); assert.equal(fact('În numele'), 'SC Casa SRL'); assert.equal(fact('Contact indicat'), 'ana@example.com');
  assert.equal(fact('Data și ora acceptării'), '22.11.2026, 23:30:05 (ora României) · 2026-11-22T21:30:05.123Z (UTC)');
  assert.equal(fact('Amprenta SHA-256 a conținutului acceptat'), hash); assert.equal(fact('Începere înainte de 14 zile'), 'necerută'); assert.match(fact('Preț total de plată')!, /^7\.260\sRON \(include TVA 21%: 1\.260\sRON\)$/);
  assert.match(fact('Termen de retragere (consumatori)')!, /06\.12\.2026/); assert.equal(fact('Retragere înregistrată'), undefined);
  const text = confirmationText(r);
  for (const must of [hash, 'Ana Pop', 'SC Casa SRL', snapshot.texts.nature, snapshot.texts.disclaimer, snapshot.texts.effect, snapshot.texts.changes, snapshot.texts.retention, snapshot.texts.withdrawal.title, ...snapshot.texts.withdrawal.info, snapshot.texts.withdrawal.scope, snapshot.texts.withdrawal.source, snapshot.texts.withdrawal.consequencesTitle, ...snapshot.texts.withdrawal.consequences,
    snapshot.texts.withdrawal.loss, snapshot.texts.withdrawal.form.title, snapshot.texts.withdrawal.form.note, snapshot.texts.withdrawal.form.footnote,
    ...snapshot.texts.withdrawal.form.lines, `[x] ${snapshot.texts.acceptLabel}`, `[ ] ${snapshot.texts.earlyStartLabel}`, `„${snapshot.texts.buttonLabel}”`, 'Studio Linia', 'Avans, la semnare', 'Living', 'RO49 AAAA 1B31 0075 9384 0000', canonicalJson(snapshot)])
    assert.ok(text.includes(must), must.slice(0, 60));
  // amprenta se poate recalcula din ultimul rând al fișierului
  assert.equal(sha256(text.trimEnd().split('\r\n').pop()!), hash);
  const early = confirmationText({ ...r, earlyStart: true, withdrawnAt: '2026-11-30T12:00:00.000Z' });
  assert.ok(early.includes(`[x] ${snapshot.texts.earlyStartLabel}`)); assert.ok(early.includes('Începere înainte de 14 zile: cerută expres de client')); assert.ok(early.includes('Retragere înregistrată: 30.11.2026'));
  assert.equal(legalTexts({}, {}).withdrawal.form.lines[0], 'Către Prestator:');
});

test('„De urmărit azi”: oferta acceptată (de știut, 14 zile) și oferta schimbată după acceptare (curând)', () => {
  const F = (offer: any) => projectAttention({ projectId: 'p', projectName: 'P', offer }, '2026-11-22');
  assert.deepEqual(F(null), []);
  const acc = F({ state: 'accepted', acceptedAt: '2026-11-20T10:00:00.000Z', name: 'Ana Pop' });
  assert.equal(acc.length, 1); assert.equal(acc[0]!.severity, 'info'); assert.equal(acc[0]!.msg, 'offer.accepted'); assert.equal(acc[0]!.kind, 'offer'); assert.deepEqual(acc[0]!.vars, { name: 'Ana Pop' }); assert.equal(acc[0]!.due, '2026-11-20');
  assert.equal(F({ state: 'accepted', acceptedAt: '2026-11-08T10:00:00.000Z', name: 'Ana' }).length, 1); assert.equal(ATTENTION.offerAcceptedDays, 14);
  assert.deepEqual(F({ state: 'accepted', acceptedAt: '2026-11-07T10:00:00.000Z', name: 'Ana' }), [], 'după 14 zile nu mai apare');
  const ch = F({ state: 'changed', acceptedAt: '2026-09-01T10:00:00.000Z', name: 'Ana' });
  assert.equal(ch[0]!.severity, 'soon'); assert.equal(ch[0]!.msg, 'offer.changed'); assert.match(ch[0]!.href, /stage=survey$/);
});

// ---------- server ----------
async function setup(owner = A, o: { fee?: boolean; pin?: boolean; enable?: boolean } = {}){
  const id = await repo.createProject(owner, 'Ofertă', 'demo'), { number } = await repo.createRevision(owner, id, 'Predat clientului'), { token } = await createShare(owner, id, number);
  await saveProfile(owner, { name: 'Studio Linia', email: 'office@linia.ro', address: 'Str. Lungă 1, Cluj' });
  await repo.saveMeta(owner, id, { status: 'design', client: { name: 'Ana Pop', email: 'ana@example.com' } });
  if (o.fee !== false) await saveContract(owner, id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 6000 } });
  let cookie = '';
  if (o.pin !== false){ const { pin } = await setSharePin(owner, id, token); cookie = (await verifySharePin(token, pin)).cookie; }
  if (o.pin !== false && o.enable !== false) await setOfferAccept(owner, id, token, true);
  return { id, token, number, cookie, pins: cookie ? [cookie] : [] };
}
const st = async (p: Promise<unknown>) => { try { await p; return 0; } catch (e: any){ assert.ok(e instanceof repo.HttpError, String(e?.stack ?? e)); return e.status; } };
const input = (hash: string, over: Record<string, unknown> = {}) => ({ hash, name: 'Ana Pop', accept: true, earlyStart: false, ...over });
let ipN = 0; const ip = () => `198.51.100.${(++ipN % 250) + 1}`;
const rq = (token: string, method: 'GET' | 'POST', body?: unknown, cookie = '', headers: Record<string, string> = {}, sub = '') => new Request(`http://localhost/api/share/${token}/offer${sub}`, { method,
  headers: { ...(method === 'POST' ? { 'content-type': 'application/json' } : {}), 'cf-connecting-ip': ip(), ...(cookie ? { cookie: `${PIN_COOKIE}=${cookie}` } : {}), ...headers }, body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body) });
const P = (token: string) => ({ params: Promise.resolve({ token }) });

test('activarea cere PIN pe link; fără PIN → refuz; dezactivarea PIN-ului oprește acceptarea; alt proprietar → 404', async () => {
  const s = await setup(A, { pin: false });
  let adm = await acceptanceAdmin(A, s.id);
  const link = (x: typeof adm, token = s.token) => { const l = x.links.find(k => k.token === token)!; return { revisionNumber: l.revisionNumber, pinEnabled: l.pinEnabled, enabled: l.enabled, accepted: l.accepted }; };
  assert.equal(adm.links.length, 1); assert.deepEqual(link(adm), { revisionNumber: s.number, pinEnabled: false, enabled: false, accepted: 0 }); assert.equal(adm.state, 'none'); assert.deepEqual(adm.acceptances, []);
  assert.equal(adm.offer!.revisionNumber, s.number); assert.deepEqual(adm.offer!.acceptable, { ok: true });
  const err: any = await setOfferAccept(A, s.id, s.token, true).catch(e => e);
  assert.equal(err.status, 409); assert.match(err.message, /PIN/); assert.deepEqual(err.details, { needsPin: true });
  assert.equal(await st(offerForShare(s.token, [])), 404, 'neactivată: clientul nu află nimic');
  assert.equal(await st(setOfferAccept(A, s.id, s.token, 'da')), 400);
  const { pin } = await setSharePin(A, s.id, s.token), { cookie } = await verifySharePin(s.token, pin);
  assert.equal(await st(offerForShare(s.token, [cookie])), 404, 'PIN setat, dar acceptarea încă neactivată (opt-in explicit)');
  adm = await setOfferAccept(A, s.id, s.token, true);
  assert.deepEqual(link(adm), { revisionNumber: s.number, pinEnabled: true, enabled: true, accepted: 0 });
  assert.equal((await listShares(A, s.id))[0]!.offerAccept, true);
  assert.equal((await offerForShare(s.token, [cookie])).state, 'none');
  assert.equal(await st(offerForShare(s.token, [])), 401, 'fără sesiunea PIN'); assert.equal(await st(offerForShare(s.token, ['1.x'])), 401);
  // comutatorul e pe fiecare link: un link nou nu moștenește acceptarea, iar cel vechi rămâne pornit și se vede în panou
  const newer = await createShare(A, s.id, s.number);
  adm = await acceptanceAdmin(A, s.id);
  assert.deepEqual(adm.links.map(l => [l.token, l.enabled]), [[newer.token, false], [s.token, true]], 'cele mai noi întâi, fiecare cu starea lui');
  assert.equal(await st(offerForShare(newer.token, [cookie])), 404); assert.equal((await offerForShare(s.token, [cookie])).state, 'none', 'linkul vechi rămâne pornit');
  assert.equal(await st(setOfferAccept(A, s.id, newer.token, true)), 409, 'fiecare link își cere PIN-ul');
  const np = await setSharePin(A, s.id, newer.token); adm = await setOfferAccept(A, s.id, newer.token, true);
  assert.deepEqual(adm.links.map(l => l.enabled), [true, true]); assert.equal((await offerForShare(newer.token, [(await verifySharePin(newer.token, np.pin)).cookie])).state, 'none');
  // oprirea pe linkul vechi îl oprește pe el, nu pe celălalt
  adm = await setOfferAccept(A, s.id, s.token, false);
  assert.deepEqual(adm.links.map(l => [l.token, l.enabled]), [[newer.token, true], [s.token, false]]); assert.equal(await st(offerForShare(s.token, [cookie])), 404);
  await setOfferAccept(A, s.id, s.token, true);
  // PIN dezactivat → acceptarea se oprește pe acel link și nu repornește singură la un PIN nou
  await clearSharePin(A, s.id, s.token);
  assert.equal(await st(offerForShare(s.token, [cookie])), 404); assert.equal(link(await acceptanceAdmin(A, s.id)).enabled, false);
  const again = await setSharePin(A, s.id, s.token); assert.equal(await st(offerForShare(s.token, [(await verifySharePin(s.token, again.pin)).cookie])), 404);
  // un link revocat iese din panou și nu se mai poate porni
  await revokeShare(A, s.id, newer.token); adm = await acceptanceAdmin(A, s.id);
  assert.deepEqual(adm.links.map(l => l.token), [s.token]); assert.equal(await st(setOfferAccept(A, s.id, newer.token, true)), 404);
  // alt proprietar nu vede și nu schimbă nimic; linkul altui proiect nu se poate porni de aici
  for (const p of [acceptanceAdmin(B, s.id), setOfferAccept(B, s.id, s.token, true), listAcceptances(B, s.id), getAcceptance(B, s.id, '11111111-1111-4111-8111-111111111111')]) assert.equal(await st(p), 404);
  const bare = await repo.createProject(A, 'Fără link', 'demo'), ba = await acceptanceAdmin(A, bare);
  assert.deepEqual(ba.links, []); assert.equal(ba.offer, null); assert.equal(ba.state, 'none');
  for (const tok of [s.token, 'x', null]) assert.equal(await st(setOfferAccept(A, bare, tok, true)), 404);
});

test('oferta de acceptat vine din REVIZIA linkului: ciorna nepublicată nu ajunge la client și nu schimbă oferta acceptată', async () => {
  const s = await setup(), v1 = await offerForShare(s.token, s.pins), rooms1 = v1.offer.snapshot.offer.sections.find(x => x.id === 'scope')!.table!.rows.map(r => r[0]);
  const a = (await acceptOffer(s.token, s.pins, input(v1.offer.hash))).acceptance;
  // designerul redenumește lucrarea și o cameră în ciornă, fără să publice
  const p = await repo.getProject(A, s.id), draft = structuredClone(p.draft); draft.name = 'Nume nou în ciornă'; draft.floor.rooms[0]!.name = 'Cameră redenumită în ciornă';
  await repo.saveDraft(A, s.id, draft);
  const v2 = await offerForShare(s.token, s.pins);
  assert.equal(v2.offer.hash, v1.offer.hash, 'ciorna nu schimbă oferta de pe link'); assert.equal(v2.state, 'accepted');
  assert.doesNotMatch(JSON.stringify(v2.offer.snapshot), /în ciornă/); assert.equal(v2.offer.snapshot.project.name, 'Ofertă');
  assert.equal((await acceptanceAdmin(A, s.id)).state, 'accepted', 'la designer: tot acceptată');
  assert.ok(!(await getAttention(A, { fresh: true })).items.some(i => i.projectId === s.id && i.msg === 'offer.changed'));
  // publică revizia nouă pe un link nou: cât acceptarea nu e pornită pe el, starea rămâne cea a linkului acceptat
  const { number: n2 } = await repo.createRevision(A, s.id, 'v2'), l2 = await createShare(A, s.id, n2);
  assert.equal((await acceptanceAdmin(A, s.id)).state, 'accepted'); assert.equal((await acceptanceAdmin(A, s.id)).offer!.revisionNumber, s.number);
  // pornită pe linkul reviziei noi: oferta ei diferă (alt nume, altă cameră) → „schimbată”, și la designer, și pe linkul nou
  const pin2 = await setSharePin(A, s.id, l2.token), c2 = [(await verifySharePin(l2.token, pin2.pin)).cookie]; const adm = await setOfferAccept(A, s.id, l2.token, true);
  assert.equal(adm.offer!.revisionNumber, n2); assert.equal(adm.state, 'changed'); assert.equal(adm.acceptance!.id, a.id); assert.ok(adm.changes.some(c => c.part === 'project'));
  const w2 = await offerForShare(l2.token, c2);
  assert.equal(w2.offer.snapshot.project.name, 'Nume nou în ciornă'); assert.notEqual(w2.offer.hash, v1.offer.hash); assert.notDeepEqual(w2.offer.snapshot.offer.sections.find(x => x.id === 'scope')!.table!.rows.map(r => r[0]), rooms1);
  // acceptarea făcută pe alt link: starea se vede, dar fără nume, conținut sau confirmare
  assert.equal(w2.state, 'changed'); assert.equal(w2.acceptance, null); assert.deepEqual(w2.elsewhere, { acceptedAt: a.acceptedAt, withdrawnAt: null }); assert.deepEqual(w2.changes, []); assert.deepEqual(w2.acceptedBy, []);
  assert.doesNotMatch(JSON.stringify({ ...w2, offer: null }), /Ana Pop/);
  assert.equal(await st(confirmationForShare(l2.token, c2, a.id)), 404, 'PIN-ul altui link nu descarcă acceptarea'); assert.equal(await st(confirmationForShare(l2.token, c2)), 404);
  assert.match((await confirmationForShare(s.token, s.pins, a.id)).text, /Ana Pop/, 'de pe linkul ei se descarcă');
  // linkul vechi arată în continuare revizia lui, acceptată
  assert.equal((await offerForShare(s.token, s.pins)).state, 'accepted');
  assert.deepEqual((await getAttention(A, { fresh: true })).items.filter(i => i.projectId === s.id && i.kind === 'offer').map(i => i.msg), ['offer.changed']);
  // datele comerciale se iau live pe orice revizie: schimbarea onorariului schimbă oferta și pe linkul vechi
  await saveContract(A, s.id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 6100 } });
  const v3 = await offerForShare(s.token, s.pins); assert.equal(v3.state, 'changed'); assert.equal(v3.acceptance!.id, a.id); assert.deepEqual(v3.changes.map(c => c.part), ['price', 'schedule']);
});

test('acceptarea înregistrează exact instantaneul și amprenta serverului; a doua acceptare pe aceeași versiune e idempotentă; fără IP stocat', async () => {
  const s = await setup(), view = await offerForShare(s.token, s.pins);
  assert.equal(view.state, 'none'); assert.equal(view.acceptance, null); assert.deepEqual(view.offer.acceptable, { ok: true });
  assert.equal(view.offer.hash, sha256(canonicalJson(view.offer.snapshot))); assert.equal(view.offer.snapshot.price.gross, 6000); assert.equal(view.offer.snapshot.client.name, 'Ana Pop');
  assert.equal(await st(acceptOffer(s.token, [], input(view.offer.hash))), 401, 'acceptarea cere sesiunea PIN');
  assert.equal(await st(acceptOffer(s.token, s.pins, input(view.offer.hash, { accept: false }))), 400);
  assert.equal(await st(acceptOffer(s.token, s.pins, input(view.offer.hash, { name: 'A' }))), 400);
  const before = Date.now();
  // conținutul trimis de client nu contează: se păstrează instantaneul construit de server
  const r = await acceptOffer(s.token, s.pins, { ...input(view.offer.hash, { name: '  Ana   Pop ', contact: 'ana@example.com', onBehalf: 'Familia Pop', earlyStart: true }), snapshot: { strecurat: true }, ip: '1.2.3.4' });
  assert.equal(r.created, true); const a = r.acceptance;
  assert.equal(a.name, 'Ana Pop'); assert.equal(a.contact, 'ana@example.com'); assert.equal(a.onBehalf, 'Familia Pop'); assert.equal(a.earlyStart, true); assert.equal(a.withdrawnAt, null); assert.equal(a.revisionNumber, s.number);
  assert.equal(a.hash, view.offer.hash); assert.equal(canonicalJson(a.snapshot), canonicalJson(view.offer.snapshot)); assert.equal(sha256(canonicalJson(a.snapshot)), a.hash, 'instantaneul citit din bază dă aceeași amprentă');
  assert.ok(Math.abs(new Date(a.acceptedAt).getTime() - before) < 60_000, 'momentul serverului');
  // rândul din bază: fără IP și fără agent de browser (nici coloane, nici în instantaneu)
  const { q } = await getDb(), { rows } = await q('select * from offer_acceptances where project_id=$1', [s.id]);
  assert.equal(rows.length, 1); assert.deepEqual(Object.keys(rows[0]).sort(), ['accepted_at', 'accepted_contact', 'accepted_for', 'accepted_name', 'early_start', 'hash', 'id', 'name_key', 'project_id', 'revision_number', 'share_token', 'snapshot', 'withdrawn_at']);
  assert.equal(rows[0].name_key, 'ana pop'); assert.doesNotMatch(JSON.stringify(rows[0]), /1\.2\.3\.4|198\.51\.100|user-agent|Mozilla|"ip"|strecurat/i);
  // idempotent pentru ACEEAȘI persoană (numele normalizat: majuscule, diacritice și spații nu contează), chiar și simultan
  const r2 = await acceptOffer(s.token, s.pins, input(view.offer.hash, { name: ' ANA  pop', contact: 'alt@example.com', earlyStart: false }));
  assert.equal(r2.created, false); assert.equal(r2.acceptance.id, a.id); assert.equal(r2.acceptance.name, 'Ana Pop'); assert.equal(r2.acceptance.contact, 'ana@example.com'); assert.equal(r2.acceptance.earlyStart, true); assert.equal(r2.acceptance.acceptedAt, a.acceptedAt);
  const both = await Promise.all([acceptOffer(s.token, s.pins, input(view.offer.hash)), acceptOffer(s.token, s.pins, input(view.offer.hash))]);
  assert.ok(both.every(x => !x.created && x.acceptance.id === a.id)); assert.equal((await q('select count(*)::int as n from offer_acceptances where project_id=$1', [s.id])).rows[0].n, 1);
  assert.equal(personKey('  Ștefan   ȚĂRANU '), 'stefan taranu'); assert.equal(personKey('Ana Pop'), personKey('ana  POP'));
  // altă persoană (coproprietarul) acceptă aceeași versiune: acceptare distinctă, cu confirmarea ei
  const r3 = await acceptOffer(s.token, s.pins, input(view.offer.hash, { name: 'Ion Pop' }));
  assert.equal(r3.created, true); assert.notEqual(r3.acceptance.id, a.id); assert.equal(r3.acceptance.name, 'Ion Pop'); assert.equal(r3.acceptance.hash, a.hash);
  assert.equal((await acceptOffer(s.token, s.pins, input(view.offer.hash, { name: 'ion pop' }))).acceptance.id, r3.acceptance.id);
  const two = await Promise.all([acceptOffer(s.token, s.pins, input(view.offer.hash, { name: 'Maria Pop' })), acceptOffer(s.token, s.pins, input(view.offer.hash, { name: 'maria pop' }))]);
  assert.equal(two.filter(x => x.created).length, 1, 'două cereri simultane ale aceleiași persoane: o singură acceptare'); assert.equal(two[0]!.acceptance.id, two[1]!.acceptance.id);
  const after = await offerForShare(s.token, s.pins);
  assert.equal(after.state, 'accepted'); assert.equal(after.elsewhere, null); assert.deepEqual(after.changes, []);
  assert.deepEqual(after.acceptedBy.map(x => x.name).sort(), ['Ana Pop', 'Ion Pop', 'Maria Pop']); assert.equal((after.acceptedBy[0] as any).snapshot, undefined);
  assert.match((await confirmationForShare(s.token, s.pins, r3.acceptance.id)).text, /Acceptată de: Ion Pop/);
  // retragerea unei persoane nu o scoate pe cealaltă
  await withdrawAcceptance(A, s.id, r3.acceptance.id, roDay(new Date())); await withdrawAcceptance(A, s.id, two[0]!.acceptance.id, roDay(new Date()));
  const one = await offerForShare(s.token, s.pins); assert.equal(one.state, 'accepted'); assert.equal(one.acceptance!.id, a.id); assert.deepEqual(one.acceptedBy.map(x => x.name), ['Ana Pop']);
  const adm = await acceptanceAdmin(A, s.id);
  assert.equal(adm.state, 'accepted'); assert.equal(adm.acceptance!.name, 'Ana Pop'); assert.equal(adm.acceptances.length, 3); assert.equal(adm.links[0]!.accepted, 1); assert.equal((adm.acceptances[0] as any).snapshot, undefined); assert.equal((adm.acceptance as any).snapshot, undefined);
  assert.doesNotMatch(JSON.stringify(adm.acceptances), new RegExp(s.token));
  assert.equal(canonicalJson((await getAcceptance(A, s.id, a.id)).snapshot), canonicalJson(a.snapshot));
  assert.equal(await st(getAcceptance(A, s.id, 'nu-e-id')), 404);
});

test('oferta schimbată între afișare și clic → 409; după acceptare: „schimbată”, acceptarea veche rămâne, noua versiune cere acceptare nouă', async () => {
  const s = await setup(), v1 = await offerForShare(s.token, s.pins);
  await saveContract(A, s.id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 6500 } });
  const stale: any = await acceptOffer(s.token, s.pins, input(v1.offer.hash)).catch(e => e);
  assert.equal(stale.status, 409); assert.equal(stale.message, STALE_MESSAGE);
  assert.equal((await listAcceptances(A, s.id)).length, 0, 'nimic înregistrat');
  const v2 = await offerForShare(s.token, s.pins); assert.notEqual(v2.offer.hash, v1.offer.hash);
  const a = (await acceptOffer(s.token, s.pins, input(v2.offer.hash))).acceptance;
  // designerul schimbă oferta după acceptare
  await saveContract(A, s.id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 7000 }, installments: 3 });
  const v3 = await offerForShare(s.token, s.pins);
  assert.equal(v3.state, 'changed'); assert.equal(v3.acceptance!.id, a.id); assert.equal(v3.acceptance!.snapshot.price.gross, 6500, 'versiunea acceptată e înghețată');
  assert.deepEqual(v3.changes.map(c => c.part), ['price', 'schedule']);
  const adm = await acceptanceAdmin(A, s.id); assert.equal(adm.state, 'changed'); assert.deepEqual(adm.changes.map(c => c.part), ['price', 'schedule']);
  assert.equal(await st(acceptOffer(s.token, s.pins, input(a.hash))), 409, 'amprenta veche nu mai acceptă versiunea nouă');
  const a3 = await acceptOffer(s.token, s.pins, input(v3.offer.hash, { name: 'Ana Pop' }));
  assert.equal(a3.created, true); assert.notEqual(a3.acceptance.id, a.id);
  const list = await listAcceptances(A, s.id); assert.deepEqual(list.map(x => x.hash), [v3.offer.hash, v2.offer.hash], 'istoricul păstrează ambele, cea nouă întâi');
  assert.equal((await offerForShare(s.token, s.pins)).state, 'accepted');
  // revenirea la versiunea deja acceptată: tot „acceptată”, fără rând nou
  await saveContract(A, s.id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 6500 } });
  const back = await offerForShare(s.token, s.pins); assert.equal(back.state, 'accepted'); assert.equal(back.acceptance!.id, a.id);
});

test('oferta incompletă sau expirată nu se poate accepta (409), dar clientul o vede', async () => {
  const s = await setup(A, { fee: false }), v = await offerForShare(s.token, s.pins);
  assert.deepEqual(v.offer.acceptable, { ok: false, reason: 'no_price' }); assert.equal(await st(acceptOffer(s.token, s.pins, input(v.offer.hash))), 409);
  await saveContract(A, s.id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 100 }, validUntil: '2020-01-01' });
  const e = await offerForShare(s.token, s.pins); assert.deepEqual(e.offer.acceptable, { ok: false, reason: 'expired' });
  const err: any = await acceptOffer(s.token, s.pins, input(e.offer.hash)).catch(x => x); assert.equal(err.status, 409); assert.match(err.message, /valabilitate/);
  // datele de contract stricate nu se explică clientului
  await (await getDb()).q(`update project_contract set data='{"revisions":"multe"}'::jsonb where project_id=$1`, [s.id]);
  const broken: any = await offerForShare(s.token, s.pins).catch(x => x); assert.equal(broken.status, 409); assert.doesNotMatch(broken.message, /validarea|revisions|runde/);
});

test('retragerea: designerul notează data; acceptarea iese din vigoare, rămâne în istoric, iar aceeași versiune se poate accepta din nou', async () => {
  const s = await setup(), v = await offerForShare(s.token, s.pins), a = (await acceptOffer(s.token, s.pins, input(v.offer.hash))).acceptance, today = roDay(new Date());
  for (const bad of ['2020-01-01', '2999-01-01', 'ieri', '2026-13-45', null, 5]) assert.equal(await st(withdrawAcceptance(A, s.id, a.id, bad)), 400, String(bad));
  assert.equal(await st(withdrawAcceptance(B, s.id, a.id, today)), 404);
  const w = await withdrawAcceptance(A, s.id, a.id, today);
  assert.equal(w.withdrawnAt, `${today}T12:00:00.000Z`); assert.equal(await st(withdrawAcceptance(A, s.id, a.id, today)), 409, 'o retragere notată nu se rescrie');
  const after = await offerForShare(s.token, s.pins); assert.equal(after.state, 'withdrawn'); assert.equal(after.acceptance!.id, a.id);
  assert.equal((await acceptanceAdmin(A, s.id)).state, 'withdrawn');
  const again = await acceptOffer(s.token, s.pins, input(v.offer.hash));
  assert.equal(again.created, true); assert.notEqual(again.acceptance.id, a.id); assert.equal((await listAcceptances(A, s.id)).length, 2);
  assert.match((await confirmationForShare(s.token, s.pins, a.id)).text, /Retragere înregistrată/);
});

test('alt link / alt proiect → 404; link revocat, expirat sau proiect în coș → 404', async () => {
  const s = await setup(), v = await offerForShare(s.token, s.pins), a = (await acceptOffer(s.token, s.pins, input(v.offer.hash))).acceptance;
  // alt link al aceluiași proiect, fără acceptarea activată
  const other = await createShare(A, s.id, s.number);
  assert.equal(await st(offerForShare(other.token, s.pins)), 404); assert.equal(await st(acceptOffer(other.token, s.pins, input(v.offer.hash))), 404); assert.equal(await st(confirmationForShare(other.token, s.pins)), 404);
  // sesiunea PIN a unui link nu deschide alt link
  const s2 = await setup();
  assert.equal(await st(offerForShare(s2.token, s.pins)), 401); assert.equal(await st(acceptOffer(s2.token, s.pins, input(v.offer.hash))), 401);
  // alt proiect: nu vede acceptările primului și nu-i poate descărca confirmarea
  assert.equal((await offerForShare(s2.token, s2.pins)).state, 'none'); assert.equal(await st(confirmationForShare(s2.token, s2.pins, a.id)), 404); assert.equal(await st(confirmationForShare(s2.token, s2.pins)), 404);
  assert.equal(await st(getAcceptance(A, s2.id, a.id)), 404); assert.equal(await st(withdrawAcceptance(A, s2.id, a.id, roDay(new Date()))), 404);
  for (const bad of ['', 'scurt', 'A'.repeat(43), null, 7]) assert.equal(await st(offerForShare(bad, s.pins)), 404);
  // expirat
  await (await getDb()).q(`update shares set expires_at = now() - interval '1 minute' where token=$1`, [s2.token]);
  assert.equal(await st(offerForShare(s2.token, s2.pins)), 404); assert.equal(await st(acceptOffer(s2.token, s2.pins, input('a'.repeat(64)))), 404);
  // revocat: acceptarea rămâne la designer, dar linkul nu mai arată nimic
  await revokeShare(A, s.id, s.token);
  assert.equal(await st(offerForShare(s.token, s.pins)), 404); assert.equal(await st(confirmationForShare(s.token, s.pins, a.id)), 404);
  assert.equal((await getAcceptance(A, s.id, a.id)).name, 'Ana Pop'); assert.equal((await acceptanceAdmin(A, s.id)).acceptances.length, 1);
  // coș
  const s3 = await setup(); await moveToTrash(A, s3.id);
  assert.equal(await st(offerForShare(s3.token, s3.pins)), 404); assert.equal(await st(acceptOffer(s3.token, s3.pins, input('a'.repeat(64)))), 404); assert.equal(await st(acceptanceAdmin(A, s3.id)), 404);
  // chiar dacă linkul ar rămâne nerevocat, proiectul din coș nu se mai oferă
  await (await getDb()).q('update shares set revoked_at=null where token=$1', [s3.token]); assert.equal(await st(offerForShare(s3.token, s3.pins)), 404);
});

test('rutele publice: 404 / 401 / 200, 201 apoi 200 idempotent, 409, 415 fără JSON, corp prea mare, fără cache; confirmarea se descarcă doar cu PIN', async () => {
  const s = await setup();
  for (const p of [`/api/share/${s.token}/offer`, `/api/share/${s.token}/offer/confirmation`]) assert.ok(isPublicPath(p), p);
  assert.ok(!isPublicPath(`/api/share/${s.token}/offer/altceva`)); assert.ok(!isPublicPath(`/api/projects/${s.id}/acceptance`));
  // CSRF, ca restul scrierilor: o pagină de pe altă origine e oprită în proxy
  const cross = proxy(new NextRequest(`http://localhost:3123/api/share/${s.token}/offer`, { method: 'POST', headers: { host: 'localhost:3123', origin: 'https://evil.example', 'sec-fetch-site': 'cross-site', 'content-type': 'application/json' } }));
  assert.equal(cross.status, 403);
  assert.equal((await offerGET(rq('A'.repeat(43), 'GET'), P('A'.repeat(43)))).status, 404); assert.equal((await offerGET(rq('x', 'GET'), P('x'))).status, 404);
  const no = await offerGET(rq(s.token, 'GET'), P(s.token)); assert.equal(no.status, 401); assert.equal(no.headers.get('cache-control'), 'no-store'); assert.equal(no.headers.get('x-robots-tag'), 'noindex');
  const res = await offerGET(rq(s.token, 'GET', undefined, s.cookie), P(s.token)), v = await res.json();
  assert.equal(res.status, 200); assert.equal(res.headers.get('cache-control'), 'no-store'); assert.equal(v.state, 'none'); assert.ok(isOfferHash(v.offer.hash));
  assert.equal((await offerPOST(rq(s.token, 'POST', input(v.offer.hash)), P(s.token))).status, 401);
  assert.equal((await offerPOST(rq(s.token, 'POST', input(v.offer.hash), s.cookie, { 'content-type': 'text/plain' }), P(s.token))).status, 415);
  assert.equal((await offerPOST(rq(s.token, 'POST', '{nu e json', s.cookie), P(s.token))).status, 400);
  assert.equal((await offerPOST(rq(s.token, 'POST', input(v.offer.hash, { name: 'x'.repeat(5000) }), s.cookie), P(s.token))).status, 413);
  assert.equal((await offerPOST(rq(s.token, 'POST', input(v.offer.hash, { accept: false }), s.cookie), P(s.token))).status, 400);
  const stale = await offerPOST(rq(s.token, 'POST', input('c'.repeat(64)), s.cookie), P(s.token)); assert.equal(stale.status, 409); assert.equal((await stale.json()).error, STALE_MESSAGE);
  assert.equal((await confirmGET(rq(s.token, 'GET', undefined, s.cookie, {}, '/confirmation'), P(s.token))).status, 404, 'încă nu există nicio acceptare');
  const ok = await offerPOST(rq(s.token, 'POST', input(v.offer.hash), s.cookie, { 'user-agent': 'Mozilla/5.0 Test' }), P(s.token)), j = await ok.json();
  assert.equal(ok.status, 201); assert.equal(j.created, true); assert.equal(j.acceptance.hash, v.offer.hash); assert.equal(ok.headers.get('cache-control'), 'no-store');
  const twice = await offerPOST(rq(s.token, 'POST', input(v.offer.hash), s.cookie), P(s.token)); assert.equal(twice.status, 200); assert.equal((await twice.json()).acceptance.id, j.acceptance.id);
  assert.doesNotMatch(JSON.stringify((await (await getDb()).q('select * from offer_acceptances where id=$1', [j.acceptance.id])).rows), /Mozilla|198\.51\.100/);
  // confirmarea
  assert.equal((await confirmGET(rq(s.token, 'GET', undefined, '', {}, '/confirmation'), P(s.token))).status, 401);
  const c = await confirmGET(rq(s.token, 'GET', undefined, s.cookie, {}, '/confirmation'), P(s.token)), text = await c.text();
  assert.equal(c.status, 200); assert.match(c.headers.get('content-type')!, /^text\/plain; charset=utf-8/); assert.match(c.headers.get('content-disposition')!, /^attachment; filename="confirmare-acceptare-oferta-\d{4}-\d\d-\d\d-[0-9a-f]{8}\.txt"$/);
  assert.equal(c.headers.get('cache-control'), 'no-store');
  for (const must of [v.offer.hash, 'Ana Pop', 'Confirmare online a acceptării ofertei (nu este semnătură electronică calificată).', 'Accept oferta — cu obligație de plată', 'Am citit și accept oferta și graficul de plăți.', 'Model de formular de retragere', 'Consecințele retragerii', 'art. 16 lit. a', 'Studio Linia', 'Avans, la semnare'])
    assert.ok(text.includes(must), must);
  assert.equal(sha256(text.trimEnd().split('\r\n').pop()!), v.offer.hash);
  assert.equal((await confirmGET(rq(s.token, 'GET', undefined, s.cookie, {}, `/confirmation?id=${j.acceptance.id}`), P(s.token))).status, 200);
  assert.equal((await confirmGET(rq(s.token, 'GET', undefined, s.cookie, {}, '/confirmation?id=zzz'), P(s.token))).status, 404);
});

test('limite pe link și adresă: acceptarea 10 / 10 minute, citirea 60; 429 cu Retry-After', async () => {
  const s = await setup(), send = (addr: string) => offerPOST(rq(s.token, 'POST', input('d'.repeat(64)), s.cookie, { 'cf-connecting-ip': addr }), P(s.token));
  for (let i = 0; i < 10; i++) assert.equal((await send('203.0.113.9')).status, 409, `#${i + 1}`);
  const lim = await send('203.0.113.9'); assert.equal(lim.status, 429); assert.ok(Number(lim.headers.get('retry-after')) > 0); assert.match((await lim.json()).error, /^Prea multe trimiteri de pe acest link/);
  assert.equal((await send('203.0.113.10')).status, 409, 'altă adresă are limita ei');
  const view = (addr: string) => offerGET(rq(s.token, 'GET', undefined, '', { 'cf-connecting-ip': addr }), P(s.token));
  for (let i = 0; i < 60; i++) assert.equal((await view('203.0.113.11')).status, 401); assert.equal((await view('203.0.113.11')).status, 429);
  // cel mult 50 de acceptări pe proiect
  const { q } = await getDb(), v = await offerForShare(s.token, s.pins);
  for (let i = 0; i < ACCEPT_LIMITS.perProject; i++) await q(`insert into offer_acceptances(project_id, share_token, revision_number, snapshot, hash, accepted_name) values($1,$2,1,'{}'::jsonb,$3,'x')`, [s.id, s.token, sha256(String(i))]);
  assert.equal(await st(acceptOffer(s.token, s.pins, input(v.offer.hash))), 429);
});

test('„De urmărit azi” pe server și exportul proiectului: acceptările intră, fără cheia linkului', async () => {
  const s = await setup(), v = await offerForShare(s.token, s.pins);
  const none = await getAttention(A, { fresh: true }); assert.ok(!none.items.some(i => i.projectId === s.id && i.kind === 'offer'));
  const a = (await acceptOffer(s.token, s.pins, input(v.offer.hash))).acceptance;
  const acc = (await getAttention(A, { fresh: true })).items.filter(i => i.projectId === s.id && i.kind === 'offer');
  assert.deepEqual(acc.map(i => [i.msg, i.severity, i.vars.name]), [['offer.accepted', 'info', 'Ana Pop']]);
  await saveContract(A, s.id, { ...DEFAULT_CONTRACT, fee: { kind: 'fixed', amount: 9000 } });
  const ch = (await getAttention(A, { fresh: true })).items.filter(i => i.projectId === s.id && i.kind === 'offer');
  assert.deepEqual(ch.map(i => [i.msg, i.severity]), [['offer.changed', 'soon']]);
  assert.ok(!(await getAttention(B, { fresh: true })).items.some(i => i.projectId === s.id), 'alt proprietar nu vede');
  await withdrawAcceptance(A, s.id, a.id, roDay(new Date()));
  assert.ok(!(await getAttention(A, { fresh: true })).items.some(i => i.projectId === s.id && i.kind === 'offer'), 'retrasă: nimic de urmărit');
  // exportul: acceptările sunt ale designerului (dovada contractului), deci intră; cheia linkului nu
  const { data } = await exportProject(A, s.id), raw = data.toString('latin1');
  assert.ok(raw.includes('acceptari-oferta.json')); assert.ok(raw.includes(`acceptari-oferta/confirmare-acceptare-oferta-${roDay(a.acceptedAt)}-${a.hash.slice(0, 8)}.txt`)); assert.ok(raw.includes('CITESTE-MA.txt'));
  const recs = await listAcceptanceRecords(A, s.id); assert.equal(recs.length, 1); assert.equal(recs[0]!.snapshot.price.gross, 6000); assert.ok(!JSON.stringify(recs).includes(s.token), 'fără cheia linkului');
});
