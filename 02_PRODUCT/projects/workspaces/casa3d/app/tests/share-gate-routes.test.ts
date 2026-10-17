// Fiecare rută /api/share/** trece prin poartă de pe telefonul clientului (prin tunel, fără sesiune) — testat cu middleware-ul
// real pe toate rutele găsite pe disc, ca să nu se mai repete /choices blocat (401). Plus limitele pe link și IP.
import { test, beforeAll, beforeEach } from 'vitest';
import assert from 'node:assert/strict';
import { readdirSync, statSync } from 'node:fs';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
process.env.CASA3D_AUTH_FILE = path.join(import.meta.dirname, '__no_auth_file__.json');
import { NextRequest } from 'next/server';
import { middleware } from '../middleware';
import { resetDbForTests } from '../lib/db';
import { resetShareLimits, createLimiter, shareLimitMessage } from '../lib/rate-limit';
beforeAll(() => resetDbForTests());
beforeEach(() => resetShareLimits());

const root = path.resolve(import.meta.dirname, '..'), tok = 'A'.repeat(43), uuid = '12345678-1234-4234-8234-123456789abc';
const walk = (d: string): string[] => readdirSync(d).flatMap(f => { const p = path.join(d, f); return statSync(p).isDirectory() ? walk(p) : f === 'route.ts' ? [p] : []; });
const routes = walk(path.join(root, 'app', 'api', 'share')).map(f => ({ file: f, url: '/' + path.relative(path.join(root, 'app'), path.dirname(f)).split(path.sep).join('/').replace('[token]', tok).replace(/\[[^\]]+\]/g, uuid) }));
const remote = (url: string, method = 'GET', extra: Record<string, string> = {}) => new NextRequest(`http://localhost:3123${url}`, { method, headers: { host: 'localhost:3123', 'cf-connecting-ip': '203.0.113.7', ...extra } });
const passes = (res: Response) => res.headers.get('x-middleware-next') === '1';

test('toate rutele /api/share/** găsite pe disc (cel puțin share, feedback, choices, changes, changes/[change])', () => {
  assert.deepEqual(routes.map(r => r.url.replace(tok, 'T').replace(uuid, 'U')).sort(), ['/api/share/T', '/api/share/T/changes', '/api/share/T/changes/U', '/api/share/T/choices', '/api/share/T/feedback']);
});

test('prin tunel, fără sesiune: fiecare metodă exportată de fiecare rută share trece de poartă', async () => {
  for (const r of routes){ const mod = await import(r.file); const methods = ['GET', 'POST', 'PATCH', 'PUT', 'DELETE'].filter(m => typeof mod[m] === 'function');
    assert.ok(methods.length > 0, r.url);
    for (const m of methods){ const res = middleware(remote(r.url, m)); assert.ok(passes(res), `${m} ${r.url} → ${res.status}`); } }
  // martor: ce e al designerului rămâne închis prin tunel
  assert.equal(middleware(remote('/api/projects')).status, 401); assert.equal(middleware(remote(`/api/share/${tok}/../../projects`)).status, 401);
  assert.equal(middleware(remote('/api/auth/password')).status, 404);
});

test('rutele share chiar răspund fără cookie (nu 401): link inexistent → 404, nu „Autentifică-te”', async () => {
  for (const r of routes){ const mod = await import(r.file), params = Promise.resolve({ token: tok, change: uuid });
    if (mod.GET){ const res = await mod.GET(new Request(`http://localhost${r.url}`), { params }); assert.notEqual(res.status, 401, `GET ${r.url}`); }
    if (mod.POST){ const res = await mod.POST(new Request(`http://localhost${r.url}`, { method: 'POST', headers: { 'content-type': 'application/json', 'cf-connecting-ip': '203.0.113.8' }, body: '{}' }), { params }); assert.notEqual(res.status, 401, `POST ${r.url}`); } }
});

test('părerile, alegerile și deciziile publice au limită pe link + IP: 429 cu mesaj în română și Retry-After', async () => {
  const cases: [string, string, number][] = [['feedback', `/api/share/${tok}/feedback`, 20], ['choices', `/api/share/${tok}/choices`, 30], ['changes/[change]', `/api/share/${tok}/changes/${uuid}`, 20]];
  for (const [dir, url, max] of cases){ const { POST } = await import(path.join(root, 'app', 'api', 'share', '[token]', ...dir.split('/'), 'route.ts')), params = Promise.resolve({ token: tok, change: uuid });
    const send = (ip: string) => POST(new Request(`http://localhost${url}`, { method: 'POST', headers: { 'content-type': 'application/json', 'cf-connecting-ip': ip }, body: '{}' }), { params });
    for (let i = 0; i < max; i++) assert.notEqual((await send('198.51.100.1')).status, 429, `${dir} #${i + 1}`);
    const res = await send('198.51.100.1'); assert.equal(res.status, 429, dir);
    assert.match((await res.json()).error, /^Prea multe trimiteri de pe acest link\. Încearcă din nou peste \d+ minute?\.$/); assert.ok(Number(res.headers.get('retry-after')) > 0);
    assert.notEqual((await send('198.51.100.2')).status, 429, `${dir}: altă adresă are limita ei`); }
});

test('limitatorul: fereastră glisantă, timp rămas, memorie mărginită', () => {
  const l = createLimiter({ windowMs: 60_000, max: 2, maxKeys: 3 });
  assert.equal(l.take('a', 0).ok, true); assert.equal(l.take('a', 10_000).ok, true);
  const no = l.take('a', 20_000); assert.equal(no.ok, false); assert.equal(!no.ok && no.retryAfterMs, 40_000);
  assert.equal(l.take('a', 60_001).ok, true, 'prima încercare a ieșit din fereastră');
  for (const k of ['b', 'c', 'd', 'e']) l.take(k, 61_000); assert.equal(l.take('a', 61_001).ok, true, 'cheia veche a fost scoasă (memorie mărginită)');
  assert.equal(shareLimitMessage(30_000), 'Prea multe trimiteri de pe acest link. Încearcă din nou peste 1 minut.');
});
