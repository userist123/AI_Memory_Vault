// Textele interfeței, pe limbă. Dicționare plate (cheie → text cu `{variabile}`); româna e limba de rezervă.
// Adăugarea unei limbi: un obiect nou în lib/locales/<cod>.ts, o intrare în DICTS și una în LANGS (testul cere aceleași chei ca în română).
import type { Advice } from '../core/advisor';
import { formatArea, formatLength, formatMoney, type Units } from '../core/format';
import { ro } from './locales/ro';
import { en } from './locales/en';

export type Lang = 'ro' | 'en';
export type Dict = Record<string, string>;
export type Vars = Record<string, string | number>;
export const DEFAULT_LANG: Lang = 'ro';
export const DICTS: Record<Lang, Dict> = { ro, en };
/** Numele limbii în limba ei, pentru selector. */
export const LANGS: { code: Lang; name: string }[] = [{ code: 'ro', name: 'Română' }, { code: 'en', name: 'English' }];
export const isLang = (x: unknown): x is Lang => typeof x === 'string' && Object.hasOwn(DICTS, x);
export const detectLang = (navigatorLanguage?: string | null): Lang => /^ro\b/i.test(navigatorLanguage ?? '') ? 'ro' : 'en';

/** Textul cheii în limba cerută. Cheie lipsă în limba aleasă → textul românesc; lipsă și acolo → cheia însăși (se vede imediat în dezvoltare). */
export function t(lang: Lang, key: string, vars?: Vars): string {
  const s = DICTS[lang]?.[key] ?? DICTS[DEFAULT_LANG][key] ?? key;
  return vars ? s.replace(/\{(\w+)\}/g, (m, k: string) => k in vars ? String(vars[k]) : m) : s;
}
/** Singular/plural: pentru n = 1 se folosește `<cheie>.one` dacă există, altfel `<cheie>`; `{n}` e adăugat automat în variabile. */
export function tp(lang: Lang, key: string, n: number, vars?: Vars): string {
  const one = `${key}.one`;
  return t(lang, n === 1 && (one in DICTS[lang] || one in DICTS[DEFAULT_LANG]) ? one : key, { n, ...vars });
}
export type Translate = (key: string, vars?: Vars) => string;
export type TranslatePlural = (key: string, n: number, vars?: Vars) => string;
export const translator = (lang: Lang): Translate => (key, vars) => t(lang, key, vars);
export const pluralTranslator = (lang: Lang): TranslatePlural => (key, n, vars) => tp(lang, key, n, vars);

/** Localizarea Intl pentru date și numere. */
export const intlLocale = (lang: Lang) => lang === 'ro' ? 'ro-RO' : 'en-GB';
/** Număr zecimal în limba aleasă (maxim 2 zecimale). */
export const formatNumber = (n: number, lang: Lang, digits = 2) => new Intl.NumberFormat(intlLocale(lang), { maximumFractionDigits: digits }).format(n);

/** Lungime rotunjită în sus (pentru „cel puțin”): cm întregi sau inci întregi. */
function lengthUp(m: number, units: Units, lang: Lang){
  const up = units === 'imperial' ? Math.ceil(m / 0.0254 - 1e-6) * 0.0254 : Math.max(0.01, Math.ceil(m * 100 - 1e-6) / 100);
  return formatLength(up, units, lang);
}
const FALLBACK_NAMES = ['nm', 'other', 'rn'] as const;

/** Variabilele unui sfat → text: lungimi/suprafețe/sume după unități, limbă și monedă; numele lipsă primesc textul de rezervă din dicționar. */
export function adviceVars(lang: Lang, units: Units, vars: Advice['vars'], currency = 'RON'): Vars {
  const out: Vars = {};
  for (const [k, v] of Object.entries(vars)){
    if (typeof v === 'string'){ out[k] = v === '' && (FALLBACK_NAMES as readonly string[]).includes(k) ? t(lang, `advice.fallback.${k}`) : v; continue; }
    out[k] = k.endsWith('_mu') ? lengthUp(v, units, lang) : k.endsWith('_m2') ? formatArea(v, units, lang) : k.endsWith('_m') ? formatLength(v, units, lang)
      : k.endsWith('_money') ? formatMoney(v, String(vars.cur ?? currency), lang) : k.endsWith('_n') ? formatNumber(v, lang) : v;
  }
  return out;
}
/** Titlul, motivul și remedierea unui sfat în limba aleasă. */
export function adviceText(lang: Lang, a: Pick<Advice, 'code' | 'vars'>, units: Units = 'metric', currency = 'RON'): { title: string; why: string; fix: string } {
  const v = adviceVars(lang, units, a.vars, currency), k = `advice.${a.code}`;
  return { title: t(lang, `${k}.title`, v), why: t(lang, `${k}.why`, v), fix: t(lang, `${k}.fix`, v) };
}

/** Textul unei probleme de validare (sau al unei operații refuzate) în limba aleasă; variabilele `*_m` sunt lungimi
 *  formatate după unități. Fără cheie cunoscută → mesajul românesc original (de ex. probleme venite de la server). */
export function issueText(lang: Lang, i: { message: string; key?: string; vars?: Record<string, string | number>; cause?: { message: string; key?: string; vars?: Record<string, string | number> } }, units: Units = 'metric'): string {
  if (!i.key || !(i.key in DICTS[lang] || i.key in DICTS[DEFAULT_LANG])) return i.message;
  const v: Vars = {}; for (const [k, x] of Object.entries(i.vars ?? {})) v[k] = k.endsWith('_m') && typeof x === 'number' ? formatLength(x, units, lang) : x;
  if (i.cause) v.msg = issueText(lang, i.cause, units);
  return t(lang, i.key, v);
}
