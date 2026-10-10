'use client';
// Controale de aspect: culori (palete + selector liber), materiale, perete accent, goluri, dimensiuni pe comandă,
// plus produsele reale cele mai apropiate de culoarea aleasă. Logica stă în core/appearance.ts.
import { useState } from 'react';
import type { Catalog, FurniturePlacement, Opening, Room, Snapshot, Wall } from '@/core/types';
import { PALETTES, MATERIAL_OPTIONS, MATERIAL_LABEL, DEFAULT_LOOK, normalizeHex, roomLook, wallFaceRooms, wallFaceColor, openingColor, itemSizeCm, similarVariants, SIZE_LIMITS_CM } from '@/core/appearance';
import { resolve } from '@/core/catalog';
import { formatMoney, cmToInput, lengthInputUnit } from '@/core/format';
import { usePrefs } from '@/lib/prefs';

const SIZE_KEY = { w: 'look.w', d: 'look.d', h: 'look.h' } as const;

export function ColorField({ label, value, isDefault, onChange, onReset }: { label: string; value: string; isDefault: boolean; onChange(hex: string): void; onReset(): void }){
  const [open, setOpen] = useState(false), { t, lang } = usePrefs();
  return (<div className="colorfield">
    <div className="cfrow"><span className="cflabel">{label}</span>
      <input type="color" value={value} aria-label={label} onChange={e => { const h = normalizeHex(e.target.value); if (h) onChange(h); }} />
      <button className="btn" aria-expanded={open} onClick={() => setOpen(o => !o)}>{t('look.palettes')}</button>
      {!isDefault && <button className="btn" onClick={onReset}>{t('look.reset')}</button>}</div>
    {open && <div className="palettes">{PALETTES.map(p => <div key={p.id} className="palette"><small>{p.name[lang as 'ro' | 'en'] ?? p.name.ro}</small>
      {p.colors.map(c => <button key={c} className="swatch" style={{ background: c }} aria-label={`${p.name[lang as 'ro' | 'en'] ?? p.name.ro} ${c}`} aria-pressed={c === value} onClick={() => onChange(c)} />)}</div>)}</div>}
  </div>);
}

export function RoomLookPanel({ room, snap, onRoom, onAllWalls }: { room: Room; snap: Snapshot; onRoom(part: 'walls' | 'floor' | 'ceiling', hex: string | null): void; onAllWalls(hex: string): void }){
  const { t } = usePrefs(), l = roomLook(snap, room.id), a = snap.appearance?.rooms?.[room.id];
  return (<><h4>{t('look.colors')}</h4>
    <ColorField label={t('look.walls')} value={l.walls} isDefault={!a?.walls} onChange={h => onRoom('walls', h)} onReset={() => onRoom('walls', null)} />
    <ColorField label={t('look.floor')} value={l.floorTint} isDefault={!a?.floor} onChange={h => onRoom('floor', h)} onReset={() => onRoom('floor', null)} />
    <ColorField label={t('look.ceiling')} value={l.ceiling} isDefault={!a?.ceiling} onChange={h => onRoom('ceiling', h)} onReset={() => onRoom('ceiling', null)} />
    <button className="btn" onClick={() => onAllWalls(l.walls)}>{t('look.allWalls')}</button></>);
}

export function WallLookPanel({ wall, snap, onFace }: { wall: Wall; snap: Snapshot; onFace(roomId: string, hex: string | null): void }){
  const { t } = usePrefs(), f = wallFaceRooms(snap.floor).find(x => x.wallId === wall.id), rooms = [...new Set([...(f?.a || []), ...(f?.b || [])])];
  if (!rooms.length) return null;
  return (<><h4>{t('look.wallSides')}</h4>{rooms.map(rid => { const r = snap.floor.rooms.find(x => x.id === rid)!, own = snap.appearance?.wallFaces?.[`${wall.id}@${rid}`];
    return <ColorField key={rid} label={r.name} value={wallFaceColor(snap, wall.id, rid)} isDefault={!own} onChange={h => onFace(rid, h)} onReset={() => onFace(rid, null)} />; })}</>);
}

export function OpeningLookPanel({ op, snap, num, onPatch, onFrame }: { op: Opening; snap: Snapshot; num: any; onPatch(p: Partial<Opening>): void; onFrame(hex: string | null): void }){
  // golul rămâne sub tavan: înălțimea și parapetul se limitează la spațiul disponibil (serverul refuză altfel)
  const { t, units } = usePrefs(), u = lengthInputUnit(units), h = op.height ?? (op.kind === 'door' ? 2.1 : 1.3), sill = op.kind === 'window' ? op.sill ?? 0.9 : 0, ceil = snap.floor.ceilingHeight || 2.6;
  const r2 = (x: number) => Math.round(x * 100) / 100;
  return (<><div className="grid2">
    <label className="f"><span>{t('look.height', { u })}</span><input type="number" value={cmToInput(h * 100, units)} onChange={e => num(e.target.value, (x: number) => onPatch({ height: r2(Math.min(x / 100, ceil - sill)) }), 30)} /></label>
    {op.kind === 'window' && <label className="f"><span>{t('look.sill', { u })}</span><input type="number" value={cmToInput(sill * 100, units)} onChange={e => num(e.target.value, (x: number) => onPatch({ sill: r2(Math.max(0, Math.min(x / 100, ceil - h))) }), 0)} /></label>}
  </div>
  <ColorField label={t('look.frame')} value={openingColor(snap, op.id)} isDefault={!snap.appearance?.openings?.[op.id]} onChange={h => onFrame(h)} onReset={() => onFrame(null)} /></>);
}

export function ItemLookPanel({ p, snap, catalog, model, num, onItem, onSize, onVariant }: { p: FurniturePlacement; snap: Snapshot; catalog: Catalog; model: string; num: any;
  onItem(patch: { color?: string | null; material?: string | null }): void; onSize(size: FurniturePlacement['size'] | null): void; onVariant(vid: string): void }){
  const { t, lang, units } = usePrefs(), u = lengthInputUnit(units), rv = resolve(catalog, p.variantId), own = snap.appearance?.items?.[p.id], base = normalizeHex(rv?.variant.style?.col) ?? '#cccccc';
  const current = normalizeHex(own?.color) ?? base, mats = MATERIAL_OPTIONS[model] || [], size = itemSizeCm(catalog, p);
  const similar = similarVariants(catalog, p.group, current, 4);
  return (<><h4>{t('look.look')}</h4>
    <ColorField label={t('look.color')} value={current} isDefault={!own?.color} onChange={h => onItem({ color: h })} onReset={() => onItem({ color: null })} />
    {mats.length > 0 && <label className="f"><span>{t('look.material')}</span><select value={own?.material ?? ''} onChange={e => onItem({ material: e.target.value || null })}>
      <option value="">— {t('look.current')}</option>{mats.map(m => <option key={m} value={m}>{MATERIAL_LABEL[m]?.[lang as 'ro' | 'en'] ?? MATERIAL_LABEL[m]?.ro ?? m}</option>)}</select></label>}
    {similar.length > 0 && <><h4>{t('look.similar')}</h4><div className="similar">{similar.map(v => <button key={v.variantId} className="var" aria-pressed={v.variantId === p.variantId} onClick={() => onVariant(v.variantId)}>
      <span><i className="dot" style={{ background: v.color }} /> {v.name}</span><span className="mono">{v.price != null && v.currency ? formatMoney(v.price, v.currency, lang) : t('look.unknownPrice')}</span><small>ΔE {v.distance}</small></button>)}</div></>}
    <h4>{t('look.custom')}</h4>
    {size && <div className="grid2">{(['w', 'd', 'h'] as const).map(k => <label key={k} className="f"><span>{t(SIZE_KEY[k], { u })}</span><input type="number" min={cmToInput(SIZE_LIMITS_CM.min, units)} max={cmToInput(SIZE_LIMITS_CM.max, units)} value={cmToInput(size[k], units)}
      onChange={e => num(e.target.value, (x: number) => onSize({ ...size, [k]: Math.round(x) }), SIZE_LIMITS_CM.min)} /></label>)}</div>}
    {p.size ? <><p className="prov" style={{ margin: 0 }}>{t('look.customNote')}</p><button className="btn" onClick={() => onSize(null)}>{t('look.customOff')}</button></> : null}
  </>);
}
