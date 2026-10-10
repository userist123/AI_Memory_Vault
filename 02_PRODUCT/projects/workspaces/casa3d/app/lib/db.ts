// Acces la baza de date: PostgreSQL real când există DATABASE_URL, altfel PGlite (Postgres în proces).
import { SCHEMA } from './schema';
import catalogSeed from '../data/catalog.v1.json';
import materialsSeed from '../data/materials.v1.json';
export type Query = (sql: string, params?: unknown[]) => Promise<{ rows: any[] }>;
export type DbMode = 'postgres' | 'pglite-file' | 'pglite-memory';
/** Rulează `fn` într-o tranzacție: orice excepție face ROLLBACK și e rearuncată neschimbată. */
export type Tx = <T>(fn: (q: Query) => Promise<T>) => Promise<T>;
type Db = { q: Query; tx: Tx; mode: DbMode };
let ready: Promise<Db> | null = null;

async function connect(): Promise<Db> {
  if (process.env.DATABASE_URL){ const { Pool } = await import('pg'); const pool = new Pool({ connectionString: process.env.DATABASE_URL, max: 5, ssl: process.env.DATABASE_URL.includes('localhost') ? undefined : { rejectUnauthorized: false } });
    const tx: Tx = async fn => { const c = await pool.connect();
      try { await c.query('begin'); const out = await fn((s, p) => c.query(s, p as any[])); await c.query('commit'); return out; }
      catch (e){ await c.query('rollback').catch(() => {}); throw e; }
      finally { c.release(); } };
    return { q: (s, p) => pool.query(s, p as any[]), tx, mode: 'postgres' }; }
  const { PGlite } = await import('@electric-sql/pglite');
  const memory = !!process.env.VERCEL || process.env.PGLITE_MEMORY === '1';
  const dir = process.env.PGLITE_DIR || './.data/pglite';
  // PGlite creează doar ultimul director: pe o copie curată ./.data lipsește încă.
  if (!memory && !dir.includes('://')){ const { mkdirSync } = await import('node:fs'); mkdirSync(dir, { recursive: true }); }
  const db = memory ? new PGlite() : new PGlite(dir);
  // PGlite ține tranzacția exclusiv: celelalte interogări așteaptă până la commit/rollback.
  const tx: Tx = fn => db.transaction(t => fn((s, p) => t.query(s, p as any[]) as any));
  return { q: (s, p) => db.query(s, p as any[]) as any, tx, mode: memory ? 'pglite-memory' : 'pglite-file' };
}
// Catalogul se completează la fiecare pornire, nu doar pe o bază goală: produsele noi din JSON ajung și în bazele existente,
// iar un preț verificat mai recent decât cel din bază (inclusiv unul pus manual) îl înlocuiește și intră în istoric.
async function seedCatalog(q: Query){
  const c = catalogSeed as any;
  for (const s of c.suppliers) await q('insert into suppliers values($1,$2,$3,$4) on conflict do nothing', [s.id, s.name, s.country, s.website]);
  for (const p of c.products) await q('insert into products values($1,$2,$3,$4,$5,$6) on conflict do nothing', [p.id, p.group, p.name, p.brand, p.category, p.model3d]);
  for (const v of c.variants) await q('insert into variants values($1,$2,$3,$4,$5,$6,$7,$8,$9) on conflict do nothing', [v.id, v.productId, v.name, v.legacyIndex, v.dimensionsCm ? JSON.stringify(v.dimensionsCm) : null, v.dimensionsConfidence, JSON.stringify(v.style || {}), v.chairs ?? null, v.includedWith ?? null]);
  const have = new Map((await q('select id, verified_at from offers')).rows.map((r: any) => [r.id, r.verified_at ? new Date(r.verified_at).toISOString().slice(0, 10) : '']));
  for (const o of c.offers){ const pv = o.provenance, at = have.get(o.id);
    if (at === undefined) await q('insert into offers values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12) on conflict do nothing', [o.id, o.variantId, o.supplierId, o.price, o.currency, o.availability, o.affiliateUrl, pv.source, pv.sourceUrl, pv.verifiedAt, pv.verificationType, pv.confidence]);
    else if (pv.verifiedAt && pv.verifiedAt > at) await q('update offers set price=$1, verified_at=$2, verification_type=$3, confidence=$4, source_url=$5 where id=$6', [o.price, pv.verifiedAt, pv.verificationType, pv.confidence, pv.sourceUrl, o.id]); }
}
async function seedOutbound(q: Query){
  // retaileri (unul pentru fiecare furnizor) + linkuri directe (fără afiliere în modul de testare) + istoricul prețurilor;
  // fiecare pas adaugă doar ce lipsește, ca pornirile repetate să nu dubleze nimic.
  const c = catalogSeed as any, m = materialsSeed as any, slug = (s: string) => s === 'IKEA' ? 'ikea-ro' : 'dedeman';
  const names: Record<string, string> = { 'ikea-ro': 'IKEA România' };
  for (const s of c.suppliers) await q('insert into retailers(slug, name, website) values($1,$2,$3) on conflict do nothing', [s.id, names[s.id] ?? s.name, s.website]);
  const linked = new Set((await q("select target_kind || ':' || target_id as k from offer_links")).rows.map((r: any) => r.k));
  for (const o of c.offers) if (o.provenance.sourceUrl && !linked.has('o:' + o.id)) await q("insert into offer_links(target_kind, target_id, retailer, type, url) values('o',$1,$2,'direct',$3)", [o.id, o.supplierId, o.provenance.sourceUrl]);
  for (const x of m.materials) if (x.sourceUrl && !linked.has('m:' + x.id)) await q("insert into offer_links(target_kind, target_id, retailer, type, url) values('m',$1,$2,'direct',$3)", [x.id, slug(x.supplier), x.sourceUrl]);
  const key = (k: string, id: string, price: number, at: string) => `${k}:${id}:${Number(price).toFixed(2)}:${at}`;
  const seen = new Set((await q('select target_kind, target_id, price, verified_at from price_history')).rows.map((r: any) => key(r.target_kind, r.target_id, r.price, new Date(r.verified_at).toISOString().slice(0, 10))));
  const hist = async (k: string, id: string, price: number, at: string) => { if (seen.has(key(k, id, price, at))) return; seen.add(key(k, id, price, at)); await q('insert into price_history(target_kind, target_id, price, verified_at) values($1,$2,$3,$4)', [k, id, price, at]); };
  for (const o of c.offers) await hist('o', o.id, o.price, o.provenance.verifiedAt);
  for (const x of m.materials) await hist('m', x.id, x.pack?.price ?? x.unitPrice, m.verifiedAt);
}
async function migrate(q: Query){
  for (const stmt of SCHEMA.split(';').map(s => s.trim()).filter(Boolean)) await q(stmt);
  const m = materialsSeed as any, mcount = (await q('select count(*)::int as n from materials')).rows[0].n;
  if (mcount === 0){
    for (const x of m.materials) await q('insert into materials values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14) on conflict do nothing', [x.id, x.category, x.name, x.supplier, x.unit, x.unitPrice, x.pack ? JSON.stringify(x.pack) : null, x.coverage ?? null, x.consumption ?? null, x.sourceUrl, m.verifiedAt, x.verificationType, x.confidence, x.note ?? null]);
    for (const x of m.labor) await q('insert into labor_rates values($1,$2,$3,$4,$5,$6,$7,$8,$9) on conflict do nothing', [x.id, x.label, x.unit, x.low, x.expected, x.high, JSON.stringify(x.sources), x.confidence, m.verifiedAt]);
    for (const x of m.services) await q('insert into services values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) on conflict do nothing', [x.id, x.label, x.supplier, x.price ?? null, x.pricePerMeter ?? null, x.sourceUrl, x.verificationType, x.confidence, x.note ?? null, m.verifiedAt]);
  }
  await seedCatalog(q);
  await seedOutbound(q);
}
export async function getDb(){ if (!ready) ready = (async () => { const c = await connect(); await migrate(c.q); return c; })(); return ready; }
export function resetDbForTests(){ ready = null; }
export async function reseedForTests(){ await migrate((await getDb()).q); }
