import { resolve } from './catalog';
import { rectHit, insideRect, footprintAt, frontRect, wallLength } from './geometry';
import { FRONT_CLEARANCE, DINING_CLEARANCE, TALL_ITEM_M, WALL_PROXIMITY_M, ALLOWED_OVERLAP } from './rules';
import type { Catalog, Floor, FurniturePlacement, Issue, Room, Severity, Snapshot, RoomRect } from './types';

const allowed = (a: string, b: string) => ALLOWED_OVERLAP.some(([x, y]) => (x === a && y === b) || (x === b && y === a));
// golurile de pe o latură a camerei (aceeași logică ca openingsOnSide din motor)
export function openingsOnSide(floor: Floor, room: Room, side: 'N' | 'S' | 'W' | 'E'){
  const r = room.rect, out: { kind: string; a: number; b: number }[] = [];
  for (const w of floor.walls){ const [ax, az] = w.a, [bx, bz] = w.b, horiz = Math.abs(az - bz) < 1e-6;
    const on = (side === 'N' && horiz && Math.abs(az - r.z0) < 1e-6) || (side === 'S' && horiz && Math.abs(az - r.z1) < 1e-6) || (side === 'W' && !horiz && Math.abs(ax - r.x0) < 1e-6) || (side === 'E' && !horiz && Math.abs(ax - r.x1) < 1e-6);
    if (!on) continue; const dir = horiz ? Math.sign(bx - ax) : Math.sign(bz - az), s0 = horiz ? ax : az;
    for (const o of w.openings){ const p = s0 + dir * o.offset, q = s0 + dir * (o.offset + o.width); out.push({ kind: o.kind, a: Math.min(p, q), b: Math.max(p, q) }); } }
  const lo = side === 'N' || side === 'S' ? r.x0 : r.z0, hi = side === 'N' || side === 'S' ? r.x1 : r.z1;
  return out.filter(o => o.b > lo + .01 && o.a < hi - .01);
}
export function doorZones(floor: Floor, room: Room): RoomRect[] {
  const r = room.rect, z: RoomRect[] = [];
  for (const s of ['N', 'S', 'W', 'E'] as const) for (const o of openingsOnSide(floor, room, s).filter(o => o.kind === 'door')){
    const f = s === 'N' || s === 'S' ? { x0: o.a - .05, x1: o.b + .05, z0: r.z0, z1: r.z1 } : { z0: o.a - .05, z1: o.b + .05, x0: r.x0, x1: r.x1 };
    if (s === 'N') f.z1 = r.z0 + .95; if (s === 'S') f.z0 = r.z1 - .95; if (s === 'W') f.x1 = r.x0 + .95; if (s === 'E') f.x0 = r.x1 - .95; z.push(f); }
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
      if (openingsOnSide(snap.floor, room, s).some(o => o.kind === 'window' && b > o.a + .01 && a < o.b - .01)) { issues.push({ code: 'WINDOW_BLOCKED', severity: 'WARNING', message: 'Acoperă o fereastră (piesă mai înaltă de 95 cm).', key: 'issue.WINDOW_BLOCKED', vars: { h_m: TALL_ITEM_M } }); break; } } }
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
    for (const o of w.openings) if (o.offset < 0 || o.offset + o.width > L + 1e-6) out.push({ code: 'OPENING_OUTSIDE_WALL', severity: 'ERROR', message: `Golul ${o.id} iese din peretele ${w.id}.`, key: 'issue.OPENING_OUTSIDE_WALL', vars: { op: o.id, id: w.id } }); }
  return out;
}
