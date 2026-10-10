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
  const db = memory ? new PGlite() : new PGlite(process.env.PGLITE_DIR || './.data/pglite');
  // PGlite ține tranzacția exclusiv: celelalte interogări așteaptă până la commit/rollback.
  const tx: Tx = fn => db.transaction(t => fn((s, p) => t.query(s, p as any[]) as any));
  return { q: (s, p) => db.query(s, p as any[]) as any, tx, mode: memory ? 'pglite-memory' : 'pglite-file' };
}
async function seedOutbound(q: Query){
  // retaileri + linkuri directe (fără afiliere în modul de testare); se rulează după catalog și materiale
  if ((await q('select count(*)::int as n from retailers')).rows[0].n === 0){
    await q("insert into retailers(slug, name, website) values('ikea-ro','IKEA România','https://www.ikea.com/ro/ro/'),('dedeman','Dedeman','https://www.dedeman.ro/') on conflict do nothing"); }
  if ((await q('select count(*)::int as n from offer_links')).rows[0].n > 0) return;
  const c = catalogSeed as any, m = materialsSeed as any, slug = (s: string) => s === 'IKEA' ? 'ikea-ro' : 'dedeman';
  for (const o of c.offers) if (o.provenance.sourceUrl) await q("insert into offer_links(target_kind, target_id, retailer, type, url) values('o',$1,$2,'direct',$3)", [o.id, o.supplierId === 'ikea-ro' ? 'ikea-ro' : 'dedeman', o.provenance.sourceUrl]);
  for (const x of m.materials) if (x.sourceUrl) await q("insert into offer_links(target_kind, target_id, retailer, type, url) values('m',$1,$2,'direct',$3)", [x.id, slug(x.supplier), x.sourceUrl]);
  for (const o of c.offers) await q("insert into price_history(target_kind, target_id, price, verified_at) values('o',$1,$2,$3)", [o.id, o.price, o.provenance.verifiedAt]);
  for (const x of m.materials) await q("insert into price_history(target_kind, target_id, price, verified_at) values('m',$1,$2,$3)", [x.id, x.pack?.price ?? x.unitPrice, m.verifiedAt]);
}
async function migrate(q: Query){
  for (const stmt of SCHEMA.split(';').map(s => s.trim()).filter(Boolean)) await q(stmt);
  const m = materialsSeed as any, mcount = (await q('select count(*)::int as n from materials')).rows[0].n;
  if (mcount === 0){
    for (const x of m.materials) await q('insert into materials values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14) on conflict do nothing', [x.id, x.category, x.name, x.supplier, x.unit, x.unitPrice, x.pack ? JSON.stringify(x.pack) : null, x.coverage ?? null, x.consumption ?? null, x.sourceUrl, m.verifiedAt, x.verificationType, x.confidence, x.note ?? null]);
    for (const x of m.labor) await q('insert into labor_rates values($1,$2,$3,$4,$5,$6,$7,$8,$9) on conflict do nothing', [x.id, x.label, x.unit, x.low, x.expected, x.high, JSON.stringify(x.sources), x.confidence, m.verifiedAt]);
    for (const x of m.services) await q('insert into services values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) on conflict do nothing', [x.id, x.label, x.supplier, x.price ?? null, x.pricePerMeter ?? null, x.sourceUrl, x.verificationType, x.confidence, x.note ?? null, m.verifiedAt]);
  }
  await seedOutbound(q);
  const { rows } = await q('select count(*)::int as n from offers'); if (rows[0].n > 0) return;
  const c = catalogSeed as any;
  for (const s of c.suppliers) await q('insert into suppliers values($1,$2,$3,$4) on conflict do nothing', [s.id, s.name, s.country, s.website]);
  for (const p of c.products) await q('insert into products values($1,$2,$3,$4,$5,$6) on conflict do nothing', [p.id, p.group, p.name, p.brand, p.category, p.model3d]);
  for (const v of c.variants) await q('insert into variants values($1,$2,$3,$4,$5,$6,$7,$8,$9) on conflict do nothing', [v.id, v.productId, v.name, v.legacyIndex, v.dimensionsCm ? JSON.stringify(v.dimensionsCm) : null, v.dimensionsConfidence, JSON.stringify(v.style || {}), v.chairs ?? null, v.includedWith ?? null]);
  for (const o of c.offers) await q('insert into offers values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12) on conflict do nothing', [o.id, o.variantId, o.supplierId, o.price, o.currency, o.availability, o.affiliateUrl, o.provenance.source, o.provenance.sourceUrl, o.provenance.verifiedAt, o.provenance.verificationType, o.provenance.confidence]);
  await seedOutbound(q);
}
export async function getDb(){ if (!ready) ready = (async () => { const c = await connect(); await migrate(c.q); return c; })(); return ready; }
export function resetDbForTests(){ ready = null; }
