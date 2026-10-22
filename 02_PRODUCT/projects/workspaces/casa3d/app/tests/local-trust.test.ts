// P0 (SALES_READINESS_PLAN 3.1.1): antetul Host nu mai acordă singur acces local. Fără CASA3D_LOCAL_TRUST=1 (pus doar de
// scripts/run-local.mjs, care ascultă pe 127.0.0.1), „Host: localhost” trimis direct la server nu deschide administrarea.
import { test, afterEach } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { isLocalRequest, localTrustEnabled, gate } from '../lib/public-gate';
const H = (o: Record<string, string> = {}) => ({ get: (k: string) => o[k] ?? null });
const saved = { trust: process.env.CASA3D_LOCAL_TRUST, vercel: process.env.VERCEL };
afterEach(() => { process.env.CASA3D_LOCAL_TRUST = saved.trust; if (saved.vercel === undefined) delete process.env.VERCEL; else process.env.VERCEL = saved.vercel; });

test('fără încredere locală, „Host: localhost” e tratat ca cerere din afară', () => {
  delete process.env.CASA3D_LOCAL_TRUST;
  assert.equal(localTrustEnabled(), false);
  for (const h of ['localhost', 'localhost:3000', '127.0.0.1:3000', '[::1]:3000']) assert.equal(isLocalRequest(h, H()), false, h);
  for (const path of ['/api/admin/stats', '/admin', '/api/auth/password']) assert.equal(gate({ local: isLocalRequest('localhost', H()), path, method: 'GET', session: false }), 'notfound', path);
  assert.equal(gate({ local: isLocalRequest('localhost', H()), path: '/api/projects', method: 'GET', session: false }), 'unauthorized');
});
test('doar valoarea exactă „1” activează încrederea; pe Vercel niciodată', () => {
  for (const v of ['true', 'yes', '0', '', ' 1']){ process.env.CASA3D_LOCAL_TRUST = v; assert.equal(localTrustEnabled(), false, JSON.stringify(v)); }
  process.env.CASA3D_LOCAL_TRUST = '1'; assert.equal(isLocalRequest('localhost:3000', H()), true);
  process.env.VERCEL = '1'; assert.equal(isLocalRequest('localhost:3000', H()), false);
});
test('scripturile locale ascultă doar pe 127.0.0.1 și activează încrederea; varianta găzduită nu', () => {
  const pkg = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8')).scripts;
  assert.equal(pkg.start, 'node scripts/run-local.mjs start'); assert.equal(pkg.dev, 'node scripts/run-local.mjs dev');
  assert.ok(!/LOCAL_TRUST/.test(pkg['start:hosted']));
  const launcher = readFileSync(new URL('../scripts/run-local.mjs', import.meta.url), 'utf8');
  assert.match(launcher, /'-H', '127\.0\.0\.1'/); assert.match(launcher, /CASA3D_LOCAL_TRUST: '1'/);
});
