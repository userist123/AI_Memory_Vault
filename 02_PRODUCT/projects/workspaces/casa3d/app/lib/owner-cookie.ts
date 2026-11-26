// Cookie-ul proprietarului anonim: aceleași reguli pentru proxy (care îl atribuie o singură dată) și pentru ownerId().
export const OWNER_COOKIE = 'casa_owner';
export const OWNER_RE = /^[0-9a-f-]{36}$/;
export const ownerCookieOptions = () => ({ httpOnly: true, sameSite: 'lax' as const, secure: process.env.NODE_ENV === 'production', path: '/', maxAge: 60 * 60 * 24 * 365 * 2 });
/** Fișierele statice (/_next, /models/x.gltf, /favicon.ico) nu au nevoie de proprietar. */
export const needsOwner = (path: string) => !path.startsWith('/_next/') && !/\.[a-z0-9]+$/i.test(path);
/** Antetul Cookie cu proprietarul înlocuit (celelalte cookie-uri rămân). */
export function withOwnerCookie(header: string | null, id: string){
  const rest = (header ?? '').split(';').map(c => c.trim()).filter(c => c && !c.startsWith(`${OWNER_COOKIE}=`));
  return [...rest, `${OWNER_COOKIE}=${id}`].join('; ');
}
