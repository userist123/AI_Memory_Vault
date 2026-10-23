'use client';
// Bara de unelte din stânga. Când etapa are multe unelte (Releveu: 15), pe ecran lat ele se grupează ca bara să nu
// iasă din ecran: „Goluri” (ușă, fereastră, trecere, nișă), „Structură” (stâlp, ghenă, grindă, perete de sticlă) și
// „Adnotări” (riglă, cotă, text). Grupa e un buton care deschide lista ei (model disclosure: Tab intră în listă, Escape o
// închide și întoarce focusul); tastele uneltelor (lib/commands.ts) aleg direct, fără meniu. Pe telefon bara derulează
// pe orizontală, deci rămâne netgrupată.
import { useEffect, useRef, useState } from 'react';
import type { Tool } from './PlanView';
import { railSlots } from '@/lib/tool-groups';

interface Props { tools: Tool[]; active: Tool; grouped: boolean; icon: Record<Tool, string>; label(tk: Tool): string; title(tk: Tool): string; keys(tk: Tool): string; groupLabel(id: string): string; onPick(tk: Tool): void; ariaLabel: string }
export default function ToolRail(p: Props){
  const [open, setOpen] = useState<string | null>(null), [last, setLast] = useState<Record<string, Tool>>({}), box = useRef<HTMLElement>(null);
  // un clic în afara grupei deschise o închide
  useEffect(() => { if (!open) return; const f = (e: PointerEvent) => { if (!(e.target as Element).closest?.(`[data-group="${open}"]`)) setOpen(null); };
    document.addEventListener('pointerdown', f); return () => document.removeEventListener('pointerdown', f); }, [open]);
  const btn = (tk: Tool, after?: () => void) => <button key={tk} type="button" aria-pressed={p.active === tk} onClick={() => { p.onPick(tk); after?.(); }} title={p.title(tk)} aria-keyshortcuts={p.keys(tk)}>
    <svg viewBox="0 0 24 24" aria-hidden="true"><path d={p.icon[tk]} /></svg>{p.label(tk)}</button>;
  return (<nav ref={box} className="tools studio-toolrail" aria-label={p.ariaLabel}>
    {railSlots(p.tools, p.grouped).map(s => { if ('tool' in s) return btn(s.tool);
      const on = s.tools.includes(p.active), shown = on ? p.active : last[s.group] && s.tools.includes(last[s.group]!) ? last[s.group]! : s.tools[0]!, isOpen = open === s.group, menu = `toolgroup-${s.group}`;
      const close = (focus: boolean) => { setOpen(null); if (focus) box.current?.querySelector<HTMLButtonElement>(`[data-group="${s.group}"] > button`)?.focus(); };
      return (<div key={s.group} className="toolgroup" data-group={s.group} onKeyDown={e => { if (e.key === 'Escape' && isOpen){ e.preventDefault(); e.stopPropagation(); close(true); } }}
        onBlur={e => { if (isOpen && !e.currentTarget.contains(e.relatedTarget as Node | null)) setOpen(null); }}>
        <button type="button" className={on ? 'is-active' : undefined} aria-expanded={isOpen} aria-controls={menu} aria-label={`${p.groupLabel(s.group)} · ${p.label(shown)}`} title={p.groupLabel(s.group)} onClick={() => setOpen(isOpen ? null : s.group)}>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d={p.icon[shown]} /></svg>{p.groupLabel(s.group)}<span className="tg-caret" aria-hidden="true">▸</span></button>
        {isOpen && <div id={menu} className="toolgroup-menu" role="group" aria-label={p.groupLabel(s.group)}>
          {s.tools.map(tk => btn(tk, () => { setLast(l => ({ ...l, [s.group]: tk })); close(false); }))}</div>}
      </div>); })}
  </nav>);
}
