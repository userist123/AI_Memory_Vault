// Pinii de comentariu pe linkul clientului, protejați cu PIN: probele P1–P11 din
// 00_GOVERNANCE/coordination/projects/CASA3D/SHARE_PINS_DESIGN.md, la nivel de lib și de rută.
import { test, beforeAll, beforeEach } from 'vitest';
import assert from 'node:assert/strict';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
process.env.CASA3D_AUTH_FILE = path.join(import.meta.dirname, '__no_auth_file__.json');
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { HttpError } from '../lib/repo';
import { createShare, revokeShare, listShares } from '../lib/share';
import { addFeedback, addPinnedFeedback, parseAnchor, listFeedback, listFeedbackForShare, FEEDBACK_LIMITS } from '../lib/feedback';
import { setSharePin, clearSharePin, verifySharePin, verifyPinSession, makePinSession, hasPinSession, pinCookies, PinLockedError,
  PIN_COOKIE, PIN_LOCK_MS, PIN_SESSION_MS, PIN_MAX_FAILURES } from '../lib/share-pin';
import { isPublicPath, gate } from '../lib/public-gate';
import { resetShareLimits } from '../lib/rate-limit';
import { POST as pinPOST } from '../app/api/share/[token]/pin/route';
import { POST as pinsPOST } from '../app/api/share/[token]/pins/route';
import { POST as feedbackPOST, GET as feedbackGET } from '../app/api/share/[token]/feedback/route';
beforeAll(() => resetDbForTests());
beforeEach(() => resetShareLimits());
const A = '77777777-7777-4777-8777-777777777777', B = '88888888-8888-4888-8888-888888888888', T0 = Date.UTC(2020, 0, 1, 12);

async function shared(){
  const id = await repo.createProject(A, 'Pini', 'demo'), { number } = await repo.createRevision(A, id, 'Predat clientului');
  const { token } = await createShare(A, id, number), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!.id;
  return { id, token, room };
}
const wrong = (pin: string) => pin === '000000' ? '000001' : '000000';
const status = async (p: Promise<unknown>) => { try { await p; return 0; } catch (e) { assert.ok(e instanceof HttpError, String(e)); return e.status; } };
const errOf = async (p: Promise<unknown>) => { try { await p; return null; } catch (e) { return e as HttpError; } };
let ipN = 0; const ip = () => `198.51.100.${(++ipN % 250) + 1}`;
const req = (url: string, body: unknown, headers: Record<string, string> = {}) => new Request(`http://localhost${url}`, { method: 'POST',
  headers: { 'content-type': 'application/json', 'cf-connecting-ip': ip(), ...headers }, body: typeof body === 'string' ? body : JSON.stringify(body) });
const ctx = (token: string) => ({ params: Promise.resolve({ token }) });
const cookieOf = (res: Response) => { const c = res.headers.get('set-cookie')!; return c.split(';')[0]!; };
const pin_ = { verdict: 'change', author: 'Ana', text: 'Aici aș vrea o priză.' };

test('P1: 5 eșecuri consecutive blochează 15 minute; în blocare 429 fără verificarea PIN-ului; după blocare PIN-ul corect merge', async () => {
  const { id, token } = await shared(), { pin } = await setSharePin(A, id, token);
  assert.match(pin, /^\d{6}$/);
  for (let i = 1; i < PIN_MAX_FAILURES; i++) assert.equal(await status(verifySharePin(token, wrong(pin), T0 + i)), 401, `eșec ${i}`);
  const fifth = await errOf(verifySharePin(token, wrong(pin), T0 + 10));
  assert.ok(fifth instanceof PinLockedError); assert.equal(fifth.status, 429); assert.equal(fifth.retryAfterMs, PIN_LOCK_MS);
  // în blocare: nici PIN-ul corect nu trece (nu se verifică), nici formatul greșit nu ajunge la 400
  assert.equal(await status(verifySharePin(token, pin, T0 + 60e3)), 429);
  assert.equal(await status(verifySharePin(token, 'abc', T0 + 60e3)), 429);
  const { rows: [s] } = await (await getDb()).q('select pin_failures, pin_locked_until from shares where token=$1', [token]);
  assert.equal(new Date(s.pin_locked_until).getTime(), T0 + 10 + PIN_LOCK_MS, 'blocarea e în DB (persistentă)');
  // după expirare, PIN-ul corect deschide sesiunea și șterge eșecurile
  const ok = await verifySharePin(token, pin, T0 + 10 + PIN_LOCK_MS + 1); assert.ok(ok.cookie);
  const { rows: [s2] } = await (await getDb()).q('select pin_failures, pin_locked_until from shares where token=$1', [token]);
  assert.equal(s2.pin_failures, 0); assert.equal(s2.pin_locked_until, null);
});

test('P1: o reușită resetează numărătoarea (eșecurile trebuie să fie consecutive)', async () => {
  const { id, token } = await shared(), { pin } = await setSharePin(A, id, token);
  for (let i = 0; i < PIN_MAX_FAILURES - 1; i++) await status(verifySharePin(token, wrong(pin), T0));
  await verifySharePin(token, pin, T0);
  for (let i = 0; i < PIN_MAX_FAILURES - 1; i++) assert.equal(await status(verifySharePin(token, wrong(pin), T0)), 401);
});

test('P1: limita pe IP a rutei /pin (10 în 10 minute) → 429 cu Retry-After', async () => {
  const { token } = await shared(), headers = { 'content-type': 'application/json', 'cf-connecting-ip': '203.0.113.50' };
  const send = () => pinPOST(new Request(`http://localhost/api/share/${token}/pin`, { method: 'POST', headers, body: '{"pin":"12"}' }), ctx(token));
  for (let i = 0; i < 10; i++) assert.notEqual((await send()).status, 429, `#${i + 1}`);
  const r = await send(); assert.equal(r.status, 429); assert.ok(Number(r.headers.get('retry-after')) > 0);
});

test('P2: formatul se verifică înainte de scrypt (400, nu se numără ca eșec)', async () => {
  const { id, token } = await shared(), { pin } = await setSharePin(A, id, token);
  for (const bad of ['', '12345', '1234567', '12345a', ' 123456', 123456, null, undefined, ['123456'], { pin }]) assert.equal(await status(verifySharePin(token, bad, T0)), 400, String(bad));
  const { rows: [s] } = await (await getDb()).q('select pin_failures from shares where token=$1', [token]); assert.equal(s.pin_failures, 0);
});

test('P3: sesiunea e semnată per link, expiră și moare la PIN nou sau dezactivare', async () => {
  const { id, token, room } = await shared(), { pin } = await setSharePin(A, id, token);
  const { cookie } = await verifySharePin(token, pin, T0);
  assert.equal(await hasPinSession(token, [cookie], T0 + 1), true);
  assert.equal(await hasPinSession(token, [cookie], T0 + PIN_SESSION_MS), false, 'expirat');
  const [exp, sig] = cookie.split('.');
  for (const bad of [`${Number(exp) + 1000}.${sig}`, `${exp}.${sig!.slice(0, -1)}${sig!.endsWith('A') ? 'B' : 'A'}`, `${exp}.`, 'x', '', `${exp}.${sig}.x`]) assert.equal(await hasPinSession(token, [bad], T0 + 1), false, bad);
  assert.equal(verifyPinSession('alta-cheie', token, cookie, T0 + 1), false);
  assert.equal(verifyPinSession(null, token, cookie, T0 + 1), false);
  assert.equal(verifyPinSession('k', token, makePinSession('k', token, T0 + 10 * PIN_SESSION_MS), T0), false, 'exp prea departe în viitor');
  // același cookie nu merge pe alt link
  const other = await shared(); await setSharePin(A, other.id, other.token);
  assert.equal(await hasPinSession(other.token, [cookie], T0 + 1), false);
  // PIN nou → cheie nouă → cookie-ul vechi moare
  const { pin: pin2 } = await setSharePin(A, id, token); assert.equal(await hasPinSession(token, [cookie], T0 + 1), false);
  assert.equal(await status(verifySharePin(token, pin === pin2 ? wrong(pin) : pin, T0)), 401, 'PIN-ul vechi nu mai merge');
  const c2 = (await verifySharePin(token, pin2, T0)).cookie; assert.equal(await hasPinSession(token, [c2], T0 + 1), true);
  // dezactivare → 403 la /pin, cookie-ul moare
  await clearSharePin(A, id, token); assert.equal(await hasPinSession(token, [c2], T0 + 1), false);
  assert.equal(await status(verifySharePin(token, pin2, T0)), 403);
  assert.equal((await listShares(A, id)).find(s => s.token === token)!.pinEnabled, false);
  // proprietarul e verificat
  assert.equal(await status(setSharePin(B, id, token)), 404); assert.equal(await status(clearSharePin(B, id, token)), 404);
  assert.equal(await status(setSharePin(A, id, 'nu-e-token')), 404);
  void room;
});

test('P3/P9: ruta /pin pune cookie HttpOnly, SameSite=Strict, pe calea linkului, 12h; Secure doar pe https', async () => {
  const { id, token, room } = await shared(), { pin } = await setSharePin(A, id, token);
  const res = await pinPOST(req(`/api/share/${token}/pin`, { pin }), ctx(token)); assert.equal(res.status, 200);
  const c = res.headers.get('set-cookie')!;
  assert.match(c, new RegExp(`^${PIN_COOKIE}=\\d{13}\\.[\\w-]{43}; Path=/api/share/${token}; Max-Age=43200; HttpOnly; SameSite=Strict$`));
  assert.equal(res.headers.get('cache-control'), 'no-store');
  const sec = await pinPOST(req(`/api/share/${token}/pin`, { pin }, { 'x-forwarded-proto': 'https' }), ctx(token)); assert.match(sec.headers.get('set-cookie')!, /; Secure$/);
  // cookie-ul deschide /pins
  const ok = await pinsPOST(req(`/api/share/${token}/pins`, { ...pin_, roomId: room, anchor: { surface: 'room', u: 0.5, v: 0.25 } }, { cookie: `other=1; ${cookieOf(res)}` }), ctx(token));
  assert.equal(ok.status, 201); const j = await ok.json(); assert.deepEqual(j.anchor, { surface: 'room', u: 0.5, v: 0.25 });
  assert.equal(ok.headers.get('cache-control'), 'no-store');
  assert.deepEqual(pinCookies(`a=1; ${PIN_COOKIE}=x.y;b=2`), ['x.y']); assert.deepEqual(pinCookies(null), []);
});

test('P4: token necunoscut sau revocat → 404 pe /pin și /pins; PIN neconfigurat → 403', async () => {
  const { id, token, room } = await shared(), unknown = 'Z'.repeat(43);
  for (const t of [unknown, 'scurt']){
    assert.equal((await pinPOST(req(`/api/share/${t}/pin`, { pin: '123456' }), ctx(t))).status, 404, t);
    assert.equal((await pinsPOST(req(`/api/share/${t}/pins`, { ...pin_, roomId: null, anchor: { surface: 'overview', u: 0, v: 0 } }), ctx(t))).status, 404, t);
  }
  assert.equal(await status(verifySharePin(unknown, '123456')), 404);
  const r403 = await pinPOST(req(`/api/share/${token}/pin`, { pin: '123456' }), ctx(token)); assert.equal(r403.status, 403);
  const { pin } = await setSharePin(A, id, token), res = await pinPOST(req(`/api/share/${token}/pin`, { pin }), ctx(token)), cookie = cookieOf(res);
  await revokeShare(A, id, token);
  assert.equal((await pinPOST(req(`/api/share/${token}/pin`, { pin }), ctx(token))).status, 404);
  assert.equal((await pinsPOST(req(`/api/share/${token}/pins`, { ...pin_, roomId: room, anchor: { surface: 'room', u: 0.1, v: 0.1 } }, { cookie }), ctx(token))).status, 404);
  assert.equal(await status(setSharePin(A, id, token)), 404, 'pe un link revocat nu se mai pune PIN');
});

test('P3: /pins fără cookie, cu cookie falsificat sau expirat → 401', async () => {
  const { id, token, room } = await shared(), { pin } = await setSharePin(A, id, token);
  const body = { ...pin_, roomId: room, anchor: { surface: 'room', u: 0.5, v: 0.5 } };
  assert.equal((await pinsPOST(req(`/api/share/${token}/pins`, body), ctx(token))).status, 401);
  const { cookie } = await verifySharePin(token, pin, T0);
  const [exp, sig] = cookie.split('.');
  assert.equal((await pinsPOST(req(`/api/share/${token}/pins`, body, { cookie: `${PIN_COOKIE}=${exp}.${'A'.repeat(43)}` }), ctx(token))).status, 401);
  assert.equal((await pinsPOST(req(`/api/share/${token}/pins`, body, { cookie: `${PIN_COOKIE}=${Number(exp) + 1}.${sig}` }), ctx(token))).status, 401);
  assert.equal((await pinsPOST(req(`/api/share/${token}/pins`, body, { cookie: `${PIN_COOKIE}=${cookie}` }), ctx(token))).status, 401, 'creat la T0 (2020), expirat acum');
  assert.equal((await listFeedbackForShare(token)).length, 0);
});

test('P5: /feedback cu câmpul anchor → 400 (nu se ocolește PIN-ul)', async () => {
  const { token, room } = await shared();
  for (const anchor of [{ surface: 'room', u: 0.5, v: 0.5 }, null, undefined])
    assert.equal(await status(addFeedback(token, { ...pin_, roomId: room, anchor })), 400);
  const r = await feedbackPOST(req(`/api/share/${token}/feedback`, { ...pin_, roomId: room, anchor: { surface: 'room', u: 0.5, v: 0.5 } }), ctx(token));
  assert.equal(r.status, 400); assert.equal((await listFeedbackForShare(token)).length, 0);
});

test('P7: validarea ancorei', async () => {
  const rooms = ['r1', 'r2'], bad = (a: unknown, roomId: unknown = 'r1') => assert.throws(() => parseAnchor(a, roomId, rooms), (e: any) => e instanceof HttpError && e.status === 400, JSON.stringify(a) + ' ' + String(roomId));
  for (const v of [NaN, Infinity, -Infinity, -0.0001, 1.0001, '0.5', null, undefined, true, [0.5]]) { bad({ surface: 'room', u: v, v: 0.5 }); bad({ surface: 'room', u: 0.5, v }); }
  for (const s of ['wall', 'ROOM', '', null, undefined, 1, ['room']]) bad({ surface: s, u: 0.5, v: 0.5 });
  bad({ surface: 'room', u: 0.5, v: 0.5 }, null); bad({ surface: 'room', u: 0.5, v: 0.5 }, 'nu-exista'); bad({ surface: 'room', u: 0.5, v: 0.5 }, 42);
  bad({ surface: 'overview', u: 0.5, v: 0.5 }, 'r1');
  for (const a of [null, undefined, 'room', 5, [], [{ surface: 'room', u: 0, v: 0 }]]) bad(a);
  assert.deepEqual(parseAnchor({ surface: 'room', u: 0.123456, v: 1, extra: '<script>', __proto__: { x: 1 } }, 'r2', rooms), { surface: 'room', u: 0.1235, v: 1 });
  const ov = parseAnchor({ surface: 'overview', u: 0, v: 0.99999 }, null, rooms); assert.deepEqual(ov, { surface: 'overview', u: 0, v: 1 }); assert.deepEqual(Object.keys(ov), ['surface', 'u', 'v']);
  // prin lib: camera trebuie să fie chiar în revizie, cheile în plus nu se salvează
  const { token, room } = await shared();
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, roomId: 'nu-exista', anchor: { surface: 'room', u: 0.5, v: 0.5 } })), 400);
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, roomId: room, anchor: { surface: 'overview', u: 0.5, v: 0.5 } })), 400);
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, roomId: room })), 400, 'fără ancoră');
  const f = await addPinnedFeedback(token, { ...pin_, roomId: null, item: { key: 'x' }, anchor: { surface: 'overview', u: 0.3, v: 0.7, z: 9 } });
  assert.deepEqual(f.anchor, { surface: 'overview', u: 0.3, v: 0.7 }); assert.equal(f.item, null, 'pinul nu e o aprobare pe produs');
});

test('P6/P8: aceeași validare ca la părere (clean, nume, text), plafonul perShare și corp ≤ 4 KB', async () => {
  const { id, token, room } = await shared(), a = { surface: 'room', u: 0.5, v: 0.5 };
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, verdict: 'meh', roomId: room, anchor: a })), 400);
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, author: '', roomId: room, anchor: a })), 400);
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, text: '  ', roomId: room, anchor: a })), 400);
  const f = await addPinnedFeedback(token, { verdict: 'question', author: '  An‮a  ', text: '<img src=x onerror=alert(1)>', roomId: room, anchor: a });
  assert.equal(f.author, 'Ana'); assert.equal(f.text, '<img src=x onerror=alert(1)>', 'textul rămâne text (React îl escapează)');
  // corp peste 4 KB → 413
  const { pin } = await setSharePin(A, id, token), cookie = cookieOf(await pinPOST(req(`/api/share/${token}/pin`, { pin }), ctx(token)));
  const big = await pinsPOST(req(`/api/share/${token}/pins`, { ...pin_, roomId: room, anchor: a, text: 'x'.repeat(5000) }, { cookie }), ctx(token)); assert.equal(big.status, 413);
  // plafonul pe link e comun cu părerile simple
  const { q } = await getDb();
  await q(`insert into share_feedback(token, project_id, room_id, verdict, author, body) select $1, $2, null, 'question', 'Bot', 'x' from generate_series(1, $3::int)`, [token, id, FEEDBACK_LIMITS.perShare - 1]);
  assert.equal(await status(addPinnedFeedback(token, { ...pin_, roomId: room, anchor: a })), 429);
});

test('P9: /pin și /pins cer content-type application/json', async () => {
  const { id, token, room } = await shared(), { pin } = await setSharePin(A, id, token);
  for (const ct of ['text/plain', 'application/x-www-form-urlencoded', 'multipart/form-data; boundary=x', '']){
    const r1 = await pinPOST(req(`/api/share/${token}/pin`, JSON.stringify({ pin }), { 'content-type': ct }), ctx(token));
    assert.ok([400, 415].includes(r1.status), `${ct} → ${r1.status}`); assert.equal(r1.headers.get('set-cookie'), null);
    const r2 = await pinsPOST(req(`/api/share/${token}/pins`, JSON.stringify({ ...pin_, roomId: room, anchor: { surface: 'room', u: 0, v: 0 } }), { 'content-type': ct }), ctx(token));
    assert.ok([400, 415].includes(r2.status), `${ct} → ${r2.status}`);
  }
});

test('P10: PIN-ul nu apare în niciun mesaj de eroare', async () => {
  const { id, token } = await shared(), { pin } = await setSharePin(A, id, token), msgs: string[] = [];
  const grab = async (p: Promise<unknown>) => { const e = await errOf(p); if (e) msgs.push(e.message, JSON.stringify(e.details ?? null)); };
  for (let i = 0; i < PIN_MAX_FAILURES + 1; i++) await grab(verifySharePin(token, wrong(pin), T0));
  await grab(verifySharePin(token, pin, T0)); await grab(verifySharePin(token, `${pin}x`, T0 + PIN_LOCK_MS * 2));
  for (const body of [{ pin: wrong(pin) }, { pin }, { pin: `${pin}9` }]) { const r = await pinPOST(req(`/api/share/${token}/pin`, body), ctx(token)); msgs.push(await r.text()); }
  assert.ok(msgs.length > 8);
  for (const m of msgs) { assert.ok(!m.includes(pin), m); assert.ok(!m.includes(wrong(pin)), m); }
});

test('P11: poarta publică permite exact /pin și /pins pe linkul clientului; rutele proprietarului rămân închise', () => {
  const tok = 'A'.repeat(43);
  for (const p of [`/api/share/${tok}/pin`, `/api/share/${tok}/pins`]) { assert.equal(isPublicPath(p), true, p); assert.equal(gate({ local: false, path: p, method: 'POST', session: false }), 'allow'); }
  for (const p of [`/api/share/${tok}/pinx`, `/api/share/${tok}/pins/x`, `/api/share/${tok}/pin/`, `/api/share/${tok}/pinss`, `/api/share/${tok}/pins/../pin`, '/api/projects/x/shares/pin', `/api/projects/x/shares/pin?token=${tok}`, `/api/share/short/pin`])
    assert.equal(isPublicPath(p), false, p);
  assert.equal(gate({ local: false, path: '/api/projects/x/shares/pin', method: 'POST', session: false }), 'unauthorized');
});

test('designerul vede ancora și tokenul; clientul vede ancora pe /feedback', async () => {
  const { id, token, room } = await shared(), { pin } = await setSharePin(A, id, token);
  assert.equal((await listShares(A, id)).find(s => s.token === token)!.pinEnabled, true);
  const cookie = cookieOf(await pinPOST(req(`/api/share/${token}/pin`, { pin }), ctx(token)));
  const made = await (await pinsPOST(req(`/api/share/${token}/pins`, { ...pin_, roomId: room, anchor: { surface: 'room', u: 0.2, v: 0.8 } }, { cookie }), ctx(token))).json();
  await addFeedback(token, { verdict: 'like', author: 'Ana', text: '', roomId: null });
  const own = await listFeedback(A, id), p = own.find(f => f.id === made.id)!;
  assert.deepEqual(p.anchor, { surface: 'room', u: 0.2, v: 0.8 }); assert.equal(p.token, token); assert.equal(own.find(f => f.id !== made.id)!.anchor, null);
  const pub = await (await feedbackGET(new Request(`http://localhost/api/share/${token}/feedback`), ctx(token))).json();
  assert.deepEqual(pub.find((f: any) => f.id === made.id).anchor, { surface: 'room', u: 0.2, v: 0.8 });
  assert.equal(pub.some((f: any) => 'token' in f), false, 'clientul nu primește tokenul în rânduri');
});

test('ruta proprietarului pentru PIN: refuză cereri din altă origine sau fără JSON, fără cache', async () => {
  const { POST: ownerPOST, DELETE: ownerDELETE } = await import('../app/api/projects/[id]/shares/pin/route');
  const octx = { params: Promise.resolve({ id: '00000000-0000-4000-8000-000000000000' }) };
  const mk = (method: string, headers: Record<string, string>) => new Request('http://localhost/api/projects/x/shares/pin', { method, headers: { host: 'localhost', ...headers }, body: '{"token":"x"}' });
  for (const fn of [ownerPOST, ownerDELETE]) for (const h of <Record<string, string>[]>[
    { 'content-type': 'application/json', 'sec-fetch-site': 'cross-site' },
    { 'content-type': 'application/json', origin: 'https://evil.example' },
    { 'content-type': 'text/plain' },
  ]){ const r = await fn(mk(fn === ownerPOST ? 'POST' : 'DELETE', h), octx); assert.equal(r.status, 403); assert.equal(r.headers.get('cache-control'), 'no-store'); }
});
