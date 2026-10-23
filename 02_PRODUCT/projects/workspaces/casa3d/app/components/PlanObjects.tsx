'use client';
// Lista obiectelor planului pentru tastatură și cititor de ecran (UI/UX P3-16). Stă înaintea planului în ordinea Tab:
// ascunsă vizual până primește focus, apoi apare ca panou peste plan. Enter alege obiectul (selecție + încadrare),
// Escape duce focusul înapoi în plan. Descrierile vin din core/plan-describe.ts, în limba interfeței.
import { useRef } from 'react';
import { usePrefs } from '@/lib/prefs';
import type { MirrorGroup, MirrorItem } from '@/core/plan-describe';

export default function PlanObjects({ groups, selected, onPick }: { groups: readonly MirrorGroup[]; selected: { kind: string; id: string } | null; onPick(i: MirrorItem): void }){
  const { t } = usePrefs(), box = useRef<HTMLElement>(null);
  const toPlan = () => box.current?.parentElement?.querySelector<SVGSVGElement>('svg.plan')?.focus();
  return (<nav ref={box} className="plan-mirror" aria-label={t('mirror.aria')} onKeyDown={e => { if (e.key === 'Escape'){ e.preventDefault(); e.stopPropagation(); toPlan(); } }}>
    <h2>{t('mirror.title')}</h2><p className="muted">{t('mirror.hint')}</p>
    {groups.map(g => <section key={g.id} aria-labelledby={`pm-${g.id}`}><h3 id={`pm-${g.id}`}>{g.title}</h3>
      <ul>{g.items.map(i => { const on = !!selected && selected.kind === i.kind && selected.id === i.id;
        return <li key={`${i.kind}:${i.id}`}><button type="button" aria-current={on ? 'true' : undefined} onClick={() => onPick(i)}>{i.label}</button></li>; })}</ul></section>)}
  </nav>);
}
