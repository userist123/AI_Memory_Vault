import { NextResponse, type NextRequest } from 'next/server';
import { gate, isLocalRequest } from './lib/public-gate';
import { readAuth, verifySession, SESSION_COOKIE } from './lib/auth';
// Din afară (prin tunel) se văd linkurile pentru client și pagina de login; după login, toată aplicația
// (fără setarea parolei și administrare, care rămân doar pe acest calculator).
export const config = { runtime: 'nodejs' };
const H = { 'x-robots-tag': 'noindex', 'cache-control': 'no-store' };
export function middleware(req: NextRequest){
  const path = req.nextUrl.pathname, local = isLocalRequest(req.headers.get('host'), req.headers);
  const d = gate({ local, path, method: req.method, session: !local && verifySession(readAuth(), req.cookies.get(SESSION_COOKIE)?.value), demo: process.env.CASA3D_DEMO === '1' });
  if (d === 'allow') return NextResponse.next();
  // pagina de login se arată chiar la adresa cerută (fără redirecționare: serverul nu știe adresa publică a tunelului);
  // după login, pagina se reîncarcă pe aceeași adresă, acum cu sesiune
  if (d === 'login'){ const u = req.nextUrl.clone(); u.pathname = '/login'; u.search = ''; u.protocol = 'http:'; return NextResponse.rewrite(u, { headers: H }); } // intern: serverul vorbește http, chiar dacă tunelul a venit pe https
  if (d === 'unauthorized') return NextResponse.json({ error: 'Autentifică-te.' }, { status: 401, headers: H });
  return new NextResponse('Not found', { status: 404, headers: H });
}
