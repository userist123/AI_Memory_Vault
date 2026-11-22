'use client';
// Panoul de straturi al planului 2D (core/layers.ts): butonul „Straturi” din bara nivelurilor deschide o listă de bife,
// una pe categorie, plus „Arată tot”. Escape sau clicul în afară închide; la închiderea de la tastatură focusul revine pe
// buton. Straturile ascunse de etapă (obiectele de pe perete la Releveu) apar cu bifa blocată și cu explicația alături.
import { useEffect, useId, useRef } from 'react';
import { LAYER_IDS, stageAllows, hiddenLayers, type LayerId, type LayerState } from '@/core/layers';
import { usePrefs } from '@/lib/prefs';

export default function LayersPanel({ layers, stage, open, onOpen, onToggle, onShowAll }: { layers: LayerState; stage: string; open: boolean; onOpen(open: boolean): void; onToggle(id: LayerId): void; onShowAll(): void }){
  const { t } = usePrefs(), box = useRef<HTMLDivElement>(null), btn = useRef<HTMLButtonElement>(null), pop = useRef<HTMLDivElement>(null), uid = useId().replace(/:/g, '');
  const hidden = hiddenLayers(layers).length;
  // la deschidere focusul intră în panou (și când vine din meniul Vizualizare sau din paleta Ctrl+K)
  useEffect(() => { if (!open) return; const f = requestAnimationFrame(() => pop.current?.querySelector<HTMLInputElement>('input:not(:disabled)')?.focus());
    const out = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) onOpen(false); };
    window.addEventListener('pointerdown', out); return () => { cancelAnimationFrame(f); window.removeEventListener('pointerdown', out); }; }, [open]); // eslint-disable-line react-hooks/exhaustive-deps
  const close = () => { onOpen(false); btn.current?.focus(); };
  return (<div className="layers" ref={box} onKeyDown={e => { if (e.key === 'Escape' && open){ e.preventDefault(); e.stopPropagation(); close(); } }}>
    <button type="button" ref={btn} className="btn layers-btn" aria-expanded={open} aria-controls={open ? `layers-${uid}` : undefined} title={hidden ? t('layers.hiddenCount', { n: hidden }) : t('layers.aria')} onClick={() => onOpen(!open)}>
      {t('layers.title')}{hidden > 0 && <span className="layers-badge" aria-hidden="true">{hidden}</span>}{hidden > 0 && <span className="sr-only"> ({t('layers.hiddenCount', { n: hidden })})</span>}</button>
    {open && <div className="layers-pop" id={`layers-${uid}`} ref={pop} role="group" aria-label={t('layers.aria')}>
      {LAYER_IDS.map(id => { const stageOn = stageAllows(id, stage);
        return (<label key={id} className={stageOn ? undefined : 'is-off'}><input type="checkbox" checked={stageOn && layers[id]} disabled={!stageOn} onChange={() => onToggle(id)} />
          <span>{t(`layers.${id}`)}</span>{!stageOn && <small>{t('layers.stageOff')}</small>}</label>); })}
      <div className="layers-foot"><button type="button" className="btn" disabled={!hidden} onClick={onShowAll}>{t('layers.showAll')}</button>
        <button type="button" className="btn ghost" onClick={close}>{t('layers.close')}</button></div>
    </div>}
  </div>);
}
