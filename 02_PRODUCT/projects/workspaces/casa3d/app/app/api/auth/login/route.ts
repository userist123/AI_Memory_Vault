import { NextResponse } from 'next/server';
import { readAuth, checkPassword, makeSession, takeLoginAttempt, loginSucceeded, loginRetryAfterMs, SESSION_COOKIE, SESSION_DAYS } from '@/lib/auth';
import { sameOriginJson } from '@/lib/public-gate';
import { readJsonLimited } from '@/lib/body-limit';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
// Login de pe telefon: parola setată pe calculatorul designerului. La reușită primești sesiunea și proiectele lui.
export const POST = async (req: Request) => {
  try {
    const cfg = readAuth(); if (!cfg) throw new HttpError(404, 'Accesul de pe telefon nu e activat. Pune o parolă din aplicație, pe calculator.');
    if (!sameOriginJson(req.headers)) throw new HttpError(403, 'Cerere refuzată.');
    // încercarea se rezervă înainte de prima așteptare: cererile simultane nu pot trece de limită
    const ip = req.headers.get('cf-connecting-ip') || 'local';
    if (!takeLoginAttempt(ip)){ const ms = loginRetryAfterMs(ip), min = Math.max(1, Math.ceil(ms / 60e3));
      return NextResponse.json({ error: `Prea multe încercări. Încearcă din nou peste ${min} ${min === 1 ? 'minut' : 'minute'}.`, retryAfter: Math.ceil(ms / 1000) }, { status: 429, headers: { ...H, 'retry-after': String(Math.ceil(ms / 1000)) } }); }
    const b = await readJsonLimited(req, 2048) as any;
    if (!(await checkPassword(cfg, b?.password))) throw new HttpError(401, 'Parolă greșită.');
    loginSucceeded(ip);
    const res = NextResponse.json({ ok: true }, { headers: H }), secure = process.env.NODE_ENV === 'production';
    res.cookies.set(SESSION_COOKIE, makeSession(cfg), { httpOnly: true, sameSite: 'lax', secure, path: '/', maxAge: SESSION_DAYS * 86400 });
    res.cookies.set('casa_owner', cfg.owner, { httpOnly: true, sameSite: 'lax', secure, path: '/', maxAge: 60 * 60 * 24 * 365 * 2 });
    return res;
  } catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status, headers: H }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500, headers: H }); }
};
