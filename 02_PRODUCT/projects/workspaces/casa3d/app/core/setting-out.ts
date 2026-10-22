// Planul de placare (setting-out) pentru gresie și faianță: pe fiecare suprafață se alege de unde pornește placarea
// (de la colț, cu rost la mijloc sau cu placa centrată) așa încât tăieturile de la capete să nu fie fâșii subțiri
// (sub o treime de placă arată rău și se sparg la tăiere), apoi se numără plăcile reale (întregi + tăiate).
import type { Catalog, Floor, MaterialsCatalog, Room, Snapshot } from './types';
import { materialOf, pieceSizeCm, layoutOf, sideGeometry, bathTiles } from './finishes';
import { finishesOf } from './boq';
import { kitchenOf, kitchenRun } from './kitchen';
import { floors } from './levels';
import { BACKSPLASH_HEIGHT } from './rules.boq';

export type StartRule = 'edge' | 'centerJoint' | 'centerTile' | 'top';
export interface AxisPlan { start: StartRule; pieces: number; full: number; cuts: number[]; minCut: number | null; sliver: boolean }
const EPS = 1e-4, r3 = (v: number) => Math.round(v * 1000) / 1000;

/** Bucățile de-a lungul unei axe, cu muchia unei plăci la `s` (în (-pas, 0]); rosturile intră între plăci. */
function piecesAt(L: number, T: number, g: number, s: number): number[] {
  const p = T + g, out: number[] = []; for (let x = s; x < L - EPS; x += p){ const a = Math.max(0, x), b = Math.min(L, x + T); if (b - a > EPS) out.push(r3(b - a)); } return out;
}
const norm = (s: number, p: number) => { let v = s % p; if (v > EPS) v -= p; return v; };
/** Cea mai bună pornire pe o axă: cea cu cea mai mare tăietură minimă (fără fâșii), apoi cu mai puține bucăți. */
export function axisPlan(L: number, T: number, g: number, options: StartRule[] = ['edge', 'centerJoint', 'centerTile']): AxisPlan {
  const p = T + g, starts: Record<StartRule, number> = { edge: 0, centerJoint: norm(L / 2 + g / 2, p), centerTile: norm(L / 2 - T / 2, p), top: norm(L - T, p) };
  let best: AxisPlan | null = null;
  for (const st of options){ const ps = piecesAt(L, T, g, starts[st]), cuts = ps.filter(x => x < T - 1e-3), minCut = cuts.length ? Math.min(...cuts) : null;
    const plan: AxisPlan = { start: st, pieces: ps.length, full: ps.length - cuts.length, cuts, minCut, sliver: minCut != null && minCut < T / 3 };
    const score = (a: AxisPlan) => (a.minCut ?? T) * 1000 - a.pieces;
    if (!best || score(plan) > score(best) + 1e-9) best = plan; }
  return best!;
}
export interface SurfacePlan { roomId: string; room: string; surface: 'floor' | 'N' | 'S' | 'W' | 'E' | 'backsplash'; material: string; tileCm: [number, number]; groutMm: number;
  widthM: number; heightM: number; across: AxisPlan; along: AxisPlan; tiles: number; sliver: boolean;
  pattern?: import('./types').FloorPattern; netAreaM2?: number; grossAreaM2?: number; wastePct?: number }

/** Planurile de placare pentru toate camerele cu gresie (așezare dreaptă / tablă de șah) sau faianță. */
export function settingOut(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog): SurfacePlan[] {
  const out: SurfacePlan[] = [];
  floors(snap).forEach(fl => { for (const room of fl.rooms) out.push(...roomPlans(snap, cat, mc, fl, room)); });
  return out;
}
function plan(room: Room, surface: SurfacePlan['surface'], material: string, tileCm: [number, number], groutMm: number, W: number, H: number, vertical: boolean): SurfacePlan {
  const T1 = tileCm[0] / 100, T2 = tileCm[1] / 100, g = groutMm / 1000;
  const across = axisPlan(W, T1, g), along = vertical ? axisPlan(H, T2, g, ['edge', 'top']) : axisPlan(H, T2, g);
  return { roomId: room.id, room: room.name, surface, material, tileCm, groutMm, widthM: r3(W), heightM: r3(H), across, along, tiles: across.pieces * along.pieces, sliver: across.sliver || along.sliver };
}
function roomPlans(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, fl: Floor, room: Room): SurfacePlan[] {
  const f = finishesOf(snap, room), out: SurfacePlan[] = [], W = room.rect.x1 - room.rect.x0, D = room.rect.z1 - room.rect.z0;
  const fm = materialOf(mc, f.floor);
  if (fm?.category === 'floor_tile'){ const lay = layoutOf(f, fm); if (lay.pattern === 'straight' || lay.pattern === 'checker'){
    const [a, b] = pieceSizeCm(fm), rot = lay.angle === 90; out.push(plan(room, 'floor', fm.name, rot ? [b, a] : [a, b], lay.groutMm ?? 3, W, D, false)); } }
  const wt = materialOf(mc, f.wallTile); if (!wt) return out;
  const [a, b] = pieceSizeCm(wt), tile: [number, number] = [a, b], grout = wt.specs?.rectified ? 2 : 3;
  if (room.type === 'baie') for (const w of bathTiles(f, room.type)){ const g = sideGeometry(fl, room, w.side, w.heightM); if (g.lengthM > .05) out.push(plan(room, w.side, wt.name, tile, grout, g.lengthM, g.heightM, true)); }
  if (room.type === 'bucatarie' && (!f.kitchen || kitchenOf(f).backsplash === 'tile')){ const run = kitchenRun(snap, cat, room.id); if (run) out.push(plan(room, 'backsplash', wt.name, tile, grout, run.lengthM, BACKSPLASH_HEIGHT, true)); }
  return out;
}

const PATTERN_WASTE_PCT: Record<import('./types').FloorPattern, number> = {
  straight: 10,
  checker: 10,
  brick: 12,
  third: 12,
  diagonal: 15,
  herringbone: 15,
  chevron: 15,
};

/**
 * Calculează planul de placare cu origine explicită (`originCm`), model (`pattern`: grid/straight, brick/staggered, herringbone etc.)
 * și pierdere estimată configurabilă (`wastePctOverride`), returnând tăieturile la margini, aria netă, aria brută și numărul de plăci.
 */
export function customSurfaceSettingOut(opts: {
  roomId: string;
  roomName: string;
  surface: SurfacePlan['surface'];
  material: string;
  widthM: number;
  heightM: number;
  tileCm: [number, number];
  groutMm?: number;
  pattern?: import('./types').FloorPattern;
  originCm?: [number, number];
  wastePctOverride?: number;
}): SurfacePlan {
  const groutMm = opts.groutMm ?? 3;
  const pattern = opts.pattern ?? 'straight';
  const T1 = opts.tileCm[0] / 100, T2 = opts.tileCm[1] / 100, g = groutMm / 1000;
  const across = opts.originCm
    ? (() => {
        const s = norm(-(opts.originCm[0] / 100), T1 + g);
        const ps = piecesAt(opts.widthM, T1, g, s), cuts = ps.filter(x => x < T1 - 1e-3), minCut = cuts.length ? Math.min(...cuts) : null;
        return { start: 'edge' as StartRule, pieces: ps.length, full: ps.length - cuts.length, cuts, minCut, sliver: minCut != null && minCut < T1 / 3 };
      })()
    : axisPlan(opts.widthM, T1, g);
  const along = opts.originCm
    ? (() => {
        const s = norm(-(opts.originCm[1] / 100), T2 + g);
        const ps = piecesAt(opts.heightM, T2, g, s), cuts = ps.filter(x => x < T2 - 1e-3), minCut = cuts.length ? Math.min(...cuts) : null;
        return { start: 'edge' as StartRule, pieces: ps.length, full: ps.length - cuts.length, cuts, minCut, sliver: minCut != null && minCut < T2 / 3 };
      })()
    : axisPlan(opts.heightM, T2, g);
  const netAreaM2 = r3(opts.widthM * opts.heightM);
  const wastePct = opts.wastePctOverride ?? PATTERN_WASTE_PCT[pattern] ?? 10;
  const grossAreaM2 = r3(netAreaM2 * (1 + wastePct / 100));
  const tileAreaM2 = T1 * T2;
  const tiles = Math.max(across.pieces * along.pieces, Math.ceil(grossAreaM2 / Math.max(tileAreaM2, 1e-6)));
  return {
    roomId: opts.roomId,
    room: opts.roomName,
    surface: opts.surface,
    material: opts.material,
    tileCm: opts.tileCm,
    groutMm,
    widthM: r3(opts.widthM),
    heightM: r3(opts.heightM),
    across,
    along,
    tiles,
    sliver: across.sliver || along.sliver,
    pattern,
    netAreaM2,
    grossAreaM2,
    wastePct,
  };
}

