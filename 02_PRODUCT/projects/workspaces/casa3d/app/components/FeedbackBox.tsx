'use client';
// Caseta de păreri de sub o planșă, pe linkul pentru client: aprobarea pe rând a fiecărui finisaj și a fiecărei piese
// („aprob” / „aș schimba”), plus o părere liberă despre cameră („îmi place / aș schimba ceva / am o întrebare”).
// Numele se ține minte în browserul clientului (doar o comoditate); părerile trimise apar sub casetă, cu „rezolvat” când designerul le bifează.
import { useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

export interface FeedbackItem { id: string; roomId: string | null; verdict: 'like' | 'change' | 'question'; author: string; text: string; createdAt: string; resolvedAt: string | null; item?: { key: string; label: string; role: string | null } | null }
export interface ApprovalItem { key: string; label: string; role: string | null; swatch?: React.CSSProperties }
const VERDICTS = ['like', 'change', 'question'] as const;
const NAME_KEY = 'casa3d.fb.name';
const savedName = () => { try { return localStorage.getItem(NAME_KEY) ?? ''; } catch { return ''; } };

export default function FeedbackBox({ token, roomId, title, sub, items, approvals = [], onAdded }: { token: string; roomId: string | null; title: string; sub?: string; items: FeedbackItem[]; approvals?: ApprovalItem[]; onAdded(f: FeedbackItem): void }){
  const { t, lang } = usePrefs(), textRef = useRef<HTMLTextAreaElement>(null);
  const [verdict, setVerdict] = useState<FeedbackItem['verdict'] | null>(null), [about, setAbout] = useState<ApprovalItem | null>(null), [text, setText] = useState(''), [name, setName] = useState(savedName), [busy, setBusy] = useState(false), [msg, setMsg] = useState<{ ok: boolean; s: string } | null>(null);
  const canSend = !!verdict && name.trim().length > 0 && (verdict === 'like' || text.trim().length > 0) && !busy;
  async function post(body: object){ setBusy(true); setMsg(null);
    try { const r = await fetch(`/api/share/${token}/feedback`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId, author: name, ...body }) });
      const j = await r.json(); if (!r.ok){ setMsg({ ok: false, s: j.error || t('fb.error') }); return false; }
      try { localStorage.setItem(NAME_KEY, name.trim()); } catch { /* fără stocare: doar nu ținem minte numele */ }
      onAdded(j); setMsg({ ok: true, s: t('fb.sent') }); return true; }
    catch { setMsg({ ok: false, s: t('fb.error') }); return false; } finally { setBusy(false); } }
  async function send(e: React.FormEvent){ e.preventDefault(); if (!canSend) return;
    if (await post({ verdict, text, ...(about ? { item: { key: about.key } } : {}) })){ setText(''); setVerdict(null); setAbout(null); } }
  const ensureName = () => { if (name.trim()) return true; const n = savedName(); if (n){ setName(n); return true; } setMsg({ ok: false, s: t('fb.nameFirst') }); return false; };
  async function approve(a: ApprovalItem){ if (!ensureName()) return; await post({ verdict: 'like', text: '', item: { key: a.key } }); }
  function change(a: ApprovalItem){ setAbout(a); setVerdict('change'); if (!name.trim()) setName(savedName()); setTimeout(() => textRef.current?.focus(), 0); }
  // starea fiecărui produs = ultima părere a clientului despre el
  const last = (key: string) => [...items].reverse().find(f => f.item?.key === key);
  const free = items.filter(f => !f.item), approved = approvals.filter(a => last(a.key)?.verdict === 'like').length;
  return (<aside className="fb no-print" aria-label={title}>
    <div className="fb-col">
      <header><h3>{title}</h3>{sub && <p>{sub}</p>}</header>
      {approvals.length > 0 && <div className="fb-approve">
        <div className="fb-approve-head"><b>{t('fb.approveTitle')}</b><small>{t('fb.approved', { n: approved, total: approvals.length })}</small></div>
        {!name.trim() && <input className="fb-name" value={name} onChange={e => setName(e.target.value)} maxLength={60} placeholder={t('fb.name')} aria-label={t('fb.name')} />}
        <ul>{approvals.map(a => { const l = last(a.key), st = l?.verdict === 'like' ? 'ok' : l ? 'change' : '';
          return <li key={a.key} className={st}>{a.swatch ? <i style={a.swatch} /> : <i className="prod" />}<span>{a.role && <small>{t(`pres.role.${a.role.split('.')[0]}`)}</small>}{a.label}</span>
            <button type="button" className={`fb-ok-btn ${st === 'ok' ? 'on' : ''}`} aria-pressed={st === 'ok'} disabled={busy} onClick={() => approve(a)}>{st === 'ok' ? t('fb.isApproved') : t('fb.approve')}</button>
            <button type="button" className={`fb-ch-btn ${st === 'change' ? 'on' : ''}`} disabled={busy} onClick={() => change(a)}>{t('fb.changeShort')}</button></li>; })}</ul>
      </div>}
    </div>
    <div className="fb-col">
      <form onSubmit={send}>
        <div className="fb-verdicts" role="radiogroup" aria-label={title}>{VERDICTS.map(v => <button key={v} type="button" role="radio" aria-checked={verdict === v} className={`fb-v fb-${v}`} onClick={() => { setVerdict(v); if (v !== 'change') setAbout(null); if (!name.trim()) setName(savedName()); }}>{t(`fb.${v}`)}</button>)}</div>
        {verdict && <>
          {about && <p className="fb-about">{t('fb.about', { what: about.label })} <button type="button" onClick={() => setAbout(null)} aria-label={t('common.close')}>×</button></p>}
          <textarea ref={textRef} value={text} onChange={e => setText(e.target.value)} maxLength={2000} rows={3} placeholder={t('fb.placeholder')} aria-label={t('fb.placeholder')} />
          <div className="fb-send"><input value={name} onChange={e => setName(e.target.value)} maxLength={60} placeholder={t('fb.name')} aria-label={t('fb.name')} /><button className="btn primary" disabled={!canSend}>{t('fb.send')}</button></div></>}
        {msg && <p className={msg.ok ? 'fb-ok' : 'fb-err'} role="status">{msg.s}</p>}
      </form>
      {(free.length > 0 || items.some(f => f.item && f.verdict !== 'like')) && <ul className="fb-list">{items.filter(f => !f.item || f.verdict !== 'like').map(f => <li key={f.id} className={f.resolvedAt ? 'done' : ''}>
        <span className={`fb-tag fb-${f.verdict}`}>{t(`fb.${f.verdict}`)}{f.item ? ` · ${f.item.label}` : ''}</span>{f.text && <q>{f.text}</q>}
        <small>{f.author} · {new Date(f.createdAt).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' })}{f.resolvedAt ? ` · ${t('fb.resolved')}` : ''}</small></li>)}</ul>}
    </div>
  </aside>);
}
