// Vecinii în studiul de însorire: clădirile din jur ca volume (dreptunghi în plan + înălțime), în ambele sensuri cerute de
// Ordinul MS nr. 119/2014, art. 3: (1) încăperile de locuit „din clădire și din locuințele învecinate” trebuie să aibă 1½ h
// de soare la solstițiul de iarnă; (2) studiul de însorire e obligatoriu când distanța dintre clădiri e cel mult înălțimea
// celei mai înalte (D ≤ H_max); (3) față de un calcan (fațadă fără ferestre) se verifică doar pereții cu ferestre, cu
// respectarea dreptului la însorire al celui mai vechi amplasament.
//
// Convenții: coordonatele vecinului sunt cele ale planului (m), pe teren (cota 0 = pardoseala parterului nostru). Axa
// „adâncimii” vecinului (rot, în sensul acelor de ceasornic de la „sus pe plan”) pleacă de la casa noastră: fațada din față
// (spre noi) e latura de la -d/2. Ferestrele-țintă ale vecinului sunt puse automat pe fațada din față, pe etaje de 3 m;
// nu le cunoaștem poziția reală, deci verificăm o grilă și raportăm punctul cel mai defavorabil.
import type { Floor, Room, Snapshot } from './types';
import { roomBoundary } from './room-geometry';
import { floors, elevationOf, SLAB } from './levels';
import { sunPosition } from './solar';
import { sunVector, type SiteInfo } from './site-location';

export interface Neighbour { id: string; name: string; /** centrul în plan (m) */ x: number; z: number; /** lățimea fațadei din față și adâncimea (m) */ w: number; d: number;
  /** rotația (°): direcția din casă spre vecin, față de „sus pe plan” */ rot: number; /** înălțimea la cornișă (m) */ h: number;
  /** `home` = are locuințe (protejate de normă); `other` = birouri, depozit, garaj */ kind: 'home' | 'other';
  /** amplasamentul mai vechi (clădire existentă înaintea proiectului nostru): are prioritate la dreptul de însorire */ older: boolean;
  /** fațada spre noi e calcan (fără ferestre): nu are ferestre de protejat (alin. 3) */ calcan: boolean }
/** Un volum opac, extrudat vertical: poligon în plan, între cotele y0 și y1 (în coordonatele nivelului studiat). */
export interface Obstacle { pts: [number, number][]; y0: number; y1: number; bx0: number; bx1: number; bz0: number; bz1: number }
type V3 = [number, number, number];
export const MAX_NEIGHBOURS = 12, STOREY_M = 3;
const rad = (d: number) => d * Math.PI / 180;

export function obstacle(pts: [number, number][], y0: number, y1: number): Obstacle {
  const xs = pts.map(p => p[0]), zs = pts.map(p => p[1]);
  return { pts, y0, y1, bx0: Math.min(...xs), bx1: Math.max(...xs), bz0: Math.min(...zs), bz1: Math.max(...zs) };
}
/** Axele vecinului: `dep` = de la casă spre el, `wid` = de-a lungul fațadei din față. */
export function axes(n: Pick<Neighbour, 'rot'>){ const a = rad(n.rot); return { dep: [Math.sin(a), -Math.cos(a)] as [number, number], wid: [Math.cos(a), Math.sin(a)] as [number, number] }; }
/** Colțurile vecinului; primele două sunt fațada din față (spre noi). */
export function corners(n: Neighbour): [number, number][] {
  const { dep, wid } = axes(n), p = (s: number, t: number): [number, number] => [n.x + wid[0] * s * n.w / 2 + dep[0] * t * n.d / 2, n.z + wid[1] * s * n.w / 2 + dep[1] * t * n.d / 2];
  return [p(-1, -1), p(1, -1), p(1, 1), p(-1, 1)];
}

/** True dacă raza din p pe direcția d (dy > 0) trece prin volum. Funcționează și pentru poligoane concave. */
export function hits(p: V3, d: V3, o: Obstacle): boolean {
  if (d[1] <= 1e-6 || p[1] >= o.y1) return false;
  const tEnd = (o.y1 - p[1]) / d[1], tBeg = Math.max(0, (o.y0 - p[1]) / d[1]); // raza e în banda de înălțime a volumului doar între tBeg și tEnd
  const ex = p[0] + d[0] * tEnd, ez = p[2] + d[2] * tEnd;
  if (Math.max(p[0], ex) < o.bx0 || Math.min(p[0], ex) > o.bx1 || Math.max(p[2], ez) < o.bz0 || Math.min(p[2], ez) > o.bz1) return false;
  const ts: number[] = [tBeg, tEnd], n = o.pts.length;
  for (let i = 0; i < n; i++){ const a = o.pts[i]!, b = o.pts[(i + 1) % n]!, sx = b[0] - a[0], sz = b[1] - a[1], den = d[0] * sz - d[2] * sx;
    if (Math.abs(den) < 1e-12) continue; const qx = a[0] - p[0], qz = a[1] - p[2], t = (qx * sz - qz * sx) / den, u = (qx * d[2] - qz * d[0]) / den;
    if (u >= 0 && u <= 1 && t > tBeg && t < tEnd) ts.push(t); }
  ts.sort((a, b) => a - b);
  for (let i = 0; i + 1 < ts.length; i++){ const tm = (ts[i]! + ts[i + 1]!) / 2; if (ts[i + 1]! - ts[i]! < 1e-9) continue;
    if (inPoly(o.pts, p[0] + d[0] * tm, p[2] + d[2] * tm)) return true; }
  return false;
}
export function inPoly(pts: [number, number][], x: number, z: number): boolean {
  let c = false; for (let i = 0, j = pts.length - 1; i < pts.length; j = i++){ const a = pts[i]!, b = pts[j]!;
    if ((a[1] > z) !== (b[1] > z) && x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]) c = !c; }
  return c;
}
export const blocked = (p: V3, d: V3, obs: readonly Obstacle[]) => obs.some(o => hits(p, d, o));

/** Volumul casei noastre (toate nivelurile), pe teren: camerele acoperite cu placa de deasupra, spațiile deschise doar cu placa lor. */
export function houseMass(s: Snapshot): Obstacle[] {
  return floors(s).flatMap((f, i) => { const e = elevationOf(s, i);
    return f.rooms.map(r => obstacle(roomBoundary(r) as [number, number][], i ? e - SLAB : e, roofedRoom(r) ? e + f.ceilingHeight + SLAB : e)).filter(o => o.y1 > o.y0); });
}
const roofedRoom = (r: Room) => !['open_balcony', 'terrace', 'outdoor'].includes(r.spaceKind ?? 'interior');
/** Ce umbrește nivelul `i` din afara lui: vecinii și nivelurile de deasupra ale casei (cotele relative la pardoseala nivelului). */
export function obstaclesFor(s: Snapshot, i: number, site: Pick<SiteInfo, 'neighbors'> | null): Obstacle[] {
  const e = elevationOf(s, i), out: Obstacle[] = (site?.neighbors ?? []).map(n => obstacle(corners(n), -e, n.h - e));
  floors(s).forEach((f, k) => { if (k <= i) return; const ek = elevationOf(s, k);
    for (const r of f.rooms) out.push(obstacle(roomBoundary(r) as [number, number][], ek - SLAB - e, (roofedRoom(r) ? ek + f.ceilingHeight + SLAB : ek) - e)); });
  return out;
}

/** Conturul casei pe teren (cutia tuturor camerelor) și înălțimea ei (cel mai înalt nivel + placa). */
export function houseBox(s: Snapshot){
  const pts = floors(s).flatMap(f => f.rooms.flatMap(r => roomBoundary(r))), xs = pts.map(p => p[0]), zs = pts.map(p => p[1]), fl = floors(s);
  if (!pts.length) return null;
  const top = Math.max(...fl.map((f, i) => f.rooms.length ? elevationOf(s, i) + f.ceilingHeight + SLAB : 0));
  return { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs), h: Math.round(top * 100) / 100 };
}
/** Un vecin nou pe direcția adevărată `trueAz` (0 = nord), la distanța `dist` de casă. */
export function placeNeighbour(s: Snapshot, site: Pick<SiteInfo, 'northDeg'>, o: { id: string; name: string; trueAz: number; dist: number; w: number; d: number; h: number }): Neighbour {
  const b = houseBox(s) ?? { x0: 0, x1: 6, z0: 0, z1: 6, h: 3 }, rot = ((o.trueAz - site.northDeg) % 360 + 360) % 360;
  const n: Neighbour = { id: o.id, name: o.name, x: 0, z: 0, w: o.w, d: o.d, rot, h: o.h, kind: 'home', older: true, calcan: false };
  return moveTo(n, s, o.dist, b);
}
/** Mută vecinul pe direcția lui până la distanța liberă `dist` față de casă (căutare pe rază, precizie 1 cm). */
export function moveTo(n: Neighbour, s: Snapshot, dist: number, box = houseBox(s)): Neighbour {
  const b = box ?? { x0: 0, x1: 6, z0: 0, z1: 6, h: 3 }, cx = (b.x0 + b.x1) / 2, cz = (b.z0 + b.z1) / 2, { dep } = axes(n);
  const at = (k: number) => ({ ...n, x: r2(cx + dep[0] * k), z: r2(cz + dep[1] * k) });
  // „lipit” (distanța 0, calcan pe limită) = la 5 mm, nu suprapus: gap() dă 0 și pentru volumele care se suprapun
  const want = Math.max(dist, .005);
  let lo = 0, hi = 500 + n.d + Math.hypot(b.x1 - b.x0, b.z1 - b.z0); for (let i = 0; i < 50; i++){ const m = (lo + hi) / 2; if (gap(at(m), b) < want) lo = m; else hi = m; }
  return at(hi);
}
const r2 = (v: number) => Math.round(v * 100) / 100;
/** Distanța liberă (în plan) dintre vecin și conturul casei; 0 dacă se suprapun. */
export function gap(n: Neighbour, b: { x0: number; x1: number; z0: number; z1: number }): number {
  const P = corners(n), Q: [number, number][] = [[b.x0, b.z0], [b.x1, b.z0], [b.x1, b.z1], [b.x0, b.z1]];
  if (P.some(p => p[0] >= b.x0 && p[0] <= b.x1 && p[1] >= b.z0 && p[1] <= b.z1) || Q.some(q => inPoly(P, q[0], q[1]))) return 0;
  let m = Infinity; for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++){ const a = P[i]!, a2 = P[(i + 1) % 4]!, c = Q[j]!, c2 = Q[(j + 1) % 4]!;
    if (cross(a, a2, c, c2)) return 0; // laturi care se taie fără niciun colț înăuntru (vecin pus în cruce peste casă)
    m = Math.min(m, segDist(a, a2, c), segDist(c, c2, a)); }
  return m;
}
function cross(a: [number, number], b: [number, number], c: [number, number], d: [number, number]){
  const o = (p: [number, number], q: [number, number], r: [number, number]) => Math.sign((q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0]));
  return o(a, b, c) * o(a, b, d) < 0 && o(c, d, a) * o(c, d, b) < 0; }
function segDist(a: [number, number], b: [number, number], p: [number, number]){ const dx = b[0] - a[0], dz = b[1] - a[1], L = dx * dx + dz * dz;
  const t = L ? Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L)) : 0; return Math.hypot(a[0] + dx * t - p[0], a[1] + dz * t - p[1]); }

export interface Trigger { neighbourId: string; name: string; distM: number; hMaxM: number; required: boolean }
/** Alin. (2): studiul de însorire e obligatoriu când distanța dintre clădiri ≤ înălțimea celei mai înalte. */
export function studyTriggers(s: Snapshot, site: Pick<SiteInfo, 'neighbors'>): Trigger[] {
  const b = houseBox(s); if (!b) return [];
  return (site.neighbors ?? []).map(n => { const D = r2(gap(n, b)), H = r2(Math.max(b.h, n.h)); return { neighbourId: n.id, name: n.name, distM: D, hMaxM: H, required: D <= H + 1e-9 }; });
}

export interface TargetSun { u: number; y: number; before: number; after: number }
export interface NeighbourSun { neighbourId: string; name: string; status: 'ok' | 'affected' | 'already' | 'calcan' | 'other'; older: boolean;
  /** cel mai defavorabil punct al fațadei: orele fără casa noastră și cu ea */ worstBefore: number; worstAfter: number; affected: number; targets: TargetSun[] }
/** Ferestrele-țintă de pe fațada din față a vecinului: la fiecare ~2,5 m pe lățime, în mijlocul fiecărui etaj (1,5 m peste pardoseală). */
export function targetsOf(n: Neighbour): { p: V3; u: number; nOut: [number, number] }[] {
  const { dep, wid } = axes(n), cols = Math.max(1, Math.min(7, Math.round(n.w / 2.5))), rows = Math.max(1, Math.min(8, Math.floor(n.h / STOREY_M)));
  const out: { p: V3; u: number; nOut: [number, number] }[] = [];
  for (let i = 0; i < cols; i++){ const u = (i + .5) / cols * n.w - n.w / 2;
    // o clădire mai joasă de un etaj are totuși un punct, la jumătatea înălțimii
    const ys: number[] = []; for (let k = 0; k < rows; k++){ const y = k * STOREY_M + 1.5; if (y > n.h - .3) break; ys.push(y); } if (!ys.length) ys.push(Math.min(1.5, n.h / 2));
    for (const y of ys){
      out.push({ p: [n.x + wid[0] * u - dep[0] * (n.d / 2 + .05), y, n.z + wid[1] * u - dep[1] * (n.d / 2 + .05)], u: r2(u + n.w / 2), nOut: [-dep[0], -dep[1]] }); } }
  return out;
}
/** Efectul casei noastre asupra ferestrelor vecinilor, în ziua normei (pas 5 minute). */
export function neighbourStudy(s: Snapshot, site: SiteInfo, toUtc: (d: string, m: number) => Date, date: string, normH: number, stepMin = 5): NeighbourSun[] {
  const list = site.neighbors ?? []; if (!list.length) return [];
  const dirs: V3[] = []; for (let m = stepMin / 2; m < 1440; m += stepMin){ const p = sunPosition(toUtc(date, m), site.lat, site.lon); if (p.elevationDeg > 0) dirs.push(sunVector(p.azimuthDeg, p.elevationDeg, site) as V3); }
  const house = houseMass(s), stepH = stepMin / 60;
  return list.map(n => {
    const base = { neighbourId: n.id, name: n.name, older: n.older, worstBefore: 0, worstAfter: 0, affected: 0, targets: [] as TargetSun[] };
    if (n.kind !== 'home') return { ...base, status: 'other' as const };
    if (n.calcan) return { ...base, status: 'calcan' as const };
    const others = list.filter(o => o.id !== n.id).map(o => obstacle(corners(o), 0, o.h));
    const targets = targetsOf(n).map(t => { let b = 0, a = 0;
      for (const d of dirs){ if (d[0] * t.nOut[0] + d[2] * t.nOut[1] <= 1e-4 || blocked(t.p, d, others)) continue; b++; if (!blocked(t.p, d, house)) a++; }
      return { u: t.u, y: t.p[1], before: r2(b * stepH), after: r2(a * stepH) }; });
    const worstBefore = Math.min(...targets.map(t => t.before)), worstAfter = Math.min(...targets.map(t => t.after));
    const affected = targets.filter(t => t.before + 1e-9 >= normH && t.after + 1e-9 < normH).length;
    return { ...base, targets, worstBefore, worstAfter, affected, status: affected ? 'affected' as const : worstBefore + 1e-9 < normH ? 'already' as const : 'ok' as const }; });
}

/** Validează lista de vecini (din interfață sau din API); aruncă un Error cu mesaj clar. */
export function checkNeighbours(x: unknown): Neighbour[] {
  if (!Array.isArray(x) || x.length > MAX_NEIGHBOURS) throw new Error(`Vecinii: cel mult ${MAX_NEIGHBOURS}.`);
  const num = (v: unknown, lo: number, hi: number, what: string) => { if (typeof v !== 'number' || !Number.isFinite(v) || v < lo || v > hi) throw new Error(`Vecin: ${what} e în afara intervalului.`); return r2(v); };
  const ids = new Set<string>();
  return x.map((o: any) => { if (!o || typeof o !== 'object') throw new Error('Vecin invalid.');
    if (typeof o.id !== 'string' || !/^[\w-]{1,40}$/.test(o.id) || ids.has(o.id)) throw new Error('Vecin: id invalid.'); ids.add(o.id);
    const name = typeof o.name === 'string' ? o.name.slice(0, 60) : '';
    return { id: o.id, name, x: num(o.x, -2000, 2000, 'poziția'), z: num(o.z, -2000, 2000, 'poziția'), w: num(o.w, 1, 300, 'lățimea'), d: num(o.d, 1, 300, 'adâncimea'),
      rot: ((num(o.rot, -720, 720, 'rotația') % 360) + 360) % 360, h: num(o.h, 1, 250, 'înălțimea'), kind: o.kind === 'other' ? 'other' : 'home', older: o.older !== false, calcan: o.calcan === true }; });
}
/** Pentru ce nivel se calculează: indicele nivelului cu podeaua `fl` în snapshot (0 dacă nu e găsit). */
export const levelIndexOf = (s: Snapshot, fl: Floor) => Math.max(0, floors(s).findIndex(f => f.id === fl.id));
