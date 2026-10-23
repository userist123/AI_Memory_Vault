'use client';
// „Firma ta”: numele, logo-ul, CUI-ul, IBAN-ul și contactul care apar pe planșe și pe dosarul de predare.
import { useEffect, useState } from 'react';
import type { StudioProfile } from '@/core/studio-profile';
import { usePrefs } from '@/lib/prefs';

const FIELDS = ['name', 'cui', 'regCom', 'address', 'phone', 'email', 'website', 'iban', 'bank'] as const;
// logo-ul se micșorează în browser la maximum 400 px și se salvează PNG (fișierele mari nu ajung pe server)
async function shrink(file: File): Promise<string> {
  const url = URL.createObjectURL(file); try { const img = new Image(); img.src = url; await img.decode();
    const k = Math.min(1, 400 / Math.max(img.width, img.height)), c = document.createElement('canvas'); c.width = Math.round(img.width * k); c.height = Math.round(img.height * k);
    c.getContext('2d')!.drawImage(img, 0, 0, c.width, c.height); return c.toDataURL('image/png'); } finally { URL.revokeObjectURL(url); }
}
export default function StudioProfileForm(){
  const { t } = usePrefs(), [p, setP] = useState<StudioProfile | null>(null), [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null), [busy, setBusy] = useState(false);
  useEffect(() => { fetch('/api/studio').then(r => r.ok ? r.json() : {}).then(setP).catch(() => setP({})); }, []);
  if (!p) return null;
  async function save(){ setBusy(true); setMsg(null); const r = await fetch('/api/studio', { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(p) }); const j = await r.json().catch(() => ({}));
    setBusy(false); if (r.ok){ setP(j); setMsg({ ok: true, text: t('studio.saved') }); } else setMsg({ ok: false, text: j.error || t('studio.error') }); }
  return (<details className="studio-prof" id="studio"><summary><b>{t('studio.title')}</b>{p.name && <span className="muted"> · {p.name}</span>}</summary>
    <p className="prov">{t('studio.note')}</p>
    <div className="grid2">{FIELDS.map(k => <label key={k} className="f"><span>{t(`studio.${k}`)}</span><input value={p[k] ?? ''} maxLength={160} onChange={e => setP({ ...p, [k]: e.target.value })} /></label>)}</div>
    <div className="studio-logo">{p.logo ? <img src={p.logo} alt={t('studio.logo')} /> : <span className="muted">{t('studio.noLogo')}</span>}
      <label className="btn">{t('studio.uploadLogo')}<input type="file" accept="image/png,image/jpeg" hidden onChange={async e => { const f = e.target.files?.[0]; if (f) setP({ ...p, logo: await shrink(f) }); }} /></label>
      {p.logo && <button className="btn" onClick={() => setP({ ...p, logo: undefined })}>{t('studio.removeLogo')}</button>}</div>
    <div className="pcard-act"><button className="btn primary" disabled={busy} onClick={save}>{t('studio.save')}</button>{msg && <span className={msg.ok ? 'prov' : 'issue ERROR'} role="status">{msg.text}</span>}</div>
  </details>);
}
