// Panoul de stil al unei camere: materialele alese (cu mostră și preț), paleta de culori ponderată după suprafață
// (pereți, pardoseală, mobilier) și verificarea regulii 60-30-10 (dominantă / secundară / accent).
import type { Catalog, MaterialsCatalog, Room, Snapshot } from './types';
import { finishesOf, roomGeometry } from './boq';
import { materialOf } from './finishes';
import { itemStyle } from './appearance';
import { deriveStyle } from './product-style';
import { resolve, groups } from './catalog';
import { hexToRgb } from './color';

export const RATIO = { dominant: [0.5, 0.75], secondary: [0.15, 0.4], accent: [0.03, 0.2] } as const;
export interface BoardColor { hex: string; label: string; share: number; role: 'dominant' | 'secondary' | 'accent' }
export interface BoardMaterial { role: string; id: string; name: string; supplier: string; price: number | null; unit: string; category: string }
export interface BoardPiece { id: string; group: string; label: string; name: string; dims: { w: number; d: number; h: number } | null; price: number | null; hex: string | null }
export interface RoomBoard { roomId: string; name: string; type: string; materials: BoardMaterial[]; palette: BoardColor[]; ratio: { dominant: number; secondary: number; accent: number }; balanced: boolean; pieces: BoardPiece[] }

const dist = (a: string, b: string) => { const x = hexToRgb(a)!, y = hexToRgb(b)!; return Math.hypot(x.r - y.r, x.g - y.g, x.b - y.b); };

export function roomBoard(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, room: Room): RoomBoard {
  const f = finishesOf(snap, room), g = roomGeometry(snap, room), mats: BoardMaterial[] = [], areas: { hex: string; label: string; a: number }[] = [];
  const addMat = (role: string, id?: string | null) => { const m = materialOf(mc, id); if (!m) return null;
    mats.push({ role, id: m.id, name: m.name, supplier: m.supplier, price: m.unitPrice ?? null, unit: m.unit, category: m.category }); return m; };
  const wall = addMat('wall', f.wallPaint), floor = addMat('floor', f.floor); addMat('wallTile', f.wallTile); addMat('baseboard', f.baseboard);
  for (const w of f.wallFeatures ?? []) addMat('feature', (w as any).material);
  if (f.kitchen?.countertop) addMat('countertop', f.kitchen.countertop);
  if (f.rug && (f.rug as any).material) addMat('rug', (f.rug as any).material);
  for (const w of f.windows ?? []){ addMat('curtain', w.curtain); addMat('sheer', w.sheer); addMat('blind', w.blind); }
  const hexOf = (m: ReturnType<typeof materialOf> | null, fallback: string) => (m?.specs?.color && hexToRgb(m.specs.color) ? m.specs.color : fallback);
  areas.push({ hex: hexOf(wall, '#f2efe8'), label: wall?.name ?? 'pereți', a: g.wallNet }, { hex: hexOf(floor, '#d8c9ae'), label: floor?.name ?? 'pardoseală', a: g.floorArea });
  const gs = groups(cat), pieces: BoardPiece[] = [];
  for (const p of snap.placements.filter(x => x.roomId === room.id)){ const rv = resolve(cat, p.variantId); if (!rv) continue;
    const model = gs[p.group]?.model ?? '', st = itemStyle(snap, p, { ...deriveStyle(p.group, rv.variant.name), ...(rv.variant.style || {}) }, model), dm = p.size ?? rv.variant.dimensionsCm;
    const hex = typeof st.col === 'string' && hexToRgb(st.col) ? st.col : null;
    pieces.push({ id: p.id, group: p.group, label: gs[p.group]?.label ?? p.group, name: rv.variant.name, dims: dm ?? null, price: p.size ? null : rv.offer?.price ?? null, hex });
    if (hex && dm) areas.push({ hex, label: rv.product.name, a: (dm.w / 100) * (dm.h / 100) + (dm.w / 100) * (dm.d / 100) * .5 }); }
  // culorile apropiate se adună (aceeași familie), apoi rolurile după pondere
  const merged: { hex: string; label: string; a: number }[] = [];
  for (const c of areas.sort((a, b) => b.a - a.a)){ const m = merged.find(x => dist(x.hex, c.hex) < 38); if (m) m.a += c.a; else merged.push({ ...c }); }
  const total = merged.reduce((s, c) => s + c.a, 0) || 1;
  const palette: BoardColor[] = merged.map((c, i) => ({ hex: c.hex, label: c.label, share: c.a / total, role: i === 0 ? 'dominant' : 'secondary' }));
  let cum = palette[0]?.share ?? 0; for (const c of palette.slice(1)){ if (cum >= .9 || c.share < .06) c.role = 'accent'; cum += c.share; }
  const sum = (r: BoardColor['role']) => palette.filter(c => c.role === r).reduce((s, c) => s + c.share, 0);
  const ratio = { dominant: sum('dominant'), secondary: sum('secondary'), accent: sum('accent') };
  const inR = (v: number, [lo, hi]: readonly [number, number]) => v >= lo && v <= hi;
  return { roomId: room.id, name: room.name, type: room.type, materials: mats, palette, ratio, balanced: inR(ratio.dominant, RATIO.dominant) && inR(ratio.secondary, RATIO.secondary) && inR(ratio.accent, RATIO.accent), pieces };
}
