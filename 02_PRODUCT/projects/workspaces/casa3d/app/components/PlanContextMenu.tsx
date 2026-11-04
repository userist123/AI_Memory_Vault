'use client';
// Meniul contextual al planului (PLAN_A1_2): comenzile vin din registru (lib/commands.ts contextCommands), cu eticheta și
// scurtătura de acolo. Modelul APG „menu”: focus pe primul element, săgeți / Home / End, Enter, Esc închide și întoarce
// focusul pe plan; clic în afară sau Tab închide.
import { useEffect, useLayoutEffect, useRef, useState } from 'react';

export interface MenuItem { id: string; label: string; hint?: string; danger?: boolean }
interface Props { x: number; y: number; label: string; items: MenuItem[]; onPick(id: string): void; onClose(): void }

export default function PlanContextMenu({ x, y, label, items, onPick, onClose }: Props){
  const ref = useRef<HTMLDivElement>(null), [pos, setPos] = useState({ x, y });
  // în ecran: meniul se deschide la cursor, dar nu iese peste marginea ferestrei (16 px)
  useLayoutEffect(() => { const el = ref.current; if (!el) return; const r = el.getBoundingClientRect(), m = 16;
    setPos({ x: Math.max(m, Math.min(x, innerWidth - r.width - m)), y: Math.max(m, Math.min(y, innerHeight - r.height - m)) });
    (el.querySelector('[role=menuitem]') as HTMLElement | null)?.focus(); }, [x, y]);
  useEffect(() => { const out = (e: PointerEvent) => { if (!ref.current?.contains(e.target as Node)) onClose(); };
    document.addEventListener('pointerdown', out, true); return () => document.removeEventListener('pointerdown', out, true); }, [onClose]);
  function key(e: React.KeyboardEvent){
    const all = [...(ref.current?.querySelectorAll<HTMLElement>('[role=menuitem]') ?? [])], i = all.indexOf(document.activeElement as HTMLElement);
    const go = (n: number) => { e.preventDefault(); all[(n + all.length) % all.length]?.focus(); };
    if (e.key === 'ArrowDown') go(i + 1); else if (e.key === 'ArrowUp') go(i - 1); else if (e.key === 'Home') go(0); else if (e.key === 'End') go(all.length - 1);
    else if (e.key === 'Escape' || e.key === 'Tab'){ e.preventDefault(); onClose(); }
    e.stopPropagation();   // tastele meniului nu ajung la scurtăturile editorului
  }
  return (<div ref={ref} className="plan-menu" role="menu" aria-label={label} style={{ left: pos.x, top: pos.y }} onKeyDown={key} onContextMenu={e => e.preventDefault()}>
    {items.map(it => <button key={it.id} type="button" role="menuitem" tabIndex={-1} className={it.danger ? 'danger' : undefined} onClick={() => onPick(it.id)}>
      <span>{it.label}</span>{it.hint && <kbd className="cmdk-hint" aria-hidden="true">{it.hint}</kbd>}</button>)}
  </div>);
}
