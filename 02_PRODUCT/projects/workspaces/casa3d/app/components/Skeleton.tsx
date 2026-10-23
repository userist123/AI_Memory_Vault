'use client';
// Loading skeletons shaped like the real layouts (editor, print package, presentation, documents, lists), instead of a
// plain "Loading…" line. One live status with the loading text for screen readers; the shapes are hidden from them.
// The shimmer stops under prefers-reduced-motion (globals.css, .skel).
import type { ReactNode } from 'react';

const B = ({ w, h, r, className = '' }: { w?: number | string; h?: number | string; r?: number; className?: string }) =>
  <span className={`skel ${className}`} style={{ width: w, height: h, borderRadius: r }} />;
const Live = ({ label }: { label: string }) => <span className="sr-only">{label}</span>;
function Frame({ label, className, children }: { label: string; className: string; children: ReactNode }){
  return (<div className={`skel-frame ${className}`} role="status" aria-live="polite" aria-busy="true"><Live label={label} /><div aria-hidden="true" className="skel-inner">{children}</div></div>);
}

/** Editor: top bar, stage bar, tools column, plan canvas and the side panel (bottom sheet under 1024 px). */
export function EditorSkeleton({ label }: { label: string }){
  return (<Frame label={label} className="skel-editor">
    <div className="skel-top"><B w={96} h={20} /><B w={180} h={22} /><B w={110} h={28} r={999} /><span className="skel-grow" /><B w={88} h={32} className="skel-hide-s" /><B w={150} h={32} className="skel-hide-s" /><B w={132} h={36} /></div>
    <div className="skel-stages">{Array.from({ length: 10 }, (_, i) => <B key={i} w={i === 0 ? 92 : 78} h={14} />)}</div>
    <div className="skel-body">
      <div className="skel-tools">{Array.from({ length: 6 }, (_, i) => <B key={i} w={36} h={36} />)}</div>
      <div className="skel-canvas"><div className="skel-plan"><span /><span /><span /><span /><span /></div></div>
      <div className="skel-side"><B w="55%" h={26} /><B w="80%" h={12} /><B w="100%" h={1} /><B w="40%" h={12} /><B w="100%" h={36} /><B w="40%" h={12} /><B w="100%" h={36} /><B w="100%" h={36} /><B w="70%" h={12} /><B w="100%" h={88} /></div>
    </div>
  </Frame>);
}

/** Print package: header with the print button, cover with key figures, then A4 landscape sheets. */
export function PrintSkeleton({ label }: { label: string }){
  return (<Frame label={label} className="skel-print">
    <div className="skel-phead"><div><B w={260} h={26} /><B w={200} h={12} /></div><div className="skel-pright"><B w={120} h={14} /><B w={180} h={36} /></div></div>
    <div className="skel-cover"><B className="skel-hero" /><div className="skel-figs">{Array.from({ length: 6 }, (_, i) => <B key={i} w={i % 3 === 0 ? '50%' : '90%'} h={12} />)}<B w="100%" h={120} /></div></div>
    {[0, 1].map(i => <B key={i} className="skel-sheet" />)}
  </Frame>);
}

/** Presentation / client link: a 16:9 slide and the thumbnail strip. */
export function DeckSkeleton({ label }: { label: string }){
  return (<Frame label={label} className="skel-deck"><B className="skel-slide" /><div className="skel-thumbs">{Array.from({ length: 5 }, (_, i) => <B key={i} className="skel-thumb" />)}</div></Frame>);
}

/** A4 document pages (contract, proforma, handover, site report, mood board). */
export function DocSkeleton({ label }: { label: string }){
  return (<Frame label={label} className="skel-doc"><div className="skel-a4"><B w="45%" h={24} /><B w="30%" h={12} /><B w="100%" h={1} />
    {Array.from({ length: 9 }, (_, i) => <B key={i} w={`${[100, 92, 96, 70, 100, 88, 94, 60, 80][i]}%`} h={12} />)}<B w="100%" h={140} /></div></Frame>);
}

/** A list or a panel block (orders, site snags, commercial, contract panels). */
export function ListSkeleton({ label, rows = 4 }: { label: string; rows?: number }){
  return (<Frame label={label} className="skel-list">{Array.from({ length: rows }, (_, i) => <div key={i} className="skel-row"><B w={40} h={40} /><span><B w="60%" h={12} /><B w="35%" h={10} /></span><B w={64} h={28} /></div>)}</Frame>);
}

/** Project cards on the home page: plan thumbnail, status tag, name and date. */
export function CardsSkeleton({ label, count = 3 }: { label: string; count?: number }){
  return (<Frame label={label} className="skel-cards"><div className="pgrid">{Array.from({ length: count }, (_, i) => <div key={i} className="skel-card"><B className="skel-thumb-card" /><B w={84} h={16} /><B w="70%" h={16} /><B w="50%" h={11} /></div>)}</div></Frame>);
}
