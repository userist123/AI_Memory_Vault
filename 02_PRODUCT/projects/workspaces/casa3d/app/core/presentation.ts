// Prezentarea pentru client: ce se arată pe fiecare planșă (mostrele de finisaj ale camerei, piesele principale, moodboard-ul
// casei, cifrele de buget). Doar date; desenul e în components/PresentView.tsx.
import type { Catalog, Material, MaterialsCatalog, Room, Snapshot } from './types';
import { finishesOf, computeBudget } from './boq';
import { floors } from './levels';
import { materialOf, layoutOf, ceilingOf } from './finishes';
import { roomLook } from './appearance';
import { resolve, pricedOffer } from './catalog';
import { area } from './geometry';

export interface Swatch { role: string; material: Material | null; color: string; note?: string; /** cheie stabilă în cameră, pentru aprobarea clientului */ key: string }
export interface RoomSlide { id: string; name: string; type: string; level: number; areaM2: number; swatches: Swatch[]; products: { id: string; name: string; variant: string; price: number | null; currency: string | null }[] }
export interface BoardGroup { role: 'floors' | 'walls' | 'textiles' | 'fittings' | 'lighting'; items: { material: Material | null; color: string }[] }

/** Mostrele camerei, în ordinea în care le citește clientul: pardoseală, pereți, accente, tavan, lumină, textile, obiecte. */
export function roomSwatches(snap: Snapshot, mc: MaterialsCatalog, room: Room): Swatch[] {
  const f = finishesOf(snap, room), out: Swatch[] = [], add = (role: string, id: string | null | undefined, color?: string, note?: string) => {
    const m = materialOf(mc, id) ?? null; if (!m && !color) return; const c = color ?? m?.specs?.color ?? '#e2ddd4'; out.push({ role, material: m, color: c, key: `${role}:${m?.id ?? c}`.slice(0, 120), ...(note ? { note } : {}) }); };
  const fm = materialOf(mc, f.floor); add('floor', f.floor, undefined, layoutOf(f, fm).pattern);
  add('walls', f.wallPaint, roomLook(snap, room.id).walls);
  for (const w of f.wallFeatures || []) add(`accent.${w.kind}`, w.material, w.color);
  if (f.wallTile && (room.type === 'baie' || room.type === 'bucatarie')) add('walltile', f.wallTile);
  const c = ceilingOf(f); if (c.cornice) add('cornice', c.cornice);
  add('light', f.light);
  for (const fx of f.fixtures || []) add(`fixture.${fx.kind}`, fx.material, fx.material ? undefined : fx.color);
  for (const w of f.windows || []){ add('curtain', w.curtain); add('sheer', w.sheer); add('blind', w.blind); }
  add('rug', f.rug?.material);
  if (f.kitchen){ add('countertop', f.kitchen.countertop, f.kitchen.countertop ? undefined : f.kitchen.countertopColor); add('sink', f.kitchen.sink); add('tap', f.kitchen.tap); }
  if (f.bath){ add('bathTap', f.bath.tap); add('shower', f.bath.shower); add('mirror', f.bath.mirror); }
  // aceeași mostră o singură dată (ex. aceeași draperie la două ferestre)
  return out.filter((s, i) => out.findIndex(o => o.role === s.role && o.material?.id === s.material?.id && o.color === s.color) === i);
}
export function presentation(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog){
  const rooms: RoomSlide[] = floors(snap).flatMap((fl, level) => fl.rooms.map(r => ({ id: r.id, name: r.name, type: r.type, level, areaM2: Math.round(area(r.rect) * 10) / 10,
    swatches: roomSwatches(snap, mc, r),
    products: snap.placements.filter(p => p.roomId === r.id).map(p => { const rv = resolve(cat, p.variantId), o = pricedOffer(cat, p);
      return { id: p.id, name: rv?.product.name ?? p.group, variant: rv?.variant.name ?? '', price: o?.price ?? null, currency: o?.currency ?? null }; }) })));
  const ROLE: Record<string, BoardGroup['role']> = { floor: 'floors', walls: 'walls', walltile: 'walls', cornice: 'walls', curtain: 'textiles', sheer: 'textiles', blind: 'textiles', rug: 'textiles',
    countertop: 'fittings', sink: 'fittings', tap: 'fittings', bathTap: 'fittings', shower: 'fittings', mirror: 'fittings', light: 'lighting' };
  const board: BoardGroup[] = (['floors', 'walls', 'textiles', 'fittings', 'lighting'] as const).map(role => ({ role, items: [] as BoardGroup['items'] }));
  for (const s of rooms.flatMap(r => r.swatches)){ const role = ROLE[s.role] ?? (s.role.startsWith('accent.') ? 'walls' : s.role.startsWith('fixture.') ? 'lighting' : null); if (!role) continue;
    const g = board.find(b => b.role === role)!; if (!g.items.some(i => i.material?.id === s.material?.id && i.color === s.color)) g.items.push({ material: s.material, color: s.color }); }
  const b = computeBudget(snap, cat, mc);
  // paleta casei: culorile distincte ale tuturor grupurilor, în ordinea pardoseală, pereți, textile, dotări, lumină
  const palette: BoardGroup['items'] = []; for (const g of board) for (const it of g.items) if (!palette.some(p => p.color.toLowerCase() === it.color.toLowerCase())) palette.push(it);
  return { rooms, board: board.filter(g => g.items.length), palette: palette.slice(0, 8), budget: { total: b.chosen.total, categories: b.categories, labor: b.chosen.labor, contingency: b.chosen.contingency, unknown: b.unknownItems.length + b.unknownLines.length },
    totalArea: Math.round(rooms.reduce((a, r) => a + r.areaM2, 0) * 10) / 10 };
}

/** Ce poate aproba clientul într-o cameră: fiecare finisaj (f:<cheie>) și fiecare piesă (p:<id>), cu numele afișat. */
export function approvalItems(slide: RoomSlide): { key: string; label: string; role: string | null }[] {
  return [...slide.swatches.map(s => ({ key: `f:${s.key}`, label: s.material ? s.material.name.split(',')[0]! : s.color, role: s.role })),
    ...slide.products.map(p => ({ key: `p:${p.id}`.slice(0, 120), label: !p.variant ? p.name : p.variant.toLowerCase().startsWith(p.name.toLowerCase()) ? p.variant : `${p.name} · ${p.variant}`, role: null }))];
}
