// Studiul de însorire al unui nivel: câte ore primește soare direct fiecare fereastră și fiecare punct al pardoselii,
// într-o zi dată, pentru amplasamentul și orientarea proiectului. Pur și determinist (fără DOM, fără three.js).
//
// Modelul fizic (razele merg din punctul analizat spre soare):
// - pereții sunt prisme de grosimea lor, de la pardoseală la tavan; raza trece printr-un perete doar dacă tot drumul ei
//   prin grosimea peretelui rămâne în golul unei ferestre (sau al unui pasaj): glaful și buiandrugul umbresc corect;
// - ușile sunt considerate închise (opace), pereții de sticlă transparenți, pereții de demolat lipsesc (starea propusă);
// - tavanul e opac: o rază care urcă la înălțimea tavanului deasupra unei camere a nivelului e oprită;
// - corpurile clădirii (aripi, pereți exteriori) se umbresc între ele; vecinii și nivelurile de deasupra intră ca volume
//   opace (`obs`, core/neighbours.ts); streașina, copacii și mobila NU sunt modelate.
// Verificarea normei e orientativă: Ordinul MS nr. 119/2014, art. 3 (modificat prin Ordinul nr. 994/2018), cere minimum
// 1½ ore de însorire la solstițiul de iarnă pentru încăperile de locuit (camere de zi și dormitoare). Norma nu definește
// punctul de calcul; convenția noastră e centrul ferestrei (planul geamului, la mijlocul grosimii peretelui). Modul
// SR EN 17037 (`point: 'en17037'`) ia centrul orizontal, la cel puțin 1,2 m de pardoseală și 0,3 m peste parapet.
// Nu înlocuiește studiul de însorire semnat, cerut la autorizare.
import type { Floor, Room, Wall } from './types';
import { roomBoundary, pointInRoom } from './room-geometry';
import { sunPosition, sunDay, utcToLocalMin } from './solar';
import { sunVector, facing, type SiteInfo } from './site-location';
import { blocked, type Obstacle } from './neighbours';

export type { Slab };
export const WINDOW_SILL = .9, WINDOW_HEIGHT = 1.3, DOOR_H = 2.1;
export const NORM_HOURS = 1.5;
/** Ziua de referință a normei (solstițiul de iarnă); declinația soarelui variază de la un an la altul cu sub 0,01°. */
export const NORM_REFERENCE_DATE = '2026-12-21';
/** Tipurile de cameră care sunt „încăperi de locuit” în sensul normei (camere de zi și dormitoare). */
export const HABITABLE = new Set(['living', 'dormitor']);
/** SR EN 17037 (voluntar), expunerea la soare: ore pe o zi între 1 februarie și 21 martie; niveluri minim / mediu / ridicat. */
export const EN17037_DATE = '2026-03-21', EN17037_LEVELS = [1.5, 3, 4] as const;
export const en17037Level = (h: number): 0 | 1 | 2 | 3 => (EN17037_LEVELS.filter(v => h + 1e-9 >= v).length) as 0 | 1 | 2 | 3;
/** Punctul de calcul pe înălțimea ferestrei. */
export function pointY(sill: number, top: number, mode: 'center' | 'en17037' = 'center'){ return mode === 'center' ? (sill + top) / 2 : Math.min(top - .05, Math.max(1.2, sill + .3)); }
/** Spațiile fără acoperiș (terasa, balconul deschis, curtea): nu opresc razele și sunt „afară” pentru ferestre. */
const OPEN_KINDS = new Set(['open_balcony', 'terrace', 'outdoor']);
/** Spațiile din afara anvelopei (inclusiv balconul acoperit): o fereastră spre ele e o fereastră exterioară. */
const indoor = (r: Room) => !r.spaceKind || r.spaceKind === 'interior';
const roofed = (r: Room) => !OPEN_KINDS.has(r.spaceKind ?? 'interior');
type V3 = [number, number, number];
interface Aperture { u0: number; u1: number; y0: number; y1: number }
interface Slab { ax: number; az: number; ux: number; uz: number; nx: number; nz: number; len: number; half: number; e0: number; e1: number; aps: Aperture[]; bx0: number; bx1: number; bz0: number; bz1: number }

/** Pereții nivelului ca prisme cu golurile prin care trece lumina. */
export function slabsOf(fl: Floor): Slab[] {
  const H = fl.ceilingHeight, out: Slab[] = [], live = fl.walls.filter(w => w.status !== 'demolish');
  // capătul unui perete se prelungește cu jumătate de grosime doar unde întâlnește un perete în unghi (colț sau T), ca să
  // închidă colțul; la un perete care continuă în linie dreaptă prelungirea ar umbri fals fereastra de lângă îmbinare
  const corner = (w: Wall, end: [number, number]) => live.some(o => o !== w && [o.a, o.b].some(q => Math.hypot(q[0] - end[0], q[1] - end[1]) < .02)
    && Math.abs((w.b[0] - w.a[0]) * (o.b[1] - o.a[1]) - (w.b[1] - w.a[1]) * (o.b[0] - o.a[0])) / ((Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) * Math.hypot(o.b[0] - o.a[0], o.b[1] - o.a[1])) || 1) > .1);
  for (const w of live){
    const dx = w.b[0] - w.a[0], dz = w.b[1] - w.a[1], len = Math.hypot(dx, dz); if (len < 1e-6) continue;
    const ux = dx / len, uz = dz / len, half = Math.max(.02, w.thickness / 2);
    const aps: Aperture[] = w.openings.flatMap(o => o.kind === 'window' ? [{ u0: o.offset, u1: o.offset + o.width, y0: o.sill ?? WINDOW_SILL, y1: Math.min(H - .05, (o.sill ?? WINDOW_SILL) + (o.height ?? WINDOW_HEIGHT)) }]
      : o.passage ? [{ u0: o.offset, u1: o.offset + o.width, y0: 0, y1: Math.min(H - .05, o.height ?? DOOR_H) }] : []);
    const ext = half + .01;
    out.push({ ax: w.a[0], az: w.a[1], ux, uz, nx: -uz, nz: ux, len, half, aps, e0: corner(w, w.a) ? half : 0, e1: corner(w, w.b) ? half : 0,
      bx0: Math.min(w.a[0], w.b[0]) - ext, bx1: Math.max(w.a[0], w.b[0]) + ext, bz0: Math.min(w.a[1], w.b[1]) - ext, bz1: Math.max(w.a[1], w.b[1]) + ext }); }
  return out;
}

/** True dacă raza din `p` (x, y, z) pe direcția `d` (spre soare, dy > 0) ajunge la cer. `skip` = peretele de pornire. */
export function seesSky(p: V3, d: V3, slabs: Slab[], rooms: Room[], H: number, skip?: Slab, obs?: readonly Obstacle[]): boolean {
  if (d[1] <= 1e-4) return false;
  const tTop = (H - p[1]) / d[1]; if (tTop <= 0) return true; // deasupra tavanului (nu apare în practică)
  const ex = p[0] + d[0] * tTop, ez = p[2] + d[2] * tTop;
  // tavanul: punctul unde raza atinge înălțimea tavanului e deasupra unei camere acoperite a nivelului
  for (const r of rooms) if (roofed(r) && pointInRoom(r, ex, ez)) return false;
  const lx0 = Math.min(p[0], ex), lx1 = Math.max(p[0], ex), lz0 = Math.min(p[2], ez), lz1 = Math.max(p[2], ez);
  for (const s of slabs){
    if (s.bx1 < lx0 || s.bx0 > lx1 || s.bz1 < lz0 || s.bz0 > lz1) continue;
    // coordonatele razei în sistemul peretelui: u de-a lungul lui, v pe grosime
    const pu = (p[0] - s.ax) * s.ux + (p[2] - s.az) * s.uz, pv = (p[0] - s.ax) * s.nx + (p[2] - s.az) * s.nz;
    const du = d[0] * s.ux + d[2] * s.uz, dv = d[0] * s.nx + d[2] * s.nz;
    let t0 = 0, t1 = tTop;
    const clip = (o: number, k: number, lo: number, hi: number) => { if (Math.abs(k) < 1e-12){ if (o < lo || o > hi) t1 = -1; return; }
      let a = (lo - o) / k, b = (hi - o) / k; if (a > b) [a, b] = [b, a]; if (a > t0) t0 = a; if (b < t1) t1 = b; };
    clip(pv, dv, -s.half, s.half); if (t1 <= t0) continue;
    clip(pu, du, -s.e0, s.len + s.e1); if (t1 - t0 <= 1e-9) continue;
    if (s === skip) t0 = Math.max(t0, 1e-6);
    const u0 = pu + du * t0, u1 = pu + du * t1, y0 = p[1] + d[1] * t0, y1 = p[1] + d[1] * t1, e = 1e-6;
    const ok = s.aps.some(a => Math.min(u0, u1) >= a.u0 - e && Math.max(u0, u1) <= a.u1 + e && Math.min(y0, y1) >= a.y0 - e && Math.max(y0, y1) <= a.y1 + e);
    if (!ok) return false; }
  return !obs?.length || !blocked(p, d, obs);
}

export interface StudyOptions { date: string; /** volumele din afara nivelului (vecini, etajele de deasupra), în cotele nivelului */ obs?: readonly Obstacle[];
  /** punctul de calcul pe fereastră: centrul (convenția noastră pentru normă) sau cel din SR EN 17037 */ point?: 'center' | 'en17037'; /** doar ferestrele acestei camere (căutarea de remedii) */ roomId?: string; /** pasul de timp, minute */ stepMin?: number; /** latura celulei de pardoseală, m */ cellM?: number; floorGrid?: boolean }
export interface WindowSun { wallId: string; openingId: string; roomId: string; facing: string; hours: number; longestH: number; first: string | null; last: string | null; litMask: boolean[] }
export interface RoomSun { roomId: string; name: string; type: string; habitable: boolean; hours: number; longestH: number; windows: number; verdict: 'ok' | 'low' | 'none' | 'na';
  /** fracția medie a pardoselii însorite cât e soare pe cer (0–1) și cea mai bună oră */ floorShare: number; peak: string | null }
export interface FloorCell { x: number; z: number; hours: number; roomId: string }
export interface Study { date: string; times: string[]; /** pozițiile soarelui pe pași (azimut adevărat, înălțime) */ sun: { az: number; el: number }[];
  sunrise: string | null; sunset: string | null; dayLengthH: number; noonElevationDeg: number; rooms: RoomSun[]; windows: WindowSun[]; cells: FloorCell[]; cellM: number; stepMin: number }

const hhmm = (min: number) => { const m = ((Math.round(min) % 1440) + 1440) % 1440; return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`; };
/** Ora de pe ceas mutată cu `d` minute (marginea unui pas al calculului). */
const edge = (t: string, d: number) => { const [h, m] = t.split(':').map(Number); return hhmm(h! * 60 + m! + d); };
function runs(mask: boolean[]){ let best = 0, cur = 0; for (const b of mask){ cur = b ? cur + 1 : 0; if (cur > best) best = cur; } return best; }

/** Prisma fiecărui perete, după id (aceeași ordine de filtrare ca slabsOf). */
export function slabMap(fl: Floor, slabs = slabsOf(fl)): Map<string, Slab> {
  const m = new Map<string, Slab>(); let i = 0;
  for (const w of fl.walls){ if (w.status === 'demolish') continue; if (Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) >= 1e-6) m.set(w.id, slabs[i++]!); }
  return m;
}
/** Ferestrele nivelului, fiecare cu camera în care dă și normala spre exterior. */
export function windowsOf(fl: Floor){
  const out: { wall: Wall; o: Wall['openings'][number]; room: Room; c: [number, number]; nOut: [number, number] }[] = [];
  for (const w of fl.walls){ if (w.status === 'demolish') continue;
    const dx = w.b[0] - w.a[0], dz = w.b[1] - w.a[1], len = Math.hypot(dx, dz); if (len < 1e-6) continue;
    const ux = dx / len, uz = dz / len, nx = -uz, nz = ux;
    for (const o of w.openings){ if (o.kind !== 'window') continue;
      const m = o.offset + o.width / 2, cx = w.a[0] + ux * m, cz = w.a[1] + uz * m, k = w.thickness / 2 + .15;
      const plus = fl.rooms.find(r => indoor(r) && pointInRoom(r, cx + nx * k, cz + nz * k)), minus = fl.rooms.find(r => indoor(r) && pointInRoom(r, cx - nx * k, cz - nz * k));
      // fereastra interioară (cameră închisă de ambele părți) nu e o sursă de soare direct; terasa sau balconul din fața ei e „afară”
      if (plus && minus) continue; const room = plus ?? minus; if (!room) continue;
      out.push({ wall: w, o, room, c: [cx, cz], nOut: plus ? [-nx, -nz] : [nx, nz] }); } }
  return out;
}

// pașii soarelui depind doar de amplasament, zi și pas: căutarea de remedii simulează zeci de variante pe aceeași zi
const STEPS = new Map<string, { day: ReturnType<typeof sunDay>; times: string[]; dirs: V3[]; sun: Study['sun'] }>();
function sunSteps(site: SiteInfo, toUtc: (date: string, minutes: number) => Date, date: string, step: number){
  const key = `${site.lat}|${site.lon}|${site.northDeg}|${site.tz}|${date}|${step}|${toUtc(date, 720).getTime()}`, hit = STEPS.get(key); if (hit) return hit;
  const day = sunDay(toUtc(date, 720), site.lat, site.lon), times: string[] = [], dirs: V3[] = [], sun: Study['sun'] = [];
  for (let m = step / 2; m < 1440; m += step){ const s = sunPosition(toUtc(date, m), site.lat, site.lon);
    if (s.elevationDeg <= 0) continue; times.push(hhmm(m)); sun.push({ az: Math.round(s.azimuthDeg * 10) / 10, el: Math.round(s.elevationDeg * 10) / 10 });
    dirs.push(sunVector(s.azimuthDeg, s.elevationDeg, site) as V3); }
  if (STEPS.size > 32) STEPS.clear(); const v = { day, times, dirs, sun }; STEPS.set(key, v); return v;
}
/** Studiul complet pentru o zi. Momentele sunt în ora locală a amplasamentului. */
export function insolationStudy(fl: Floor, site: SiteInfo, toUtc: (date: string, minutes: number) => Date, opts: StudyOptions): Study {
  const step = Math.max(2, Math.min(60, opts.stepMin ?? 10)), cellM = Math.max(.15, Math.min(1, opts.cellM ?? .3)), H = fl.ceilingHeight;
  const slabs = slabsOf(fl), rooms = fl.rooms, wins = windowsOf(fl).filter(w => !opts.roomId || w.room.id === opts.roomId);
  const st = sunSteps(site, toUtc, opts.date, step), day = st.day, dirs = st.dirs, times = st.times.slice(), sun = st.sun.slice(); // copii: cache-ul nu se modifică din afară
  const stepH = step / 60, slabByWall = slabMap(fl, slabs);
  const windows: WindowSun[] = wins.map(({ wall, o, room, c, nOut }) => {
    const sill = o.sill ?? WINDOW_SILL, top = Math.min(H - .05, sill + (o.height ?? WINDOW_HEIGHT)), p: V3 = [c[0], pointY(sill, top, opts.point), c[1]];
    const litMask = dirs.map(d => !!d && d[0] * nOut[0] + d[2] * nOut[1] > 1e-4 && seesSky(p, d, slabs, rooms, H, slabByWall.get(wall.id), opts.obs));
    const fi = litMask.indexOf(true), li = litMask.lastIndexOf(true);
    return { wallId: wall.id, openingId: o.id, roomId: room.id, facing: facing(nOut[0], nOut[1], site), hours: round2(litMask.filter(Boolean).length * stepH), longestH: round2(runs(litMask) * stepH),
      first: fi >= 0 ? edge(times[fi]!, -step / 2) : null, last: li >= 0 ? edge(times[li]!, step / 2) : null, litMask }; });
  const cells: FloorCell[] = [], share = new Map<string, number[]>();
  if (opts.floorGrid !== false) for (const r of rooms.filter(x => !opts.roomId || x.id === opts.roomId)){ const b = roomBoundary(r), xs = b.map(q => q[0]), zs = b.map(q => q[1]);
    const lit = new Array(dirs.length).fill(0); let n = 0;
    for (let x = Math.min(...xs) + cellM / 2; x < Math.max(...xs); x += cellM) for (let z = Math.min(...zs) + cellM / 2; z < Math.max(...zs); z += cellM){
      if (!pointInRoom(r, x, z)) continue; n++; let k = 0;
      dirs.forEach((d, i) => { if (d && seesSky([x, .01, z], d, slabs, rooms, H, undefined, opts.obs)){ k++; lit[i]++; } });
      cells.push({ x: round2(x), z: round2(z), hours: round2(k * stepH), roomId: r.id }); }
    share.set(r.id, n ? lit.map(v => v / n) : []); }
  const roomsOut: RoomSun[] = rooms.filter(x => !opts.roomId || x.id === opts.roomId).map(r => {
    const ws = windows.filter(w => w.roomId === r.id), any = dirs.map((_, i) => ws.some(w => w.litMask[i]));
    const hours = round2(any.filter(Boolean).length * stepH), longestH = round2(runs(any) * stepH), habitable = HABITABLE.has(r.type);
    const sh = share.get(r.id) ?? [], lit = sh.filter(v => v > 0), pi = sh.length ? sh.indexOf(Math.max(...sh)) : -1;
    return { roomId: r.id, name: r.name, type: r.type, habitable, hours, longestH, windows: ws.length,
      verdict: !habitable ? 'na' : !ws.length ? 'none' : hours + 1e-9 >= NORM_HOURS ? 'ok' : 'low',
      floorShare: lit.length ? round2(lit.reduce((a, v) => a + v, 0) / lit.length) : 0, peak: pi >= 0 && sh[pi]! > 0 ? times[pi]! : null }; });
  const loc = (d: Date | null) => d ? hhmm(utcToLocalMin(d, site.tz)) : null; // ora de pe ceas, corectă și în ziua schimbării orei
  return { date: opts.date, times, sun, sunrise: loc(day.sunrise), sunset: loc(day.sunset), dayLengthH: round2(day.dayLengthH), noonElevationDeg: Math.round(day.noonElevationDeg * 10) / 10,
    rooms: roomsOut, windows, cells, cellM, stepMin: step };
}
const round2 = (v: number) => Math.round(v * 100) / 100;

/** Petele de soare pe pardoseală în momentul dat: proiecția fiecărei ferestre prin ambele fețe ale peretelui (poligon convex). */
export function sunPatches(fl: Floor, d: V3): { roomId: string; pts: [number, number][] }[] {
  if (d[1] <= 1e-4) return []; const H = fl.ceilingHeight, out: { roomId: string; pts: [number, number][] }[] = [];
  for (const { wall, o, room, nOut } of windowsOf(fl)){ if (d[0] * nOut[0] + d[2] * nOut[1] <= 1e-4) continue;
    const dx = wall.b[0] - wall.a[0], dz = wall.b[1] - wall.a[1], len = Math.hypot(dx, dz), ux = dx / len, uz = dz / len, half = wall.thickness / 2;
    const sill = o.sill ?? WINDOW_SILL, top = Math.min(H - .05, sill + (o.height ?? WINDOW_HEIGHT));
    const proj = (u: number, y: number, side: number): [number, number] => { const x = wall.a[0] + ux * u + nOut[0] * half * side, z = wall.a[1] + uz * u + nOut[1] * half * side, t = y / d[1]; return [x - d[0] * t, z - d[2] * t]; };
    const quad = (side: number) => [proj(o.offset, sill, side), proj(o.offset + o.width, sill, side), proj(o.offset + o.width, top, side), proj(o.offset, top, side)];
    let poly = clipConvex(quad(-1), quad(1)); // lumina trece doar prin ambele fețe ale golului (glaful umbrește)
    poly = clipConvex(poly, roomBoundaryConvex(room)); if (poly.length >= 3) out.push({ roomId: room.id, pts: poly.map(q => [round2(q[0]), round2(q[1])]) }); }
  return out;
}
const roomBoundaryConvex = (r: Room): [number, number][] => r.boundary?.length ? [[r.rect.x0, r.rect.z0], [r.rect.x1, r.rect.z0], [r.rect.x1, r.rect.z1], [r.rect.x0, r.rect.z1]] : roomBoundary(r) as [number, number][];
/** Sutherland–Hodgman: poligonul `a` tăiat de poligonul convex `b` (orice sens de parcurgere). */
export function clipConvex(a: [number, number][], b: [number, number][]): [number, number][] {
  if (a.length < 3 || b.length < 3) return []; let out = a;
  const area = b.reduce((s, p, i) => { const q = b[(i + 1) % b.length]!; return s + p[0] * q[1] - q[0] * p[1]; }, 0), sgn = area >= 0 ? 1 : -1;
  for (let i = 0; i < b.length && out.length; i++){ const p = b[i]!, q = b[(i + 1) % b.length]!, inside = (c: [number, number]) => sgn * ((q[0] - p[0]) * (c[1] - p[1]) - (q[1] - p[1]) * (c[0] - p[0])) >= -1e-9;
    const inp = out; out = [];
    for (let j = 0; j < inp.length; j++){ const c = inp[j]!, prev = inp[(j + inp.length - 1) % inp.length]!, ci = inside(c), pi = inside(prev);
      if (ci !== pi){ const x1 = prev[0], y1 = prev[1], x2 = c[0], y2 = c[1], x3 = p[0], y3 = p[1], x4 = q[0], y4 = q[1], den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.abs(den) > 1e-12){ const t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den; out.push([x1 + t * (x2 - x1), y1 + t * (y2 - y1)]); } }
      if (ci) out.push(c); } }
  return out;
}
