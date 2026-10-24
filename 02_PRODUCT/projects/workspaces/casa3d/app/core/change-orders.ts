// Modificările cu preț (change orders): orice schimbare față de oferta aprobată se scrie, cu diferența de cost
// (plus sau minus), și se trimite clientului; clientul o aprobă sau o refuză pe link. Bugetul actual = oferta +
// modificările aprobate; cele trimise și încă nedecise se arată separat. Cauza nr. 1 a bugetelor depășite sunt
// schimbările de pe parcurs pe care nu le-a scris nimeni.
export const CHANGE_STATUSES = ['draft', 'sent', 'approved', 'rejected'] as const;
export type ChangeStatus = typeof CHANGE_STATUSES[number];
export interface ChangeOrder { id: string; number: number; title: string; body: string; amount: number; days: number; status: ChangeStatus;
  createdAt: string; sentAt: string | null; decidedAt: string | null; decidedBy: string | null; clientNote: string | null }
export const CHANGE_LIMITS = { title: 160, body: 2000, perProject: 200, author: 60, note: 1000, amount: 1e7, days: 365 };
export class ChangeError extends Error {}
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f​-‏‪-‮⁠-⁩﻿]/g, '').trim().slice(0, max + 1) : null;

/** Ce scrie designerul: titlu, descriere, diferența de cost (lei, cu semn) și zilele cu care se mută termenul. */
export function checkChange(x: unknown, partial = false): Partial<Pick<ChangeOrder, 'title' | 'body' | 'amount' | 'days'>> {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new ChangeError('Modificarea nu e validă.');
  const out: Partial<Pick<ChangeOrder, 'title' | 'body' | 'amount' | 'days'>> = {}, has = (k: string) => !partial || k in o;
  if (has('title')){ const t = clean(o.title, CHANGE_LIMITS.title); if (!t) throw new ChangeError('Scrie ce se schimbă.'); if (t.length > CHANGE_LIMITS.title) throw new ChangeError('Titlul e prea lung.'); out.title = t; }
  if (has('body')){ const b = clean(o.body ?? '', CHANGE_LIMITS.body); if (b == null || b.length > CHANGE_LIMITS.body) throw new ChangeError('Descrierea e prea lungă.'); out.body = b; }
  if (has('amount')){ const a = o.amount; if (typeof a !== 'number' || !Number.isFinite(a) || Math.abs(a) >= CHANGE_LIMITS.amount) throw new ChangeError('Diferența de cost nu e validă.'); out.amount = Math.round(a * 100) / 100; }
  if (has('days')){ const d = o.days ?? 0; if (!Number.isInteger(d) || Math.abs(d) > CHANGE_LIMITS.days) throw new ChangeError('Numărul de zile nu e valid.'); out.days = d; }
  return out;
}
/** Decizia clientului pe link: aprob / refuz, cu numele lui și, opțional, un motiv. */
/** `seen` = data trimiterii variantei pe care a văzut-o clientul: o decizie se aplică doar acelei variante. */
export function checkDecision(x: unknown): { status: 'approved' | 'rejected'; author: string; note: string | null; seen: string } {
  const o = x as any; if (!o || typeof o !== 'object') throw new ChangeError('Decizia nu e validă.');
  if (o.decision !== 'approved' && o.decision !== 'rejected') throw new ChangeError('Alege: aprob sau refuz.');
  const author = clean(o.author, CHANGE_LIMITS.author); if (!author || author.length > CHANGE_LIMITS.author) throw new ChangeError('Scrieți numele (cel mult 60 de caractere).');
  const note = clean(o.note ?? '', CHANGE_LIMITS.note); if (note == null || note.length > CHANGE_LIMITS.note) throw new ChangeError('Motivul e prea lung.');
  if (typeof o.seen !== 'string' || o.seen.length > 40 || Number.isNaN(Date.parse(o.seen))) throw new ChangeError('Pagina e veche; reîncărcați-o și decideți din nou.');
  return { status: o.decision, author, note: note || null, seen: new Date(o.seen).toISOString() };
}
const r2 = (v: number) => Math.round(v * 100) / 100;
/** Bugetul cu modificări: oferta, ce s-a aprobat (cu semn), ce așteaptă decizia, totalul actual și zilele adăugate. */
export function budgetWithChanges(offered: number, changes: Pick<ChangeOrder, 'amount' | 'days' | 'status'>[]){
  const approved = changes.filter(c => c.status === 'approved'), pending = changes.filter(c => c.status === 'sent');
  const a = r2(approved.reduce((s, c) => s + c.amount, 0)), p = r2(pending.reduce((s, c) => s + c.amount, 0));
  return { offered: r2(offered), approved: a, pending: p, current: r2(offered + a), ifAllApproved: r2(offered + a + p), days: approved.reduce((s, c) => s + c.days, 0), pendingCount: pending.length };
}
