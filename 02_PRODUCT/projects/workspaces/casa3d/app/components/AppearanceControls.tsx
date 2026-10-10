'use client';
// Controale de aspect: culori (palete + selector liber), materiale, perete accent, goluri, dimensiuni pe comandă,
// plus produsele reale cele mai apropiate de culoarea aleasă. Logica stă în core/appearance.ts.
import { useState } from 'react';
import type { Catalog, FurniturePlacement, Opening, Room, Snapshot, Wall } from '@/core/types';
import { PALETTES, MATERIAL_OPTIONS, MATERIAL_LABEL, DEFAULT_LOOK, normalizeHex, roomLook, wallFaceRooms, wallFaceColor, openingColor, itemSizeCm, similarVariants, SIZE_LIMITS_CM } from '@/core/appearance';
import { resolve } from '@/core/catalog';
import { formatMoney } from '@/core/format';

const T = {
  colors: 'Culori', walls: 'Pereți', floor: 'Nuanță pardoseală', ceiling: 'Tavan', reset: 'Implicit', allWalls: 'Aplică pereții în toată casa',
  wallSides: 'Culoarea fiecărei fețe (perete accent)', exterior: 'exterior', frame: 'Culoare ramă', height: 'Înălțime (cm)', sill: 'Parapet (cm)',
  look: 'Aspect', color: 'Culoare', material: 'Material', similar: 'Produse reale în culori apropiate', use: 'Folosește', current: 'actuală',
  custom: 'Dimensiune pe comandă', customNote: 'Piesa pe comandă nu are preț de catalog: bugetul o arată ca preț necunoscut până primești oferta.', customOff: 'Revino la dimensiunea din catalog',
  w: 'Lățime (cm)', d: 'Adâncime (cm)', h: 'Înălțime (cm)', unknownPrice: 'preț necunoscut',
};

export function ColorField({ label, value, isDefault, onChange, onReset }: { label: string; value: string; isDefault: boolean; onChange(hex: string): void; onReset(): void }){
  const [open, setOpen] = useState(false);
  return (<div className="colorfield">
    <div className="cfrow"><span className="cflabel">{label}</span>
      <input type="color" value={value} aria-label={label} onChange={e => { const h = normalizeHex(e.target.value); if (h) onChange(h); }} />
      <button className="btn" aria-expanded={open} onClick={() => setOpen(o => !o)}>Palete</button>
      {!isDefault && <button className="btn" onClick={onReset}>{T.reset}</button>}</div>
    {open && <div className="palettes">{PALETTES.map(p => <div key={p.id} className="palette"><small>{p.name.ro}</small>
      {p.colors.map(c => <button key={c} className="swatch" style={{ background: c }} aria-label={`${p.name.ro} ${c}`} aria-pressed={c === value} onClick={() => onChange(c)} />)}</div>)}</div>}
  </div>);
}

export function RoomLookPanel({ room, snap, onRoom, onAllWalls }: { room: Room; snap: Snapshot; onRoom(part: 'walls' | 'floor' | 'ceiling', hex: string | null): void; onAllWalls(hex: string): void }){
  const l = roomLook(snap, room.id), a = snap.appearance?.rooms?.[room.id];
  return (<><h4>{T.colors}</h4>
    <ColorField label={T.walls} value={l.walls} isDefault={!a?.walls} onChange={h => onRoom('walls', h)} onReset={() => onRoom('walls', null)} />
    <ColorField label={T.floor} value={l.floorTint} isDefault={!a?.floor} onChange={h => onRoom('floor', h)} onReset={() => onRoom('floor', null)} />
    <ColorField label={T.ceiling} value={l.ceiling} isDefault={!a?.ceiling} onChange={h => onRoom('ceiling', h)} onReset={() => onRoom('ceiling', null)} />
    <button className="btn" onClick={() => onAllWalls(l.walls)}>{T.allWalls}</button></>);
}

export function WallLookPanel({ wall, snap, onFace }: { wall: Wall; snap: Snapshot; onFace(roomId: string, hex: string | null): void }){
  const f = wallFaceRooms(snap.floor).find(x => x.wallId === wall.id), rooms = [f?.a, f?.b].filter((x): x is string => !!x);
  if (!rooms.length) return null;
  return (<><h4>{T.wallSides}</h4>{rooms.map(rid => { const r = snap.floor.rooms.find(x => x.id === rid)!, own = snap.appearance?.wallFaces?.[`${wall.id}@${rid}`];
    return <ColorField key={rid} label={r.name} value={wallFaceColor(snap, wall.id, rid)} isDefault={!own} onChange={h => onFace(rid, h)} onReset={() => onFace(rid, null)} />; })}</>);
}

export function OpeningLookPanel({ op, snap, num, onPatch, onFrame }: { op: Opening; snap: Snapshot; num: any; onPatch(p: Partial<Opening>): void; onFrame(hex: string | null): void }){
  const h = op.height ?? (op.kind === 'door' ? 2.1 : 1.3);
  return (<><div className="grid2">
    <label className="f"><span>{T.height}</span><input type="number" value={Math.round(h * 100)} onChange={e => num(e.target.value, (x: number) => onPatch({ height: Math.round(x) / 100 }), 30)} /></label>
    {op.kind === 'window' && <label className="f"><span>{T.sill}</span><input type="number" value={Math.round((op.sill ?? 0.9) * 100)} onChange={e => num(e.target.value, (x: number) => onPatch({ sill: Math.round(x) / 100 }), 0)} /></label>}
  </div>
  <ColorField label={T.frame} value={openingColor(snap, op.id)} isDefault={!snap.appearance?.openings?.[op.id]} onChange={h => onFrame(h)} onReset={() => onFrame(null)} /></>);
}

export function ItemLookPanel({ p, snap, catalog, model, num, onItem, onSize, onVariant }: { p: FurniturePlacement; snap: Snapshot; catalog: Catalog; model: string; num: any;
  onItem(patch: { color?: string | null; material?: string | null }): void; onSize(size: FurniturePlacement['size'] | null): void; onVariant(vid: string): void }){
  const rv = resolve(catalog, p.variantId), own = snap.appearance?.items?.[p.id], base = normalizeHex(rv?.variant.style?.col) ?? '#cccccc';
  const current = normalizeHex(own?.color) ?? base, mats = MATERIAL_OPTIONS[model] || [], size = itemSizeCm(catalog, p);
  const similar = similarVariants(catalog, p.group, current, 4);
  return (<><h4>{T.look}</h4>
    <ColorField label={T.color} value={current} isDefault={!own?.color} onChange={h => onItem({ color: h })} onReset={() => onItem({ color: null })} />
    {mats.length > 0 && <label className="f"><span>{T.material}</span><select value={own?.material ?? ''} onChange={e => onItem({ material: e.target.value || null })}>
      <option value="">— {T.current}</option>{mats.map(m => <option key={m} value={m}>{MATERIAL_LABEL[m]?.ro ?? m}</option>)}</select></label>}
    {similar.length > 0 && <><h4>{T.similar}</h4><div className="similar">{similar.map(v => <button key={v.variantId} className="var" aria-pressed={v.variantId === p.variantId} onClick={() => onVariant(v.variantId)}>
      <span><i className="dot" style={{ background: v.color }} /> {v.name}</span><span className="mono">{v.price != null && v.currency ? formatMoney(v.price, v.currency) : T.unknownPrice}</span><small>ΔE {v.distance}</small></button>)}</div></>}
    <h4>{T.custom}</h4>
    {size && <div className="grid2">{(['w', 'd', 'h'] as const).map(k => <label key={k} className="f"><span>{T[k]}</span><input type="number" min={SIZE_LIMITS_CM.min} max={SIZE_LIMITS_CM.max} value={size[k]}
      onChange={e => num(e.target.value, (x: number) => onSize({ ...size, [k]: Math.round(x) }), SIZE_LIMITS_CM.min)} /></label>)}</div>}
    {p.size ? <><p className="prov" style={{ margin: 0 }}>{T.customNote}</p><button className="btn" onClick={() => onSize(null)}>{T.customOff}</button></> : null}
  </>);
}
