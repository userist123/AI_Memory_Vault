// Limite în memorie, ca la login: fereastră glisantă pe cheie, memoria mărginită (cele mai vechi chei ies primele).
// Folosite pe linkurile publice pentru client: cine are linkul nu poate inunda designerul cu păreri, alegeri sau decizii.
import { NextResponse } from 'next/server';

export interface Limiter { take(key: string, now?: number): { ok: true } | { ok: false; retryAfterMs: number }; reset(): void }
export function createLimiter(o: { windowMs: number; max: number; maxKeys?: number }): Limiter {
  const hits = new Map<string, number[]>(), maxKeys = o.maxKeys ?? 10000;
  return {
    take(key, now = Date.now()){
      const r = (hits.get(key) ?? []).filter(t => now - t < o.windowMs);
      if (r.length >= o.max){ hits.set(key, r); return { ok: false, retryAfterMs: Math.max(1000, o.windowMs - (now - r[0]!)) }; }
      hits.delete(key); hits.set(key, [...r, now]);
      for (const k of hits.keys()){ if (hits.size <= maxKeys) break; hits.delete(k); }
      return { ok: true };
    },
    reset(){ hits.clear(); },
  };
}

/** Adresa clientului: tunelul (cloudflared) pune cf-connecting-ip; pe calculatorul designerului nu există. */
export const clientIp = (h: { get(k: string): string | null }) => h.get('cf-connecting-ip')?.trim() || 'local';

export type ShareAction = 'feedback' | 'choices' | 'changes';
const WINDOW = 10 * 60e3;
// pe adresă și link (un client normal trimite câteva păreri) și, separat, pe link de pe toate adresele (mai larg)
const PER_IP: Record<ShareAction, Limiter> = { feedback: createLimiter({ windowMs: WINDOW, max: 20 }), choices: createLimiter({ windowMs: WINDOW, max: 30 }), changes: createLimiter({ windowMs: WINDOW, max: 20 }) };
const PER_TOKEN: Record<ShareAction, Limiter> = { feedback: createLimiter({ windowMs: WINDOW, max: 100 }), choices: createLimiter({ windowMs: WINDOW, max: 150 }), changes: createLimiter({ windowMs: WINDOW, max: 100 }) };
export const minutesLeft = (ms: number) => Math.max(1, Math.ceil(ms / 60e3));
export const shareLimitMessage = (ms: number) => `Prea multe trimiteri de pe acest link. Încearcă din nou peste ${minutesLeft(ms)} ${minutesLeft(ms) === 1 ? 'minut' : 'minute'}.`;

/** null dacă cererea trece; altfel răspunsul 429 (cu Retry-After și mesaj în română). */
export function shareRateLimit(action: ShareAction, token: string, headers: { get(k: string): string | null }, now = Date.now()): NextResponse | null {
  const a = PER_IP[action].take(`${token}|${clientIp(headers)}`, now); const b = a.ok ? PER_TOKEN[action].take(token, now) : a;
  if (a.ok && b.ok) return null;
  const ms = !a.ok ? a.retryAfterMs : !b.ok ? b.retryAfterMs : 60e3;
  return NextResponse.json({ error: shareLimitMessage(ms) }, { status: 429, headers: { 'retry-after': String(Math.ceil(ms / 1000)), 'cache-control': 'no-store', 'x-robots-tag': 'noindex' } });
}
export function resetShareLimits(){ for (const l of [...Object.values(PER_IP), ...Object.values(PER_TOKEN)]) l.reset(); }
