// Produsele proprii în CSV: citire (separator ; sau ,, virgulă zecimală, ghilimele, BOM, antete RO/EN fără diacritice),
// export protejat împotriva injecției de formule și fișierul-șablon. Validarea de autoritate rămâne checkCustomItem (server).
import { checkCustomItem, ClipError, type CustomItemInput } from './clip';
import { safeCell, FORMULA_START } from './csv-safe';
export { safeCell };

export const CSV_LIMITS = { rows: 500, bytes: 200 * 1024 };
export type CsvField = 'name' | 'supplier' | 'sku' | 'url' | 'unit' | 'qty' | 'costPrice' | 'price' | 'roomId' | 'leadDays' | 'dimension' | 'currency';
const ALIASES: Record<CsvField, string[]> = {
  name: ['denumire', 'nume', 'name', 'produs', 'product', 'articol'], supplier: ['furnizor', 'supplier', 'vendor'], sku: ['cod', 'sku', 'cod_produs', 'code'],
  url: ['link', 'url'], unit: ['um', 'unit', 'unitate'], qty: ['cantitate', 'qty', 'quantity', 'cant'], costPrice: ['pret_achizitie', 'cost', 'cost_price', 'pret_cost'],
  price: ['pret_vanzare', 'price', 'pret', 'list_price'], roomId: ['camera', 'room'], leadDays: ['termen_zile', 'lead_days', 'lead_time', 'termen'],
  dimension: ['dimensiune', 'dimensiuni', 'dimension', 'dimensions'], currency: ['moneda', 'currency'] };
export const TEMPLATE_HEADER = ['denumire', 'furnizor', 'cod', 'link', 'um', 'cantitate', 'pret_achizitie', 'pret_vanzare', 'camera', 'termen_zile', 'dimensiune'];
export const normHeader = (h: string) => h.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
const FIELD_OF = new Map<string, CsvField>(); for (const [f, as] of Object.entries(ALIASES)) for (const a of as) FIELD_OF.set(a, f as CsvField);
/** Același text, fără diacritice și litere mici: așa se potrivesc numele camerelor. */
export const normText = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();

/** Rânduri de celule; ghilimele duble „""”, separatorul și rândurile noi în câmpuri între ghilimele. */
export function splitCsv(text: string, sep: string): string[][] {
  const rows: string[][] = []; let row: string[] = [], cell = '', q = false, any = false;
  for (let i = 0; i < text.length; i++){ const c = text[i]!;
    if (q){ if (c === '"'){ if (text[i + 1] === '"'){ cell += '"'; i++; } else q = false; } else cell += c; continue; }
    if (c === '"' && cell === ''){ q = true; any = true; }
    else if (c === sep){ row.push(cell); cell = ''; any = true; }
    else if (c === '\n' || c === '\r'){ if (c === '\r' && text[i + 1] === '\n') i++; if (any || cell !== ''){ row.push(cell); rows.push(row); } row = []; cell = ''; any = false; }
    else { cell += c; any = true; } }
  if (q) throw new ClipError('CSV invalid: ghilimele neînchise.');
  if (any || cell !== ''){ row.push(cell); rows.push(row); }
  return rows;
}
function detectSep(text: string){ let q = false, sc = 0, cm = 0; for (const c of text){ if (c === '"') q = !q; else if (!q){ if (c === '\n' || c === '\r') break; if (c === ';') sc++; else if (c === ',') cm++; } } return sc >= cm && sc > 0 ? ';' : cm > 0 ? ',' : ';'; }
/** „1.234,56”, „12,5”, „1,234.56”, „12.5” → număr; text gol → null; altceva → NaN. */
export function parseNumber(raw: string): number | null {
  const s = raw.replace(/[\s ]/g, ''); if (s === '') return null;
  if (!/^[+-]?[\d.,]+$/.test(s)) return NaN;
  const lc = s.lastIndexOf(','), ld = s.lastIndexOf('.'); let t: string;
  if (lc >= 0 && ld >= 0) t = lc > ld ? s.replace(/\./g, '').replace(',', '.') : s.replace(/,/g, '');
  else if (lc >= 0) t = (s.match(/,/g)!.length > 1 ? s.replace(/,/g, '') : s.replace(',', '.'));
  else t = s.match(/\./g)?.length! > 1 ? s.replace(/\./g, '') : s;
  const n = Number(t); return Number.isFinite(n) ? n : NaN;
}
const unq = (s: string) => s.startsWith("'") && FORMULA_START.test(s.slice(1)) ? s.slice(1) : s;   // inversul protecției de la export (exporturile o pun la loc)

export interface CsvRow { line: number; item: CustomItemInput | null; errors: string[]; roomName: string | null }
export interface CsvParse { rows: CsvRow[]; columns: CsvField[]; ok: number; failed: number }
/** Citește CSV-ul; `resolveRoom` transformă numele camerei în id (null = necunoscută). Erorile pe rând nu opresc citirea. */
export function parseCustomCsv(input: string, resolveRoom: (name: string) => string | null = () => null): CsvParse {
  if (typeof input !== 'string') throw new ClipError('CSV invalid.');
  if (Buffer.byteLength(input, 'utf8') > CSV_LIMITS.bytes) throw new ClipError(`Fișierul depășește ${CSV_LIMITS.bytes / 1024} KB.`);
  const text = input.replace(/^﻿/, ''), all = splitCsv(text, detectSep(text)).filter(r => r.some(c => c.trim() !== ''));
  if (!all.length) throw new ClipError('CSV-ul e gol.');
  const head = all[0]!.map(h => FIELD_OF.get(normHeader(h)) ?? null), idx = new Map<CsvField, number>();
  head.forEach((f, i) => { if (f && !idx.has(f)) idx.set(f, i); });
  if (!idx.has('name') || !idx.has('supplier')) throw new ClipError('Antetul trebuie să conțină cel puțin coloanele denumire și furnizor.');
  const data = all.slice(1); if (data.length > CSV_LIMITS.rows) throw new ClipError(`Cel mult ${CSV_LIMITS.rows} de rânduri.`);
  const rows: CsvRow[] = data.map((cells, k) => {
    const line = k + 2, errors: string[] = [], get = (f: CsvField) => { const i = idx.get(f); return i == null ? '' : unq((cells[i] ?? '').trim()); };
    const num = (f: CsvField, label: string) => { const v = parseNumber(get(f)); if (Number.isNaN(v)) { errors.push(`${label} nu e număr.`); return null; } return v; };
    const raw: Record<string, unknown> = { name: get('name'), supplier: get('supplier'), sku: get('sku'), url: get('url') || null, unit: get('unit'), dimension: get('dimension'), currency: get('currency') || null };
    const qty = num('qty', 'Cantitatea'), cost = num('costPrice', 'Prețul de achiziție'), price = num('price', 'Prețul de vânzare'), lead = num('leadDays', 'Termenul');
    raw.qty = qty ?? 1; raw.costPrice = cost; raw.price = price; raw.leadDays = lead;
    const roomName = get('roomId') || null; let roomId: string | null = null;
    if (roomName){ roomId = resolveRoom(roomName); if (!roomId) errors.push(`Camera „${roomName}” nu există în proiect.`); }
    raw.roomId = roomId;
    let item: CustomItemInput | null = null;
    if (!errors.length) try { item = checkCustomItem(raw) as CustomItemInput; } catch (e: any){ if (e instanceof ClipError) errors.push(e.message); else throw e; }
    return { line, item: errors.length ? null : item, errors, roomName };
  });
  const ok = rows.filter(r => r.item).length; return { rows, columns: [...idx.keys()], ok, failed: rows.length - ok };
}

// safeCell (core/csv-safe.ts): text care începe cu = + - @ (sau tab/CR) primește apostrof în față, ca Excel să nu o execute ca formulă.
const cell = (s: string) => /[";\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
const dec = (v: number | null | undefined) => v == null ? '' : String(v).replace('.', ',');
const toCsv = (rows: string[][]) => '﻿' + rows.map(r => r.map(cell).join(';')).join('\r\n') + '\r\n';
export const customTemplateCsv = () => toCsv([TEMPLATE_HEADER, ['Canapea la comandă', 'Atelier Exemplu', 'CAN-01', 'https://exemplu.ro/canapea', 'buc', '1', '3200,50', '4500', 'Living', '42', '240x95x85 cm']]);
export interface ExportItem extends CustomItemInput { roomName?: string | null }
/** Exportul produselor proprii (cu prețul de achiziție: fișier doar pentru proprietar), ; și virgulă zecimală, reimportabil. */
export function customItemsCsv(items: ExportItem[]): string {
  return toCsv([TEMPLATE_HEADER, ...items.map(i => [safeCell(i.name), safeCell(i.supplier), safeCell(i.sku), safeCell(i.url), safeCell(i.unit), dec(i.qty), dec(i.costPrice), dec(i.price), safeCell(i.roomName), i.leadDays == null ? '' : String(i.leadDays), safeCell(i.dimension)])]);
}

/** Totaluri pentru buget: vânzare (către client), achiziție (ce plătește studioul) și marja; produsele fără preț se numără separat. */
export function customBudget(items: Pick<CustomItemInput, 'price' | 'costPrice' | 'qty' | 'supplier'>[]){
  const r2 = (v: number) => Math.round(v * 100) / 100; let sell = 0, cost = 0, noPrice = 0, noCost = 0; const by = new Map<string, number>();   // Map: un furnizor „__proto__” nu atinge prototipul
  for (const i of items){ if (i.price == null) noPrice++; else { sell += i.price * i.qty; by.set(i.supplier, (by.get(i.supplier) ?? 0) + i.price * i.qty); } if (i.costPrice == null) noCost++; else cost += i.costPrice * i.qty; }
  return { count: items.length, sell: r2(sell), cost: r2(cost), margin: r2(sell - cost), noPrice, noCost, bySupplier: Object.fromEntries([...by].map(([k, v]) => [k, r2(v)])) as Record<string, number> };
}
