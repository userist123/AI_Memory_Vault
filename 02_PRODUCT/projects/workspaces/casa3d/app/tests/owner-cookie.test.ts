// Proprietarul anonim (casa_owner) se atribuie o singură dată, în proxy, la prima cerere de pagină sau API.
// Înainte, fiecare cerere API fără cookie își făcea alt proprietar; la prima vizită pagina trimite mai multe cereri
// deodată, câștiga ultimul Set-Cookie și proiectele create cu celelalte id-uri rămâneau orfane.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { NextRequest } from 'next/server';
import { proxy } from '../proxy';
import { OWNER_COOKIE } from '../lib/owner';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const req = (p: string, cookie?: string) => new NextRequest(`http://127.0.0.1:3000${p}`, { headers: { host: '127.0.0.1:3000', ...(cookie ? { cookie } : {}) } });
const forwardedCookie = (res: Response) => res.headers.get('x-middleware-request-cookie') ?? '';

test('fără cookie: proxy atribuie un proprietar, îl pune în răspuns și în cererea transmisă mai departe', () => {
  for (const p of ['/', '/p/abc', '/api/projects']) {
    const res = proxy(req(p)), id = res.cookies.get(OWNER_COOKIE)?.value ?? '';
    assert.match(id, UUID, p);
    assert.ok(forwardedCookie(res).includes(`${OWNER_COOKIE}=${id}`), `${p}: handler-ul trebuie să vadă același id`);
  }
});

test('cookie valid: nu se schimbă; cookie invalid: se înlocuiește', () => {
  const ok = 'aaaaaaaa-0000-4000-8000-00000000000a';
  assert.equal(proxy(req('/api/projects', `${OWNER_COOKIE}=${ok}`)).cookies.get(OWNER_COOKIE), undefined);
  const res = proxy(req('/', `${OWNER_COOKIE}=nu-e-uuid; alt=1`)), id = res.cookies.get(OWNER_COOKIE)?.value ?? '';
  assert.match(id, UUID);
  assert.ok(forwardedCookie(res).includes(`${OWNER_COOKIE}=${id}`) && forwardedCookie(res).includes('alt=1'));
  assert.ok(!forwardedCookie(res).includes('nu-e-uuid'));
});

test('fișierele statice nu primesc cookie de proprietar', () => {
  for (const p of ['/_next/static/chunks/a.js', '/models/x/x.gltf', '/favicon.ico']) assert.equal(proxy(req(p)).cookies.get(OWNER_COOKIE), undefined, p);
});

test('autentificarea, linkurile clientului și login-ul nu primesc proprietar anonim', () => {
  for (const p of ['/api/auth/login', '/share/abc', '/api/share/abc', '/login']) assert.equal(proxy(req(p)).cookies.get(OWNER_COOKIE), undefined, p);
  // dar o cale doar asemănătoare primește
  assert.match(proxy(req('/loginx')).cookies.get(OWNER_COOKIE)?.value ?? '', UUID);
});
