// Camerele vecine la redimensionare (runda 8). Când o latură a unei camere se mută, camerele care o împart (latura lor
// opusă e pe aceeași linie, la cel mult 1 cm, și se suprapun pe cealaltă axă) se mută odată cu ea, ca să rămână lipite.
// Grupul se închide tranzitiv: un vecin mai lung decât latura (suprapunere parțială) își mută latura întreagă, deci și
// camerele lipite de el de aceeași parte cu camera redimensionată vin la pachet; altfel ar rămâne un gol sau o suprapunere.
// Funcții pure; pereții îi mută core/room-resize.ts o singură dată, pe toată întinderea grupului.
import type { Room, RoomRect } from './types';

export type EdgeKey = 'x0' | 'x1' | 'z0' | 'z1';
/** O cameră din grup și latura ei care stă pe linia mutată. */
export interface EdgeMember { id: string; key: EdgeKey }
/** Toleranța pentru „latură comună” (m). */
export const SHARE_TOL = .01;
/** Dimensiunea minimă a unei camere, aceeași ca la desenare (PlanView) și în câmpurile lățime/lungime (60 cm). */
export const MIN_ROOM_M = .6;

const OPP: Record<EdgeKey, EdgeKey> = { x0: 'x1', x1: 'x0', z0: 'z1', z1: 'z0' };
/** Întinderea camerei de-a lungul laturii (pe cealaltă axă). */
export const spanOf = (r: RoomRect, key: EdgeKey): [number, number] => key[0] === 'x' ? [r.z0, r.z1] : [r.x0, r.x1];
const overlap = (a: [number, number], b: [number, number]) => Math.min(a[1], b[1]) - Math.max(a[0], b[0]);

/** Camera `roomId` cu latura `key`, plus toate camerele legate de ea pe aceeași linie (vecini și vecinii vecinilor). */
export function edgeGroup(rooms: Room[], roomId: string, key: EdgeKey): EdgeMember[] {
  const start = rooms.find(r => r.id === roomId); if (!start) return [];
  const line = start.rect[key], out: EdgeMember[] = [{ id: roomId, key }], seen = new Set([roomId]);
  for (let i = 0; i < out.length; i++){
    const m = out[i]!, mr = rooms.find(r => r.id === m.id)!.rect, want = OPP[m.key];
    for (const r of rooms){
      if (seen.has(r.id) || Math.abs(r.rect[want] - line) > SHARE_TOL + 1e-9) continue;
      if (overlap(spanOf(mr, m.key), spanOf(r.rect, want)) <= SHARE_TOL) continue;   // doar colț sau alt rând
      seen.add(r.id); out.push({ id: r.id, key: want });
    }
  }
  return out;
}

/**
 * Noua poziție a liniei, limitată astfel încât niciun vecin din grup să nu scadă sub `minRoom` (unul deja mai mic nu
 * scade mai mult). Camera `roomId` nu e limitată aici: pentru ea decide redimensionarea (minimum 20 cm, refuz explicit).
 */
export function clampEdge(rooms: Room[], group: EdgeMember[], roomId: string, _key: EdgeKey, to: number, minRoom = MIN_ROOM_M): number {
  let lo = -Infinity, hi = Infinity;
  for (const m of group){
    if (m.id === roomId) continue;
    const r = rooms.find(x => x.id === m.id)!.rect, axis = m.key[0] as 'x' | 'z', size = axis === 'x' ? r.x1 - r.x0 : r.z1 - r.z0, min = Math.min(minRoom, size);
    if (m.key.endsWith('0')) hi = Math.min(hi, r[`${axis}1`] - min); else lo = Math.max(lo, r[`${axis}0`] + min);
  }
  return Math.round(Math.max(lo, Math.min(hi, to)) * 1000) / 1000;
}
