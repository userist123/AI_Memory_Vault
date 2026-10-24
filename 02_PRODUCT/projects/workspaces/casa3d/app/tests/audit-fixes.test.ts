// SECURITY_AUDIT.md (security-lockdown, 2026-10-22): câte un test pentru fiecare constatare reparată; fiecare pică pe
// codul de dinainte de reparație.
import { test, beforeAll, afterAll } from 'vitest';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { scryptSync, randomBytes } from 'node:crypto';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
process.env.CASA3D_AUTH_FILE = path.join(os.tmpdir(), `casa3d-audit-${process.pid}.json`);
import { NextRequest } from 'next/server';
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { errorSummary } from '../lib/log';
import { body, BODY_MAX, DRAFT_BODY_MAX } from '../lib/http';
import { isCrossSiteWrite, isLocalRequest } from '../lib/public-gate';
import { proxy as middleware } from '../proxy';   // Next.js 16: middleware.ts → proxy.ts
import { clientIp, ipv6Prefix64 } from '../lib/rate-limit';
import { setPassword, readAuth, checkPassword, removePassword, SCRYPT_N, type AuthConfig } from '../lib/auth';
import { assertAdmin } from '../lib/outbound';
import { aiConfigured } from '../lib/ai';
import { createShare } from '../lib/share';
import { setSharePin, verifySharePin } from '../lib/share-pin';
import { addChange, updateChange } from '../lib/change-orders';
import { POST as decideRoute } from '../app/api/share/[token]/changes/[change]/route';
import { GET as shareGet } from '../app/api/share/[token]/route';
beforeAll(() => resetDbForTests());
afterAll(() => removePassword());
const A = 'aaaaaaaa-0000-4000-8000-00000000000a', OWNER = 'bbbbbbbb-0000-4000-8000-00000000000b';
const H = (o: Record<string, string> = {}) => ({ get: (k: string) => o[k.toLowerCase()] ?? null });
const withEnv = async (vars: Record<string, string | undefined>, fn: () => unknown) => {
  const old: Record<string, string | undefined> = {}; for (const k of Object.keys(vars)){ old[k] = process.env[k]; if (vars[k] === undefined) delete process.env[k]; else process.env[k] = vars[k]; }
  try { return await fn(); } finally { for (const [k, v] of Object.entries(old)){ if (v === undefined) delete process.env[k]; else process.env[k] = v; } }
};

test('A-1: jurnalul primește tipul și mesajul erorii, nu interogarea și parametrii (tokenuri, PIN, nume)', () => {
  const e = Object.assign(new Error('duplicate key value violates unique constraint "shares_pkey"'), { code: '23505', query: 'insert into shares(token) values($1)', params: ['TOKEN-SECRET'] });
  const s = JSON.stringify(errorSummary(e));
  assert.ok(!s.includes('TOKEN-SECRET') && !s.includes('insert into')); assert.match(s, /23505/);
});
test('A-2: baza de pe disc care nu se deschide oprește serverul; nimic nu se șterge, fără bază goală în memorie', async () => {
  const blocker = path.join(os.tmpdir(), `casa3d-not-a-dir-${process.pid}`); fs.writeFileSync(blocker, 'datele proprietarului');
  try {
    await withEnv({ PGLITE_MEMORY: undefined, PGLITE_DIR: blocker, VERCEL: undefined }, async () => {
      resetDbForTests(); await assert.rejects(getDb(), /nu poate fi deschisă/); });
    assert.equal(fs.readFileSync(blocker, 'utf8'), 'datele proprietarului', 'fișierul nu a fost șters');
    await withEnv({ DATABASE_URL: 'postgres://casa:x@127.0.0.1:1/casa', PGLITE_MEMORY: undefined }, async () => {
      resetDbForTests(); await assert.rejects(getDb()); });
  } finally { fs.rmSync(blocker, { force: true }); resetDbForTests(); }
  assert.equal((await getDb()).mode, 'pglite-memory', 'după eroare, o nouă încercare reușește (nu rămâne blocată)');
});
test('A-4: tokenul de admin se compară în timp constant și rămâne obligatoriu', async () => {
  await withEnv({ ADMIN_TOKEN: 'x'.repeat(24) }, () => {
    const req = (t?: string) => new Request('http://localhost/api/admin/stats', { headers: t ? { 'x-admin-token': t } : {} });
    assert.throws(() => assertAdmin(req()), (e: any) => e.status === 401); assert.throws(() => assertAdmin(req('x'.repeat(23))), (e: any) => e.status === 401);
    assertAdmin(req('x'.repeat(24))); });
  await withEnv({ ADMIN_TOKEN: 'scurt' }, () => assert.throws(() => assertAdmin(new Request('http://localhost/')), (e: any) => e.status === 404));
});
test('B-1: scrierile în API de pe altă origine primesc 403 (inclusiv formulare text/plain de pe subdomenii-frate)', () => {
  const r = (method: string, h: Record<string, string>) => new NextRequest('http://casa.example.com/api/studio', { method, headers: { host: 'casa.example.com', ...h } });
  for (const h of <Record<string, string>[]>[{ 'sec-fetch-site': 'same-site', 'content-type': 'text/plain' }, { 'sec-fetch-site': 'cross-site' }, { origin: 'https://evil.example.com' }, { origin: 'null' }])
    assert.equal(middleware(r('PUT', h)).status, 403, JSON.stringify(h));
  assert.equal(isCrossSiteWrite('POST', H({ 'sec-fetch-site': 'same-origin', origin: 'http://casa.example.com', host: 'casa.example.com' })), false);
  assert.equal(isCrossSiteWrite('POST', H({ host: 'x' })), false, 'fără antete de browser (curl) nu e CSRF');
  assert.equal(isCrossSiteWrite('GET', H({ 'sec-fetch-site': 'cross-site' })), false, 'citirile nu sunt afectate');
  assert.notEqual(middleware(r('GET', { 'sec-fetch-site': 'cross-site' })).status, 403);
});
test('B-2: în demo AI-ul rămâne oprit chiar cu cheia setată', async () => {
  await withEnv({ ANTHROPIC_API_KEY: 'sk-ant-test', CASA3D_DEMO: '1' }, () => assert.equal(aiConfigured(), false));
  await withEnv({ ANTHROPIC_API_KEY: 'sk-ant-test', CASA3D_DEMO: undefined }, () => assert.equal(aiConfigured(), true));
});
test('B-3: adresele IPv6 din același /64 au aceeași limită; antetul de adresă se poate alege pentru serverul găzduit', async () => {
  assert.equal(ipv6Prefix64('2001:db8:1:2::1'), '2001:db8:1:2::/64'); assert.equal(ipv6Prefix64('2001:0db8:0001:0002:ffff:1:2:3'), '2001:db8:1:2::/64');
  assert.equal(clientIp(H({ 'cf-connecting-ip': '2001:db8:1:2::6' })), clientIp(H({ 'cf-connecting-ip': '2001:db8:1:2:a:b:c:d' })));
  assert.equal(clientIp(H({ 'cf-connecting-ip': '203.0.113.7' })), '203.0.113.7'); assert.equal(ipv6Prefix64('::ffff:10.0.0.1'), null);
  await withEnv({ CASA3D_CLIENT_IP_HEADER: 'x-real-ip' }, () => {
    assert.equal(clientIp(H({ 'x-real-ip': '198.51.100.1', 'cf-connecting-ip': '1.1.1.1' })), '198.51.100.1', 'antetul ales, nu cel falsificabil'); });
});
test('B-5: parolele noi folosesc scrypt N=2^17; cele vechi (N=2^14, fără n) se verifică în continuare', async () => {
  setPassword('parola-noua-lunga', OWNER); const cfg = readAuth()!; assert.equal(cfg.n, SCRYPT_N); assert.equal(SCRYPT_N, 2 ** 17);
  assert.equal(await checkPassword(cfg, 'parola-noua-lunga'), true); assert.equal(await checkPassword(cfg, 'alta'), false);
  const salt = randomBytes(16).toString('base64'), legacy: AuthConfig = { v: 1, salt, owner: OWNER, secret: 's', updatedAt: '', hash: scryptSync('veche-parola-1', Buffer.from(salt, 'base64'), 64, { N: 2 ** 14, r: 8, p: 1 }).toString('base64') };
  assert.equal(await checkPassword(legacy, 'veche-parola-1'), true);
  assert.equal(await checkPassword({ ...legacy, n: 2 ** 30 }, 'veche-parola-1'), true, 'un n necunoscut nu poate cere un cost uriaș: se folosește N-ul vechi');
});
test('B-4: cu PIN activat pe link, aprobarea unei modificări cere sesiunea PIN', async () => {
  const id = await repo.createProject(A, 'PIN aprobare', 'demo'), { number } = await repo.createRevision(A, id, 'x'), { token } = await createShare(A, id, number);
  const c = await addChange(A, id, { title: 'Prize', amount: 300 }), seen = (await updateChange(A, id, c.id, { action: 'send' })).sentAt;
  const { pin } = await setSharePin(A, id, token);
  const call = (cookie?: string) => decideRoute(new Request(`http://localhost/api/share/${token}/changes/${c.id}`, { method: 'POST', headers: { 'content-type': 'application/json', ...(cookie ? { cookie } : {}) }, body: JSON.stringify({ decision: 'approved', author: 'Ana', seen }) }), { params: Promise.resolve({ token, change: c.id }) });
  assert.equal((await call()).status, 401);
  const s = await verifySharePin(token, pin), res = await call(`casa_share_pin=${s.cookie}`);
  assert.equal(res.status, 200, await res.clone().text());
});
test('B-7: eroarea 404 a linkului public nu se pune în cache', async () => {
  const res = await shareGet(new Request('http://localhost/'), { params: Promise.resolve({ token: 'Z'.repeat(43) }) });
  assert.equal(res.status, 404); assert.equal(res.headers.get('cache-control'), 'no-store');
});
test('B-8: un proxy local care rescrie Host în localhost dar transmite adresa clientului nu primește încredere locală', () => {
  assert.equal(isLocalRequest('localhost:3000', H({ 'x-forwarded-for': '203.0.113.9' })), false);
  assert.equal(isLocalRequest('localhost:3000', H({ 'x-forwarded-for': '127.0.0.1' })), true);
  assert.equal(isLocalRequest('localhost:3000', H({ 'x-forwarded-for': '::ffff:127.0.0.1' })), true, 'Next pune adresa socket-ului');
  assert.equal(isLocalRequest('localhost:3000', H({ 'x-forwarded-for': '127.0.0.1, 198.51.100.4' })), false);
});
test('C-1: corpul JSON al rutelor autentificate are limită reală (413), nu e citit întreg în memorie', async () => {
  const big = (n: number) => new Request('http://localhost/', { method: 'POST', body: JSON.stringify({ x: 'a'.repeat(n) }) });
  await assert.rejects(body(big(BODY_MAX + 10)), (e: any) => e.status === 413);
  assert.equal((await body(big(1024 * 1024), DRAFT_BODY_MAX)).x.length, 1024 * 1024, 'draftul poate fi mai mare');
  await assert.rejects(body(big(DRAFT_BODY_MAX + 10), DRAFT_BODY_MAX), (e: any) => e.status === 413);
});
test('C-2: proiectul: chei necunoscute eliminate, coordonate absurde refuzate, nume de cameră curățate', async () => {
  const id = await repo.createProject(A, 'Validare', 'demo'), p = await repo.getProject(A, id);
  const s: any = structuredClone(p.draft); s.evil = { a: 1 }; s.floor.rooms[0].name = '<script>x</script>' + 'a'.repeat(50000);
  await repo.saveDraft(A, id, s); const saved: any = (await repo.getProject(A, id)).draft;
  assert.ok(!('evil' in saved)); assert.ok(saved.floor.rooms[0].name.length <= 80 && !saved.floor.rooms[0].name.includes('<'));
  const far: any = structuredClone(p.draft); far.floor.rooms[0].rect.x1 = 1e300; await assert.rejects(repo.saveDraft(A, id, far), (e: any) => e.status === 400);
  const farP: any = structuredClone(p.draft); farP.placements[0].x = -5000; await assert.rejects(repo.saveDraft(A, id, farP), (e: any) => e.status === 400);
});
test('C-2: lista cheilor permise e exact interfața Snapshot (o cheie nouă din model nu e ștearsă din greșeală)', () => {
  const src = fs.readFileSync(new URL('../core/types.ts', import.meta.url), 'utf8');
  const decl = src.slice(src.indexOf('export interface Snapshot {')); const bodyTxt = decl.slice(decl.indexOf('{') + 1, decl.indexOf('\nexport '));
  const keys = [...bodyTxt.replace(/\/\*\*[\s\S]*?\*\//g, '').matchAll(/(?:^|[;{\s])(\w+)\??:/g)].map(m => m[1]);
  const top = keys.filter(k => !['import'].includes(k));
  assert.deepEqual([...new Set(top)].sort(), [...repo.SNAPSHOT_KEYS].sort());
});
test('A-5: configurația greșită e refuzată la pornire; lipsa variabilelor rămâne permisă', async () => {
  const { envProblems } = await import('../lib/env-check');
  assert.deepEqual(envProblems({}), []);
  assert.deepEqual(envProblems({ ADMIN_TOKEN: 'x'.repeat(16), ANTHROPIC_API_KEY: 'sk-ant-abcdefghijkl', DATABASE_URL: 'postgresql://u:p@h/db', CASA3D_PUBLIC_URL: 'https://casa.exemplu.ro', CASA3D_LOCAL_TRUST: '1', CASA3D_CLIENT_IP_HEADER: 'x-real-ip' }), []);
  for (const env of [{ ADMIN_TOKEN: 'scurt' }, { ANTHROPIC_API_KEY: 'cheie' }, { DATABASE_URL: 'mysql://x' }, { CASA3D_LOCAL_TRUST: 'true' }, { CASA3D_PUBLIC_URL: 'https://x.ro/cale?a=1' },
    { CASA3D_CLIENT_IP_HEADER: 'x real ip' }, { CASA3D_LOCAL_TRUST: '1', CASA3D_DEMO: '1' }]) assert.equal(envProblems(env).length, 1, JSON.stringify(env));
  const inst = fs.readFileSync(new URL('../instrumentation.ts', import.meta.url), 'utf8'); assert.match(inst, /process\.exit\(1\)/);
});
test('review L2: login reușit cu parolă veche (N=2^14) → hash nou N=2^17, aceeași cheie de sesiune; scrypt limitat la 2 simultan', async () => {
  const { upgradePasswordHash, makeSession, verifySession } = await import('../lib/auth');
  const salt = randomBytes(16).toString('base64'), legacy: AuthConfig = { v: 1, salt, owner: OWNER, secret: 'cheie-sesiune', updatedAt: '', hash: scryptSync('veche-parola-2', Buffer.from(salt, 'base64'), 64, { N: 2 ** 14, r: 8, p: 1 }).toString('base64') };
  fs.writeFileSync(process.env.CASA3D_AUTH_FILE!, JSON.stringify(legacy)); const session = makeSession(legacy);
  const next = await upgradePasswordHash(legacy, 'veche-parola-2');
  assert.equal(next.n, SCRYPT_N); assert.equal(readAuth()!.n, SCRYPT_N); assert.equal(await checkPassword(readAuth()!, 'veche-parola-2'), true);
  assert.equal(verifySession(readAuth(), session), true, 'telefoanele logate rămân logate');
  const t0 = Date.now(); const rs = await Promise.all(Array.from({ length: 6 }, () => checkPassword(readAuth()!, 'gresit')));
  assert.ok(rs.every(r => r === false)); assert.ok(Date.now() - t0 > 0);
  const src = fs.readFileSync(new URL('../lib/auth.ts', import.meta.url), 'utf8'); assert.match(src, /SCRYPT_SLOTS = 2/);
});
