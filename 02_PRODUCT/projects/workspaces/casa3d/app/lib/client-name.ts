'use client';
// Numele clientului pe linkul de prezentare: se cere O SINGURĂ DATĂ pe link și se ține minte în browserul clientului
// (doar o comoditate; serverul validează numele la fiecare trimitere). Toate casetele de pe pagină (păreri, aprobări,
// alegerea variantei A/B, modificările cu preț) folosesc același nume și află imediat când e scris sau schimbat.
import { useCallback, useEffect, useState } from 'react';

export const CLIENT_NAME_EVENT = 'casa3d:client-name';
export const clientNameKey = (token: string) => `casa3d.client.name.${token}`;
export const CLIENT_NAME_MAX = 60;
/** Numele curățat: fără spații în plus, cel mult 60 de caractere. */
export const cleanClientName = (s: string) => s.replace(/\s+/g, ' ').trim().slice(0, CLIENT_NAME_MAX);

export function readClientName(token: string): string { try { return localStorage.getItem(clientNameKey(token)) ?? ''; } catch { return ''; } }
export function writeClientName(token: string, name: string){
  const n = cleanClientName(name);
  try { if (n) localStorage.setItem(clientNameKey(token), n); else localStorage.removeItem(clientNameKey(token)); } catch { /* fără stocare: numele ține doar cât e pagina deschisă */ }
  try { window.dispatchEvent(new CustomEvent(CLIENT_NAME_EVENT, { detail: { token, name: n } })); } catch { /* fără window (test) */ }
  return n;
}
/** [numele ținut minte (gol până îl scrie clientul), salvează(nume)] — același în toate casetele de pe link. */
export function useClientName(token: string): [string, (name: string) => string] {
  const [name, setName] = useState('');
  useEffect(() => { setName(readClientName(token));
    const on = (e: Event) => { const d = (e as CustomEvent).detail; if (d?.token === token) setName(d.name); };
    window.addEventListener(CLIENT_NAME_EVENT, on); return () => window.removeEventListener(CLIENT_NAME_EVENT, on); }, [token]);
  const save = useCallback((n: string) => { const c = writeClientName(token, n); setName(c); return c; }, [token]);
  return [name, save];
}
