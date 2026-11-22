// Situații de lucrări: decontarea pe cantități executate, pe perioade, față de un deviz de bază „fixat” (oferta acceptată).
// Devizul de bază e o copie înghețată a liniilor ofertei (manoperă, materiale, servicii) cu cantitatea și prețul unitar de atunci,
// ca modificările ulterioare ale proiectului să nu rescrie trecutul. Fiecare situație are cantitățile executate în perioada ei;
// cumulatele, restul de executat și procentele se calculează. Lucrările suplimentare (aprobate separat) apar pe linii proprii.
// Valorile vin doar din ofertă și din ce introduce omul (CONSTITUTION art. 4). Documentul e un instrument de lucru între
// părți, nu un formular oficial. Funcții pure.

export type BaseKind = 'labor' | 'material' | 'service' | 'extra';
export interface BaseLine { key: string; label: string; unit: string; qty: number; unitPrice: number; kind: BaseKind }
/** `vatIncluded`: prețurile devizului includ deja TVA (oferta către client); atunci situația nu mai adaugă TVA, ca să nu se dubleze. */
export interface Baseline { createdAt: string; lines: BaseLine[]; vatIncluded?: boolean }
/** Măsurătorile care justifică o cantitate (foaia de atașament): rânduri descriere + cantitate, pe codul liniei. */
export interface Measure { label: string; qty: number }
export interface StatementInput { id: string; number: number; from: string; to: string; qty: Record<string, number>; extra: BaseLine[]; advancePct?: number; retentionPct?: number; vatPct?: number; note?: string; measures?: Record<string, Measure[]> }
export class ProgressError extends Error {}
export const PROGRESS_LIMITS = { lines: 2000, statements: 120, extra: 200, label: 200, unit: 12, note: 1000, pctMax: 100, valueMax: 1e9, measuresPerLine: 50 } as const;

const DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const KEY_RE = /^[\w:.@#/-]{1,120}$/;
// id-ul situației ajunge într-un segment de URL ([sid]): fără „/” și „#”, ca o situație să poată fi mereu editată și ștearsă
const SID_RE = /^[\w.-]{1,120}$/;
const r2 = (n: number) => Math.round(n * 100) / 100, r3 = (n: number) => Math.round(n * 1000) / 1000;
const clean = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f\u007f<>]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max) : '';
const num = (v: unknown, lo: number, hi: number) => typeof v === 'number' && Number.isFinite(v) && v >= lo && v <= hi;
const validDate = (v: unknown): v is string => typeof v === 'string' && DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);

function checkLine(x: any, kind?: BaseKind): BaseLine {
  if (!x || typeof x !== 'object' || typeof x.key !== 'string' || !KEY_RE.test(x.key)) throw new ProgressError('O linie a devizului nu are un cod valid.');
  const label = clean(x.label, PROGRESS_LIMITS.label), unit = clean(x.unit, PROGRESS_LIMITS.unit);
  if (!label || !unit) throw new ProgressError('Fiecare linie are nevoie de denumire și unitate de măsură.');
  if (!num(x.qty, 0, 1e7) || !num(x.unitPrice, 0, PROGRESS_LIMITS.valueMax)) throw new ProgressError(`Cantitatea sau prețul liniei „${label}” nu sunt valide.`);
  const k = kind ?? (['labor', 'material', 'service', 'extra'].includes(x.kind) ? x.kind : null); if (!k) throw new ProgressError('Tipul liniei e necunoscut.');
  return { key: x.key, label, unit, qty: r3(x.qty), unitPrice: r2(x.unitPrice), kind: k };
}
/** Devizul de bază, validat: coduri unice, cantități și prețuri finite. */
export function checkBaseline(x: unknown): Baseline {
  const o = x as any; if (!o || typeof o !== 'object' || !Array.isArray(o.lines) || !validDate(String(o.createdAt ?? '').slice(0, 10))) throw new ProgressError('Devizul de bază nu e valid.');
  if (o.lines.length > PROGRESS_LIMITS.lines) throw new ProgressError('Devizul are prea multe linii.');
  const lines = o.lines.map((l: unknown) => checkLine(l)), seen = new Set<string>();
  for (const l of lines){ if (seen.has(l.key)) throw new ProgressError('Două linii au același cod.'); seen.add(l.key); }
  return { createdAt: String(o.createdAt), lines, ...(o.vatIncluded === true ? { vatIncluded: true } : {}) };
}
/** O situație: perioada (de la ≤ până la), cantitățile din perioadă (≥ 0, doar pe codurile devizului), lucrările suplimentare, procentele. */
export function checkStatement(x: unknown, base: Baseline): StatementInput {
  const o = x as any; if (!o || typeof o !== 'object') throw new ProgressError('Situația nu e validă.');
  if (typeof o.id !== 'string' || !SID_RE.test(o.id) || !Number.isInteger(o.number) || o.number < 1 || o.number > 9999) throw new ProgressError('Situația nu are un număr valid.');
  if (!validDate(o.from) || !validDate(o.to) || o.to < o.from) throw new ProgressError('Perioada situației nu e validă (de la ≤ până la, AAAA-LL-ZZ).');
  const keys = new Set(base.lines.map(l => l.key)), qty: Record<string, number> = {};
  for (const [k, v] of Object.entries(o.qty ?? {})){ if (!keys.has(k)) continue; if (!num(v, 0, 1e7)) throw new ProgressError('O cantitate executată nu e validă.'); if ((v as number) > 0) qty[k] = r3(v as number); }
  const extra = (Array.isArray(o.extra) ? o.extra : []).slice(0, PROGRESS_LIMITS.extra).map((l: unknown) => checkLine(l, 'extra'));
  const out: StatementInput = { id: o.id, number: o.number, from: o.from, to: o.to, qty, extra };
  if (o.measures != null){ if (typeof o.measures !== 'object' || Array.isArray(o.measures)) throw new ProgressError('Măsurătorile nu sunt valide.'); const ms: Record<string, Measure[]> = {};
    for (const [k, v] of Object.entries(o.measures)){ if (!keys.has(k) || !Array.isArray(v)) continue; if (v.length > PROGRESS_LIMITS.measuresPerLine) throw new ProgressError('Prea multe măsurători pe o linie.');
      const rows = v.map((m: any) => { const label = clean(m?.label, PROGRESS_LIMITS.label); if (!label || !num(m?.qty, 0, 1e7)) throw new ProgressError('O măsurătoare nu e validă (descriere și cantitate).'); return { label, qty: r3(m.qty) }; });
      if (rows.length) ms[k] = rows; }
    if (Object.keys(ms).length) out.measures = ms; }
  for (const k of ['advancePct', 'retentionPct', 'vatPct'] as const){ if (o[k] == null) continue; if (!num(o[k], 0, PROGRESS_LIMITS.pctMax)) throw new ProgressError('Un procent nu e valid (0–100).'); out[k] = r2(o[k]); }
  if ((out.advancePct ?? 0) + (out.retentionPct ?? 0) > PROGRESS_LIMITS.pctMax) throw new ProgressError('Avansul recuperat și garanția reținută depășesc împreună 100% din valoarea perioadei.');
  const note = clean(o.note, PROGRESS_LIMITS.note); if (note) out.note = note;
  return out;
}

export interface StatementRow { key: string; label: string; unit: string; kind: BaseKind; contractQty: number; prevQty: number; thisQty: number; cumQty: number; remainingQty: number;
  pct: number | null; unitPrice: number; thisValue: number; cumValue: number; over: boolean }
export interface StatementTotals { contractValue: number; prevValue: number; thisValue: number; cumValue: number; pct: number | null; extraThis: number;
  advance: number; retention: number; vat: number | null; payable: number }
/** Situația nr. `number` (din lista `all`, oricare ordine): rândurile pe devizul de bază + lucrările suplimentare ale situației. */
export function computeStatement(base: Baseline, all: StatementInput[], number: number): { rows: StatementRow[]; extra: StatementRow[]; totals: StatementTotals; warnings: { key: string; code: 'OVER' | 'MEASURES' }[] } {
  const cur = all.find(s => s.number === number); if (!cur) throw new ProgressError('Situația nu există.');
  const prev = all.filter(s => s.number < number);
  const rows = base.lines.map(l => { const prevQty = r3(prev.reduce((a, s) => a + (s.qty[l.key] ?? 0), 0)), thisQty = cur.qty[l.key] ?? 0, cumQty = r3(prevQty + thisQty);
    return { key: l.key, label: l.label, unit: l.unit, kind: l.kind, contractQty: l.qty, prevQty, thisQty, cumQty, remainingQty: r3(l.qty - cumQty), pct: l.qty > 0 ? r2(cumQty / l.qty * 100) : null,
      unitPrice: l.unitPrice, thisValue: r2(thisQty * l.unitPrice), cumValue: r2(cumQty * l.unitPrice), over: cumQty > l.qty + 1e-9 }; });
  const extra = cur.extra.map(l => ({ key: l.key, label: l.label, unit: l.unit, kind: 'extra' as BaseKind, contractQty: 0, prevQty: 0, thisQty: l.qty, cumQty: l.qty, remainingQty: 0, pct: null,
    unitPrice: l.unitPrice, thisValue: r2(l.qty * l.unitPrice), cumValue: r2(l.qty * l.unitPrice), over: false }));
  const sum = (xs: StatementRow[], f: (r: StatementRow) => number) => r2(xs.reduce((a, r) => a + f(r), 0));
  const contractValue = r2(base.lines.reduce((a, l) => a + l.qty * l.unitPrice, 0)), thisBase = sum(rows, r => r.thisValue), cumValue = sum(rows, r => r.cumValue), extraThis = sum(extra, r => r.thisValue);
  // TVA doar dacă e declarat pe situație (plătitor sau nu de TVA nu se presupune); avansul și reținerea se aplică valorii fără TVA
  const thisValue = r2(thisBase + extraThis), advance = r2(thisValue * (cur.advancePct ?? 0) / 100), retention = r2(thisValue * (cur.retentionPct ?? 0) / 100);
  const vat = cur.vatPct != null && !base.vatIncluded ? r2((thisValue - advance) * cur.vatPct / 100) : null;
  // foaia de atașament: dacă o linie are măsurători, suma lor trebuie să fie cantitatea din perioadă
  const measureWarn = Object.entries(cur.measures ?? {}).filter(([k, ms]) => Math.abs(ms.reduce((a, m) => a + m.qty, 0) - (cur.qty[k] ?? 0)) > 1e-6).map(([k]) => ({ key: k, code: 'MEASURES' as const }));
  return { rows, extra, warnings: [...rows.filter(r => r.over).map(r => ({ key: r.key, code: 'OVER' as const })), ...measureWarn],
    totals: { contractValue, prevValue: sum(rows, r => r.prevQty * r.unitPrice), thisValue, cumValue, pct: contractValue > 0 ? r2(cumValue / contractValue * 100) : null, extraThis, advance, retention, vat, payable: r2(thisValue - advance - retention + (vat ?? 0)) } };
}
/** Perioadele nu se suprapun și numerele sunt consecutive (1, 2, 3…): ce nu se potrivește, ca avertismente. */
export function statementIssues(all: StatementInput[]): { code: 'GAP' | 'OVERLAP'; number: number }[] {
  const s = [...all].sort((a, b) => a.number - b.number), out: { code: 'GAP' | 'OVERLAP'; number: number }[] = [];
  s.forEach((x, i) => { if (x.number !== i + 1) out.push({ code: 'GAP', number: x.number }); const p = s[i - 1]; if (p && x.from <= p.to) out.push({ code: 'OVERLAP', number: x.number }); });
  return out;
}
