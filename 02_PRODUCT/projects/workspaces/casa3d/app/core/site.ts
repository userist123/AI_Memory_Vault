// Șantierul: remedierile (snag list) găsite la vizite — cameră, meserie, descriere, poze, stare (deschis → rezolvat →
// verificat). Doar date și validare; stocarea în lib/site.ts.
export const SNAG_STATUSES = ['open', 'fixed', 'verified'] as const;
export type SnagStatus = typeof SNAG_STATUSES[number];
export const TRADES = ['general', 'paint', 'electric', 'plumbing', 'tiles', 'flooring', 'carpentry', 'doors', 'hvac', 'cleaning'] as const;
export type Trade = typeof TRADES[number];
export interface SnagInput { title: string; body?: string; roomId?: string | null; trade: Trade; status?: SnagStatus; photos?: string[] }
export interface Snag extends Required<Pick<SnagInput, 'title' | 'trade'>> { id: string; number: number; body: string; roomId: string | null; status: SnagStatus; photos: string[]; createdAt: string; updatedAt: string; resolvedAt: string | null }
export const SITE_LIMITS = { title: 160, body: 2000, photosPerSnag: 8, photoBytes: 3 * 1024 * 1024, photosPerProject: 600, snagsPerProject: 1000 };
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const isUuid = (v: unknown): v is string => typeof v === 'string' && UUID_RE.test(v);
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u0009\u000b-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, '').trim().slice(0, max) : '';

export class SiteError extends Error {}
/** Validare pentru creare (full) sau modificare (parțial). Camerele trebuie să existe în proiect. */
export function checkSnag(x: unknown, roomIds: Set<string>, partial = false): Partial<SnagInput> {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new SiteError('Remedierea e invalidă.');
  const out: Partial<SnagInput> = {};
  if (o.title !== undefined || !partial){ const t = clean(o.title, SITE_LIMITS.title); if (!t) throw new SiteError('Scrie pe scurt ce e de remediat.'); out.title = t; }
  if (o.body !== undefined) out.body = clean(o.body, SITE_LIMITS.body);
  if (o.roomId !== undefined){ if (o.roomId !== null && !(typeof o.roomId === 'string' && roomIds.has(o.roomId))) throw new SiteError('Camera nu există în proiect.'); out.roomId = o.roomId; }
  if (o.trade !== undefined || !partial){ const tr = o.trade ?? 'general'; if (!(TRADES as readonly string[]).includes(tr)) throw new SiteError('Meserie necunoscută.'); out.trade = tr; }
  if (o.status !== undefined){ if (!(SNAG_STATUSES as readonly string[]).includes(o.status)) throw new SiteError('Stare necunoscută.'); out.status = o.status; }
  if (o.photos !== undefined){ if (!Array.isArray(o.photos) || o.photos.length > SITE_LIMITS.photosPerSnag || !o.photos.every(isUuid) || new Set(o.photos).size !== o.photos.length) throw new SiteError('Pozele sunt invalide.'); out.photos = o.photos; }
  return out;
}
/** Tipul real al imaginii, după primii octeți (nu după antetul trimis): doar JPEG, PNG, WebP. */
export function sniffImage(b: Uint8Array): 'image/jpeg' | 'image/png' | 'image/webp' | null {
  if (b.length > 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) return 'image/jpeg';
  if (b.length > 8 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47 && b[4] === 0x0d && b[5] === 0x0a && b[6] === 0x1a && b[7] === 0x0a) return 'image/png';
  if (b.length > 12 && String.fromCharCode(...b.slice(0, 4)) === 'RIFF' && String.fromCharCode(...b.slice(8, 12)) === 'WEBP') return 'image/webp';
  return null;
}
export function snagSummary(items: Snag[]){ return { open: items.filter(s => s.status === 'open').length, fixed: items.filter(s => s.status === 'fixed').length, verified: items.filter(s => s.status === 'verified').length }; }
