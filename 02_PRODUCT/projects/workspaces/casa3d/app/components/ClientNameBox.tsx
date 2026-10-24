'use client';
// Numele clientului, cerut o singură dată pe link (lib/client-name.ts): cât nu e scris, un câmp; după aceea doar
// „Trimiteți ca Ana · Schimbați numele”, în toate casetele de pe pagină.
import { useState } from 'react';
import { cleanClientName, useClientName, CLIENT_NAME_MAX } from '@/lib/client-name';
import { usePrefs } from '@/lib/prefs';

/** Starea numelui pentru o casetă: `ensure()` întoarce numele (îl salvează la prima folosire) sau null dacă lipsește. */
export function useClientNameBox(token: string){
  const [name, save] = useClientName(token), [draft, setDraft] = useState('');
  const ensure = (): string | null => { if (name) return name; const n = cleanClientName(draft); return n ? save(n) : null; };
  return { name, draft, setDraft, save, ensure, ready: !!(name || cleanClientName(draft)) };
}
export default function ClientNameBox({ box, className }: { box: ReturnType<typeof useClientNameBox>; className?: string }){
  const { t } = usePrefs();
  if (box.name) return <p className={`client-as muted small ${className ?? ''}`}>{t('client.as', { name: box.name })} <button type="button" className="linkish" onClick={() => { box.setDraft(box.name); box.save(''); }}>{t('client.changeName')}</button></p>;
  return <input className={className} value={box.draft} maxLength={CLIENT_NAME_MAX} autoComplete="name" placeholder={t('fb.name')} aria-label={t('fb.name')}
    onChange={e => box.setDraft(e.target.value)} onBlur={() => { const n = cleanClientName(box.draft); if (n) box.save(n); }} />;
}
