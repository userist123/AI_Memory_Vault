// Obiecte pe perete (tablou, oglindă, raft, TV): geometria pe fața peretelui, camera și latura în care dau, valorile
// implicite (regula de montaj), validările și elevațiile. Funcții pure, fără dependențe de validate.ts (care le citește);
// planul, 3D-ul, devizul, planșele și serverul citesc de aici. Legătura e `wallId` + `side`, ca la nișă: obiectul se
// mișcă odată cu peretele, iar suprapunerea cu golurile se verifică pe același perete.
import type { Catalog, Floor, Issue, MaterialCategory, Room, Snapshot, Wall, WallItem, WallItemKind } from './types';
import { resolve } from './catalog';
import { footprintAt } from './geometry';
import { doorSwing, doorTypeOf, slideZone } from './door-geometry';
import { WALL_ITEM_RULES } from './rules';

export const WALL_ITEM_KINDS: WallItemKind[] = ['art', 'mirror', 'shelf', 'tv'];
/** Categoria de produs din catalogul de materiale pentru fiecare tip (TV-ul nu are: e al clientului). */
export const WALL_ITEM_CATEGORY: Record<WallItemKind, MaterialCategory | null> = { art: 'wall_art', mirror: 'mirror', shelf: 'wall_shelf', tv: null };
/** Adâncimea implicită (m), când obiectul nu o are. */
export const wallItemDepth = (it: Pick<WallItem, 'kind' | 'depth'>) => it.depth ?? WALL_ITEM_RULES.defaults[it.kind].depth;

export interface WallItemGeometry { wall: Wall; wallIndex: number; L: number; ux: number; uz: number; nx: number; nz: number; s0: number; s1: number;
  /** capetele obiectului pe fața peretelui */ face: [[number, number], [number, number]]; center: [number, number] }
/** Geometria obiectului pe peretele lui (null dacă peretele nu mai există). `nx/nz` = normala feței (spre cameră); `offset` = centrul. */
export function wallItemGeometry(floor: Pick<Floor, 'walls'>, it: Pick<WallItem, 'wallId' | 'side' | 'offset' | 'width'>): WallItemGeometry | null {
  const wi = floor.walls.findIndex(x => x.id === it.wallId), w = floor.walls[wi]; if (!w) return null;
  const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]); if (L < .01) return null;
  const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, sg = it.side === -1 ? -1 : 1, nx = -uz * sg, nz = ux * sg, h = w.thickness / 2, s0 = it.offset - it.width / 2, s1 = it.offset + it.width / 2;
  const P = (s: number, d: number): [number, number] => [w.a[0] + ux * s + nx * d, w.a[1] + uz * s + nz * d];
  return { wall: w, wallIndex: wi, L, ux, uz, nx, nz, s0, s1, face: [P(s0, h), P(s1, h)], center: P(it.offset, h) };
}
/** Camera în fața obiectului (prima cameră de pe fața lui), sau null. */
export function wallItemRoom(floor: Pick<Floor, 'walls' | 'rooms'>, it: WallItem): Room | null {
  const g = wallItemGeometry(floor, it); if (!g) return null; const x = g.center[0] + g.nx * .15, z = g.center[1] + g.nz * .15;
  return floor.rooms.find(r => x > r.rect.x0 && x < r.rect.x1 && z > r.rect.z0 && z < r.rect.z1) ?? null;
}
/** Latura camerei pe care stă obiectul și poziția centrului pe ea (m de la colțul x0/z0, ca la placări), sau null pe un perete oblic. */
export function wallItemFace(floor: Pick<Floor, 'walls' | 'rooms'>, it: WallItem): { room: Room; side: 'N' | 'S' | 'E' | 'W'; u: number } | null {
  const g = wallItemGeometry(floor, it), room = g && wallItemRoom(floor, it); if (!g || !room) return null;
  const side = g.nz > .7 ? 'N' : g.nz < -.7 ? 'S' : g.nx > .7 ? 'W' : g.nx < -.7 ? 'E' : null; if (!side) return null;
  return { room, side, u: side === 'N' || side === 'S' ? g.center[0] - room.rect.x0 : g.center[1] - room.rect.z0 };
}

/** Piesa de mobilier lipită de fața obiectului și sub el pe lungime (canapea, pat, comodă), cu înălțimea ei; null dacă nu e. */
export function furnitureBelow(snap: Pick<Snapshot, 'placements'>, cat: Catalog, floor: Pick<Floor, 'walls'>, it: Pick<WallItem, 'wallId' | 'side' | 'offset' | 'width'>, groups: readonly string[] | null = WALL_ITEM_RULES.aboveGroups): { id: string; group: string; top: number } | null {
  const g = wallItemGeometry(floor, it); if (!g) return null; const fx = g.center[0], fz = g.center[1];
  let best: { id: string; group: string; top: number } | null = null;
  for (const p of snap.placements){ if (groups && !groups.includes(p.group)) continue; const rv = resolve(cat, p.variantId); if (!rv) continue;
    const fp = p.size ? footprintAt(p.x, p.z, p.rotation, p.size.w / 100, p.size.d / 100) : footprintAt(p.x, p.z, p.rotation, rv.w, rv.d), top = p.size ? p.size.h / 100 : rv.h;
    const cs: [number, number][] = [[fp.x0, fp.z0], [fp.x1, fp.z0], [fp.x0, fp.z1], [fp.x1, fp.z1]], dn = cs.map(([x, z]) => (x - fx) * g.nx + (z - fz) * g.nz), du = cs.map(([x, z]) => (x - g.wall.a[0]) * g.ux + (z - g.wall.a[1]) * g.uz);
    if (Math.min(...dn) < -.05 || Math.min(...dn) > WALL_ITEM_RULES.furnitureNearM) continue;
    if (Math.min(Math.max(...du), g.s1) - Math.max(Math.min(...du), g.s0) < .05) continue;
    if (!best || top > best.top) best = { id: p.id, group: p.group, top }; }
  return best;
}
/** Mărimea și cota implicite: tablou 70 × 50 cm cu centrul la 1,45 m (deasupra canapelei, a patului sau a comodei: marginea de
 *  jos la 20 cm peste piesă), oglindă 60 × 90 cm la 1,55 m, raft 80 cm la 1,6 m, TV 55″ (123 × 71 cm) la 1,1 m. */
export function wallItemDefaults(kind: WallItemKind, below?: { top: number } | null): { width: number; height: number; depth: number; elevation: number } {
  const d = WALL_ITEM_RULES.defaults[kind]; const elevation = kind === 'art' && below ? Math.round((below.top + WALL_ITEM_RULES.aboveFurnitureM + d.height / 2) * 1000) / 1000 : d.elevation;
  return { width: d.width, height: d.height, depth: d.depth, elevation };
}

/** Golul pe verticală: ușa de la pardoseală, fereastra de la parapet (aceleași valori implicite ca în 3D). */
const openingSpan = (o: Wall['openings'][number]): [number, number] => o.kind === 'door' ? [0, o.height ?? 2.1] : [o.sill ?? .9, (o.sill ?? .9) + (o.height ?? 1.3)];
const cross = (a0: number, a1: number, b0: number, b1: number) => Math.min(a1, b1) - Math.max(a0, b0) > .005;
const issue = (it: WallItem, sev: 'ERROR' | 'WARNING', k: string, message: string, extra: Record<string, string | number> = {}): Issue =>
  ({ code: 'WALL_ITEM', severity: sev, message, key: `issue.WALL_ITEM.${k}`, vars: { item: it.id, ...extra } });
/** Obiectul stă pe perete și între pardoseală și tavan, nu acoperă un gol sau o nișă de pe aceeași față, nu stă pe zona în care
 *  parchează o ușă glisantă (pe fața șinei) sau pe caseta unei uși în perete (pe ambele fețe). */
export function wallItemIssues(floor: Floor): Issue[] {
  const out: Issue[] = [];
  for (const it of floor.wallItems ?? []){ const g = wallItemGeometry(floor, it); if (!g) continue; const w = g.wall, y0 = it.elevation - it.height / 2, y1 = it.elevation + it.height / 2;
    if (g.s0 < -1e-6 || g.s1 > g.L + 1e-6 || y0 < -1e-6 || y1 > floor.ceilingHeight + 1e-6)
      out.push(issue(it, 'ERROR', 'outside', `Obiectul ${it.id} iese din peretele ${w.id}, sub pardoseală sau peste tavan.`));
    if (w.openings.some(o => { const [a, b] = openingSpan(o); return cross(o.offset, o.offset + o.width, g.s0, g.s1) && cross(a, b, y0, y1); })
      || (floor.niches ?? []).some(n => n.wallId === w.id && (n.side ?? 1) === (it.side === -1 ? -1 : 1) && cross(n.offset, n.offset + n.width, g.s0, g.s1) && cross(n.elevation, n.elevation + n.height, y0, y1)))
      out.push(issue(it, 'ERROR', 'overOpening', `Obiectul ${it.id} se suprapune cu un gol sau cu o nișă din perete.`));
    w.openings.forEach((o, oi) => { const t = doorTypeOf(o); if (t !== 'sliding' && t !== 'pocket') return; const z = slideZone(floor, g.wallIndex, oi); if (!z || !cross(z.t0, z.t1, g.s0, g.s1) || y0 > (o.height ?? 2.1) + .1) return;
      if (t === 'sliding' && doorSwing(floor, g.wallIndex, oi).swing !== (it.side === -1 ? -1 : 1)) return;
      out.push(issue(it, 'WARNING', 'slideZone', `Obiectul ${it.id} stă pe zona în care culisează ușa ${o.id}.`, { op: o.id })); }); }
  return out;
}
/** O piesă de mobilier mai înaltă decât marginea de jos a obiectului, lipită de aceeași față a peretelui, îl acoperă. */
export function wallItemFurnitureIssues(snap: Pick<Snapshot, 'placements'>, cat: Catalog, floor: Floor): Issue[] {
  const out: Issue[] = [];
  for (const it of floor.wallItems ?? []){ const b = furnitureBelow(snap, cat, floor, it, null); if (!b) continue;
    const y0 = it.elevation - it.height / 2; if (b.top > y0 + .01) out.push(issue(it, 'WARNING', 'behindTall', `O piesă de mobilier acoperă obiectul ${it.id} de pe perete.`, { h_m: Math.round(b.top * 100) / 100, pc: b.id })); }
  return out;
}
/** Obiectele de pe perete văzute în elevația unei laturi: `u` = centrul de la stânga la dreapta, privind peretele din cameră. */
export function wallItemsOnSide(floor: Floor, room: Room, side: 'N' | 'S' | 'E' | 'W'): { id: string; kind: WallItemKind; u: number; y: number; w: number; h: number }[] {
  const len = side === 'N' || side === 'S' ? room.rect.x1 - room.rect.x0 : room.rect.z1 - room.rect.z0, flip = side === 'S' || side === 'W';
  return (floor.wallItems ?? []).flatMap(it => { const f = wallItemFace(floor, it); if (!f || f.room.id !== room.id || f.side !== side) return [];
    return [{ id: it.id, kind: it.kind, u: Math.round((flip ? len - f.u : f.u) * 1000) / 1000, y: it.elevation, w: it.width, h: it.height }]; }).sort((a, b) => a.u - b.u);
}
