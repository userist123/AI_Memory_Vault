// Prin tunel se ajunge doar la linkurile pentru client; editorul, proiectele, randările și administrarea rămân locale.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { isLocalRequest, isPublicPath } from '../lib/public-gate';
const H = (o: Record<string, string> = {}) => ({ get: (k: string) => o[k] ?? null });
const tok = 'a'.repeat(43);

test('cererile locale au acces complet; cele prin tunel sau cu alt host nu', () => {
  for (const h of ['localhost:3123', '127.0.0.1:3123', 'localhost', '[::1]:3123']) assert.equal(isLocalRequest(h, H()), true, h);
  assert.equal(isLocalRequest('casa.trycloudflare.com', H()), false);
  assert.equal(isLocalRequest('localhost:3123', H({ 'cf-connecting-ip': '1.2.3.4' })), false, 'tunelul cu host rescris rămâne public');
  assert.equal(isLocalRequest('localhost:3123', H({ 'x-forwarded-host': 'evil.example' })), false);
  assert.equal(isLocalRequest('localhost:3123', H({ 'x-forwarded-host': 'localhost:3123' })), true, 'Next.js pune singur x-forwarded-host = Host');
  assert.equal(isLocalRequest(null, H()), false);
  assert.equal(isLocalRequest('localhost.evil.com', H()), false);
});
test('din afară: doar prezentarea pentru client și ce îi trebuie', () => {
  for (const p of [`/share/${tok}`, `/api/share/${tok}`, `/api/share/${tok}/feedback`, `/api/share/${tok}/choices`, `/api/share/${tok}/changes`, `/api/share/${tok}/changes/12345678-1234-4234-8234-123456789abc`, '/_next/static/x.js', '/tex/oak.jpg', '/models/plant.glb']) assert.equal(isPublicPath(p), true, p);
  for (const p of ['/api/catalog', '/api/materials', '/', '/p/123', '/p/123/present', '/api/projects', `/api/projects/x/feedback`, '/api/projects/x/renders', '/api/admin/x', '/api/outbound', '/share/short', `/api/share/${tok}/../../projects`, `/share/${tok}/x`, '/api/health', `/api/share/${tok}/changes/x`, `/api/share/${tok}/choices/x`, '/api/projects/x/choices', `/api/share/${tok}/changes/12345678-1234-4234-8234-123456789abc/x`, '/api/projects/x/changes']) assert.equal(isPublicPath(p), false, p);
});

import { gate, safeNext } from '../lib/public-gate';
test('prin tunel: fără login doar linkurile pentru client și loginul; cu login aplicația, fără parolă și administrare', () => {
  const g = (path: string, session = false, method = 'GET', local = false) => gate({ local, path, method, session });
  assert.equal(g('/'), 'login'); assert.equal(g('/p/x'), 'login'); assert.equal(g('/api/projects'), 'unauthorized'); assert.equal(g('/', false, 'POST'), 'unauthorized');
  assert.equal(g('/login'), 'allow'); assert.equal(g('/api/auth/login', false, 'POST'), 'allow'); assert.equal(g(`/share/${tok}`), 'allow');
  for (const p of ['/', '/p/x', '/api/projects', '/api/projects/x/renders', '/api/auth/logout']) assert.equal(g(p, true), 'allow', p);
  for (const p of ['/api/auth/password', '/api/admin/offers', '/admin']) { assert.equal(g(p, true), 'notfound', p); assert.equal(g(p, false, 'GET', true), 'allow', 'local: ' + p); }
});
test('după login se întoarce doar pe o cale din aplicație', () => {
  assert.equal(safeNext('/p/123?x=1'), '/p/123?x=1'); for (const bad of ['//evil.com', 'https://evil.com', '/\\evil.com', '/login', null, 'p/1']) assert.equal(safeNext(bad), '/', String(bad));
});
