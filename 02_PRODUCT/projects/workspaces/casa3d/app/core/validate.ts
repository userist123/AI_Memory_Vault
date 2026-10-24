import { resolve } from './catalog';
import { rectHit, insideRect, footprintAt, frontRect, wallLength } from './geometry';
import { FRONT_CLEARANCE, DINING_CLEARANCE, TALL_ITEM_M, WALL_PROXIMITY_M, ALLOWED_OVERLAP, WALL_HIGH, DOOR_SLIDE_EXTRA_M, POCKET_MIN_WALL_M, DOUBLE_NARROW_M } from './rules';
import { nicheIssues } from './niches';
import { wallItemIssues } from './wall-items';
import { renovationIssues } from './renovation';
import { zoneIssues } from './floor-zones';
import { ceilingZoneIssues } from './ceiling-zones';
import { railingIssues } from './railings';
import { radiatorFront } from './tech-geometry';
import { slideSide, doorSwing } from './door-geometry';
import type { Catalog, DoorType, Floor, FurniturePlacement, Issue, Room, Severity, Snapshot, RoomRect } from './types';

const allowed = (a: string, b: string) => WALL_HIGH.has(a) !== WALL_HIGH.has(b) || ALLOWED_OVERLAP.some(([x, y]) => (x === a && y === b) || (x === b && y === a));
// golurile de pe o latură a camerei (aceeași logică ca openingsOnSide din motor)
export function openingsOnSide(floor: Floor, room: Room, side: 'N' | 'S' | 'W' | 'E'){
  const r = room.rect, out: { kind: string; a: number; b: number; passage?: boolean; doorType?: DoorType }[] = [];
  // golurile unui perete de demolat nu mai contează (peretele dispare, deci și ușile și ferestrele lui)
  for (const w of floor.walls){ if (w.status === 'demolish') continue; const [ax, az] = w.a, [bx, bz] = w.b, horiz = Math.abs(az - bz) < 1e-6;
    const on = (side === 'N' && horiz && Math.abs(az - r.z0) < 1e-6) || (side === 'S' && horiz && Math.abs(az - r.z1) < 1e-6) || (side === 'W' && !horiz && Math.abs(ax - r.x0) < 1e-6) || (side === 'E' && !horiz && Math.abs(ax - r.x1) < 1e-6);
    if (!on) continue; const dir = horiz ? Math.sign(bx - ax) : Math.sign(bz - az), s0 = horiz ? ax : az;
    for (const o of w.openings){ const p = s0 + dir * o.offset, q = s0 + dir * (o.offset + o.width); out.push({ kind: o.kind, a: Math.min(p, q), b: Math.max(p, q), ...(o.passage ? { passage: true } : o.kind === 'door' && o.doorType ? { doorType: o.doorType } : {}) }); } }
  const lo = side === 'N' || side === 'S' ? r.x0 : r.z0, hi = side === 'N' || side === 'S' ? r.x1 : r.z1;
  return out.filter(o => o.b > lo + .01 && o.a < hi - .01);
}
// zona din fața ușii: 95 cm (arcul foii); la golul de trecere doar 60 cm de circulație
export const DOOR_ZONE_M = .95, PASSAGE_ZONE_M = .6;
/** Adâncimea zonei libere din fața ușii, pe tip: glisantele n-au arc (doar 60 cm de circulație), dubla are două foi de w/2,
 *  pliantele se strâng în ~w/2, pivotanta mătură 3w/4; cel puțin 60 cm de circulație la toate. Lipsa tipului = batanta de azi. */
export function doorZoneDepth(o: { passage?: boolean; doorType?: DoorType }, width: number): number {
  if (o.passage) return PASSAGE_ZONE_M;
  switch (o.doorType){ case 'sliding': case 'pocket': return PASSAGE_ZONE_M; case 'double': case 'folding': return Math.max(PASSAGE_ZONE_M, width / 2);
    case 'pivot': return Math.max(PASSAGE_ZONE_M, width * .75); default: return DOOR_ZONE_M; }
}
export function doorZones(floor: Floor, room: Room): RoomRect[] {
  const r = room.rect, z: RoomRect[] = [];
  for (const s of ['N', 'S', 'W', 'E'] as const) for (const o of openingsOnSide(floor, room, s).filter(o => o.kind === 'door')){ const dz = doorZoneDepth(o, o.b - o.a);
    const f = s === 'N' || s === 'S' ? { x0: o.a - .05, x1: o.b + .05, z0: r.z0, z1: r.z1 } : { z0: o.a - .05, z1: o.b + .05, x0: r.x0, x1: r.x1 };
    if (s === 'N') f.z1 = r.z0 + dz; if (s === 'S') f.z0 = r.z1 - dz; if (s === 'W') f.x1 = r.x0 + dz; if (s === 'E') f.x0 = r.x1 - dz; z.push(f); }
  return z;
}
export function footprintOf(cat: Catalog, p: FurniturePlacement){ const rv = resolve(cat, p.variantId); if (!rv) return null; return p.size ? footprintAt(p.x, p.z, p.rotation, p.size.w / 100, p.size.d / 100) : footprintAt(p.x, p.z, p.rotation, rv.w, rv.d); }
function clearanceZone(cat: Catalog, p: FurniturePlacement, fp: RoomRect): RoomRect | null {
  if (p.group === 'masa'){ const rv = resolve(cat, p.variantId)!, px = (rv.variant.chairs || 4) === 2 ? DINING_CLEARANCE.chairs2 : DINING_CLEARANCE.chairs4, pz = DINING_CLEARANCE.ends;
    const q = Math.round(p.rotation / (Math.PI / 2)) % 2 !== 0; const ex = q ? pz : px, ez = q ? px : pz; return { x0: fp.x0 - ex, x1: fp.x1 + ex, z0: fp.z0 - ez, z1: fp.z1 + ez }; }
  return frontRect(fp, p.rotation, FRONT_CLEARANCE[p.group] ?? 0);
}
// Verificarea unei plasări. ERROR = nu se aplică; WARNING = se aplică doar după confirmare.
export function validatePlacement(snap: Snapshot, cat: Catalog, p: FurniturePlacement): Issue[] {
  const issues: Issue[] = [], rv = resolve(cat, p.variantId), room = snap.floor.rooms.find(r => r.id === p.roomId);
  if (!rv) return [{ code: 'UNKNOWN_VARIANT', severity: 'ERROR', message: 'Produsul nu mai există în catalog.', key: 'issue.UNKNOWN_VARIANT' }];
  if (!room) return [{ code: 'OUT_OF_ROOM', severity: 'ERROR', message: 'Piesa nu aparține niciunei camere.', key: 'issue.NO_ROOM' }];
  const fp = footprintOf(cat, p)!, ph = p.size ? p.size.h / 100 : rv.h; // dimensiunile pe comandă au prioritate
  if (!insideRect(room.rect, fp)) issues.push({ code: 'OUT_OF_ROOM', severity: 'ERROR', message: `Iese din ${room.name}.`, key: 'issue.OUT_OF_ROOM', vars: { rn: room.name } });
  if (doorZones(snap.floor, room).some(z => rectHit(z, fp))) issues.push({ code: 'DOOR_ZONE', severity: 'ERROR', message: 'Blochează deschiderea unei uși.', key: 'issue.DOOR_ZONE' });
  const others = snap.placements.filter(o => o.id !== p.id && o.roomId === p.roomId);
  for (const o of others){ const ofp = footprintOf(cat, o); if (!ofp || allowed(p.group, o.group)) continue;
    if (rectHit(fp, ofp)) issues.push({ code: 'OVERLAP', severity: 'ERROR', message: `Se suprapune cu ${resolve(cat, o.variantId)?.product.name}.`, with: o.id, key: 'issue.OVERLAP', vars: { nm: resolve(cat, o.variantId)?.product.name ?? '' } }); }
  // LACUNA 1 reparată: mobilierul înalt nu are voie în fața ferestrei nici la mutarea manuală
  if (ph > TALL_ITEM_M){ const r = room.rect, sides: [('N' | 'S' | 'W' | 'E'), number, boolean][] = [['N', fp.z0 - r.z0, true], ['S', r.z1 - fp.z1, true], ['W', fp.x0 - r.x0, false], ['E', r.x1 - fp.x1, false]];
    for (const [s, gap, horiz] of sides){ if (gap > WALL_PROXIMITY_M) continue; const a = horiz ? fp.x0 : fp.z0, b = horiz ? fp.x1 : fp.z1;
      if (openingsOnSide(snap.floor, room, s).some(o => o.kind === 'window' && b > o.a + .01 && a < o.b - .01)) { issues.push({ code: 'WINDOW_BLOCKED', severity: 'WARNING', message: 'Acoperă o fereastră (piesă mai înaltă de 95 cm).', key: 'issue.WINDOW_BLOCKED', vars: { h_m: TALL_ITEM_M } }); break; } }
    // caloriferul (doar cel cu corp, plasat cu unealta nouă): piesa înaltă din fața lui oprește aerul cald
    if ((snap.tech ?? []).some(t => t.roomId === room.id && (() => { const z = radiatorFront(snap.floor, t); return !!z && rectHit(z, fp); })()))
      issues.push({ code: 'RADIATOR_BLOCKED', severity: 'WARNING', message: 'Stă în fața unui calorifer (piesă mai înaltă de 95 cm): căldura nu mai circulă.', key: 'issue.RADIATOR_BLOCKED', vars: { h_m: TALL_ITEM_M } }); }
  // LACUNA 2 reparată: spațiul de circulație din fața piesei (și cel al pieselor vecine)
  const cz = clearanceZone(cat, p, fp);
  if (cz){ if (!insideRect(room.rect, cz)) issues.push({ code: 'CLEARANCE', severity: 'WARNING', message: `Nu rămân ${Math.round((FRONT_CLEARANCE[p.group] ?? .6) * 100)} cm liberi în față.`, key: 'issue.CLEARANCE_FRONT', vars: { d_m: FRONT_CLEARANCE[p.group] ?? .6 } });
    for (const o of others){ const ofp = footprintOf(cat, o); if (ofp && !allowed(p.group, o.group) && rectHit(cz, ofp)) { issues.push({ code: 'CLEARANCE', severity: 'WARNING', message: `Prea aproape de ${resolve(cat, o.variantId)?.product.name}: spațiul de circulație e redus.`, with: o.id, key: 'issue.CLEARANCE_NEAR', vars: { nm: resolve(cat, o.variantId)?.product.name ?? '' } }); break; } } }
  for (const o of others){ const ofp = footprintOf(cat, o); if (!ofp || allowed(p.group, o.group)) continue; const oz = clearanceZone(cat, o, ofp);
    if (oz && rectHit(oz, fp) && !rectHit(ofp, fp)){ issues.push({ code: 'CLEARANCE', severity: 'WARNING', message: `Stă în spațiul de circulație al piesei ${resolve(cat, o.variantId)?.product.name}.`, with: o.id, key: 'issue.CLEARANCE_IN', vars: { nm: resolve(cat, o.variantId)?.product.name ?? '' } }); break; } }
  return issues;
}
export const severityOf = (issues: Issue[]): Severity => issues.some(i => i.severity === 'ERROR') ? 'ERROR' : issues.length ? 'WARNING' : 'PASS';
export function validateFloor(floor: Floor): Issue[] {
  const out: Issue[] = [];
  for (const w of floor.walls){ const L = wallLength(w.a, w.b);
    if (L < .2) out.push({ code: 'WALL_TOO_SHORT', severity: 'ERROR', message: `Peretele ${w.id} are sub 20 cm.`, key: 'issue.WALL_TOO_SHORT', vars: { id: w.id, d_m: 0.2 } });
    if (w.status === 'demolish') continue;   // golurile peretelui de demolat nu mai contează
    for (const o of w.openings) if (o.offset < 0 || o.offset + o.width > L + 1e-6) out.push({ code: 'OPENING_OUTSIDE_WALL', severity: 'ERROR', message: `Golul ${o.id} iese din peretele ${w.id}.`, key: 'issue.OPENING_OUTSIDE_WALL', vars: { op: o.id, id: w.id } }); }
  out.push(...nicheIssues(floor), ...wallItemIssues(floor), ...renovationIssues(floor), ...zoneIssues(floor), ...ceilingZoneIssues(floor), ...railingIssues(floor));
  // tipul ușii: glisantele cer perete liber lângă gol (foaia + 10 cm) de partea unde parchează; caseta cere perete interior gros; dubla îngustă
  floor.walls.forEach((w, wi) => w.openings.forEach((o, oi) => { if (o.kind !== 'door' || o.passage || !o.doorType || w.status === 'demolish') return; const vars = { op: o.id, id: w.id };
    if (o.doorType === 'pocket' || o.doorType === 'sliding'){ const free = slideSide(floor, wi, oi)[doorSwing(floor, wi, oi).hinge], need = o.width + DOOR_SLIDE_EXTRA_M;
      if (free < need - 1e-6) out.push(o.doorType === 'pocket'
        ? { code: 'DOOR_TYPE', severity: 'ERROR', message: `Ușa în perete ${o.id} nu are loc de casetă: lângă gol sunt ${Math.round(free * 100)} cm de perete liber, trebuie ${Math.round(need * 100)} cm.`, key: 'issue.DOOR_TYPE.pocketSpace', vars: { ...vars, free_m: free, need_m: need } }
        : { code: 'DOOR_TYPE', severity: 'WARNING', message: `Ușa glisantă ${o.id} nu are unde să parcheze: lângă gol sunt ${Math.round(free * 100)} cm de perete liber, trebuie ${Math.round(need * 100)} cm.`, key: 'issue.DOOR_TYPE.slideSpace', vars: { ...vars, free_m: free, need_m: need } }); }
    if (o.doorType === 'pocket' && (w.exterior || w.thickness < POCKET_MIN_WALL_M - 1e-9)) out.push({ code: 'DOOR_TYPE', severity: 'WARNING', message: `Caseta ușii ${o.id} cere un perete interior de cel puțin ${POCKET_MIN_WALL_M * 100} cm; verifică modelul.`, key: 'issue.DOOR_TYPE.pocketWall', vars: { ...vars, d_m: POCKET_MIN_WALL_M } });
    if (o.doorType === 'double' && o.width < DOUBLE_NARROW_M - 1e-9) out.push({ code: 'DOOR_TYPE', severity: 'WARNING', message: `Ușa dublă ${o.id} are sub ${DOUBLE_NARROW_M * 100} cm: foile ies prea înguste.`, key: 'issue.DOOR_TYPE.doubleNarrow', vars: { ...vars, d_m: DOUBLE_NARROW_M } }); }));
  // ghene și casetări: avertismente (se pot construi, dar blochează o ușă sau ies din cameră)
  for (const c of floor.columns ?? []){ if (c.role !== 'shaft' && c.role !== 'boxing') continue; const r = columnRect(c);
    if (!floor.rooms.some(room => insideRect(room.rect, r))) out.push({ code: 'SHAFT_OUTSIDE_ROOM', severity: 'WARNING', message: `Ghena ${c.id} nu stă în întregime într-o cameră.`, key: 'issue.SHAFT_OUTSIDE_ROOM', vars: { col: c.id } });
    if (floor.rooms.some(room => doorZones(floor, room).some(z => rectHit(z, r)))) out.push({ code: 'SHAFT_DOOR', severity: 'WARNING', message: `Ghena ${c.id} stă în deschiderea unei uși.`, key: 'issue.SHAFT_DOOR', vars: { col: c.id } }); }
  return out;
}

export const columnRect = (c: { x: number; z: number; w: number; d: number }): RoomRect => ({ x0: c.x - c.w / 2, x1: c.x + c.w / 2, z0: c.z - c.d / 2, z1: c.z + c.d / 2 });
export type IssueTarget =
  | { kind: 'placement' | 'room' | 'wall' | 'stair' | 'column' | 'niche' | 'wall_item' | 'floor_zone' | 'ceiling_zone' | 'railing'; id: string }
  | { kind: 'opening'; id: string; wallId: string };

export function issueTarget(issue: Pick<Partial<Issue>, 'with' | 'vars'> & { refs?: string[] }, floor?: Floor, placements?: FurniturePlacement[]): IssueTarget | null {
  if (issue.with && (!placements || placements.some(p => p.id === issue.with))) return { kind: 'placement', id: issue.with };
  if (issue.vars?.stair && (!floor?.stairs || floor.stairs.some(s => s.id === String(issue.vars?.stair)))) return { kind: 'stair', id: String(issue.vars.stair) };
  if (issue.vars?.niche && (!floor || floor.niches?.some(n => n.id === String(issue.vars?.niche)))) return { kind: 'niche', id: String(issue.vars.niche) };
  if (issue.vars?.item && (!floor || floor.wallItems?.some(x => x.id === String(issue.vars?.item)))) return { kind: 'wall_item', id: String(issue.vars.item) };
  if (issue.vars?.zone && (!floor || floor.zones?.some(x => x.id === String(issue.vars?.zone)))) return { kind: 'floor_zone', id: String(issue.vars.zone) };
  if (issue.vars?.czone && (!floor || floor.ceilingZones?.some(x => x.id === String(issue.vars?.czone)))) return { kind: 'ceiling_zone', id: String(issue.vars.czone) };
  if (issue.vars?.rail && (!floor || floor.railings?.some(x => x.id === String(issue.vars?.rail)))) return { kind: 'railing', id: String(issue.vars.rail) };
  if (issue.vars?.col && (!floor || floor.columns?.some(c => c.id === String(issue.vars?.col)))) return { kind: 'column', id: String(issue.vars.col) };
  if (issue.vars?.op && issue.vars?.id) return { kind: 'opening', id: String(issue.vars.op), wallId: String(issue.vars.id) };
  if (issue.vars?.id && (!floor || floor.walls.some(w => w.id === String(issue.vars?.id)))) return { kind: 'wall', id: String(issue.vars.id) };
  if (Array.isArray(issue.refs)) {
    for (const ref of issue.refs) {
      if (placements?.some(p => p.id === ref)) return { kind: 'placement', id: ref };
      if (floor?.rooms.some(r => r.id === ref)) return { kind: 'room', id: ref };
      for (const w of floor?.walls ?? []) {
        if (w.id === ref) return { kind: 'wall', id: ref };
        if (w.openings.some(o => o.id === ref)) return { kind: 'opening', id: ref, wallId: w.id };
      }
    }
  }
  return null;
}

