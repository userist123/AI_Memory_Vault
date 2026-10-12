// Proprietar anonim per browser (cookie httpOnly). Proiectele sunt private implicit; autentificarea completă vine ulterior.
import { cookies } from 'next/headers';
export async function ownerId(create = true){
  const jar = await cookies(); let id = jar.get('casa_owner')?.value;
  if ((!id || !/^[0-9a-f-]{36}$/.test(id)) && create){ id = crypto.randomUUID(); jar.set('casa_owner', id, { httpOnly: true, sameSite: 'lax', secure: process.env.NODE_ENV === 'production', path: '/', maxAge: 60 * 60 * 24 * 365 * 2 }); }
  return id || null;
}
