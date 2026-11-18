// Prețurile reverificate pe paginile produselor se aplică peste bază doar dacă sunt mai noi; a doua pornire nu schimbă nimic;
// o corectură manuală mai nouă rămâne; istoricul primește câte o intrare; datele din fișierul de reverificări sunt valide.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import { applyPriceRechecks, PRICE_RECHECKS, type PriceRecheck } from '../lib/price-rechecks';
import { updatePrice } from '../lib/outbound';
import materialsJson from '../data/materials.v1.json';
import catalogJson from '../data/catalog.v1.json';
beforeAll(() => resetDbForTests());

test('aplicare: material simplu, material cu preț pe pachet, ofertă; idempotent; manualul mai nou rămâne; un rând de istoric', async () => {
  const { q } = await getDb(), mats = (materialsJson as any).materials, offers = (catalogJson as any).offers;
  const plain = mats.find((m: any) => !m.pack?.price && m.verifiedAt && m.verifiedAt < '2026-11-01'), packed = mats.find((m: any) => m.pack?.price != null && m.pack.size > 0);
  const offer = offers.find((o: any) => o.provenance?.verifiedAt && o.provenance.verifiedAt < '2026-11-01');
  const items: PriceRecheck[] = [
    { kind: 'm', id: plain.id, price: 12.34, seenAt: '2026-11-30', url: plain.sourceUrl, evidence: 'test' },
    { kind: 'm', id: packed.id, price: 300, seenAt: '2026-11-30', url: packed.sourceUrl, evidence: 'test' },
    { kind: 'o', id: offer.id, price: 999.5, seenAt: '2026-11-30', url: offer.provenance.sourceUrl, evidence: 'test' }];
  const hist = async (id: string) => Number((await q('select count(*)::int as n from price_history where target_id=$1', [id])).rows[0].n), h0 = await hist(plain.id);
  assert.equal(await applyPriceRechecks(q, items), 3);
  const row = async (sql: string, id: string) => (await q(sql, [id])).rows[0];
  const a = await row('select unit_price, verified_at::text as v from materials where id=$1', plain.id); assert.equal(Number(a.unit_price), 12.34); assert.equal(a.v, '2026-11-30');
  const b = await row('select unit_price, pack from materials where id=$1', packed.id); assert.equal(b.pack.price, 300); assert.equal(Number(b.unit_price), Math.round(300 / packed.pack.size * 100) / 100);
  const c = await row('select price from offers where id=$1', offer.id); assert.equal(Number(c.price), 999.5);
  assert.equal(await applyPriceRechecks(q, items), 0, 'a doua pornire nu mai schimbă nimic');
  // pachet observat pe unitate (parchet: lei/m²): prețul pe unitate se actualizează, prețul pachetului din import se scoate
  const sqm = mats.find((m: any) => m.pack?.price != null && m.unit === 'm2' && m.id !== packed.id)!;
  assert.equal(await applyPriceRechecks(q, [{ kind: 'm', id: sqm.id, price: 77.7, seenAt: '2026-11-30', url: sqm.sourceUrl, evidence: 't', per: 'unit' }]), 1);
  const u = await row('select unit_price, pack from materials where id=$1', sqm.id); assert.equal(Number(u.unit_price), 77.7); assert.equal(u.pack.price, undefined); assert.equal(u.pack.size, sqm.pack.size);
  assert.equal(await hist(plain.id) - h0, 1, 'un singur rând nou de istoric');
  // o corectură manuală mai nouă nu e suprascrisă de o reverificare mai veche
  await updatePrice({ kind: 'm', targetId: plain.id, price: 15, verifiedAt: '2026-12-05' });
  assert.equal(await applyPriceRechecks(q, [{ ...items[0]!, price: 11, seenAt: '2026-12-01' }]), 0);
  assert.equal(Number((await row('select unit_price from materials where id=$1', plain.id)).unit_price), 15);
});

test('fișierul de reverificări: fiecare intrare are ID existent, preț pozitiv, dată, link https pe magazinul produsului și dovadă', () => {
  const mats = new Map((materialsJson as any).materials.map((m: any) => [m.id, m])), offers = new Map((catalogJson as any).offers.map((o: any) => [o.id, o]));
  for (const r of PRICE_RECHECKS){
    const src = r.kind === 'm' ? (mats.get(r.id) as any)?.sourceUrl : (offers.get(r.id) as any)?.provenance?.sourceUrl;
    assert.ok(src, `${r.kind}:${r.id} există în catalog`); assert.match(r.url, /^https:\/\//);
    assert.equal(new URL(r.url).hostname.replace(/^www\./, ''), new URL(src).hostname.replace(/^www\./, ''), `${r.id}: același magazin`);
    assert.ok(r.price > 0 && r.evidence.length > 3, r.id); assert.match(r.seenAt, /^\d{4}-\d{2}-\d{2}$/); }
});
