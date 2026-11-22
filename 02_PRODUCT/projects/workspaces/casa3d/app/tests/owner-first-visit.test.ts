// Proprietarul anonim la prima vizită (lib/owner.ts + proxy.ts): pagina primește cookie-ul înainte ca cererile ei paralele
// să-și inventeze fiecare câte un proprietar (proiectul nou apărea „negăsit”).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { ownerForPage } from '../lib/owner';

const req = (method: string, cookie?: string) => ({ method, cookies: { get: (n: string) => n === 'casa_owner' && cookie !== undefined ? { value: cookie } : undefined } });
const ID = '11111111-1111-4111-8111-111111111111';
test('o pagină GET fără proprietar primește unul nou; cu unul valid, nimic', () => {
  const a = ownerForPage(req('GET'), '/'), b = ownerForPage(req('GET'), '/p/abc');
  assert.match(a!, /^[0-9a-f-]{36}$/); assert.match(b!, /^[0-9a-f-]{36}$/); assert.notEqual(a, b);
  assert.equal(ownerForPage(req('GET', ID), '/'), null);
  assert.match(ownerForPage(req('GET', 'stricat'), '/')!, /^[0-9a-f-]{36}$/, 'un cookie invalid se înlocuiește');
});
test('API-ul, linkurile clientului, fișierele și scrierile nu primesc proprietar din proxy', () => {
  for (const p of ['/api/projects', '/share/tok', '/_next/static/x.js', '/favicon.ico', '/robots.txt']) assert.equal(ownerForPage(req('GET'), p), null, p);
  assert.equal(ownerForPage(req('POST'), '/'), null);
});
