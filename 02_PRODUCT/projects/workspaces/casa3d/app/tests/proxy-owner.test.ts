// proxy.ts la prima vizită: pagina aplicației primește proprietarul anonim (același în răspuns și în cererea curentă),
// iar API-ul, linkurile clientului și paginile publice nu primesc nimic; un proprietar valid nu se schimbă.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { NextRequest } from 'next/server';
import { proxy } from '../proxy';

const ID = '22222222-2222-4222-8222-222222222222';
const call = (path: string, cookie?: string, method = 'GET') => proxy(new NextRequest(`http://127.0.0.1:3000${path}`, { method, headers: { host: '127.0.0.1:3000', ...(cookie ? { cookie } : {}) } }));
const ownerSet = (res: Response) => (res.headers.get('set-cookie') ?? '').match(/casa_owner=([0-9a-f-]{36})/)?.[1] ?? null;

test('prima pagină: cookie httpOnly, SameSite=Lax, 2 ani, același id trimis și cererii curente', () => {
  const res = call('/'), id = ownerSet(res);
  assert.ok(id, 'pune proprietarul');
  const sc = res.headers.get('set-cookie')!.toLowerCase();
  assert.ok(sc.includes('httponly') && sc.includes('samesite=lax') && sc.includes('path=/') && sc.includes('max-age=63072000'), sc);
  // Next transmite cererea modificată prin antetul x-middleware-request-cookie
  const fwd = res.headers.get('x-middleware-request-cookie') ?? '';
  assert.ok(fwd.includes(`casa_owner=${id}`), `cererea curentă vede proprietarul: ${fwd}`);
});
test('un proprietar valid rămâne; unul stricat se înlocuiește', () => {
  assert.equal(ownerSet(call('/p/x', `casa_owner=${ID}`)), null);
  assert.match(ownerSet(call('/', 'casa_owner=stricat'))!, /^[0-9a-f-]{36}$/);
});
test('API, linkul clientului, paginile publice și scrierile nu primesc proprietar din proxy', () => {
  for (const p of ['/api/projects', '/share/abc', '/accesibilitate', '/login']) assert.equal(ownerSet(call(p)), null, p);
  assert.equal(ownerSet(call('/', undefined, 'HEAD')), null);
});
