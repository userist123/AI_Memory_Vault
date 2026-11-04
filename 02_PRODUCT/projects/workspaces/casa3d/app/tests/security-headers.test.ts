// P0 (SALES_READINESS_PLAN 3.1.3): fiecare răspuns (permis, login, 401, 404) are CSP cu nonce nou, frame-ancestors 'none',
// nosniff, Permissions-Policy; HSTS doar pe https; linkurile de client fără Referer; nicio resursă de la terți (fonturi).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
process.env.CASA3D_AUTH_FILE = path.join(import.meta.dirname, '__no_auth_file__.json');
import { NextRequest } from 'next/server';
import { proxy as middleware } from '../proxy';   // Next.js 16: middleware.ts → proxy.ts
import { contentSecurityPolicy, hstsFor, API_CSP } from '../lib/security-headers';
const tok = 'A'.repeat(43);
const req = (url: string, extra: Record<string, string> = {}, host = 'localhost:3123') => new NextRequest(`http://${host}${url}`, { headers: { host, ...extra } });
const remote = (url: string, extra: Record<string, string> = {}) => req(url, { 'cf-connecting-ip': '203.0.113.7', ...extra });
const nonceOf = (csp: string) => /'nonce-([A-Za-z0-9+/=]+)'/.exec(csp)?.[1];

test('toate deciziile porții (permis, login, 401, 404) primesc antetele', () => {
  const cases = [req('/'), remote('/'), remote('/api/projects'), remote('/api/admin/stats'), remote(`/share/${tok}`)];
  const statuses = new Set<number>();
  for (const r of cases.filter(c => !c.nextUrl.pathname.startsWith('/api/'))){ const res = middleware(r); statuses.add(res.status);
    const csp = res.headers.get('content-security-policy')!; assert.ok(csp, r.url);
    for (const d of ["default-src 'self'", "frame-ancestors 'none'", "object-src 'none'", "base-uri 'self'", "'strict-dynamic'"]) assert.ok(csp.includes(d), `${r.url}: ${d}`);
    assert.ok(nonceOf(csp)!.length >= 22, 'nonce de cel puțin 128 de biți');
    assert.equal(res.headers.get('x-content-type-options'), 'nosniff'); assert.equal(res.headers.get('x-frame-options'), 'DENY');
    assert.match(res.headers.get('permissions-policy')!, /camera=\(\)/); assert.equal(res.headers.get('strict-transport-security'), null, 'fără HSTS pe http'); }
  for (const r of cases.filter(c => c.nextUrl.pathname.startsWith('/api/'))){ const res = middleware(r); statuses.add(res.status);
    assert.equal(res.headers.get('content-security-policy'), API_CSP, r.url); assert.equal(res.headers.get('x-content-type-options'), 'nosniff'); }
  assert.deepEqual([...statuses].sort(), [200, 401, 404]);
});
test('review F2: antetele middleware-ului înlocuiesc pe ale rutei, deci pe /api și /go/ se alege varianta strictă aici', () => {
  // Next pune întâi antetele din middleware; un antet cu același nume pus de rută se ignoră
  const photo = middleware(req('/api/projects/12345678-1234-4234-8234-123456789abc/site/photos/x'));
  assert.equal(photo.headers.get('content-security-policy'), API_CSP); assert.match(API_CSP, /sandbox/); assert.match(API_CSP, /default-src 'none'/);
  for (const u of ['/go/o/offer-1', '/go/m/mat-1']) assert.equal(middleware(req(u)).headers.get('referrer-policy'), 'no-referrer', u);
});
test('nonce nou la fiecare cerere și transmis lui Next.js în cerere', () => {
  const a = middleware(req('/')), b = middleware(req('/'));
  const na = nonceOf(a.headers.get('content-security-policy')!), nb = nonceOf(b.headers.get('content-security-policy')!);
  assert.notEqual(na, nb);
  assert.equal(a.headers.get('x-middleware-request-x-nonce'), na, 'Next primește nonce-ul în antetele cererii');
  assert.ok(a.headers.get('x-middleware-request-content-security-policy')?.includes(`'nonce-${na}'`));
});
test('fără unsafe-eval și fără surse externe în producție; eval doar în dezvoltare', () => {
  const prod = contentSecurityPolicy('n', false), dev = contentSecurityPolicy('n', true);
  assert.ok(!prod.includes('unsafe-eval')); assert.ok(!/https?:/.test(prod), 'nicio origine externă'); assert.ok(!/script-src[^;]*unsafe-inline/.test(prod));
  assert.ok(dev.includes("'unsafe-eval'"));
});
test('HSTS doar când cererea a venit pe https; linkul de client fără Referer', () => {
  assert.equal(hstsFor({ get: k => k === 'x-forwarded-proto' ? 'https' : null }), 'max-age=31536000; includeSubDomains');
  assert.equal(hstsFor({ get: () => null }), null);
  assert.ok(middleware(remote('/', { 'x-forwarded-proto': 'https' })).headers.get('strict-transport-security'));
  for (const u of [`/share/${tok}`, `/api/share/${tok}/feedback`]) assert.equal(middleware(remote(u)).headers.get('referrer-policy'), 'no-referrer', u);
  assert.equal(middleware(req('/')).headers.get('referrer-policy'), 'strict-origin-when-cross-origin');
});
test('layout-ul nu încarcă fonturi sau scripturi de la terți', () => {
  const layout = readFileSync(new URL('../app/layout.tsx', import.meta.url), 'utf8');
  assert.ok(!/googleapis|gstatic|https?:\/\//.test(layout.replace(/^\s*\/\/.*$/gm, '')), 'fără origini externe în layout');
  assert.match(layout, /@fontsource\/instrument-sans/);
});
