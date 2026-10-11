'use client';
// Login pentru accesul de pe telefon (prin adresa publică). Pe calculatorul designerului nu e nevoie de el.
import { useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { safeNext } from '@/lib/public-gate';

export default function Login(){
  const { t } = usePrefs(), [pw, setPw] = useState(''), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  async function go(e: React.FormEvent){ e.preventDefault(); setBusy(true); setErr('');
    try { const r = await fetch('/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ password: pw }) });
      if (r.ok){ location.replace(safeNext(new URLSearchParams(location.search).get('next'))); return; }
      const j = await r.json().catch(() => ({})); setErr(j.error || t('auth.failed')); }
    catch { setErr(t('auth.failed')); } finally { setBusy(false); } }
  return (<main className="login">
    <form onSubmit={go}>
      <span className="wordmark">Casa<b>3D</b></span>
      <h1>{t('auth.title')}</h1><p className="muted">{t('auth.sub')}</p>
      <label>{t('auth.password')}<input type="password" autoComplete="current-password" value={pw} onChange={e => setPw(e.target.value)} autoFocus required maxLength={200} /></label>
      {err && <p className="err" role="alert">{err}</p>}
      <button className="btn primary" disabled={busy || !pw}>{t('auth.enter')}</button>
    </form>
  </main>);
}
