// Încadrarea unui obiect în plan (UI/UX P2-12): clic pe o problemă din „Sănătate” sau tasta F → planul se mută și se
// apropie astfel încât obiectul selectat să fie în centru, cu margine. Funcții pure; PlanView doar aplică viewBox-ul.
import type { Catalog, Snapshot } from './types';
import { footprintOf } from './validate';
import { nicheGeometry } from './niches';
import { annotationBox } from './annotations';

export type Box = { x0: number; z0: number; x1: number; z1: number };
export type FrameSel = { kind: string; id: string; wallId?: string } | null;

const seg = (a: [number, number], b: [number, number], t: number): Box =>
  ({ x0: Math.min(a[0], b[0]) - t / 2, x1: Math.max(a[0], b[0]) + t / 2, z0: Math.min(a[1], b[1]) - t / 2, z1: Math.max(a[1], b[1]) + t / 2 });

/** Conturul obiectului selectat, în metri (planul nivelului din `snap.floor`); null dacă nu există. */
export function selectionBox(snap: Snapshot, cat: Catalog, sel: FrameSel): Box | null {
  if (!sel) return null; const f = snap.floor;
  switch (sel.kind){
    case 'room': { const r = f.rooms.find(x => x.id === sel.id); return r ? { ...r.rect } : null; }
    case 'placement': { const p = snap.placements.find(x => x.id === sel.id); return p ? footprintOf(cat, p) : null; }
    case 'wall': { const w = f.walls.find(x => x.id === sel.id); return w ? seg(w.a, w.b, w.thickness) : null; }
    case 'opening': { const w = f.walls.find(x => x.id === sel.wallId); return w ? seg(w.a, w.b, w.thickness) : null; }
    case 'beam': { const b = f.beams?.find(x => x.id === sel.id); return b ? seg(b.a, b.b, b.width) : null; }
    case 'glass_wall': { const g = f.glassWalls?.find(x => x.id === sel.id); return g ? seg(g.a, g.b, g.thickness) : null; }
    case 'column': { const c = f.columns?.find(x => x.id === sel.id); return c ? { x0: c.x - c.w / 2, x1: c.x + c.w / 2, z0: c.z - c.d / 2, z1: c.z + c.d / 2 } : null; }
    case 'niche': { const n = f.niches?.find(x => x.id === sel.id), g = n && nicheGeometry(f, n); if (!g) return null; const xs = g.quad.map(p => p[0]), zs = g.quad.map(p => p[1]); return { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) }; }
    case 'annotation': { const a = f.annotations?.find(x => x.id === sel.id); return a ? annotationBox(a) : null; }
    case 'stair': { const s = f.stairs?.find(x => x.id === sel.id); if (!s) return null; const r = Math.hypot(s.width, s.length) / 2; return { x0: s.x - r, x1: s.x + r, z0: s.z - r, z1: s.z + r }; }
    default: return null;
  }
}
/**
 * viewBox-ul care arată `box` în centru: margine de jumătate din latura mare (cel puțin 1 m), cel puțin 3 m în cadru
 * (o priză sau un stâlp nu umple ecranul), la proporția ferestrei planului (`aspect` = lățime / înălțime).
 */
export function frameViewBox(box: Box, aspect: number): { x: number; y: number; w: number; h: number } {
  const a = Number.isFinite(aspect) && aspect > 0 ? aspect : 1, cx = (box.x0 + box.x1) / 2, cy = (box.z0 + box.z1) / 2;
  const bw = Math.max(0, box.x1 - box.x0), bh = Math.max(0, box.z1 - box.z0), pad = Math.max(1, Math.max(bw, bh) / 2);
  let w = Math.max(3, bw + pad * 2), h = Math.max(3, bh + pad * 2);
  if (w / h < a) w = h * a; else h = w / a;
  return { x: cx - w / 2, y: cy - h / 2, w, h };
}
