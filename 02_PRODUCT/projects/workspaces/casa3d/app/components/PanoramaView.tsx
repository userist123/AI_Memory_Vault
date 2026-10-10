'use client';
// Fereastra 360°: bara cu descărcare/închidere; vizualizarea propriu-zisă e în panorama-engine.js.
import { useEffect, useRef } from 'react';
import { usePrefs } from '@/lib/prefs';

export default function PanoramaView({ url, title, fileName, onClose }: { url: string; title: string; fileName: string; onClose(): void }){
  const { t } = usePrefs(), ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => { let alive = true, view: { dispose(): void } | null = null;
    import('./panorama-engine.js').then(m => { if (alive && ref.current) view = m.createPanorama(ref.current, url); });
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); }; addEventListener('keydown', esc);
    return () => { alive = false; view?.dispose(); removeEventListener('keydown', esc); };
  }, [url]); // eslint-disable-line
  return (<div className="pano" role="dialog" aria-modal="true" aria-label={title}>
    <div className="panobar"><b>{title}</b><span className="muted">{t('pano.hint')}</span>
      <a className="btn" href={url} download={fileName}>{t('pano.download')}</a><button className="btn" onClick={onClose}>{t('pano.close')}</button></div>
    <canvas ref={ref} />
  </div>);
}
