'use client';
// Login pentru accesul de pe telefon (prin adresa publică). Pe calculatorul designerului nu e nevoie de el.
import { useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { safeNext } from '@/lib/public-gate';

export default function Login(){
  const { t } = usePrefs(), [pw, setPw] = useState(''), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  async function go(e: React.FormEvent){ e.preventDefault(); setBusy(true); setErr('');
    try { const r = await fetch('/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ password: pw }) });
      // deschisă direct (/login?next=…) sau arătată în locul paginii cerute (adresa rămâne cea cerută)
      if (r.ok){ location.replace(safeNext(location.pathname.startsWith('/login') ? new URLSearchParams(location.search).get('next') : location.pathname + location.search)); return; }
      const j = await r.json().catch(() => ({}));
      // blocat după prea multe încercări: cât mai e de așteptat, în limba aleasă
      if (r.status === 429){ const s = Number(j.retryAfter ?? r.headers.get('retry-after')); setErr(Number.isFinite(s) && s > 0 ? t('auth.locked', { min: Math.max(1, Math.ceil(s / 60)) }) : (j.error || t('auth.failed'))); return; }
      setErr(j.error || t('auth.failed')); }
    catch { setErr(t('auth.failed')); } finally { setBusy(false); } }
  return (<main className="login">
    <form onSubmit={go}>
      <span className="wordmark">Casa<b>3D</b></span>
      <h1>{t('auth.title')}</h1><p className="muted">{t('auth.sub')}</p>
      {/* un singur cont: utilizatorul e fix, dar managerii de parole de pe telefon cer un câmp „username” ca să salveze parola */}
      <input type="text" name="username" autoComplete="username" value="casa3d" readOnly hidden aria-hidden="true" tabIndex={-1} aria-label={t('auth.username')} />
      <label>{t('auth.password')}<input type="password" name="password" autoComplete="current-password" value={pw} onChange={e => setPw(e.target.value)} autoFocus required maxLength={200} /></label>
      {err && <p className="err" role="alert">{err}</p>}
      <button className="btn primary" disabled={busy || !pw}>{t('auth.enter')}</button>
    </form>
  </main>);
}
