// Planul se folosește și de la tastatură (audit UI F-26): Tab / Shift+Tab trec prin camere, piese și scări în ordinea
// citirii (sus → jos, stânga → dreapta), săgețile mută elementul ales, R îl rotește, Delete îl șterge, Escape renunță.
// Aici e doar logica, fără DOM: ce element urmează și ce acțiune produce o tastă. PlanView și editorul o aplică.
import type { Snapshot } from './types';

export type PlanItemKind = 'room' | 'placement' | 'stair' | 'column' | 'beam' | 'glass_wall';
export interface PlanItem { kind: PlanItemKind; id: string; x: number; z: number }
export type PlanSel = { kind: string; id: string } | null;

/** Elementele planului care se pot alege cu Tab: camerele, apoi piesele, scările și elementele structurale, fiecare grup în ordinea citirii. */
export function planItems(snap: Pick<Snapshot, 'floor' | 'placements'>): PlanItem[] {
  const order = (a: PlanItem, b: PlanItem) => (Math.abs(a.z - b.z) > .05 ? a.z - b.z : a.x - b.x) || a.id.localeCompare(b.id);
  const rooms = snap.floor.rooms.map(r => ({ kind: 'room' as const, id: r.id, x: r.rect.x0, z: r.rect.z0 })).sort(order);
  const pieces = snap.placements.map(p => ({ kind: 'placement' as const, id: p.id, x: p.x, z: p.z })).sort(order);
  const stairs = (snap.floor.stairs ?? []).map(s => ({ kind: 'stair' as const, id: s.id, x: s.x, z: s.z })).sort(order);
  const columns = (snap.floor.columns ?? []).map(c => ({ kind: 'column' as const, id: c.id, x: c.x, z: c.z })).sort(order);
  const beams = (snap.floor.beams ?? []).map(b => ({ kind: 'beam' as const, id: b.id, x: (b.a[0] + b.b[0]) / 2, z: (b.a[1] + b.b[1]) / 2 })).sort(order);
  const glassWalls = (snap.floor.glassWalls ?? []).map(g => ({ kind: 'glass_wall' as const, id: g.id, x: (g.a[0] + g.b[0]) / 2, z: (g.a[1] + g.b[1]) / 2 })).sort(order);
  return [...rooms, ...pieces, ...stairs, ...columns, ...beams, ...glassWalls];
}

/** Elementul următor (dir = 1) sau anterior (dir = −1). `null` = Tab iese din plan (după ultimul sau înainte de primul). */
export function cycleSelection(items: PlanItem[], sel: PlanSel, dir: 1 | -1): PlanItem | null {
  if (!items.length) return null;
  const i = sel ? items.findIndex(x => x.kind === sel.kind && x.id === sel.id) : -1;
  if (i < 0) return dir > 0 ? items[0]! : items[items.length - 1]!;
  const j = i + dir; return j < 0 || j >= items.length ? null : items[j]!;
}

export type PlanKeyAction =
  | { type: 'select'; item: PlanItem } | { type: 'leave' } | { type: 'deselect' }
  | { type: 'move'; dxCm: number; dzCm: number } | { type: 'rotate' } | { type: 'delete' }
  | { type: 'duplicate' } | { type: 'toggleShape' } | null;
export interface KeyIn { key: string; shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean; altKey?: boolean }
export const MOVE_CM = 5, MOVE_BIG_CM = 50;

/**
 * Ce face o tastă apăsată cu planul în focus. Tab alege elementul următor (Shift+Tab, cel anterior); după ultimul, Tab
 * iese din plan ca să nu prindă tastatura. Săgețile mută cu 5 cm (cu Shift, 50 cm), R rotește, Delete / Backspace șterge,
 * Ctrl+D duplică piesa aleasă.
 */
export function planKeyAction(e: KeyIn, items: PlanItem[], sel: PlanSel): PlanKeyAction {
  if (e.altKey) return null;
  if ((e.ctrlKey || e.metaKey) && (e.key === 'd' || e.key === 'D')) {
    return sel && sel.kind === 'placement' && items.some(x => x.kind === sel.kind && x.id === sel.id) ? { type: 'duplicate' } : null;
  }
  if (e.ctrlKey || e.metaKey) return null;
  if (e.key === 'Tab'){ const n = cycleSelection(items, sel, e.shiftKey ? -1 : 1); return n ? { type: 'select', item: n } : { type: 'leave' }; }
  if (e.key === 'Escape') return sel ? { type: 'deselect' } : null;
  if (!sel || !items.some(x => x.kind === sel.kind && x.id === sel.id)) return null;
  if (e.key.startsWith('Arrow')){ const s = e.shiftKey ? MOVE_BIG_CM : MOVE_CM;
    return { type: 'move', dxCm: e.key === 'ArrowLeft' ? -s : e.key === 'ArrowRight' ? s : 0, dzCm: e.key === 'ArrowUp' ? -s : e.key === 'ArrowDown' ? s : 0 }; }
  if (e.key === 'r' || e.key === 'R') return sel.kind === 'room' ? null : { type: 'rotate' };
  if (e.key === 'Delete' || e.key === 'Backspace') return { type: 'delete' };
  return null;
}
