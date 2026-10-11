// „Produs din link”: serverul citește pagina produsului dintr-un magazin permis și scoate numele, prețul, poza.
// Cererea pleacă de pe calculatorul designerului, deci e păzită: doar magazine din listă, doar https, adresa IP la care
// se conectează efectiv se verifică la conectare (nu doar înainte — fără rebinding DNS spre rețeaua locală), fiecare
// redirecționare se verifică din nou, timp și mărime limitate, pagina e doar text.
import https from 'node:https';
import dns from 'node:dns';
import zlib from 'node:zlib';
import type { LookupFunction } from 'node:net';
import { HttpError } from './repo';
import { shopOf, isPrivateIp, parseProductPage, type ClippedProduct } from '../core/clip';

export const CLIP_LIMITS = { bytes: 2 * 1024 * 1024, timeoutMs: 8000, redirects: 3 };
export interface Hop { status: number; location: string | null; type: string; body: Buffer | null }
export type GetFn = (url: string) => Promise<Hop>;

type DnsLookup = (host: string, opts: { all: true }, cb: (e: NodeJS.ErrnoException | null, a: { address: string; family: number }[]) => void) => void;
/** Rezolvarea folosită la conectare: dacă oricare adresă e locală/privată, conexiunea nu se face. */
export function safeLookup(lookup: DnsLookup = dns.lookup as any): LookupFunction {
  return ((host: string, opts: any, cb: any) => {
    lookup(host, { all: true }, (err, addrs) => {
      if (err) return cb(err);
      if (!addrs.length || addrs.some(a => isPrivateIp(a.address))) return cb(Object.assign(new Error('Adresa magazinului nu e pe internet.'), { code: 'EPRIVATE' }));
      if (opts?.all) return cb(null, addrs);
      cb(null, addrs[0]!.address, addrs[0]!.family);
    });
  }) as LookupFunction;
}

/** O singură cerere GET, fără a urma redirecționări; corpul doar pentru 200 HTML, decomprimat și limitat. */
export const httpsGet: GetFn = (url) => new Promise((resolve, reject) => {
  const req = https.get(url, { lookup: safeLookup(), timeout: CLIP_LIMITS.timeoutMs, headers: { 'user-agent': 'Mozilla/5.0 (Casa3D; product preview)', accept: 'text/html,application/xhtml+xml', 'accept-encoding': 'gzip, deflate, br', 'accept-language': 'ro-RO,ro;q=0.9,en;q=0.5' } }, res => {
    const status = res.statusCode ?? 0, location = typeof res.headers.location === 'string' ? res.headers.location : null, type = String(res.headers['content-type'] ?? '');
    if (status !== 200 || !/^(text\/html|application\/xhtml\+xml)/i.test(type)){ res.resume(); return resolve({ status, location, type, body: null }); }
    const enc = String(res.headers['content-encoding'] ?? '').toLowerCase();
    const stream = enc === 'gzip' ? res.pipe(zlib.createGunzip()) : enc === 'deflate' ? res.pipe(zlib.createInflate()) : enc === 'br' ? res.pipe(zlib.createBrotliDecompress()) : res;
    const chunks: Buffer[] = []; let n = 0;
    stream.on('data', (c: Buffer) => { n += c.length; if (n > CLIP_LIMITS.bytes){ req.destroy(); stream.destroy(); reject(new HttpError(422, 'Pagina e prea mare.')); return; } chunks.push(c); });
    stream.on('end', () => resolve({ status, location, type, body: Buffer.concat(chunks) }));
    stream.on('error', reject);
  });
  req.on('timeout', () => req.destroy(new HttpError(504, 'Magazinul nu a răspuns la timp.')));
  req.on('error', reject);
});

/** Produsul din adresa dată; urmează cel mult 3 redirecționări, fiecare tot spre un magazin permis. */
export async function fetchProduct(raw: unknown, get: GetFn = httpsGet): Promise<ClippedProduct> {
  let shop = shopOf(raw); if (!shop) throw new HttpError(400, 'Adresa trebuie să fie o pagină https a unui magazin cunoscut.');
  let url = new URL((raw as string).trim()).href;
  for (let hop = 0; ; hop++){
    let r: Hop; try { r = await get(url); } catch (e: any){ if (e instanceof HttpError) throw e; throw new HttpError(502, e?.code === 'EPRIVATE' ? 'Adresa magazinului nu e pe internet.' : 'Pagina magazinului nu s-a putut deschide.'); }
    if (r.status >= 300 && r.status < 400 && r.location){
      if (hop >= CLIP_LIMITS.redirects) throw new HttpError(502, 'Prea multe redirecționări.');
      let next: string; try { next = new URL(r.location, url).href; } catch { throw new HttpError(502, 'Redirecționare invalidă.'); }
      const s = shopOf(next); if (!s) throw new HttpError(502, 'Magazinul trimite spre o adresă din afara listei.'); shop = s; url = next; continue;
    }
    if (r.status !== 200) throw new HttpError(502, `Magazinul a răspuns ${r.status}. Poți completa produsul de mână.`);
    if (!r.body) throw new HttpError(422, 'Adresa nu e o pagină de produs.');
    const p = parseProductPage(r.body.toString('utf8'), url, shop); if (!p) throw new HttpError(422, 'Pe pagină nu am găsit un produs. Poți completa de mână.');
    return p;
  }
}
