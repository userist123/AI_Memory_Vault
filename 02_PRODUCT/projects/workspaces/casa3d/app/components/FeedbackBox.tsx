'use client';
// Caseta de păreri de sub o planșă, pe linkul pentru client: aprobarea pe rând a fiecărui finisaj și a fiecărei piese
// („aprob” / „aș schimba”), plus o părere liberă despre cameră („îmi place / aș schimba ceva / am o întrebare”).
// Numele se ține minte în browserul clientului (doar o comoditate); părerile trimise apar sub casetă, cu „rezolvat” când designerul le bifează.
import { useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import ClientNameBox, { useClientNameBox } from './ClientNameBox';

export interface FeedbackItem { id: string; roomId: string | null; verdict: 'like' | 'change' | 'question'; author: string; text: string; createdAt: string; resolvedAt: string | null; item?: { key: string; label: string; role: string | null } | null }
export interface ApprovalItem { key: string; label: string; role: string | null; swatch?: React.CSSProperties }
const VERDICTS = ['like', 'change', 'question'] as const;

export default function FeedbackBox({ token, roomId, title, sub, items, approvals = [], onAdded, id }: { token: string; roomId: string | null; title: string; sub?: string; items: FeedbackItem[]; approvals?: ApprovalItem[]; onAdded(f: FeedbackItem): void; id?: string }){
  const { t, lang } = usePrefs(), textRef = useRef<HTMLTextAreaElement>(null);
  const [verdict, setVerdict] = useState<FeedbackItem['verdict'] | null>(null), [about, setAbout] = useState<ApprovalItem | null>(null), [text, setText] = useState(''), box = useClientNameBox(token), [busy, setBusy] = useState(false), [msg, setMsg] = useState<{ ok: boolean; s: string } | null>(null);
  // numele se cere o singură dată pe link (lib/client-name.ts) și e același în aprobări, păreri, variante și modificări
  const canSend = !!verdict && box.ready && (verdict === 'like' || text.trim().length > 0) && !busy;
  const sending = useRef(false);   // dublul click nu trimite aceeași părere de două ori
  async function post(body: object){ if (sending.current) return false; const author = box.ensure(); if (!author){ setMsg({ ok: false, s: t('fb.nameFirst') }); return false; }
    sending.current = true; setBusy(true); setMsg(null);
    try { const r = await fetch(`/api/share/${token}/feedback`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId, author, ...body }) });
      const j = await r.json(); if (!r.ok){ setMsg({ ok: false, s: j.error || t('fb.error') }); return false; }
      onAdded(j); setMsg({ ok: true, s: t('fb.sent') }); return true; }
    catch { setMsg({ ok: false, s: t('fb.error') }); return false; } finally { sending.current = false; setBusy(false); } }
  async function send(e: React.FormEvent){ e.preventDefault(); if (!canSend) return;
    if (await post({ verdict, text, ...(about ? { item: { key: about.key } } : {}) })){ setText(''); setVerdict(null); setAbout(null); } }
  async function approve(a: ApprovalItem){ await post({ verdict: 'like', text: '', item: { key: a.key } }); }
  // „Aprobă camera”: toate produsele încă neaprobate, într-o singură cerere (lib/feedback.ts addApprovals)
  async function approveRoom(){ const todo = approvals.filter(a => last(a.key)?.verdict !== 'like'); if (!todo.length || !roomId) return;
    if (sending.current) return; const author = box.ensure(); if (!author){ setMsg({ ok: false, s: t('fb.nameFirst') }); return; }
    sending.current = true; setBusy(true); setMsg(null);
    try { const r = await fetch(`/api/share/${token}/feedback`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId, author, items: todo.map(a => ({ key: a.key })) }) });
      const j = await r.json(); if (!r.ok){ setMsg({ ok: false, s: j.error || t('fb.error') }); return; }
      for (const f of j as FeedbackItem[]) onAdded(f); setMsg({ ok: true, s: t('fb.roomApproved') }); }
    catch { setMsg({ ok: false, s: t('fb.error') }); } finally { sending.current = false; setBusy(false); } }
  function change(a: ApprovalItem){ setAbout(a); setVerdict('change'); setTimeout(() => textRef.current?.focus(), 0); }
  // starea fiecărui produs = ultima părere a clientului despre el
  const last = (key: string) => [...items].reverse().find(f => f.item?.key === key);
  const free = items.filter(f => !f.item), approved = approvals.filter(a => last(a.key)?.verdict === 'like').length;
  const allOk = approvals.length > 0 && approved === approvals.length, anyChange = approvals.some(a => { const l = last(a.key); return l && l.verdict !== 'like'; });
  return (<aside className="fb no-print" aria-label={title} id={id}>
    <div className="fb-col">
      <header><h3>{title}</h3>{sub && <p>{sub}</p>}</header>
      {approvals.length > 0 && <div className="fb-approve">
        <div className="fb-approve-head"><b>{t('fb.approveTitle')}</b><small>{t('fb.approved', { n: approved, total: approvals.length })}</small></div>
        <ClientNameBox box={box} className="fb-name" />
        <div className="fb-room-act">{allOk ? <p className="fb-allok" role="status">{t('fb.roomAllOk')}</p>
          : <button type="button" className="btn primary lg" disabled={busy || !box.ready} onClick={approveRoom}>{t('fb.approveRoom', { n: approvals.length - approved })}</button>}</div>
        <details className="fb-items" open={anyChange || undefined}><summary>{t('fb.itemsToggle', { n: approvals.length })}</summary>
        <ul>{approvals.map(a => { const l = last(a.key), st = l?.verdict === 'like' ? 'ok' : l ? 'change' : '';
          return <li key={a.key} className={st}>{a.swatch ? <i style={a.swatch} /> : <i className="prod" />}<span>{a.role && <small>{t(`pres.role.${a.role.split('.')[0]}`)}</small>}{a.label}</span>
            <button type="button" className={`fb-ok-btn ${st === 'ok' ? 'on' : ''}`} aria-pressed={st === 'ok'} disabled={busy} onClick={() => approve(a)}>{st === 'ok' ? t('fb.isApproved') : t('fb.approve')}</button>
            <button type="button" className={`fb-ch-btn ${st === 'change' ? 'on' : ''}`} disabled={busy} onClick={() => change(a)}>{t('fb.changeShort')}</button></li>; })}</ul></details>
      </div>}
    </div>
    <div className="fb-col">
      <form onSubmit={send}>
        <div className="fb-verdicts" role="radiogroup" aria-label={title}>{VERDICTS.map(v => <button key={v} type="button" role="radio" aria-checked={verdict === v} className={`fb-v fb-${v}`} onClick={() => { setVerdict(v); if (v !== 'change') setAbout(null); }}>{t(`fb.${v}`)}</button>)}</div>
        {verdict && <>
          {about && <p className="fb-about">{t('fb.about', { what: about.label })} <button type="button" onClick={() => setAbout(null)} aria-label={t('common.close')}>×</button></p>}
          <textarea ref={textRef} value={text} onChange={e => setText(e.target.value)} maxLength={2000} rows={3} placeholder={t('fb.placeholder')} aria-label={t('fb.placeholder')} />
          <div className="fb-send">{approvals.length === 0 && <ClientNameBox box={box} />}<button className="btn primary" disabled={!canSend}>{t('fb.send')}</button></div></>}
        {msg && <p className={msg.ok ? 'fb-ok' : 'fb-err'} role="status">{msg.s}</p>}
      </form>
      {(free.length > 0 || items.some(f => f.item && f.verdict !== 'like')) && <ul className="fb-list">{items.filter(f => !f.item || f.verdict !== 'like').map(f => <li key={f.id} className={f.resolvedAt ? 'done' : ''}>
        <span className={`fb-tag fb-${f.verdict}`}>{t(`fb.${f.verdict}`)}{f.item ? ` · ${f.item.label}` : ''}</span>{f.text && <q>{f.text}</q>}
        <small>{f.author} · {new Date(f.createdAt).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' })}{f.resolvedAt ? ` · ${t('fb.resolved')}` : ''}</small></li>)}</ul>}
    </div>
  </aside>);
}
