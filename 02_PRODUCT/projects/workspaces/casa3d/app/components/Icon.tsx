// Setul de pictograme al aplicației (audit UI §icon set, F-7, F-29): SVG inline 24×24, contur 1,75, culoarea textului,
// ascunse cititoarelor de ecran (eticheta stă pe buton). Înlocuiesc glifele text (↶ ↷ ← ▾ ⚠ ×) și emoji.
// Traseele sunt desenate după Lucide (licență ISC), doar geometria.
export const ICONS = {
  back: 'M15 18l-6-6 6-6',
  undo: 'M9 14L4 9l5-5M4 9h10.5a5.5 5.5 0 0 1 0 11H11',
  redo: 'M15 14l5-5-5-5M20 9H9.5a5.5 5.5 0 0 0 0 11H13',
  check: 'M20 6L9 17l-5-5',
  cloudCheck: 'M17.5 19H8a6 6 0 1 1 5.7-7.9A4.5 4.5 0 1 1 17.5 19zM9.5 14l2 2 4-4',
  cloudOff: 'M2 2l20 20M5.8 8.6A6 6 0 0 0 8 19h9.5c.6 0 1.1-.1 1.6-.3M21.8 15.7A4.5 4.5 0 0 0 17.5 11h-1.8A6 6 0 0 0 10.8 6.3',
  spinner: 'M21 12a9 9 0 1 1-6.2-8.6',
  alert: 'M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0zM12 9v4M12 17h.01',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 16v-4M12 8h.01',
  close: 'M18 6L6 18M6 6l12 12',
  plus: 'M12 5v14M5 12h14',
  chevronDown: 'M6 9l6 6 6-6',
  chevronUp: 'M18 15l-6-6-6 6',
  chevronLeft: 'M15 18l-6-6 6-6',
  chevronRight: 'M9 18l6-6-6-6',
  more: 'M12 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM19 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM5 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2z',
  plan: 'M3 3h18v18H3zM3 12h9M12 3v18',
  cube: 'M21 16V8a2 2 0 0 0-1-1.7l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.7l7 4a2 2 0 0 0 2 0l7-4a2 2 0 0 0 1-1.7zM3.3 7L12 12l8.7-5M12 22V12',
  panel: 'M3 3h18v18H3zM3 15h18',
  split: 'M3 3h18v18H3zM12 3v18',
  door: 'M5 21V4h9v17M5 21h14M12 12h.01',
  sun: 'M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  walk: 'M13 4a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM9 22l2.5-7 2.5 2v5M7 12l2-4 4 1 3 3M11.5 15L10 9',
  download: 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3',
  trash: 'M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6',
  copy: 'M9 9h11v11H9zM5 15H4V4h11v1',
  rotate: 'M21 12a9 9 0 1 1-3-6.7L21 8M21 3v5h-5',
  link: 'M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7',
  print: 'M6 9V2h12v7M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 14h12v8H6z',
  grip: 'M8 9h8M8 15h8',
  search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16zM21 21l-4.3-4.3',
  arrowRight: 'M5 12h14M13 6l6 6-6 6',
} as const;
export type IconName = keyof typeof ICONS;

export default function Icon({ name, size = 18, className = '' }: { name: IconName; size?: number; className?: string }){
  return (<svg className={`ico ${className}`} width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" focusable="false"
    fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round"><path d={ICONS[name]} /></svg>);
}
