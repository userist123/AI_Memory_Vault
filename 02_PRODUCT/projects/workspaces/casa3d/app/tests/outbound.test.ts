// Faza 4 (mod testare) — STOP GATE: ofertă lipsă, ofertă, afiliere, schimbare preț, ofertă expirată, contorizare clicuri.
import { test, describe, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { activeLink, destination, isAllowedUrl, deviceBucket, freshness, relFor } from '../core/outbound';

const L = (o: any) => ({ id: 1, target_kind: 'o', target_id: 'x', retailer: 'ikea-ro', network: null, active_from: '2020-01-01', active_to: null, ...o });
describe('logică (fără bază de date)', () => {
  test('afilierea activă are prioritate; expirată sau viitoare → link direct', () => {
    const d = L({ type: 'direct', url: 'https://www.ikea.com/ro/ro/p/a/' }), a = L({ id: 2, type: 'affiliate', network: 'profitshare', url: 'https://l.profitshare.ro/l/1' });
    assert.equal(activeLink([d, a] as any)!.type, 'affiliate');
    assert.equal(activeLink([d, { ...a, active_to: '2021-01-01' }] as any)!.type, 'direct');
    assert.equal(activeLink([d, { ...a, active_from: '2999-01-01' }] as any)!.type, 'direct');
    assert.equal(activeLink([]), null);
  });
  test('UTM doar pe linkurile directe, fără a suprascrie parametrii existenți; afilierea rămâne neatinsă', () => {
    const u = new URL(destination(L({ type: 'direct', url: 'https://www.ikea.com/ro/ro/p/a/?utm_source=x' }) as any));
    assert.equal(u.searchParams.get('utm_source'), 'x'); assert.equal(u.searchParams.get('utm_medium'), 'referral');
    assert.equal(destination(L({ type: 'affiliate', url: 'https://l.profitshare.ro/l/1' }) as any), 'https://l.profitshare.ro/l/1');
  });
  test('URL-uri permise: rețea pentru afiliere, retailer pentru direct, doar https (fără open-redirect)', () => {
    assert.ok(isAllowedUrl('https://l.profitshare.ro/l/abc', 'affiliate', 'profitshare', 'ikea-ro'));
    assert.ok(!isAllowedUrl('https://evil.example/l', 'affiliate', 'profitshare', 'ikea-ro'));
    assert.ok(!isAllowedUrl('http://www.ikea.com/ro/', 'direct', null, 'ikea-ro'));
    assert.ok(!isAllowedUrl('https://www.dedeman.ro/x', 'direct', null, 'ikea-ro'));
    assert.equal(relFor('affiliate'), 'sponsored nofollow noopener noreferrer'); assert.ok(!relFor('direct').includes('sponsored'));
  });
  test('dispozitiv și prospețimea prețului', () => {
    assert.equal(deviceBucket('Mozilla/5.0 (iPhone; CPU iPhone OS) Mobile'), 'mobil'); assert.equal(deviceBucket('Googlebot/2.1'), 'bot');
    assert.equal(freshness('2026-10-01', new Date('2026-10-10')).stale, false); assert.equal(freshness('2026-09-01', new Date('2026-10-10')).stale, true);
  });
});
describe('server (PGlite)', () => {
  let ob: typeof import('../lib/outbound'), repo: typeof import('../lib/repo'), db: typeof import('../lib/db');
  beforeAll(async () => { process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL; db = await import('../lib/db'); db.resetDbForTests(); ob = await import('../lib/outbound'); repo = await import('../lib/repo'); });
  const ua = 'Mozilla/5.0 (Windows NT 10.0) Chrome/130';
  test('produs fără ofertă → 404, fără buton în UI', async () => {
    await assert.rejects(ob.go('o', 'offer-inexistent-0', { ua, from: null }), (e: any) => e.status === 404);
    await assert.rejects(ob.go('x', '../../etc', { ua, from: null }), (e: any) => e.status === 404);
    assert.equal((await ob.outboundStatus()).targets['o:offer-inexistent-0'], undefined);
  });
  test('produs cu ofertă → redirect la IKEA cu UTM; clic înregistrat fără IP și fără ID de proiect', async () => {
    const r = await ob.go('o', 'offer-canapea-0', { ua, from: '/p/11111111-1111-4111-8111-111111111111' });
    assert.equal(r.type, 'direct'); assert.ok(r.url.startsWith('https://www.ikea.com/ro/ro/p/kivik')); assert.ok(r.url.includes('utm_source=casamea3d'));
    const { q } = await db.getDb(), cols = (await q("select column_name from information_schema.columns where table_name='clicks'")).rows.map((x: any) => x.column_name);
    assert.ok(!cols.some((c: string) => /ip|cookie|agent|session/i.test(c)), cols.join(','));
    const row = (await q("select * from clicks where target_id='offer-canapea-0'")).rows[0]; assert.equal(row.device, 'desktop'); assert.equal(row.from_page, '/p/:id');
    assert.equal(new Date(row.hour).getUTCMinutes(), 0);
  });
  test('roboții nu sunt contorizați', async () => {
    const { q } = await db.getDb(), n0 = (await q('select count(*)::int n from clicks')).rows[0].n;
    await ob.go('m', 'parchet-egger-h2099', { ua: 'Googlebot', from: null }); assert.equal((await q('select count(*)::int n from clicks')).rows[0].n, n0);
  });
  test('link de afiliere: are prioritate, fără UTM, activează informarea; domeniu străin → refuzat', async () => {
    await assert.rejects(ob.addLink({ kind: 'o', targetId: 'offer-masuta-0', type: 'affiliate', network: 'profitshare', url: 'https://evil.example/x' }), (e: any) => e.status === 400);
    await ob.addLink({ kind: 'o', targetId: 'offer-masuta-0', type: 'affiliate', network: 'profitshare', url: 'https://l.profitshare.ro/l/test123' });
    const r = await ob.go('o', 'offer-masuta-0', { ua, from: null }); assert.equal(r.type, 'affiliate'); assert.equal(r.url, 'https://l.profitshare.ro/l/test123');
    const st = await ob.outboundStatus(); assert.equal(st.targets['o:offer-masuta-0'], 'affiliate'); assert.equal(st.disclosure, true);
  });
  test('afiliere expirată → revine automat la linkul direct', async () => {
    await ob.addLink({ kind: 'o', targetId: 'offer-pat-0', type: 'affiliate', network: '2performant', url: 'https://event.2performant.ro/events/click?x=1', activeFrom: '2025-01-01', activeTo: '2025-02-01' });
    const r = await ob.go('o', 'offer-pat-0', { ua, from: null }); assert.equal(r.type, 'direct'); assert.equal(new URL(r.url).hostname, 'www.ikea.com');
  });
  test('schimbare de preț: catalogul se actualizează, istoricul se păstrează', async () => {
    await assert.rejects(ob.updatePrice({ kind: 'o', targetId: 'offer-canapea-0', price: -5, verifiedAt: '2026-10-02' }), (e: any) => e.status === 400);
    await ob.updatePrice({ kind: 'o', targetId: 'offer-canapea-0', price: 2199, verifiedAt: '2026-10-02' });
    const c = await repo.getCatalog(), o = c.offers.find(x => x.id === 'offer-canapea-0')!;
    assert.equal(o.price, 2199); assert.equal(o.provenance.verifiedAt, '2026-10-02'); assert.equal(o.provenance.verificationType, 'manual');
    assert.deepEqual((await ob.priceHistory('o', 'offer-canapea-0')).map(h => h.price), [2299, 2199]);
    await ob.updatePrice({ kind: 'm', targetId: 'vopsea-innenweiss', price: 159, verifiedAt: '2026-10-02' });
    const m = (await repo.getMaterials()).materials.find(x => x.id === 'vopsea-innenweiss')!; assert.equal(m.pack!.price, 159); assert.equal(m.unitPrice, 10.6);
  });
  test('statistici de clicuri și verificarea linkurilor (HEAD simulat)', async () => {
    const s = await ob.clickStats(); assert.ok(s.byRetailer.some((r: any) => r.retailer === 'ikea-ro' && r.n >= 1));
    const fake = (async (url: string) => ({ status: url.includes('canapea') ? 404 : 200 })) as any;
    const res = await ob.checkLinks(fake, 500); assert.ok(res.checked > 50); assert.ok(res.broken.some(u => u.includes('kivik')));
  });
  test('administrarea cere ADMIN_TOKEN', async () => {
    delete process.env.ADMIN_TOKEN; assert.throws(() => ob.assertAdmin(new Request('http://x')), (e: any) => e.status === 404);
    process.env.ADMIN_TOKEN = 'x'.repeat(32); assert.throws(() => ob.assertAdmin(new Request('http://x', { headers: { 'x-admin-token': 'gresit' } })), (e: any) => e.status === 401);
    assert.doesNotThrow(() => ob.assertAdmin(new Request('http://x', { headers: { 'x-admin-token': 'x'.repeat(32) } })));
  });
});
