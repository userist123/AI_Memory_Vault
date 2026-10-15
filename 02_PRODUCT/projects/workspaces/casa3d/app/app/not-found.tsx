// Pagina 404 a aplicației, în română și în stilul studioului: o vede mai ales clientul cu un link revocat sau scris greșit.
// E randată pe server, deci textul nu trece prin preferința de limbă din browser (care pornește abia după hidratare).
import { t, type Lang } from '@/lib/i18n';
const RO: Lang = 'ro';
export const metadata = { title: `${t(RO, 'nf.title')} · Casa mea 3D`, robots: { index: false, follow: false } };
export default function NotFound(){
  return (<main className="nf" lang="ro">
    <div className="nf-card">
      <span className="wordmark">Casa<b>3D</b></span>
      <span className="nf-kicker">{t(RO, 'nf.kicker')}</span>
      <h1>{t(RO, 'nf.title')}</h1>
      <p>{t(RO, 'nf.body')}</p>
      <a className="btn primary" href="/">{t(RO, 'nf.home')}</a>
    </div>
  </main>);
}
