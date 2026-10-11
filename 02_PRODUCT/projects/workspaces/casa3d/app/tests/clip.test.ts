// Produs din link: doar magazine permise prin https, fără adrese interne; datele din JSON-LD sau OpenGraph, curățate.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { shopOf, isPrivateIp, parseProductPage } from '../core/clip';

test('doar magazine permise, prin https, fără trucuri în adresă', () => {
  for (const ok of ['https://www.dedeman.ro/ro/p/4026660', 'https://www.ikea.com/ro/ro/p/virrmo-70430780/', 'https://www.emag.ro/x/pd/ABC/']) assert.ok(shopOf(ok), ok);
  for (const bad of ['http://www.dedeman.ro/p/1', 'https://dedeman.ro.evil.com/p', 'https://evildedeman.ro/p', 'https://user:pw@www.dedeman.ro/', 'https://www.dedeman.ro:8443/', 'javascript:alert(1)', 'file:///etc/passwd', 'https://127.0.0.1/', 'https://localhost/', 'nu e url', 42])
    assert.equal(shopOf(bad), null, String(bad));
});
test('adresele interne sunt recunoscute', () => {
  for (const ip of ['127.0.0.1', '10.1.2.3', '192.168.1.10', '172.20.0.5', '169.254.169.254', '100.64.0.1', '0.0.0.0', '::1', 'fd00::1', 'fe80::1', '::ffff:127.0.0.1', '224.0.0.1']) assert.equal(isPrivateIp(ip), true, ip);
  for (const ip of ['104.18.2.3', '2a00:1450:4001::1', '8.8.8.8']) assert.equal(isPrivateIp(ip), false, ip);
});
test('JSON-LD Product (și în @graph), OpenGraph ca rezervă, text curățat', () => {
  const ld = `<html><head><title>x</title><script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"BreadcrumbList"},{"@type":"Product","name":"Gresie McKinley 60x60 &amp; alb‮","sku":"4026660","brand":{"@type":"Brand","name":"Cesarom"},"image":["/img/p.jpg"],"offers":{"@type":"Offer","price":"69.90","priceCurrency":"ron"}}]}</script></head></html>`;
  const p = parseProductPage(ld, 'https://www.dedeman.ro/ro/p/4026660', 'dedeman.ro')!;
  assert.deepEqual(p, { name: 'Gresie McKinley 60x60 & alb', price: 69.9, currency: 'RON', image: 'https://www.dedeman.ro/img/p.jpg', sku: '4026660', brand: 'Cesarom', supplier: 'Dedeman', url: 'https://www.dedeman.ro/ro/p/4026660' });
  const og = `<meta property="og:title" content="VIRRMO Plafonieră LED"><meta property="og:image" content="https://www.ikea.com/x.jpg"><meta property="product:price:amount" content="1.299,00">`;
  const q = parseProductPage(og, 'https://www.ikea.com/ro/ro/p/virrmo-70430780/', 'ikea.com')!; assert.equal(q.name, 'VIRRMO Plafonieră LED'); assert.equal(q.price, 1299); assert.equal(q.supplier, 'IKEA');
  const js = parseProductPage(`<script type="application/ld+json">{"@type":"Product","name":"x","image":"javascript:alert(1)"}</script>`, 'https://www.emag.ro/a', 'emag.ro')!; assert.equal(js.image, null, 'doar imagini https');
  assert.equal(parseProductPage('<html></html>', 'https://www.emag.ro/a', 'emag.ro'), null);
  assert.equal(parseProductPage('<script type="application/ld+json">{stricat</script><title>Canapea</title>', 'https://www.emag.ro/a', 'emag.ro')!.name, 'Canapea', 'JSON stricat nu oprește citirea');
});

test('adrese private în orice scriere (review de securitate)', async () => {
  const { isPrivateIp } = await import('../core/clip');
  for (const ip of ['127.0.0.1', '10.0.0.5', '192.168.1.1', '169.254.169.254', '100.64.0.1', '0.0.0.0', '::', '::1', '::ffff:127.0.0.1', '::ffff:7f00:1', '::127.0.0.1', '::7f00:1', '::ffff:0:7f00:1',
    '2002:7f00:1::', '2002:a00:1::1', 'fec0::1', 'fe80::1%eth0', 'fd00::1', 'fc00::1', 'ff02::1', '64:ff9b::7f00:1', '64:ff9b::808:808', '2001:db8::1', 'nu-e-ip', '1::2::3', '300.1.1.1'])
    assert.equal(isPrivateIp(ip), true, ip);
  for (const ip of ['93.184.216.34', '8.8.8.8', '2606:4700::6810:85e5', '::ffff:93.184.216.34', '2002:5db8:d822::1', '2a00:1450:4001:80b::200e']) assert.equal(isPrivateIp(ip), false, ip);
});
