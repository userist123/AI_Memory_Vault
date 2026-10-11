// Baia ca sistem: aceleași armături peste tot (crom, negru mat, alamă, gun metal), duș walk-in sau cabină, WC suspendat sau pe
// pardoseală, faianța până la 1,2 m / 2,1 m / tavan, calorifer port-prosop și oglindă cu LED.
import type { BathSpec, MaterialsCatalog, Room, RoomFinishes, Snapshot } from './types';
import { materialOf } from './finishes';

export const METAL_COLOR: Record<NonNullable<BathSpec['metal']>, string> = { chrome: '#cfcfcf', black: '#1c1c1c', brass: '#b08d57', gunmetal: '#55585c' };
/** Cum își numesc magazinele finisajul: crom/cromat, negru (mat), alamă/auriu, grafit/gri periat/gun metal. */
export const METAL_FINISH: Record<NonNullable<BathSpec['metal']>, RegExp> = { chrome: /crom|inox/i, black: /negru|black/i, brass: /alam|auri|gold|brass/i, gunmetal: /gun|grafit|gri/i };
export const TILE_ZONE_M: Record<NonNullable<BathSpec['tileZone']>, number | undefined> = { h120: 1.2, h210: 2.1, full: undefined };
export const bathOf = (f: RoomFinishes) => ({ metal: 'chrome' as const, showerType: 'cabin' as const, wc: 'floor' as const, tileZone: 'h210' as const, ...(f.bath || {}) });
/** Înălțimea faianței din baie (`undefined` = până la tavan). */
export const bathTileHeight = (f: RoomFinishes): number | undefined => TILE_ZONE_M[bathOf(f).tileZone];

export interface BathIssue { key: string; roomId: string; vars?: Record<string, string | number> }
export function bathIssues(snap: Snapshot, mc: MaterialsCatalog, room: Room, f: RoomFinishes): BathIssue[] {
  if (room.type !== 'baie' || !f.bath) return []; const b = bathOf(f), out: BathIssue[] = [], hasShower = snap.placements.some(p => p.roomId === room.id && p.group === 'dus');
  if (b.showerType === 'walkin') out.push({ key: 'bath.walkin', roomId: room.id });
  if (b.wc === 'wall') out.push({ key: 'bath.wallFrame', roomId: room.id });
  if (hasShower && b.tileZone === 'h120') out.push({ key: 'bath.tileLow', roomId: room.id });
  const m = materialOf(mc, b.mirror), ip = m?.specs?.ip ? Number(m.specs.ip.replace(/\D/g, '').slice(-1)) : null; if (m && ip != null && ip < 4) out.push({ key: 'bath.mirrorIp', roomId: room.id, vars: { ip: m.specs!.ip! } });
  for (const id of [b.tap, b.shower]){ const m = materialOf(mc, id), fin = m?.specs?.finish; if (m && fin && !METAL_FINISH[b.metal].test(fin)) out.push({ key: 'bath.metalMismatch', roomId: room.id, vars: { finish: fin } }); }
  return out;
}
export function sanitizeBath(b: any): string | null {
  if (b === undefined) return null; if (!b || typeof b !== 'object' || Array.isArray(b)) return 'Baia este invalidă.';
  const one = (v: unknown, xs: readonly string[]) => v === undefined || xs.includes(v as string), id = (v: unknown) => v == null || (typeof v === 'string' && v.length <= 80);
  if (!one(b.metal, Object.keys(METAL_COLOR)) || !one(b.showerType, ['cabin', 'walkin']) || !one(b.wc, ['floor', 'wall']) || !one(b.tileZone, Object.keys(TILE_ZONE_M))
    || !id(b.tap) || !id(b.shower) || !id(b.towelRadiator) || !id(b.mirror)) return 'Baia este invalidă.';
  return null;
}
/** Stilul pentru piesele din baie în 3D: culoarea armăturilor, duș walk-in, WC suspendat. */
export function bathVisual(f: RoomFinishes){ if (!f.bath) return null; const b = bathOf(f); return { metal: METAL_COLOR[b.metal], walkin: b.showerType === 'walkin', wallHung: b.wc === 'wall' }; }
