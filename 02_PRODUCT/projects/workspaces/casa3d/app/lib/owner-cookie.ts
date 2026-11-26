// Cookie-ul proprietarului anonim: aceleași reguli pentru proxy (care îl atribuie o singură dată) și pentru ownerId().
export const OWNER_COOKIE = 'casa_owner';
export const OWNER_RE = /^[0-9a-f-]{36}$/;
export const ownerCookieOptions = () => ({ httpOnly: true, sameSite: 'lax' as const, secure: process.env.NODE_ENV === 'production', path: '/', maxAge: 60 * 60 * 24 * 365 * 2 });
/** Căi care nu primesc proprietar anonim: autentificarea (login-ul pune el proprietarul designerului; două Set-Cookie
 *  în același răspuns ar depinde de ordinea lor), linkurile pentru client (n-au nevoie de un id persistent) și login-ul. */
const NO_OWNER = ['/api/auth/', '/share/', '/api/share/', '/login'];
/** Fișierele statice (/_next, /models/x.gltf, /favicon.ico) și căile de mai sus nu au nevoie de proprietar. */
export const needsOwner = (path: string) => !path.startsWith('/_next/') && !/\.[a-z0-9]+$/i.test(path)
  && !NO_OWNER.some(p => { const base = p.replace(/\/$/, ''); return path === base || path.startsWith(base + '/'); });
/** Antetul Cookie cu proprietarul înlocuit (celelalte cookie-uri rămân). */
export function withOwnerCookie(header: string | null, id: string){
  const rest = (header ?? '').split(';').map(c => c.trim()).filter(c => c && !c.startsWith(`${OWNER_COOKIE}=`));
  return [...rest, `${OWNER_COOKIE}=${id}`].join('; ');
}
