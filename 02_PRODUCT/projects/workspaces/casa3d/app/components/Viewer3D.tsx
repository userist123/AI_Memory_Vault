'use client';
import { useEffect, useRef, useState } from 'react';
import Joystick from './Joystick';
import { usePrefs } from '@/lib/prefs';
import PanoramaView from './PanoramaView';
import type { Catalog, Snapshot } from '@/core/types';
import { viewerInput } from '@/lib/viewer-input';
import { toEngineCatalog } from '@/core/catalog';
import { lighting, sunDirection, captureFileName, type TimeOfDay } from '@/core/lighting';

export default function Viewer3D({ snap, catalog, onPick }: { snap: Snapshot; catalog: Catalog; onPick(id: string | null): void }){
  const { t } = usePrefs();
  const [room, setRoom] = useState<string | null>(null), [pano, setPano] = useState<{ url: string; title: string; file: string } | null>(null), [panoBusy, setPanoBusy] = useState(false), [panoMsg, setPanoMsg] = useState('');
  const ref = useRef<HTMLCanvasElement>(null), v = useRef<any>(null), pickRef = useRef(onPick);
  const [mode, setMode] = useState<'house' | 'walk'>('house'), [err, setErr] = useState(''), [touch, setTouch] = useState(false);
  const [time, setTime] = useState<TimeOfDay>('day'), [azimuth, setAzimuth] = useState(135), [lightOpen, setLightOpen] = useState(false);
  // calitate înaltă (ocluzie ambientală) implicit pe desktop; pe telefon rămâne normală, pentru fluiditate și baterie
  const [quality, setQuality] = useState<'normal' | 'high'>('normal'), qRef = useRef(quality); qRef.current = quality;
  useEffect(() => { const t = typeof window !== 'undefined' && (matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0); setTouch(t); if (!t) setQuality('high'); }, []);
  pickRef.current = onPick;
  useEffect(() => { let alive = true;
    import('./viewer3d-engine.js').then(m => { if (!alive || !ref.current) return; try { v.current = m.createViewer(ref.current, { onPick: (id: string | null) => pickRef.current(id) }); applyLight(); v.current.setQuality?.(qRef.current); push(); } catch { setErr('webgl'); } });
    return () => { alive = false; v.current?.dispose(); v.current = null; }; }, []); // eslint-disable-line
  const engineCat = useRef<ReturnType<typeof toEngineCatalog> | null>(null);
  function push(){ if (!v.current) return; engineCat.current ||= toEngineCatalog(catalog);
    const { plan, items } = viewerInput(snap, catalog, engineCat.current); v.current.setState(plan, items); }
  useEffect(push, [snap]); // eslint-disable-line
  function applyLight(){ const p = lighting(time, azimuth); v.current?.setLighting({ ...p, dir: sunDirection(p.azimuthDeg, p.elevationDeg) }); }
  useEffect(applyLight, [time, azimuth]); // eslint-disable-line
  useEffect(() => { const q = v.current?.setQuality?.(quality); if (q && q !== quality) setQuality(q); }, [quality]); // eslint-disable-line
  // panoramă 360° a camerei curente, randată din mijlocul ei; se deschide în vizualizator și se poate descărca
  function openPano(){ if (!room || !v.current?.renderPanorama) return; setPanoBusy(true); setPanoMsg('');
    setTimeout(() => { try { const rn = snap.floor.rooms.find(r => r.id === room)?.name;
        // camera aleasă în tur poate fi ștearsă între timp: spunem, nu tăcem
        const url = rn ? v.current.renderPanorama(room, 2048) : null;
        if (url && rn) setPano({ url, title: t('pano.of', { room: rn }), file: captureFileName(`${snap.name}-${rn}-360`).replace(/\.png$/, '.jpg') });
        else setPanoMsg(t('pano.noRoom')); }
      catch (e){ setPanoMsg(t('pano.failed', { msg: e instanceof Error ? e.message : String(e) })); }
      finally { setPanoBusy(false); } }, 30); }
  function capture(){ const url: string | undefined = v.current?.capture(); if (!url) return;
    const a = document.createElement('a'); a.href = url; a.download = captureFileName(snap.name); document.body.appendChild(a); a.click(); a.remove(); }
  // Joystick-ul dispare când ieși din tur: orice mișcare rămasă e anulată, altfel jucătorul ar aluneca la următorul tur.
  useEffect(() => { if (mode !== 'walk') v.current?.setMove(0, 0); }, [mode]);
  const go = (m: 'house' | 'walk') => { setMode(m); v.current?.setMode(m); };
  return (<div className="view3d">
    <canvas ref={ref} aria-label={t('viewer.aria')} />
    {mode === 'walk' && touch && !err && <Joystick onMove={(f, s) => v.current?.setMove(f, s)} />}
    <div className="v3bar">
      <button className="btn" aria-pressed={mode === 'house'} onClick={() => go('house')}>{t('viewer.house')}</button>
      {snap.floor.rooms.map(r => <button key={r.id} className="btn" onClick={() => { setMode('walk'); setRoom(r.id); v.current?.goRoom(r.id); }}>{t('viewer.tour', { room: r.name })}</button>)}
      {mode === 'walk' && room && <button className="btn" disabled={panoBusy || !!err} title={t('pano.title')} onClick={openPano}>360°</button>}
      {panoMsg && <span className="muted" role="status">{panoMsg}</span>}
      <button className="btn" aria-expanded={lightOpen} onClick={() => setLightOpen(o => !o)} title={t('viewer.lightTitle')}>{t('viewer.light')}</button>
      <button className="btn" onClick={capture} disabled={!!err} title={t('viewer.captureTitle')}>{t('viewer.capture')}</button>
    </div>
    {lightOpen && <div className="v3light" role="group" aria-label={t('viewer.lighting')}>
      {(['day', 'evening', 'night'] as const).map(tod => <button key={tod} className="btn" aria-pressed={time === tod} onClick={() => setTime(tod)}>{t(tod === 'day' ? 'viewer.day' : tod === 'evening' ? 'viewer.evening' : 'viewer.night')}</button>)}
      <button className="btn" aria-pressed={quality === 'high'} onClick={() => setQuality(q => q === 'high' ? 'normal' : 'high')} title={t('viewer.qualityTitle')}>{t('viewer.quality')}</button>
      <label className="f" style={{ minWidth: 160 }}><span>{t('viewer.sun', { deg: Math.round(azimuth) })}</span><input type="range" min={0} max={359} value={azimuth} onChange={e => setAzimuth(Number(e.target.value))} /></label>
    </div>}
    <div className="hintbar">{err ? t('viewer.noWebgl') : (mode === 'walk' ? (touch ? t('viewer.hintWalkTouch') : t('viewer.hintWalk')) : t('viewer.hintHouse'))}</div>
    {pano && <PanoramaView url={pano.url} title={pano.title} fileName={pano.file} onClose={() => setPano(null)} />}
  </div>);
}
