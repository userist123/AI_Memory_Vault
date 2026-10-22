'use client';
// Contract și ofertă de servicii (A4, PDF din browser). Textul îl construiește serverul din profilul studioului, fișa proiectului și datele contractului.
import { useEffect, useState } from 'react';
import { DocSkeleton } from './Skeleton';
import type { ContractDoc } from '@/core/contract';
import { usePrefs } from '@/lib/prefs';
import LoadError from './LoadError';

export default function ContractPage({ id, kind }: { id: string; kind: 'contract' | 'offer' }){
  const { t } = usePrefs(), [doc, setDoc] = useState<ContractDoc | null>(null), [err, setErr] = useState(false);
  useEffect(() => { fetch(`/api/projects/${id}/contract`).then(r => r.ok ? r.json() : Promise.reject()).then(j => setDoc(j[kind])).catch(() => setErr(true)); }, [id, kind]);
  if (err) return <LoadError />;
  if (!doc) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  // câmpurile necompletate apar pe document ca „………” (se completează de mână sau în profil); pe ecran le numărăm înainte de tipărire
  const blanks = (JSON.stringify(doc).match(/…{3,}/g) ?? []).length;
  return (<main className="report contract-doc">
    <div className="no-print report-bar"><a className="btn" href={`/p/${id}/${kind === 'contract' ? 'offer' : 'contract'}`}>{t(kind === 'contract' ? 'ctr.openOffer' : 'ctr.openContract')}</a><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    {blanks > 0 && <p className="missing no-print" role="status"><b>{t('doc.missing')}</b><span>{t('doc.blanks', { n: blanks })}</span><a className="btn sm" href="/#studio">{t('doc.fillStudio')}</a></p>}
    <header>{doc.studioLines.map((l, k) => k === 0 ? <b key={k}>{l}</b> : <small key={k}>{l}</small>)}<h1>{doc.title}</h1><small>{doc.dateLine}</small></header>
    <p className="contract-notice">{doc.notice}</p>
    {doc.sections.map(s => <section key={s.id} id={s.id}><h2>{s.title}</h2>
      {s.paras.map((p, k) => <p key={k}>{p}</p>)}
      {s.items && <ul>{s.items.map((i, k) => <li key={k}>{i}</li>)}</ul>}
      {s.table && <table><thead><tr>{s.table.head.map(h => <th key={h}>{h}</th>)}</tr></thead><tbody>{s.table.rows.map((r, k) => <tr key={k}>{r.map((c, j) => <td key={j}>{c}</td>)}</tr>)}</tbody></table>}
      {s.id === 'signatures' && <div className="contract-sign"><div>{doc.signatures.studio}</div><div>{doc.signatures.client}</div></div>}</section>)}
    {kind === 'offer' && <div className="contract-sign"><div>{doc.signatures.studio}</div><div>{doc.signatures.client}</div></div>}
  </main>);
}
