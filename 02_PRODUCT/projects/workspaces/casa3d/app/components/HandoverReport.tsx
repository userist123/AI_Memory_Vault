'use client';
import { profileLine, type StudioProfile } from '@/core/studio-profile';
import { DocSkeleton } from './Skeleton';
// Dosarul de predare pentru client (PDF din browser): produsele montate cu garanții, codurile finisajelor, îngrijire,
// ce nu s-a livrat încă, remedierile deschise și un model de proces-verbal de recepție.
import { useEffect, useState } from 'react';
import type { Handover } from '@/core/handover';
import type { ProjectMeta } from '@/core/project-meta';
import { formatQty } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import LoadError from './LoadError';
import { intlLocale } from '@/lib/i18n';

type Data = Handover & { name: string; meta: ProjectMeta; openSnags: { number: number; title: string; trade: string }[] };
export default function HandoverReport({ id }: { id: string }){
  const { t, lang } = usePrefs(), [d, setD] = useState<Data | null>(null), [err, setErr] = useState(false), [pv, setPv] = useState(true);
  const [profile, setProfile] = useState<StudioProfile | null>(null);
  useEffect(() => { fetch('/api/studio').then(r => r.ok ? r.json() : null).then(setProfile).catch(() => {}); }, []);
  useEffect(() => { fetch(`/api/projects/${id}/handover`).then(r => r.ok ? r.json() : Promise.reject()).then(setD).catch(() => setErr(true)); }, [id]);
  if (err) return <LoadError />;
  if (!d) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  const date = (s: string) => new Date(s.length === 10 ? s + 'T12:00:00' : s).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long', year: 'numeric' });
  const today = date(new Date().toISOString()), addr = d.meta.siteAddress || d.meta.client?.address;
  return (<main className="report handover">
    <div className="no-print report-bar"><label><input type="checkbox" checked={pv} onChange={e => setPv(e.target.checked)} /> {t('ho.withPv')}</label><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    <header>{profile && (profile.logo || profile.name) && <div className="ho-studio">{profile.logo && <img src={profile.logo} alt="" />}<div><b>{profile.name}</b><small>{profileLine({ ...profile, name: undefined })}</small>{profile.address && <small>{profile.address}</small>}</div></div>}<span className="kicker">{t('ho.kicker')}</span><h1>{d.name}</h1>
      <p>{[d.meta.client?.name, addr, today].filter(Boolean).join(' · ')}</p><p className="muted">{t('ho.intro')}</p></header>

    <section><h2>{t('ho.items')}</h2>
      {d.items.length === 0 ? <p className="muted">{t('ho.noItems')}</p> :
      <table className="ho-table"><thead><tr><th>{t('orders.col.product')}</th><th>{t('ho.col.where')}</th><th>{t('ho.col.supplier')}</th><th>{t('ho.col.delivered')}</th><th>{t('ho.col.warranty')}</th></tr></thead>
        <tbody>{d.items.map((i, k) => <tr key={k}><td><b>{i.product}</b>{(i.code || i.ref) && <small>{[i.code && t('ho.code', { c: i.code }), i.ref && t('ho.ref', { r: i.ref })].filter(Boolean).join(' · ')}</small>}</td>
          <td>{i.rooms.join(', ') || '—'}<small>{formatQty(i.qty, i.unit, lang)}</small></td><td>{i.supplier}</td><td>{i.deliveredAt ? date(i.deliveredAt) : '—'}</td>
          <td>{i.warrantyMonths == null ? '—' : i.warrantyUntil ? <>{t('ho.until', { d: date(i.warrantyUntil) })}<small>{t('ho.months', { n: i.warrantyMonths })}</small></> : t('ho.months', { n: i.warrantyMonths })}</td></tr>)}</tbody></table>}
      <p className="muted small">{t('ho.warrantyNote')}</p></section>

    {d.finishes.length > 0 && <section><h2>{t('ho.finishes')}</h2><p className="muted">{t('ho.finishesSub')}</p>
      <table className="ho-table"><thead><tr><th>{t('ho.col.where')}</th><th>{t('ho.col.element')}</th><th>{t('orders.col.product')}</th><th>{t('orders.col.code')}</th></tr></thead>
        <tbody>{d.finishes.map((f, k) => <tr key={k}><td>{f.room}</td><td>{t(`sched.el.${f.element}`)}</td><td>{f.product}<small>{f.supplier}</small></td><td>{f.code ?? '—'}</td></tr>)}</tbody></table></section>}

    {d.care.length > 0 && <section><h2>{t('ho.care')}</h2><dl className="ho-care">{d.care.map(c => <div key={c}><dt>{t(`care.${c}.t`)}</dt><dd>{t(`care.${c}`)}</dd></div>)}</dl>
      <p className="muted small">{t('ho.careNote')}</p></section>}

    {(d.notDelivered.length > 0 || d.openSnags.length > 0) && <section><h2>{t('ho.open')}</h2>
      {d.openSnags.length > 0 && <><h3>{t('ho.snags', { n: d.openSnags.length })}</h3><ul>{d.openSnags.map(s => <li key={s.number}>#{s.number} {s.title} · {t(`trade.${s.trade}`)}</li>)}</ul></>}
      {d.notDelivered.length > 0 && <><h3>{t('ho.pending', { n: d.notDelivered.length })}</h3><ul>{d.notDelivered.map((p, k) => <li key={k}>{p.product} · {t(`ostatus.${p.status}`)}</li>)}</ul></>}</section>}

    {pv && <section className="ho-pv"><h2>{t('ho.pv')}</h2><p className="muted small">{t('ho.pvNote')}</p>
      <p>{t('ho.pvBody', { project: d.name, addr: addr ?? '………………', date: today })}</p>
      <p>{t('ho.pvParties')}</p><ol><li>{t('ho.pvClient', { c: d.meta.client?.name ?? '………………' })}</li><li>{t('ho.pvContractor')}</li></ol>
      <p>{t('ho.pvFound')}</p><div className="ho-lines">{(d.openSnags.length ? d.openSnags.map(s => `#${s.number} ${s.title}`) : ['', '', '']).map((s, k) => <div key={k}>{s}</div>)}</div>
      <p>{t('ho.pvTerm')}</p>
      <div className="ho-sign"><div>{t('ho.pvSignClient')}</div><div>{t('ho.pvSignContractor')}</div></div></section>}
  </main>);
}
