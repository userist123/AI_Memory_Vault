// „Preia cursul BNR”: ruta e doar a proprietarului proiectului; cererea către BNR e fixă, fără date personale, cu limite;
// răspunsul se ține în memorie; un eșec dă un mesaj clar (502), iar introducerea de mână rămâne. Fără rețea: fetch e simulat.
import { test, beforeAll, beforeEach, afterAll, vi } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const A = 'aaaaaaaa-b0b0-4b0b-8b0b-aaaaaaaaaaaa', B = 'bbbbbbbb-b0b0-4b0b-8b0b-bbbbbbbbbbbb';
const who = vi.hoisted(() => ({ id: '' }));
vi.mock('@/lib/owner', () => ({ ownerId: async () => who.id }));
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { GET } from '../app/api/projects/[id]/fx/bnr/route';
import { bnrRates, resetBnrCache, BNR_URLS, BNR_LIMITS } from '../lib/fx-bnr';

const XML = `<?xml version="1.0" encoding="utf-8"?><DataSet xmlns="https://www.bnr.ro/xsd"><Body><OrigCurrency>RON</OrigCurrency><Cube date="2026-11-20"><Rate currency="EUR">5.3482</Rate><Rate currency="HUF" multiplier="100">1.4341</Rate><Rate currency="USD">4.8782</Rate></Cube></Body></DataSet>`;
const xmlRes = (body = XML, status = 200, headers: Record<string, string> = {}) => new Response(body, { status, headers: { 'content-type': 'text/xml', ...headers } });
let calls: { url: string; init: RequestInit }[] = [], reply: (url: string) => Promise<Response> | Response = () => xmlRes();
const realFetch = globalThis.fetch;
beforeAll(async () => { await resetDbForTests(); vi.stubGlobal('fetch', async (url: any, init: any) => { calls.push({ url: String(url), init: init ?? {} }); return reply(String(url)); }); });
beforeEach(() => { calls = []; reply = () => xmlRes(); resetBnrCache(); });
afterAll(() => { vi.stubGlobal('fetch', realFetch); });
const call = async (id: string) => { const res = await GET(new Request('http://localhost/x'), { params: Promise.resolve({ id }) }); return { status: res.status, json: await res.json() as any }; };

test('ruta BNR: doar proprietarul; cerere fixă, fără date personale; răspuns ținut în memorie', async () => {
  who.id = A; const id = await repo.createProject(A, 'Curs BNR', 'demo');
  // alt proprietar, proiect inexistent sau id greșit: 404, fără nicio cerere spre BNR
  who.id = B; assert.equal((await call(id)).status, 404);
  who.id = A; assert.equal((await call('00000000-0000-4000-8000-000000000000')).status, 404);
  assert.equal(calls.length, 0, 'nimic nu pleacă spre BNR pentru cine nu are proiectul');
  const r = await call(id);
  assert.equal(r.status, 200); assert.equal(r.json.date, '2026-11-20'); assert.equal(r.json.rates.EUR, 5.3482); assert.equal(r.json.rates.HUF, 0.014341); assert.match(r.json.fetchedAt, /^\d{4}-\d{2}-\d{2}T/);
  assert.equal(calls.length, 1); const c = calls[0]!, h = new Headers(c.init.headers);
  assert.equal(c.url, BNR_URLS[0]); assert.equal(c.url, 'https://curs.bnr.ro/nbrfxrates.xml'); assert.equal(c.init.method, 'GET'); assert.equal(c.init.redirect, 'manual'); assert.equal(c.init.body, undefined);
  assert.equal(c.init.credentials, 'omit'); assert.ok(c.init.signal instanceof AbortSignal, 'cererea are termen-limită');
  assert.equal(h.get('user-agent'), 'Mozilla/5.0 (compatible; fx-rates/1.0)'); assert.deepEqual([...h.keys()].sort(), ['accept', 'user-agent'], 'fără cookie, referer sau alte antete');
  assert.ok(!c.url.includes(id) && !JSON.stringify([...h]).includes(id) && !JSON.stringify([...h]).includes(A), 'nimic despre proiect sau proprietar în cerere');
  // a doua cerere în aceeași oră: din memorie
  assert.equal((await call(id)).json.rates.USD, 4.8782); assert.equal(calls.length, 1);
  // după o oră se citește din nou
  const t0 = Date.now(); await bnrRates(undefined, () => t0 + BNR_LIMITS.cacheMs + 1); assert.equal(calls.length, 2);
});

test('eșecul BNR: mesaj clar (502), a doua adresă încercată, pauză de un minut, limită de mărime', async () => {
  who.id = A; const id = await repo.createProject(A, 'Curs BNR eșec', 'demo');
  // redirecționarea spre prima pagină (adresa veche) nu e urmată; se încearcă adresa de rezervă, apoi 502
  reply = () => new Response('', { status: 302, headers: { location: 'https://www.bnr.ro/' } });
  let r = await call(id); assert.equal(r.status, 502); assert.match(r.json.error, /Cursul BNR nu a putut fi citit/); assert.match(r.json.error, /de mână/);
  assert.deepEqual(calls.map(c => c.url), [...BNR_URLS]);
  // imediat după eșec nu se mai încearcă (butonul apăsat repetat nu bombardează BNR)
  r = await call(id); assert.equal(r.status, 502); assert.equal(calls.length, 2);
  // prima adresă cade, a doua răspunde
  resetBnrCache(); calls = []; reply = url => url === BNR_URLS[0] ? Promise.reject(new Error('rețea')) : xmlRes();
  r = await call(id); assert.equal(r.status, 200); assert.equal(r.json.rates.EUR, 5.3482); assert.equal(calls.length, 2);
  // răspunsuri care nu sunt fișierul BNR, prea mari sau cu alt cod: 502, fără detalii interne
  for (const bad of [() => xmlRes('<html>mentenanță</html>'), () => xmlRes(XML, 500), () => xmlRes(XML + ' '.repeat(BNR_LIMITS.bytes)), () => xmlRes(XML, 200, { 'content-length': String(BNR_LIMITS.bytes + 1) }), () => { throw new Error('ECONNRESET 10.0.0.5'); }]){
    resetBnrCache(); reply = bad as any; r = await call(id); assert.equal(r.status, 502); assert.ok(!/ECONNRESET|10\.0\.0\.5|html/.test(JSON.stringify(r.json)));
  }
  // termenul-limită: o cerere care nu răspunde e oprită prin semnal
  resetBnrCache(); vi.useFakeTimers();
  try { reply = () => new Promise<Response>(() => {}); calls = [];
    const stuck = (url: string, init: RequestInit) => new Promise<Response>((_, rej) => { calls.push({ url, init }); init.signal!.addEventListener('abort', () => rej(new Error('abort'))); });
    const pending = bnrRates(stuck).catch(e => e); await vi.advanceTimersByTimeAsync(BNR_LIMITS.timeoutMs * 2 + 10);
    const e = await pending; assert.equal(e.status, 502); assert.equal(calls.length, 2); assert.ok(calls.every(c => (c.init.signal as AbortSignal).aborted));
  } finally { vi.useRealTimers(); }
});
