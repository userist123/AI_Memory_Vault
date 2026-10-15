import { NextResponse } from 'next/server';
import { SESSION_COOKIE } from '@/lib/auth';
import { sameOriginJson } from '@/lib/public-gate';
export const dynamic = 'force-dynamic';
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
// Ieșirea din cont: aceeași verificare de origine ca la login (o pagină străină nu poate scoate designerul din cont).
export const POST = async (req: Request) => {
  if (!sameOriginJson(req.headers)) return NextResponse.json({ error: 'Cerere refuzată.' }, { status: 403, headers: H });
  const res = NextResponse.json({ ok: true }, { headers: H }); res.cookies.set(SESSION_COOKIE, '', { path: '/', maxAge: 0 }); return res; };
