'use client';
// Fereastra 360°: bara cu descărcare/închidere; vizualizarea propriu-zisă e în panorama-engine.js. Turul virtual: punctele de
// trecere (core/pano-tour.ts) stau peste imagine, pe ușile spre camerele vecine, și urmăresc privirea; clic = camera aceea.
import { useEffect, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { screenOf, type TourHotspot } from '@/core/pano-tour';

type View = { lon: number; lat: number; fov: number; aspect: number };
export default function PanoramaView({ url, title, fileName, onClose, hotspots = [], onGo, startLon = 0 }: { url: string; title: string; fileName: string; onClose(): void; hotspots?: TourHotspot[]; onGo?(h: TourHotspot): void; startLon?: number }){
  const { t } = usePrefs(), ref = useRef<HTMLCanvasElement>(null), [view, setView] = useState<View | null>(null);
  useEffect(() => { let alive = true, pano: { dispose(): void; look(l: number, a: number): void } | null = null;
    import('./panorama-engine.js').then(m => { if (alive && ref.current){ pano = m.createPanorama(ref.current, url, (v: View) => { if (alive) setView(v); }); if (startLon) pano.look(startLon, 0); } });
    // Fereastra e modală: tastele nu mai ajung la editor (Delete, R, săgeți, Ctrl+Z) sau la mersul din 3D (WASD).
    // Ascultătorul e în faza de captură pe window, deci rulează înaintea celorlalți și oprește propagarea.
    const keys = (e: KeyboardEvent) => { if ((e.target as HTMLElement | null)?.closest?.('.pano-hot') && (e.key === 'Enter' || e.key === ' ')) return; e.stopImmediatePropagation(); if (e.key === 'Escape') onClose(); };
    addEventListener('keydown', keys, true);
    return () => { alive = false; pano?.dispose(); removeEventListener('keydown', keys, true); };
  }, [url]); // eslint-disable-line
  return (<div className="pano" role="dialog" aria-modal="true" aria-label={title}>
    <div className="panobar"><b>{title}</b><span className="muted">{hotspots.length ? t('pano.tourHint') : t('pano.hint')}</span>
      <a className="btn" href={url} download={fileName}>{t('pano.download')}</a><button className="btn" onClick={onClose}>{t('pano.close')}</button></div>
    <div className="pano-stage"><canvas ref={ref} />
      {view && onGo && hotspots.map(h => { const p = screenOf(h, view.lon, view.lat, view.fov, view.aspect); if (!p || p.x < -.05 || p.x > 1.05 || p.y < -.05 || p.y > 1.05) return null;
        return <button key={h.openingId + h.toRoomId} type="button" className="pano-hot" style={{ left: `${p.x * 100}%`, top: `${p.y * 100}%` }} aria-label={t('pano.goTo', { room: h.toName })} onClick={() => onGo(h)}><span aria-hidden="true">➜</span> {h.toName}</button>; })}
      {onGo && hotspots.length > 0 && <nav className="pano-rooms" aria-label={t('pano.roomsAria')}>{hotspots.map(h => <button key={'n' + h.openingId + h.toRoomId} type="button" className="btn sm" onClick={() => onGo(h)}>{h.toName}</button>)}</nav>}
    </div>
  </div>);
}
