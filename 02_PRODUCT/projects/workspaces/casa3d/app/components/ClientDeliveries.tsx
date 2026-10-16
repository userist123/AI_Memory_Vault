'use client';
// Pe linkul clientului: stadiul comenzilor (doar starea și datele de livrare), cu data ultimei actualizări —
// clientul nu mai trebuie să întrebe „când vine?”, iar o dată veche se vede ca veche.
import type { ClientProgress } from '@/core/client-progress';
import { formatQty } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

export default function ClientDeliveries({ p }: { p: ClientProgress }){
  const { t, lang } = usePrefs(), d = (s: string) => new Date(s.length === 10 ? s + 'T12:00:00' : s).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long' });
  if (!p.items.length) return null;
  return (<section className="cdel no-print" aria-labelledby="cdel-h">
    <header><span className="kicker">{t('cdel.kicker')}</span><h2 id="cdel-h">{t('cdel.title')}</h2>
      {p.updatedAt && <p className="muted">{t('cdel.updated', { d: d(p.updatedAt) })}</p>}</header>
    <dl className="cdel-sum">{(['todo', 'ordered', 'delivered', 'installed'] as const).map(s => <div key={s}><dt>{t(`cdel.st.${s}`)}</dt><dd>{p.counts[s]}</dd></div>)}
      {p.late > 0 && <div className="late"><dt>{t('cdel.late')}</dt><dd>{p.late}</dd></div>}</dl>
    <ul>{p.items.filter(i => i.status !== 'todo').map((i, k) => <Row key={k} i={i} d={d} />)}</ul>
    {p.decisionsDue > 0 && <div className="cdel-due"><b>{t('cdel.decide', { n: p.decisionsDue })}</b><ul>{p.items.filter(i => i.decideUrgent).map((i, k) => <Row key={k} i={i} d={d} />)}</ul></div>}
    {p.counts.todo > 0 && <details className="cdel-todo"><summary>{t('cdel.todoList', { n: p.counts.todo })}</summary>
      <ul>{p.items.filter(i => i.status === 'todo').map((i, k) => <Row key={k} i={i} d={d} />)}</ul></details>}
  </section>);
}
function Row({ i, d }: { i: ClientProgress['items'][number]; d(s: string): string }){
  const { t, lang } = usePrefs();
  return <li className={`cdel-row st-c-${i.status} ${i.late ? 'is-late' : ''}`}>
    <span className="cdel-name"><b>{i.product}</b><small>{[formatQty(i.qty, i.unit, lang), i.rooms.join(', ')].filter(Boolean).join(' · ')}</small></span>
    <span className="cdel-st">{t(`cdel.st.${i.status}`)}<small>{i.late ? t('cdel.lateSince', { d: d(i.eta!) }) : i.eta ? t('cdel.eta', { d: d(i.eta) }) : i.deliveredAt ? t('cdel.on', { d: d(i.deliveredAt) }) : i.decideBy ? t(i.decideLate ? 'cdel.decideByLate' : 'cdel.decideBy', { d: d(i.decideBy) }) : ''}</small></span></li>;
}
