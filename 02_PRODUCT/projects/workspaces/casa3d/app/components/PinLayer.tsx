'use client';
// Pinii de comentariu peste o imagine din prezentarea clientului (cameră sau vederea de ansamblu). Pinii existenți sunt
// butoane numerotate, poziționate în procente (u,v relativ la imagine). „Adaugă pin” cere întâi PIN-ul primit de la
// designer (sesiunea e un cookie HttpOnly pus de server; aici se ține minte doar că a fost introdus, ca indiciu de UI),
// apoi un click pe imagine și o casetă cu părerea. Textele clientului rămân text React (fără HTML).
import { useEffect, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import ClientNameBox, { useClientNameBox } from './ClientNameBox';
import type { FeedbackItem } from './FeedbackBox';

const VERDICTS = ['like', 'change', 'question'] as const;
const okKey = (token: string) => `casa_pin_ok:${token}`;
const hasHint = (token: string) => { try { return Number(localStorage.getItem(okKey(token)) ?? 0) > Date.now(); } catch { return false; } };
const setHint = (token: string, until: number | null) => { try { if (until) localStorage.setItem(okKey(token), String(until)); else localStorage.removeItem(okKey(token)); } catch { /* fără stocare locală */ } };

export default function PinLayer({ token, surface, roomId, pins, onAdded }: { token: string; surface: 'room' | 'overview'; roomId: string | null; pins: FeedbackItem[]; onAdded(f: FeedbackItem): void }){
  const { t } = usePrefs(), box = useClientNameBox(token), layer = useRef<HTMLDivElement>(null), sending = useRef(false);
  const [mode, setMode] = useState<'idle' | 'pin' | 'place' | 'form'>('idle'), [pin, setPin] = useState(''), [at, setAt] = useState<{ u: number; v: number } | null>(null);
  const [verdict, setVerdict] = useState<FeedbackItem['verdict'] | null>(null), [text, setText] = useState(''), [busy, setBusy] = useState(false), [msg, setMsg] = useState<{ ok: boolean; s: string } | null>(null);
  const reset = () => { setMode('idle'); setAt(null); setVerdict(null); setText(''); setPin(''); };
  useEffect(() => { if (mode === 'idle') return; const on = (e: KeyboardEvent) => { if (e.key === 'Escape') reset(); }; addEventListener('keydown', on); return () => removeEventListener('keydown', on); }, [mode]);
  const start = () => { setMsg(null); setMode(hasHint(token) ? 'place' : 'pin'); };
  async function unlock(e: React.FormEvent){ e.preventDefault(); if (!/^\d{6}$/.test(pin) || busy) return; setBusy(true); setMsg(null);
    try { const r = await fetch(`/api/share/${token}/pin`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ pin }) }), j = await r.json().catch(() => null);
      if (!r.ok){ setMsg({ ok: false, s: j?.error || t('fb.error') }); return; }
      setHint(token, Date.parse(j?.expiresAt) || Date.now() + 3600e3); setPin(''); setMode('place'); }
    catch { setMsg({ ok: false, s: t('fb.error') }); } finally { setBusy(false); } }
  function place(e: React.MouseEvent<HTMLButtonElement>){ const b = layer.current?.getBoundingClientRect();
    // tastatura (Enter / Spațiu) nu are coordonate: pinul cade în mijloc
    const u = !b || e.detail === 0 || !b.width ? 0.5 : (e.clientX - b.left) / b.width, v = !b || e.detail === 0 || !b.height ? 0.5 : (e.clientY - b.top) / b.height;
    const clamp = (x: number) => Math.round(Math.min(1, Math.max(0, x)) * 1e4) / 1e4; setAt({ u: clamp(u), v: clamp(v) }); setMode('form'); }
  const canSend = !!verdict && !!at && box.ready && (verdict === 'like' || text.trim().length > 0) && !busy;
  async function send(e: React.FormEvent){ e.preventDefault(); if (!canSend || sending.current) return; const author = box.ensure(); if (!author){ setMsg({ ok: false, s: t('fb.nameFirst') }); return; }
    sending.current = true; setBusy(true); setMsg(null);
    try { const r = await fetch(`/api/share/${token}/pins`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ verdict, author, text, roomId, anchor: { surface, ...at } }) });
      const j = await r.json().catch(() => null);
      if (r.status === 401){ setHint(token, null); setMode('pin'); setMsg({ ok: false, s: t('pin.needPin') }); return; }
      if (!r.ok){ setMsg({ ok: false, s: j?.error || t('fb.error') }); return; }
      onAdded(j); reset(); setMsg({ ok: true, s: t('pin.sent') }); }
    catch { setMsg({ ok: false, s: t('fb.error') }); } finally { sending.current = false; setBusy(false); } }
  return (<div className="pin-layer no-print" ref={layer} aria-label={t('pin.layerAria')} role="group">
    {pins.map((f, i) => f.anchor && <button key={f.id} id={`pin-${f.id}`} type="button" className={`pin-dot fb-${f.verdict}${f.resolvedAt ? ' done' : ''}`}
      style={{ left: `${f.anchor.u * 100}%`, top: `${f.anchor.v * 100}%` }} aria-label={t('pin.aria', { n: i + 1, text: f.text || t(`fb.${f.verdict}`) })} title={f.text || undefined}>{i + 1}</button>)}
    {mode === 'place' && <button type="button" className="pin-place" onClick={place} aria-label={t('pin.placeAria')}><span>{t('pin.clickHint')}</span></button>}
    {at && mode === 'form' && <span className="pin-dot pin-new" style={{ left: `${at.u * 100}%`, top: `${at.v * 100}%` }} aria-hidden="true">+</span>}
    <div className="pin-ctl">
      {mode === 'idle' ? <button type="button" className="btn pin-add" onClick={start}>{t('pin.add')}</button>
        : <button type="button" className="btn pin-add" onClick={reset}>{t('pin.cancel')}</button>}
      {mode === 'pin' && <form className="pin-card" onSubmit={unlock}>
        <label>{t('pin.pinLabel')}<input value={pin} onChange={e => setPin(e.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" maxLength={6} required autoFocus /></label>
        <button className="btn primary" disabled={busy || pin.length !== 6}>{t('pin.unlock')}</button></form>}
      {mode === 'form' && <form className="pin-card" onSubmit={send}>
        <div className="fb-verdicts" role="radiogroup" aria-label={t('pin.add')}>{VERDICTS.map(v => <button key={v} type="button" role="radio" aria-checked={verdict === v} className={`fb-v fb-${v}`} onClick={() => setVerdict(v)}>{t(`fb.${v}`)}</button>)}</div>
        <ClientNameBox box={box} />
        <textarea value={text} onChange={e => setText(e.target.value)} maxLength={2000} rows={3} placeholder={t('fb.placeholder')} aria-label={t('fb.placeholder')} />
        <button className="btn primary" disabled={!canSend}>{t('fb.send')}</button></form>}
      {msg && <p className={`pin-msg ${msg.ok ? 'fb-ok' : 'fb-err'}`} role="status">{msg.s}</p>}
    </div>
  </div>);
}
