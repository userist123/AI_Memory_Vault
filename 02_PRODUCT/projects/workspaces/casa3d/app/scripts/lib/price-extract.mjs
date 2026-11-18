// Citirea prețului observat de pe pagina unui produs (decizia proprietarului, 2026-11-17: link + preț observat, fără acorduri cu
// magazinele, fără ocolirea protecțiilor anti-bot). Prețul se acceptă DOAR dintr-o sursă legată de produs:
//   - Dedeman: prețul final din cutia de preț a codului din adresă (…/p/<cod>); pagina conține și prețurile produselor recomandate;
//   - altfel: JSON-LD Product.offers.price în RON (IKEA, Hornbach, JYSK, Mobexpert).
// Un preț din textul paginii nu se acceptă (pe o pagină de categorie ar fi prețul altui produs). Fără sursă clară → null.

const num = s => Number(String(s).replace(/\s/g, '').replace(/\.(?=\d{3}(\D|$))/g, '').replace(',', '.'));

export function priceFromHtml(html, url){
  const code = /dedeman\.ro\/.*\/p\/(\d+)/.exec(url)?.[1];
  if (code){
    const i = html.indexOf(`"finalPrice" data-product-id="${code}"`), j = i >= 0 ? html.lastIndexOf('data-price-box="product-id-', i) : -1;
    if (i < 0) return null;
    const box = html.slice(Math.max(j, i - 1500), i + 1200);
    const f = /data-price-amount="([\d.]+)"\s+data-price-type="finalPrice"/.exec(box) ?? /data-price-type="finalPrice"[^>]*data-price-amount="([\d.]+)"/.exec(box);
    if (!f) return null;
    const unit = /price-sale-unit">\s*\/\s*([^<\s]+)/.exec(html.slice(i, i + 900))?.[1] ?? null;
    return { price: Number(f[1]), unit, how: 'dedeman-finalPrice',
      evidence: `cod ${code}: finalPrice ${f[1]} lei${unit ? ' /' + unit : ''}${/oldPrice/.test(box) ? ' (pagina arată și un preț vechi: promoție)' : ''}` };
  }
  for (const mm of html.matchAll(/<script[^>]+application\/ld\+json[^>]*>([\s\S]*?)<\/script>/gi)){
    let j; try { j = JSON.parse(mm[1]); } catch { continue; }
    for (const n of (Array.isArray(j) ? j : j['@graph'] ?? [j])){
      if (![].concat(n?.['@type'] || []).includes('Product')) continue;
      for (const o of [].concat(n.offers || [])){
        if (o.price != null || o.lowPrice != null){ const p = o.price ?? o.lowPrice;
          if (/^RON$/i.test(o.priceCurrency || 'RON') && num(p) > 0) return { price: num(p), unit: null, how: 'json-ld', evidence: `offers.price=${p} ${o.priceCurrency || ''}`.trim() + (o.seller?.name ? ` (vânzător: ${String(o.seller.name).slice(0, 60)})` : '') }; continue; }
        // IKEA: priceSpecification e o listă — prețul curent și, la promoții, prețul vechi tăiat (StrikethroughPrice), care nu se ia
        const specs = [].concat(o.priceSpecification || []), cur = specs.filter(s => s?.price != null && !/Strikethrough|ListPrice/i.test(s.priceType || '') && /^RON$/i.test(s.priceCurrency || 'RON'));
        if (cur.length === 1 && num(cur[0].price) > 0) return { price: num(cur[0].price), unit: null, how: 'json-ld',
          evidence: `offers.priceSpecification.price=${cur[0].price} RON${specs.length > 1 ? ' (pagina arată și un preț vechi: promoție)' : ''}` };
      }
    }
  }
  return null;
}

// Aceeași regulă ca isProductPage din core/commercial.ts (testul o compară): pagina unui produs, nu o categorie, doar https.
export function isProductUrl(url){
  let p; try { p = new URL(url); } catch { return false; }
  if (p.protocol !== 'https:') return false;
  const path = p.pathname.toLowerCase();
  if (/\/p\/|\/pd\//.test(path)) return true;
  if (/\/(cat|c|category|categorie|categorii)\/|\/c\/\d+\/?$/.test(path)) return false;
  return path.split('/').filter(Boolean).length >= 1;
}

const norm = u => String(u || '').toLowerCase().replace('²', '2');

// Rezultatul unei citiri, față de prețul curent. La materialele vândute la pachet, pagina poate da prețul pe unitate (m², ml):
// atunci „per” = unit și se compară cu prețul unitar; altfel cu prețul pachetului.
export function classify(item, page){
  if (!page || !(page.price > 0)) return { status: 'manual', why: 'preț negăsit clar în pagină' };
  const perUnit = item.pack != null && !!page.unit && norm(page.unit) === norm(item.unit);
  const compared = item.pack != null && !perUnit ? item.pack : item.old;
  return { status: Math.abs(page.price - compared) < 0.005 ? 'same' : 'changed', price: page.price, compared,
    ...(item.pack != null ? { per: perUnit ? 'unit' : 'pack' } : {}), how: page.how, evidence: page.evidence };
}

// Ce trebuie reverificat: prețul efectiv (baza + reverificările deja salvate, aplicate ca în lib/price-rechecks.ts) mai vechi
// decât `cutoff` (AAAA-LL-ZZ). La pachet: per=unit scoate prețul pachetului; per=pack îl înlocuiește.
export function staleItems(materials, catalog, rechecks, cutoff){
  const rk = new Map((rechecks.items || []).map(r => [`${r.kind}:${r.id}`, r]));
  const out = [];
  for (const x of materials.materials){
    const r = rk.get(`m:${x.id}`), seenAt = r?.seenAt ?? x.verifiedAt ?? materials.verifiedAt, url = r?.url ?? x.sourceUrl;
    if (!url || !(seenAt < cutoff)) continue;
    let old = Number(x.unitPrice), pack = x.pack?.price ?? null;
    if (r && x.pack && r.per === 'unit'){ old = r.price; pack = null; }
    else if (r && pack != null){ pack = r.price; old = Math.round(r.price / x.pack.size * 100) / 100; }
    else if (r) old = r.price;
    out.push({ kind: 'm', id: x.id, url, old, pack, unit: x.unit, seenAt, store: String(x.supplier || '').toLowerCase() });
  }
  for (const o of catalog.offers){
    const r = rk.get(`o:${o.id}`), seenAt = r?.seenAt ?? o.provenance?.verifiedAt, url = r?.url ?? o.provenance?.sourceUrl;
    if (!url || !seenAt || !(seenAt < cutoff)) continue;
    out.push({ kind: 'o', id: o.id, url, old: Number(r?.price ?? o.price), pack: null, seenAt, store: String(o.supplierId || '').replace(/-ro$/, '') });
  }
  return out;
}

// Intră în fișierul de reverificări doar citirile sigure; o citire nouă înlocuiește intrarea veche a aceluiași produs.
// Linkul rămâne cel citit (pentru oferte, aplicarea la pornire actualizează și sursa ofertei).
export function mergeRechecks(rechecks, results, readAt){
  const ok = results.filter(r => (r.status === 'same' || r.status === 'changed') && (r.how === 'json-ld' || r.how === 'dedeman-finalPrice') && r.price > 0);
  const ids = new Set(ok.map(r => `${r.kind}:${r.id}`));
  const items = (rechecks.items || []).filter(i => !ids.has(`${i.kind}:${i.id}`))
    .concat(ok.map(r => ({ kind: r.kind, id: r.id, price: r.price, seenAt: readAt, url: r.url, evidence: r.evidence, ...(r.per ? { per: r.per } : {}) })));
  const notVerified = (rechecks.notVerified || []).filter(i => !ids.has(`${i.kind === 'material' ? 'm' : i.kind === 'offer' ? 'o' : i.kind}:${i.id}`));
  for (const r of results) if (!ok.includes(r) && !(r.kind === 'o' && r.status === 'gone')) {
    const k = r.kind === 'm' ? 'material' : 'offer';
    if (!notVerified.some(n => n.kind === k && n.id === r.id)) notVerified.push({ kind: k, id: r.id, url: r.url, status: r.status, why: r.why ?? (r.http ? String(r.http) : '') });
  }
  return { ...rechecks, items, notVerified, applied: ok.length };
}

// Ofertele a căror pagină a dispărut / trimite la o categorie; o ofertă citită din nou cu preț iese din listă.
export function mergeGone(gone, results, readAt){
  const read = new Set(results.filter(r => r.kind === 'o' && (r.status === 'same' || r.status === 'changed')).map(r => r.id));
  const now = results.filter(r => r.kind === 'o' && r.status === 'gone');
  const ids = new Set(now.map(r => r.id));
  const items = (gone.items || []).filter(g => !read.has(g.id) && !ids.has(g.id))
    .concat(now.map(r => ({ id: r.id, checkedAt: readAt, url: r.url, why: r.why ?? '' })));
  return { ...gone, items };
}
