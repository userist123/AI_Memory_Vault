// Tavan fals pe zonă (Floor.ceilingZones): o casetă din gips-carton coborâtă pe o porțiune a camerei (deasupra mesei, a patului,
// a bucătăriei din open-space), cu bandă laterală, scafă și LED pe contur opționale. Funcții pure: decuparea la cameră,
// cantitățile pentru deviz, validările și ștergerea odată cu camera.
import type { CeilingZone, Floor, Issue, Room, RoomRect } from './types';
import { intersect, rectArea } from './floor-zones';
import { doorZoneDepth } from './validate';
import { DOOR_HEIGHT, WINDOW_HEIGHT } from './rules.boq';

const r2 = (v: number) => Math.round(v * 100) / 100;
/** Distanța minimă dintre partea de jos a casetei și buiandrugul unui gol din dreptul ei (ușa sau fereastra trebuie să se deschidă). */
export const CEILING_ZONE_RULES = { openingClearM: .05, windowStripM: .3 };

export const ceilingZonesOfRoom = (fl: Pick<Floor, 'ceilingZones'>, roomId: string): CeilingZone[] => (fl.ceilingZones ?? []).filter(z => z.roomId === roomId);
/** Dreptunghiul casetei decupat la camera ei, sau null (în afara camerei). */
export function ceilingZoneRect(fl: Pick<Floor, 'rooms'>, z: CeilingZone): RoomRect | null {
  const room = fl.rooms.find(r => r.id === z.roomId); return room ? intersect(room.rect, z.rect) : null;
}
export interface CeilingZoneQty { w: number; d: number; areaM2: number; perimeterM: number; bandM: number; bandM2: number; coveM2: number; boardM2: number; ledM: number }
/** Cantitățile casetei (din dreptunghiul decupat la cameră): placa de jos, banda laterală (perimetrul × coborârea care iese sub
 *  tavanul camerei; un tavan fals al camerei o micșorează), raftul scafei (perimetrul × lățimea scafei) și LED-ul pe contur (ml). */
export function ceilingZoneQty(fl: Pick<Floor, 'rooms'>, z: CeilingZone, roomDropCm = 0): CeilingZoneQty | null {
  const c = ceilingZoneRect(fl, z); if (!c) return null;
  const w = c.x1 - c.x0, d = c.z1 - c.z0, area = w * d, per = 2 * (w + d), band = Math.max(0, z.dropCm - roomDropCm) / 100, cove = (z.coveCm ?? 0) / 100;
  return { w: r2(w), d: r2(d), areaM2: r2(area), perimeterM: r2(per), bandM: r2(band), bandM2: r2(per * band), coveM2: r2(per * cove), boardM2: r2(area + per * band + per * cove), ledM: z.led ? r2(per) : 0 };
}
type Top = { a: number; b: number; top: number; depth: number; door: boolean };
/** Golurile de pe laturile camerei, cu buiandrugul lor (m de la pardoseală) și adâncimea fâșiei din fața lor (ușa: zona foii;
 *  fereastra: 30 cm pentru cercevea și perdea). Ca openingsOnSide: pereții orizontali / verticali pe laturile camerei, fără cei de demolat. */
function openingTops(fl: Pick<Floor, 'walls'>, room: Room, side: 'N' | 'S' | 'W' | 'E'): Top[] {
  const r = room.rect, out: Top[] = [];
  for (const w of fl.walls){ if (w.status === 'demolish') continue; const [ax, az] = w.a, [bx, bz] = w.b, horiz = Math.abs(az - bz) < 1e-6;
    const on = (side === 'N' && horiz && Math.abs(az - r.z0) < 1e-6) || (side === 'S' && horiz && Math.abs(az - r.z1) < 1e-6) || (side === 'W' && !horiz && Math.abs(ax - r.x0) < 1e-6) || (side === 'E' && !horiz && Math.abs(ax - r.x1) < 1e-6);
    if (!on) continue; const dir = horiz ? Math.sign(bx - ax) : Math.sign(bz - az), s0 = horiz ? ax : az;
    for (const o of w.openings){ const p = s0 + dir * o.offset, q = s0 + dir * (o.offset + o.width), door = o.kind === 'door';
      out.push({ a: Math.min(p, q), b: Math.max(p, q), top: door ? (o.height ?? DOOR_HEIGHT) : (o.sill ?? .9) + (o.height ?? WINDOW_HEIGHT),
        depth: door ? doorZoneDepth(o, o.width) : CEILING_ZONE_RULES.windowStripM, door }); } }
  return out;
}
/** Golul cel mai înalt din dreptul casetei (fâșia din fața lui atinge caseta), sau null. */
export function openingUnder(fl: Pick<Floor, 'rooms' | 'walls'>, z: CeilingZone): { top: number; kind: 'door' | 'window' } | null {
  const room = fl.rooms.find(r => r.id === z.roomId), c = room && ceilingZoneRect(fl, z); if (!room || !c) return null;
  const r = room.rect; let best: { top: number; kind: 'door' | 'window' } | null = null;
  for (const side of ['N', 'S', 'W', 'E'] as const) for (const o of openingTops(fl, room, side)){
    const strip: RoomRect = side === 'N' ? { x0: o.a - .05, x1: o.b + .05, z0: r.z0, z1: r.z0 + o.depth } : side === 'S' ? { x0: o.a - .05, x1: o.b + .05, z0: r.z1 - o.depth, z1: r.z1 }
      : side === 'W' ? { z0: o.a - .05, z1: o.b + .05, x0: r.x0, x1: r.x0 + o.depth } : { z0: o.a - .05, z1: o.b + .05, x0: r.x1 - o.depth, x1: r.x1 };
    if (intersect(strip, c) && (!best || o.top > best.top)) best = { top: o.top, kind: o.door ? 'door' : 'window' }; }
  return best;
}
const nameOf = (z: CeilingZone) => z.label || `${r2(rectArea(z.rect))} m²`;
/** CEILING_ZONE_OUTSIDE_ROOM și CEILING_ZONE_OVERLAP (erori, ca la zonele de pardoseală); CEILING_ZONE_LOW (avertisment): partea de
 *  jos a casetei ajunge sub buiandrugul unui gol din dreptul ei + 5 cm, deci ușa (sau cerceveaua) lovește tavanul. */
export function ceilingZoneIssues(fl: Pick<Floor, 'rooms' | 'walls' | 'ceilingHeight' | 'ceilingZones'>): Issue[] {
  const out: Issue[] = [], zs = fl.ceilingZones ?? [];
  for (const z of zs){ const room = fl.rooms.find(r => r.id === z.roomId), r = room?.rect, e = 1e-6;
    if (!r || z.rect.x0 < r.x0 - e || z.rect.x1 > r.x1 + e || z.rect.z0 < r.z0 - e || z.rect.z1 > r.z1 + e)
      out.push({ code: 'CEILING_ZONE_OUTSIDE_ROOM', severity: 'ERROR', message: `Tavanul fals pe zonă ${z.label || z.id} iese din ${room?.name ?? 'camera lui'}.`, key: 'issue.CEILING_ZONE_OUTSIDE_ROOM', vars: { czone: z.id, nm: nameOf(z), rn: room?.name ?? '' } });
    const op = openingUnder(fl, z), bottom = r2(fl.ceilingHeight - z.dropCm / 100);
    if (op && bottom < op.top + CEILING_ZONE_RULES.openingClearM - 1e-9)
      out.push({ code: 'CEILING_ZONE_LOW', severity: 'WARNING', message: `Tavanul fals pe zonă ${z.label || z.id} coboară la ${Math.round(bottom * 100)} cm, sub ${op.kind === 'door' ? 'ușa' : 'fereastra'} din dreptul lui (${Math.round(op.top * 100)} cm + 5 cm): ${op.kind === 'door' ? 'ușa' : 'cerceveaua'} lovește tavanul.`,
        key: op.kind === 'door' ? 'issue.CEILING_ZONE_LOW.door' : 'issue.CEILING_ZONE_LOW.window', vars: { czone: z.id, nm: nameOf(z), h_m: bottom, top_m: op.top } }); }
  zs.forEach((a, i) => zs.slice(i + 1).forEach(b => { if (a.roomId !== b.roomId) return; const c = intersect(a.rect, b.rect);
    if (c && rectArea(c) > 1e-4) out.push({ code: 'CEILING_ZONE_OVERLAP', severity: 'ERROR', message: `Două tavane false pe zonă se suprapun (${a.label || a.id} și ${b.label || b.id}).`, key: 'issue.CEILING_ZONE_OVERLAP', vars: { czone: a.id, nm: nameOf(a), other: nameOf(b) } }); }));
  return out;
}
/** Camera ștearsă își ia tavanele false pe zonă (lista goală dispare). Modifică nivelul pe loc. */
export function removeRoomCeilingZones(fl: Pick<Floor, 'ceilingZones'>, roomId: string): void {
  if (!fl.ceilingZones) return; fl.ceilingZones = fl.ceilingZones.filter(z => z.roomId !== roomId); if (!fl.ceilingZones.length) delete fl.ceilingZones;
}
