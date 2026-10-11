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
  for (const p of [`/share/${tok}`, `/api/share/${tok}`, `/api/share/${tok}/feedback`, '/_next/static/x.js', '/tex/oak.jpg', '/models/plant.glb']) assert.equal(isPublicPath(p), true, p);
  for (const p of ['/api/catalog', '/api/materials', '/', '/p/123', '/p/123/present', '/api/projects', `/api/projects/x/feedback`, '/api/projects/x/renders', '/api/admin/x', '/api/outbound', '/share/short', `/api/share/${tok}/../../projects`, `/share/${tok}/x`, '/api/health']) assert.equal(isPublicPath(p), false, p);
});
