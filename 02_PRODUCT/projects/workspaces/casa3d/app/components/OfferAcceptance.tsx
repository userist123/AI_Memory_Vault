'use client';
// Acceptarea ofertei (core/offer-acceptance.ts). Pe linkul clientului: secțiunea „Oferta” — informațiile precontractuale, oferta
// completă, graficul de plăți, dreptul de retragere cu formularul lui, numele tastat, cele două bife și butonul cu obligație de
// plată; după acceptare, confirmarea de descărcat sau de tipărit (și o a doua persoană, de exemplu coproprietarul, poate accepta
// aceeași versiune). În editor (designer): comutatorul „Acceptare online” pe fiecare link activ (cere PIN pe link), starea,
// istoricul acceptărilor, confirmarea și „Marchează retragerea”.
// Textele juridice vin din instantaneul ofertei (în română, înghețate odată cu ea); eticheta interfeței vine din lib/locales.
import { useEffect, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { formatMoney } from '@/core/format';
import { roDate, type DocSection } from '@/core/contract';
import { confirmationDoc, roDateTime, roDay, ACCEPT_LIMITS, type OfferSnapshot, type AcceptanceRecord, type Acceptance, type AcceptanceState, type OfferChange, type NotAcceptable } from '@/core/offer-acceptance';
import { DocSkeleton } from './Skeleton';
import LoadError from './LoadError';

type T = (k: string, v?: Record<string, string | number>) => string;
type Acceptable = { ok: true } | { ok: false; reason: NotAcceptable };
const JSON_H = { 'content-type': 'application/json' };
const whenRo = (t: T, iso: string) => t('oa.roTime', { when: roDateTime(iso).slice(0, 17) });

function Sections({ sections, h = 'h3' }: { sections: DocSection[]; h?: 'h2' | 'h3' }){ const H = h;
  return <>{sections.map(s => <section key={s.id}><H>{s.title}</H>
    {s.paras.map((p, k) => <p key={k}>{p}</p>)}
    {s.items && <ul>{s.items.map((i, k) => <li key={k}>{i}</li>)}</ul>}
    {s.table && <table><thead><tr>{s.table.head.map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>{s.table.rows.map((r, k) => <tr key={k}>{r.map((c, j) => <td key={j}>{c}</td>)}</tr>)}</tbody></table>}</section>)}</>;
}
/** Confirmarea acceptării, ca document A4 (aceeași pe linkul clientului și la designer). */
export function ConfirmationView({ rec }: { rec: AcceptanceRecord }){ const d = confirmationDoc(rec);
  return (<article className="contract-doc oa-doc">
    <header>{d.studioLines.map((l, k) => k === 0 ? <b key={k}>{l}</b> : <small key={k}>{l}</small>)}<h1>{d.title}</h1></header>
    <p className="contract-notice">{d.nature} {d.disclaimer}</p>
    <table className="oa-doc-facts"><tbody>{d.facts.map(([k, v]) => <tr key={k}><th scope="row">{k}</th><td>{v}</td></tr>)}</tbody></table>
    <h2 className="oa-doc-offer">{d.offerTitle}</h2>
    <Sections sections={d.sections} h="h2" />
  </article>);
}
const changeText = (t: T, c: OfferChange) => c.part === 'price' ? t('oa.chg.price', { before: c.before ?? '—', after: c.after ?? '—' }) : c.part === 'section' ? t('oa.chg.section', { title: c.title ?? '' }) : t(`oa.chg.${c.part}`);
function printConfirmation(){
  const root = document.documentElement, off = () => { root.classList.remove('oa-printing'); window.removeEventListener('afterprint', off); };
  root.classList.add('oa-printing'); window.addEventListener('afterprint', off); window.print();
}

// ---------- clientul, pe link ----------
type View = { state: AcceptanceState; offer: { snapshot: OfferSnapshot; hash: string; acceptable: Acceptable }; acceptance: AcceptanceRecord | null;
  elsewhere: { acceptedAt: string; withdrawnAt: string | null } | null; acceptedBy: Acceptance[]; changes: OfferChange[] };
export function ClientOffer({ token }: { token: string }){
  const { t, lang } = usePrefs(), [mode, setMode] = useState<'loading' | 'off' | 'pin' | 'view'>('loading'), [v, setV] = useState<View | null>(null);
  const [pin, setPin] = useState(''), [busy, setBusy] = useState(false), [msg, setMsg] = useState(''), [showDoc, setShowDoc] = useState(false);
  const [f, setF] = useState({ name: '', contact: '', onBehalf: '', accept: false, early: false });
  const money = (n: number | null) => formatMoney(n, 'RON', lang, '—');
  async function load(){
    try { const r = await fetch(`/api/share/${token}/offer`);
      if (r.status === 401){ setMode('pin'); return; } if (!r.ok){ setMode('off'); return; }
      setV(await r.json()); setMode('view'); } catch { setMode('off'); } }
  useEffect(() => { load(); }, [token]); // eslint-disable-line
  async function unlock(e: React.FormEvent){ e.preventDefault(); if (!/^\d{6}$/.test(pin) || busy) return; setBusy(true); setMsg('');
    try { const r = await fetch(`/api/share/${token}/pin`, { method: 'POST', headers: JSON_H, body: JSON.stringify({ pin }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setMsg(j.error || t('oa.error')); return; } setPin(''); await load(); }
    catch { setMsg(t('oa.error')); } finally { setBusy(false); } }
  async function accept(e: React.FormEvent){ e.preventDefault(); if (!v || busy) return;
    if (f.name.trim().length < ACCEPT_LIMITS.nameMin){ setMsg(t('oa.nameFirst')); return; } if (!f.accept){ setMsg(t('oa.tickFirst')); return; }
    setBusy(true); setMsg('');
    try { const r = await fetch(`/api/share/${token}/offer`, { method: 'POST', headers: JSON_H, body: JSON.stringify({ hash: v.offer.hash, name: f.name, contact: f.contact, onBehalf: f.onBehalf, accept: true, earlyStart: f.early }) }), j = await r.json().catch(() => ({}));
      if (r.status === 401){ setMode('pin'); setMsg(t('oa.pinAgain')); return; }
      if (!r.ok){ setMsg(j.error || t('oa.error')); if (r.status === 409){ setF(x => ({ ...x, accept: false, early: false })); await load(); } return; }
      setShowDoc(true); setF({ name: '', contact: '', onBehalf: '', accept: false, early: false }); await load(); setMsg(t(j.created === false ? 'oa.already' : 'oa.thanks')); }
    catch { setMsg(t('oa.error')); } finally { setBusy(false); } }
  if (mode === 'loading' || mode === 'off') return null;
  if (mode === 'pin' || !v) return (<section className="oa no-print" id="oferta" aria-labelledby="oa-h">
    <header><span className="kicker">{t('oa.kicker')}</span><h2 id="oa-h">{t('oa.title')}</h2></header>
    <p className="oa-lead">{t('oa.pinIntro')}</p>
    <form className="cco-pin" onSubmit={unlock}><label htmlFor="oa-pin-in">{t('pin.pinLabel')}</label>
      <div className="cco-pin-row"><input id="oa-pin-in" inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" maxLength={6} value={pin} onChange={e => setPin(e.target.value.replace(/\D/g, ''))} />
        <button className="btn primary" disabled={busy || pin.length !== 6}>{t('pin.unlock')}</button></div></form>
    {msg && <p className="cco-msg" role="alert">{msg}</p>}
  </section>);

  const s = v.offer.snapshot, x = s.texts, w = x.withdrawal, a = v.acceptance, el = v.elsewhere, accepted = v.state === 'accepted';
  // oferta completă cu informările și formularul de acceptare: mereu înaintea butonului
  const body = (<div className="oa-body oa-screen">
    <h3 className="oa-sub">{t('oa.precontract')}</h3>
    <dl className="oa-facts">
      <div><dt>{t('oa.f.studio')}</dt><dd>{s.offer.studioLines.map((l, k) => <span key={k}>{l}</span>)}</dd></div>
      <div><dt>{t('oa.f.for')}</dt><dd>{s.client.name || t('oa.f.forNobody')}</dd></div>
      <div><dt>{t('oa.f.object')}</dt><dd><span>{s.project.name}{s.project.siteAddress ? ` · ${s.project.siteAddress}` : ''}</span><span>{t('oa.f.objectSee')}</span></dd></div>
      <div className="total"><dt>{t('oa.f.total')}</dt><dd><b>{money(s.price.gross)}</b><span>{s.price.vatPayer ? t('oa.f.vat', { pct: Math.round(s.price.vatRate * 100), vat: money(s.price.vat) }) : t('oa.f.noVat')}</span></dd></div>
      <div><dt>{t('oa.f.terms')}</dt><dd><span>{t('oa.f.start', { date: s.terms.startDate ? roDate(s.terms.startDate) : t('oa.f.agreed') })}</span><span>{t('oa.f.due', { date: s.terms.dueDate ? roDate(s.terms.dueDate) : t('oa.f.agreed') })}</span>
        {s.terms.validUntil && <span>{t('oa.f.valid', { date: roDate(s.terms.validUntil) })}</span>}</dd></div>
      <div><dt>{t('oa.f.changes')}</dt><dd>{x.changes}</dd></div>
    </dl>
    <h3 className="oa-sub">{t('oa.schedule')}</h3>
    {s.schedule.length ? <table className="oa-pay"><thead><tr><th>{t('oa.pay.what')}</th><th>%</th><th>{t('oa.pay.amount')}</th></tr></thead>
      <tbody>{s.schedule.map((p, k) => <tr key={k}><td>{p.label}</td><td>{p.pct}</td><td>{money(p.amount)}</td></tr>)}</tbody></table> : <p className="oa-lead">{t('oa.noSchedule')}</p>}
    <h3 className="oa-sub">{t('oa.full')}</h3>
    <div className="oa-offer"><Sections sections={s.offer.sections} h="h3" /></div>
    <h3 className="oa-sub">{w.title}</h3>
    <p className="oa-small">{w.scope}</p>
    {w.info.map((p, k) => <p key={k}>{p}</p>)}
    <h4 className="oa-h4">{w.consequencesTitle}</h4>
    {(w.consequences ?? []).map((p, k) => <p key={k}>{p}</p>)}
    <p>{w.loss}</p>
    <details className="oa-form"><summary>{w.form.title}</summary><p><small>{w.form.note}</small></p><ul>{w.form.lines.map((l, k) => <li key={k}>{l}</li>)}</ul><p><small>{w.form.footnote}</small></p></details>
    <p className="oa-small">{w.source}</p><p className="oa-small">{x.binds}</p><p className="oa-small">{x.retention}</p><p className="oa-small">{x.disclaimer}</p>

    {!v.offer.acceptable.ok ? <p className="oa-status st-changed" role="status"><b>{t(`oa.cannot.${v.offer.acceptable.reason}`)}</b></p>
    : <form className="oa-accept" onSubmit={accept} aria-label={t('oa.formAria')}>
      <label className="f"><span>{t('oa.name')}</span><input value={f.name} maxLength={ACCEPT_LIMITS.nameMax} autoComplete="name" required onChange={e => setF(o => ({ ...o, name: e.target.value }))} /></label>
      <label className="f"><span>{t('oa.onBehalf')}</span><input value={f.onBehalf} maxLength={ACCEPT_LIMITS.onBehalf} onChange={e => setF(o => ({ ...o, onBehalf: e.target.value }))} /></label>
      <label className="f"><span>{t('oa.contact')}</span><input value={f.contact} maxLength={ACCEPT_LIMITS.contact} autoComplete="email" onChange={e => setF(o => ({ ...o, contact: e.target.value }))} /></label>
      <p className="oa-effect">{x.effect}</p>
      <label className="oa-chk"><input type="checkbox" checked={f.accept} onChange={e => setF(o => ({ ...o, accept: e.target.checked }))} /><span>{x.acceptLabel}</span></label>
      <label className="oa-chk"><input type="checkbox" checked={f.early} onChange={e => setF(o => ({ ...o, early: e.target.checked }))} /><span>{x.earlyStartLabel} <small>{t('oa.optional')}</small></span></label>
      <button className="btn primary lg oa-go" disabled={busy}>{x.buttonLabel}</button>
    </form>}
  </div>);
  return (<section className="oa" id="oferta" aria-labelledby="oa-h">
    <header className="oa-screen"><span className="kicker">{t('oa.kicker')}</span><h2 id="oa-h">{s.offer.title}</h2><p className="oa-lead">{x.nature}</p></header>
    {a && <div className={`oa-status oa-screen st-${v.state}`} role="status">
      {accepted ? v.acceptedBy.map(p => <b key={p.id}>{t('oa.accepted', { name: p.name, when: whenRo(t, p.acceptedAt) })}
 · <a className="linkish" href={`/api/share/${token}/offer/confirmation?id=${p.id}`} download>{t('oa.download')}</a></b>)
        : <b>{v.state === 'changed' ? t('oa.changed', { when: whenRo(t, a.acceptedAt) }) : t('oa.withdrawn', { when: whenRo(t, a.acceptedAt), date: roDate(a.withdrawnAt?.slice(0, 10)) })}</b>}
      {v.state === 'changed' && v.changes.length > 0 && <ul>{v.changes.map((c, k) => <li key={k}>{changeText(t, c)}</li>)}</ul>}
      <small>{t('oa.hash', { hash: a.hash })}</small>
      <div className="oa-act">{!accepted && <a className="btn" href={`/api/share/${token}/offer/confirmation?id=${a.id}`} download>{t('oa.download')}</a>}
        <button type="button" className="btn" onClick={() => { setShowDoc(true); printConfirmation(); }}>{t('oa.print')}</button>
        <button type="button" className="btn" aria-expanded={showDoc} onClick={() => setShowDoc(o => !o)}>{showDoc ? t('oa.hideDoc') : t('oa.showDoc')}</button></div>
      <small>{t('oa.keepCopy')}</small>
    </div>}
    {/* acceptare făcută pe alt link al proiectului: doar faptul și data, fără nume sau confirmare */}
    {!a && el && <div className={`oa-status oa-screen st-${v.state}`} role="status">
      <b>{accepted ? t('oa.el.accepted', { when: whenRo(t, el.acceptedAt) }) : v.state === 'changed' ? t('oa.el.changed', { when: whenRo(t, el.acceptedAt) }) : t('oa.el.withdrawn', { when: whenRo(t, el.acceptedAt) })}</b></div>}
    {a && <div className={`oa-confirm ${showDoc ? '' : 'oa-collapsed'}`}><ConfirmationView rec={a} /></div>}
    {accepted ? <details className="oa-more oa-screen"><summary>{t('oa.another')}</summary><p className="oa-small">{t('oa.anotherSub')}</p>{body}</details> : body}
    {msg && <p className="cco-msg oa-screen" role="status">{msg}</p>}
  </section>);
}

// ---------- designerul, în panoul „Contract și ofertă” ----------
type AdminLink = { token: string; revisionNumber: number; createdAt: string; expiresAt: string | null; pinEnabled: boolean; enabled: boolean; accepted: number };
type Admin = { links: AdminLink[]; offer: { revisionNumber: number; hash: string; acceptable: Acceptable } | null; state: AcceptanceState; acceptance: Acceptance | null; changes: OfferChange[]; acceptances: Acceptance[] };
// `tick`: se schimbă la fiecare salvare a contractului, ca starea (acceptată / schimbată după acceptare) să se recalculeze imediat
export function OfferAcceptanceAdmin({ id, say, tick = 0 }: { id: string; say(s: string): void; tick?: number }){
  const { t } = usePrefs(), [d, setD] = useState<Admin | null>(null), [busy, setBusy] = useState(false), [needPin, setNeedPin] = useState<string | null>(null), [shown, setShown] = useState<{ token: string; pin: string } | null>(null);
  const [wd, setWd] = useState<{ id: string; date: string } | null>(null);
  const load = () => fetch(`/api/projects/${id}/acceptance`).then(r => r.ok ? r.json() : null).then(j => { if (j) setD(j); }).catch(() => {});
  useEffect(() => { load(); const iv = setInterval(load, 30000); return () => clearInterval(iv); }, [id, tick]); // eslint-disable-line
  async function toggle(token: string, enabled: boolean){ setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/acceptance`, { method: 'PUT', headers: JSON_H, body: JSON.stringify({ token, enabled }) }), j = await r.json().catch(() => null);
      if (!r.ok){ if (j?.details?.needsPin) setNeedPin(token); else say(j?.error || t('oa.d.failed')); return; }
      setNeedPin(null); setD(j); say(t(enabled ? 'oa.d.on' : 'oa.d.off')); }
    catch { say(t('oa.d.failed')); } finally { setBusy(false); } }
  // linkul nu are PIN: îl generează (se arată o singură dată), apoi activează acceptarea pe acel link
  async function pinAndEnable(token: string){ if (busy) return; setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/shares/pin`, { method: 'POST', headers: JSON_H, body: JSON.stringify({ token }) }), j = await r.json().catch(() => null);
      if (!r.ok){ say(j?.error || t('pin.failed')); setBusy(false); return; } setShown({ token, pin: String(j?.pin ?? '') }); }
    catch { say(t('pin.failed')); setBusy(false); return; }
    await toggle(token, true); }
  async function withdraw(){ if (!wd || busy) return; setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/acceptance/${wd.id}`, { method: 'PATCH', headers: JSON_H, body: JSON.stringify({ action: 'withdraw', date: wd.date }) }), j = await r.json().catch(() => null);
      if (!r.ok){ say(j?.error || t('oa.d.failed')); return; } setWd(null); say(t('oa.d.withdrawn')); await load(); }
    catch { say(t('oa.d.failed')); } finally { setBusy(false); } }
  if (!d) return null;
  const a = d.acceptance, when = (x: Acceptance) => whenRo(t, x.acceptedAt), anyOn = d.links.some(l => l.enabled);
  return (<div className="oa-admin">
    <h4>{t('oa.d.title')}</h4>
    {d.links.length === 0 ? <p className="hint">{t('pin.noLink')}</p> : <>
      <p className="hint">{t('oa.d.linksSub')}</p>
      <ul className="oa-links">{d.links.map(l => <li key={l.token}>
        <label className="chk"><input type="checkbox" checked={l.enabled} disabled={busy} onChange={e => toggle(l.token, e.target.checked)} />
          <span>{t('oa.d.link', { n: l.revisionNumber, date: roDateTime(l.createdAt).slice(0, 17) })} <small>{t(l.pinEnabled ? 'oa.d.linkPin' : 'oa.d.linkNoPin')}{l.accepted > 0 ? ` · ${t('oa.d.linkAccepted', { n: l.accepted })}` : ''}</small></span></label>
        {needPin === l.token && !l.enabled && <p className="notice" role="status">{t('oa.d.needsPin')} <button type="button" className="btn sm" disabled={busy} onClick={() => pinAndEnable(l.token)}>{t('oa.d.pinAndEnable')}</button></p>}
        {shown?.token === l.token && <p className="notice" role="status"><b>{t('pin.shown', { pin: shown.pin })}</b></p>}
      </li>)}</ul>
      {anyOn && d.offer && !d.offer.acceptable.ok && <p className="notice" role="status">{t(`oa.d.cannot.${d.offer.acceptable.reason}`)}</p>}
    </>}
    <p className={`oa-state st-${d.state}`} role="status"><b>{d.state === 'none' || !a ? t('oa.d.state.none') : d.state === 'accepted' ? t('oa.d.state.accepted', { name: a.name, when: when(a) }) : d.state === 'changed' ? t('oa.d.state.changed', { name: a.name, when: when(a) })
      : t('oa.d.state.withdrawn', { name: a.name, date: roDate(a.withdrawnAt?.slice(0, 10)) })}</b>{d.offer && <small> {t('oa.d.stateRev', { n: d.offer.revisionNumber })}</small>}</p>
    {d.state === 'changed' && d.changes.length > 0 && <ul className="hint">{d.changes.map((c, k) => <li key={k}>{changeText(t, c)}</li>)}</ul>}
    {d.acceptances.length > 0 && <ul className="oa-list">{d.acceptances.map(x => <li key={x.id} className={x.withdrawnAt ? 'off' : ''}>
      <div><b>{x.name}</b>{x.onBehalf ? ` · ${t('oa.d.for', { who: x.onBehalf })}` : ''}{x.contact ? ` · ${x.contact}` : ''}</div>
      <small>{when(x)} · {t('oa.d.rev', { n: x.revisionNumber })} · {x.hash.slice(0, 12)}…{x.earlyStart ? ` · ${t('oa.d.early')}` : ''}{x.withdrawnAt ? ` · ${t('oa.d.withdrawnOn', { date: roDate(x.withdrawnAt.slice(0, 10)) })}` : !d.offer || x.hash === d.offer.hash ? '' : ` · ${t('oa.d.oldVersion')}`}</small>
      <div className="oa-act"><a className="btn sm" href={`/p/${id}/acceptance/${x.id}`} target="_blank" rel="noopener">{t('oa.d.open')}</a>
        <a className="btn sm" href={`/api/projects/${id}/acceptance/${x.id}?download=1`} download>{t('oa.download')}</a>
        {!x.withdrawnAt && wd?.id !== x.id && <button type="button" className="btn sm" disabled={busy} onClick={() => setWd({ id: x.id, date: roDay(new Date()) })}>{t('oa.d.markWithdrawn')}</button>}</div>
      {wd?.id === x.id && <div className="oa-wd"><label className="f"><span>{t('oa.d.withdrawDate')}</span><input type="date" value={wd.date} min={roDay(x.acceptedAt)} max={roDay(new Date())} onChange={e => setWd({ id: x.id, date: e.target.value })} /></label>
        <div className="oa-act"><button type="button" className="btn sm primary" disabled={busy || !wd.date} onClick={withdraw}>{t('oa.d.withdrawOk')}</button><button type="button" className="btn sm" onClick={() => setWd(null)}>{t('pin.cancel')}</button></div></div>}
    </li>)}</ul>}
    <p className="hint">{t('oa.d.note')}</p>
  </div>);
}

/** Pagina designerului /p/[id]/acceptance/[acc]: confirmarea unei acceptări, de tipărit sau de descărcat. */
export default function AcceptancePage({ id, acc }: { id: string; acc: string }){
  const { t } = usePrefs(), [rec, setRec] = useState<AcceptanceRecord | null>(null), [err, setErr] = useState(false);
  useEffect(() => { fetch(`/api/projects/${id}/acceptance/${acc}`).then(r => r.ok ? r.json() : Promise.reject()).then(setRec).catch(() => setErr(true)); }, [id, acc]);
  if (err) return <LoadError />;
  if (!rec) return <main className="report"><DocSkeleton label={t('print.loading')} /></main>;
  return (<main className="report">
    <div className="no-print report-bar"><a className="btn" href={`/api/projects/${id}/acceptance/${acc}?download=1`} download>{t('oa.download')}</a><button className="btn primary" onClick={() => print()}>{t('pres.pdf')}</button></div>
    <ConfirmationView rec={rec} />
  </main>);
}
