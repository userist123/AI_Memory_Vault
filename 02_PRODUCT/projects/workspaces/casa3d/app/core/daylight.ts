// Lumina naturală pe cer acoperit: factorul de lumină naturală (FLN, „daylight factor”) pe planul de lucru al fiecărei camere.
// Soarele direct (insolation.ts) răspunde la „când intră soarele”; FLN răspunde la „cât de luminoasă e camera într-o zi
// înnorată”, care la noi e iarna cea mai frecventă. Pur și determinist, fără GPU.
//
// Modelul:
// - cerul CIE complet acoperit: luminanța L(h) ∝ (1 + 2 sin h) / 3, aceeași în toate direcțiile (nu depinde de nord);
// - cerul e împărțit în 145 de petice (benzile Tregenza: 30, 30, 24, 24, 18, 12, 6 petice la 6°, 18° … 78° și zenitul);
// - componenta cerului într-un punct = suma peticelor văzute prin goluri (aceleași raze ca la soare: pereți cu glaf,
//   tavan, aripi, vecini și etajele de deasupra), raportată la iluminarea orizontală fără obstacole, înmulțită cu transmisia
//   geamului (0,7), cu murdăria (0,9) și cu partea de sticlă din gol (0,72, rama nu e modelată);
// - componenta reflectată interior: formula „split-flux” BRE, aceeași pe toată camera:
//   CRI = 0,85 · W / (A · (1 − R)) · (C · Rfw + 5 · Rcw), cu reflectanțe tipice (R 0,5, Rfw 0,4, Rcw 0,7) și C între 5 (cer
//   ascuns de vecini sau de aripile casei) și 39 (orizont liber). 0,85 e transmisia geamului clar curat din formulă, deci o
//   înlocuim cu transmisia noastră (0,7) și cu murdăria (interpretarea noastră; textul BRE nu l-am verificat).
// Țintele pe cameră (FLN mediu: bucătărie 2 %, cameră de zi 1,5 %, dormitor 1 %) sunt cele din BS 8206-2, standard retras
// (înlocuit de EN 17037): orientative, nu normă românească. Precizia față de Radiance nu e validată: ordin de mărime.
import type { Floor, Room } from './types';
import { seesSky, slabsOf, windowsOf, WINDOW_SILL, WINDOW_HEIGHT } from './insolation';
import { roomBoundary, pointInRoom, roomArea } from './room-geometry';
import type { Obstacle } from './neighbours';
import { GLASS_FRACTION } from './overheating';

type V3 = [number, number, number];
export const TAU_GLASS = .7, MAINTENANCE = .9, WORK_PLANE = .85;
export const REFL = { mean: .5, low: .4, high: .7 };
/** FLN mediu țintă pe tip de cameră (BS 8206-2, orientativ). */
export const DF_TARGETS: Record<string, number> = { bucatarie: 2, living: 1.5, dormitor: 1, birou: 1.5 };
export interface SkyPatch { dir: V3; w: number; alt: number }

/** Peticele cerului (benzile Tregenza) cu ponderea lor în iluminarea orizontală, pe cerul CIE acoperit. */
export const SKY: { patches: SkyPatch[]; total: number } = (() => {
  const bands: [number, number][] = [[6, 30], [18, 30], [30, 24], [42, 24], [54, 18], [66, 12], [78, 6]], rad = Math.PI / 180, patches: SkyPatch[] = [];
  const add = (alt: number, az: number, omega: number) => { const L = (1 + 2 * Math.sin(alt * rad)) / 3, c = Math.cos(alt * rad);
    patches.push({ alt, w: L * Math.sin(alt * rad) * omega, dir: [c * Math.sin(az * rad), Math.sin(alt * rad), -c * Math.cos(az * rad)] }); };
  for (const [alt, n] of bands){ const om = 2 * Math.PI / n * (Math.sin((alt + 6) * rad) - Math.sin((alt - 6) * rad)); for (let i = 0; i < n; i++) add(alt, (i + .5) / n * 360, om); }
  add(90, 0, 2 * Math.PI * (1 - Math.sin(84 * rad)));
  return { patches, total: patches.reduce((a, p) => a + p.w, 0) };
})();

export interface DaylightCell { x: number; z: number; df: number; roomId: string }
export interface RoomDaylight { roomId: string; name: string; type: string; mean: number; median: number; min: number; uniformity: number; sky: number; irc: number;
  target: number | null; verdict: 'ok' | 'low' | 'none' | 'na' }
export interface Daylight { cells: DaylightCell[]; rooms: RoomDaylight[]; cellM: number }
const indoor = (r: Room) => !r.spaceKind || r.spaceKind === 'interior';
const r2 = (v: number) => Math.round(v * 100) / 100;

/** Lumina la o piesă (centrul ei) față de pragul sfertului celui mai luminos al camerei (percentila 75); null dacă piesa nu
 *  stă pe grila camerei (în afara ei sau cameră fără ferestre). */
export function seatLight(df: Daylight, p: { x: number; z: number; roomId: string }){
  const cs = df.cells.filter(c => c.roomId === p.roomId); if (!cs.length) return null;
  const at = cs.reduce((b, c) => Math.hypot(c.x - p.x, c.z - p.z) < Math.hypot(b.x - p.x, b.z - p.z) ? c : b);
  if (Math.hypot(at.x - p.x, at.z - p.z) > df.cellM * 1.5) return null;
  const s = cs.map(c => c.df).sort((a, b) => a - b), best = s[Math.floor((s.length - 1) * .75)]!;
  return { at: at.df, best, ratio: best ? at.df / best : 1 };
}
/** Biroul „departe de lumină”: sub 40 % din sfertul cel mai luminos, într-o cameră în care acesta are măcar 1,5 %. */
export const darkSeat = (l: { at: number; best: number } | null) => !!l && l.best >= 1.5 && l.at < l.best * .4;
/** FLN pe grila planului de lucru (0,85 m) a fiecărei camere interioare a nivelului. */
export function daylightStudy(fl: Floor, opts: { obs?: readonly Obstacle[]; cellM?: number; roomId?: string } = {}): Daylight {
  const cellM = Math.max(.25, Math.min(1, opts.cellM ?? .4)), H = fl.ceilingHeight, slabs = slabsOf(fl), sky = TAU_GLASS * MAINTENANCE * GLASS_FRACTION * 100 / SKY.total;
  const wins = windowsOf(fl), cells: DaylightCell[] = [], rooms: RoomDaylight[] = [];
  for (const r of fl.rooms.filter(x => indoor(x) && (!opts.roomId || x.id === opts.roomId))){
    const ws = wins.filter(w => w.room.id === r.id), b = roomBoundary(r), xs = b.map(q => q[0]), zs = b.map(q => q[1]), mine: number[] = [];
    // componenta reflectată: aria de sticlă, suprafețele camerei și cât din orizont văd ferestrele (vecinii scad C)
    let glass = 0, open = 0;
    for (const { wall, o, c, nOut } of ws){ const sill = o.sill ?? WINDOW_SILL, top = Math.min(H - .05, sill + (o.height ?? WINDOW_HEIGHT)), a = o.width * Math.max(0, top - sill) * GLASS_FRACTION;
      glass += a; const p: V3 = [c[0] + nOut[0] * (wall.thickness / 2 + .05), (sill + top) / 2, c[1] + nOut[1] * (wall.thickness / 2 + .05)];
      const low = SKY.patches.filter(q => q.alt <= 30 && q.dir[0] * nOut[0] + q.dir[2] * nOut[1] > 0);
      // orizontul văzut din fața ferestrei: vecinii și aripile casei (aceleași raze, din afara peretelui)
      open += a * (low.length ? low.filter(q => seesSky(p, q.dir, slabs, fl.rooms, H, undefined, opts.obs)).length / low.length : 1); }
    const area = roomArea(r), per = b.reduce((s, q, i) => { const n = b[(i + 1) % b.length]!; return s + Math.hypot(n[0] - q[0], n[1] - q[1]); }, 0);
    const A = 2 * area + per * H, C = glass ? 5 + 34 * open / glass : 0;
    const irc = glass ? glass / (A * (1 - REFL.mean)) * (C * REFL.low + 5 * REFL.high) * TAU_GLASS * MAINTENANCE : 0;
    if (ws.length) for (let x = Math.min(...xs) + cellM / 2; x < Math.max(...xs); x += cellM) for (let z = Math.min(...zs) + cellM / 2; z < Math.max(...zs); z += cellM){
      if (!pointInRoom(r, x, z)) continue; const p: V3 = [x, WORK_PLANE, z]; let s = 0;
      for (const q of SKY.patches) if (seesSky(p, q.dir, slabs, fl.rooms, H, undefined, opts.obs)) s += q.w;
      const df = s * sky + irc; mine.push(df); cells.push({ x: r2(x), z: r2(z), df: r2(df), roomId: r.id }); }
    const sorted = mine.slice().sort((a, b) => a - b), mean = mine.length ? mine.reduce((a, v) => a + v, 0) / mine.length : 0, target = DF_TARGETS[r.type] ?? null;
    rooms.push({ roomId: r.id, name: r.name, type: r.type, mean: r2(mean), median: r2(sorted[Math.floor(sorted.length / 2)] ?? 0), min: r2(sorted[0] ?? 0),
      uniformity: mean ? r2((sorted[0] ?? 0) / mean) : 0, sky: Math.max(0, r2(mean - irc)), irc: r2(irc), target,
      verdict: target == null ? 'na' : !ws.length ? 'none' : mean + 1e-9 >= target ? 'ok' : 'low' });
  }
  return { cells, rooms, cellM };
}
