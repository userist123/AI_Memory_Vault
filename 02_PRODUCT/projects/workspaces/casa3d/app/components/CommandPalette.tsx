'use client';
// Command palette (Ctrl+K / Cmd+K or the search button): a modal <dialog> with a combobox and a listbox. Fuzzy search
// without diacritics (lib/fuzzy.ts), arrows / Home / End / Enter / Esc, focus kept inside, focus returned on close.
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { fuzzyFilter, moveActive, isPaletteShortcut } from '@/lib/fuzzy';
import { paletteKeyAction, type PaletteTarget } from '@/lib/palette-keys';
import { usePrefs } from '@/lib/prefs';
import Icon from './Icon';

export interface Command {
  id: string;
  label: string;
  group: string;
  /** extra searchable words (stage number, phase, synonyms); never shown */
  keywords?: string[];
  /** short text on the right (shortcut or state) */
  hint?: string;
  /** stage rows show their done dot */
  done?: boolean;
  disabled?: boolean;
  run(): void;
}

const FOCUSABLE = 'input,button:not([disabled])';
export default function CommandPalette({ open, onClose, commands }: { open: boolean; onClose(): void; commands: Command[] }){
  const { t, tp } = usePrefs(), ref = useRef<HTMLDialogElement>(null), input = useRef<HTMLInputElement>(null), list = useRef<HTMLUListElement>(null);
  const back = useRef<HTMLElement | null>(null), [q, setQ] = useState(''), [active, setActive] = useState(0), uid = useId().replace(/:/g, '');
  const shown = useMemo(() => fuzzyFilter(commands.filter(c => !c.disabled), q, c => [c.label, c.group, ...(c.keywords ?? [])]), [commands, q]);
  // open / close the native modal dialog; remember and give back the focus
  useEffect(() => { const el = ref.current; if (!el) return;
    if (open){ back.current = document.activeElement as HTMLElement | null; setQ(''); setActive(0); try { if (!el.open) el.showModal(); } catch { el.setAttribute('open', ''); }
      requestAnimationFrame(() => input.current?.focus()); }
    else if (el.open || el.hasAttribute('open')){ try { el.close(); } catch { el.removeAttribute('open'); } const b = back.current; back.current = null; if (b?.isConnected) b.focus?.(); } }, [open]);
  useEffect(() => { setActive(a => shown.length ? Math.min(a, shown.length - 1) : -1); }, [shown.length]);
  useEffect(() => { list.current?.querySelector<HTMLElement>(`[data-i="${active}"]`)?.scrollIntoView({ block: 'nearest' }); }, [active]);
  const run = (c: Command | undefined) => { if (!c) return; onClose(); setTimeout(() => c.run(), 0); };
  // every key stays inside the dialog: the editor's shortcuts (arrows nudge, Delete, R) must not see it
  const onKey = (e: React.KeyboardEvent) => { e.stopPropagation();
    if (isPaletteShortcut(e.nativeEvent)){ e.preventDefault(); onClose(); return; }
    const el = e.target as HTMLElement, where: PaletteTarget = el === input.current ? 'input' : el.getAttribute('role') === 'option' ? 'option' : el.tagName === 'BUTTON' ? 'button' : 'other';
    const act = paletteKeyAction(e.key, where, !q);
    if (act === 'move'){ e.preventDefault(); setActive(a => moveActive(e.key, a, shown.length)); }
    else if (act === 'run'){ e.preventDefault(); run(shown[active]); }
    else if (act === 'close'){ e.preventDefault(); onClose(); }
    else if (act === 'trap'){ const els = [...ref.current!.querySelectorAll<HTMLElement>(FOCUSABLE)]; if (!els.length) return; e.preventDefault();
      const i = els.indexOf(document.activeElement as HTMLElement), n = e.shiftKey ? (i <= 0 ? els.length - 1 : i - 1) : (i + 1) % els.length; els[n]!.focus(); } };
  // groups in the order of their first result
  const groups: { name: string; rows: { c: Command; i: number }[] }[] = [];
  shown.forEach((c, i) => { let g = groups.find(x => x.name === c.group); if (!g){ g = { name: c.group, rows: [] }; groups.push(g); } g.rows.push({ c, i }); });
  const lid = `cmdk-list-${uid}`, oid = (i: number) => `cmdk-opt-${uid}-${i}`;
  return (<dialog ref={ref} className="dlg cmdk" aria-modal="true" aria-labelledby={`cmdk-t-${uid}`} onKeyDown={onKey}
    onCancel={e => { e.preventDefault(); onClose(); }} onClick={e => { if (e.target === ref.current) onClose(); }}>
    {open && <div className="cmdk-card">
      <h2 id={`cmdk-t-${uid}`} className="sr-only">{t('cmdk.title')}</h2>
      <div className="cmdk-search">
        <Icon name="search" size={18} />
        <input ref={input} type="text" role="combobox" aria-expanded={shown.length > 0} aria-controls={lid} aria-autocomplete="list" aria-activedescendant={active >= 0 && shown[active] ? oid(active) : undefined}
          aria-label={t('cmdk.placeholder')} placeholder={t('cmdk.placeholder')} value={q} autoComplete="off" spellCheck={false} onChange={e => { setQ(e.target.value); setActive(0); }} />
        <button type="button" className="btn ghost icon-btn cmdk-x" aria-label={t('cmdk.close')} onClick={onClose}><Icon name="close" size={16} /></button>
      </div>
      <ul ref={list} id={lid} role="listbox" aria-label={t('cmdk.title')} className="cmdk-list">
        {groups.map(g => <li key={g.name} role="presentation" className="cmdk-group"><span className="cmdk-gname" aria-hidden="true">{g.name}</span>
          <ul role="group" aria-label={g.name}>{g.rows.map(({ c, i }) => <li key={c.id} id={oid(i)} data-i={i} role="option" aria-selected={i === active} className="cmdk-opt"
            onMouseMove={() => { if (i !== active) setActive(i); }} onClick={() => run(c)}>
            {c.done != null ? <span className={`st-dot ${c.done ? 'is-done' : ''}`} aria-hidden="true" /> : <Icon name="arrowRight" size={16} />}
            <span className="cmdk-label">{c.label}{c.done != null && <span className="sr-only">, {c.done ? t('stage.doneAria') : t('stage.todoAria')}</span>}</span>
            {c.hint && <kbd className="cmdk-hint">{c.hint}</kbd>}</li>)}</ul></li>)}
      </ul>
      {shown.length === 0 && <div className="cmdk-empty" role="status"><b>{t('cmdk.empty', { q: q.trim() })}</b><span>{t('cmdk.emptyHint')}</span></div>}
      <p className="cmdk-foot" aria-hidden="true">{t('cmdk.keys')}</p>
      <p className="sr-only" role="status" aria-live="polite">{tp('cmdk.results', shown.length)}</p>
    </div>}
  </dialog>);
}
