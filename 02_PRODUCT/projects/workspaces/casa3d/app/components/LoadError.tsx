'use client';
// Pagina de document care nu se poate încărca (proiect inexistent, șters sau fără acces): aceeași casetă ca pagina 404,
// cu explicație și drum înapoi (audit UI F-36), în locul unui rând de text pe o pagină goală.
import Icon from './Icon';
import { usePrefs } from '@/lib/prefs';

export default function LoadError(){
  const { t } = usePrefs();
  return (<main className="nf" role="alert">
    <div className="nf-card">
      <span className="nf-kicker"><Icon name="alert" size={16} /> {t('nf.loadKicker')}</span>
      <h1>{t('print.loadError')}</h1>
      <p>{t('nf.loadBody')}</p>
      <a className="btn primary" href="/">{t('nf.back')}</a>
    </div>
  </main>);
}
