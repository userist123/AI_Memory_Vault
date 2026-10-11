'use client';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Floor, Snapshot, Severity } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve } from '@/core/catalog';
import { area, r3, snapPoint, wallLength } from '@/core/geometry';
import { measure } from '@/core/edit-ops';
import { formatArea, formatLength } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import type { Calib } from './UnderlayPanel';
import { TECH_SYMBOL } from './TechPanel';
import { stairGeometry, SLAB } from '@/core/levels';
import { allFixturePoints } from '@/core/fixtures';
import { finishesOf } from '@/core/boq';

export type Tool = 'select' | 'wall' | 'room' | 'door' | 'window' | 'measure' | 'tech';
export type Sel = { kind: 'wall' | 'room' | 'placement' | 'stair'; id: string } | { kind: 'opening'; id: string; wallId: string } | null;
type Phase = 'start' | 'move' | 'end';
interface Props { snap: Snapshot; catalog: Catalog; sel: Sel; tool: Tool; severities: Record<string, Severity>;
  onSelect(s: Sel): void; onEdit(fn: (s: Snapshot) => void, phase: Phase): void; onAddWall(a: [number, number], b: [number, number]): void;
  onAddRoom(r: { x0: number; z0: number; x1: number; z1: number }): void; onAddOpening(wallId: string, offset: number, kind: 'door' | 'window'): void;
  calib?: Calib | null; onCalibPick?(a: [number, number], b: [number, number]): void;
  showTech?: boolean; onTechAt?(x: number, z: number): void;
  /** golurile din placă ale acestui nivel (scările care urcă de dedesubt) */
  voids?: { x0: number; z0: number; x1: number; z1: number }[]; }
const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };
const SEV: Record<Severity, string> = { PASS: '#1F4E79', WARNING: '#B7791F', ERROR: '#B3261E' };
const G = .05;
const snapG = (v: number) => r3(Math.round(v / G) * G);

export default function PlanView(p: Props){
  const { t, lang, units } = usePrefs();
  // etichete de lungime: metric = cm fără unitate pe plan (aglomerat), imperial = picioare și inci
  const wl = (m: number) => units === 'imperial' ? formatLength(m, 'imperial', lang) : String(Math.round(m * 100));
  const svg = useRef<SVGSVGElement>(null);
  const floor = p.snap.floor;
  const bounds = useMemo(() => { const xs = floor.walls.flatMap(w => [w.a[0], w.b[0]]).concat(floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = floor.walls.flatMap(w => [w.a[1], w.b[1]]).concat(floor.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
    return xs.length ? { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) } : { x0: 0, x1: 6, z0: 0, z1: 5 }; }, []); // eslint-disable-line
  const [vb, setVb] = useState(() => { const pad = 1.2; return { x: bounds.x0 - pad, y: bounds.z0 - pad, w: bounds.x1 - bounds.x0 + pad * 2, h: bounds.z1 - bounds.z0 + pad * 2 }; });
  const [preview, setPreview] = useState<{ a: [number, number]; b: [number, number] } | null>(null);
  const [roomDraft, setRoomDraft] = useState<{ a: [number, number]; b: [number, number] } | null>(null);
  const [cursor, setCursor] = useState<[number, number] | null>(null);
  const [rulerHover, setRulerHover] = useState<[number, number] | null>(null);
  const [aspect, setAspect] = useState(1), [calA, setCalA] = useState<[number, number] | null>(null);
  const ul = p.snap.underlay;
  useEffect(() => { if (!ul) return; const i = new Image(); i.onload = () => i.naturalWidth && setAspect(i.naturalHeight / i.naturalWidth); i.src = ul.dataUrl; }, [ul?.dataUrl]); // eslint-disable-line
  useEffect(() => { if (p.calib?.stage !== 'pick') setCalA(null); }, [p.calib?.stage]);
  const [ruler, setRuler] = useState<{ a: [number, number]; b: [number, number] | null } | null>(null);
  useEffect(() => { setRuler(null); }, [p.tool]);
  const drag = useRef<any>(null), pan = useRef<any>(null);

  const toWorld = (e: { clientX: number; clientY: number }): [number, number] => { const s = svg.current!, pt = s.createSVGPoint(); pt.x = e.clientX; pt.y = e.clientY; const w = pt.matrixTransform(s.getScreenCTM()!.inverse()); return [w.x, w.y]; };
  useEffect(() => { const el = svg.current!; const wheel = (e: WheelEvent) => { e.preventDefault(); const [wx, wz] = toWorld(e), k = e.deltaY > 0 ? 1.12 : 1 / 1.12;
    setVb(v => { const w = Math.min(80, Math.max(1.5, v.w * k)), h = v.h * (w / v.w); return { x: wx - (wx - v.x) * (w / v.w), y: wz - (wz - v.y) * (w / v.w), w, h }; }); };
    el.addEventListener('wheel', wheel, { passive: false }); return () => el.removeEventListener('wheel', wheel); }, []);
  useEffect(() => { const k = (e: KeyboardEvent) => { if (e.key === 'Escape'){ setCalA(null); setRuler(null); setPreview(null); setRoomDraft(null); drag.current = null; } }; addEventListener('keydown', k); return () => removeEventListener('keydown', k); }, []);

  function nearestWall(pt: [number, number], maxD = .35){ let best: { id: string; offset: number; d: number } | null = null;
    for (const w of floor.walls){ const L = wallLength(w.a, w.b); if (L < .01) continue; const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, s = Math.max(0, Math.min(L, (pt[0] - w.a[0]) * ux + (pt[1] - w.a[1]) * uz)), d = Math.hypot(pt[0] - (w.a[0] + ux * s), pt[1] - (w.a[1] + uz * s));
      if (d < maxD && (!best || d < best.d)) best = { id: w.id, offset: s, d }; } return best; }
  function down(e: React.PointerEvent){
    if (e.button === 1 || (e.button === 0 && e.altKey)){ pan.current = { x: e.clientX, y: e.clientY, vb }; (e.target as Element).setPointerCapture?.(e.pointerId); return; }
    if (e.button !== 0) return; const pt = toWorld(e);
    if (p.calib?.stage === 'pick'){ const q: [number, number] = [r3(pt[0]), r3(pt[1])]; if (!calA) setCalA(q); else if (Math.hypot(q[0] - calA[0], q[1] - calA[1]) > .05){ p.onCalibPick?.(calA, q); setCalA(null); } return; }
    if (p.tool === 'tech'){ p.onTechAt?.(pt[0], pt[1]); return; }
    if (p.tool === 'measure'){ const s = snapPoint(pt, floor); setRuler(r => r && !r.b ? { a: r.a, b: s } : { a: s, b: null }); return; }
    if (p.tool === 'wall'){ const s = snapPoint(pt, floor); if (!preview) setPreview({ a: s, b: s }); else { const b = ortho(preview.a, snapPoint(pt, floor)); if (wallLength(preview.a, b) >= .2) p.onAddWall(preview.a, b); setPreview({ a: b, b }); } return; }
    if (p.tool === 'room'){ const s: [number, number] = [snapG(pt[0]), snapG(pt[1])]; setRoomDraft({ a: s, b: s }); svg.current!.setPointerCapture(e.pointerId); return; }
    if (p.tool === 'door' || p.tool === 'window'){ const w = nearestWall(pt); if (w) p.onAddOpening(w.id, w.offset, p.tool); return; }
    const t = (e.target as Element).closest('[data-k]') as HTMLElement | null;
    if (!t){ p.onSelect(null); return; }
    const kind = t.dataset.k!, id = t.dataset.id!;
    if (kind === 'underlay'){ p.onSelect(null); drag.current = { kind, start: pt, moved: false, orig: null }; svg.current!.setPointerCapture(e.pointerId); return; }
    if (kind === 'handle'){ drag.current = { kind, wallId: t.dataset.w, end: t.dataset.end, start: pt, moved: false }; }
    else if (kind === 'opening'){ p.onSelect({ kind: 'opening', id, wallId: t.dataset.w! }); drag.current = { kind, id, wallId: t.dataset.w, start: pt, moved: false }; }
    else { p.onSelect({ kind: kind as any, id }); drag.current = { kind, id, start: pt, moved: false, orig: null }; }
    svg.current!.setPointerCapture(e.pointerId);
  }
  function move(e: React.PointerEvent){
    const pt = toWorld(e); setCursor(pt);
    if (pan.current){ const s = svg.current!.getScreenCTM()!, k = 1 / s.a; setVb({ ...pan.current.vb, x: pan.current.vb.x - (e.clientX - pan.current.x) * k, y: pan.current.vb.y - (e.clientY - pan.current.y) * k }); return; }
    if (ruler && !ruler.b) { setRulerHover(snapPoint(pt, floor)); }
    if (preview && p.tool === 'wall'){ setPreview({ a: preview.a, b: ortho(preview.a, snapPoint(pt, floor)) }); return; }
    if (roomDraft){ setRoomDraft({ a: roomDraft.a, b: [snapG(pt[0]), snapG(pt[1])] }); return; }
    const d = drag.current; if (!d) return;
    const dx = pt[0] - d.start[0], dz = pt[1] - d.start[1]; if (!d.moved && Math.hypot(dx, dz) < .03) return;
    const phase: Phase = d.moved ? 'move' : 'start'; d.moved = true;
    if (!d.orig) d.orig = JSON.parse(JSON.stringify(p.snap));
    const o = d.orig as Snapshot;
    p.onEdit(s => {
      if (d.kind === 'underlay'){ const a = o.underlay, b = s.underlay; if (a && b && !a.locked){ b.x = r3(a.x + dx); b.z = r3(a.z + dz); } }
      else if (d.kind === 'placement'){ const a = o.placements.find(x => x.id === d.id)!, b = s.placements.find(x => x.id === d.id)!; b.x = snapG(a.x + dx); b.z = snapG(a.z + dz);
        const room = s.floor.rooms.find(r => b.x >= r.rect.x0 && b.x <= r.rect.x1 && b.z >= r.rect.z0 && b.z <= r.rect.z1); if (room) b.roomId = room.id; b.source = 'manual'; }
      else if (d.kind === 'stair'){ const a = o.floor.stairs?.find(x => x.id === d.id), b = s.floor.stairs?.find(x => x.id === d.id); if (a && b){ b.x = snapG(a.x + dx); b.z = snapG(a.z + dz); } }
      else if (d.kind === 'room'){ const a = o.floor.rooms.find(x => x.id === d.id)!.rect, b = s.floor.rooms.find(x => x.id === d.id)!; const ddx = snapG(dx), ddz = snapG(dz);
        b.rect = { x0: r3(a.x0 + ddx), x1: r3(a.x1 + ddx), z0: r3(a.z0 + ddz), z1: r3(a.z1 + ddz) }; }
      else if (d.kind === 'wall'){ const a = o.floor.walls.find(x => x.id === d.id)!, b = s.floor.walls.find(x => x.id === d.id)!; const ddx = snapG(dx), ddz = snapG(dz);
        b.a = [r3(a.a[0] + ddx), r3(a.a[1] + ddz)]; b.b = [r3(a.b[0] + ddx), r3(a.b[1] + ddz)]; }
      else if (d.kind === 'handle'){ const b = s.floor.walls.find(x => x.id === d.wallId)!; const other = d.end === 'a' ? b.b : b.a; const np = ortho(other, snapPoint(pt, { ...s.floor, walls: s.floor.walls.filter(w => w.id !== d.wallId) }));
        if (d.end === 'a') b.a = np; else b.b = np; }
      else if (d.kind === 'opening'){ const w = s.floor.walls.find(x => x.id === d.wallId)!, op = w.openings.find(x => x.id === d.id)!, L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
        const sAlong = (pt[0] - w.a[0]) * ux + (pt[1] - w.a[1]) * uz; op.offset = r3(Math.max(0, Math.min(L - op.width, snapG(sAlong - op.width / 2)))); }
    }, phase);
  }
  function up(e: React.PointerEvent){
    if (pan.current){ pan.current = null; return; }
    if (roomDraft){ const { a, b } = roomDraft; setRoomDraft(null); const r = { x0: Math.min(a[0], b[0]), x1: Math.max(a[0], b[0]), z0: Math.min(a[1], b[1]), z1: Math.max(a[1], b[1]) }; if (r.x1 - r.x0 >= .6 && r.z1 - r.z0 >= .6) p.onAddRoom(r); return; }
    const d = drag.current; drag.current = null; if (d?.moved) p.onEdit(() => {}, 'end');
  }
  const ortho = (a: [number, number], b: [number, number]): [number, number] => { const dx = b[0] - a[0], dz = b[1] - a[1]; if (Math.abs(dx) < Math.abs(dz) * .15) return [a[0], b[1]]; if (Math.abs(dz) < Math.abs(dx) * .15) return [b[0], a[1]]; return b; };

  const fs = Math.max(.12, vb.w / 70), isSel = (k: string, id: string) => p.sel && p.sel.kind === k && p.sel.id === id;
  return (<svg ref={svg} className="plan" viewBox={`${vb.x} ${vb.y} ${vb.w} ${vb.h}`} onPointerDown={down} onPointerMove={move} onPointerUp={up} onContextMenu={e => { e.preventDefault(); setPreview(null); }}
    style={{ cursor: p.tool === 'select' ? 'default' : 'crosshair' }} role="application" aria-label={t('plan.aria')}>
    <defs><pattern id="g" width="1" height="1" patternUnits="userSpaceOnUse"><path d="M1 0H0V1" fill="none" stroke="#e4e5e0" strokeWidth=".01" /></pattern></defs>
    <rect x={vb.x} y={vb.y} width={vb.w} height={vb.h} fill="url(#g)" />
    {ul && <image data-k="underlay" href={ul.dataUrl} x={ul.x} y={ul.z} width={ul.widthM} height={ul.widthM * aspect} preserveAspectRatio="none" opacity={ul.opacity} pointerEvents={ul.locked || p.calib?.stage === 'pick' ? 'none' : 'auto'} style={{ cursor: ul.locked ? 'default' : 'move' }} />}
    {floor.rooms.map(r => { const w = r.rect.x1 - r.rect.x0, d = r.rect.z1 - r.rect.z0; return (<g key={r.id}>
      <rect data-k="room" data-id={r.id} x={r.rect.x0} y={r.rect.z0} width={w} height={d} fill={ROOM_FILL[r.type] || '#f2eee6'} fillOpacity={ul ? .45 : 1} stroke={isSel('room', r.id) ? '#1F4E79' : 'none'} strokeWidth={.04} />
      <text x={(r.rect.x0 + r.rect.x1) / 2} y={(r.rect.z0 + r.rect.z1) / 2 - fs * .2} textAnchor="middle" fontSize={fs * 1.05} fontFamily="IBM Plex Sans" fill="#23262B" pointerEvents="none">{r.name}</text>
      <text x={(r.rect.x0 + r.rect.x1) / 2} y={(r.rect.z0 + r.rect.z1) / 2 + fs * 1.1} textAnchor="middle" fontSize={fs * .8} fontFamily="IBM Plex Mono" fill="#5E636B" pointerEvents="none">{formatArea(area(r.rect), units, lang)} · {wl(w)}×{wl(d)}</text>
    </g>); })}
    {(p.voids || []).map((v, k) => <g key={`void-${k}`} pointerEvents="none">
      <rect x={v.x0} y={v.z0} width={v.x1 - v.x0} height={v.z1 - v.z0} fill="#FBFBF9" stroke="#5E636B" strokeWidth={.02} strokeDasharray=".08 .05" />
      <path d={`M${v.x0} ${v.z0}L${v.x1} ${v.z1}M${v.x1} ${v.z0}L${v.x0} ${v.z1}`} stroke="#8A8F96" strokeWidth={.012} />
      <text x={(v.x0 + v.x1) / 2} y={v.z0 - fs * .3} textAnchor="middle" fontSize={fs * .7} fontFamily="IBM Plex Mono" fill="#5E636B">{t('stair.void')}</text></g>)}
    {(floor.stairs || []).map(st => { const g = stairGeometry(st, floor.ceilingHeight + SLAB), r = g.rect, s = isSel('stair', st.id), [dx, dz] = g.dir, px = -dz, pz = dx, hw = st.width / 2;
      // trepte perpendiculare pe direcția de urcare, săgeata de la prima treaptă spre ultima, „SUS” la pornire
      const treads = Array.from({ length: g.steps - 1 }, (_, k) => { const cx = g.bottom[0] + dx * g.going * (k + 1), cz = g.bottom[1] + dz * g.going * (k + 1); return `M${cx - px * hw} ${cz - pz * hw}L${cx + px * hw} ${cz + pz * hw}`; }).join('');
      const ah = Math.min(.25, st.width * .3), tip = g.top, arrow = `M${g.bottom[0] + dx * .1} ${g.bottom[1] + dz * .1}L${tip[0] - dx * .05} ${tip[1] - dz * .05}M${tip[0] - dx * ah - px * ah * .6} ${tip[1] - dz * ah - pz * ah * .6}L${tip[0] - dx * .05} ${tip[1] - dz * .05}L${tip[0] - dx * ah + px * ah * .6} ${tip[1] - dz * ah + pz * ah * .6}`;
      return (<g key={st.id}><rect data-k="stair" data-id={st.id} x={r.x0} y={r.z0} width={r.x1 - r.x0} height={r.z1 - r.z0} fill="#e9e2d5" stroke={s ? '#1F4E79' : '#5E636B'} strokeWidth={s ? .035 : .018} style={{ cursor: 'move' }} />
        <path d={treads} stroke="#8A8F96" strokeWidth={.01} pointerEvents="none" /><path d={arrow} fill="none" stroke="#23262B" strokeWidth={.02} pointerEvents="none" />
        <text x={g.bottom[0] - dx * fs * .5} y={g.bottom[1] - dz * fs * .5 + fs * .3} textAnchor="middle" fontSize={fs * .7} fontFamily="IBM Plex Mono" fill="#23262B" pointerEvents="none">{t('stair.up')}</text></g>); })}
    {p.snap.placements.map(pl => { const fp = footprintOf(p.catalog, pl); if (!fp) return null; const sv = p.severities[pl.id] || 'PASS', rv = resolve(p.catalog, pl.variantId);
      const k = ((Math.round(pl.rotation / (Math.PI / 2)) % 4) + 4) % 4, fl = k === 0 ? [fp.x0, fp.z1, fp.x1, fp.z1] : k === 2 ? [fp.x0, fp.z0, fp.x1, fp.z0] : k === 1 ? [fp.x1, fp.z0, fp.x1, fp.z1] : [fp.x0, fp.z0, fp.x0, fp.z1];
      return (<g key={pl.id}><rect data-k="placement" data-id={pl.id} x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill={SEV[sv]} fillOpacity={isSel('placement', pl.id) ? .35 : .16} stroke={SEV[sv]} strokeWidth={isSel('placement', pl.id) ? .035 : .015} style={{ cursor: 'move' }} />
        <line x1={fl[0]} y1={fl[1]} x2={fl[2]} y2={fl[3]} stroke={SEV[sv]} strokeWidth={.03} pointerEvents="none" />
        {(fp.x1 - fp.x0) > .45 && <text x={(fp.x0 + fp.x1) / 2} y={(fp.z0 + fp.z1) / 2 + fs * .3} textAnchor="middle" fontSize={fs * .72} fontFamily="IBM Plex Mono" fill={SEV[sv]} pointerEvents="none">{rv?.product.name.split(' ')[0]}</text>}</g>); })}
    {floor.walls.map(w => { const L = wallLength(w.a, w.b); if (L < .01) return null; const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, nx = -uz, nz = ux, sel = isSel('wall', w.id);
      const mid = [(w.a[0] + w.b[0]) / 2 + nx * (w.thickness / 2 + fs * .9), (w.a[1] + w.b[1]) / 2 + nz * (w.thickness / 2 + fs * .9)];
      return (<g key={w.id}>
        <line data-k="wall" data-id={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke={sel ? '#1F4E79' : '#23262B'} strokeWidth={w.thickness} strokeLinecap="square" style={{ cursor: 'pointer' }} />
        {w.openings.map(o => { const a = [w.a[0] + ux * o.offset, w.a[1] + uz * o.offset], b = [w.a[0] + ux * (o.offset + o.width), w.a[1] + uz * (o.offset + o.width)], os = p.sel?.kind === 'opening' && p.sel.id === o.id;
          return (<g key={o.id}><line data-k="opening" data-id={o.id} data-w={w.id} x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} stroke={os ? '#dbe7f3' : '#FBFBF9'} strokeWidth={w.thickness + .01} style={{ cursor: 'ew-resize' }} />
            {o.kind === 'window' ? <><line x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} stroke="#2E6DA4" strokeWidth={.025} pointerEvents="none" /><line x1={a[0] + nx * w.thickness * .3} y1={a[1] + nz * w.thickness * .3} x2={b[0] + nx * w.thickness * .3} y2={b[1] + nz * w.thickness * .3} stroke="#2E6DA4" strokeWidth={.012} pointerEvents="none" /></>
              : <path d={`M${a[0]} ${a[1]} L${a[0] + nx * o.width} ${a[1] + nz * o.width} A${o.width} ${o.width} 0 0 ${1} ${b[0]} ${b[1]}`} fill="none" stroke="#5E636B" strokeWidth={.012} strokeDasharray=".04 .03" pointerEvents="none" />}</g>); })}
        <text x={mid[0]} y={mid[1] + fs * .3} textAnchor="middle" fontSize={fs * .78} fontFamily="IBM Plex Mono" fill={sel ? '#1F4E79' : '#5E636B'} pointerEvents="none">{wl(L)}</text>
        {sel && ([['a', w.a], ['b', w.b]] as const).map(([end, pt]) => <circle key={end} data-k="handle" data-w={w.id} data-end={end} data-id={w.id} cx={pt[0]} cy={pt[1]} r={fs * .6} fill="#fff" stroke="#1F4E79" strokeWidth={.03} style={{ cursor: 'grab' }} />)}
      </g>); })}
    {preview && <line x1={preview.a[0]} y1={preview.a[1]} x2={preview.b[0]} y2={preview.b[1]} stroke="#2E6DA4" strokeWidth={.15} strokeOpacity={.5} strokeDasharray=".1 .06" />}
    {preview && <text x={(preview.a[0] + preview.b[0]) / 2} y={(preview.a[1] + preview.b[1]) / 2 - fs} textAnchor="middle" fontSize={fs} fontFamily="IBM Plex Mono" fill="#1F4E79">{units === 'imperial' ? formatLength(wallLength(preview.a, preview.b), 'imperial', lang) : `${Math.round(wallLength(preview.a, preview.b) * 100)} cm`}</text>}
    {roomDraft && <rect x={Math.min(roomDraft.a[0], roomDraft.b[0])} y={Math.min(roomDraft.a[1], roomDraft.b[1])} width={Math.abs(roomDraft.b[0] - roomDraft.a[0])} height={Math.abs(roomDraft.b[1] - roomDraft.a[1])} fill="#2E6DA4" fillOpacity={.12} stroke="#2E6DA4" strokeWidth={.02} strokeDasharray=".08 .05" />}
    {allFixturePoints(p.snap, p.catalog, floor.rooms, finishesOf).map((f, i) => { const r = fs * .45, c = '#8A5A00';
      // simbolurile de pe planul de iluminat: pendul = cerc cu cruce, aplică = semicerc lipit de perete, șină = linie cu spoturi
      if (f.kind === 'track'){ const h = (f.len ?? 2) / 2, [x0, z0, x1, z1] = f.alongX ? [f.x - h, f.z, f.x + h, f.z] : [f.x, f.z - h, f.x, f.z + h], n = Math.max(2, Math.round((f.len ?? 2) / .5));
        return <g key={`fx${i}`} pointerEvents="none"><line x1={x0} y1={z0} x2={x1} y2={z1} stroke={c} strokeWidth={.03} />{Array.from({ length: n }, (_, k) => { const o = (k + .5) / n; return <circle key={k} cx={x0 + (x1 - x0) * o} cy={z0 + (z1 - z0) * o} r={r * .45} fill="#fff" stroke={c} strokeWidth={.015} />; })}</g>; }
      if (f.kind === 'sconce'){ const a = Math.atan2(f.nz, f.nx) * 180 / Math.PI; return <path key={`fx${i}`} pointerEvents="none" transform={`translate(${f.x} ${f.z}) rotate(${a})`} d={`M0 ${-r} A${r} ${r} 0 0 1 0 ${r} Z`} fill="#fff" stroke={c} strokeWidth={.02} />; }
      return <g key={`fx${i}`} pointerEvents="none"><circle cx={f.x} cy={f.z} r={r} fill="#fff" stroke={c} strokeWidth={.02} /><path d={`M${f.x - r * .7} ${f.z - r * .7}L${f.x + r * .7} ${f.z + r * .7}M${f.x - r * .7} ${f.z + r * .7}L${f.x + r * .7} ${f.z - r * .7}`} stroke={c} strokeWidth={.015} /></g>; })}
    {p.showTech && (p.snap.tech || []).map(tp => { const sym = TECH_SYMBOL[tp.kind], r = fs * (sym.letter.length > 1 ? .62 : .5);
      return <g key={tp.id} pointerEvents="none"><circle cx={tp.x} cy={tp.z} r={r} fill={sym.color} stroke="#fff" strokeWidth={fs * .08} opacity={.92} />
        <text x={tp.x} y={tp.z + fs * .2} textAnchor="middle" fontSize={fs * (sym.letter.length > 1 ? .5 : .6)} fontFamily="IBM Plex Mono" fill="#fff">{sym.letter}</text></g>; })}
    {ruler && (() => { const b = ruler.b || rulerHover || ruler.a, d = measure(ruler.a, b); return (<g pointerEvents="none">
      <line x1={ruler.a[0]} y1={ruler.a[1]} x2={b[0]} y2={b[1]} stroke="#B3261E" strokeWidth={.03} strokeDasharray=".1 .05" />
      <circle cx={ruler.a[0]} cy={ruler.a[1]} r={fs * .3} fill="#B3261E" /><circle cx={b[0]} cy={b[1]} r={fs * .3} fill="#B3261E" />
      <text x={(ruler.a[0] + b[0]) / 2} y={(ruler.a[1] + b[1]) / 2 - fs * .6} textAnchor="middle" fontSize={fs} fontFamily="IBM Plex Mono" fill="#B3261E" stroke="#fff" strokeWidth={fs * .25} paintOrder="stroke">{formatLength(d, units, lang)}</text></g>); })()}
    {p.calib && (() => { const a = p.calib.stage === 'enter' ? p.calib.a : calA, b = p.calib.stage === 'enter' ? p.calib.b : (calA && cursor) || undefined; if (!a) return null; return (<g pointerEvents="none">
      {b && <line x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} stroke="#7A4A7F" strokeWidth={.03} strokeDasharray=".1 .05" />}
      <circle cx={a[0]} cy={a[1]} r={fs * .3} fill="#7A4A7F" />{b && <circle cx={b[0]} cy={b[1]} r={fs * .3} fill="#7A4A7F" />}</g>); })()}
    {cursor && <text x={vb.x + vb.w - fs * .6} y={vb.y + vb.h - fs * .6} textAnchor="end" fontSize={fs * .8} fontFamily="IBM Plex Mono" fill="#5E636B" pointerEvents="none">{units === 'imperial' ? `x ${formatLength(cursor[0], 'imperial', lang)} · z ${formatLength(cursor[1], 'imperial', lang)}` : `x ${cursor[0].toFixed(2)} · z ${cursor[1].toFixed(2)} m`}</text>}
  </svg>);
}
