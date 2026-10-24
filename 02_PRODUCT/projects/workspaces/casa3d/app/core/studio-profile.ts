// Profilul firmei designerului: apare pe planșe (cartuș), pe prezentare și pe dosarul de predare.
// CUI-ul și IBAN-ul se verifică după cifrele lor de control, ca un document trimis clientului să nu aibă date greșite.

export interface StudioProfile { name?: string; cui?: string; regCom?: string; address?: string; phone?: string; email?: string; website?: string; iban?: string; bank?: string; logo?: string }
export class ProfileError extends Error {}
export const PROFILE_LIMITS = { text: 160, logoBytes: 300 * 1024 };

/** CUI românesc: cifra de control cu cheia 753217532 (prefixul RO e opțional). */
export function validCui(raw: string): boolean {
  const s = raw.replace(/^RO/i, '').replace(/\s/g, ''); if (!/^\d{2,10}$/.test(s)) return false;
  const body = s.slice(0, -1).padStart(9, '0'), key = '753217532'; let sum = 0;
  for (let i = 0; i < 9; i++) sum += Number(body[i]) * Number(key[i]);
  const c = (sum * 10) % 11 % 10; return c === Number(s.at(-1));
}
/** IBAN: lungimea țării (RO = 24) și restul 1 la împărțirea cu 97. */
export function validIban(raw: string): boolean {
  const s = raw.replace(/\s/g, '').toUpperCase(); if (!/^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$/.test(s)) return false;
  if (s.startsWith('RO') && s.length !== 24) return false;
  const r = s.slice(4) + s.slice(0, 4); let m = 0;
  for (const ch of r){ const v = /\d/.test(ch) ? ch : String(ch.charCodeAt(0) - 55); for (const d of v) m = (m * 10 + Number(d)) % 97; }
  return m === 1;
}
export function checkProfile(input: unknown): StudioProfile {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new ProfileError('Profil invalid.');
  const o = input as Record<string, unknown>, out: StudioProfile = {};
  for (const k of ['name', 'cui', 'regCom', 'address', 'phone', 'email', 'website', 'iban', 'bank'] as const){ const v = o[k];
    if (v === undefined || v === null || v === '') continue; if (typeof v !== 'string' || v.length > PROFILE_LIMITS.text) throw new ProfileError('Câmp prea lung sau invalid.'); out[k] = v.trim(); }
  if (out.cui && !validCui(out.cui)) throw new ProfileError('CUI-ul nu e valid (cifra de control nu se potrivește).');
  if (out.iban && !validIban(out.iban)) throw new ProfileError('IBAN-ul nu e valid.');
  if (out.iban) out.iban = out.iban.replace(/\s/g, '').toUpperCase().replace(/(.{4})/g, '$1 ').trim();
  if (out.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(out.email)) throw new ProfileError('Adresa de e-mail nu e validă.');
  if (out.website && !/^https?:\/\/[^\s]+$/i.test(out.website)) throw new ProfileError('Site-ul trebuie să înceapă cu https://');
  if (o.logo !== undefined && o.logo !== null && o.logo !== ''){
    // doar PNG sau JPEG ca data URL, mic; SVG nu (poate purta scripturi)
    if (typeof o.logo !== 'string' || !/^data:image\/(png|jpeg);base64,[A-Za-z0-9+/=]+$/.test(o.logo)) throw new ProfileError('Logo-ul trebuie să fie o imagine PNG sau JPEG.');
    if (o.logo.length * .75 > PROFILE_LIMITS.logoBytes) throw new ProfileError('Logo-ul e prea mare (maximum 300 KB).');
    out.logo = o.logo; }
  return out;
}
/** Rândul de identificare pentru documente: „Studio X · CUI RO123 · J40/… · tel · e-mail”. */
export const profileLine = (p: StudioProfile) => [p.name, p.cui && `CUI ${p.cui}`, p.regCom, p.phone, p.email, p.website?.replace(/^https?:\/\//, '')].filter(Boolean).join(' · ');
