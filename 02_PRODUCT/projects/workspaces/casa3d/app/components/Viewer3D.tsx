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
import { parseViews, addView, removeView, viewsKey, clampCut, type SavedView } from '@/core/saved-views';
import { askPrompt } from '@/lib/dialog';

export default function Viewer3D({ snap, catalog, mc = null, onPick, voids = NO_VOIDS, projectId, selectedId = null, hoverId = null, onHover, frame, onFrameOther }: { snap: Snapshot; catalog: Catalog; mc?: MaterialsCatalog | null; onPick(id: string | null): void; voids?: PlateVoid[]; projectId?: string;
  /** selecția comună cu planul (UI/UX P2-10): piesa selectată și cea de sub mouse (aici sau în plan); `frame.n` crește la fiecare cerere de încadrare */
  selectedId?: string | null; hoverId?: string | null; onHover?(id: string | null): void; frame?: { id: string; n: number; at: number }; onFrameOther?(id: string): void }){
  const { t, lang } = usePrefs();
  const [room, setRoom] = useState<string | null>(null), [pano, setPano] = useState<{ url: string; title: string; file: string } | null>(null), [panoBusy, setPanoBusy] = useState(false), [panoMsg, setPanoMsg] = useState('');
  const ref = useRef<HTMLCanvasElement>(null), v = useRef<any>(null), pickRef = useRef(onPick), hoverRef = useRef(onHover); hoverRef.current = onHover;
  const [xray, setXray] = useState(false);
  // motorul se încarcă asincron: selecția și o cerere de încadrare venite înainte se aplică imediat după creare
  const syncSel = useRef(() => {}); syncSel.current = () => { v.current?.setSelection?.(selectedId); v.current?.setHover?.(hoverId); if (frame && Date.now() - frame.at < 4000 && v.current?.frame?.(frame.id)){ setMode('house'); setRoom(null); } };
  const [mode, setMode] = useState<'house' | 'walk'>('house'), [err, setErr] = useState(''), [touch, setTouch] = useState(false);
  const [time, setTime] = useState<TimeOfDay>('day'), [azimuth, setAzimuth] = useState(135), [lightOpen, setLightOpen] = useState(false), [exportOpen, setExportOpen] = useState(false);
  // vederi salvate (pe proiect, în acest browser) și tăierea machetei (UI/UX P2-11)
  const [views, setViews] = useState<SavedView[]>([]), [viewsOpen, setViewsOpen] = useState(false), [cutPct, setCutPct] = useState(42);
  useEffect(() => { if (!projectId) return; try { setViews(parseViews(localStorage.getItem(viewsKey(projectId)))); } catch { /* stocare blocată */ } }, [projectId]);
  const keepViews = (l: SavedView[]) => { setViews(l); if (projectId) try { localStorage.setItem(viewsKey(projectId), JSON.stringify(l)); } catch { /* stocare plină sau blocată */ } };
  async function saveView(){ const view = v.current?.getView?.(); if (!view) return; setViewsOpen(false);
    const name = await askPrompt({ title: t('viewer.saveView'), confirmLabel: t('viewer.saveViewOk'), input: { label: t('viewer.viewName'), maxLength: 40, initial: mode === 'walk' && room ? snap.floor.rooms.find(r => r.id === room)?.name ?? '' : '' } });
    if (name && name.trim()) keepViews(addView(views, name, view, typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : String(Date.now()))); }
  function openView(sv: SavedView){ setViewsOpen(false); setMode(sv.view.mode); if (sv.view.mode !== 'walk') setRoom(null); setCutPct(Math.round(sv.view.cut * 100)); v.current?.setView?.(sv.view); }
  const preset = (k: 'top' | 'iso' | 'front') => { setViewsOpen(false); go('house'); v.current?.setCameraPreset(k); };
  // calitate înaltă (ocluzie ambientală) implicit pe desktop; pe telefon rămâne normală, pentru fluiditate și baterie
  const [quality, setQuality] = useState<'normal' | 'high'>('normal'), qRef = useRef(quality); qRef.current = quality;
  useEffect(() => { const t = typeof window !== 'undefined' && (matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0); setTouch(t); if (!t) setQuality('high'); }, []);
  pickRef.current = onPick;
  const [doorsOpen, setDoorsOpen] = useState(true);
  useEffect(() => { let alive = true;
    import('./viewer3d-engine.js').then(m => { if (!alive || !ref.current) return; try { v.current = m.createViewer(ref.current, { onPick: (id: string | null) => pickRef.current(id), onHover: (id: string | null) => hoverRef.current?.(id), onContextLost: () => setErr('context-lost'), onContextRestored: () => { setErr(''); applyLight(); v.current?.setQuality?.(qRef.current); push(); } }); if (process.env.NODE_ENV !== 'production') (window as any).__casa3dViewer = v.current; applyLight(); v.current.setQuality?.(qRef.current); push(); syncSel.current(); } catch (e){ console.error('3D', e); setErr('webgl'); } });
    return () => { alive = false; v.current?.dispose(); v.current = null; }; }, []); // eslint-disable-line
  const engineCat = useRef<ReturnType<typeof toEngineCatalog> | null>(null);
  function push(){ if (!v.current) return; engineCat.current ||= toEngineCatalog(catalog);
    const { plan, items } = viewerInput(snap, catalog, engineCat.current, { voids, mc }); v.current.setState(plan, items); }
  useEffect(push, [snap, mc]); // eslint-disable-line
  useEffect(() => { v.current?.setSelection?.(selectedId); }, [selectedId, snap]);
  useEffect(() => { v.current?.setHover?.(hoverId); }, [hoverId]);
  useEffect(() => { if (frame && v.current?.frame?.(frame.id)){ setMode('house'); setRoom(null); } }, [frame?.n]); // eslint-disable-line
  useEffect(() => { v.current?.setXray?.(xray); }, [xray]);
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
      {/* presetările și vederile salvate într-un singur meniu (Shapr3D „Saved Views”); bara rămâne pe un rând */}
      <div className="v3pop">
        <button className="btn" aria-expanded={viewsOpen} aria-haspopup="menu" onClick={() => { setViewsOpen(o => !o); setLightOpen(false); }} title={t('viewer.viewsTitle')}><Icon name="plan" size={16} /><span className="v3lbl">{t('viewer.views')}</span></button>
        {viewsOpen && <div className="v3menu v3menu-l" role="menu" onKeyDown={e => { if (e.key === 'Escape') setViewsOpen(false); }}>
          <button role="menuitem" onClick={() => preset('top')} title={t('viewer.camTopTitle')}>{t('viewer.camTop')}</button>
          <button role="menuitem" onClick={() => preset('iso')} title={t('viewer.camIsoTitle')}>{t('viewer.camIso')}</button>
          <button role="menuitem" onClick={() => preset('front')} title={t('viewer.camFrontTitle')}>{t('viewer.camFront')}</button>
          {views.length > 0 && <div className="v3menu-h" role="presentation">{t('viewer.savedViews')}</div>}
          {views.map(sv => <div key={sv.id} className="v3menu-row" role="presentation">
            <button role="menuitem" onClick={() => openView(sv)}>{sv.name}</button>
            <button type="button" className="v3menu-x" aria-label={t('viewer.deleteView', { name: sv.name })} title={t('viewer.deleteView', { name: sv.name })} onClick={() => keepViews(removeView(views, sv.id))}>×</button></div>)}
          {projectId && <button role="menuitem" className="v3menu-add" onClick={saveView}>{t('viewer.saveView')}…</button>}
        </div>}
      </div>
      {/* intrarea în camere: o listă, nu câte un buton pe cameră (bara încape pe un rând și pe laptop) */}
      <select className="btn" aria-label={t('viewer.tourPick')} value={mode === 'walk' && room ? room : ''} onChange={e => { const id = e.target.value; if (!id) { go('house'); return; } setMode('walk'); setRoom(id); v.current?.goRoom(id); }}>
        <option value="">{t('viewer.tourPick')}</option>
        {snap.floor.rooms.map(r => <option key={r.id} value={r.id}>{t('viewer.tour', { room: r.name })}</option>)}
      </select>
      {mode === 'walk' && room && <button className="btn" disabled={panoBusy || !!err} title={t('pano.title')} onClick={openPano}>360°</button>}
      {panoMsg && <span className="muted" role="status">{panoMsg}</span>}
      {/* un singur meniu „Afișare”: uși, tăierea machetei, lumină și calitate */}
      <button className="btn" aria-expanded={lightOpen} onClick={() => { setLightOpen(o => !o); setViewsOpen(false); }} title={t('viewer.displayTitle')} aria-label={t('viewer.displayTitle')}><Icon name="sun" size={16} /><span className="v3lbl" aria-hidden="true">{t('viewer.display')}</span></button>
      {selectedId && onFrameOther && <button className="btn" title={t('viewer.showInPlanTitle')} onClick={() => onFrameOther(selectedId)}><Icon name="plan" size={16} /><span className="v3lbl">{t('viewer.showInPlan')}</span></button>}
      <div className="v3exp">
        <button className="btn" aria-expanded={exportOpen} aria-haspopup="menu" disabled={!!err} onClick={() => setExportOpen(o => !o)} title={t('viewer.exportTitle')} aria-label={t('viewer.exportTitle')}><Icon name="download" size={16} /><span className="v3lbl" aria-hidden="true">{t('viewer.export')}</span><Icon name={exportOpen ? 'chevronUp' : 'chevronDown'} size={14} /></button>
        {exportOpen && <div className="v3menu" role="menu" onKeyDown={e => { if (e.key === 'Escape') setExportOpen(false); }}>
          <button role="menuitem" onClick={() => { setExportOpen(false); capture(); }} title={t('viewer.captureTitle')}>{t('viewer.capture')}</button>
          <button role="menuitem" onClick={() => { setExportOpen(false); exportBlender(); }} title={t('viewer.blenderTitle')}>{t('viewer.blender')}</button>
          <button role="menuitem" onClick={() => { setExportOpen(false); exportGlb(); }} title={t('viewer.glbTitle')}>{t('viewer.glb')}</button>
        </div>}
      </div>
    </div>
    {lightOpen && <div className="v3light" role="group" aria-label={t('viewer.displayTitle')}>
      <button className="btn" aria-pressed={doorsOpen} title={t('viewer.doors')} onClick={() => { const n = !doorsOpen; setDoorsOpen(n); v.current?.setDoors(n); }}><Icon name="door" size={16} />{t('viewer.doorsOpen')}</button>
      <button className="btn" aria-pressed={xray} title={t('viewer.xrayTitle')} onClick={() => setXray(x => !x)}>{t('viewer.xray')}</button>
      {mode === 'house' && <label className="f" style={{ minWidth: 160 }} title={t('viewer.cutTitle')}><span>{t('viewer.cut', { pct: cutPct })}</span><input type="range" min={12} max={100} value={cutPct} onChange={e => { const n = Number(e.target.value); setCutPct(n); v.current?.setCut?.(clampCut(n / 100)); }} /></label>}
      {(['day', 'evening', 'night'] as const).map(tod => <button key={tod} className="btn" aria-pressed={time === tod} onClick={() => setTime(tod)}>{t(tod === 'day' ? 'viewer.day' : tod === 'evening' ? 'viewer.evening' : 'viewer.night')}</button>)}
      <button className="btn" aria-pressed={quality === 'high'} onClick={() => setQuality(q => q === 'high' ? 'normal' : 'high')} title={t('viewer.qualityTitle')}>{t('viewer.quality')}</button>
      <label className="f" style={{ minWidth: 160 }}><span>{t('viewer.sun', { deg: Math.round(azimuth) })}</span><input type="range" min={0} max={359} value={azimuth} onChange={e => setAzimuth(Number(e.target.value))} /></label>
    </div>}
    <div className="hintbar">{err ? t('viewer.noWebgl') : (mode === 'walk' ? (touch ? t('viewer.hintWalkTouch') : t('viewer.hintWalk')) : (touch ? t('viewer.hintHouseTouch') : t('viewer.hintHouse')))}</div>
    {pano && <PanoramaView url={pano.url} title={pano.title} fileName={pano.file} onClose={() => setPano(null)} />}
    {/* WCAG 2.2 2.5.7: tot ce se face prin tragere (rotire, înclinare, zoom) are și un buton */}
    {!err && <div className="v3nudge" role="group" aria-label={t('viewer.nudgeAria')}>
      {([['left', '◀'], ['up', '▲'], ['down', '▼'], ['right', '▶'], ['in', '+'], ['out', '−']] as const).map(([k, sym]) =>
        <button key={k} type="button" className="btn" aria-label={t(`viewer.nudge.${k}`)} title={t(`viewer.nudge.${k}`)} onClick={() => v.current?.nudge?.(k)}>{sym}</button>)}
    </div>}
  </div>);
}
