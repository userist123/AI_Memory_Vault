// Acces de pe telefon: designerul pune o parolă pe calculatorul lui (doar local); prin adresa publică (tunel) editorul
// se deschide numai după login. Parola se ține doar ca hash scrypt, în .data/auth.json (în afara git, lizibil doar de
// utilizator), alături de proprietarul proiectelor și cheia sesiunilor. Schimbarea parolei schimbă cheia: toate
// sesiunile vechi (telefoanele logate) se închid.
import { scrypt, scryptSync, randomBytes, timingSafeEqual, createHmac } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

export interface AuthConfig { v: 1; salt: string; hash: string; owner: string; secret: string; updatedAt: string }
export const SESSION_COOKIE = 'casa_session', SESSION_DAYS = 30, PASSWORD_MIN = 10, PASSWORD_MAX = 200;
const SCRYPT = { N: 16384, r: 8, p: 1, maxmem: 64 * 1024 * 1024 };
const OWNER_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const file = () => process.env.CASA3D_AUTH_FILE || path.join(process.cwd(), '.data', 'auth.json');

let cache: { path: string; mtime: number; cfg: AuthConfig | null } | null = null;
export function readAuth(): AuthConfig | null {
  const f = file(); let mtime: number; try { mtime = fs.statSync(f).mtimeMs; } catch { cache = null; return null; }
  if (cache && cache.path === f && cache.mtime === mtime) return cache.cfg;
  let cfg: AuthConfig | null = null; try { const j = JSON.parse(fs.readFileSync(f, 'utf8')); if (j?.v === 1 && j.salt && j.hash && OWNER_RE.test(j.owner) && j.secret) cfg = j; } catch { cfg = null; }
  cache = { path: f, mtime, cfg }; return cfg;
}
const hashOf = (pw: string, salt: string) => scryptSync(pw.normalize('NFC'), Buffer.from(salt, 'base64'), 64, SCRYPT).toString('base64');

/** Doar de pe calculatorul designerului (verificat de ruta API): parola nouă, legată de proiectele proprietarului curent. */
export function setPassword(password: unknown, owner: string): void {
  if (typeof password !== 'string' || password.length < PASSWORD_MIN || password.length > PASSWORD_MAX) throw Object.assign(new Error(`Parola trebuie să aibă între ${PASSWORD_MIN} și ${PASSWORD_MAX} de caractere.`), { status: 400 });
  if (!OWNER_RE.test(owner)) throw Object.assign(new Error('Proprietar invalid.'), { status: 400 });
  const salt = randomBytes(16).toString('base64'), cfg: AuthConfig = { v: 1, salt, hash: hashOf(password, salt), owner, secret: randomBytes(32).toString('base64url'), updatedAt: new Date().toISOString() };
  const f = file(); fs.mkdirSync(path.dirname(f), { recursive: true });
  const tmp = `${f}.${process.pid}.tmp`; fs.writeFileSync(tmp, JSON.stringify(cfg), { mode: 0o600 }); fs.renameSync(tmp, f); cache = null;
}
export function removePassword(){ try { fs.unlinkSync(file()); } catch { /* deja lipsă */ } cache = null; }
/** Verificarea e asincronă (scrypt în fundal): serverul nu se blochează cât se verifică o parolă. */
export async function checkPassword(cfg: AuthConfig, password: unknown): Promise<boolean> {
  if (typeof password !== 'string' || password.length > PASSWORD_MAX) return false;
  const a = await new Promise<Buffer>((res, rej) => scrypt(password.normalize('NFC'), Buffer.from(cfg.salt, 'base64'), 64, SCRYPT, (e, k) => e ? rej(e) : res(k)));
  const b = Buffer.from(cfg.hash, 'base64'); return a.length === b.length && timingSafeEqual(a, b);
}

const sign = (cfg: AuthConfig, exp: number) => createHmac('sha256', cfg.secret).update(`${exp}.${cfg.owner}`).digest('base64url');
export function makeSession(cfg: AuthConfig, now = Date.now()){ const exp = now + SESSION_DAYS * 864e5; return `${exp}.${sign(cfg, exp)}`; }
export function verifySession(cfg: AuthConfig | null, token: string | undefined | null, now = Date.now()): boolean {
  if (!cfg || !token || token.length > 200) return false; const m = /^(\d{13})\.([\w-]{43})$/.exec(token); if (!m) return false;
  const exp = Number(m[1]); if (!(exp > now)) return false;
  const a = Buffer.from(m[2]!), b = Buffer.from(sign(cfg, exp)); return a.length === b.length && timingSafeEqual(a, b);
}

// Ghicitul parolei: fiecare încercare se rezervă ÎNAINTE de orice așteptare (cereri simultane nu trec de limită),
// cel mult 5 în 15 minute de la aceeași adresă; o reușită șterge încercările adresei. Fără limită globală: altfel
// oricine ar putea bloca intrarea designerului cu încercări greșite de pe multe adrese.
const WINDOW = 15 * 60e3, PER_IP = 5, MAX_IPS = 10000;
const attempts = new Map<string, number[]>();
const recent = (k: string, now: number) => (attempts.get(k) ?? []).filter(t => now - t < WINDOW);
/** Rezervă o încercare pentru adresă; false dacă adresa și-a epuizat încercările. */
export function takeLoginAttempt(ip: string, now = Date.now()){
  const r = recent(ip, now); if (r.length >= PER_IP){ attempts.set(ip, r); return false; }
  attempts.delete(ip); attempts.set(ip, [...r, now]);                    // reinserat la coadă: cele mai vechi adrese ies primele
  for (const k of attempts.keys()){ if (attempts.size <= MAX_IPS) break; attempts.delete(k); }   // memoria rămâne mărginită
  return true;
}
export function loginSucceeded(ip: string){ attempts.delete(ip); }
/** Cât mai e de așteptat (ms) până când adresa poate încerca din nou; 0 dacă poate acum. */
export function loginRetryAfterMs(ip: string, now = Date.now()){ const r = recent(ip, now); return r.length < PER_IP ? 0 : Math.max(1000, WINDOW - (now - r[0]!)); }
export function resetLoginLimits(){ attempts.clear(); }
