'use client';
// Secțiune pliabilă cu stare memorată (UI/UX P2-9): panoul etapei Releveu are client, sarcini, ore, contract și releveu
// pe șantier, peste 2 500 px; designerul pliază ce nu folosește, iar alegerea rămâne la următoarea deschidere (în acest
// browser, pentru toate proiectele). Titlul (cu contoarele lui) stă în <summary>, deci se vede și când e pliată.
import { useState, type ReactNode } from 'react';

const KEY = 'casa3d.folds';
const read = (): Record<string, boolean> => { try { const o = JSON.parse(localStorage.getItem(KEY) || '{}'); return o && typeof o === 'object' ? o : {}; } catch { return {}; } };
export function foldOpen(id: string, def = true){ const v = read()[id]; return typeof v === 'boolean' ? v : def; }
function remember(id: string, open: boolean){ try { localStorage.setItem(KEY, JSON.stringify({ ...read(), [id]: open })); } catch { /* stocare blocată: rămâne doar pe sesiune */ } }

export default function FoldSection({ id, title, className, children, defaultOpen = true }: { id: string; title: ReactNode; className?: string; children: ReactNode; defaultOpen?: boolean }){
  const [open, setOpen] = useState(() => typeof window === 'undefined' ? defaultOpen : foldOpen(id, defaultOpen));
  return (<details className="fold" data-fold={id} open={open} onToggle={e => { const o = e.currentTarget.open; if (o !== open){ setOpen(o); remember(id, o); } }}>
    <summary>{title}</summary>
    <div className={className}>{children}</div>
  </details>);
}
