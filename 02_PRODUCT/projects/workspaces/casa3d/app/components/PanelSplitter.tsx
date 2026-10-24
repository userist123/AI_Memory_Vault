'use client';
// Separatorul unui panou andocat (PLAN_A1_3), după modelul APG „window splitter”: tragere cu pointerul, săgeți ±16 px
// (Shift ±64), Home/End = minim/maxim, dublu clic = lățimea implicită. Lățimea se raportează în timpul tragerii
// (onChange) și se salvează o singură dată, la eliberare (onCommit).
import { useRef } from 'react';
import { LIMITS, clampWidth, stepWidth } from '@/lib/workspace-layout';

export default function PanelSplitter({ side, width, label, onChange, onCommit }: {
  side: 'left' | 'right'; width: number; label: string; onChange(px: number): void; onCommit(px: number): void }){
  const drag = useRef<{ x: number; w: number; last: number } | null>(null), lim = LIMITS[side];
  const at = (clientX: number) => { const d = drag.current!; return clampWidth(side, side === 'left' ? d.w + (clientX - d.x) : d.w - (clientX - d.x)); };
  return <div role="separator" aria-orientation="vertical" aria-label={label} aria-valuenow={width} aria-valuemin={lim.min} aria-valuemax={lim.max}
    tabIndex={0} className={`splitter splitter-${side}`}
    onPointerDown={e => { if (e.button !== 0) return; e.preventDefault(); e.currentTarget.setPointerCapture(e.pointerId); drag.current = { x: e.clientX, w: width, last: width }; }}
    onPointerMove={e => { if (!drag.current) return; const w = at(e.clientX); if (w !== drag.current.last){ drag.current.last = w; onChange(w); } }}
    onPointerUp={e => { const d = drag.current; if (!d) return; drag.current = null; e.currentTarget.releasePointerCapture(e.pointerId); if (d.last !== d.w) onCommit(d.last); }}
    onPointerCancel={() => { const d = drag.current; drag.current = null; if (d && d.last !== d.w) onCommit(d.last); }}
    onDoubleClick={() => onCommit(lim.def)}
    onKeyDown={e => { const w = stepWidth(side, width, e.key, e.shiftKey); if (w == null) return; e.preventDefault(); e.stopPropagation(); if (w !== width) onCommit(w); }} />;
}
