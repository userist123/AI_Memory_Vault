// Rutele de autentificare chemate direct: ieșirea din cont cere aceeași verificare de origine ca loginul, iar blocarea după
// prea multe încercări spune cât mai e de așteptat (Retry-After + minute în mesaj).
import { test, beforeEach } from 'vitest';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'casa3d-authr-')); process.env.CASA3D_AUTH_FILE = path.join(dir, 'auth.json');
import { POST as logout } from '../app/api/auth/logout/route';
import { POST as login } from '../app/api/auth/login/route';
import { setPassword, resetLoginLimits, loginRetryAfterMs, takeLoginAttempt } from '../lib/auth';
import { isLocalHostname } from '../lib/public-gate';
beforeEach(() => resetLoginLimits());
const req = (url: string, h: Record<string, string>, body = '{}') => new Request(`http://localhost:3123${url}`, { method: 'POST', headers: { host: 'localhost:3123', ...h }, body });

test('logout: o pagină străină (cross-site, alt Origin, form text/plain) primește 403 și nu șterge cookie-ul', async () => {
  const bad: Record<string, string>[] = [{ 'sec-fetch-site': 'cross-site', 'content-type': 'application/json' }, { origin: 'https://evil.example', 'content-type': 'application/json' }, { 'content-type': 'text/plain' }];
  for (const h of bad){
    const r = await logout(req('/api/auth/logout', h)); assert.equal(r.status, 403, JSON.stringify(h)); assert.equal(r.headers.get('set-cookie'), null); }
  const ok = await logout(req('/api/auth/logout', { 'sec-fetch-site': 'same-origin', origin: 'http://localhost:3123', 'content-type': 'application/json' }));
  assert.equal(ok.status, 200); assert.match(ok.headers.get('set-cookie') ?? '', /casa_session=;.*Max-Age=0/i);
});

test('login blocat: 429 cu minutele rămase și Retry-After', async () => {
  setPassword('parola-de-test-1', '77777777-7777-4777-8777-777777777777');
  const h = { 'sec-fetch-site': 'same-origin', 'content-type': 'application/json', 'cf-connecting-ip': '203.0.113.9' };
  for (let i = 0; i < 5; i++) assert.equal((await login(req('/api/auth/login', h, JSON.stringify({ password: 'gresit-' + i })))).status, 401);
  const r = await login(req('/api/auth/login', h, JSON.stringify({ password: 'parola-de-test-1' }))); assert.equal(r.status, 429, 'blocat chiar și cu parola bună');
  const j = await r.json(); assert.match(j.error, /^Prea multe încercări\. Încearcă din nou peste (1[0-5]|[1-9]) minute?\.$/); assert.ok(j.retryAfter > 0 && j.retryAfter <= 900);
  assert.equal(r.headers.get('retry-after'), String(j.retryAfter));
});

test('timpul rămas scade odată cu fereastra; 0 cât timp mai sunt încercări', () => {
  assert.equal(loginRetryAfterMs('ip', 0), 0); for (let i = 0; i < 5; i++) takeLoginAttempt('ip', i * 1000);
  assert.equal(loginRetryAfterMs('ip', 5000), 15 * 60e3 - 5000); assert.equal(loginRetryAfterMs('ip', 15 * 60e3 + 1), 0);
});

test('PhoneAccess întreabă de parolă doar pe acest calculator (fără 404 prin tunel)', () => {
  for (const h of ['localhost', '127.0.0.1', '[::1]', '::1']) assert.equal(isLocalHostname(h), true, h);
  for (const h of ['casa.trycloudflare.com', '192.168.1.10', 'localhost.evil.com', '']) assert.equal(isLocalHostname(h), false, h);
});
