'use client';
// Panoul Structură (PLAN_A1_3): arborele proiectului, nivel → cameră → piese / zone / obiecte pe perete, apoi Structura
// (perete → goluri, nișe) și Notele. Model: core/outliner.ts peste descrierile din core/plan-describe.ts (aceleași texte
// ca pentru cititorul de ecran). Arbore APG: o singură oprire Tab (roving tabindex), sus/jos trec prin rânduri, dreapta
// deschide sau intră, stânga închide sau urcă, Home/End, Enter alege și încadrează, Space doar alege, Escape duce în plan.
import { useEffect, useMemo, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import Icon from './Icon';
import { findPath, visibleRows, type OutlinerNode } from '@/core/outliner';
import type { MirrorItem } from '@/core/plan-describe';

type OnPick = (level: number, item: MirrorItem, frame: boolean) => void;
// semne mici pe tipuri (14 px), desenate cu culoarea textului: camera e un contur, peretele o bară, golul un arc
const GLYPH: Record<string, string> = {
  room: 'M2.5 2.5h9v9h-4v-3.5h-5z', wall: 'M1.5 5.5h11v3h-11z', glass_wall: 'M1.5 6.5h11M1.5 8h11', beam: 'M1.5 4.5h11M1.5 9.5h11',
  opening: 'M3 11.5V3.5a8 8 0 0 1 8 8z', placement: 'M3 4.5h8a1 1 0 0 1 1 1v4h-10v-4a1 1 0 0 1 1-1z', column: 'M4.5 4.5h5v5h-5z',
  stair: 'M2.5 11.5h3v-3h3v-3h3v-3', niche: 'M2.5 4.5h3v5h3v-5h3', wall_item: 'M3.5 3.5h7v5h-7zM7 8.5v3', floor_zone: 'M2.5 2.5h9v9h-9zM2.5 7h9',
  annotation: 'M2 7h10M2 4.5v5M12 4.5v5',
  ceiling_zone: 'M2.5 4.5h9M4 4.5v3h6v-3', railing: 'M1.5 4.5h11M3 4.5v6M7 4.5v6M11 4.5v6'
};
const Glyph = ({ kind }: { kind?: string }) => <svg className="ol-glyph" width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">
  {kind && GLYPH[kind] ? <path d={GLYPH[kind]} /> : <circle cx="7" cy="7" r="1.6" />}</svg>;

export default function Outliner({ nodes, level, selected, onPick, onHover, onHide }: {
  nodes: readonly OutlinerNode[]; level: number; selected: { kind: string; id: string } | null;
  onPick: OnPick; onHover(placementId: string | null): void; onHide?(): void }){
  const { t } = usePrefs(), box = useRef<HTMLUListElement>(null);
  const path = useMemo(() => findPath(nodes, level, selected), [nodes, level, selected]);
  const [open, setOpen] = useState<ReadonlySet<string>>(() => new Set([`L${level}`, ...(path?.ancestors ?? [])]));
  const [cur, setCur] = useState<string | null>(null);
  // nivelul curent și calea spre selecție (din plan, din 3D sau din Ctrl+K) se deschid singure; restul rămâne cum l-a lăsat utilizatorul
  useEffect(() => { setOpen(o => { const need = [`L${level}`, ...(path?.ancestors ?? [])].filter(k => !o.has(k)); return need.length ? new Set([...o, ...need]) : o; }); }, [level, path?.key]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (!path) return; setCur(path.key); box.current?.querySelector<HTMLElement>(`[data-key="${CSS.escape(path.key)}"]`)?.scrollIntoView({ block: 'nearest' }); }, [path?.key]); // eslint-disable-line react-hooks/exhaustive-deps
  const rows = useMemo(() => visibleRows(nodes, open), [nodes, open]);
  const ixState = Math.max(0, rows.findIndex(r => r.node.key === cur)), active = rows[ixState]?.node.key;
  const toggle = (k: string, want?: boolean) => setOpen(o => { const on = o.has(k), next = want ?? !on; if (next === on) return o; const n = new Set(o); if (next) n.add(k); else n.delete(k); return n; });
  const focusKey = (k: string) => { setCur(k); requestAnimationFrame(() => box.current?.querySelector<HTMLElement>(`[data-key="${CSS.escape(k)}"]`)?.focus()); };
  const choose = (n: OutlinerNode, frame: boolean) => { if (n.item) onPick(n.level, n.item, frame); else toggle(n.key); };
  const onKey = (e: React.KeyboardEvent) => {
    // rândul e cel care primește tasta (focusul real), nu cel din stare, care poate rămâne cu un pas în urmă
    const fk = (e.target as HTMLElement).closest<HTMLElement>('[data-key]')?.dataset.key, at = fk ? rows.findIndex(x => x.node.key === fk) : -1, ix = at >= 0 ? at : ixState;
    const r = rows[ix]; if (!r) return; const n = r.node, kids = n.children.length > 0, isOpen = open.has(n.key);
    let done = true;
    switch (e.key){
      case 'ArrowDown': if (rows[ix + 1]) focusKey(rows[ix + 1]!.node.key); break;
      case 'ArrowUp': if (rows[ix - 1]) focusKey(rows[ix - 1]!.node.key); break;
      case 'Home': if (rows[0]) focusKey(rows[0].node.key); break;
      case 'End': if (rows.length) focusKey(rows[rows.length - 1]!.node.key); break;
      case 'ArrowRight': if (kids && !isOpen) toggle(n.key, true); else if (kids && rows[ix + 1]) focusKey(rows[ix + 1]!.node.key); break;
      case 'ArrowLeft': if (kids && isOpen) toggle(n.key, false); else if (r.parent) focusKey(r.parent); break;
      case 'Enter': choose(n, true); break;
      case ' ': if (n.item) onPick(n.level, n.item, false); else toggle(n.key); break;
      // fără plan (doar 3D) focusul iese din arbore, ca Escape să nu rămână fără efect
      case 'Escape': { const plan = document.querySelector<SVGSVGElement>('svg.plan'); if (plan) plan.focus(); else (e.target as HTMLElement).blur(); } break;
      default: done = false; }
    // tastele arborelui nu ajung la ascultătorii planului; dispecerul editorului ignoră oricum tastele din .outliner
    if (done){ e.preventDefault(); e.stopPropagation(); } };
  const empty = !nodes.some(n => n.children.length);
  return (<section className="outliner" aria-labelledby="ol-title">
    <header className="ol-head"><h2 id="ol-title">{t('outliner.title')}</h2>
      {onHide && <button type="button" className="btn ghost icon-btn sm" aria-label={t('outliner.hide')} title={t('outliner.hide')} onClick={onHide}><Icon name="chevronLeft" size={16} /></button>}</header>
    <p className="sr-only" id="ol-keys">{t('outliner.keys')}</p>
    {empty && <p className="ol-empty muted">{t('outliner.empty')}</p>}
    <ul ref={box} role="tree" className="ol-tree" aria-label={t('outliner.aria')} aria-describedby="ol-keys" onKeyDown={onKey}>
      {rows.map(r => { const n = r.node, kids = n.children.length > 0, isOpen = open.has(n.key), on = n.key === path?.key;
        const pid = n.item?.kind === 'placement' ? n.item.id : null;
        return <li key={n.key} data-key={n.key} role="treeitem" tabIndex={n.key === active ? 0 : -1} aria-level={r.depth + 1} aria-posinset={r.index} aria-setsize={r.setSize}
          aria-expanded={kids ? isOpen : undefined} aria-selected={n.item ? on : undefined} aria-current={!n.item && r.depth === 0 && n.level === level ? 'true' : undefined}
          className={`ol-row ${on ? 'is-sel' : ''} ${n.item ? '' : 'is-head'}`} style={{ '--d': r.depth } as React.CSSProperties} title={n.desc ?? n.label} aria-label={n.desc}
          onFocus={e => { if (e.target === e.currentTarget) setCur(n.key); }} onClick={() => { setCur(n.key); choose(n, false); }} onDoubleClick={() => n.item && onPick(n.level, n.item, true)}
          onMouseEnter={pid ? () => onHover(pid) : undefined} onMouseLeave={pid ? () => onHover(null) : undefined}>
          <span className="ol-twist" aria-hidden="true" onClick={kids ? e => { e.stopPropagation(); toggle(n.key); } : undefined}>{kids && <Icon name={isOpen ? 'chevronDown' : 'chevronRight'} size={14} />}</span>
          {n.item ? <Glyph kind={n.item.kind} /> : null}<span className="ol-label">{n.label}</span></li>; })}
    </ul>
  </section>);
}
