'use client';
// Lista obiectelor planului pentru tastatură și cititor de ecran (UI/UX P3-16). Stă înaintea planului în ordinea Tab, ca
// o singură oprire (roving tabindex): săgețile sus/jos și Home/End trec prin obiecte, Enter alege obiectul (selecție +
// încadrare), Escape duce focusul înapoi în plan. Ascunsă vizual până primește focus, apoi panou peste plan.
// Descrierile vin din core/plan-describe.ts, în limba interfeței.
import { useEffect, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import type { MirrorGroup, MirrorItem } from '@/core/plan-describe';

export default function PlanObjects({ groups, selected, onPick }: { groups: readonly MirrorGroup[]; selected: { kind: string; id: string } | null; onPick(i: MirrorItem): void }){
  const { t } = usePrefs(), box = useRef<HTMLElement>(null), [cur, setCur] = useState(0);
  const flat = groups.flatMap(g => g.items), selIx = selected ? flat.findIndex(i => i.kind === selected.kind && i.id === selected.id) : -1;
  const active = Math.min(Math.max(0, cur), Math.max(0, flat.length - 1));
  // la intrarea în listă, oprirea Tab e obiectul selectat (selecția din plan sau 3D)
  useEffect(() => { if (selIx >= 0) setCur(selIx); }, [selIx]);
  const toPlan = () => box.current?.parentElement?.querySelector<SVGSVGElement>('svg.plan')?.focus();
  const move = (ix: number) => { const n = Math.min(Math.max(0, ix), flat.length - 1); setCur(n); box.current?.querySelectorAll<HTMLButtonElement>('button[data-ix]')[n]?.focus(); };
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape'){ e.preventDefault(); e.stopPropagation(); toPlan(); return; }
    const k = e.key === 'ArrowDown' ? active + 1 : e.key === 'ArrowUp' ? active - 1 : e.key === 'Home' ? 0 : e.key === 'End' ? flat.length - 1 : null;
    if (k != null){ e.preventDefault(); e.stopPropagation(); move(k); } };
  let ix = -1;
  return (<nav ref={box} className="plan-mirror" aria-label={t('mirror.aria')} onKeyDown={onKey}>
    <h2>{t('mirror.title')}</h2><p className="muted">{t('mirror.listKeys')}</p>
    {groups.map(g => <section key={g.id} aria-labelledby={`pm-${g.id}`}><h3 id={`pm-${g.id}`}>{g.title}</h3>
      <ul>{g.items.map(i => { ix++; const me = ix, on = me === selIx;
        return <li key={`${i.kind}:${i.id}`}><button type="button" data-ix={me} tabIndex={me === active ? 0 : -1} aria-current={on ? 'true' : undefined}
          onFocus={() => setCur(me)} onClick={() => onPick(i)}>{i.label}</button></li>; })}</ul></section>)}
  </nav>);
}
