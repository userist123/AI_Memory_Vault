'use client';
// Selectorul compact de etape (UI/UX P2-9, după Vectorworks 2026): pe ecranele de laptop (1024–1499 px), unde bara cu
// 10 etape nu încape, un singur buton „03 Finisaje · 4 din 10 gata” deschide lista tuturor etapelor, pe faze.
// Bara completă rămâne pe ecranele late; pe telefon și tabletă rămâne comportamentul de până acum.
import { useEffect, useRef, useState } from 'react';
import Icon from './Icon';
import { usePrefs } from '@/lib/prefs';

type Stage = { id: string };
export default function StagePicker({ stages, phases, current, done, warn, onPick }: { stages: readonly Stage[]; phases: readonly { id: string; stages: readonly string[] }[];
  current: string; done: Record<string, boolean> | null; warn: number; onPick(id: string): void }){
  const { t } = usePrefs(), [open, setOpen] = useState(false), box = useRef<HTMLDivElement>(null);
  const num = (id: string) => String(stages.findIndex(s => s.id === id) + 1).padStart(2, '0'), n = stages.filter(s => done?.[s.id]).length;
  // închidere la clic în afară și la Escape (focusul revine pe buton); la deschidere focusul merge pe etapa curentă
  useEffect(() => { if (!open) return; const out = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    box.current?.querySelector<HTMLElement>('[aria-checked=true]')?.focus(); addEventListener('pointerdown', out); return () => removeEventListener('pointerdown', out); }, [open]);
  const pick = (id: string) => { setOpen(false); onPick(id); box.current?.querySelector<HTMLElement>('.stagepick-btn')?.focus(); };
  return (<div className="stagepick" ref={box} onKeyDown={e => { if (e.key === 'Escape' && open){ e.stopPropagation(); setOpen(false); box.current?.querySelector<HTMLElement>('.stagepick-btn')?.focus(); } }}>
    <button type="button" className="stagepick-btn" aria-haspopup="menu" aria-expanded={open} aria-label={t('stage.pickAria', { stage: `${num(current)} ${t(`stage.${current}`)}` })} onClick={() => setOpen(o => !o)}>
      <i>{num(current)}</i><b>{t(`stage.${current}`)}</b><small>{t('stage.progress', { n, total: stages.length })}</small><Icon name="chevronDown" size={16} />
    </button>
    {open && <div className="stagepick-menu" role="menu" aria-label={t('stage.aria')}>
      {phases.map(ph => <div key={ph.id} role="group" aria-label={t(`phase.${ph.id}`)} className="stagepick-phase"><span aria-hidden="true">{t(`phase.${ph.id}`)}</span>
        {ph.stages.map(id => <button key={id} type="button" role="menuitemradio" aria-checked={id === current} onClick={() => pick(id)} title={t(`stage.why.${id}`)}>
          <i>{num(id)}</i><b>{t(`stage.${id}`)}</b>{id === 'docs' && warn > 0 && <span className="count">{warn}</span>}<em role="img" className={done?.[id] ? 'ok' : ''} aria-label={done?.[id] ? t('stage.doneAria') : t('stage.todoAria')} /></button>)}
      </div>)}
    </div>}
  </div>);
}
