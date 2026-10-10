'use client';
import { LANGS, isLang } from '@/lib/i18n';
import { usePrefs } from '@/lib/prefs';

/** Selectoare compacte pentru limbă și unități (editor, tipărire, pagina de partajare). */
export default function PrefsSwitcher({ className = '' }: { className?: string }){
  const { lang, units, setLang, setUnits, t } = usePrefs();
  const sel = { minHeight: 32, padding: '0 6px' } as const;
  return (<span className={`prefs ${className}`} style={{ display: 'inline-flex', gap: 6 }}>
    <select className="btn" style={sel} value={lang} aria-label={t('prefs.language')} title={t('prefs.language')} onChange={e => { if (isLang(e.target.value)) setLang(e.target.value); }}>
      {LANGS.map(l => <option key={l.code} value={l.code}>{l.name}</option>)}</select>
    <select className="btn" style={sel} value={units} aria-label={t('prefs.units')} title={t('prefs.units')} onChange={e => setUnits(e.target.value === 'imperial' ? 'imperial' : 'metric')}>
      <option value="metric">{t('prefs.metric')}</option><option value="imperial">{t('prefs.imperial')}</option></select>
  </span>);
}
