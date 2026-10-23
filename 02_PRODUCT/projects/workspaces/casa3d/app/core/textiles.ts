// Textile: draperii, perdele, storuri și covoare, cu regulile de dimensionare din cercetarea de finisaje
// (FINISHES_RESEARCH_2026-10-10): bara depășește fereastra cu ~20 cm pe fiecare parte, încrețirea e 1,5 / 2 / 2,5 × lățimea barei,
// draperia cade până la ~1 cm de pardoseală de la bara pusă ~15 cm deasupra golului; covorul lasă cel puțin 20 cm de perete.
import type { Floor, Material, MaterialsCatalog, Room, RoomFinishes, WindowTreatment } from './types';
import { materialOf } from './finishes';

const r2 = (v: number) => Math.round(v * 100) / 100;
export const TEXTILE_RULES = { rodOverhangM: .2, rodAboveM: .15, floorClearM: .01, fullness: [1.5, 2, 2.5] as const, defaultFullness: 2, rugWallGapM: .2, blindTolM: .1 };
export interface RoomWindow { openingId: string; wallIndex: number; openingIndex: number; side: 'N' | 'S' | 'W' | 'E'; widthM: number; sillM: number; topM: number; inward: 1 | -1 }

/** Ferestrele de pe pereții camerei, cu partea dinspre cameră (normala peretelui (-uz, ux) înmulțită cu `inward`). */
export function roomWindows(fl: Floor, room: Room): RoomWindow[] {
  const r = room.rect, cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, out: RoomWindow[] = [];
  fl.walls.forEach((w, wi) => { const horiz = Math.abs(w.a[1] - w.b[1]) < 1e-6, vert = Math.abs(w.a[0] - w.b[0]) < 1e-6; if (!horiz && !vert) return;
    const side = horiz ? (Math.abs(w.a[1] - r.z0) < 1e-6 ? 'N' : Math.abs(w.a[1] - r.z1) < 1e-6 ? 'S' : null) : (Math.abs(w.a[0] - r.x0) < 1e-6 ? 'W' : Math.abs(w.a[0] - r.x1) < 1e-6 ? 'E' : null);
    if (!side) return; const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    const inward: 1 | -1 = ((cx - w.a[0]) * -uz + (cz - w.a[1]) * ux) > 0 ? 1 : -1;
    w.openings.forEach((o, oi) => { if (o.kind !== 'window') return; const m = o.offset + o.width / 2, px = w.a[0] + ux * m, pz = w.a[1] + uz * m;
      if (px < r.x0 - 1e-6 || px > r.x1 + 1e-6 || pz < r.z0 - 1e-6 || pz > r.z1 + 1e-6) return;
      const sill = o.sill ?? .9; out.push({ openingId: o.id, wallIndex: wi, openingIndex: oi, side, widthM: o.width, sillM: sill, topM: sill + (o.height ?? 1.3), inward }); }); });
  return out;
}
const panelSize = (m: Material | undefined): [number, number] => { const s = m?.specs?.sizeCm ?? [145, 250]; return [s[0] / 100, s[1] / 100]; };
/** Draperie sau perdea: lățimea barei, câte panouri și câte pachete, căderea și cât se tivește. */
export function curtainPlan(win: RoomWindow, H: number, m: Material | undefined, fullness = TEXTILE_RULES.defaultFullness){
  const [pw, pl] = panelSize(m), rodW = win.widthM + 2 * TEXTILE_RULES.rodOverhangM, rodH = Math.min(H - .03, win.topM + TEXTILE_RULES.rodAboveM), drop = r2(rodH - TEXTILE_RULES.floorClearM);
  const panels = Math.max(2, Math.ceil(rodW * fullness / pw - 1e-9)), packs = Math.ceil(panels / (m?.specs?.pieces ?? 2));
  return { rodW: r2(rodW), rodH: r2(rodH), drop, panels, packs, short: pl < drop - 1e-6, hemM: r2(Math.max(0, pl - drop)) };
}
/** Storuri pe fereastră: câte bucăți alăturate și dacă acoperă golul. */
export function blindPlan(win: RoomWindow, m: Material | undefined){
  const s = m?.specs?.sizeCm ?? [80, 155], bw = s[0] / 100, bl = s[1] / 100, n = Math.max(1, Math.round(win.widthM / bw)), cover = n * bw;
  return { count: n, coverM: r2(cover), gapM: r2(win.widthM - cover), short: bl < win.topM - win.sillM - 1e-6 };
}
/** Covorul: dimensiunea orientată după camera (latura lungă pe latura lungă), sau rotit. */
export function rugSize(room: Room, m: Material | undefined, rotate = false): { w: number; d: number } | null {
  const s = m?.specs?.sizeCm; if (!s) return null; const a = Math.max(s[0], s[1]) / 100, b = Math.min(s[0], s[1]) / 100, wide = room.rect.x1 - room.rect.x0 >= room.rect.z1 - room.rect.z0;
  return (wide !== rotate) ? { w: a, d: b } : { w: b, d: a };
}
export interface TextileIssue { key: string; roomId: string; vars?: Record<string, string | number> }
export function textileIssues(mc: MaterialsCatalog, fl: Floor, room: Room, f: RoomFinishes): TextileIssue[] {
  const out: TextileIssue[] = [], wins = roomWindows(fl, room);
  for (const t of f.windows || []){ const win = wins.find(w => w.openingId === t.openingId); if (!win) { out.push({ key: 'tex.noWindow', roomId: room.id }); continue; }
    for (const id of [t.curtain, t.sheer]){ const m = materialOf(mc, id); if (!m) continue; const p = curtainPlan(win, fl.ceilingHeight, m, t.fullness);
      if (p.short) out.push({ key: 'tex.curtainShort', roomId: room.id, vars: { name: m.name, drop: p.drop } }); }
    const b = materialOf(mc, t.blind); if (b){ const p = blindPlan(win, b);
      if (Math.abs(p.gapM) > TEXTILE_RULES.blindTolM) out.push({ key: 'tex.blindFit', roomId: room.id, vars: { name: b.name, w: win.widthM, cover: p.coverM } });
      if (p.short) out.push({ key: 'tex.blindShort', roomId: room.id, vars: { name: b.name } }); } }
  const rm = materialOf(mc, f.rug?.material), rs = rm ? rugSize(room, rm, f.rug?.rotate) : null;
  if (rs){ const g = TEXTILE_RULES.rugWallGapM; if (rs.w > room.rect.x1 - room.rect.x0 - 2 * g || rs.d > room.rect.z1 - room.rect.z0 - 2 * g) out.push({ key: 'tex.rugTooBig', roomId: room.id, vars: { cm: g * 100 } }); }
  return out;
}
export function sanitizeTextiles(rf: any): string | null {
  const id = (x: unknown) => x == null || (typeof x === 'string' && x.length <= 80);
  if (rf.windows !== undefined && (!Array.isArray(rf.windows) || rf.windows.length > 30 || rf.windows.some((t: any) => !t || typeof t !== 'object' || typeof t.openingId !== 'string' || t.openingId.length > 80
    || !id(t.curtain) || !id(t.sheer) || !id(t.blind) || (t.fullness !== undefined && !(typeof t.fullness === 'number' && t.fullness >= 1 && t.fullness <= 3)))
    || new Set(rf.windows.map((t: any) => t.openingId)).size !== rf.windows.length)) return 'Textilele de la ferestre sunt invalide.';
  if (rf.rug != null && (typeof rf.rug !== 'object' || typeof rf.rug.material !== 'string' || rf.rug.material.length > 80 || (rf.rug.rotate !== undefined && typeof rf.rug.rotate !== 'boolean'))) return 'Covorul este invalid.';
  return null;
}
/** Ce desenează motorul 3D la ferestre și covorul. */
export function textileVisual(mc: MaterialsCatalog, fl: Floor, room: Room, f: RoomFinishes){
  const wins = roomWindows(fl, room), windows = (f.windows || []).flatMap(t => { const w = wins.find(x => x.openingId === t.openingId); if (!w) return [];
    const c = materialOf(mc, t.curtain), s = materialOf(mc, t.sheer), b = materialOf(mc, t.blind), plan = curtainPlan(w, fl.ceilingHeight, c ?? s, t.fullness);
    return [{ wallIndex: w.wallIndex, openingIndex: w.openingIndex, inward: w.inward, rodH: plan.rodH,
      curtain: c ? { color: c.specs?.color ?? '#cfc6b8' } : null, sheer: s ? { color: s.specs?.color ?? '#f4f3ee' } : null,
      blind: b ? { color: b.specs?.color ?? '#f2f1ec', kind: b.specs?.blind ?? 'roller' } : null }]; });
  const rm = materialOf(mc, f.rug?.material), rs = rm ? rugSize(room, rm, f.rug?.rotate) : null;
  return { windows, rug: rs ? { ...rs, color: rm!.specs?.color ?? '#c8b79a' } : null };
}
