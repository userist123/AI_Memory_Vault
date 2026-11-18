// Reverificarea periodică a prețurilor observate (link + preț observat, fără acorduri cu magazinele).
//   npm run prices:recheck                     → raport în research/price-recheck-<data>.json (nu schimbă datele)
//   npm run prices:recheck -- --apply          → și scrie citirile sigure în data/price-rechecks.json, iar ofertele a căror
//                                                pagină a dispărut în data/offers-gone.json (ambele aplicate la pornire)
//   npm run prices:recheck -- --from <raport.json> --apply → aplică un raport deja citit, fără cereri noi
// Opțiuni: --days N (implicit 30: prețuri mai vechi de N zile), --store dedeman,ikea, --limit N, --out <fișier>.
// Reguli: o cerere la 3–5 s, fără ocolirea protecțiilor (403/429 → „manual”, nu se reîncearcă), doar pagini de produs,
// prețul doar din date legate de produs (scripts/lib/price-extract.mjs). Fără imagini sau descrieri copiate.
import fs from 'node:fs';
import path from 'node:path';
import { priceFromHtml, isProductUrl, classify, staleItems, mergeRechecks, mergeGone } from './lib/price-extract.mjs';

const app = path.resolve(import.meta.dirname, '..');
const args = process.argv.slice(2), arg = k => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : null; };
const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Bucharest' }).format(new Date());
const days = Number(arg('--days') ?? 30), limit = Number(arg('--limit') ?? Infinity), stores = (arg('--store') ?? '').split(',').filter(Boolean);
const cutoff = new Date(Date.parse(today + 'T00:00:00Z') - days * 864e5).toISOString().slice(0, 10);
const read = f => JSON.parse(fs.readFileSync(path.join(app, f), 'utf8'));
const rcFile = path.join(app, 'data/price-rechecks.json'), rechecks = read('data/price-rechecks.json');
const todo = staleItems(read('data/materials.v1.json'), read('data/catalog.v1.json'), rechecks, cutoff)
  .filter(i => !stores.length || stores.some(s => i.store.includes(s))).slice(0, limit);
const out = arg('--out') ?? path.join(app, `research/price-recheck-${today}.json`);
fs.mkdirSync(path.dirname(out), { recursive: true });
if (!arg('--from')) console.log(`De reverificat: ${todo.length} prețuri citite înainte de ${cutoff}.`);

const from = arg('--from');
let res = [];
if (from){ res = JSON.parse(fs.readFileSync(from, 'utf8')); console.log(`Raport existent: ${res.length} citiri (${from}).`); }
else {
const UA = { 'user-agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36', 'accept-language': 'ro-RO,ro;q=0.9' };
for (const [k, it] of todo.entries()){
  let r;
  if (!isProductUrl(it.url)) r = { status: 'manual', why: 'linkul nu e pagina unui produs' };
  else try {
    const resp = await fetch(it.url, { headers: UA, redirect: 'follow', signal: AbortSignal.timeout(25000) });
    if (resp.status === 404 || resp.status === 410) r = { status: 'gone', http: resp.status, why: 'pagina nu mai există' };
    else if (!resp.ok) r = { status: 'manual', http: resp.status, why: `răspuns ${resp.status} (magazinul nu permite citirea automată)` };
    else if (resp.url !== it.url && !isProductUrl(resp.url)) r = { status: 'gone', http: resp.status, finalUrl: resp.url, why: 'pagina produsului trimite acum la o categorie (scos din vânzare?)' };
    else r = { http: resp.status, ...classify(it, priceFromHtml(await resp.text(), it.url, resp.url)), ...(resp.url !== it.url ? { finalUrl: resp.url } : {}) };
  } catch (e) { r = { status: 'manual', why: String(e?.name || e).slice(0, 60) }; }
  res.push({ ...it, ...r, readAt: today });
  console.log(`${k + 1}/${todo.length} ${r.status.padEnd(7)} ${it.store.padEnd(10)} ${it.id}`);
  fs.writeFileSync(out, JSON.stringify(res, null, 1));
  if (k < todo.length - 1) await new Promise(z => setTimeout(z, 3000 + Math.random() * 2000));
}
}
const readAt = res[0]?.readAt ?? today;
const sum = res.reduce((o, r) => (o[r.status] = (o[r.status] || 0) + 1, o), {});
console.log('Rezumat:', JSON.stringify(sum), '→', path.relative(app, out));
if (args.includes('--apply')){
  const merged = mergeRechecks(rechecks, res, readAt), { applied, ...data } = merged;
  fs.writeFileSync(rcFile, JSON.stringify(data, null, 1) + '\n');
  const goneFile = path.join(app, 'data/offers-gone.json'), gone = mergeGone(read('data/offers-gone.json'), res, readAt);
  fs.writeFileSync(goneFile, JSON.stringify(gone, null, 1) + '\n');
  console.log(`Oferte scoase din vânzare (data/offers-gone.json): ${gone.items.length}.`);
  console.log(`Scrise în data/price-rechecks.json: ${applied} citiri sigure (se aplică la următoarea pornire a serverului).`);
}
