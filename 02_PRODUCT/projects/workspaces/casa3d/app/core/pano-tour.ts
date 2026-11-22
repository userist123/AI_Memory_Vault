// Turul virtual 360°: din panorama unei camere se trece în camera vecină printr-un punct pus pe ușa sau golul dintre ele.
// Panorama se randează din centrul camerei la înălțimea ochiului (viewer3d-engine.js renderPanorama); convenția unghiurilor e
// cea din panorama-engine.js: longitudinea 0 privește spre −z (sus pe plan), 90° spre +x; latitudinea pozitivă în sus. Pur.
import type { Floor, Room } from './types';

export interface TourHotspot { toRoomId: string; toName: string; lon: number; lat: number; x: number; z: number; openingId: string }
const STEP = .35;        // cât de departe de perete se caută camera de dincolo (m)
const DOOR_Y = 1.0;      // punctul se pune la mijlocul ușii (m de la pardoseală)

const inRoom = (r: Room, x: number, z: number) => x > r.rect.x0 && x < r.rect.x1 && z > r.rect.z0 && z < r.rect.z1;
export const roomCenter = (r: Room): [number, number] => [(r.rect.x0 + r.rect.x1) / 2, (r.rect.z0 + r.rect.z1) / 2];

/** Direcția (longitudine, latitudine în grade) din punctul (cx, eye, cz) spre (x, y, z), în convenția panoramei. */
export function lonLat(cx: number, cz: number, eye: number, x: number, y: number, z: number): { lon: number; lat: number } {
  const dx = x - cx, dz = z - cz, h = Math.hypot(dx, dz);
  return { lon: Math.round(Math.atan2(dx, -dz) * 180 / Math.PI * 10) / 10, lat: Math.round(Math.atan2(y - eye, h) * 180 / Math.PI * 10) / 10 };
}

/** Punctele de trecere din camera `roomId`: câte unul pe fiecare ușă sau gol de trecere care dă într-o altă cameră a nivelului. */
export function tourHotspots(floor: Pick<Floor, 'rooms' | 'walls'>, roomId: string, eye = 1.5): TourHotspot[] {
  const room = floor.rooms.find(r => r.id === roomId); if (!room) return [];
  const [cx, cz] = roomCenter(room), out: TourHotspot[] = [], seen = new Set<string>();
  for (const w of floor.walls){ const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]); if (L < 1e-6) continue; const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    for (const o of w.openings){ if (o.kind !== 'door') continue;   // golurile de trecere sunt tot „door” (cu `passage`), ferestrele nu duc nicăieri
      const m = o.offset + o.width / 2, x = w.a[0] + ux * m, z = w.a[1] + uz * m, nx = -uz, nz = ux;
      const sideA = floor.rooms.find(r => inRoom(r, x + nx * STEP, z + nz * STEP)), sideB = floor.rooms.find(r => inRoom(r, x - nx * STEP, z - nz * STEP));
      const other = sideA?.id === roomId ? sideB : sideB?.id === roomId ? sideA : undefined;
      if (!other || other.id === roomId || seen.has(other.id + '@' + o.id)) continue; seen.add(other.id + '@' + o.id);
      out.push({ toRoomId: other.id, toName: other.name, ...lonLat(cx, cz, eye, x, DOOR_Y, z), x, z, openingId: o.id }); } }
  return out.sort((a, b) => a.lon - b.lon);
}
/** Unde e punctul pe ecran (fracții 0..1 din lățime/înălțime) pentru o privire (lon0, lat0) și un câmp vizual vertical `fov`;
 *  null dacă e în spatele privitorului. Proiecție perspectivă, ca în panorama-engine.js. */
export function screenOf(h: { lon: number; lat: number }, lon0: number, lat0: number, fovDeg: number, aspect: number): { x: number; y: number } | null {
  const d = (lon: number, lat: number) => { const p = (90 - lat) * Math.PI / 180, t = lon * Math.PI / 180; return [Math.sin(p) * Math.sin(t), Math.cos(p), -Math.sin(p) * Math.cos(t)] as const; };
  const f = d(lon0, lat0), v = d(h.lon, h.lat), up = [0, 1, 0] as const;
  const cr = (a: readonly number[], b: readonly number[]) => [a[1]! * b[2]! - a[2]! * b[1]!, a[2]! * b[0]! - a[0]! * b[2]!, a[0]! * b[1]! - a[1]! * b[0]!];
  const dot = (a: readonly number[], b: readonly number[]) => a[0]! * b[0]! + a[1]! * b[1]! + a[2]! * b[2]!;
  const r = cr(f, up), rl = Math.hypot(r[0]!, r[1]!, r[2]!) || 1, right = r.map(x => x / rl), u = cr(right, f);
  const zc = dot(v, f); if (zc <= 0.05) return null;
  const k = 1 / Math.tan(fovDeg * Math.PI / 360);
  return { x: .5 + dot(v, right) / zc * k / aspect / 2, y: .5 - dot(v, u) / zc * k / 2 };
}
