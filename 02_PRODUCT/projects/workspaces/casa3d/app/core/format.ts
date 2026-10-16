// Formatare independentă de piață: bani în moneda ofertei, lungimi și suprafețe metrice sau imperiale, după limba aleasă.
export type Units = 'metric' | 'imperial';
export type Locale = 'ro' | 'en' | (string & {});
const M_PER_FT = 0.3048, M2_PER_FT2 = M_PER_FT * M_PER_FT;
const intlLocale = (l: Locale) => l === 'ro' ? 'ro-RO' : l === 'en' ? 'en-GB' : l;

/** Suma în moneda ei (ISO 4217); `null`/lipsă înseamnă preț necunoscut, nu 0. */
export function formatMoney(amount: number | null | undefined, currency: string, locale: Locale = 'ro', unknown = '—'): string {
  if (amount == null || !Number.isFinite(amount)) return unknown;
  return new Intl.NumberFormat(intlLocale(locale), { style: 'currency', currency, maximumFractionDigits: 0 }).format(amount);
}
/** Diferență cu semn explicit (+/−), pentru comparații între variante sau revizii. */
export function formatMoneyDelta(delta: number, currency: string, locale: Locale = 'ro'): string {
  const s = formatMoney(Math.abs(delta), currency, locale); return delta > 0 ? `+${s}` : delta < 0 ? `−${s}` : s;
}
/** Lungime dată în metri: metric în m (sau cm sub 1 m), imperial în picioare și inci. */
export function formatLength(m: number, units: Units = 'metric', locale: Locale = 'ro'): string {
  if (units === 'imperial'){ const totalIn = Math.round(m / M_PER_FT * 12), ft = Math.floor(totalIn / 12), inch = totalIn - ft * 12; return ft === 0 ? `${inch}″` : inch ? `${ft}′ ${inch}″` : `${ft}′`; }
  const nf = (d: number) => new Intl.NumberFormat(intlLocale(locale), { maximumFractionDigits: d, minimumFractionDigits: d });
  return m < 1 ? `${Math.round(m * 100)} cm` : `${nf(2).format(m)} m`;
}
/** Suprafață dată în m²: metric m², imperial ft². */
export function formatArea(m2: number, units: Units = 'metric', locale: Locale = 'ro'): string {
  const nf = new Intl.NumberFormat(intlLocale(locale), { maximumFractionDigits: 1, minimumFractionDigits: 1 });
  return units === 'imperial' ? `${nf.format(m2 / M2_PER_FT2)} ft²` : `${nf.format(m2)} m²`;
}
export const toMetres = (value: number, units: Units) => units === 'imperial' ? value * M_PER_FT : value;
const CM_PER_IN = 2.54;
/** Eticheta unității pentru câmpurile de lungime mică: „cm” (metric) sau „in” (imperial). */
export const lengthInputUnit = (units: Units): 'cm' | 'in' => units === 'imperial' ? 'in' : 'cm';
/** Valoarea de afișat într-un câmp numeric dat în cm: cm întregi (metric) sau inci cu o zecimală (imperial). Datele rămân metrice. */
export function cmToInput(cm: number, units: Units = 'metric'): number {
  return units === 'imperial' ? Math.round(cm / CM_PER_IN * 10) / 10 : Math.round(cm);
}
/** Valoarea scrisă în câmp (cm sau inci) convertită în cm; `null` dacă nu e un număr finit (virgula zecimală e acceptată). */
export function inputToCm(value: string | number, units: Units = 'metric'): number | null {
  const x = typeof value === 'number' ? value : value.trim() === '' ? NaN : Number(value.replace(',', '.'));
  return Number.isFinite(x) ? (units === 'imperial' ? x * CM_PER_IN : x) : null;
}
/** Moneda unică a ofertelor din catalog (RON dacă nu există nicio ofertă). */
export const catalogCurrency = (c: { offers: { currency: string }[] }): string => c.offers[0]?.currency ?? 'RON';
/** Dimensiuni L×A×Î date în cm: „40×50×80 cm” (metric) sau „1′ 4″ × 1′ 8″ × 2′ 7″” (imperial). */
export function formatDimsCm(d: { w: number; d: number; h: number }, units: Units = 'metric', locale: Locale = 'ro'): string {
  return units === 'imperial' ? [d.w, d.d, d.h].map(c => formatLength(c / 100, 'imperial', locale)).join(' × ') : `${d.w}×${d.d}×${d.h} cm`;
}

// ---- numere, cantități și date: o singură formatare în toată interfața (virgulă zecimală în română, m², buc, ml) ----
/** Unitatea de măsură scrisă corect: „m2”/„mp” → „m²”, „m3” → „m³”, „ml” și „buc” rămân; restul neschimbate. */
export function normalizeUnit(unit: string | null | undefined): string {
  const u = (unit ?? '').trim(), l = u.toLowerCase();
  if (l === 'm2' || l === 'mp' || l === 'm^2' || l === 'sqm') return 'm²';
  if (l === 'm3' || l === 'mc' || l === 'm^3') return 'm³';
  if (l === 'pcs' || l === 'pc' || l === 'buc.') return 'buc';
  return u;
}
/** Un număr cu zecimalele date: `decimals` fix, sau între `min` și `max`. Virgulă zecimală în română, punct de mii. */
export function formatNumber(n: number | null | undefined, locale: Locale = 'ro', o: { decimals?: number; min?: number; max?: number } = {}): string {
  if (n == null || !Number.isFinite(n)) return '—';
  const min = o.decimals ?? o.min ?? 0, max = o.decimals ?? Math.max(min, o.max ?? 2);
  return new Intl.NumberFormat(intlLocale(locale), { minimumFractionDigits: min, maximumFractionDigits: max }).format(n);
}
/** Câte zecimale are o cantitate în unitatea ei: suprafețe, lungimi și volume cu 2, bucăți întregi (sau 2 dacă e fracție). */
export function qtyDecimals(unit: string, qty: number): number {
  const u = normalizeUnit(unit);
  if (u === 'm²' || u === 'm³' || u === 'ml' || u === 'm' || u === 'L' || u === 'kg') return 2;
  return Number.isInteger(qty) ? 0 : 2;
}
/** Cantitate cu unitate: „9,98 m²”, „2,43 ml”, „3 buc”. Aceleași zecimale peste tot (deviz, comenzi, proformă, predare). */
export function formatQty(qty: number | null | undefined, unit: string | null | undefined, locale: Locale = 'ro'): string {
  if (qty == null || !Number.isFinite(qty)) return '—';
  const u = normalizeUnit(unit);
  return u ? `${formatNumber(qty, locale, { decimals: qtyDecimals(u, qty) })} ${u}` : formatNumber(qty, locale, { max: 2 });
}
/** Data în interfață: „15 oct. 2026” (`short`, implicit); în contracte și documente oficiale: „15.10.2026” (`doc`). */
export function formatDate(d: string | number | Date | null | undefined, locale: Locale = 'ro', style: 'short' | 'doc' | 'long' = 'short'): string {
  if (d == null || d === '') return '—';
  const x = typeof d === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(d) ? new Date(d + 'T12:00:00') : new Date(d);
  if (Number.isNaN(x.getTime())) return '—';
  const opts: Intl.DateTimeFormatOptions = style === 'doc' ? { day: '2-digit', month: '2-digit', year: 'numeric' } : style === 'long' ? { day: 'numeric', month: 'long', year: 'numeric' } : { day: 'numeric', month: 'short', year: 'numeric' };
  return x.toLocaleDateString(intlLocale(locale), opts);
}
