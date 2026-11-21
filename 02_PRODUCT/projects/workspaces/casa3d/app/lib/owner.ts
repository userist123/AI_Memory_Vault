// Proprietar anonim per browser (cookie httpOnly). Proiectele sunt private implicit; autentificarea completă vine ulterior.
// Cookie-ul se pune de proxy.ts la prima pagină (ownerForPage), ca cererile paralele ale paginii să vadă același proprietar;
// ownerId() rămâne plasa de siguranță pentru apelurile directe în API.
import { cookies } from 'next/headers';
import { isPublicPath } from './public-gate';
export const OWNER_COOKIE = 'casa_owner';
const OWNER_RE = /^[0-9a-f-]{36}$/;
export const ownerCookieOptions = () => ({ httpOnly: true, sameSite: 'lax' as const, secure: process.env.NODE_ENV === 'production', path: '/', maxAge: 60 * 60 * 24 * 365 * 2 });
export async function ownerId(create = true){
  const jar = await cookies(); let id = jar.get(OWNER_COOKIE)?.value;
  if ((!id || !OWNER_RE.test(id)) && create){ id = crypto.randomUUID(); jar.set(OWNER_COOKIE, id, ownerCookieOptions()); }
  return id || null;
}
/** Un proprietar nou pentru o pagină a aplicației (GET, nu API, nu paginile publice — linkul clientului, accesibilitatea, login —, nu fișiere), doar dacă browserul nu are deja unul valid. */
export function ownerForPage(req: { method: string; cookies: { get(n: string): { value: string } | undefined } }, path: string): string | null {
  if (req.method !== 'GET' || path.startsWith('/api/') || path.startsWith('/share/') || isPublicPath(path) || /^\/login\/?$/.test(path) || path.startsWith('/_next/') || /\.[a-z0-9]+$/i.test(path)) return null;
  const cur = req.cookies.get(OWNER_COOKIE)?.value;
  return cur && OWNER_RE.test(cur) ? null : crypto.randomUUID();
}
