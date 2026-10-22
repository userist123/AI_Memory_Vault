// Produs din link: preluarea paginii e păzită (magazine permise, IP-uri locale refuzate la conectare, redirecționări
// verificate), produsul intră în comenzi ca linie proprie, cu starea lui, și pleacă din comenzi când e șters.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { fetchProduct, safeLookup, type Hop } from '../lib/clip';
import { addCustomItem, updateCustomItem, deleteCustomItem, CUSTOM_LIMITS } from '../lib/custom-items';
import { getOrders, setOrderState } from '../lib/procurement';
import { checkCustomItem } from '../core/clip';
beforeAll(() => resetDbForTests());
const A = '88888888-8888-4888-8888-888888888888', B = '99999999-9999-4999-8999-999999999999';
const PAGE = `<html><head><script type="application/ld+json">{"@type":"Product","name":"Lampă Hektar","offers":{"price":"149.99","priceCurrency":"RON"},"image":"/img/h.jpg"}</script></head></html>`;
const ok = (body = PAGE): Hop => ({ status: 200, location: null, type: 'text/html; charset=utf-8', body: Buffer.from(body) });
const redir = (to: string): Hop => ({ status: 301, location: to, type: '', body: null });

test('preluarea: doar magazine permise, redirecționări verificate, erori clare', async () => {
  const seen: string[] = [], get = (map: Record<string, Hop>) => async (u: string) => { seen.push(u); const h = map[u]; if (!h) throw new Error('neașteptat ' + u); return h; };
  const p = await fetchProduct('https://www.ikea.com/ro/ro/p/hektar', get({ 'https://www.ikea.com/ro/ro/p/hektar': ok() }));
  assert.equal(p.name, 'Lampă Hektar'); assert.equal(p.price, 149.99); assert.equal(p.supplier, 'IKEA'); assert.equal(p.image, 'https://www.ikea.com/img/h.jpg');
  // redirecționare în același magazin: urmată; spre altă adresă: refuzată fără să fie cerută
  assert.equal((await fetchProduct('https://ikea.com/a', get({ 'https://ikea.com/a': redir('/b'), 'https://ikea.com/b': ok() }))).url, 'https://ikea.com/b');
  seen.length = 0;
  for (const to of ['http://169.254.169.254/latest', 'https://localhost/x', 'https://evil.example/p', 'http://www.ikea.com/p'])
    await assert.rejects(fetchProduct('https://ikea.com/a', get({ 'https://ikea.com/a': redir(to) })), (e: any) => e.status === 502, to);
  assert.ok(seen.every(u => u === 'https://ikea.com/a'), 'adresele din afara listei nu se cer niciodată');
  const loop = async (u: string) => redir(u + 'x');
  await assert.rejects(fetchProduct('https://ikea.com/a', loop), (e: any) => e.status === 502 && /redirec/i.test(e.message));
  for (const bad of ['http://ikea.com/a', 'https://127.0.0.1/', 'https://ikea.com.evil.example/', 'https://u:p@ikea.com/', 'https://ikea.com:8443/', 'nu e url', 42, null])
    await assert.rejects(fetchProduct(bad, async () => ok()), (e: any) => e.status === 400, String(bad));
  await assert.rejects(fetchProduct('https://ikea.com/a', async () => ({ status: 403, location: null, type: 'text/html', body: null })), (e: any) => e.status === 502);
  await assert.rejects(fetchProduct('https://ikea.com/a', async () => ({ status: 200, location: null, type: 'application/pdf', body: null })), (e: any) => e.status === 422);
  await assert.rejects(fetchProduct('https://ikea.com/a', async () => ok('<html><body>nimic</body></html>')), (e: any) => e.status === 422);
  await assert.rejects(fetchProduct('https://ikea.com/a', async () => { throw Object.assign(new Error('x'), { code: 'EPRIVATE' }); }), (e: any) => e.status === 502 && /internet/.test(e.message));
});

test('conectarea refuză orice adresă locală, chiar dacă DNS-ul o amestecă printre cele publice', async () => {
  const run = (addrs: { address: string; family: number }[], all = false) => new Promise<any>((res) => {
    const lk = safeLookup((_h, _o, cb) => cb(null, addrs)); (lk as any)('shop.example', { all }, (e: any, a: any, f: any) => res({ e, a, f })); });
  const pub = await run([{ address: '93.184.216.34', family: 4 }]); assert.equal(pub.e, null); assert.equal(pub.a, '93.184.216.34'); assert.equal(pub.f, 4);
  assert.deepEqual((await run([{ address: '93.184.216.34', family: 4 }], true)).a, [{ address: '93.184.216.34', family: 4 }]);
  for (const bad of ['127.0.0.1', '10.1.2.3', '192.168.1.10', '169.254.169.254', '::1', 'fd00::1', '::ffff:10.0.0.1', '0.0.0.0'])
    assert.equal((await run([{ address: '93.184.216.34', family: 4 }, { address: bad, family: bad.includes(':') ? 6 : 4 }])).e?.code, 'EPRIVATE', bad);
  assert.equal((await run([])).e?.code, 'EPRIVATE');
});

test('produsul adăugat intră în comenzi, cu cameră, total și stare; ștergerea îl scoate cu tot cu stare', async () => {
  const id = await repo.createProject(A, 'Din link', 'demo'), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!;
  const it = await addCustomItem(A, id, { name: '  Lampă Hektar ', supplier: 'IKEA', url: 'https://www.ikea.com/p', image: 'https://www.ikea.com/i.jpg', price: 149.99, currency: 'ron', qty: 2, roomId: room.id });
  assert.equal(it.name, 'Lampă Hektar'); assert.equal(it.currency, 'RON'); assert.equal(it.unit, 'buc');
  let o = await getOrders(A, id), line = o.rows.find(r => r.key === 'c|' + it.id)!;
  assert.equal(line.kind, 'custom'); assert.equal(line.total, 299.98); assert.deepEqual(line.rooms, [room.name]); assert.equal(line.supplier, 'IKEA'); assert.equal(line.image, 'https://www.ikea.com/i.jpg');
  await setOrderState(A, id, line.key, { status: 'ordered', ref: 'IK-1' });
  await updateCustomItem(A, id, it.id, { qty: 3 }); o = await getOrders(A, id); line = o.rows.find(r => r.key === 'c|' + it.id)!;
  assert.equal(line.total, 449.97); assert.equal(line.state.status, 'ordered', 'starea rămâne la modificare');
  await assert.rejects(updateCustomItem(B, id, it.id, { qty: 1 })); await assert.rejects(deleteCustomItem(B, id, it.id)); await assert.rejects(addCustomItem(B, id, { name: 'x', supplier: 'y', qty: 1 }));
  await deleteCustomItem(A, id, it.id); o = await getOrders(A, id);
  assert.ok(!o.rows.some(r => r.key === 'c|' + it.id));
  await assert.rejects(setOrderState(A, id, 'c|' + it.id, { status: 'ordered' }), (e: any) => e.status === 400, 'linia ștearsă nu mai primește stare');
  await assert.rejects(deleteCustomItem(A, id, it.id), (e: any) => e.status === 404);
});

test('validarea produsului adăugat', async () => {
  const id = await repo.createProject(A, 'Validare', 'demo');
  for (const bad of [{ supplier: 'y', qty: 1 }, { name: 'x', qty: 1 }, { name: 'x', supplier: 'y', qty: 0 }, { name: 'x', supplier: 'y', qty: 1, price: -1 }, { name: 'x', supplier: 'y', qty: 1, price: '12' },
    { name: 'x', supplier: 'y', qty: 1, url: 'javascript:alert(1)' }, { name: 'x', supplier: 'y', qty: 1, image: 'http://a/b.jpg' }, { name: 'x', supplier: 'y', qty: 1, currency: 'lei!' },
    { name: 'x', supplier: 'y', qty: 1, roomId: 'nu-exista' }, null, []])
    await assert.rejects(addCustomItem(A, id, bad), (e: any) => e.status === 400, JSON.stringify(bad));
  assert.deepEqual(checkCustomItem({ qty: 2 }, true), { qty: 2 }, 'la modificare doar câmpurile trimise');
  assert.equal(checkCustomItem({ name: 'A‮B', supplier: 'S', qty: 1 }).name, 'A B');
  await assert.rejects(updateCustomItem(A, id, 'nu-e-uuid', { qty: 1 }), (e: any) => e.status === 404);
  assert.ok(CUSTOM_LIMITS.perProject >= 100);
});
