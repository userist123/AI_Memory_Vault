'use client';
import { useEffect, useRef, useState } from 'react';
import Joystick from './Joystick';
import Icon from './Icon';
import { usePrefs } from '@/lib/prefs';
import PanoramaView from './PanoramaView';
import type { Catalog, Snapshot, MaterialsCatalog } from '@/core/types';
import { viewerInput, type PlateVoid } from '@/lib/viewer-input';
const NO_VOIDS: PlateVoid[] = [];
import { toEngineCatalog } from '@/core/catalog';
import { lighting, sunDirection, captureFileName, type TimeOfDay } from '@/core/lighting';

export default function Viewer3D({ snap, catalog, mc = null, onPick, voids = NO_VOIDS, projectId }: { snap: Snapshot; catalog: Catalog; mc?: MaterialsCatalog | null; onPick(id: string | null): void; voids?: PlateVoid[]; projectId?: string }){
  const { t, lang } = usePrefs();
  const [room, setRoom] = useState<string | null>(null), [pano, setPano] = useState<{ url: string; title: string; file: string } | null>(null), [panoBusy, setPanoBusy] = useState(false), [panoMsg, setPanoMsg] = useState('');
  const ref = useRef<HTMLCanvasElement>(null), v = useRef<any>(null), pickRef = useRef(onPick);
  const [mode, setMode] = useState<'house' | 'walk'>('house'), [err, setErr] = useState(''), [touch, setTouch] = useState(false);
  const [time, setTime] = useState<TimeOfDay>('day'), [azimuth, setAzimuth] = useState(135), [lightOpen, setLightOpen] = useState(false), [exportOpen, setExportOpen] = useState(false);
  // calitate înaltă (ocluzie ambientală) implicit pe desktop; pe telefon rămâne normală, pentru fluiditate și baterie
  const [quality, setQuality] = useState<'normal' | 'high'>('normal'), qRef = useRef(quality); qRef.current = quality;
  useEffect(() => { const t = typeof window !== 'undefined' && (matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0); setTouch(t); if (!t) setQuality('high'); }, []);
  pickRef.current = onPick;
  const [doorsOpen, setDoorsOpen] = useState(true);
  useEffect(() => { let alive = true;
    import('./viewer3d-engine.js').then(m => { if (!alive || !ref.current) return; try { v.current = m.createViewer(ref.current, { onPick: (id: string | null) => pickRef.current(id), onContextLost: () => setErr('context-lost'), onContextRestored: () => { setErr(''); applyLight(); v.current?.setQuality?.(qRef.current); push(); } }); if (process.env.NODE_ENV !== 'production') (window as any).__casa3dViewer = v.current; applyLight(); v.current.setQuality?.(qRef.current); push(); } catch (e){ console.error('3D', e); setErr('webgl'); } });
    return () => { alive = false; v.current?.dispose(); v.current = null; }; }, []); // eslint-disable-line
  const engineCat = useRef<ReturnType<typeof toEngineCatalog> | null>(null);
  function push(){ if (!v.current) return; engineCat.current ||= toEngineCatalog(catalog);
    const { plan, items } = viewerInput(snap, catalog, engineCat.current, { voids, mc }); v.current.setState(plan, items); }
  useEffect(push, [snap, mc]); // eslint-disable-line
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
  // scena pentru randarea fotorealistă în Blender (tools/blender/casa3d_render.py): aceleași forme, materiale și lumină
  async function exportBlender(){ const p = lighting(time, azimuth), sc = v.current?.exportScene?.({ name: snap.name, time, azimuthDeg: p.azimuthDeg, level: snap.floor.name, lang }); if (!sc) return;
    // pe calculatorul cu Blender: randarea pornește direct, imaginile apar în etapa Prezentare; altfel se descarcă scena
    if (projectId){ try { const r = await fetch(`/api/projects/${projectId}/renders`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(sc) });
        if (r.status === 202){ setPanoMsg(t('render.started')); return; } if (r.status !== 501){ const j = await r.json().catch(() => ({})); setPanoMsg(j.error || t('render.failed')); return; } } catch { /* fără server: descărcăm */ } }
    const a = document.createElement('a'), url = URL.createObjectURL(new Blob([JSON.stringify(sc)], { type: 'application/json' }));
    a.href = url; a.download = captureFileName(`${snap.name}-blender`).replace(/\.png$/, '.casa3d.json'); document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
  async function exportGlb(){ try { const buf: ArrayBuffer | null = await v.current?.exportGLB?.(); if (!buf) return;
      const a = document.createElement('a'), url = URL.createObjectURL(new Blob([buf], { type: 'model/gltf-binary' })); a.href = url; a.download = captureFileName(snap.name).replace(/\.png$/, '.glb');
      document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
    catch (e){ setPanoMsg(t('viewer.glbFailed')); } }
  function capture(){ const url: string | undefined = v.current?.capture(); if (!url) return;
    const a = document.createElement('a'); a.href = url; a.download = captureFileName(snap.name); document.body.appendChild(a); a.click(); a.remove(); }
  // Joystick-ul dispare când ieși din tur: orice mișcare rămasă e anulată, altfel jucătorul ar aluneca la următorul tur.
  useEffect(() => { if (mode !== 'walk') v.current?.setMove(0, 0); }, [mode]);
  const go = (m: 'house' | 'walk') => { setMode(m); v.current?.setMode(m); };
  return (<div className="view3d">
    <canvas ref={ref} tabIndex={0} aria-label={t('viewer.aria')} />
    {err === 'context-lost' && <div className="pending" role="alert"><span style={{ marginRight: 8 }}>{t('viewer.noWebgl')}</span><button className="btn" onClick={() => { setErr(''); applyLight(); push(); }}>{t('editor.revert')}</button></div>}
    {mode === 'walk' && touch && !err && <Joystick onMove={(f, s) => v.current?.setMove(f, s)} />}
    <div className="v3bar">
      <button className="btn" aria-pressed={mode === 'house'} onClick={() => go('house')}><Icon name="cube" size={16} /><span className="v3lbl">{t('viewer.house')}</span></button>
      {/* intrarea în camere: o listă, nu câte un buton pe cameră (bara încape pe un rând și pe laptop) */}
      <select className="btn" aria-label={t('viewer.tourPick')} value={mode === 'walk' && room ? room : ''} onChange={e => { const id = e.target.value; if (!id) { go('house'); return; } setMode('walk'); setRoom(id); v.current?.goRoom(id); }}>
        <option value="">{t('viewer.tourPick')}</option>
        {snap.floor.rooms.map(r => <option key={r.id} value={r.id}>{t('viewer.tour', { room: r.name })}</option>)}
      </select>
      {mode === 'walk' && room && <button className="btn" disabled={panoBusy || !!err} title={t('pano.title')} onClick={openPano}>360°</button>}
      {panoMsg && <span className="muted" role="status">{panoMsg}</span>}
      <button className="btn" aria-pressed={doorsOpen} title={t('viewer.doors')} aria-label={t('viewer.doorsOpen')} onClick={() => { const n = !doorsOpen; setDoorsOpen(n); v.current?.setDoors(n); }}><Icon name="door" size={16} /><span className="v3lbl" aria-hidden="true">{doorsOpen ? t('viewer.doorsOpen') : t('viewer.doorsClosed')}</span></button>
      <button className="btn" aria-expanded={lightOpen} onClick={() => setLightOpen(o => !o)} title={t('viewer.lightTitle')} aria-label={t('viewer.lightTitle')}><Icon name="sun" size={16} /><span className="v3lbl" aria-hidden="true">{t('viewer.light')}</span></button>
      <div className="v3exp">
        <button className="btn" aria-expanded={exportOpen} aria-haspopup="menu" disabled={!!err} onClick={() => setExportOpen(o => !o)} title={t('viewer.exportTitle')} aria-label={t('viewer.exportTitle')}><Icon name="download" size={16} /><span className="v3lbl" aria-hidden="true">{t('viewer.export')}</span><Icon name={exportOpen ? 'chevronUp' : 'chevronDown'} size={14} /></button>
        {exportOpen && <div className="v3menu" role="menu" onKeyDown={e => { if (e.key === 'Escape') setExportOpen(false); }}>
          <button role="menuitem" onClick={() => { setExportOpen(false); capture(); }} title={t('viewer.captureTitle')}>{t('viewer.capture')}</button>
          <button role="menuitem" onClick={() => { setExportOpen(false); exportBlender(); }} title={t('viewer.blenderTitle')}>{t('viewer.blender')}</button>
          <button role="menuitem" onClick={() => { setExportOpen(false); exportGlb(); }} title={t('viewer.glbTitle')}>{t('viewer.glb')}</button>
        </div>}
      </div>
    </div>
    {lightOpen && <div className="v3light" role="group" aria-label={t('viewer.lighting')}>
      {(['day', 'evening', 'night'] as const).map(tod => <button key={tod} className="btn" aria-pressed={time === tod} onClick={() => setTime(tod)}>{t(tod === 'day' ? 'viewer.day' : tod === 'evening' ? 'viewer.evening' : 'viewer.night')}</button>)}
      <button className="btn" aria-pressed={quality === 'high'} onClick={() => setQuality(q => q === 'high' ? 'normal' : 'high')} title={t('viewer.qualityTitle')}>{t('viewer.quality')}</button>
      <label className="f" style={{ minWidth: 160 }}><span>{t('viewer.sun', { deg: Math.round(azimuth) })}</span><input type="range" min={0} max={359} value={azimuth} onChange={e => setAzimuth(Number(e.target.value))} /></label>
    </div>}
    <div className="hintbar">{err ? t('viewer.noWebgl') : (mode === 'walk' ? (touch ? t('viewer.hintWalkTouch') : t('viewer.hintWalk')) : (touch ? t('viewer.hintHouseTouch') : t('viewer.hintHouse')))}</div>
    {pano && <PanoramaView url={pano.url} title={pano.title} fileName={pano.file} onClose={() => setPano(null)} />}
  </div>);
}
