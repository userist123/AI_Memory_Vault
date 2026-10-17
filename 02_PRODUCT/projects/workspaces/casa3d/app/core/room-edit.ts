// Ajutoare pure pentru editarea camerelor din plan (runda 9): o editare fără efect nu lasă un pas de anulare, iar mânerul
// de latură al camerei alese stă pe porțiunea liberă cea mai lungă a laturii, nu peste o ușă sau o fereastră.
import type { Floor, RoomRect } from './types';
import { r3, wallLength } from './geometry';
import { resizeRoom } from './room-resize';
import type { EdgeKey } from './room-neighbours';

/** Egalitate structurală, fără să conteze ordinea cheilor (JSON-ul unui obiect refăcut prin spread poate diferi). */
export function sameState(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null) return typeof a === 'number' && typeof b === 'number' && Number.isNaN(a) && Number.isNaN(b);
  if (Array.isArray(a) !== Array.isArray(b)) return false;
  if (Array.isArray(a)){ const B = b as unknown[]; return a.length === B.length && a.every((x, i) => sameState(x, B[i])); }
  const A = a as Record<string, unknown>, B = b as Record<string, unknown>;
  const ka = Object.keys(A).filter(k => A[k] !== undefined), kb = Object.keys(B).filter(k => B[k] !== undefined);
  return ka.length === kb.length && ka.every(k => Object.prototype.hasOwnProperty.call(B, k) && sameState(A[k], B[k]));
}

/**
 * Redimensionarea din panou (lățime/lungime), cu vecinii: `changed` e fals când rezultatul e identic cu planul de acum (de ex.
 * vecinul e deja la 60 cm), caz în care Editorul nu înregistrează niciun pas de anulare; `clamped` spune că dreptunghiul
 * obținut diferă de cel cerut. Aruncă RoomResizeError, ca resizeRoom.
 */
export function planRoomResize(floor: Floor, roomId: string, rect: RoomRect, newId: () => string): { floor: Floor; changed: boolean; clamped: boolean } {
  const f = resizeRoom(floor, roomId, rect, newId, { neighbours: true });
  const got = f.rooms.find(r => r.id === roomId)?.rect;
  const clamped = !!got && (got.x0 !== r3(Math.min(rect.x0, rect.x1)) || got.x1 !== r3(Math.max(rect.x0, rect.x1)) || got.z0 !== r3(Math.min(rect.z0, rect.z1)) || got.z1 !== r3(Math.max(rect.z0, rect.z1)));
  return { floor: f, changed: !sameState(f, floor), clamped };
}

const TOL = .03;

/**
 * Poziția mânerului de latură (coordonata de-a lungul laturii): mijlocul celei mai lungi porțiuni a laturii neacoperite de
 * goluri (uși, ferestre) de pe pereții de pe linia ei, fiecare gol lărgit cu `margin` (raza zonei de apucare). La egalitate
 * câștigă porțiunea cea mai apropiată de mijloc. Dacă zona de apucare nu încape nicăieri, mijlocul celui mai lung spațiu
 * dintre goluri (fără margine); dacă golurile acoperă toată latura, mijlocul laturii.
 */
function edgeSpans(floor: Floor, rect: RoomRect, key: EdgeKey) {
  const vertical = key[0] === 'x', line = rect[key], lo = vertical ? rect.z0 : rect.x0, hi = vertical ? rect.z1 : rect.x1, mid = (lo + hi) / 2;
  const ops: [number, number][] = [];
  for (const w of floor.walls){
    const L = wallLength(w.a, w.b); if (L < .01 || !w.openings.length) continue;
    const ca = vertical ? w.a[0] : w.a[1], cb = vertical ? w.b[0] : w.b[1]; if (Math.abs(ca - line) > TOL || Math.abs(cb - line) > TOL) continue;
    const oa = vertical ? w.a[1] : w.a[0], ob = vertical ? w.b[1] : w.b[0], dir = (ob - oa) / L;
    for (const o of w.openings){ const p = oa + dir * o.offset, q = oa + dir * (o.offset + o.width); ops.push([Math.min(p, q), Math.max(p, q)]); }
  }
  ops.sort((a, b) => a[0] - b[0]);
  const longestFree = (m: number): [number, number] | null => {
    const free: [number, number][] = []; let cur = lo;
    for (const [s, e] of ops){ if (s - m > cur) free.push([cur, Math.min(s - m, hi)]); cur = Math.max(cur, e + m); if (cur >= hi) break; }
    if (cur < hi) free.push([cur, hi]);
    let best: [number, number] | null = null; const dm = (x: [number, number]) => Math.abs((x[0] + x[1]) / 2 - mid);
    for (const f of free){ const len = f[1] - f[0], bl = best ? best[1] - best[0] : 0; if (len <= 1e-9) continue;
      if (!best || len > bl + 1e-9 || (Math.abs(len - bl) <= 1e-9 && dm(f) < dm(best))) best = f; }
    return best;
  };
  return { mid, longestFree };
}

export function edgeHandleAt(floor: Floor, rect: RoomRect, key: EdgeKey, margin: number): number {
  const { mid, longestFree } = edgeSpans(floor, rect, key);
  // fără loc pentru toată zona de apucare: măcar între goluri (cel mai lung spațiu dintre ele), abia apoi mijlocul
  const best = longestFree(margin) ?? longestFree(0);
  return best ? r3((best[0] + best[1]) / 2) : r3(mid);
}

/** True când latura are cel puțin o porțiune liberă în afara golurilor (uși/ferestre). */
export function edgeHasFreeSpan(floor: Floor, rect: RoomRect, key: EdgeKey): boolean {
  return edgeSpans(floor, rect, key).longestFree(0) !== null;
}

/**
 * Coordonatele (x, z) ale mânerului de latură: când un gol acoperă complet latura (`!edgeHasFreeSpan`),
 * mânerul este decalat perpendicular în afara camerei cu `outwardOffset`, ca să nu se suprapună peste ușă.
 */
export function edgeHandlePoint(floor: Floor, rect: RoomRect, key: EdgeKey, margin = 0.25, outwardOffset = 0): { x: number; z: number; offset: boolean } {
  const v = key[0] === 'x', along = edgeHandleAt(floor, rect, key, margin), free = edgeHasFreeSpan(floor, rect, key);
  const shift = !free && outwardOffset > 0;
  const offX = shift ? (key === 'x0' ? -outwardOffset : key === 'x1' ? outwardOffset : 0) : 0;
  const offZ = shift ? (key === 'z0' ? -outwardOffset : key === 'z1' ? outwardOffset : 0) : 0;
  return {
    x: r3((v ? rect[key] : along) + offX),
    z: r3((v ? along : rect[key]) + offZ),
    offset: shift,
  };
}

const MIN_SPLIT_SIDE = 0.6;

/**
 * Împarte o cameră în două camere adiacente printr-un perete interior pe axa `x` (verticală pe plan) sau `z` (orizontală pe plan).
 * Refuză împărțirea dacă oricare din camerele rezultate ar avea latura sub 60 cm (`MIN_SPLIT_SIDE`).
 */
export function splitRoom(
  floor: Floor,
  roomId: string,
  axis: 'x' | 'z',
  at: number,
  newRoom: { id: string; name: string; type: string },
  newWallId: string,
): Floor {
  const room = floor.rooms.find(r => r.id === roomId);
  if (!room) throw new Error(`Camera inexistentă: ${roomId}`);
  const c = r3(at), { x0, z0, x1, z1 } = room.rect;
  if (axis === 'x') {
    if (c - x0 < MIN_SPLIT_SIDE - 1e-6 || x1 - c < MIN_SPLIT_SIDE - 1e-6) throw new Error('Împărțirea lasă o cameră sub 60 cm lățime.');
    const rA = { ...room, rect: { x0, z0, x1: c, z1 } };
    const rB = { id: newRoom.id, name: newRoom.name, type: newRoom.type, rect: { x0: c, z0, x1, z1 } };
    const partWall = { id: newWallId, a: [c, z0] as [number, number], b: [c, z1] as [number, number], thickness: 0.125, exterior: false, openings: [] };
    return { ...floor, rooms: floor.rooms.flatMap(r => (r.id === roomId ? [rA, rB] : [r])), walls: [...floor.walls, partWall] };
  }
  if (c - z0 < MIN_SPLIT_SIDE - 1e-6 || z1 - c < MIN_SPLIT_SIDE - 1e-6) throw new Error('Împărțirea lasă o cameră sub 60 cm adâncime.');
  const rA = { ...room, rect: { x0, z0, x1, z1: c } };
  const rB = { id: newRoom.id, name: newRoom.name, type: newRoom.type, rect: { x0, z0: c, x1, z1 } };
  const partWall = { id: newWallId, a: [x0, c] as [number, number], b: [x1, c] as [number, number], thickness: 0.125, exterior: false, openings: [] };
  return { ...floor, rooms: floor.rooms.flatMap(r => (r.id === roomId ? [rA, rB] : [r])), walls: [...floor.walls, partWall] };
}

/**
 * Unește două camere adiacente care împart complet o latură comună, păstrând `roomAId` și eliminând peretele interior despărțitor.
 */
export function mergeRooms(floor: Floor, roomAId: string, roomBId: string): Floor {
  if (roomAId === roomBId) throw new Error('Nu se poate uni o cameră cu ea însăși.');
  const a = floor.rooms.find(r => r.id === roomAId), b = floor.rooms.find(r => r.id === roomBId);
  if (!a || !b) throw new Error('Camerele de unit nu există.');
  const ra = a.rect, rb = b.rect;
  const shareX = (Math.abs(ra.x1 - rb.x0) < TOL || Math.abs(rb.x1 - ra.x0) < TOL) && Math.abs(ra.z0 - rb.z0) < TOL && Math.abs(ra.z1 - rb.z1) < TOL;
  const shareZ = (Math.abs(ra.z1 - rb.z0) < TOL || Math.abs(rb.z1 - ra.z0) < TOL) && Math.abs(ra.x0 - rb.x0) < TOL && Math.abs(ra.x1 - rb.x1) < TOL;
  if (!shareX && !shareZ) throw new Error('Camerele nu împart o latură comună completă.');
  const mergedRect: RoomRect = {
    x0: r3(Math.min(ra.x0, rb.x0)),
    z0: r3(Math.min(ra.z0, rb.z0)),
    x1: r3(Math.max(ra.x1, rb.x1)),
    z1: r3(Math.max(ra.z1, rb.z1)),
  };
  const sharedCoord = shareX ? (Math.abs(ra.x1 - rb.x0) < TOL ? ra.x1 : rb.x1) : (Math.abs(ra.z1 - rb.z0) < TOL ? ra.z1 : rb.z1);
  const walls = floor.walls.filter(w => {
    if (w.exterior) return true;
    if (shareX) {
      const onLine = Math.abs(w.a[0] - sharedCoord) < TOL && Math.abs(w.b[0] - sharedCoord) < TOL;
      const inSpan = Math.min(w.a[1], w.b[1]) >= mergedRect.z0 - TOL && Math.max(w.a[1], w.b[1]) <= mergedRect.z1 + TOL;
      return !(onLine && inSpan);
    }
    const onLine = Math.abs(w.a[1] - sharedCoord) < TOL && Math.abs(w.b[1] - sharedCoord) < TOL;
    const inSpan = Math.min(w.a[0], w.b[0]) >= mergedRect.x0 - TOL && Math.max(w.a[0], w.b[0]) <= mergedRect.x1 + TOL;
    return !(onLine && inSpan);
  });
  return {
    ...floor,
    rooms: floor.rooms.filter(r => r.id !== roomBId).map(r => (r.id === roomAId ? { ...r, rect: mergedRect } : r)),
    walls,
  };
}
