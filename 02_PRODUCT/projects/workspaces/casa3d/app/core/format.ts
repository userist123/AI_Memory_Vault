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
  if (units === 'imperial'){ const totalIn = Math.round(m / M_PER_FT * 12), ft = Math.floor(totalIn / 12), inch = totalIn - ft * 12; return inch ? `${ft}′ ${inch}″` : `${ft}′`; }
  const nf = (d: number) => new Intl.NumberFormat(intlLocale(locale), { maximumFractionDigits: d, minimumFractionDigits: d });
  return m < 1 ? `${Math.round(m * 100)} cm` : `${nf(2).format(m)} m`;
}
/** Suprafață dată în m²: metric m², imperial ft². */
export function formatArea(m2: number, units: Units = 'metric', locale: Locale = 'ro'): string {
  const nf = new Intl.NumberFormat(intlLocale(locale), { maximumFractionDigits: 1, minimumFractionDigits: 1 });
  return units === 'imperial' ? `${nf.format(m2 / M2_PER_FT2)} ft²` : `${nf.format(m2)} m²`;
}
export const toMetres = (value: number, units: Units) => units === 'imperial' ? value * M_PER_FT : value;
