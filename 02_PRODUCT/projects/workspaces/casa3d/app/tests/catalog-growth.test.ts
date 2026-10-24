// Catalogul extins (produse verificate pe 2026-10-10): date valide, linkuri spre magazinul declarat, semănare incrementală în baze existente.
import { test } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogJson from '../data/catalog.v1.json';
import evidence from '../data/catalog-evidence.2026-10-10.json';
import { groups, toEngineCatalog } from '../core/catalog';
import { RETAILER_HOSTS, isAllowedUrl } from '../core/outbound';
import { FRONT_CLEARANCE } from '../core/rules';
import { getDb, resetDbForTests, reseedForTests } from '../lib/db';
import type { Catalog } from '../core/types';

const cat = catalogJson as unknown as Catalog;

test('fiecare variantă are dimensiuni plauzibile, o ofertă cu preț și un link https spre magazinul furnizorului', () => {
  assert.ok(cat.variants.length >= 200, `${cat.variants.length} variante`);
  const ids = new Set<string>();
  for (const v of cat.variants){ assert.ok(!ids.has(v.id), v.id); ids.add(v.id);
    assert.ok(cat.products.some(p => p.id === v.productId), v.id);
    if (v.dimensionsCm) for (const k of ['w', 'd', 'h'] as const) assert.ok(v.dimensionsCm[k] > 0 && v.dimensionsCm[k] < 400, `${v.id}.${k}`); }
  const urls = new Set<string>();
  for (const o of cat.offers){ assert.ok(ids.has(o.variantId), o.id); assert.ok(o.price > 0 && Number.isFinite(o.price), o.id);
    const u = o.provenance.sourceUrl; if (!u) continue;
    // fiecare pagină de produs verificată acum aparține unei singure oferte (cele vechi, ca bucătăria ENHET, acoperă și electrocasnicele incluse)
    if (o.provenance.verifiedAt === '2026-10-10'){ assert.ok(!urls.has(u), `URL repetat: ${u}`); urls.add(u); }
    assert.ok(RETAILER_HOSTS[o.supplierId]!.includes(new URL(u).hostname), `${o.id}: ${u} nu e pe ${o.supplierId}`);
    assert.ok(isAllowedUrl(u, 'direct', null, o.supplierId), o.id); }
  // fiecare ofertă verificată pe 2026-10-10 are dovada citită pe pagina produsului
  const ev = new Set(evidence.items.map(x => x.url));
  for (const o of cat.offers) if (o.provenance.verifiedAt === '2026-10-10') assert.ok(ev.has(o.provenance.sourceUrl!), o.id);
});
test('grupele noi (fotoliu, comodă) au model 3D, etichetă și spațiu liber în față; magazinele noi au furnizor', () => {
  const g = groups(cat), eng = toEngineCatalog(cat);
  for (const k of ['fotoliu', 'comoda']){ assert.ok(g[k]!.variants.length >= 5, k); assert.ok(eng[k]!.model, k); assert.ok(FRONT_CLEARANCE[k]! > 0, k); }
  for (const s of ['jysk-ro', 'mobexpert']) assert.ok(cat.suppliers.some(x => x.id === s) && cat.offers.some(o => o.supplierId === s), s);
  for (const [k, grp] of Object.entries(g)) grp.variants.forEach((v, i) => assert.equal(v.legacyIndex, i, `${k}: indexuri consecutive`));
});
test('repornirea pe o bază existentă adaugă produsele lipsă și prețurile mai noi, fără dubluri', async () => {
  resetDbForTests(); const { q } = await getDb();
  const count = async (t: string) => (await q(`select count(*)::int as n from ${t}`)).rows[0].n as number;
  const before = { offers: await count('offers'), links: await count('offer_links'), hist: await count('price_history'), ret: await count('retailers') };
  assert.equal(before.offers, cat.offers.length); assert.equal(before.ret, cat.suppliers.length);
  // o bază veche: fără ofertele noi și cu un preț mai vechi
  await q("delete from offers where verified_at = '2026-10-10' and id <> 'offer-canapea-0'");
  await q("update offers set price = 2299, verified_at = '2026-09-30' where id = 'offer-canapea-0'");
  await reseedForTests(); await reseedForTests();
  assert.equal(await count('offers'), before.offers); assert.equal(await count('offer_links'), before.links); assert.equal(await count('price_history'), before.hist);
  const o = (await q("select price, verified_at from offers where id = 'offer-canapea-0'")).rows[0];
  assert.equal(Number(o.price), cat.offers.find(x => x.id === 'offer-canapea-0')!.price);
  // un preț pus manual după ultima verificare din catalog rămâne
  await q("update offers set price = 1500, verified_at = '2026-10-11' where id = 'offer-canapea-0'"); await reseedForTests();
  assert.equal(Number((await q("select price from offers where id = 'offer-canapea-0'")).rows[0].price), 1500);
});
test('baia completă (2026-10-11): cadă, bideu, walk-in, calorifer, coloană, dulap cu oglindă, boiler — cu model 3D, simbol, preț citit pe pagină', async () => {
  const { planSymbol } = await import('../core/plan-symbols');
  const { FRONT_CLEARANCE, WALL_HIGH } = await import('../core/rules');
  const ev11 = (await import('../data/catalog-evidence.2026-10-11.json')).default as { items: { url: string; price: number }[] };
  const g = groups(cat), eng = toEngineCatalog(cat);
  for (const k of ['cada', 'bideu', 'caloriferBaie', 'coloanaBaie', 'dulapOglinda', 'boiler']){ assert.ok(g[k]!.variants.length >= 1, k); assert.ok(eng[k]!.model, k); assert.ok(FRONT_CLEARANCE[k] !== undefined, k);
    const v = g[k]!.variants[0]!.dimensionsCm!; assert.ok(planSymbol(k, v.w / 100, v.d / 100).detail || planSymbol(k, v.w / 100, v.d / 100).outline, k); }
  assert.ok(g['dus']!.variants.some(v => v.style?.walkin), 'perete walk-in în catalog');
  // fiecare WC are montajul, rezervorul și forma vasului scrise pe produs (nu dintr-o setare a băii)
  for (const v of g['wc']!.variants) assert.ok(['wall', 'floor'].includes(v.style.mount) && ['concealed', 'ceramic'].includes(v.style.cistern) && ['oval', 'square'].includes(v.style.shape), v.id);
  // ofertele din 2026-10-11 au prețul citit pe pagina produsului
  const byUrl = new Map(ev11.items.map(x => [x.url, x.price]));
  for (const o of cat.offers) if (o.provenance.verifiedAt === '2026-10-11') assert.equal(byUrl.get(o.provenance.sourceUrl!), o.price, o.id);
  assert.ok(WALL_HIGH.has('dulapOglinda') && WALL_HIGH.has('boiler'));
});
test('decor (2026-10-24): plante, lampadar, veioză, șemineu electric, focar pe lemne — model 3D, simbol, cameră, preț citit pe pagină', async () => {
  const { planSymbol } = await import('../core/plan-symbols');
  const { ON_SURFACE, NEEDS_CHIMNEY } = await import('../core/rules');
  const { groupsForRoom } = await import('../core/furniture-fit');
  const { validatePlacement, surfaceUnder } = await import('../core/validate');
  const ev24 = (await import('../data/catalog-evidence.2026-10-24.json')).default as { items: { url: string; price: number; dimensionsCm: { w: number; d: number; h: number } | null }[] };
  const g = groups(cat), eng = toEngineCatalog(cat), keys = Object.keys(g);
  const NEW = { plantaArtificiala: 'plant', plantaNaturala: 'plant', lampadar: 'floorlamp', veioza: 'tablelamp', semineu: 'fireplace', focarLemne: 'stove' } as const;
  for (const [k, model] of Object.entries(NEW)){ assert.ok(g[k]!.variants.length >= 2, k); assert.equal(eng[k]!.model, model, k); assert.ok(FRONT_CLEARANCE[k] !== undefined, k);
    for (const v of g[k]!.variants){ const dm = v.dimensionsCm!; assert.ok(dm && dm.w > 0 && dm.d > 0 && dm.h > 0, v.id); const s = planSymbol(k, dm.w / 100, dm.d / 100); assert.ok(s.outline && s.detail, v.id); }
    // fiecare grup nou e propus în cel puțin o cameră
    assert.ok(['living', 'dormitor', 'birou', 'hol', 'bucatarie'].some(r => groupsForRoom(r, keys).suggested.includes(k)), k); }
  // șemineul și focarul nu se propun în baie și nici în bucătărie
  for (const k of ['semineu', 'focarLemne']) for (const r of ['baie', 'bucatarie']){ const f = groupsForRoom(r, keys); assert.ok(!f.suggested.includes(k) && !f.others.includes(k), `${k} în ${r}`); }
  assert.ok(FRONT_CLEARANCE.focarLemne! > 0 && FRONT_CLEARANCE.semineu! > 0);
  // fiecare ofertă din 2026-10-24 are dovada ei: același URL, același preț, aceleași dimensiuni declarate; URL-urile sunt unice
  const byUrl = new Map(ev24.items.map(x => [x.url, x])), seen = new Set<string>();
  const offers24 = cat.offers.filter(o => o.provenance.verifiedAt === '2026-10-24'); assert.equal(offers24.length, ev24.items.length);
  for (const o of offers24){ const u = o.provenance.sourceUrl!, e = byUrl.get(u); assert.ok(e, o.id); assert.equal(e!.price, o.price, o.id); assert.ok(!seen.has(u), u); seen.add(u);
    assert.equal(o.provenance.verificationType, 'product_page', o.id); assert.deepEqual(cat.variants.find(v => v.id === o.variantId)!.dimensionsCm, e!.dimensionsCm, o.id);
    assert.ok(Object.keys(NEW).includes(o.variantId.slice(0, o.variantId.lastIndexOf('-'))), o.id); }
  // focarul pe lemne primește avertismentul de coș de fum; veioza pusă pe noptieră stă pe ea (fără suprapunere, ridicată în 3D)
  assert.ok(NEEDS_CHIMNEY.has('focarLemne') && ON_SURFACE.has('veioza'));
  const snap = { floor: { id: 'f', name: 'P', ceilingHeight: 2.6, walls: [], rooms: [{ id: 'r', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 5, z1: 4 } }] },
    placements: [{ id: 'f1', roomId: 'r', group: 'focarLemne', variantId: 'focarLemne-0', x: 2.5, z: .5, rotation: 0, source: 'manual' },
      { id: 'n1', roomId: 'r', group: 'noptiera', variantId: 'noptiera-0', x: 1, z: 3, rotation: 0, source: 'manual' },
      { id: 'v1', roomId: 'r', group: 'veioza', variantId: 'veioza-0', x: 1, z: 3, rotation: 0, source: 'manual' }], selections: {}, picked: [], name: 't' } as any;
  assert.ok(validatePlacement(snap, cat, snap.placements[0]).some((i: any) => i.code === 'CHIMNEY' && i.severity === 'WARNING' && i.key === 'issue.CHIMNEY'));
  assert.ok(!validatePlacement(snap, cat, snap.placements[2]).some((i: any) => i.code === 'OVERLAP'));
  const nh = cat.variants.find(v => v.id === 'noptiera-0')!.dimensionsCm!.h / 100; assert.equal(surfaceUnder(snap, cat, snap.placements[2]), nh);
  assert.equal(surfaceUnder(snap, cat, { ...snap.placements[2], x: 4, z: 3 }), 0);
});
