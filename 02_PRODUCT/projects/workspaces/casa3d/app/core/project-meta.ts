// Fișa proiectului pentru studio: clientul, adresa lucrării, etapa comercială, termenele și onorariul.
// Stă lângă proiect, nu în planul lui (Snapshot): nu intră în revizii și nu ajunge pe linkurile trimise clienților.
export const PROJECT_STATUSES = ['lead', 'offer', 'design', 'execution', 'done', 'paused'] as const;
export type ProjectStatus = typeof PROJECT_STATUSES[number];
// 'percent' (adăugat pentru contract): procent din bugetul de mobilare și finisaje; datele vechi (fixed, per_m2, hourly) rămân valide.
export const FEE_KINDS = ['fixed', 'per_m2', 'hourly', 'percent'] as const;
export interface ClientInfo { name?: string; phone?: string; email?: string; address?: string; notes?: string }
export interface ProjectMeta {
  status: ProjectStatus;
  client?: ClientInfo;
  /** adresa lucrării, dacă e alta decât a clientului */
  siteAddress?: string;
  startDate?: string; dueDate?: string;            // AAAA-LL-ZZ
  fee?: { kind: typeof FEE_KINDS[number]; amount: number };
  /** clientul vede pe link stadiul comenzilor (stare și date de livrare, fără prețuri de achiziție) */
  shareOrders?: boolean;
}
export const DEFAULT_META: ProjectMeta = { status: 'design' };
const LIM = { name: 120, phone: 40, email: 160, address: 300, notes: 4000, site: 300 };
const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const EMAIL_RE = /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/;
const PHONE_RE = /^[+0-9 ()./-]{4,40}$/;
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u0008\u000b-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, '').trim().slice(0, max) : '';
const validDate = (v: string) => DATE_RE.test(v) && !Number.isNaN(Date.parse(v + 'T00:00:00Z')) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);

export class MetaError extends Error { constructor(msg: string){ super(msg); } }
/** Validează și curăță fișa trimisă de interfață. Câmpurile goale dispar; valorile greșite opresc salvarea cu un mesaj. */
export function checkMeta(x: unknown): ProjectMeta {
  if (!x || typeof x !== 'object' || Array.isArray(x)) throw new MetaError('Fișa proiectului e invalidă.');
  const o = x as any, out: ProjectMeta = { status: DEFAULT_META.status };
  if (o.status !== undefined){ if (!(PROJECT_STATUSES as readonly string[]).includes(o.status)) throw new MetaError('Etapă necunoscută.'); out.status = o.status; }
  if (o.client !== undefined && o.client !== null){ if (typeof o.client !== 'object') throw new MetaError('Datele clientului sunt invalide.');
    const c: ClientInfo = {}, name = clean(o.client.name, LIM.name), phone = clean(o.client.phone, LIM.phone), email = clean(o.client.email, LIM.email), address = clean(o.client.address, LIM.address), notes = clean(o.client.notes, LIM.notes);
    if (phone && !PHONE_RE.test(phone)) throw new MetaError('Telefonul conține caractere nepermise.');
    if (email && !EMAIL_RE.test(email)) throw new MetaError('Adresa de e-mail nu e validă.');
    if (name) c.name = name; if (phone) c.phone = phone; if (email) c.email = email.toLowerCase(); if (address) c.address = address; if (notes) c.notes = notes;
    if (Object.keys(c).length) out.client = c; }
  const site = clean(o.siteAddress, LIM.site); if (site) out.siteAddress = site;
  for (const k of ['startDate', 'dueDate'] as const){ const v = o[k]; if (v == null || v === '') continue; if (typeof v !== 'string' || !validDate(v)) throw new MetaError('Data nu e validă (AAAA-LL-ZZ).'); out[k] = v; }
  if (out.startDate && out.dueDate && out.dueDate < out.startDate) throw new MetaError('Termenul de predare e înaintea începerii.');
  if (o.fee != null){ const f = o.fee; if (typeof f !== 'object' || !(FEE_KINDS as readonly string[]).includes(f.kind) || typeof f.amount !== 'number' || !Number.isFinite(f.amount) || f.amount < 0 || f.amount > 1e8 || (f.kind === 'percent' && f.amount > 100)) throw new MetaError('Onorariul e invalid.');
    out.fee = { kind: f.kind, amount: Math.round(f.amount * 100) / 100 }; }
  if (o.shareOrders === true) out.shareOrders = true; else if (o.shareOrders != null && o.shareOrders !== false) throw new MetaError('Opțiune invalidă.');
  return out;
}
/** Citire tolerantă din baza de date (date vechi sau incomplete): niciodată nu aruncă. */
export function readMeta(x: unknown): ProjectMeta { try { return checkMeta(x ?? {}); } catch { return { ...DEFAULT_META }; } }
/** Onorariul estimat în lei: fix, pe m² (suprafața proiectului), procent din `budgetBase` (dacă e dat) sau pe oră (fără ore, nu se poate estima). */
export function feeEstimate(meta: ProjectMeta, areaM2: number, budgetBase?: number): number | null {
  if (!meta.fee) return null; if (meta.fee.kind === 'fixed') return meta.fee.amount; if (meta.fee.kind === 'per_m2') return Math.round(meta.fee.amount * areaM2 * 100) / 100;
  if (meta.fee.kind === 'percent' && budgetBase != null && budgetBase > 0) return Math.round(meta.fee.amount * budgetBase) / 100; return null;
}
/** Zile până la termen (negativ = depășit), față de ziua dată (AAAA-LL-ZZ). */
export function daysLeft(meta: ProjectMeta, today: string): number | null {
  if (!meta.dueDate || meta.status === 'done') return null; return Math.round((Date.parse(meta.dueDate + 'T00:00:00Z') - Date.parse(today + 'T00:00:00Z')) / 864e5);
}
