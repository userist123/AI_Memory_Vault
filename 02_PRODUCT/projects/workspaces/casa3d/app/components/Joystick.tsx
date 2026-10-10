'use client';
import { useEffect, useRef, useState } from 'react';
// Joystick virtual pentru tur pe telefon: un deget pe pad → axe (strafe, înainte) în [-1, 1]; eliberarea oprește mișcarea.
export default function Joystick({ onMove, size = 112 }: { onMove(forward: number, strafe: number): void; size?: number }){
  const pad = useRef<HTMLDivElement>(null), [knob, setKnob] = useState({ x: 0, y: 0 }), active = useRef<number | null>(null);
  const r = size / 2, dead = 0.12;
  const axes = (e: React.PointerEvent) => { const b = pad.current!.getBoundingClientRect(); let dx = (e.clientX - (b.left + r)) / r, dy = (e.clientY - (b.top + r)) / r;
    const m = Math.hypot(dx, dy); if (m > 1){ dx /= m; dy /= m; } const mag = Math.hypot(dx, dy) < dead ? 0 : 1;
    setKnob({ x: dx * r * 0.6, y: dy * r * 0.6 }); onMove(-dy * mag, dx * mag); };
  const stop = () => { active.current = null; setKnob({ x: 0, y: 0 }); onMove(0, 0); };
  const onMoveRef = useRef(onMove); onMoveRef.current = onMove;
  useEffect(() => () => onMoveRef.current(0, 0), []); // demontat în timpul tragerii → mișcarea se oprește
  return (<div ref={pad} role="slider" aria-label="Joystick de deplasare" aria-valuemin={-1} aria-valuemax={1} aria-valuenow={0} tabIndex={0}
    onPointerDown={e => { active.current = e.pointerId; (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId); axes(e); }}
    onPointerMove={e => { if (active.current === e.pointerId) axes(e); }} onPointerUp={stop} onPointerCancel={stop} onLostPointerCapture={stop}
    style={{ position: 'absolute', left: 16, bottom: 56, width: size, height: size, borderRadius: '50%', background: 'rgba(30,30,30,.28)', border: '1px solid rgba(255,255,255,.5)', touchAction: 'none', userSelect: 'none', zIndex: 3 }}>
    <div style={{ position: 'absolute', left: r - 22 + knob.x, top: r - 22 + knob.y, width: 44, height: 44, borderRadius: '50%', background: 'rgba(255,255,255,.85)', boxShadow: '0 2px 6px rgba(0,0,0,.3)', pointerEvents: 'none' }} />
  </div>);
}
