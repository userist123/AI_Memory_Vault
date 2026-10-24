'use client';
import type { ReactNode } from 'react';

/** Stare goală unitară: titlu, text opțional și o acțiune opțională (buton sau link). Fără pictogramă, în stilul editorial. */
export type EmptyAction = { label: string; onClick?: () => void; href?: string };
export default function EmptyState({ title, text, action, className = '' }: { title: ReactNode; text?: ReactNode; action?: EmptyAction; className?: string }){
  return (<div className={`empty-state ${className}`} role="status">
    <b className="es-title">{title}</b>
    {text && <p className="es-text">{text}</p>}
    {action && (action.href ? <a className="btn" href={action.href}>{action.label}</a> : <button type="button" className="btn" onClick={action.onClick}>{action.label}</button>)}
  </div>);
}
