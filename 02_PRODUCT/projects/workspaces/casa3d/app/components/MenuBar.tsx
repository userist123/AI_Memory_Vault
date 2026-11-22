'use client';
// Bara de meniuri a editorului, ca în programele profesionale de birou (Fișier, Editare, Vizualizare, …): aceleași comenzi ca
// paleta Ctrl+K (o singură sursă), așezate pe meniuri după id. Tastatura: săgețile stânga/dreapta trec între meniuri, jos/sus
// prin elemente, Enter alege, Escape închide; F10 duce focusul pe bară. Cu un meniu deschis, trecerea mouse-ului peste
// alt titlu îl deschide pe acela.
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Command } from './CommandPalette';

export interface MenuDef { id: string; label: string; items: (Command | 'sep')[] }
/** Meniurile, în ordine: fiecare comandă merge în primul meniu care o cere; ce rămâne nu apare (obiectele din plan). */
export function buildMenus(commands: Command[], label: (k: string) => string): MenuDef[] {
  const by = (pred: (c: Command) => boolean) => commands.filter(pred);
  const ids = (...xs: string[]) => xs.map(x => commands.find(c => c.id === x)).filter((c): c is Command => !!c);
  const sep = <T,>(...groups: T[][]): (T | 'sep')[] => groups.filter(g => g.length).flatMap((g, i) => i ? ['sep' as const, ...g] : g);
  return [
    { id: 'file', label: label('menu.file'), items: sep(ids('rev'), by(c => c.id === 'print' || c.id === 'present' || c.id === 'board' || c.id.startsWith('doc-')), ids('home')) },
    { id: 'edit', label: label('menu.edit'), items: sep(ids('undo', 'redo'), ids('frame-other', 'mirror', 'array', 'lock'), by(c => c.id.startsWith('arrange.'))) },
    { id: 'view', label: label('menu.view'), items: sep(by(c => c.id.startsWith('v-')), ids('layers'), ids('ws.focus', 'ws.outliner', 'ws.classic', 'theme')) },
    { id: 'stages', label: label('menu.stages'), items: by(c => c.id.startsWith('st-')) },
    { id: 'tools', label: label('menu.tools'), items: by(c => c.id.startsWith('tool-')) },
    { id: 'settings', label: label('menu.settings'), items: ids('lang', 'units') },
    { id: 'help', label: label('menu.help'), items: sep(ids('palette', 'help'), ids('health')) },
  ].filter(m => m.items.length);
}

export default function MenuBar({ menus, ariaLabel }: { menus: MenuDef[]; ariaLabel: string }){
  const [open, setOpen] = useState<number | null>(null), [act, setAct] = useState(-1);
  const bar = useRef<HTMLDivElement>(null), titles = useRef<(HTMLButtonElement | null)[]>([]), items = useRef<(HTMLButtonElement | null)[]>([]);
  const enabled = useMemo(() => open === null ? [] : menus[open]!.items.map((x, i) => x !== 'sep' && !x.disabled ? i : -1).filter(i => i >= 0), [open, menus]);
  useEffect(() => { if (open === null) return; const out = (e: PointerEvent) => { if (!bar.current?.contains(e.target as Node)) setOpen(null); };
    window.addEventListener('pointerdown', out); return () => window.removeEventListener('pointerdown', out); }, [open]);
  useEffect(() => { if (open !== null && act >= 0) items.current[act]?.focus(); }, [open, act]);
  useEffect(() => { const f10 = (e: KeyboardEvent) => { if (e.key === 'F10' && !e.shiftKey && !document.querySelector('dialog[open],[aria-modal=true]')){ e.preventDefault(); titles.current[0]?.focus(); } };
    window.addEventListener('keydown', f10); return () => window.removeEventListener('keydown', f10); }, []);
  const openAt = (i: number, first = false) => { setOpen(i); const m = menus[i]!; setAct(first ? m.items.findIndex(x => x !== 'sep' && !x.disabled) : -1); };
  const close = (refocus = true) => { const i = open; setOpen(null); setAct(-1); if (refocus && i !== null) titles.current[i]?.focus(); };
  const run = (c: Command) => { setOpen(null); setAct(-1); c.run(); };
  const onTitleKey = (e: React.KeyboardEvent, i: number) => {
    const n = menus.length;
    if (e.key === 'ArrowRight' || e.key === 'ArrowLeft'){ e.preventDefault(); const j = (i + (e.key === 'ArrowRight' ? 1 : n - 1)) % n; titles.current[j]?.focus(); if (open !== null) openAt(j, true); }
    else if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' '){ e.preventDefault(); openAt(i, true); }
    else if (e.key === 'ArrowUp'){ e.preventDefault(); const m = menus[i]!; openAt(i); for (let j = m.items.length - 1; j >= 0; j--){ const x = m.items[j]!; if (x !== 'sep' && !x.disabled){ setAct(j); break; } } }
    else if (e.key === 'Escape' && open !== null){ e.preventDefault(); close(); } };
  const onItemKey = (e: React.KeyboardEvent) => { if (open === null) return; const k = enabled.indexOf(act), n = menus.length;
    if (e.key === 'ArrowDown'){ e.preventDefault(); setAct(enabled[(k + 1) % enabled.length] ?? -1); }
    else if (e.key === 'ArrowUp'){ e.preventDefault(); setAct(enabled[(k - 1 + enabled.length) % enabled.length] ?? -1); }
    else if (e.key === 'Home'){ e.preventDefault(); setAct(enabled[0] ?? -1); } else if (e.key === 'End'){ e.preventDefault(); setAct(enabled[enabled.length - 1] ?? -1); }
    else if (e.key === 'ArrowRight' || e.key === 'ArrowLeft'){ e.preventDefault(); const j = (open + (e.key === 'ArrowRight' ? 1 : n - 1)) % n; titles.current[j]?.focus(); openAt(j, true); }
    else if (e.key === 'Escape'){ e.preventDefault(); close(); } else if (e.key === 'Tab') close(false); };
  return (<div className="menubar" role="menubar" aria-label={ariaLabel} ref={bar}>
    {menus.map((m, i) => <div key={m.id} className="mb-menu">
      <button type="button" role="menuitem" aria-haspopup="menu" aria-expanded={open === i} ref={el => { titles.current[i] = el; }} tabIndex={i === 0 ? 0 : -1}
        className={open === i ? 'is-open' : undefined} onClick={() => open === i ? close(false) : openAt(i)} onPointerEnter={() => { if (open !== null && open !== i){ openAt(i); titles.current[i]?.focus(); } }} onKeyDown={e => onTitleKey(e, i)}>{m.label}</button>
      {open === i && <div className="mb-pop" role="menu" aria-label={m.label} onKeyDown={onItemKey}>
        {m.items.map((x, j) => x === 'sep' ? <div key={`s${j}`} role="separator" className="mb-sep" /> :
          <button key={x.id} type="button" role="menuitem" ref={el => { items.current[j] = el; }} tabIndex={-1} disabled={x.disabled} aria-disabled={x.disabled || undefined}
            className={act === j ? 'is-act' : undefined} onPointerEnter={() => !x.disabled && setAct(j)} onClick={() => run(x)}>
            <span className={`mb-check ${x.done ? 'is-done' : ''}`} aria-hidden="true" /><span className="mb-label">{x.label}</span>{x.hint && <span className="mb-hint">{x.hint}</span>}</button>)}
      </div>}
    </div>)}
  </div>);
}
