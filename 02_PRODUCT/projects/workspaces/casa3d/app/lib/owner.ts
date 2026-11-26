// Proprietar anonim per browser (cookie httpOnly). Proiectele sunt private implicit; autentificarea completă vine ulterior.
// Proxy-ul atribuie proprietarul la prima cerere (proxy.ts); aici rămâne doar ca plasă de siguranță.
import { cookies } from 'next/headers';
import { OWNER_COOKIE, OWNER_RE, ownerCookieOptions } from './owner-cookie';
export { OWNER_COOKIE } from './owner-cookie';
export async function ownerId(create = true){
  const jar = await cookies(); let id = jar.get(OWNER_COOKIE)?.value;
  if ((!id || !OWNER_RE.test(id)) && create){ id = crypto.randomUUID(); jar.set(OWNER_COOKIE, id, ownerCookieOptions()); }
  return id || null;
}
