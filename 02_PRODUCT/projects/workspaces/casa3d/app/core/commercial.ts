// Stratul comercial al proiectului (doar pentru designer): discount de achiziție (trade) și adaos pe furnizor, raportul de
// profit, graficul de plăți și proforma. Funcții pure; persistența e în lib/commercial.ts.
//
// Prețurile din catalog sunt prețuri de listă de magazin, cu TVA inclus (aceeași bază ca bugetul din core/boq.ts):
//   cost designer = listă × (1 − discount),  preț client = listă × (1 + adaos),  profit = preț client − cost.
// Un preț necunoscut rămâne necunoscut (null), niciodată 0: linia nu intră în totaluri și e numărată separat.
// Manopera e trecută la client fără adaos (cost = preț client, profit 0).
//
// Convenția de TVA (aceeași în contract, raport și proformă):
//   * produsele (catalog și produse proprii) au prețuri de listă CU TVA inclus; proforma extrage TVA-ul din ele;
//   * onorariul de proiectare e FĂRĂ TVA (net), calculat O SINGURĂ DATĂ de computeFee/feeFor din core/contract.ts
//     (onorariul din contract sau din fișa proiectului, suprafața și baza bugetului); TVA-ul se adaugă doar dacă studioul
//     e plătitor (ContractData.vatPayer). În profit intră doar onorariul net; în totalul de client, onorariul brut.
// Produsele proprii: costul e prețul real de achiziție (costPrice × cantitate) când există, altfel din discountul furnizorului.
//
// Proforma nu e factură fiscală și nu trece prin e-Factura: e o cerere de plată pentru client.
import type { BoqCategory, Budget } from './boq';
import { safeCell, quoteCell } from './csv-safe';

const r2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

/**
 * Cota standard de TVA din România: 21% de la 1 august 2025 (Legea nr. 141/2025, Monitorul Oficial nr. 699 din 25.07.2025).
 * Verificat 2026-10-15: https://taxnews.ey.com/news/2025-1622-romanian-tax-changes-introduced-by-new-fiscal-and-budgetary-measures
 * („Starting from 1 August 2025, the standard VAT rate will increase from 19% to 21%”). Aceeași valoare ca VAT_RATE din core/rules.boq.ts.
 */
export { VAT_RATE } from './rules.boq';
import { VAT_RATE } from './rules.boq';

export const COMMERCIAL_LIMITS = { suppliers: 100, milestones: 20, supplierName: 120, label: 120, series: 12, discountMax: 99, markupMax: 1000, amountMax: 1e9, numberMax: 1e9 };
export const MILESTONE_STATUSES = ['pending', 'paid'] as const;
export type MilestoneStatus = typeof MILESTONE_STATUSES[number];

export interface Terms { discountPct?: number; markupPct?: number }
export interface Milestone { id: string; label: string; pct?: number; fixed?: number; due?: string; status: MilestoneStatus; paidAt?: string }
export interface ProformaSettings { series: string; number: number; issueDate?: string; dueDate?: string; milestoneId?: string | null }
export interface CommercialData { defaults: Terms; suppliers: Record<string, Terms>; milestones: Milestone[]; proforma: ProformaSettings }

export const DEFAULT_MILESTONES: Milestone[] = [
  { id: 'avans', label: 'Avans', pct: 30, status: 'pending' },
  { id: 'livrare', label: 'La livrarea mobilierului', pct: 50, status: 'pending' },
  { id: 'predare', label: 'La predare', pct: 20, status: 'pending' }];
export const emptyCommercial = (): CommercialData => ({ defaults: {}, suppliers: {}, milestones: [], proforma: { series: 'PF', number: 1 } });

// ---------- validare (serverul folosește exact aceste reguli) ----------
export class CommercialError extends Error {}
const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const validDate = (v: unknown): v is string => typeof v === 'string' && DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿<>]/g, '').trim() : '';
const num = (v: unknown, min: number, max: number, what: string): number => {
  if (typeof v !== 'number' || !Number.isFinite(v) || v < min || v > max) throw new CommercialError(`${what}: valoare în afara intervalului ${min}–${max}.`); return r2(v); };
function checkTerms(x: unknown, what: string): Terms {
  if (x == null) return {}; if (typeof x !== 'object' || Array.isArray(x)) throw new CommercialError(`${what}: date invalide.`);
  const o = x as any, out: Terms = {};
  if (o.discountPct != null) out.discountPct = num(o.discountPct, 0, COMMERCIAL_LIMITS.discountMax, `${what} — discount`);
  if (o.markupPct != null) out.markupPct = num(o.markupPct, 0, COMMERCIAL_LIMITS.markupMax, `${what} — adaos`);
  return out;
}
/** Suma procentelor din grafic (doar etapele cu procent). */
export const pctSum = (ms: Pick<Milestone, 'pct'>[]) => r2(ms.reduce((a, m) => a + (m.pct ?? 0), 0));
/** Procentele trebuie să facă exact 100% (dacă există etape cu procent). */
export const pctOk = (ms: Pick<Milestone, 'pct'>[]) => !ms.some(m => m.pct != null) || Math.abs(pctSum(ms) - 100) < 0.005;

/** Chei care, folosite ca nume de proprietate, ating prototipul obiectului: ignorate ca nume de furnizor. */
const UNSAFE_KEYS = new Set(['__proto__', 'constructor', 'prototype']);
const own = <T>(o: Record<string, T>, k: string): T | undefined => Object.prototype.hasOwnProperty.call(o, k) ? o[k] : undefined;
export function checkCommercial(x: unknown): CommercialData {
  if (!x || typeof x !== 'object' || Array.isArray(x)) throw new CommercialError('Datele comerciale sunt invalide.');
  const o = x as any, out = emptyCommercial(), L = COMMERCIAL_LIMITS;
  out.defaults = checkTerms(o.defaults, 'Valori implicite');
  if (o.suppliers != null){ if (typeof o.suppliers !== 'object' || Array.isArray(o.suppliers)) throw new CommercialError('Lista furnizorilor e invalidă.');
    const entries = Object.entries(o.suppliers).filter(([k]) => !UNSAFE_KEYS.has(clean(k, L.supplierName))); if (entries.length > L.suppliers) throw new CommercialError(`Cel mult ${L.suppliers} furnizori.`);
    for (const [k, v] of entries){ const name = clean(k, L.supplierName); if (!name || k.length > L.supplierName) throw new CommercialError('Numele furnizorului e gol sau prea lung.');
      if (Object.prototype.hasOwnProperty.call(out.suppliers, name)) throw new CommercialError('Furnizor duplicat.');
      const t = checkTerms(v, name); if (Object.keys(t).length) out.suppliers[name] = t; } }
  if (o.milestones != null){ if (!Array.isArray(o.milestones)) throw new CommercialError('Graficul de plăți e invalid.');
    if (o.milestones.length > L.milestones) throw new CommercialError(`Cel mult ${L.milestones} etape de plată.`);
    const ids = new Set<string>();
    for (const m of o.milestones){ if (!m || typeof m !== 'object') throw new CommercialError('Etapă de plată invalidă.');
      if (typeof m.id !== 'string' || !/^[\w-]{1,40}$/.test(m.id) || ids.has(m.id)) throw new CommercialError('Identificator de etapă invalid.'); ids.add(m.id);
      const label = clean(m.label, L.label); if (!label || (typeof m.label === 'string' && m.label.length > L.label)) throw new CommercialError('Numele etapei lipsește sau e prea lung.');
      const hasPct = m.pct != null, hasFixed = m.fixed != null;
      if (hasPct === hasFixed) throw new CommercialError(`„${label}”: alege fie procent, fie sumă fixă.`);
      const ms: Milestone = { id: m.id, label, status: 'pending' };
      if (hasPct){ ms.pct = num(m.pct, 0, 100, label); if (ms.pct <= 0) throw new CommercialError(`„${label}”: procentul trebuie să fie peste 0.`); }
      if (hasFixed) ms.fixed = num(m.fixed, 0, L.amountMax, label);
      if (m.due != null && m.due !== ''){ if (!validDate(m.due)) throw new CommercialError(`„${label}”: scadența nu e o dată validă (AAAA-LL-ZZ).`); ms.due = m.due; }
      if (!(MILESTONE_STATUSES as readonly string[]).includes(m.status ?? 'pending')) throw new CommercialError(`„${label}”: stare necunoscută.`);
      ms.status = m.status ?? 'pending';
      if (ms.status === 'paid'){ if (!validDate(m.paidAt)) throw new CommercialError(`„${label}”: data plății lipsește sau nu e validă.`); ms.paidAt = m.paidAt; }
      out.milestones.push(ms); }
    if (!pctOk(out.milestones)) throw new CommercialError(`Procentele etapelor fac ${pctSum(out.milestones)}%, nu 100%.`); }
  if (o.proforma != null){ const p = o.proforma; if (typeof p !== 'object' || Array.isArray(p)) throw new CommercialError('Setările proformei sunt invalide.');
    const series = typeof p.series === 'string' ? p.series.trim().toUpperCase() : 'PF';
    if (!/^[A-Z0-9-]{1,12}$/.test(series)) throw new CommercialError('Seria proformei: 1–12 litere, cifre sau „-”.');
    if (!Number.isInteger(p.number ?? 1) || (p.number ?? 1) < 1 || (p.number ?? 1) > L.numberMax) throw new CommercialError('Numărul proformei e invalid.');
    out.proforma = { series, number: p.number ?? 1 };
    for (const k of ['issueDate', 'dueDate'] as const) if (p[k] != null && p[k] !== ''){ if (!validDate(p[k])) throw new CommercialError('Data proformei nu e validă.'); out.proforma[k] = p[k]; }
    if (out.proforma.issueDate && out.proforma.dueDate && out.proforma.dueDate < out.proforma.issueDate) throw new CommercialError('Scadența proformei e înaintea datei emiterii.');
    if (p.milestoneId != null && p.milestoneId !== ''){ if (!out.milestones.some(m => m.id === p.milestoneId)) throw new CommercialError('Proforma se referă la o etapă inexistentă.'); out.proforma.milestoneId = p.milestoneId; } }
  return out;
}
/** Citire tolerantă din baza de date: niciodată nu aruncă. */
export function readCommercial(x: unknown): CommercialData { try { return x == null ? emptyCommercial() : checkCommercial(x); } catch { return emptyCommercial(); } }

// ---------- prețuri ----------
export const costOf = (list: number | null, discountPct = 0): number | null => list == null ? null : r2(list * (1 - discountPct / 100));
export const clientPriceOf = (list: number | null, markupPct = 0): number | null => list == null ? null : r2(list * (1 + markupPct / 100));
/** Termenii unui furnizor: ai lui, apoi cei impliciți, apoi 0. */
export function termsFor(d: Pick<CommercialData, 'defaults' | 'suppliers'>, supplier: string): Required<Terms> {
  const s = own(d.suppliers, supplier) ?? {}; return { discountPct: s.discountPct ?? d.defaults.discountPct ?? 0, markupPct: s.markupPct ?? d.defaults.markupPct ?? 0 };
}

// ---------- raportul de profit ----------
/** O linie de deviz, minimal: ce trebuie pentru profit și proformă. */
export type LineCategory = BoqCategory | 'custom';
/** `costTotal`: costul real al liniei (produs propriu cu preț de achiziție); lipsă/null → din discountul furnizorului. */
export interface SourceLine { key: string; category: LineCategory; label: string; supplier: string; qty: number; unit: string; unitPrice: number | null; total: number | null; costTotal?: number | null }
export interface SourceLabor { key: string; label: string; qty: number; unit: string; amount: number }
export interface ProfitLine { key: string; category: LineCategory; label: string; supplier: string; qty: number; unit: string;
  list: number | null; cost: number | null; client: number | null; profit: number | null; clientUnit: number | null; discountPct: number; markupPct: number }
export interface ProfitGroup { list: number | null; cost: number | null; client: number | null; profit: number | null; lines: number; unknown: number }
const addG = (g: ProfitGroup, l: Pick<ProfitLine, 'list' | 'cost' | 'client' | 'profit'>) => { g.lines++;
  if (l.list == null){ g.unknown++; return; } for (const k of ['list', 'cost', 'client', 'profit'] as const) g[k] = r2((g[k] ?? 0) + (l[k] as number)); };
const newG = (): ProfitGroup => ({ list: null, cost: null, client: null, profit: null, lines: 0, unknown: 0 });

export function profitLine(d: Pick<CommercialData, 'defaults' | 'suppliers'>, s: SourceLine): ProfitLine {
  const t = termsFor(d, s.supplier), list = s.total, cost = list != null && s.costTotal != null ? r2(s.costTotal) : costOf(list, t.discountPct), client = clientPriceOf(list, t.markupPct);
  return { key: s.key, category: s.category, label: s.label, supplier: s.supplier, qty: s.qty, unit: s.unit, list, cost, client,
    profit: client == null || cost == null ? null : r2(client - cost), clientUnit: list == null ? null : s.qty > 0 ? r2(client! / s.qty) : client, ...t };
}
export interface ProfitReport { lines: ProfitLine[]; bySupplier: Record<string, ProfitGroup>; byCategory: Record<string, ProfitGroup>; goods: ProfitGroup;
  /** onorariul NET (fără TVA) și TVA-ul lui (0 dacă studioul nu e plătitor) */
  labor: number; /** transport și montaj cunoscute (din buget), trecute la client fără adaos */ extras: number; fee: number | null; feeVat: number; totals: { cost: number; client: number; profit: number; marginPct: number | null }; unknownLines: number; feeUnknown: boolean }
/**
 * Profitul pe linie, pe furnizor, pe categorie și pe proiect. Totalurile proiectului adună doar valorile cunoscute;
 * `unknownLines` spune câte linii lipsesc, `feeUnknown` că onorariul nu se poate estima (de ex. pe oră).
 */
export function profitReport(d: Pick<CommercialData, 'defaults' | 'suppliers'>, items: SourceLine[], labor: SourceLabor[], fee: number | null, feeVat = 0, extras: SourceLabor[] = []): ProfitReport {
  // Map, nu obiect: numele furnizorului e text introdus de utilizator („__proto__”, „constructor” nu ating prototipul)
  const lines = items.map(i => profitLine(d, i)), sup = new Map<string, ProfitGroup>(), cat = new Map<string, ProfitGroup>(), goods = newG();
  const group = (m: Map<string, ProfitGroup>, k: string) => { let g = m.get(k); if (!g) m.set(k, g = newG()); return g; };
  for (const l of lines){ addG(group(sup, l.supplier), l); addG(group(cat, l.category), l); addG(goods, l); }
  const bySupplier: Record<string, ProfitGroup> = Object.fromEntries(sup), byCategory: Record<string, ProfitGroup> = Object.fromEntries(cat);
  const lab = r2(labor.reduce((a, l) => a + l.amount, 0)), f = fee ?? 0, fv = fee == null ? 0 : r2(feeVat);
  const ex = r2(extras.reduce((a, l) => a + l.amount, 0));
  const cost = r2((goods.cost ?? 0) + lab + ex), client = r2((goods.client ?? 0) + lab + ex + f + fv), profit = r2((goods.profit ?? 0) + f);
  return { lines, bySupplier, byCategory, goods, labor: lab, extras: ex, fee, feeVat: fv, totals: { cost, client, profit, marginPct: client > 0 ? r2(profit / client * 100) : null }, unknownLines: goods.unknown, feeUnknown: fee == null };
}

// ---------- graficul de plăți ----------
export interface ScheduleRow extends Milestone { amount: number; overdue: boolean; daysLate: number | null }
/**
 * Sumele etapelor: fixele se scad din total, procentele se aplică pe rest; ultima etapă cu procent ia diferența de rotunjire,
 * ca suma etapelor să fie exact totalul contractului. Restanță = neplătită și cu scadența trecută.
 */
export function paymentSchedule(ms: Milestone[], contractTotal: number, today: string){
  const fixed = r2(ms.reduce((a, m) => a + (m.fixed ?? 0), 0)), base = r2(Math.max(0, contractTotal - fixed)), lastPct = ms.map(m => m.pct != null).lastIndexOf(true);
  let pctAcc = 0;
  const rows: ScheduleRow[] = ms.map((m, i) => { let amount: number;
    if (m.fixed != null) amount = m.fixed; else if (i === lastPct && pctOk(ms)) amount = r2(base - pctAcc); else { amount = r2(base * (m.pct ?? 0) / 100); pctAcc = r2(pctAcc + amount); }
    const overdue = m.status !== 'paid' && !!m.due && m.due < today;
    return { ...m, amount, overdue, daysLate: overdue ? Math.round((Date.parse(today + 'T00:00:00Z') - Date.parse(m.due! + 'T00:00:00Z')) / 864e5) : null }; });
  const paid = r2(rows.filter(r => r.status === 'paid').reduce((a, r) => a + r.amount, 0)), scheduled = r2(rows.reduce((a, r) => a + r.amount, 0));
  return { rows, paid, outstanding: r2(scheduled - paid), scheduled, overdue: rows.filter(r => r.overdue).length, pctSum: pctSum(ms), pctOk: pctOk(ms), fixedExceeds: fixed > contractTotal + 0.005 };
}

// ---------- proforma ----------
// Ce vede clientul: doar prețuri de client. Nicio linie nu poartă cost, discount, adaos sau profit (testat).
export interface ProformaLine { code: string; name: string; unit: string; qty: number; unitNet: number; vatPct: number; net: number; vat: number; gross: number }
export interface ProformaGroup { category: string; lines: ProformaLine[]; net: number; vat: number; gross: number }
export interface Proforma { groups: ProformaGroup[]; net: number; vat: number; gross: number; vatPct: number; unknownLines: number;
  /** false: studioul nu e plătitor de TVA, deci onorariul are TVA 0 (proforma o spune) */
  feeVatPayer: boolean;
  milestone: { id: string; label: string; pct: number | null; amount: number; net: number; vat: number; vatPct: number } | null; due: number }
const cleanCode = (s: string) => s.replace(/[^\w.-]+/g, '-').replace(/^-+/, '').slice(0, 40) || 'linie';
/**
 * Proforma din prețurile de client. Produsele și manopera au TVA inclus: prețul unitar fără TVA = brut / (1 + TVA), rotunjit la
 * 2 zecimale; TVA-ul se calculează pe valoarea fără TVA a liniei, ca în programele de facturare. Onorariul (r.fee) e NET:
 * TVA-ul se adaugă doar pentru un studio plătitor (`vatPayer`, implicit: dacă raportul are TVA pe onorariu), altfel e 0.
 * Liniile cu preț necunoscut nu apar (sunt numărate). Cu o etapă aleasă, „de plată” e suma etapei din grafic; altfel, totalul.
 */
export function buildProforma(r: ProfitReport, laborLines: SourceLabor[], feeLabel: string, milestone: ScheduleRow | null, vatRate = VAT_RATE, vatPayer: boolean = r.feeVat > 0, extraLines: SourceLabor[] = []): Proforma {
  const mk = (code: string, name: string, unit: string, qty: number, grossTotal: number): ProformaLine => {
    const q = qty > 0 ? qty : 1, unitNet = r2(grossTotal / q / (1 + vatRate)), net = r2(unitNet * q), vat = r2(net * vatRate);
    return { code: cleanCode(code), name, unit, qty: q, unitNet, vatPct: r2(vatRate * 100), net, vat, gross: r2(net + vat) }; };
  const groups = new Map<string, ProformaLine[]>();
  for (const l of r.lines){ if (l.client == null) continue; const g = groups.get(l.category) ?? []; g.push(mk(l.key, l.label, l.unit, l.qty, l.client)); groups.set(l.category, g); }
  const lab = laborLines.filter(l => l.amount > 0).map(l => mk(l.key, l.label, l.unit, l.qty, l.amount)); if (lab.length) groups.set('labor', lab);
  const ex = extraLines.filter(l => l.amount > 0).map(l => mk(l.key, l.label, l.unit, l.qty, l.amount)); if (ex.length) groups.set('extras', ex);
  if (r.fee != null && r.fee > 0){ const net = r2(r.fee), rate = vatPayer ? vatRate : 0, vat = r2(net * rate);
    groups.set('fee', [{ code: 'onorariu', name: feeLabel, unit: 'serv', qty: 1, unitNet: net, vatPct: r2(rate * 100), net, vat, gross: r2(net + vat) }]); }
  const out: ProformaGroup[] = [...groups].map(([category, lines]) => ({ category, lines, net: r2(lines.reduce((a, l) => a + l.net, 0)), vat: r2(lines.reduce((a, l) => a + l.vat, 0)), gross: r2(lines.reduce((a, l) => a + l.gross, 0)) }));
  const net = r2(out.reduce((a, g) => a + g.net, 0)), vat = r2(out.reduce((a, g) => a + g.vat, 0)), gross = r2(net + vat);
  // etapa: o singură cotă dacă toate liniile au aceeași; altfel (onorariu fără TVA lângă produse cu TVA) cota efectivă a documentului
  const rates = new Set(out.flatMap(g => g.lines.map(l => l.vatPct))), single = rates.size <= 1, rate1 = single ? (rates.size ? [...rates][0]! / 100 : vatRate) : 0;
  const ms = milestone ? (() => {
    const mNet = single ? r2(milestone.amount / (1 + rate1)) : gross > 0 ? r2(milestone.amount * net / gross) : r2(milestone.amount), mVat = single ? r2(mNet * rate1) : r2(milestone.amount - mNet);
    return { id: milestone.id, label: milestone.label, pct: milestone.pct ?? null, amount: r2(mNet + mVat), net: mNet, vat: mVat, vatPct: single ? r2(rate1 * 100) : mNet > 0 ? r2(mVat / mNet * 100) : 0 }; })() : null;
  return { groups: out, net, vat, gross, vatPct: r2(vatRate * 100), unknownLines: r.unknownLines, feeVatPayer: vatPayer, milestone: ms, due: ms ? ms.amount : gross };
}

/**
 * Exportul CSV pentru programul de facturare. SmartBill, FGO și Oblio nu publică (la 2026-10-15) un format de import al
 * liniilor de factură/produse pe care să-l fi putut verifica, deci coloanele sunt GENERICE (marcate așa și în interfață):
 * cod; denumire; UM; cantitate; preț unitar fără TVA; cota TVA; valoare (fără TVA). Separator „;”, zecimale cu punct, UTF-8 cu BOM.
 */
export const PROFORMA_CSV_HEADER = ['cod', 'denumire', 'UM', 'cantitate', 'preț unitar fără TVA', 'cota TVA', 'valoare'];
export function proformaCsv(p: Proforma): string {
  const cell = (v: string | number) => quoteCell(typeof v === 'number' ? String(v) : safeCell(v));   // protecția comună (core/csv-safe.ts)
  const rows: (string | number)[][] = p.milestone
    ? [[`avans-${p.milestone.id}`, `${p.milestone.label}${p.milestone.pct != null ? ` (${p.milestone.pct}%)` : ''} conform contract`, 'serv', 1, p.milestone.net, p.milestone.vatPct, p.milestone.net]]
    : p.groups.flatMap(g => g.lines.map(l => [l.code, l.name, l.unit, l.qty, l.unitNet, l.vatPct, l.net]));
  return '﻿' + [PROFORMA_CSV_HEADER, ...rows].map(r => r.map(cell).join(';')).join('\r\n') + '\r\n';
}

// ---------- lanțul canonic al totalului (același în buget, panoul comercial și proformă) ----------
/** Produs propriu, cât trebuie pentru deviz (prețul de client cu TVA; costul real, dacă e cunoscut). */
export interface CustomSource { id: string; name: string; supplier: string; qty: number; unit: string; price: number | null; costPrice?: number | null }
/**
 * Liniile devizului din buget: produsele din catalog și cele proprii, manopera (scenariul ales, dacă e inclusă) și
 * transportul / montajul cunoscute. Aceeași funcție pe server (raport, proformă) și în buget, deci aceleași sume.
 */
export function commercialSources(b: Budget, custom: CustomSource[] = []): { items: SourceLine[]; labor: SourceLabor[]; extras: SourceLabor[] } {
  const items: SourceLine[] = b.items.map(i => ({ key: i.key, category: i.category, label: i.label, supplier: i.supplier, qty: i.orderedQty, unit: i.unit, unitPrice: i.unitPrice, total: i.total }));
  // produsele proprii: `price` e prețul de listă / către client (cu TVA, ca în catalog); costul real, dacă e cunoscut, e costPrice
  for (const c of custom) items.push({ key: 'c|' + c.id, category: 'custom', label: c.name, supplier: c.supplier, qty: c.qty, unit: c.unit,
    unitPrice: c.price, total: c.price == null ? null : r2(c.price * c.qty), costTotal: c.costPrice == null ? null : r2(c.costPrice * c.qty) });
  const labor: SourceLabor[] = b.settings.includeLabor ? b.labor.map(l => ({ key: l.key, label: l.label, qty: l.qty, unit: l.unit, amount: l[b.settings.laborScenario] })) : [];
  const extras: SourceLabor[] = b.extras.filter(e => e.amount != null && e.amount > 0).map(e => ({ key: e.key, label: e.label, qty: 1, unit: 'serv', amount: e.amount as number }));
  return { items, labor, extras };
}
/**
 * Totalul proiectului, pe rânduri cu nume, în ordinea canonică:
 *   produse la preț de listă → + adaos furnizori = produse la preț de client → + manoperă și transport / montaj (fără adaos)
 *   → + onorariu de proiectare (net + TVA dacă studioul e plătitor) = TOTAL DE FACTURAT (= proforma, fără rotunjirile pe linie)
 *   → + rezerva pentru neprevăzute (procent din produse, manoperă și transport; nu se aplică onorariului și nu se facturează)
 *   = TOTAL PROIECT ESTIMAT.
 * Fără adaos și fără onorariu, totalul e exact totalul bugetului (core/boq.ts computeBudget).
 */
export interface ProjectChain { list: number; markup: number; goodsClient: number; labor: number; extras: number; feeNet: number | null; feeVat: number; feeGross: number;
  invoiceable: number; contingencyPct: number; contingency: number; total: number; unknownLines: number; feeUnknown: boolean }
export function projectChain(r: ProfitReport, contingencyPct: number): ProjectChain {
  const list = r.goods.list ?? 0, goodsClient = r.goods.client ?? 0, feeGross = r.fee == null ? 0 : r2(r.fee + r.feeVat);
  const invoiceable = r2(goodsClient + r.labor + r.extras + feeGross), base = r2(goodsClient + r.labor + r.extras), contingency = r2(base * contingencyPct / 100);
  return { list, markup: r2(goodsClient - list), goodsClient, labor: r.labor, extras: r.extras, feeNet: r.fee, feeVat: r.feeVat, feeGross, invoiceable,
    contingencyPct, contingency, total: r2(invoiceable + contingency), unknownLines: r.unknownLines, feeUnknown: r.feeUnknown };
}
/** Rândurile cu nume ale lanțului (cheile de traducere `chain.*`), pentru buget, panoul comercial și proformă. */
export type ChainRow = { key: string; amount: number; strong?: boolean; vars?: Record<string, string | number> };
export function chainRows(c: ProjectChain, proformaGross?: number): ChainRow[] {
  const rows: ChainRow[] = [{ key: 'chain.list', amount: c.list }];
  if (c.markup !== 0) rows.push({ key: 'chain.markup', amount: c.markup }, { key: 'chain.goodsClient', amount: c.goodsClient });
  if (c.labor) rows.push({ key: 'chain.labor', amount: c.labor });
  if (c.extras) rows.push({ key: 'chain.extras', amount: c.extras });
  if (c.feeNet != null && c.feeNet > 0){ rows.push({ key: 'chain.feeNet', amount: c.feeNet }); if (c.feeVat) rows.push({ key: 'chain.feeVat', amount: c.feeVat }); }
  rows.push({ key: 'chain.invoiceable', amount: c.invoiceable, strong: true });
  if (proformaGross != null && Math.abs(proformaGross - c.invoiceable) >= 0.005) rows.push({ key: 'chain.vatRounding', amount: r2(proformaGross - c.invoiceable) });
  if (c.contingency) rows.push({ key: 'chain.contingency', amount: c.contingency, vars: { pct: c.contingencyPct } });
  rows.push({ key: 'chain.total', amount: c.total, strong: true });
  return rows;
}
