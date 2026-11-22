// Cursuri valutare pe proiect: produsele la comandă pot avea prețul în altă monedă (EUR din pagina magazinului); în deviz,
// comenzi, profit și proformă intră în moneda proiectului, la cursul ținut pe proiect (Snapshot.fx). Cursul stă în instantaneu,
// deci o revizie îl îngheață odată cu planul. Fără curs pentru o monedă folosită, prețul e NECUNOSCUT (nu se adună ca lei).
// Model pur: fără rețea, fără bază de date. Cursul BNR îl aduce lib/fx-bnr.ts; aici e doar parserul XML-ului.

export type FxSource = 'manual' | 'bnr';
/** `rate`: câte unități din moneda proiectului (lei) costă 1 unitate din moneda străină. */
export interface FxRate { rate: number; date: string; source: FxSource }
export type FxRates = Record<string, FxRate>;
export class FxError extends Error {}

export const PROJECT_CURRENCY = 'RON';
/** Monedele propuse în formular; orice alt cod ISO de 3 litere se poate scrie de mână. */
export const FX_COMMON = ['RON', 'EUR', 'USD', 'GBP', 'CHF', 'HUF', 'PLN'] as const;
/**
 * BNR publică 4 zecimale, dar pentru unele monede cursul e pe 100 de unități (HUF, JPY, KRW: multiplier="100"); cursul pe
 * 1 unitate are atunci 6 zecimale (1,4341 / 100 = 0,014341). Se păstrează 6, altfel forintul ar pierde 2% din precizie.
 */
export const FX_LIMITS = { currencies: 30, rateMax: 1000, decimals: 6 };

const CODE_RE = /^[A-Z]{3}$/, DATE_RE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
/** Rotunjire la n zecimale, cu toleranță pentru erorile binare (1,005 → 1,01; 1200 × 4,9771 → 5972,52). */
const round = (v: number, n: number) => { const k = 10 ** n; return Math.round(v * k + 1e-7) / k; };
export const roundRate = (v: number) => round(v, FX_LIMITS.decimals);
const r2 = (v: number) => round(v, 2);
export const isCurrencyCode = (c: unknown): c is string => typeof c === 'string' && CODE_RE.test(c);
export const validFxDate = (v: unknown): v is string => typeof v === 'string' && DATE_RE.test(v) && new Date(v + 'T00:00:00Z').toISOString().startsWith(v);
/** Cursul introdus sau citit: număr finit, 0 < curs ≤ 1000 după rotunjire; altfel null. */
export function cleanRate(v: unknown): number | null {
  if (typeof v !== 'number' || !Number.isFinite(v)) return null; const r = roundRate(v); return r > 0 && r <= FX_LIMITS.rateMax ? r : null;
}

/** Cursurile proiectului, validate strict: orice intrare greșită aruncă FxError (nimic nu se repară pe tăcute). */
export function checkFxRates(x: unknown, projectCurrency: string = PROJECT_CURRENCY): FxRates {
  if (x == null) return {};
  if (typeof x !== 'object' || Array.isArray(x)) throw new FxError('Cursurile valutare sunt invalide.');
  const entries = Object.entries(x as Record<string, unknown>); if (entries.length > FX_LIMITS.currencies) throw new FxError(`Cel mult ${FX_LIMITS.currencies} de monede pe proiect.`);
  // Map, apoi obiect fără prototip moștenit: un cod ciudat nu poate atinge Object.prototype (oricum trec doar 3 litere mari)
  const out = new Map<string, FxRate>();
  for (const [code, raw] of entries){
    if (!isCurrencyCode(code)) throw new FxError('Codul monedei trebuie să aibă 3 litere mari (de ex. EUR).');
    if (code === projectCurrency) throw new FxError(`Moneda proiectului (${projectCurrency}) nu are curs.`);
    const o = raw as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new FxError(`Cursul ${code} e invalid.`);
    const rate = cleanRate(o.rate); if (rate == null) throw new FxError(`Cursul ${code} trebuie să fie un număr între 0 și ${FX_LIMITS.rateMax}.`);
    if (!validFxDate(o.date)) throw new FxError(`Data cursului ${code} nu e validă (AAAA-LL-ZZ).`);
    if (o.source !== 'manual' && o.source !== 'bnr') throw new FxError(`Sursa cursului ${code} e invalidă.`);
    out.set(code, { rate, date: o.date, source: o.source });
  }
  return Object.fromEntries([...out].sort(([a], [b]) => a.localeCompare(b)));
}
const rateOf = (rates: FxRates | null | undefined, code: string): FxRate | null => rates && Object.prototype.hasOwnProperty.call(rates, code) ? rates[code]! : null;
/** „lei”, „LEI”, „leu” scrise pe pagina magazinului sau în CSV înseamnă RON (nu o monedă străină fără curs). */
export const normCurrency = (c: string): string => /^(lei|leu|ron)$/i.test(c.trim()) ? 'RON' : c.trim().toUpperCase();
/** Moneda unui preț e „străină” când e un cod valid, diferit de moneda proiectului (null / gol / aceeași = moneda proiectului). */
export const isForeign = (currency: string | null | undefined, projectCurrency: string = PROJECT_CURRENCY): currency is string =>
  typeof currency === 'string' && currency !== '' && normCurrency(currency) !== projectCurrency;

/**
 * Suma în moneda proiectului. Monedă lipsă sau aceeași cu a proiectului: suma rămâne neschimbată (nici nu se rotunjește).
 * Monedă străină cu curs: sumă × curs, la 2 zecimale. Fără curs: `value` null și `missing` true — suma NU se adună.
 */
export function toProjectCurrency(amount: number | null | undefined, currency: string | null | undefined, projectCurrency: string = PROJECT_CURRENCY, rates?: FxRates | null): { value: number | null; missing: boolean } {
  if (!isForeign(currency, projectCurrency)) return { value: amount ?? null, missing: false };
  const r = rateOf(rates, currency.toUpperCase());
  if (!r) return { value: null, missing: true };
  return { value: amount == null ? null : r2(amount * r.rate), missing: false };
}

/** Ce s-a convertit pe o linie: moneda și prețurile originale, cursul folosit (null = lipsește cursul). */
export interface FxInfo { currency: string; rate: number | null; date: string | null; source: FxSource | null; price: number | null; costPrice: number | null }
type Priced = { price: number | null; costPrice?: number | null; currency?: string | null };
/**
 * Un produs la comandă adus în moneda proiectului: `price` și `costPrice` devin prețuri UNITARE în lei (preț × curs, 2 zecimale),
 * deci totalul liniei rămâne preț unitar × cantitate, ca pe orice document. `fx` ține minte originalul, pentru afișare.
 * Fără curs: ambele prețuri devin null (linie cu preț necunoscut) și `fx.rate` e null. Produsul în moneda proiectului se
 * întoarce NEATINS (același obiect). Rezultatul are moneda proiectului, deci o a doua trecere nu mai schimbă nimic.
 */
export function fxItem<T extends Priced>(item: T, rates?: FxRates | null, projectCurrency: string = PROJECT_CURRENCY): T & { fx?: FxInfo } {
  if (!isForeign(item.currency, projectCurrency)) return item;
  const currency = item.currency.toUpperCase(), r = rateOf(rates, currency), price = item.price ?? null, costPrice = item.costPrice ?? null;
  const fx: FxInfo = { currency, rate: r?.rate ?? null, date: r?.date ?? null, source: r?.source ?? null, price, costPrice };
  return { ...item, price: r && price != null ? r2(price * r.rate) : null, costPrice: r && costPrice != null ? r2(costPrice * r.rate) : null, currency: projectCurrency, fx };
}
export const fxItems = <T extends Priced>(items: readonly T[], rates?: FxRates | null, projectCurrency: string = PROJECT_CURRENCY): (T & { fx?: FxInfo })[] => items.map(i => fxItem(i, rates, projectCurrency));

/** Monedele străine folosite de produse și cele fără curs, sortate. */
export function fxUsage(items: readonly { currency?: string | null; fx?: FxInfo }[], rates?: FxRates | null, projectCurrency: string = PROJECT_CURRENCY): { used: string[]; missing: string[] } {
  const used = new Set<string>(); for (const i of items){ if (i.fx) used.add(i.fx.currency); else if (isForeign(i.currency, projectCurrency) && isCurrencyCode(i.currency.toUpperCase())) used.add(i.currency.toUpperCase()); }
  const list = [...used].sort(); return { used: list, missing: list.filter(c => !rateOf(rates, c)) };
}

// ---------- cursul BNR ----------
export interface BnrRates { date: string; rates: Record<string, number> }
/**
 * Parser strict pentru https://curs.bnr.ro/nbrfxrates.xml: data din `<Cube date="AAAA-LL-ZZ">`, cursurile din
 * `<Rate currency="EUR">5.3482</Rate>`; `multiplier="100"` înseamnă curs pentru 100 de unități (se împarte). Doar regex pe
 * forma exactă: nu se interpretează entități, DTD-uri sau alte elemente. Orice abatere → FxError.
 */
export function parseBnrXml(raw: unknown): BnrRates {
  if (typeof raw !== 'string' || !raw.length) throw new FxError('Răspunsul BNR e gol.');
  // comentariile XML nu sunt date: un <Rate> dintr-un comentariu nu trebuie citit
  const xml = raw.replace(/<!--[\s\S]*?-->/g, '');
  const cubes = [...xml.matchAll(/<Cube\s+date="(\d{4}-\d{2}-\d{2})"\s*>/g)];
  if (cubes.length !== 1 || !validFxDate(cubes[0]![1])) throw new FxError('Răspunsul BNR nu are forma așteptată (data cursului).');
  const m = new Map<string, number>();
  for (const r of xml.matchAll(/<Rate\s+currency="([A-Z]{3})"(?:\s+multiplier="(1|10|100|1000|10000)")?\s*>(\d{1,6}(?:\.\d{1,6})?)<\/Rate>/g)){
    const v = cleanRate(Number(r[3]) / Number(r[2] ?? 1)); if (v != null && r[1] !== PROJECT_CURRENCY && !m.has(r[1]!)) m.set(r[1]!, v);
  }
  if (!m.size) throw new FxError('Răspunsul BNR nu conține cursuri.');
  return { date: cubes[0]![1]!, rates: Object.fromEntries(m) };
}

/** „4,9771” / „0,014341”: cel puțin 4 zecimale (ca BNR), cel mult 6. */
export const formatRate = (rate: number, locale = 'ro-RO') => new Intl.NumberFormat(locale, { minimumFractionDigits: 4, maximumFractionDigits: FX_LIMITS.decimals }).format(rate);

const amount = (v: number, locale: string) => new Intl.NumberFormat(locale, { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2 }).format(v);
/** Numele monedei proiectului pe rânduri: „lei” în română pentru RON, altfel codul. */
export const currencyLabel = (code: string, locale = 'ro-RO') => code === 'RON' && locale.toLowerCase().startsWith('ro') ? 'lei' : code;
/**
 * Textul unui rând convertit: „1.200 EUR × 4,9771 = 5.972,52 lei”. `converted` e prețul unitar în moneda proiectului.
 * Fără curs: `text` e null (interfața scrie „lipsește cursul EUR”), `orig` rămâne prețul original, dacă există.
 */
export function fxRowText(fx: FxInfo, converted: number | null, locale = 'ro-RO', projectCurrency: string = PROJECT_CURRENCY): { orig: string | null; text: string | null } {
  const orig = fx.price != null ? `${amount(fx.price, locale)} ${fx.currency}` : null;
  if (fx.rate == null || orig == null || converted == null) return { orig, text: null };
  return { orig, text: `${orig} × ${formatRate(fx.rate, locale)} = ${amount(converted, locale)} ${currencyLabel(projectCurrency, locale)}` };
}
