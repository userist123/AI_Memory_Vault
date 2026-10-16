'use client';
// Accesul de pe telefon, pe panoul studioului. Pe calculatorul designerului: parola și adresa publică.
// Deschis de pe telefon (prin adresa publică, după login): doar butonul de ieșire.
import { useEffect, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';
import { intlLocale } from '@/lib/i18n';
import { isLocalHostname } from '@/lib/public-gate';

type St = { enabled: boolean; updatedAt: string | null; publicUrl: string | null };
export default function PhoneAccess(){
  const { t, lang } = usePrefs(), [st, setSt] = useState<St | null | 'remote'>(null), [pw, setPw] = useState(''), [pw2, setPw2] = useState(''), [msg, setMsg] = useState<{ ok: boolean; s: string } | null>(null), [busy, setBusy] = useState(false);
  // setarea parolei există doar pe calculatorul designerului (poarta o ascunde prin tunel): din afară nu o mai cerem deloc
  const load = () => { if (!isLocalHostname(location.hostname)){ setSt('remote'); return; }
    fetch('/api/auth/password').then(async r => setSt(r.ok ? await r.json() : 'remote')).catch(() => setSt('remote')); };
  useEffect(() => { load(); }, []);
  if (st === null) return null;
  if (st === 'remote') return (<p className="phone-out"><button className="btn" onClick={async () => { await fetch('/api/auth/logout', { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' }).catch(() => null); location.replace('/login'); }}>{t('auth.logout')}</button></p>);
  async function save(e: React.FormEvent){ e.preventDefault(); if (pw !== pw2){ setMsg({ ok: false, s: t('phone.mismatch') }); return; } setBusy(true); setMsg(null);
    const r = await fetch('/api/auth/password', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ password: pw }) }), j = await r.json().catch(() => ({}));
    setBusy(false); if (!r.ok){ setMsg({ ok: false, s: j.error || t('phone.failed') }); return; } setPw(''); setPw2(''); setMsg({ ok: true, s: t('phone.saved') }); load(); }
  async function off(){ if (!(await askConfirm({ title: t('phone.offConfirm'), confirmLabel: t('dlg.phoneOffOk'), danger: true }))) return; await fetch('/api/auth/password', { method: 'DELETE' }); setMsg(null); load(); }
  const copy = async (u: string) => { try { await navigator.clipboard.writeText(u); setMsg({ ok: true, s: t('phone.copied') }); } catch { /* adresa rămâne afișată */ } };
  return (<section className="phone">
    <div className="home-sec"><h2>{t('phone.title')}</h2><span className={`muted ${st.enabled ? 'on' : ''}`}>{st.enabled ? t('phone.on', { d: new Date(st.updatedAt!).toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short' }) }) : t('phone.off')}</span></div>
    <div className="phone-grid">
      <div><p>{t('phone.how')}</p>
        {st.publicUrl ? <div className="phone-url"><b>{st.publicUrl}</b><div><button className="btn" onClick={() => copy(st.publicUrl!)}>{t('client.copy')}</button><a className="btn" href={`https://wa.me/?text=${encodeURIComponent(st.publicUrl)}`} target="_blank" rel="noopener">WhatsApp</a></div></div>
          : <div><p className="muted">{t('phone.noTunnel')}</p><details className="phone-help"><summary>{t('phone.tunnelHelp')}</summary><p className="muted small">{t('phone.tunnelPath')}</p></details></div>}
        <p className="muted small">{t('phone.note')}</p></div>
      <form onSubmit={save}>
        <label>{st.enabled ? t('phone.newPassword') : t('phone.password')}<input type="password" autoComplete="new-password" value={pw} onChange={e => setPw(e.target.value)} minLength={10} maxLength={200} required /></label>
        <label>{t('phone.repeat')}<input type="password" autoComplete="new-password" value={pw2} onChange={e => setPw2(e.target.value)} minLength={10} maxLength={200} required /></label>
        <div className="phone-act"><button className="btn primary" disabled={busy}>{st.enabled ? t('phone.change') : t('phone.enable')}</button>{st.enabled && <button type="button" className="btn danger" onClick={off}>{t('phone.disable')}</button>}</div>
        {msg && <p className={msg.ok ? 'fb-ok' : 'fb-err'} role="status">{msg.s}</p>}
      </form>
    </div>
  </section>);
}
