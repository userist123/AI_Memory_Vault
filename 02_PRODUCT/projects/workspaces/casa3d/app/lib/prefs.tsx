'use client';
// Preferințe de interfață (limbă + unități), păstrate în localStorage; limba se aplică și pe <html lang>.
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { Units } from '../core/format';
import { detectLang, isLang, pluralTranslator, translator, type Lang, type Translate, type TranslatePlural } from './i18n';

export const LANG_KEY = 'casa3d.lang', UNITS_KEY = 'casa3d.units';
export interface Prefs { lang: Lang; units: Units; setLang(l: Lang): void; setUnits(u: Units): void; t: Translate; tp: TranslatePlural }

const read = (k: string): string | null => { try { return localStorage.getItem(k); } catch { return null; } };
const write = (k: string, v: string) => { try { localStorage.setItem(k, v); } catch { /* mod privat sau stocare blocată: preferința rămâne doar pe sesiune */ } };

const fallback: Prefs = { lang: 'en', units: 'metric', setLang: () => {}, setUnits: () => {}, t: translator('en'), tp: pluralTranslator('en') };
const Ctx = createContext<Prefs>(fallback);

export function PrefsProvider({ children }: { children: ReactNode }){
  // pe server și la prima randare clientul pornește cu valorile implicite (fără nepotriviri la hidratare); preferința reală se citește după montare
  const [lang, setLangState] = useState<Lang>('en'), [units, setUnitsState] = useState<Units>('metric');
  useEffect(() => {
    const l = read(LANG_KEY), u = read(UNITS_KEY);
    setLangState(isLang(l) ? l : detectLang(typeof navigator === 'undefined' ? '' : navigator.language));
    if (u === 'metric' || u === 'imperial') setUnitsState(u);
  }, []);
  useEffect(() => { document.documentElement.lang = lang; }, [lang]);
  const setLang = useCallback((l: Lang) => { setLangState(l); write(LANG_KEY, l); }, []);
  const setUnits = useCallback((u: Units) => { setUnitsState(u); write(UNITS_KEY, u); }, []);
  const value = useMemo<Prefs>(() => ({ lang, units, setLang, setUnits, t: translator(lang), tp: pluralTranslator(lang) }), [lang, units, setLang, setUnits]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
export const usePrefs = () => useContext(Ctx);
