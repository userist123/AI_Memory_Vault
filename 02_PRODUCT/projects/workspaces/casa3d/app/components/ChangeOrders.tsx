'use client';
// Modificările cu preț. Pe linkul clientului: bugetul (oferta → actual) și modificările de aprobat, cu „Aprob” / „Refuz”.
// În editor (designer): lista, o modificare nouă (ciornă), trimiterea la client, retragerea cât nu e decisă.
import { useEffect, useState } from 'react';
import { budgetWithChanges, type ChangeOrder } from '@/core/change-orders';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';
import { intlLocale } from '@/lib/i18n';
import ClientNameBox, { useClientNameBox } from './ClientNameBox';
const signed = (v: number, f: (v: number) => string) => (v > 0 ? '+' : v < 0 ? '−' : '') + f(Math.abs(v));

export function ClientChanges({ token, offered, cur }: { token: string; offered: number; cur: string }){
  const { t, lang } = usePrefs(), [list, setList] = useState<ChangeOrder[] | null>(null), box = useClientNameBox(token), [note, setNote] = useState<Record<string, string>>({}), [busy, setBusy] = useState(''), [msg, setMsg] = useState('');
  const money = (v: number) => formatMoney(v, cur, lang, '—');
  const load = () => fetch(`/api/share/${token}/changes`).then(r => r.ok ? r.json() : []).then(setList).catch(() => setList([]));
  useEffect(() => { load(); }, [token]); // eslint-disable-line
  async function decide(c: ChangeOrder, decision: 'approved' | 'rejected'){
    const author = box.ensure(); if (!author){ setMsg(t('co.nameFirst')); return; } setBusy(c.id); setMsg('');
    try { const r = await fetch(`/api/share/${token}/changes/${c.id}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ decision, author, note: note[c.id] ?? '', seen: c.sentAt }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ setMsg(j.error || t('co.error')); } else { setMsg(t(decision === 'approved' ? 'co.thanksOk' : 'co.thanksNo')); }
      load(); } finally { setBusy(''); } }
  if (!list || list.length === 0) return null;
  const b = budgetWithChanges(offered, list), open = list.filter(c => c.status === 'sent'), done = list.filter(c => c.status !== 'sent');
  const when = (s: string) => new Date(s).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long' });
  return (<section className="cco no-print" aria-labelledby="cco-h">
    <header><span className="kicker">{t('co.kicker')}</span><h2 id="cco-h">{t('co.title')}</h2></header>
    <dl className="cco-bud"><div><dt>{t('co.offered')}</dt><dd>{money(b.offered)}</dd></div><div><dt>{t('co.approvedSum')}</dt><dd>{signed(b.approved, money)}</dd></div>
      <div className="cur"><dt>{t('co.current')}</dt><dd>{money(b.current)}</dd></div>{b.pendingCount > 0 && <div><dt>{t('co.pendingSum', { n: b.pendingCount })}</dt><dd>{signed(b.pending, money)}</dd></div>}
      {b.days !== 0 && <div><dt>{t('co.days')}</dt><dd>{b.days > 0 ? '+' : ''}{b.days}</dd></div>}</dl>
    {open.length > 0 && <>
      <p className="cco-hint">{t('co.hint')}</p>
      <div className="cco-name"><ClientNameBox box={box} /></div>
      {open.map(c => <article key={c.id} className="cco-item"><header><b>{t('co.no', { n: c.number })} · {c.title}</b><span className={c.amount > 0 ? 'up' : 'down'}>{signed(c.amount, money)}</span></header>
        {c.body && <p>{c.body}</p>}{c.days !== 0 && <small>{t('co.moves', { n: c.days })}</small>}
        <input className="cco-note" value={note[c.id] ?? ''} maxLength={1000} onChange={e => setNote(x => ({ ...x, [c.id]: e.target.value }))} placeholder={t('co.notePh')} aria-label={t('co.notePh')} />
        <div className="cco-act"><button className="btn primary" disabled={!!busy} onClick={() => decide(c, 'approved')}>{t('co.approve')}</button><button className="btn" disabled={!!busy} onClick={() => decide(c, 'rejected')}>{t('co.reject')}</button></div></article>)}
    </>}
    {msg && <p className="cco-msg" role="status">{msg}</p>}
    {done.length > 0 && <ul className="cco-done">{done.map(c => <li key={c.id}><span>{t('co.no', { n: c.number })} · {c.title}</span><span>{signed(c.amount, money)}</span>
      <small className={`co-st co-${c.status}`}>{t(`co.st.${c.status}`)}{c.decidedBy ? ` · ${c.decidedBy}` : ''}{c.decidedAt ? ` · ${when(c.decidedAt)}` : ''}{c.clientNote ? ` — „${c.clientNote}”` : ''}</small></li>)}</ul>}
  </section>);
}

export function DesignerChanges({ id, cur, say }: { id: string; cur: string; say(s: string): void }){
  const { t, lang } = usePrefs(), [list, setList] = useState<ChangeOrder[] | null>(null), [f, setF] = useState({ title: '', body: '', amount: '', days: '' }), [busy, setBusy] = useState(false);
  const money = (v: number) => formatMoney(v, cur, lang, '—');
  const load = () => fetch(`/api/projects/${id}/changes`).then(r => r.ok ? r.json() : null).then(j => j && setList(j)).catch(() => {});
  useEffect(() => { load(); const iv = setInterval(load, 30000); return () => clearInterval(iv); }, [id]); // eslint-disable-line
  async function call(url: string, method: string, body?: unknown){ setBusy(true);
    try { const r = await fetch(url, { method, headers: body ? { 'content-type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined }), j = await r.json().catch(() => ({}));
      if (!r.ok){ say(j.error || t('co.error')); return false; } await load(); return true; } finally { setBusy(false); } }
  async function add(){ const amount = Number(f.amount.replace(',', '.').replace(/\s/g, '')), days = f.days.trim() ? Number(f.days) : 0;
    if (await call(`/api/projects/${id}/changes`, 'POST', { title: f.title, body: f.body, amount, days })) setF({ title: '', body: '', amount: '', days: '' }); }
  const b = budgetWithChanges(0, list ?? []);
  return (<div className="dco"><h3>{t('co.title')}{b.pendingCount > 0 && <span className="pill">{t('co.waiting', { n: b.pendingCount })}</span>}</h3>
    <p className="muted">{t('co.designerSub')}</p>
    {list && list.length > 0 && <p className="dco-sum">{t('co.approvedSum')}: <b>{signed(b.approved, money)}</b>{b.pendingCount ? <> · {t('co.pendingSum', { n: b.pendingCount })}: <b>{signed(b.pending, money)}</b></> : null}{b.days ? <> · {t('co.days')}: <b>{b.days}</b></> : null}</p>}
    <ul className="dco-list">{(list ?? []).map(c => <li key={c.id} className={`co-${c.status}`}>
      <div><b>{t('co.no', { n: c.number })} · {c.title}</b><span>{signed(c.amount, money)}</span></div>
      <small className={`co-st co-${c.status}`}>{t(`co.st.${c.status}`)}{c.decidedBy ? ` · ${c.decidedBy}` : ''}{c.clientNote ? ` — „${c.clientNote}”` : ''}</small>
      <div className="dco-act">{c.status === 'draft' && <><button className="btn primary" disabled={busy} onClick={() => call(`/api/projects/${id}/changes/${c.id}`, 'PATCH', { action: 'send' })}>{t('co.send')}</button>
        <button className="btn" disabled={busy} onClick={async () => { if (await askConfirm({ title: t('co.confirmDelete'), confirmLabel: t('dlg.coDeleteOk'), danger: true })) call(`/api/projects/${id}/changes/${c.id}`, 'DELETE'); }}>{t('co.delete')}</button></>}
        {c.status === 'sent' && <button className="btn" disabled={busy} onClick={() => call(`/api/projects/${id}/changes/${c.id}`, 'PATCH', { action: 'withdraw' })}>{t('co.withdraw')}</button>}</div></li>)}</ul>
    <div className="dco-new">
      <label className="f"><span>{t('co.what')}</span><input value={f.title} maxLength={160} onChange={e => setF(x => ({ ...x, title: e.target.value }))} placeholder={t('co.whatPh')} /></label>
      <label className="f"><span>{t('co.details')}</span><textarea rows={2} value={f.body} maxLength={2000} onChange={e => setF(x => ({ ...x, body: e.target.value }))} /></label>
      <div className="grid2"><label className="f"><span>{t('co.amount', { cur })}</span><input inputMode="decimal" value={f.amount} onChange={e => setF(x => ({ ...x, amount: e.target.value }))} placeholder="+1500 / -900" /></label>
        <label className="f"><span>{t('co.daysField')}</span><input inputMode="numeric" value={f.days} onChange={e => setF(x => ({ ...x, days: e.target.value }))} placeholder="0" /></label></div>
      <button className="btn" disabled={busy || !f.title.trim() || !Number.isFinite(Number(f.amount.replace(',', '.').replace(/\s/g, ''))) || !f.amount.trim()} onClick={add}>{t('co.add')}</button></div>
  </div>);
}
