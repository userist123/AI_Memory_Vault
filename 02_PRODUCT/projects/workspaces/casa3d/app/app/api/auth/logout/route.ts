import { NextResponse } from 'next/server';
import { SESSION_COOKIE } from '@/lib/auth';
export const dynamic = 'force-dynamic';
export const POST = async () => { const res = NextResponse.json({ ok: true }); res.cookies.set(SESSION_COOKIE, '', { path: '/', maxAge: 0 }); return res; };
