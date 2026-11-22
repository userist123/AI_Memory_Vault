// Cereri de ofertă către meseriași: designerul trimite fiecărei echipe ACEEAȘI listă de cantități (articol, UM, cantitate),
// înghețată la creare; meseriașul completează preț unitar pe fiecare rând, incluziunile, regimul fiscal și termenii lui.
// Ofertele se compară pe aceeași bază (client persoană fizică = total cu TVA; firmă = total fără TVA), iar o ofertă cu rânduri
// goale sau neofertate nu intră în clasament ca și cum ar fi mai ieftină. Reperul de piață (labor_rates) îl vede doar designerul.
// Nimic nu se presupune în locul meseriașului (avans, termen, garanție, TVA): lipsa unei valori rămâne lipsă.
// Regimul fiscal al lucrării (inclusiv eventuala taxare inversă) NU e codificat aici: se verifică cu contabilul.
// Funcții pure; persistența e în lib/rfq.ts.
import { VAT_RATE } from './rules.boq';

export const TRADES = ['demolari', 'gipscarton', 'tencuieliGlet', 'zugraveli', 'gresieFaianta', 'pardoseli', 'electric', 'sanitar', 'montajObiecte', 'usi', 'mobilier', 'curatenie', 'altele'] as const;
export type Trade = typeof TRADES[number];
/** Tarifele de manoperă existente (data/materials.v1.json → labor_rates), pe meserii. Cornișa și bagheta le montează de obicei
 *  echipa de finisaje, înainte de zugrăvit; hidroizolația băii o face faianțarul. */
export const RATE_TRADE: Readonly<Record<string, Trade>> = {
  'manopera-parchet': 'pardoseli', 'manopera-parchet-spic': 'pardoseli', 'manopera-plinta': 'pardoseli',
  'manopera-gresie': 'gresieFaianta', 'manopera-gresie-diagonal': 'gresieFaianta', 'manopera-gresie-mare': 'gresieFaianta', 'manopera-faianta': 'gresieFaianta', 'manopera-hidroizolatie': 'gresieFaianta',
  'manopera-zugravit': 'zugraveli', 'manopera-tapet': 'zugraveli', 'manopera-cornisa': 'zugraveli', 'manopera-bagheta': 'zugraveli',
  'manopera-tencuiala-decorativa': 'tencuieliGlet',
  'manopera-rigips-tavan': 'gipscarton', 'manopera-scafa': 'gipscarton' };
// un tarif adăugat ulterior și încă necartat: după cuvintele din id; ce nu se recunoaște ajunge la „altele”
const RATE_HINTS: [RegExp, Trade][] = [[/demol|desfac|sparg/, 'demolari'], [/rigips|gips|scafa/, 'gipscarton'], [/tencui|glet|sapa/, 'tencuieliGlet'], [/zugrav|vopsi|tapet|cornis|baghet/, 'zugraveli'],
  [/gresie|faianta|hidroiz|placar/, 'gresieFaianta'], [/parchet|plinta|pardos/, 'pardoseli'], [/electr|priza|iluminat/, 'electric'], [/sanitar|instal/, 'sanitar'], [/(^|-)us[ai](-|$)/, 'usi'],
  [/mobil/, 'mobilier'], [/curat/, 'curatenie'], [/montaj/, 'montajObiecte']];
const own = <T>(o: Record<string, T>, k: string): T | undefined => Object.prototype.hasOwnProperty.call(o, k) ? o[k] : undefined;
export const tradeOfRate = (rateId: string): Trade => own(RATE_TRADE as Record<string, Trade>, rateId) ?? RATE_HINTS.find(([re]) => re.test(rateId))?.[1] ?? 'altele';
export const isTrade = (v: unknown): v is Trade => typeof v === 'string' && (TRADES as readonly string[]).includes(v);

export const RFQ_LIMITS = { title: 120, note: 1000, city: 80, label: 80, lines: 60, rooms: 80, name: 120, cui: 14, notes: 1000, priceMax: 1e6, amountMax: 1e7, qtyMax: 1e7,
  days: 730, months: 120, validDays: 365, contract: 200 } as const;
export class RfqError extends Error { missing?: string[]; constructor(m: string, missing?: string[]){ super(m); if (missing) this.missing = missing; } }

const r2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100, r3 = (n: number) => Math.round((n + Number.EPSILON) * 1000) / 1000;
const RATE_RE = /^[\w.-]{1,80}$/, DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const UNSAFE = new Set(['__proto__', 'constructor', 'prototype']);
/** Text curat pe un rând: fără caractere de control, invizibile sau „<>”. */
export const cleanLine = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿<>]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max) : '';
/** Text pe mai multe rânduri (notă, observații): păstrează rândurile, curăță restul. */
export const cleanNote = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/\r\n?/g, '\n').replace(/[\u0000-\u0009\u000b-\u001f\u007f​-‏‪-‮⁠-⁩﻿<>]/g, ' ').replace(/[ \t]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim().slice(0, max) : '';
const isNum = (v: unknown, lo: number, hi: number): v is number => typeof v === 'number' && Number.isFinite(v) && v >= lo && v <= hi;
const isInt = (v: unknown, lo: number, hi: number): v is number => Number.isInteger(v) && (v as number) >= lo && (v as number) <= hi;
export const validDate = (v: unknown): v is string => typeof v === 'string' && DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const isObj = (x: unknown): x is Record<string, unknown> => !!x && typeof x === 'object' && !Array.isArray(x);

// ---------- lista de cantități ----------
/** `ref`: reperul de piață pe unitate (labor_rates low / expected) — doar pentru designer, nu ajunge pe pagina meseriașului. */
export interface RfqLine { rateId: string; trade: Trade; label: string; unit: string; qty: number; rooms: { roomId: string; name: string; qty: number }[]; ref: { low: number; expected: number } }
export interface LaborSource { rateId: string; roomId: string; label: string; unit: string; qty: number; low: number; expected: number }
/**
 * Rândurile cererii din manopera devizului: câte un rând pe tarif (cantitatea totală pe proiect), cu defalcarea pe camere ca
 * detaliu. `trades` alege meseriile; ordinea e cea a meseriilor, apoi a apariției în deviz. Reperul = valoare / cantitate.
 */
export function packageLines(labor: LaborSource[], trades: readonly Trade[], roomNames: Record<string, string> = {}): RfqLine[] {
  const want = new Set(trades), by = new Map<string, RfqLine & { low: number; exp: number }>();
  for (const l of labor){ const trade = tradeOfRate(l.rateId); if (!want.has(trade) || !(l.qty > 0)) continue;
    let x = by.get(l.rateId); if (!x) by.set(l.rateId, x = { rateId: l.rateId, trade, label: l.label, unit: l.unit, qty: 0, rooms: [], ref: { low: 0, expected: 0 }, low: 0, exp: 0 });
    x.qty = r3(x.qty + l.qty); x.low += l.low; x.exp += l.expected;
    const room = x.rooms.find(r => r.roomId === l.roomId); if (room) room.qty = r3(room.qty + l.qty); else x.rooms.push({ roomId: l.roomId, name: roomNames[l.roomId] ?? l.roomId, qty: r3(l.qty) }); }
  return [...by.values()].sort((a, b) => TRADES.indexOf(a.trade) - TRADES.indexOf(b.trade))
    .map(({ low, exp, ...x }) => ({ ...x, ref: { low: r2(low / x.qty), expected: r2(exp / x.qty) } }));
}
/** Rândurile înghețate, validate (la creare și la fiecare citire din bază). */
export function checkLines(x: unknown): RfqLine[] {
  if (!Array.isArray(x) || !x.length || x.length > RFQ_LIMITS.lines) throw new RfqError('Lista de cantități a cererii nu e validă.');
  const seen = new Set<string>();
  return x.map((l: any) => {
    if (!isObj(l) || typeof l.rateId !== 'string' || !RATE_RE.test(l.rateId) || UNSAFE.has(l.rateId) || seen.has(l.rateId) || !isTrade(l.trade)) throw new RfqError('Un rând al cererii nu are un cod valid.'); seen.add(l.rateId);
    const label = cleanLine(l.label, 200), unit = cleanLine(l.unit, 12);
    if (!label || !unit || !isNum(l.qty, 0, RFQ_LIMITS.qtyMax) || !(l.qty > 0)) throw new RfqError('Un rând al cererii nu are denumire, unitate sau cantitate.');
    if (!isObj(l.ref) || !isNum(l.ref.low, 0, RFQ_LIMITS.priceMax) || !isNum(l.ref.expected, 0, RFQ_LIMITS.priceMax)) throw new RfqError(`Reperul de piață al rândului „${label}” nu e valid.`);
    const rooms = (Array.isArray(l.rooms) ? l.rooms : []).slice(0, RFQ_LIMITS.rooms).map((r: any) => { const name = cleanLine(r?.name, 80);
      if (!isObj(r) || typeof r.roomId !== 'string' || r.roomId.length > 120 || !name || !isNum(r.qty, 0, RFQ_LIMITS.qtyMax)) throw new RfqError(`Defalcarea pe camere a rândului „${label}” nu e validă.`);
      return { roomId: r.roomId, name, qty: r3(r.qty as number) }; });
    return { rateId: l.rateId, trade: l.trade, label, unit, qty: r3(l.qty), rooms, ref: { low: r2(l.ref.low), expected: r2(l.ref.expected) } }; });
}
/** Ce vede meseriașul dintr-un rând: denumire, UM, cantitate și camerele (doar numele). Fără reperul de piață. */
export interface PublicLine { rateId: string; trade: Trade; label: string; unit: string; qty: number; rooms: { name: string; qty: number }[] }
export const publicLines = (lines: RfqLine[]): PublicLine[] => lines.map(l => ({ rateId: l.rateId, trade: l.trade, label: l.label, unit: l.unit, qty: l.qty, rooms: l.rooms.map(r => ({ name: r.name, qty: r.qty })) }));

// ---------- oferta meseriașului ----------
export const INCLUSIONS = ['consumables', 'debris', 'transport', 'protection', 'stairs', 'cleaning'] as const;
export type Inclusion = typeof INCLUSIONS[number];
export const INCLUSION_MODES = ['included', 'excluded', 'separate'] as const;
export type InclusionMode = typeof INCLUSION_MODES[number];
export const COMPANY_FORMS = ['pfa', 'ii', 'srl', 'other'] as const;
export type CompanyForm = typeof COMPANY_FORMS[number];
/**
 * Oferta: prețul unitar pe rând (lei/UM, FĂRĂ TVA pentru un plătitor; pentru un neplătitor e prețul întreg); `null` = „nu ofertez”
 * acest rând (alegere explicită → ofertă parțială); cheie lipsă = necompletat. Sumele incluziunilor „cu preț separat” și
 * totalul negociat sunt în aceeași bază ca prețurile. Totalul negociat e opțional și se afișează separat, nu înlocuiește rândurile.
 */
export interface RfqOffer { v: 1; prices: Record<string, number | null>; inclusions: Partial<Record<Inclusion, { mode: InclusionMode; amount?: number }>>;
  company: { form?: CompanyForm; vatPayer?: boolean; name?: string; cui?: string }; advancePct?: number; startDate?: string; durationDays?: number; warrantyMonths?: number; validDays?: number;
  negotiatedTotal?: number; notes?: string }
export const emptyOffer = (): RfqOffer => ({ v: 1, prices: {}, inclusions: {}, company: {} });
const OFFER_KEYS = ['v', 'prices', 'inclusions', 'company', 'advancePct', 'startDate', 'durationDays', 'warrantyMonths', 'validDays', 'negotiatedTotal', 'notes'];
/** CUI: doar formă (RO opțional + 2–10 cifre). Nu se verifică online și nici cifra de control: e un text declarat de meseriaș. */
export const CUI_RE = /^(RO)?\d{2,10}$/;

/** Ce lipsește unei oferte ca să poată fi trimisă (chei: `price:<rând>`, `inclusion:<cheie>`, `form`, `vatPayer`, `advancePct`…). */
export function offerMissing(o: RfqOffer, lines: Pick<RfqLine, 'rateId'>[]): string[] {
  const m: string[] = [];
  for (const l of lines) if (own(o.prices, l.rateId) === undefined) m.push(`price:${l.rateId}`);
  if (lines.length && lines.every(l => own(o.prices, l.rateId) === null)) m.push('prices');
  for (const k of INCLUSIONS) if (!o.inclusions[k]) m.push(`inclusion:${k}`);
  if (!o.company.form) m.push('form'); if (o.company.vatPayer == null) m.push('vatPayer');
  for (const k of ['advancePct', 'startDate', 'durationDays', 'warrantyMonths', 'validDays'] as const) if (o[k] == null) m.push(k);
  return m;
}
/**
 * Validare strictă: chei necunoscute, rânduri din afara cererii sau valori în afara limitelor sunt refuzate. Cu `partial`
 * (ciorna salvată automat) câmpurile pot lipsi; fără, oferta trebuie să fie completă (fiecare rând cu preț sau „nu ofertez”,
 * cel puțin un rând cu preț, toate incluziunile bifate, regimul fiscal și termenii declarați) — altfel RfqError cu `missing`.
 */
export function checkOffer(x: unknown, lines: Pick<RfqLine, 'rateId'>[], opt: { partial?: boolean } = {}): RfqOffer {
  if (!isObj(x)) throw new RfqError('Oferta nu e validă.');
  for (const k of Object.keys(x)) if (!OFFER_KEYS.includes(k)) throw new RfqError('Oferta conține un câmp necunoscut.');
  if (x.v != null && x.v !== 1) throw new RfqError('Versiune necunoscută a ofertei.');
  const out = emptyOffer(), ids = new Set(lines.map(l => l.rateId));
  if (x.prices != null){ if (!isObj(x.prices)) throw new RfqError('Prețurile nu sunt valide.');
    for (const [k, v] of Object.entries(x.prices)){ if (!ids.has(k)) throw new RfqError('Oferta are preț pe un rând care nu e în cerere.');
      if (v === null){ out.prices[k] = null; continue; }
      if (!isNum(v, 0, RFQ_LIMITS.priceMax) || !(v > 0)) throw new RfqError('Un preț unitar nu e valid (trebuie să fie peste 0).'); out.prices[k] = r2(v); } }
  if (x.inclusions != null){ if (!isObj(x.inclusions)) throw new RfqError('Incluziunile nu sunt valide.');
    for (const [k, v] of Object.entries(x.inclusions)){ if (!(INCLUSIONS as readonly string[]).includes(k) || !isObj(v) || !(INCLUSION_MODES as readonly unknown[]).includes(v.mode) || Object.keys(v).some(f => f !== 'mode' && f !== 'amount')) throw new RfqError('O incluziune nu e validă.');
      const inc: { mode: InclusionMode; amount?: number } = { mode: v.mode as InclusionMode };
      if (inc.mode === 'separate'){ if (v.amount != null){ if (!isNum(v.amount, 0, RFQ_LIMITS.amountMax) || !(v.amount > 0)) throw new RfqError('Suma unei incluziuni cu preț separat nu e validă.'); inc.amount = r2(v.amount); }
        else if (!opt.partial) throw new RfqError('O incluziune „cu preț separat” are nevoie de sumă.', [`inclusion:${k}`]); }
      else if (v.amount != null) throw new RfqError('Doar incluziunile „cu preț separat” au sumă.');
      out.inclusions[k as Inclusion] = inc; } }
  if (x.company != null){ const c = x.company; if (!isObj(c) || Object.keys(c).some(f => !['form', 'vatPayer', 'name', 'cui'].includes(f))) throw new RfqError('Datele firmei nu sunt valide.');
    if (c.form != null){ if (!(COMPANY_FORMS as readonly unknown[]).includes(c.form)) throw new RfqError('Forma de organizare e necunoscută.'); out.company.form = c.form as CompanyForm; }
    if (c.vatPayer != null){ if (typeof c.vatPayer !== 'boolean') throw new RfqError('Regimul de TVA nu e valid.'); out.company.vatPayer = c.vatPayer; }
    if (c.name != null){ if (typeof c.name !== 'string' || c.name.length > RFQ_LIMITS.name) throw new RfqError('Denumirea e prea lungă.'); const n = cleanLine(c.name, RFQ_LIMITS.name); if (n) out.company.name = n; }
    if (c.cui != null){ if (typeof c.cui !== 'string') throw new RfqError('CUI-ul nu e valid.'); const s = c.cui.replace(/\s/g, '').toUpperCase();
      if (s){ if (!CUI_RE.test(s)) throw new RfqError('CUI-ul are doar cifre (2–10), opțional cu „RO” în față.'); out.company.cui = s; } } }
  if (x.advancePct != null){ if (!isNum(x.advancePct, 0, 100)) throw new RfqError('Avansul e un procent între 0 și 100.'); out.advancePct = r2(x.advancePct); }
  if (x.startDate != null){ if (!validDate(x.startDate)) throw new RfqError('Data de început nu e validă.'); out.startDate = x.startDate; }
  if (x.durationDays != null){ if (!isInt(x.durationDays, 1, RFQ_LIMITS.days)) throw new RfqError(`Durata e un număr întreg de zile lucrătoare (1–${RFQ_LIMITS.days}).`); out.durationDays = x.durationDays; }
  if (x.warrantyMonths != null){ if (!isInt(x.warrantyMonths, 0, RFQ_LIMITS.months)) throw new RfqError(`Garanția e un număr întreg de luni (0–${RFQ_LIMITS.months}).`); out.warrantyMonths = x.warrantyMonths; }
  if (x.validDays != null){ if (!isInt(x.validDays, 1, RFQ_LIMITS.validDays)) throw new RfqError(`Valabilitatea ofertei e un număr întreg de zile (1–${RFQ_LIMITS.validDays}).`); out.validDays = x.validDays; }
  if (x.negotiatedTotal != null){ if (!isNum(x.negotiatedTotal, 0, RFQ_LIMITS.amountMax * 10) || !(x.negotiatedTotal > 0)) throw new RfqError('Totalul negociat nu e valid.'); out.negotiatedTotal = r2(x.negotiatedTotal); }
  if (x.notes != null){ if (typeof x.notes !== 'string' || x.notes.length > RFQ_LIMITS.notes) throw new RfqError(`Observațiile sunt prea lungi (cel mult ${RFQ_LIMITS.notes} de caractere).`); const n = cleanNote(x.notes, RFQ_LIMITS.notes); if (n) out.notes = n; }
  if (!opt.partial){ const missing = offerMissing(out, lines); if (missing.length) throw new RfqError('Oferta nu e completă: fiecare rând are nevoie de preț sau de „nu ofertez”, iar incluziunile, regimul de TVA și termenii trebuie declarate.', missing); }
  return out;
}

/** Starea rândurilor: cu preț, neofertate explicit, necompletate. Completă = toate cu preț; parțială = fără goluri, dar cu rânduri neofertate. */
export function offerCoverage(o: RfqOffer, lines: Pick<RfqLine, 'rateId'>[]){
  const priced: string[] = [], declined: string[] = [], missing: string[] = [];
  for (const l of lines){ const p = own(o.prices, l.rateId); (p === undefined ? missing : p === null ? declined : priced).push(l.rateId); }
  return { priced, declined, missing, complete: !missing.length && !declined.length && priced.length > 0, partial: !missing.length && declined.length > 0 && priced.length > 0 };
}
/** Totalurile ofertei, în baza meseriașului: rânduri (fără TVA), incluziuni cu preț separat, TVA (doar plătitor), total. */
export function offerTotals(o: RfqOffer, lines: Pick<RfqLine, 'rateId' | 'qty'>[], vatRate = VAT_RATE){
  const net = r2(lines.reduce((a, l) => a + l.qty * (own(o.prices, l.rateId) ?? 0), 0)), separate = r2(INCLUSIONS.reduce((a, k) => a + (o.inclusions[k]?.mode === 'separate' ? o.inclusions[k]!.amount ?? 0 : 0), 0));
  const vat = o.company.vatPayer ? r2((net + separate) * vatRate) : 0;
  return { net, separate, vat, gross: r2(net + separate + vat) };
}

// ---------- compararea ----------
/** Baza comparației: client persoană fizică → total CU TVA (TVA-ul e cost); firmă → total FĂRĂ TVA. */
export type Basis = 'individual' | 'company';
export const isBasis = (v: unknown): v is Basis => v === 'individual' || v === 'company';
/** Pragul de „mult sub piață”: cu peste 40% sub tariful așteptat. */
export const BELOW_MARKET = 0.4;
export interface OfferIn { id: string; label: string; offer: RfqOffer }
export interface CompareCell { unit: number | null; declined: boolean; basisUnit: number | null; value: number | null; min: boolean; refPct: number | null; below: boolean }
export interface CompareRow { rateId: string; trade: Trade; label: string; unit: string; qty: number; ref: { low: number; expected: number }; min: number | null; cells: Record<string, CompareCell> }
export type OfferWarning = { code: 'INCOMPLETE'; n: number } | { code: 'PARTIAL'; n: number } | { code: 'BELOW_MARKET'; rateId: string; pct: number } | { code: 'EXCLUDED'; key: Inclusion };
export interface OfferSummary { id: string; label: string; complete: boolean; partial: boolean; missing: string[]; declined: string[]; vatPayer: boolean | null; form: CompanyForm | null;
  /** în baza meseriașului: rânduri fără TVA, incluziuni separate, TVA, total */ net: number; separate: number; vat: number; gross: number;
  /** pe baza aleasă: rândurile + incluziunile cu preț separat („total comparabil”) */ total: number;
  /** totalul negociat (opțional), pe baza aleasă — afișat separat, nu intră în clasament */ negotiated: number | null;
  excluded: Inclusion[]; warnings: OfferWarning[]; rank: number | null }
/**
 * Compară ofertele pe aceeași listă de cantități. Pe rând: prețul unitar al fiecărei oferte pe baza aleasă, minimul (doar când
 * cel puțin două oferte au preț), abaterea față de tariful de piață așteptat (prețul cu TVA al unui plătitor, ca pentru un
 * client persoană fizică) și avertismentul „mult sub piață” (> 40% sub cel așteptat și sub minimul pieței).
 * Pe ofertă: totalurile, rândurile lipsă / neofertate, incluziunile excluse. Clasament: doar între ofertele complete.
 */
export function compareOffers(lines: RfqLine[], offers: OfferIn[], basis: Basis, vatRate = VAT_RATE): { rows: CompareRow[]; offers: OfferSummary[] } {
  const k = (o: RfqOffer) => basis === 'individual' && o.company.vatPayer ? 1 + vatRate : 1, mkt = (o: RfqOffer) => o.company.vatPayer ? 1 + vatRate : 1;
  const rows: CompareRow[] = lines.map(l => { const cells: Record<string, CompareCell> = {};
    for (const o of offers){ const p = own(o.offer.prices, l.rateId), unit = typeof p === 'number' ? p : null, basisUnit = unit == null ? null : r2(unit * k(o.offer)), market = unit == null ? null : unit * mkt(o.offer);
      cells[o.id] = { unit, declined: p === null, basisUnit, value: unit == null ? null : r2(l.qty * unit * k(o.offer)), min: false,
        refPct: market == null || !(l.ref.expected > 0) ? null : r2((market - l.ref.expected) / l.ref.expected * 100),
        below: market != null && l.ref.expected > 0 && market < l.ref.expected * (1 - BELOW_MARKET) - 1e-9 && market < l.ref.low - 1e-9 }; }
    const have = Object.values(cells).filter(c => c.basisUnit != null), min = have.length ? Math.min(...have.map(c => c.basisUnit!)) : null;
    if (have.length >= 2) for (const c of have) c.min = c.basisUnit === min;
    return { rateId: l.rateId, trade: l.trade, label: l.label, unit: l.unit, qty: l.qty, ref: l.ref, min, cells }; });
  const out: OfferSummary[] = offers.map(o => { const cov = offerCoverage(o.offer, lines), t = offerTotals(o.offer, lines, vatRate), f = k(o.offer);
    const excluded = INCLUSIONS.filter(i => o.offer.inclusions[i]?.mode === 'excluded');
    const warnings: OfferWarning[] = [...(cov.missing.length ? [{ code: 'INCOMPLETE' as const, n: cov.missing.length }] : []), ...(cov.declined.length ? [{ code: 'PARTIAL' as const, n: cov.declined.length }] : []),
      ...rows.filter(r => r.cells[o.id]!.below).map(r => ({ code: 'BELOW_MARKET' as const, rateId: r.rateId, pct: Math.round(-r.cells[o.id]!.refPct!) })), ...excluded.map(key => ({ code: 'EXCLUDED' as const, key }))];
    return { id: o.id, label: o.label, complete: cov.complete, partial: cov.partial, missing: cov.missing, declined: cov.declined, vatPayer: o.offer.company.vatPayer ?? null, form: o.offer.company.form ?? null,
      net: t.net, separate: t.separate, vat: t.vat, gross: t.gross, total: r2((t.net + t.separate) * f), negotiated: o.offer.negotiatedTotal == null ? null : r2(o.offer.negotiatedTotal * f), excluded, warnings, rank: null }; });
  [...out].filter(o => o.complete).sort((a, b) => a.total - b.total).forEach((o, i, all) => { o.rank = i > 0 && all[i - 1]!.total === o.total ? all[i - 1]!.rank : i + 1; });
  return { rows, offers: out };
}

// ---------- prețurile contractate (după „Atribuie”) ----------
/** Prețul contractat pe tarif: lei/UM fără TVA (sau întreg, la neplătitor), cine l-a dat și din ce cerere. Stă pe proiect (Snapshot.laborContract). */
export interface LaborContractEntry { unitPrice: number; vatPayer: boolean; label: string; at: string; requestId: string }
export type LaborContract = Record<string, LaborContractEntry>;
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
/** Prețurile contractate dintr-o ofertă: doar rândurile cu preț (cele neofertate rămân pe estimarea de piață). */
export function contractFromOffer(lines: Pick<RfqLine, 'rateId'>[], o: RfqOffer, label: string, requestId: string, at: string): LaborContract {
  const out: LaborContract = {};
  for (const l of lines){ const p = own(o.prices, l.rateId); if (typeof p === 'number' && p > 0) out[l.rateId] = { unitPrice: r2(p), vatPayer: o.company.vatPayer === true, label: cleanLine(label, RFQ_LIMITS.label), at, requestId }; }
  return out;
}
/** Citire tolerantă (la salvarea proiectului): o intrare invalidă dispare, nu oprește salvarea; nimic valid → undefined. */
export function sanitizeLaborContract(x: unknown): LaborContract | undefined {
  if (!isObj(x)) return undefined; const out: LaborContract = {};
  for (const [k, v] of Object.entries(x).slice(0, RFQ_LIMITS.contract)){ const e = v as any;
    if (!RATE_RE.test(k) || UNSAFE.has(k) || !isObj(e) || !isNum(e.unitPrice, 0, RFQ_LIMITS.priceMax) || !(e.unitPrice > 0) || typeof e.vatPayer !== 'boolean' || typeof e.requestId !== 'string' || !UUID_RE.test(e.requestId)
      || typeof e.at !== 'string' || !validDate(e.at.slice(0, 10)) || e.at.length > 30) continue;
    const label = cleanLine(e.label, RFQ_LIMITS.label); if (!label) continue;
    out[k] = { unitPrice: r2(e.unitPrice), vatPayer: e.vatPayer, label, at: e.at, requestId: e.requestId }; }
  return Object.keys(out).length ? out : undefined;
}
/** Costul contractat al unei cantități, în baza devizului (prețuri cu TVA inclus): un plătitor adaugă TVA, un neplătitor nu. */
export const contractCost = (c: Pick<LaborContractEntry, 'unitPrice' | 'vatPayer'>, qty: number, vatRate = VAT_RATE) => r2(qty * c.unitPrice * (c.vatPayer ? 1 + vatRate : 1));
