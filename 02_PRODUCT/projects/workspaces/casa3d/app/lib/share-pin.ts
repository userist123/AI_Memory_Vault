// PIN pentru pinii de comentariu pe linkul clientului. Citirea părerilor rămâne doar cu tokenul linkului; pentru un pin
// pe imagine clientul introduce un PIN de 6 cifre dat de designer. PIN-ul se ține doar ca hash scrypt (cu sare), iar
// sesiunea e un cookie semnat HMAC cu o cheie per link, rotită la fiecare PIN nou sau dezactivare (cookie-urile vechi mor).
// Ghicitul: 5 eșecuri consecutive blochează linkul 15 minute (în DB, numărat în tranzacție cu `for update`), plus
// limitele în memorie pe IP și pe link (lib/rate-limit.ts, acțiunea 'pin'). PIN-ul nu apare în erori și nu se loghează.
import { scrypt, randomBytes, randomInt, timingSafeEqual, createHmac } from 'node:crypto';
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { isShareToken, resolveShare, shareActive } from './share';

export const PIN_COOKIE = 'casa_share_pin', PIN_SESSION_MS = 12 * 3600e3, PIN_MAX_FAILURES = 5, PIN_LOCK_MS = 15 * 60e3;
const PIN_RE = /^\d{6}$/, SESSION_RE = /^(\d{13})\.([\w-]{43})$/;
const SCRYPT = { N: 16384, r: 8, p: 1, maxmem: 64 * 1024 * 1024 };
export const isPinFormat = (p: unknown): p is string => typeof p === 'string' && PIN_RE.test(p);
const hashPin = (pin: string, salt: string) => new Promise<Buffer>((res, rej) => scrypt(pin, Buffer.from(salt, 'base64'), 64, SCRYPT, (e, k) => e ? rej(e) : res(k)));
const notFound = () => new HttpError(404, 'Link inexistent.');

/** 429 în blocare: mesajul spune cât mai e de așteptat; `details.retryAfterMs` ajunge în antetul Retry-After. */
export class PinLockedError extends HttpError {
  constructor(public retryAfterMs: number){ const m = Math.max(1, Math.ceil(retryAfterMs / 60e3));
    super(429, `Prea multe încercări greșite. Încearcă din nou peste ${m} ${m === 1 ? 'minut' : 'minute'}.`, { retryAfterMs }); }
}

/** Doar proprietarul: PIN nou de 6 cifre (crypto.randomInt), cheie de sesiune nouă, eșecuri șterse. PIN-ul se întoarce o singură dată. */
export async function setSharePin(owner: string, projectId: string, token: unknown): Promise<{ pin: string }> {
  await getProject(owner, projectId); if (!isShareToken(token)) throw notFound();
  const pin = String(randomInt(0, 1_000_000)).padStart(6, '0'), salt = randomBytes(16).toString('base64');
  const hash = (await hashPin(pin, salt)).toString('base64'), key = randomBytes(32).toString('base64url');
  const { q } = await getDb();
  const { rows } = await q(`update shares set pin_hash=$3, pin_salt=$4, pin_key=$5, pin_failures=0, pin_locked_until=null
    where token=$1 and project_id=$2 and ${shareActive()} returning token`, [token, projectId, hash, salt, key]);
  if (!rows[0]) throw notFound();
  return { pin };
}
/** Doar proprietarul: dezactivează pinii pe link (hash, sare și cheie șterse → sesiunile existente nu mai trec). */
export async function clearSharePin(owner: string, projectId: string, token: unknown): Promise<{ ok: true }> {
  await getProject(owner, projectId); if (!isShareToken(token)) throw notFound();
  const { q } = await getDb();
  const { rows } = await q(`update shares set pin_hash=null, pin_salt=null, pin_key=null, pin_failures=0, pin_locked_until=null
    where token=$1 and project_id=$2 returning token`, [token, projectId]);
  if (!rows[0]) throw notFound();
  return { ok: true };
}

const sign = (key: string, token: string, exp: number) => createHmac('sha256', key).update(`${token}.${exp}`).digest('base64url');
/** Valoarea cookie-ului: `<exp>.<hmac(pin_key, token.exp)>`. */
export const makePinSession = (key: string, token: string, now = Date.now()) => { const exp = now + PIN_SESSION_MS; return `${exp}.${sign(key, token, exp)}`; };
/** Verificare pură: cheie prezentă, format exact, neexpirat, semnătură comparată în timp constant. */
export function verifyPinSession(key: string | null | undefined, token: string, value: string | null | undefined, now = Date.now()): boolean {
  if (!key || !value || value.length > 100) return false; const m = SESSION_RE.exec(value); if (!m) return false;
  const exp = Number(m[1]); if (!(exp > now) || exp > now + PIN_SESSION_MS) return false;
  const a = Buffer.from(m[2]!), b = Buffer.from(sign(key, token, exp)); return a.length === b.length && timingSafeEqual(a, b);
}
/** Cookie-urile `casa_share_pin` din antetul Cookie (pot fi mai multe, cel mult câteva se verifică). */
export function pinCookies(header: string | null): string[] {
  if (!header) return [];
  return header.split(';').map(s => s.trim()).filter(s => s.startsWith(`${PIN_COOKIE}=`)).map(s => s.slice(PIN_COOKIE.length + 1)).slice(0, 4);
}
/** Linkul activ are pinii activați și una din valorile cookie e o sesiune validă pentru el. */
export async function hasPinSession(token: string, values: string[], now = Date.now()): Promise<boolean> {
  if (!isShareToken(token) || !values.length) return false;
  const { q } = await getDb(); const { rows: [s] } = await q(`select pin_key from shares where token=$1 and ${shareActive()}`, [token]);
  return !!s?.pin_key && values.some(v => verifyPinSession(s.pin_key, token, v, now));
}

/**
 * Clientul introduce PIN-ul. Link necunoscut / revocat → 404; pini neactivați → 403; în blocare → 429 fără verificarea
 * PIN-ului; format greșit → 400 (fără scrypt); PIN greșit → 401 și eșecul numărat (al 5-lea blochează 15 minute).
 * Numărarea și blocarea se fac în aceeași tranzacție cu rândul linkului blocat (`for update`); eroarea se aruncă după
 * commit, ca eșecul să rămână scris. La succes: cookie-ul de sesiune.
 */
export async function verifySharePin(token: unknown, pin: unknown, now = Date.now()): Promise<{ cookie: string; expiresAt: string }> {
  await resolveShare(token); const t = token as string;
  const { tx } = await getDb();
  const out = await tx(async q => {
    const { rows: [s] } = await q(`select pin_hash, pin_salt, pin_key, pin_failures, pin_locked_until from shares where token=$1 and ${shareActive()} for update`, [t]);
    if (!s) return { err: notFound() };
    if (!s.pin_hash || !s.pin_salt || !s.pin_key) return { err: new HttpError(403, 'Pinii pe imagine nu sunt activați pe acest link.') };
    const lockedUntil = s.pin_locked_until ? new Date(s.pin_locked_until).getTime() : 0;
    if (lockedUntil > now) return { err: new PinLockedError(lockedUntil - now) };
    if (!isPinFormat(pin)) return { err: new HttpError(400, 'PIN-ul are 6 cifre.') };
    const a = await hashPin(pin, s.pin_salt), b = Buffer.from(s.pin_hash, 'base64');
    if (a.length === b.length && timingSafeEqual(a, b)){
      await q('update shares set pin_failures=0, pin_locked_until=null where token=$1', [t]);
      return { cookie: makePinSession(s.pin_key, t, now) };
    }
    const failures = (Number(s.pin_failures) || 0) + 1;
    if (failures >= PIN_MAX_FAILURES){ await q('update shares set pin_failures=0, pin_locked_until=$2 where token=$1', [t, new Date(now + PIN_LOCK_MS)]); return { err: new PinLockedError(PIN_LOCK_MS) }; }
    await q('update shares set pin_failures=$2, pin_locked_until=null where token=$1', [t, failures]);
    return { err: new HttpError(401, 'PIN greșit.') };
  });
  if ('err' in out) throw out.err;
  return { cookie: out.cookie!, expiresAt: new Date(now + PIN_SESSION_MS).toISOString() };
}
