// Contract și ofertă de servicii de design interior: calculatorul de onorariu (pur) și constructorul textului documentelor.
// Datele contractului stau lângă proiect (tabelul project_contract), nu în plan: nu intră în revizii și nu ajung pe linkul clientului.
// Textul e un MODEL ORIENTATIV în română; nu e consultanță juridică (vezi CONTRACT_NOTICE).
import { FEE_KINDS, type ClientInfo, type ProjectMeta } from './project-meta';
import { profileLine, type StudioProfile } from './studio-profile';
import { formatMoney } from './format';
import { VAT_RATE } from './rules.boq';

export const CONTRACT_NOTICE = 'Model orientativ, nu constituie consultanță juridică; verificați-l cu un jurist.';

// ---------- etape ----------
export const PHASE_IDS = ['survey', 'concept', 'render3d', 'drawings', 'procurement', 'supervision'] as const;
export type PhaseId = typeof PHASE_IDS[number];
export const PHASE_INFO: Record<PhaseId, { title: string; text: string; deliverables: string[] }> = {
  survey: { title: 'Releveu și analiza spațiului', text: 'măsurarea spațiului, fotografii, stabilirea cerințelor și a bugetului orientativ', deliverables: ['releveu cotat al spațiului existent', 'fișa de cerințe a Beneficiarului'] },
  concept: { title: 'Concept și mobilare', text: 'propunerea de organizare a spațiului, stil, culori și materiale', deliverables: ['plan de mobilare', 'planșă de ambianță cu materiale și finisaje'] },
  render3d: { title: 'Vizualizări 3D', text: 'imagini 3D ale spațiilor proiectate, pentru aprobarea conceptului', deliverables: ['vizualizări 3D ale camerelor proiectate'] },
  drawings: { title: 'Planșe tehnice și deviz', text: 'planuri cotate, finisaje, iluminat, instalații de interior și lista de cantități', deliverables: ['planșe tehnice (PDF)', 'tabel de finisaje', 'deviz de cantități și buget estimativ'] },
  procurement: { title: 'Achiziții', text: 'lista de produse, comparații de oferte, urmărirea comenzilor și a livrărilor', deliverables: ['listă de achiziții cu furnizori și prețuri de referință'] },
  supervision: { title: 'Coordonare și vizite pe șantier', text: 'verificarea execuției față de proiect și clarificări pentru executanți', deliverables: ['rapoarte de vizită pe șantier'] },
};

// ---------- onorariu ----------
/** Intervale orientative de pe piața din România (cercetare 2026); doar indicații, nu o normă și nu o recomandare de preț. */
export const FEE_HINTS: Record<typeof FEE_KINDS[number], { min: number; max: number; unit: 'lei/mp' | '%' | 'lei' | 'lei/oră' }> = {
  per_m2: { min: 50, max: 250, unit: 'lei/mp' },
  percent: { min: 5, max: 15, unit: '%' },
  fixed: { min: 4500, max: 15000, unit: 'lei' },
  hourly: { min: 200, max: 500, unit: 'lei/oră' },
};
export type FeeKind = typeof FEE_KINDS[number];
export interface ContractFee { kind: FeeKind; amount: number; hours?: number }
const r2 = (n: number) => Math.round(n * 100) / 100;

export interface FeeInput { fee?: ContractFee | null; areaM2: number; /** bugetul de mobilare și finisaje (fără manoperă), pentru onorariul procentual */ budgetBase?: number | null; vatPayer: boolean; vatRate?: number }
export interface FeeResult { kind: FeeKind | null; net: number | null; vat: number; gross: number | null; vatRate: number; vatPayer: boolean; base: number | null; reason?: 'no_fee' | 'no_hours' | 'no_budget' }
/** Onorariul net, TVA-ul și totalul. Suma introdusă e FĂRĂ TVA; TVA se adaugă doar dacă studioul e plătitor. */
export function computeFee(i: FeeInput): FeeResult {
  const rate = i.vatRate ?? VAT_RATE, out = (kind: FeeKind | null, net: number | null, base: number | null, reason?: FeeResult['reason']): FeeResult => {
    const vat = net != null && i.vatPayer ? r2(net * rate) : 0;
    return { kind, net, vat, gross: net == null ? null : r2(net + vat), vatRate: rate, vatPayer: i.vatPayer, base, ...(reason ? { reason } : {}) }; };
  const f = i.fee; if (!f) return out(null, null, null, 'no_fee');
  if (f.kind === 'fixed') return out('fixed', r2(f.amount), null);
  if (f.kind === 'per_m2') return out('per_m2', r2(f.amount * Math.max(0, i.areaM2)), r2(Math.max(0, i.areaM2)));
  if (f.kind === 'hourly'){ if (!f.hours || f.hours <= 0) return out('hourly', null, null, 'no_hours'); return out('hourly', r2(f.amount * f.hours), f.hours); }
  if (!i.budgetBase || i.budgetBase <= 0) return out('percent', null, null, 'no_budget');
  return out('percent', r2(i.budgetBase * f.amount / 100), r2(i.budgetBase));
}
/** Onorariul față de intervalul orientativ: 'below' | 'within' | 'above' | null (nu se poate compara). Pur informativ. */
export function hintPosition(f: ContractFee | null | undefined): 'below' | 'within' | 'above' | null {
  if (!f || f.amount <= 0) return null; const h = FEE_HINTS[f.kind]; return f.amount < h.min ? 'below' : f.amount > h.max ? 'above' : 'within';
}

export interface PayRow { key: 'deposit' | 'installment' | 'final'; n: number; pct: number; amount: number }
/** Avans + tranșe egale din rest; ultima tranșă (la predare) preia diferența de rotunjire, deci suma e exactă. Fără tranșe: restul la predare. */
export function paymentSchedule(total: number | null, depositPct: number, installments: number): PayRow[] {
  if (total == null || !(total > 0)) return [];
  const dep = Math.min(100, Math.max(0, depositPct)), rows: PayRow[] = [];
  const depAmt = r2(total * dep / 100); if (dep > 0) rows.push({ key: 'deposit', n: 0, pct: dep, amount: depAmt });
  if (dep >= 100) return rows;
  const parts = Math.max(1, Math.floor(installments)), rest = r2(total - depAmt), restPct = r2(100 - dep);
  let paid = 0, pctPaid = 0;
  for (let k = 1; k <= parts; k++){ const last = k === parts, amount = last ? r2(rest - paid) : r2(rest / parts), pct = last ? r2(restPct - pctPaid) : r2(restPct / parts);
    paid = r2(paid + amount); pctPaid = r2(pctPaid + pct); rows.push({ key: last ? 'final' : 'installment', n: k, pct, amount }); }
  return rows;
}

// ---------- datele contractului (stocate) ----------
export interface ContractData {
  fee?: ContractFee;                 // dacă lipsește, se folosește meta.fee
  vatPayer: boolean; revisions: number; phases: PhaseId[]; depositPct: number; installments: number;
  contractNo?: string; contractDate?: string; validUntil?: string; deliverables?: string; notes?: string;
}
export const DEFAULT_CONTRACT: ContractData = { vatPayer: false, revisions: 2, phases: ['survey', 'concept', 'render3d', 'drawings'], depositPct: 30, installments: 2 };
export const CONTRACT_LIMITS = { revisions: 20, installments: 12, hours: 10000, amount: 1e8, no: 40, deliverables: 1500, notes: 2000 };
export class ContractError extends Error { constructor(msg: string){ super(msg); } }
const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const validDate = (v: string) => DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u0008\u000b-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, '').trim().slice(0, max) : '';
const intIn = (v: unknown, lo: number, hi: number, msg: string) => { if (typeof v !== 'number' || !Number.isInteger(v) || v < lo || v > hi) throw new ContractError(msg); return v; };
/** Validează și curăță datele trimise de interfață; valorile greșite opresc salvarea cu un mesaj. */
export function checkContract(x: unknown): ContractData {
  if (!x || typeof x !== 'object' || Array.isArray(x)) throw new ContractError('Datele contractului sunt invalide.');
  const o = x as Record<string, any>, out: ContractData = { ...DEFAULT_CONTRACT, phases: [...DEFAULT_CONTRACT.phases] };
  if (o.vatPayer !== undefined){ if (typeof o.vatPayer !== 'boolean') throw new ContractError('Opțiunea TVA e invalidă.'); out.vatPayer = o.vatPayer; }
  if (o.revisions !== undefined) out.revisions = intIn(o.revisions, 0, CONTRACT_LIMITS.revisions, 'Numărul de runde de modificări e invalid (0–20).');
  if (o.installments !== undefined) out.installments = intIn(o.installments, 0, CONTRACT_LIMITS.installments, 'Numărul de tranșe e invalid (0–12).');
  if (o.depositPct !== undefined){ if (typeof o.depositPct !== 'number' || !Number.isFinite(o.depositPct) || o.depositPct < 0 || o.depositPct > 100) throw new ContractError('Avansul trebuie să fie între 0 și 100%.'); out.depositPct = r2(o.depositPct); }
  if (o.phases !== undefined){ if (!Array.isArray(o.phases) || o.phases.length > PHASE_IDS.length * 2) throw new ContractError('Etapele sunt invalide.');
    for (const p of o.phases) if (typeof p !== 'string' || !(PHASE_IDS as readonly string[]).includes(p)) throw new ContractError('Etapă necunoscută.');
    const set = new Set<string>(o.phases); if (!set.size) throw new ContractError('Alege cel puțin o etapă.'); out.phases = PHASE_IDS.filter(p => set.has(p)); }
  if (o.fee != null){ const f = o.fee; if (typeof f !== 'object' || Array.isArray(f) || !(FEE_KINDS as readonly string[]).includes(f.kind) || typeof f.amount !== 'number' || !Number.isFinite(f.amount) || f.amount < 0 || f.amount > CONTRACT_LIMITS.amount || (f.kind === 'percent' && f.amount > 100)) throw new ContractError('Onorariul e invalid.');
    const fee: ContractFee = { kind: f.kind, amount: r2(f.amount) };
    if (f.hours != null && f.hours !== ''){ if (typeof f.hours !== 'number' || !Number.isFinite(f.hours) || f.hours < 0 || f.hours > CONTRACT_LIMITS.hours) throw new ContractError('Numărul de ore e invalid.'); if (f.kind === 'hourly') fee.hours = r2(f.hours); }
    out.fee = fee; }
  for (const k of ['contractDate', 'validUntil'] as const){ const v = o[k]; if (v == null || v === '') continue; if (typeof v !== 'string' || !validDate(v)) throw new ContractError('Data nu e validă (AAAA-LL-ZZ).'); out[k] = v; }
  for (const [k, max] of [['contractNo', CONTRACT_LIMITS.no], ['deliverables', CONTRACT_LIMITS.deliverables], ['notes', CONTRACT_LIMITS.notes]] as const){ const v = o[k]; if (v == null || v === '') continue;
    if (typeof v !== 'string' || v.length > max * 2) throw new ContractError('Text prea lung.'); const c = clean(v, max); if (c) out[k] = c; }
  return out;
}
/** Citire tolerantă din baza de date: niciodată nu aruncă. */
export function readContract(x: unknown): ContractData { try { return checkContract(x ?? {}); } catch { return { ...DEFAULT_CONTRACT, phases: [...DEFAULT_CONTRACT.phases] }; } }

// ---------- textul documentelor ----------
export interface DocSection { id: string; title: string; paras: string[]; items?: string[]; table?: { head: string[]; rows: string[][] } }
export interface ContractDoc { kind: 'contract' | 'offer'; title: string; notice: string; studioLines: string[]; dateLine: string; sections: DocSection[]; signatures: { studio: string; client: string } }
export interface DocContext {
  studio: StudioProfile; client: ClientInfo; siteAddress?: string; projectName: string; rooms: { name: string; areaM2: number }[]; areaM2: number;
  meta: Pick<ProjectMeta, 'startDate' | 'dueDate' | 'fee'>; data: ContractData; budgetBase: number | null; today: string; hint?: boolean;
}
const BLANK = '………………………';
export const roDate = (d?: string) => d && DATE_RE.test(d) ? `${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}` : BLANK;
const money = (n: number | null | undefined) => n == null ? BLANK : formatMoney(n, 'RON', 'ro');
const num = (n: number) => n.toLocaleString('ro-RO', { maximumFractionDigits: 2 });
/**
 * Onorariul are O SINGURĂ sursă: contractul (ContractData.fee, tabelul project_contract). Datele vechi se migrează la citire:
 * onorariul din fișa proiectului (meta.fee) și, doar pentru un proiect fără contract salvat, „Proiectare” din buget (sumă fixă).
 * `stored` = datele de contract salvate (null dacă nu există rând).
 */
export function migratedFee(stored: Pick<ContractData, 'fee'> | null, meta: Pick<ProjectMeta, 'fee'>, budgetDesign = 0): ContractFee | undefined {
  if (stored?.fee) return stored.fee;
  if (meta.fee) return { kind: meta.fee.kind, amount: meta.fee.amount };
  if (stored == null && budgetDesign > 0) return { kind: 'fixed', amount: r2(budgetDesign) };
  return undefined;
}
export function effectiveFee(meta: Pick<ProjectMeta, 'fee'>, data: ContractData): ContractFee | undefined { return data.fee ?? (meta.fee ? { kind: meta.fee.kind, amount: meta.fee.amount } : undefined); }
export function feeFor(ctx: DocContext): FeeResult { return computeFee({ fee: effectiveFee(ctx.meta, ctx.data), areaM2: ctx.areaM2, budgetBase: ctx.budgetBase, vatPayer: ctx.data.vatPayer }); }
const studioName = (s: StudioProfile) => s.name || BLANK;
const partyStudio = (s: StudioProfile) => `${studioName(s)}, cu sediul în ${s.address || BLANK}, CUI ${s.cui || BLANK}${s.regCom ? `, înregistrată la Registrul Comerțului sub nr. ${s.regCom}` : ''}${s.email ? `, e-mail ${s.email}` : ''}${s.phone ? `, tel. ${s.phone}` : ''}, denumită în continuare „Prestatorul”`;
const partyClient = (c: ClientInfo) => `${c.name || BLANK}, domiciliat/ă (sediul) în ${c.address || BLANK}${c.phone ? `, tel. ${c.phone}` : ''}${c.email ? `, e-mail ${c.email}` : ''}, denumit/ă în continuare „Beneficiarul”`;
const feeDescription = (f: ContractFee | undefined, r: FeeResult, ctx: DocContext) => {
  if (!f) return 'Onorariul urmează să fie stabilit de părți și trecut în contract înainte de semnare.';
  const vatTxt = r.vatPayer ? `la care se adaugă TVA (${Math.round(r.vatRate * 100)}%)` : 'fără TVA (Prestatorul nu este plătitor de TVA)';
  switch (f.kind){
    case 'fixed': return `Onorariul este un pachet forfetar de ${money(f.amount)}, ${vatTxt}.`;
    case 'per_m2': return `Onorariul se calculează cu ${money(f.amount)}/mp × suprafața proiectată de ${num(ctx.areaM2)} mp = ${money(r.net)}, ${vatTxt}.`;
    case 'hourly': return `Onorariul se calculează cu ${money(f.amount)}/oră × ${f.hours ? num(f.hours) + ' ore estimate' : 'un număr de ore estimat'}${r.net != null ? ` = ${money(r.net)}` : ''}; orele efectiv lucrate peste estimare se facturează cu același tarif, doar cu acordul Beneficiarului. Onorariul este ${vatTxt}.`;
    case 'percent': return `Onorariul este de ${num(f.amount)}% din bugetul de mobilare și finisaje${r.base != null ? ` (estimat la ${money(r.base)}, conform devizului din proiect)` : ''}${r.net != null ? ` = ${money(r.net)}` : ''}; se regularizează la valoarea finală a bugetului aprobat. Onorariul este ${vatTxt}.`;
  }
};
function feeSection(ctx: DocContext, r: FeeResult, f: ContractFee | undefined): DocSection {
  const paras = [feeDescription(f, r, ctx)];
  if (r.net != null) paras.push(`Valoare fără TVA: ${money(r.net)}. TVA: ${r.vatPayer ? money(r.vat) : 'nu se aplică'}. Total de plată: ${money(r.gross)}.`);
  return { id: 'fee', title: 'Onorariul', paras };
}
function paymentSection(ctx: DocContext, r: FeeResult, dateLabel: string): DocSection {
  const rows = paymentSchedule(r.gross, ctx.data.depositPct, ctx.data.installments), label = (p: PayRow) => p.key === 'deposit' ? 'Avans, la semnare' : p.key === 'final' ? `Tranșa ${p.n} (finală), la ${dateLabel}` : `Tranșa ${p.n}, la finalizarea etapei convenite`;
  return { id: 'payment', title: 'Modalitatea de plată', paras: [`Plata se face în lei, prin transfer bancar${ctx.studio.iban ? ` în contul ${ctx.studio.iban}${ctx.studio.bank ? ` deschis la ${ctx.studio.bank}` : ''}` : ''}, în ${PAY_DAYS} zile de la emiterea facturii.`],
    ...(rows.length ? { table: { head: ['Plata', '%', 'Sumă'], rows: rows.map(p => [label(p), num(p.pct), money(p.amount)]) } } : { items: ['Calendarul de plăți se completează după stabilirea onorariului.'] }) };
}
const PAY_DAYS = 5;
function scopeSection(ctx: DocContext): DocSection {
  const where = ctx.siteAddress || ctx.client.address, paras = [`Lucrarea: ${ctx.projectName || BLANK}, la adresa ${where || BLANK}.`];
  if (ctx.areaM2 > 0) paras.push(`Suprafața proiectată: ${num(ctx.areaM2)} mp.`);
  return { id: 'scope', title: 'Obiectul și spațiile proiectate', paras, ...(ctx.rooms.length ? { table: { head: ['Încăpere', 'Suprafață'], rows: ctx.rooms.map(r => [r.name, `${num(r.areaM2)} mp`]) } } : {}) };
}
function phasesSection(ctx: DocContext): DocSection { return { id: 'phases', title: 'Etapele serviciilor', paras: ['Serviciile Prestatorului cuprind următoarele etape:'], items: ctx.data.phases.map(p => `${PHASE_INFO[p].title}: ${PHASE_INFO[p].text}.`) }; }
function deliverablesSection(ctx: DocContext): DocSection {
  const items = ctx.data.phases.flatMap(p => PHASE_INFO[p].deliverables); const extra = ctx.data.deliverables?.split(/\r?\n/).map(s => s.trim()).filter(Boolean) ?? [];
  return { id: 'deliverables', title: 'Livrabile', paras: ['Prestatorul predă Beneficiarului, în format electronic (PDF și imagini):'], items: [...new Set([...items, ...extra])] };
}
function timelineSection(ctx: DocContext): DocSection {
  const { startDate, dueDate } = ctx.meta;
  return { id: 'timeline', title: 'Durata și termene', paras: [`Serviciile încep la ${roDate(startDate)} sau la data primirii avansului, dacă aceasta este ulterioară, și se predau până la ${roDate(dueDate)}. Termenele se prelungesc corespunzător cât timp Beneficiarul întârzie cu datele, deciziile sau plățile necesare.`] };
}
function revisionsSection(ctx: DocContext): DocSection {
  const n = ctx.data.revisions;
  return { id: 'revisions', title: 'Runde de modificări incluse', paras: [n === 0 ? 'Onorariul nu include runde de modificări după prezentarea fiecărei etape; orice modificare se agreează și se tarifează separat.'
    : `Onorariul include ${n} ${n === 1 ? 'rundă' : 'runde'} de modificări pentru fiecare etapă prezentată, în limita conceptului aprobat. Modificările suplimentare sau schimbarea conceptului aprobat se agreează în scris și se tarifează separat.`] };
}
function validityLine(ctx: DocContext){ return ctx.data.validUntil ? `Oferta este valabilă până la ${roDate(ctx.data.validUntil)}.` : `Oferta este valabilă 30 de zile de la data emiterii.`; }
const header = (ctx: DocContext) => [studioName(ctx.studio), profileLine({ ...ctx.studio, name: undefined }), ctx.studio.address || '', ctx.studio.iban ? `IBAN ${ctx.studio.iban}${ctx.studio.bank ? ` · ${ctx.studio.bank}` : ''}` : ''].filter(Boolean);

export function buildContract(ctx: DocContext): ContractDoc {
  const f = effectiveFee(ctx.meta, ctx.data), r = feeFor(ctx), no = ctx.data.contractNo, date = ctx.data.contractDate || ctx.today;
  const sections: DocSection[] = [
    { id: 'parties', title: 'Părțile', paras: [`Între ${partyStudio(ctx.studio)}, și ${partyClient(ctx.client)}, s-a încheiat prezentul contract de prestări servicii de design interior.`] },
    scopeSection(ctx), phasesSection(ctx), deliverablesSection(ctx), timelineSection(ctx), feeSection(ctx, r, f), paymentSection(ctx, r, 'predarea documentației'), revisionsSection(ctx),
    { id: 'obligations', title: 'Obligațiile părților', paras: ['Prestatorul execută serviciile cu diligență profesională și respectă termenele convenite. Beneficiarul pune la dispoziție accesul în spațiu, datele și deciziile necesare și plătește onorariul la termen.',
      'Prestatorul nu este executant al lucrărilor de construcții sau de amenajare și nu garantează prețurile sau disponibilitatea produselor propuse; acestea se verifică la momentul comenzii. Produsele și lucrările se contractează direct de Beneficiar cu furnizorii și executanții, dacă părțile nu convin altfel în scris.'] },
    { id: 'ip', title: 'Proprietate intelectuală și folosirea documentației', paras: ['Planurile, desenele, vizualizările 3D și celelalte documente create de Prestator sunt opere protejate de dreptul de autor (Legea nr. 8/1996) și rămân proprietatea intelectuală a Prestatorului.',
      'Prin plata integrală a onorariului, Beneficiarul primește dreptul neexclusiv de a folosi documentația pentru realizarea proiectului la adresa lucrării. Refolosirea pentru alt spațiu, transmiterea către terți în alt scop sau modificarea fără acordul Prestatorului nu sunt permise.',
      'Prestatorul poate prezenta lucrarea în portofoliul propriu, fără date personale ale Beneficiarului, dacă acesta nu se opune în scris.'] },
    { id: 'gdpr', title: 'Protecția datelor cu caracter personal (GDPR)', paras: [
      'Prestatorul prelucrează, ca operator, datele personale ale Beneficiarului (nume, adresă, telefon, e-mail și datele lucrării) în scopul încheierii și executării prezentului contract (art. 6 alin. 1 lit. b din Regulamentul (UE) 2016/679) și pentru respectarea obligațiilor legale, de exemplu cele contabile și fiscale (lit. c).',
      'Datele se păstrează pe durata contractului și, după încetarea lui, doar cât impun obligațiile legale sau cât e necesar pentru apărarea unor drepturi. Ele nu se vând; pot fi comunicate numai furnizorilor și executanților strict în măsura necesară lucrării, contabilului sau autorităților, când legea o cere.',
      `Beneficiarul are dreptul de acces, rectificare, ștergere, restricționare, portabilitate și opoziție, precum și dreptul de a depune plângere la Autoritatea Națională de Supraveghere a Prelucrării Datelor cu Caracter Personal (ANSPDCP). Cererile se trimit la ${ctx.studio.email || BLANK}.`] },
    { id: 'termination', title: 'Încetarea contractului', paras: ['Oricare parte poate înceta contractul prin notificare scrisă, cu un preaviz de 15 zile. În acest caz Beneficiarul plătește etapele predate și, proporțional, munca depusă la etapele începute; avansul se reține în limita acestor sume, iar eventuala diferență se restituie.',
      'Contractul poate fi reziliat pentru neexecutarea obligațiilor, după o notificare rămasă fără efect 7 zile. Părțile încearcă să rezolve amiabil orice diferend; în lipsa unei înțelegeri, competente sunt instanțele de la sediul Prestatorului.'] },
    { id: 'final', title: 'Dispoziții finale', paras: [`Modificările contractului se fac numai în scris. Prezentul contract se încheie în două exemplare, câte unul pentru fiecare parte${no ? `, sub nr. ${no}` : ''}.`, ...(ctx.data.notes ? [ctx.data.notes] : [])] },
    { id: 'signatures', title: 'Semnături', paras: [`Încheiat astăzi, ${roDate(date)}.`] },
  ];
  return { kind: 'contract', title: `Contract de prestări servicii de design interior${no ? ` nr. ${no}` : ''}`, notice: CONTRACT_NOTICE, studioLines: header(ctx), dateLine: roDate(date), sections, signatures: { studio: `Prestator, ${studioName(ctx.studio)}`, client: `Beneficiar, ${ctx.client.name || BLANK}` } };
}

export function buildOffer(ctx: DocContext): ContractDoc {
  const f = effectiveFee(ctx.meta, ctx.data), r = feeFor(ctx), date = ctx.data.contractDate || ctx.today, pay = paymentSection(ctx, r, 'predare');
  const sections: DocSection[] = [
    { id: 'for', title: 'Pentru', paras: [[ctx.client.name || BLANK, ctx.client.email, ctx.client.phone].filter(Boolean).join(' · ')] },
    scopeSection(ctx), phasesSection(ctx), feeSection(ctx, r, f), pay, revisionsSection(ctx),
    { id: 'validity', title: 'Valabilitatea ofertei', paras: [validityLine(ctx), 'Acceptarea ofertei se confirmă în scris, iar serviciile încep după semnarea contractului și plata avansului.'] },
  ];
  return { kind: 'offer', title: 'Ofertă de servicii de design interior', notice: CONTRACT_NOTICE, studioLines: header(ctx), dateLine: roDate(date), sections, signatures: { studio: studioName(ctx.studio), client: ctx.client.name || BLANK } };
}
