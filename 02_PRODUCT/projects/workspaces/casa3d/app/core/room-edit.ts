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

