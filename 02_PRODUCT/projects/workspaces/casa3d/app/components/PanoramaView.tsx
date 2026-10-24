'use client';
// Fereastra 360°: bara cu descărcare/închidere; vizualizarea propriu-zisă e în panorama-engine.js.
import { useEffect, useRef } from 'react';
import { usePrefs } from '@/lib/prefs';

export default function PanoramaView({ url, title, fileName, onClose }: { url: string; title: string; fileName: string; onClose(): void }){
  const { t } = usePrefs(), ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => { let alive = true, view: { dispose(): void } | null = null;
    import('./panorama-engine.js').then(m => { if (alive && ref.current) view = m.createPanorama(ref.current, url); });
    // Fereastra e modală: tastele nu mai ajung la editor (Delete, R, săgeți, Ctrl+Z) sau la mersul din 3D (WASD).
    // Ascultătorul e în faza de captură pe window, deci rulează înaintea celorlalți și oprește propagarea.
    const keys = (e: KeyboardEvent) => { e.stopImmediatePropagation(); if (e.key === 'Escape') onClose(); }; addEventListener('keydown', keys, true);
    return () => { alive = false; view?.dispose(); removeEventListener('keydown', keys, true); };
  }, [url]); // eslint-disable-line
  return (<div className="pano" role="dialog" aria-modal="true" aria-label={title}>
    <div className="panobar"><b>{title}</b><span className="muted">{t('pano.hint')}</span>
      <a className="btn" href={url} download={fileName}>{t('pano.download')}</a><button className="btn" onClick={onClose}>{t('pano.close')}</button></div>
    <canvas ref={ref} />
  </div>);
}
