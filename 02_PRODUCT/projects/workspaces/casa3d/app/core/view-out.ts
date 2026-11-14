// Vederea spre exterior după SR EN 17037 (voluntar): din fiecare punct al camerei, la înălțimea ochilor unui om așezat
// (1,2 m), cât de larg se vede afară (unghiul orizontal prin ferestre), cât de departe (distanța până la primul obstacol din
// fața ferestrei) și ce straturi se văd (cer, peisaj, sol). Pragurile pe niveluri (minim / mediu / ridicat): unghi 14° / 28° /
// 54°, distanță 6 / 20 / 50 m, straturi: peisajul / peisajul și încă unul / toate trei. Le-am luat din rezumatele standardului,
// nu din textul lui (NEVERIFICAT), iar nivelul camerei e cel atins în cel puțin 75 % din punctele ei (alegerea noastră).
// Ușile și pasajele nu sunt „vedere afară”; vecinii și aripile casei opresc privirea.
import type { Floor, Room } from './types';
import { slabsOf, clearOfWalls, seesSky, windowsOf, HABITABLE } from './insolation';
import { roomBoundary, pointInRoom } from './room-geometry';
import type { Obstacle } from './neighbours';

type V3 = [number, number, number];
export const EYE_Y = 1.2, VIEW_STEP_DEG = 5, VIEW_MAX_M = 100;
export const VIEW_LEVELS = { angle: [14, 28, 54], dist: [6, 20, 50] } as const;
export interface CellView { x: number; z: number; angle: number; dist: number; sky: boolean; land: boolean; ground: boolean; level: 0 | 1 | 2 | 3 }
export interface RoomView { roomId: string; name: string; type: string; habitable: boolean; windows: number;
  /** mediana pe punctele camerei */ angle: number; dist: number; layers: { sky: number; land: number; ground: number }; level: 0 | 1 | 2 | 3 }
const indoor = (r: Room) => !r.spaceKind || r.spaceKind === 'interior';
const lv = (v: number, th: readonly number[]) => (th.filter(x => v + 1e-9 >= x).length) as 0 | 1 | 2 | 3;

/** Distanța pe orizontală de la p (în direcția d) până la ieșirea din poligonul camerei. */
function leave(r: Room, p: V3, d: V3): number {
  const b = roomBoundary(r); let t = Infinity;
  for (let i = 0; i < b.length; i++){ const a = b[i]!, c = b[(i + 1) % b.length]!, sx = c[0] - a[0], sz = c[1] - a[1], den = d[0] * sz - d[2] * sx;
    if (Math.abs(den) < 1e-12) continue; const qx = a[0] - p[0], qz = a[1] - p[2], tt = (qx * sz - qz * sx) / den, u = (qx * d[2] - qz * d[0]) / den;
    if (u >= -1e-9 && u <= 1 + 1e-9 && tt > 1e-6) t = Math.min(t, tt); }
  return t;
}
/** Primul volum (vecin, etaj) tăiat de raza orizontală la înălțimea y; Infinity dacă niciunul. */
function firstObstacle(p: V3, d: V3, obs: readonly Obstacle[]): number {
  let t = Infinity;
  for (const o of obs){ if (p[1] < o.y0 || p[1] > o.y1) continue; const n = o.pts.length;
    for (let i = 0; i < n; i++){ const a = o.pts[i]!, c = o.pts[(i + 1) % n]!, sx = c[0] - a[0], sz = c[1] - a[1], den = d[0] * sz - d[2] * sx;
      if (Math.abs(den) < 1e-12) continue; const qx = a[0] - p[0], qz = a[1] - p[2], tt = (qx * sz - qz * sx) / den, u = (qx * d[2] - qz * d[0]) / den;
      if (u >= 0 && u <= 1 && tt > 0) t = Math.min(t, tt); } }
  return t;
}

/** Vederea spre exterior pe grila camerelor interioare ale nivelului. `elev` = cota pardoselii nivelului față de teren. */
export function viewOut(fl: Floor, opts: { obs?: readonly Obstacle[]; elev?: number; cellM?: number; roomId?: string } = {}): { rooms: RoomView[]; cells: (CellView & { roomId: string })[] } {
  const slabs = slabsOf(fl), H = fl.ceilingHeight, cellM = Math.max(.4, Math.min(1.5, opts.cellM ?? .8)), elev = opts.elev ?? 0, obs = opts.obs ?? [];
  const wins = windowsOf(fl), live = fl.walls.filter(w => w.status !== 'demolish');
  const xs = live.flatMap(w => [w.a[0], w.b[0]]), zs = live.flatMap(w => [w.a[1], w.b[1]]);
  const box = xs.length ? { x0: Math.min(...xs) - 1, x1: Math.max(...xs) + 1, z0: Math.min(...zs) - 1, z1: Math.max(...zs) + 1 } : null;
  const cells: (CellView & { roomId: string })[] = [], rooms: RoomView[] = [], rad = Math.PI / 180;
  if (!box) return { rooms, cells };
  // cât mai merge raza până iese din cutia casei (pereții de după fereastră: aripile)
  const exitT = (p: V3, d: V3) => { const tx = d[0] > 1e-9 ? (box.x1 - p[0]) / d[0] : d[0] < -1e-9 ? (box.x0 - p[0]) / d[0] : Infinity, tz = d[2] > 1e-9 ? (box.z1 - p[2]) / d[2] : d[2] < -1e-9 ? (box.z0 - p[2]) / d[2] : Infinity; return Math.max(0, Math.min(tx, tz)); };
  for (const r of fl.rooms.filter(x => indoor(x) && (!opts.roomId || x.id === opts.roomId))){
    const ws = wins.filter(w => w.room.id === r.id), b = roomBoundary(r), rxs = b.map(q => q[0]), rzs = b.map(q => q[1]), mine: CellView[] = [];
    const sillMin = Math.min(...ws.map(w => w.o.sill ?? .9));
    if (ws.length) for (let x = Math.min(...rxs) + cellM / 2; x < Math.max(...rxs); x += cellM) for (let z = Math.min(...rzs) + cellM / 2; z < Math.max(...rzs); z += cellM){
      if (!pointInRoom(r, x, z)) continue; const p: V3 = [x, EYE_Y, z]; let n = 0, sky = false, ground = false;
      const dists: number[] = [];
      for (let a = 0; a < 360; a += VIEW_STEP_DEG){
        const d: V3 = [Math.sin(a * rad), 0, -Math.cos(a * rad)], tl = leave(r, p, d); if (!Number.isFinite(tl)) continue;
        // prin fereastra camerei (doar ferestre) și apoi afară: nu într-o altă cameră a nivelului
        if (!clearOfWalls(p, d, slabs, tl + .6, true)) continue;
        const q: [number, number] = [p[0] + d[0] * (tl + .6), p[2] + d[2] * (tl + .6)]; if (fl.rooms.some(o => indoor(o) && pointInRoom(o, q[0], q[1]))) continue;
        n++;
        // distanța: până la o aripă a casei (pereți după fereastră) sau la un vecin / etaj
        // după fereastra camerei, o aripă a casei oprește privirea: un perete sau o altă cameră a nivelului (prin ferestrele ei
        // nu se vede „afară”, se vede în casă)
        const te = exitT(p, d); let wall = Infinity;
        for (let t = tl + 1; t <= te + .01; t += .5){ const x = p[0] + d[0] * t, z = p[2] + d[2] * t;
          if (fl.rooms.some(o => indoor(o) && pointInRoom(o, x, z)) || !clearOfWalls(p, d, slabs, t, true)){ wall = t; break; } }
        const stop = Math.min(wall, firstObstacle(p, d, obs)); dists.push(Math.min(VIEW_MAX_M, stop - tl));
        // cerul: o rază în sus prin aceeași fereastră; solul: o rază în jos, dacă parapetul e sub ochi
        if (!sky) for (const e of [8, 20, 35]){ const c = Math.cos(e * rad), u: V3 = [d[0] * c, Math.sin(e * rad), d[2] * c]; if (seesSky(p, u, slabs, fl.rooms, H, undefined, obs)){ sky = true; break; } }
        // solul: raza cea mai înclinată care mai trece peste parapet atinge terenul (cota lui e la -elev) la distanța dg pe
        // orizontală; se vede doar dacă ajunge acolo înaintea primului obstacol (vecin, etaj, aripă)
        if (!ground && sillMin < EYE_Y - .05){ const e = Math.atan((EYE_Y - sillMin) / (tl + .4)) * .9, c = Math.cos(e), u: V3 = [d[0] * c, -Math.sin(e), d[2] * c];
          const dg = (EYE_Y + elev) / Math.tan(e); if (dg > tl + .6 && dg < stop && clearOfWalls(p, u, slabs, (tl + .6) / c, true)) ground = true; } }
      // distanța de vedere a punctului: mediana direcțiilor prin care se vede afară
      const ds = dists.slice().sort((a, b) => a - b), angle = n * VIEW_STEP_DEG, land = n > 0, dist = ds.length ? Math.round(ds[Math.floor(ds.length / 2)]! * 10) / 10 : 0;
      const layers = land ? 1 + (sky ? 1 : 0) + (ground ? 1 : 0) : 0;
      const level = Math.min(lv(angle, VIEW_LEVELS.angle), lv(dist, VIEW_LEVELS.dist), layers) as 0 | 1 | 2 | 3;
      const c = { x: Math.round(x * 100) / 100, z: Math.round(z * 100) / 100, angle, dist, sky, land, ground, level }; mine.push(c); cells.push({ ...c, roomId: r.id }); }
    const med = (a: number[]) => { const s = a.slice().sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)]! : 0; };
    const share = (f: (c: CellView) => boolean) => mine.length ? Math.round(mine.filter(f).length / mine.length * 100) / 100 : 0;
    // nivelul atins în cel puțin 75 % din puncte
    const lvls = mine.map(c => c.level).sort((a, b) => b - a), level = (lvls.length ? lvls[Math.max(0, Math.ceil(lvls.length * .75) - 1)]! : 0) as 0 | 1 | 2 | 3;
    rooms.push({ roomId: r.id, name: r.name, type: r.type, habitable: HABITABLE.has(r.type), windows: ws.length, angle: med(mine.map(c => c.angle)), dist: med(mine.filter(c => c.land).map(c => c.dist)), // doar punctele din care se vede afară
      layers: { sky: share(c => c.sky), land: share(c => c.land), ground: share(c => c.ground) }, level });
  }
  return { rooms, cells };
}
