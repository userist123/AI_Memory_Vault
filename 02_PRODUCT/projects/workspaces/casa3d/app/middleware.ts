import { NextResponse, type NextRequest } from 'next/server';
import { gate, isLocalRequest } from './lib/public-gate';
import { readAuth, verifySession, SESSION_COOKIE } from './lib/auth';
import { newNonce, contentSecurityPolicy, policyFor, applySecurityHeaders } from './lib/security-headers';
// Din afară (prin tunel) se văd linkurile pentru client și pagina de login; după login, toată aplicația
// (fără setarea parolei și administrare, care rămân doar pe acest calculator).
// Fiecare răspuns primește antetele de securitate și un CSP cu nonce nou (lib/security-headers.ts).
export const config = { runtime: 'nodejs' };
const H = { 'x-robots-tag': 'noindex', 'cache-control': 'no-store' };
export function middleware(req: NextRequest){
  const path = req.nextUrl.pathname, local = isLocalRequest(req.headers.get('host'), req.headers);
  const nonce = newNonce(), csp = contentSecurityPolicy(nonce), policy = policyFor(path, nonce);
  // Next.js citește CSP-ul din cererea transmisă mai departe și pune nonce-ul pe scripturile pe care le randează
  const forward = new Headers(req.headers); forward.set('content-security-policy', csp); forward.set('x-nonce', nonce);
  const done = (res: NextResponse) => applySecurityHeaders(res, policy, req.headers);
  const d = gate({ local, path, method: req.method, session: !local && verifySession(readAuth(), req.cookies.get(SESSION_COOKIE)?.value), demo: process.env.CASA3D_DEMO === '1' });
  if (d === 'allow') return done(NextResponse.next({ request: { headers: forward } }));
  // pagina de login se arată chiar la adresa cerută (fără redirecționare: serverul nu știe adresa publică a tunelului);
  // după login, pagina se reîncarcă pe aceeași adresă, acum cu sesiune
  if (d === 'login'){ const u = req.nextUrl.clone(); u.pathname = '/login'; u.search = ''; u.protocol = 'http:'; return done(NextResponse.rewrite(u, { headers: H, request: { headers: forward } })); } // intern: serverul vorbește http, chiar dacă tunelul a venit pe https
  if (d === 'unauthorized') return done(NextResponse.json({ error: 'Autentifică-te.' }, { status: 401, headers: H }));
  return done(new NextResponse('Not found', { status: 404, headers: H }));
}
