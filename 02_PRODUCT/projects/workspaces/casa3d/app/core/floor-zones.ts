// Zone de pardoseală (Floor.zones): un dreptunghi într-o cameră cu alt material (gresie la bucătăria din open-space, la intrare,
// în fața șemineului). Funcții pure: suprafețe, decuparea la cameră, validări și materialul implicit.
import type { Floor, FloorZone, Issue, MaterialsCatalog, RoomRect } from './types';

const r2 = (v: number) => Math.round(v * 100) / 100;
export const rectArea = (r: RoomRect) => Math.max(0, r.x1 - r.x0) * Math.max(0, r.z1 - r.z0);
/** Intersecția a două dreptunghiuri, sau null. */
export function intersect(a: RoomRect, b: RoomRect): RoomRect | null {
  const x0 = Math.max(a.x0, b.x0), x1 = Math.min(a.x1, b.x1), z0 = Math.max(a.z0, b.z0), z1 = Math.min(a.z1, b.z1);
  return x1 - x0 > 1e-6 && z1 - z0 > 1e-6 ? { x0, x1, z0, z1 } : null;
}
export const zonesOfRoom = (fl: Pick<Floor, 'zones'>, roomId: string): FloorZone[] => (fl.zones ?? []).filter(z => z.roomId === roomId);
/** Suprafața zonei care cade în camera ei (m²); partea din afară nu se plătește de două ori. */
export function zoneAreaIn(fl: Pick<Floor, 'rooms'>, z: FloorZone): number {
  const room = fl.rooms.find(r => r.id === z.roomId), c = room && intersect(room.rect, z.rect); return c ? r2(rectArea(c)) : 0;
}
/** Suprafața tuturor zonelor din cameră (fără dublarea suprapunerilor, care sunt oricum o eroare), cel mult cât camera. */
export function zonesAreaIn(fl: Pick<Floor, 'rooms' | 'zones'>, roomId: string): number {
  const room = fl.rooms.find(r => r.id === roomId); if (!room) return 0;
  return r2(Math.min(rectArea(room.rect), zonesOfRoom(fl, roomId).reduce((a, z) => a + zoneAreaIn(fl, z), 0)));
}
/** ZONE_OUTSIDE_ROOM: zona trebuie să stea în camera ei; ZONE_OVERLAP: două zone din aceeași cameră nu se suprapun. Erori. */
export function zoneIssues(fl: Pick<Floor, 'rooms' | 'zones'>): Issue[] {
  const out: Issue[] = [], zs = fl.zones ?? [];
  for (const z of zs){ const room = fl.rooms.find(r => r.id === z.roomId), r = room?.rect, e = 1e-6;
    if (!r || z.rect.x0 < r.x0 - e || z.rect.x1 > r.x1 + e || z.rect.z0 < r.z0 - e || z.rect.z1 > r.z1 + e)
      out.push({ code: 'ZONE_OUTSIDE_ROOM', severity: 'ERROR', message: `Zona de pardoseală ${z.label || z.id} iese din ${room?.name ?? 'camera ei'}.`, key: 'issue.ZONE_OUTSIDE_ROOM', vars: { zone: z.id, nm: z.label || `${r2(rectArea(z.rect))} m²`, rn: room?.name ?? '' } }); }
  zs.forEach((a, i) => zs.slice(i + 1).forEach(b => { if (a.roomId !== b.roomId) return; const c = intersect(a.rect, b.rect);
    if (c && rectArea(c) > 1e-4) out.push({ code: 'ZONE_OVERLAP', severity: 'ERROR', message: `Două zone de pardoseală se suprapun (${a.label || a.id} și ${b.label || b.id}).`, key: 'issue.ZONE_OVERLAP', vars: { zone: a.id, nm: a.label || `${r2(rectArea(a.rect))} m²`, other: b.label || `${r2(rectArea(b.rect))} m²` } }); }));
  return out;
}
/** Materialul implicit al unei zone noi: gresie peste parchet, parchet peste gresie (contrastul obișnuit din open-space). */
export function defaultZoneMaterial(mc: Pick<MaterialsCatalog, 'materials'> | null | undefined, roomFloor: string | undefined): string | null {
  if (!mc) return null; const cur = mc.materials.find(m => m.id === roomFloor), want = cur?.category === 'floor_tile' ? 'parquet' : 'floor_tile';
  return mc.materials.find(m => m.category === want)?.id ?? mc.materials.find(m => (m.category === 'floor_tile' || m.category === 'parquet') && m.id !== roomFloor)?.id ?? null;
}
/** Camera ștearsă își ia zonele: lista fără zonele camerei (lista goală dispare, ca la stâlpi). Modifică nivelul pe loc. */
export function removeRoomZones(fl: Pick<Floor, 'zones'>, roomId: string): void {
  if (!fl.zones) return; fl.zones = fl.zones.filter(z => z.roomId !== roomId); if (!fl.zones.length) delete fl.zones;
}
