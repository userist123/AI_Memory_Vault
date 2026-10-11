'use client';
// Caseta de păreri de sub o planșă, pe linkul pentru client: „îmi place / aș schimba ceva / am o întrebare”, textul și numele.
// Numele se ține minte în browserul clientului (doar o comoditate); părerile trimise apar sub casetă, cu „rezolvat” când designerul le bifează.
import { useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

export interface FeedbackItem { id: string; roomId: string | null; verdict: 'like' | 'change' | 'question'; author: string; text: string; createdAt: string; resolvedAt: string | null }
const VERDICTS = ['like', 'change', 'question'] as const;
const NAME_KEY = 'casa3d.fb.name';
const savedName = () => { try { return localStorage.getItem(NAME_KEY) ?? ''; } catch { return ''; } };

export default function FeedbackBox({ token, roomId, title, sub, items, onAdded }: { token: string; roomId: string | null; title: string; sub?: string; items: FeedbackItem[]; onAdded(f: FeedbackItem): void }){
  const { t, lang } = usePrefs();
  const [verdict, setVerdict] = useState<FeedbackItem['verdict'] | null>(null), [text, setText] = useState(''), [name, setName] = useState(savedName), [busy, setBusy] = useState(false), [msg, setMsg] = useState<{ ok: boolean; s: string } | null>(null);
  const canSend = !!verdict && name.trim().length > 0 && (verdict === 'like' || text.trim().length > 0) && !busy;
  async function send(e: React.FormEvent){ e.preventDefault(); if (!canSend) return; setBusy(true); setMsg(null);
    try { const r = await fetch(`/api/share/${token}/feedback`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId, verdict, author: name, text }) });
      const j = await r.json(); if (!r.ok){ setMsg({ ok: false, s: j.error || t('fb.error') }); return; }
      try { localStorage.setItem(NAME_KEY, name.trim()); } catch { /* fără stocare: doar nu ținem minte numele */ }
      onAdded(j); setText(''); setVerdict(null); setMsg({ ok: true, s: t('fb.sent') }); }
    catch { setMsg({ ok: false, s: t('fb.error') }); } finally { setBusy(false); } }
  return (<aside className="fb no-print" aria-label={title}>
    <form onSubmit={send}>
      <header><h3>{title}</h3>{sub && <p>{sub}</p>}</header>
      <div className="fb-verdicts" role="radiogroup" aria-label={title}>{VERDICTS.map(v => <button key={v} type="button" role="radio" aria-checked={verdict === v} className={`fb-v fb-${v}`} onClick={() => { setVerdict(v); if (!name.trim()) setName(savedName()); }}>{t(`fb.${v}`)}</button>)}</div>
      {verdict && <>
        <textarea value={text} onChange={e => setText(e.target.value)} maxLength={2000} rows={3} placeholder={t('fb.placeholder')} aria-label={t('fb.placeholder')} />
        <div className="fb-send"><input value={name} onChange={e => setName(e.target.value)} maxLength={60} placeholder={t('fb.name')} aria-label={t('fb.name')} /><button className="btn primary" disabled={!canSend}>{t('fb.send')}</button></div></>}
      {msg && <p className={msg.ok ? 'fb-ok' : 'fb-err'} role="status">{msg.s}</p>}
    </form>
    {items.length > 0 && <ul className="fb-list">{items.map(f => <li key={f.id} className={f.resolvedAt ? 'done' : ''}>
      <span className={`fb-tag fb-${f.verdict}`}>{t(`fb.${f.verdict}`)}</span>{f.text && <q>{f.text}</q>}
      <small>{f.author} · {new Date(f.createdAt).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' })}{f.resolvedAt ? ` · ${t('fb.resolved')}` : ''}</small></li>)}</ul>}
  </aside>);
}
