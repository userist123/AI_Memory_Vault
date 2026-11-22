'use client';
// Pe linkul clientului: jurnalul de șantier, doar intrările pe care designerul le-a făcut vizibile (data, textul, meteo,
// pozele). Numărul de oameni și meseriile nu ajung aici: serverul nu le trimite (lib/site-log.ts siteLogForShare).
import type { ClientLogEntry } from '@/core/site-log';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

export default function ClientSiteLog({ token, entries }: { token: string; entries: ClientLogEntry[] }){
  const { t, lang } = usePrefs(), day = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString(intlLocale(lang), { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  if (!entries.length) return null;
  const src = (p: string) => `/api/share/${token}/render/${p}?src=log`;
  return (<section className="cslog no-print" aria-labelledby="cslog-h">
    <header><span className="kicker">{t('slog.clientKicker')}</span><h2 id="cslog-h">{t('slog.title')}</h2><p className="muted">{t('slog.clientSub')}</p></header>
    <ol>{entries.map(e => <li key={e.id} className="cslog-row">
      <div className="cslog-head"><time dateTime={e.date}>{day(e.date)}</time>{e.weather && <small>{e.weather}</small>}</div>
      <p>{e.text}</p>
      {e.photos.length > 0 && <div className="cslog-photos">{e.photos.map(p => <a key={p} href={src(p)} target="_blank" rel="noopener"><img src={src(p)} alt={t('slog.photoAlt', { d: day(e.date) })} loading="lazy" /></a>)}</div>}
    </li>)}</ol>
  </section>);
}
