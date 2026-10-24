'use client';
import { usePrefs } from '@/lib/prefs';
import PrefsSwitcher from './PrefsSwitcher';
import PrintView from './PrintView';

/** Pagina de tipărire: limba și unitățile vin din preferințe; selectorul nu apare pe hârtie. */
export default function PrintPage({ id }: { id: string }){
  const { lang, units } = usePrefs();
  return (<><div className="no-print" style={{ display: 'flex', justifyContent: 'flex-end', padding: '8px 12px 0' }}><PrefsSwitcher /></div><PrintView id={id} locale={lang} units={units} /></>);
}
