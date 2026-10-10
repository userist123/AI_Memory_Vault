'use client';
import { useEffect, useRef, useState } from 'react';
import Joystick from './Joystick';
import type { Catalog, Snapshot } from '@/core/types';
import { floorToPlan } from '@/features/migration/legacy';
import { toEngineCatalog, groupOf, indexOf } from '@/core/catalog';
import { footprintOf } from '@/core/validate';
import { lighting, sunDirection, captureFileName, type TimeOfDay } from '@/core/lighting';

export default function Viewer3D({ snap, catalog, onPick }: { snap: Snapshot; catalog: Catalog; onPick(id: string | null): void }){
  const ref = useRef<HTMLCanvasElement>(null), v = useRef<any>(null), pickRef = useRef(onPick);
  const [mode, setMode] = useState<'house' | 'walk'>('house'), [err, setErr] = useState(''), [touch, setTouch] = useState(false);
  const [time, setTime] = useState<TimeOfDay>('day'), [azimuth, setAzimuth] = useState(135), [lightOpen, setLightOpen] = useState(false);
  useEffect(() => { setTouch(typeof window !== 'undefined' && (matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0)); }, []);
  pickRef.current = onPick;
  useEffect(() => { let alive = true;
    import('./viewer3d-engine.js').then(m => { if (!alive || !ref.current) return; try { v.current = m.createViewer(ref.current, { onPick: (id: string | null) => pickRef.current(id) }); applyLight(); push(); } catch { setErr('Browserul nu suportă WebGL, așa că vizualizarea 3D nu e disponibilă.'); } });
    return () => { alive = false; v.current?.dispose(); v.current = null; }; }, []); // eslint-disable-line
  const engineCat = useRef<ReturnType<typeof toEngineCatalog> | null>(null);
  function push(){ if (!v.current) return; engineCat.current ||= toEngineCatalog(catalog);
    const items = snap.placements.map(p => { const g = engineCat.current![groupOf(p.variantId)], vv = g?.v[indexOf(p.variantId)];
      return { id: p.id, group: p.group, x: p.x, z: p.z, rotation: p.rotation, fp: footprintOf(catalog, p), variant: vv ? { ...vv, model: g.model } : null }; });
    v.current.setState(floorToPlan(snap.floor, snap.name), items); }
  useEffect(push, [snap]); // eslint-disable-line
  function applyLight(){ const p = lighting(time, azimuth); v.current?.setLighting({ ...p, dir: sunDirection(p.azimuthDeg, p.elevationDeg) }); }
  useEffect(applyLight, [time, azimuth]); // eslint-disable-line
  function capture(){ const url: string | undefined = v.current?.capture(); if (!url) return;
    const a = document.createElement('a'); a.href = url; a.download = captureFileName(snap.name); document.body.appendChild(a); a.click(); a.remove(); }
  // Joystick-ul dispare când ieși din tur: orice mișcare rămasă e anulată, altfel jucătorul ar aluneca la următorul tur.
  useEffect(() => { if (mode !== 'walk') v.current?.setMove(0, 0); }, [mode]);
  const go = (m: 'house' | 'walk') => { setMode(m); v.current?.setMode(m); };
  return (<div className="view3d">
    <canvas ref={ref} aria-label="Vizualizare 3D" />
    {mode === 'walk' && touch && !err && <Joystick onMove={(f, s) => v.current?.setMove(f, s)} />}
    <div className="v3bar">
      <button className="btn" aria-pressed={mode === 'house'} onClick={() => go('house')}>Machetă</button>
      {snap.floor.rooms.map(r => <button key={r.id} className="btn" onClick={() => { setMode('walk'); v.current?.goRoom(r.id); }}>Tur: {r.name}</button>)}
      <button className="btn" aria-expanded={lightOpen} onClick={() => setLightOpen(o => !o)} title="Momentul zilei și orientarea soarelui">Lumină</button>
      <button className="btn" onClick={capture} disabled={!!err} title="Descarcă imaginea 3D curentă ca PNG">Captură PNG</button>
    </div>
    {lightOpen && <div className="v3light" role="group" aria-label="Iluminare">
      {(['day', 'evening', 'night'] as const).map(t => <button key={t} className="btn" aria-pressed={time === t} onClick={() => setTime(t)}>{t === 'day' ? 'Zi' : t === 'evening' ? 'Seară' : 'Noapte'}</button>)}
      <label className="f" style={{ minWidth: 160 }}><span>Soarele din direcția {Math.round(azimuth)}°</span><input type="range" min={0} max={359} value={azimuth} onChange={e => setAzimuth(Number(e.target.value))} /></label>
    </div>}
    <div className="hintbar">{err || (mode === 'walk' ? (touch ? 'Trage ca să privești · joystick-ul din stânga ca să mergi' : 'Trage ca să privești · W A S D sau săgeți ca să mergi') : 'Trage ca să rotești · rotița pentru zoom · apasă pe o piesă')}</div>
  </div>);
}
