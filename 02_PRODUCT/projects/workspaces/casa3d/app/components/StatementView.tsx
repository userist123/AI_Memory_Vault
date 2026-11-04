'use client';
import { usePrefs } from '@/lib/prefs';
import { statementFor } from '@/lib/a11y-statement';

export default function StatementView(){
  const { lang } = usePrefs(), s = statementFor(lang);
  return (<main className="home statement" lang={lang} style={{ maxWidth: 760 }}>
    <h1>{s.title}</h1>
    <p className="lead">{s.intro}</p>
    {s.sections.map(x => <section key={x.h}><h2>{x.h}</h2>
      {x.p?.map((t, i) => <p key={i}>{t}</p>)}
      {x.list && <ul>{x.list.map((t, i) => <li key={i}>{t}</li>)}</ul>}</section>)}
  </main>);
}
