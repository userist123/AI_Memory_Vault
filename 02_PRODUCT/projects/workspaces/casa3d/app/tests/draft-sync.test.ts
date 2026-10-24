// Salvarea ciornei din editor (lib/draft-sync.ts): rețeaua căzută nu aruncă, sesiunea expirată se recunoaște, orice salvare
// trimite versiunea de bază (draft_rev) — inclusiv cea dinaintea unei revizii — iar un al doilea dispozitiv primește 409.
import { test, beforeAll, vi } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const OWNER = '66666666-aaaa-4aaa-8aaa-666666666666';
vi.mock('@/lib/owner', () => ({ ownerId: async () => OWNER }));
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { PUT } from '../app/api/projects/[id]/route';
import { putDraft, draftBody, retryDelay, shouldRetry, stashDraft, readStash, clearStash, stashBase, stashKey, reloginUrl } from '../lib/draft-sync';
import { safeNext } from '../lib/public-gate';
beforeAll(() => resetDbForTests());

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

test('rețeaua căzută devine „offline” (nicio excepție neprinsă) și se reîncearcă cu pauze crescânde', async () => {
  const o = await putDraft(async () => { throw new TypeError('Failed to fetch'); }, 'x', { name: 'a' }, 3);
  assert.deepEqual(o, { kind: 'offline' }); assert.equal(shouldRetry(o), true);
  assert.deepEqual([0, 1, 2, 3, 10].map(retryDelay), [2000, 4000, 8000, 16000, 60000]);
  assert.equal(shouldRetry({ kind: 'error', message: null, retry: true }), true, '5xx');
  assert.equal(shouldRetry({ kind: 'error', message: 'invalid', retry: false }), false, '4xx nu se repetă');
  assert.equal(shouldRetry({ kind: 'conflict' }), false); assert.equal(shouldRetry({ kind: 'auth' }), false);
});

test('401 = sesiune expirată, 409 cu conflict = conflict, 5xx = eroare de reîncercat; răspunsul fără JSON nu aruncă', async () => {
  assert.deepEqual(await putDraft(async () => json(401, { error: 'Autentifică-te.' }), 'x', {}, 1), { kind: 'auth' });
  assert.deepEqual(await putDraft(async () => json(409, { error: 'x', details: { conflict: true } }), 'x', {}, 1), { kind: 'conflict' });
  assert.deepEqual(await putDraft(async () => new Response('<html>bad gateway', { status: 502 }), 'x', {}, 1), { kind: 'error', message: null, retry: true });
  assert.deepEqual(await putDraft(async () => json(400, { error: 'Desen invalid.' }), 'x', {}, 1), { kind: 'error', message: 'Desen invalid.', retry: false });
  assert.deepEqual(await putDraft(async () => json(200, { draftRev: 8 }), 'x', {}, 7), { kind: 'saved', draftRev: 8 });
});

test('corpul cererii poartă versiunea de bază; doar „păstrează ce am aici” (force) o omite', () => {
  assert.deepEqual(JSON.parse(draftBody({ n: 1 }, 4)), { snapshot: { n: 1 }, base: 4 });
  assert.deepEqual(JSON.parse(draftBody({ n: 1 }, 0)), { snapshot: { n: 1 }, base: 0 }, 'versiunea 0 e tot o versiune');
  assert.deepEqual(JSON.parse(draftBody({ n: 1 }, 4, true)), { snapshot: { n: 1 } });
});

test('două dispozitive prin ruta reală: salvarea dinaintea reviziei (flush) de pe dispozitivul rămas în urmă primește 409, nu suprascrie', async () => {
  const id = await repo.createProject(OWNER, 'Casa', 'demo'), p = await repo.getProject(OWNER, id), v0 = p.draftRev;
  const route: typeof fetch = async (url, init) => PUT(new Request(`http://localhost${url}`, init as RequestInit), { params: Promise.resolve({ id }) });
  const b = await putDraft(route, id, { ...p.draft, name: 'Pe dispozitivul B' }, v0); assert.equal(b.kind, 'saved');
  // dispozitivul A a pornit tot de la v0: flush-ul dinaintea „Salvează” trece prin aceeași cale și primește conflictul
  assert.deepEqual(await putDraft(route, id, { ...p.draft, name: 'Pe dispozitivul A' }, v0), { kind: 'conflict' });
  assert.equal((await repo.getProject(OWNER, id)).name, 'Pe dispozitivul B', 'munca lui B a rămas');
  // revizia se creează din ciorna serverului (a lui B), nu din a lui A
  const rev = await repo.createRevision(OWNER, id, 'r'); assert.equal(rev.number, 1); assert.equal((await repo.getProject(OWNER, id)).name, 'Pe dispozitivul B');
});

test('editorul nu mai are nicio salvare PUT în afara căii comune (flush, revizie și link trec prin putDraft cu base)', () => {
  const src = readFileSync(path.resolve(import.meta.dirname, '../components/Editor.tsx'), 'utf8');
  assert.equal((src.match(/method:\s*'PUT'/g) ?? []).length, 0, 'un PUT direct ar ocoli versiunea de bază');
  assert.match(src, /putDraft\(fetch, id, sent, baseRev\.current, force\)/);
  assert.match(src, /const flushDraft = async \(\): Promise<boolean>/);
  assert.match(src, /if \(!\(await flushDraft\(\)\)\) return;/, 'revizia nu se creează dacă ciorna nu s-a salvat');
  assert.match(src, /addEventListener\('online'/); assert.match(src, /addEventListener\('beforeunload'/);
});

test('ciorna păstrată local la sesiune expirată: pe proiect, citită înapoi, niciodată fără versiune de bază', () => {
  const m = new Map<string, string>(), store = { getItem: (k: string) => m.get(k) ?? null, setItem: (k: string, v: string) => void m.set(k, v), removeItem: (k: string) => void m.delete(k) };
  assert.equal(stashDraft(store, 'p1', { name: 'nesalvat' }, 5, 1000), true);
  assert.equal(readStash(store, 'p2'), null, 'alt proiect');
  assert.deepEqual(readStash(store, 'p1'), { snapshot: { name: 'nesalvat' }, base: 5, at: 1000 });
  assert.equal(stashBase(readStash(store, 'p1')!, 9), 5, 'se trimite cu versiunea de la care pornise: dacă serverul s-a mișcat, 409');
  assert.equal(stashBase({ snapshot: {}, base: null, at: 0 }, null), 0);
  m.set(stashKey('p3'), '{nu e json'); assert.equal(readStash(store, 'p3'), null);
  clearStash(store, 'p1'); assert.equal(readStash(store, 'p1'), null);
  const full = { getItem: () => null, setItem: () => { throw new Error('QuotaExceeded'); }, removeItem: () => {} };
  assert.equal(stashDraft(full, 'p1', {}, 1), false, 'stocarea plină nu aruncă');
  assert.equal(safeNext(new URLSearchParams(reloginUrl('/p/abc').split('?')[1]).get('next')), '/p/abc', 'după login se întoarce în proiect');
});
