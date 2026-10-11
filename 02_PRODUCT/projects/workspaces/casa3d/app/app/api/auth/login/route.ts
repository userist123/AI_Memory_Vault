import { NextResponse } from 'next/server';
import { readAuth, checkPassword, makeSession, loginAllowed, loginFailed, SESSION_COOKIE, SESSION_DAYS } from '@/lib/auth';
import { readJsonLimited } from '@/lib/body-limit';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
// Login de pe telefon: parola setată pe calculatorul designerului. La reușită primești sesiunea și proiectele lui.
export const POST = async (req: Request) => {
  try {
    const cfg = readAuth(); if (!cfg) throw new HttpError(404, 'Accesul de pe telefon nu e activat. Pune o parolă din aplicație, pe calculator.');
    const ip = req.headers.get('cf-connecting-ip') || 'local'; if (!loginAllowed(ip)) throw new HttpError(429, 'Prea multe încercări. Încearcă din nou peste 15 minute.');
    const b = await readJsonLimited(req, 2048) as any;
    if (!checkPassword(cfg, b?.password)){ loginFailed(ip); throw new HttpError(401, 'Parolă greșită.'); }
    const res = NextResponse.json({ ok: true }, { headers: H }), secure = process.env.NODE_ENV === 'production';
    res.cookies.set(SESSION_COOKIE, makeSession(cfg), { httpOnly: true, sameSite: 'lax', secure, path: '/', maxAge: SESSION_DAYS * 86400 });
    res.cookies.set('casa_owner', cfg.owner, { httpOnly: true, sameSite: 'lax', secure, path: '/', maxAge: 60 * 60 * 24 * 365 * 2 });
    return res;
  } catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status, headers: H }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500, headers: H }); }
};
