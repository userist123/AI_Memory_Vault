// Proiectare generativă cu lumina: pentru o cameră care nu primește destul soare la solstițiul de iarnă, generează
// variante de ferestre (lărgire, parapet coborât, fereastră nouă pe orice perete exterior al camerei), le simulează pe
// toate cu motorul de însorire și le păstrează pe cele mai eficiente. Pur și determinist; aplicarea e un Floor nou.
import type { Floor, Opening, Room, Wall } from './types';
import type { Obstacle } from './neighbours';
import { insolationStudy, windowsOf, NORM_HOURS, NORM_REFERENCE_DATE, WINDOW_SILL, WINDOW_HEIGHT } from './insolation';
import { pointInRoom, roomArea } from './room-geometry';
import { facing, type SiteInfo } from './site-location';

export type RemedyKind = 'widen' | 'lower_sill' | 'widen_lower' | 'new_window';
export interface Remedy { id: string; roomId: string; kind: RemedyKind; wallId: string; openingId: string; facing: string;
  /** lățimea și parapetul ferestrei după remediu (m) */ widthM: number; sillM: number;
  beforeH: number; afterH: number; /** suprafața de geam adăugată, m² */ glazingM2: number; meets: boolean; floor: Floor;
  /** peretele schimbat, gata de pus în planul curent (restul planului nu se copiază: nimic altceva nu se pierde) */ wall: Wall;
  /** golurile, nișele și obiectele peretelui la momentul calculului: dacă s-au schimbat între timp, propunerea e depășită */ baseOpenings: string }
/** Distanța minimă de la gol la capătul peretelui și între două goluri (m): loc pentru toc, colț și zidărie între goluri. */
export const END_CLEAR = .3, GAP = .3;
const NEW_W = 1.2, MAX_W = 3, NEW_WINDOW_PENALTY_M2 = 1.5, FRENCH_SILL = .4, MAX_OPENING_H = 3;
const r2 = (v: number) => Math.round(v * 100) / 100;
const len = (w: Wall) => Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]);
const top = (o: Opening, H: number) => Math.min(H - .05, (o.sill ?? WINDOW_SILL) + (o.height ?? WINDOW_HEIGHT));
const indoor = (r: Room) => !r.spaceKind || r.spaceKind === 'interior';

/** Intervalul [a, b] încape pe perete, departe de capete, de celelalte goluri (fără golul `skip`) și, când se dă planul, de
 *  nișele și obiectele de pe acel perete (calorifer, raft, oglindă: o fereastră nu trece prin ele)? */
export function fits(w: Wall, a: number, b: number, skip?: string, fl?: Pick<Floor, 'niches' | 'wallItems'>): boolean {
  if (a < END_CLEAR - 1e-9 || b > len(w) - END_CLEAR + 1e-9) return false;
  const busy = [...w.openings.filter(o => o.id !== skip), ...(fl?.niches ?? []).filter(n => n.wallId === w.id), ...(fl?.wallItems ?? []).filter(x => x.wallId === w.id)];
  return busy.every(o => b + GAP <= o.offset + 1e-9 || a >= o.offset + o.width + GAP - 1e-9);
}
/** Porțiunile unui perete pe care camera e de o parte și „afară” (nicio cameră închisă) de cealaltă, cu normala spre afară. */
export function exteriorSpans(fl: Floor, w: Wall, room: Room): { a: number; b: number; nOut: [number, number] }[] {
  if (w.status === 'demolish') return []; const L = len(w); if (L < 1e-6) return [];
  const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, nx = -uz, nz = ux, k = w.thickness / 2 + .15, out: { a: number; b: number; nOut: [number, number] }[] = [];
  for (const sgn of [1, -1] as const){ let start: number | null = null;
    for (let u = .05; u <= L + 1e-9; u += .1){ const x = w.a[0] + ux * u, z = w.a[1] + uz * u;
      const inside = pointInRoom(room, x + nx * k * sgn, z + nz * k * sgn), outside = !fl.rooms.some(r => indoor(r) && pointInRoom(r, x - nx * k * sgn, z - nz * k * sgn));
      if (inside && outside){ if (start == null) start = Math.max(0, u - .05); }
      else if (start != null){ out.push({ a: start, b: u - .05, nOut: [-nx * sgn, -nz * sgn] }); start = null; } }
    if (start != null) out.push({ a: start, b: L, nOut: [-nx * sgn, -nz * sgn] }); }
  return out;
}
/** Tot ce ocupă peretele (goluri, nișe, obiecte pe perete), ca text comparabil. */
export const wallState = (fl: Floor, wallId: string) => JSON.stringify([fl.walls.find(w => w.id === wallId)?.openings ?? [], (fl.niches ?? []).filter(n => n.wallId === wallId), (fl.wallItems ?? []).filter(x => x.wallId === wallId)]);
/** Pune peretele schimbat de un remediu în planul curent. null = peretele nu mai e cel pe care s-a calculat propunerea. */
export function applyRemedyWall(current: Floor, r: Pick<Remedy, 'wall' | 'baseOpenings'>): Floor | null {
  const old = current.walls.find(w => w.id === r.wall.id);
  if (!old || old.a[0] !== r.wall.a[0] || old.a[1] !== r.wall.a[1] || old.b[0] !== r.wall.b[0] || old.b[1] !== r.wall.b[1] || old.thickness !== r.wall.thickness || old.status !== r.wall.status
    || wallState(current, r.wall.id) !== r.baseOpenings) return null;
  return { ...current, walls: current.walls.map(w => w.id === r.wall.id ? r.wall : w) };
}
const withOpening = (fl: Floor, wallId: string, fn: (w: Wall) => Wall): Floor => ({ ...fl, walls: fl.walls.map(w => w.id === wallId ? fn(w) : w) });

/** Cele mai bune `max` remedii pentru camera dată, simulate pe ziua normei. `newId` dă id-ul ferestrei noi. */
export function sunRemedies(fl: Floor, site: SiteInfo, toUtc: (d: string, m: number) => Date, roomId: string, opts: { max?: number; date?: string; newId?: () => string; obs?: readonly Obstacle[] } = {}): Remedy[] {
  const room = fl.rooms.find(r => r.id === roomId); if (!room) return [];
  const date = opts.date ?? NORM_REFERENCE_DATE, H = fl.ceilingHeight, newId = opts.newId ?? (() => `op-sun-${Math.random().toString(36).slice(2, 8)}`);
  const hoursOf = (f: Floor) => insolationStudy(f, site, toUtc, { date, stepMin: 5, floorGrid: false, roomId, obs: opts.obs }).rooms[0]?.hours ?? 0;
  const before = hoursOf(fl), cands: Omit<Remedy, 'afterH' | 'meets' | 'id' | 'wall' | 'baseOpenings'>[] = [];
  // 1) ferestrele existente: mai late (centrate, cât permite peretele) sau cu parapet coborât („fereastră franceză”)
  for (const { wall, o, nOut } of windowsOf(fl).filter(x => x.room.id === roomId)){ const face = facing(nOut[0], nOut[1], site), sill = o.sill ?? WINDOW_SILL, t = top(o, H);
    const ls = r2(Math.max(FRENCH_SILL, t - MAX_OPENING_H)), low = sill > ls + .1;
    for (const extra of [.4, .8, 1.2, 1.6, 2.0, 2.4]){ const wN = Math.min(MAX_W, o.width + extra); if (wN <= o.width + 1e-9) continue; const a = o.offset - (wN - o.width) / 2;
      // centrată; dacă nu încape, lipită de capătul liber
      const tries = [a, o.offset, o.offset + o.width - wN].filter(x => fits(wall, x, x + wN, o.id, fl));
      if (!tries.length) continue; const off = tries[0]!;
      cands.push({ roomId, kind: 'widen', wallId: wall.id, openingId: o.id, facing: face, widthM: r2(wN), sillM: sill, beforeH: before, glazingM2: r2((wN - o.width) * (t - sill)),
        floor: withOpening(fl, wall.id, w => ({ ...w, openings: w.openings.map(q => q.id === o.id ? { ...q, offset: r2(off), width: r2(wN) } : q) })) });
      // lărgită și cu parapet coborât: geamul cel mai mare pe același gol
      if (low) cands.push({ roomId, kind: 'widen_lower', wallId: wall.id, openingId: o.id, facing: face, widthM: r2(wN), sillM: ls, beforeH: before, glazingM2: r2(wN * (t - ls) - o.width * (t - sill)),
        floor: withOpening(fl, wall.id, w => ({ ...w, openings: w.openings.map(q => q.id === o.id ? { ...q, offset: r2(off), width: r2(wN), sill: ls, height: r2(t - ls) } : q) })) }); }
    if (low) cands.push({ roomId, kind: 'lower_sill', wallId: wall.id, openingId: o.id, facing: face, widthM: o.width, sillM: ls, beforeH: before, glazingM2: r2(o.width * (sill - ls)),
      floor: withOpening(fl, wall.id, w => ({ ...w, openings: w.openings.map(q => q.id === o.id ? { ...q, sill: ls, height: r2(t - ls) } : q) })) }); }
  // 2) fereastră nouă de 1,2 m pe fiecare porțiune exterioară a camerei, la fiecare 30 cm
  for (const w of fl.walls) for (const sp of exteriorSpans(fl, w, room)){ const face = facing(sp.nOut[0], sp.nOut[1], site);
    for (const nw of [NEW_W, 2]) for (let a = sp.a + END_CLEAR; a + nw <= sp.b - .05 + 1e-9; a += .3){ if (!fits(w, a, a + nw, undefined, fl)) continue; const id = `cand-${w.id}-${nw}-${a.toFixed(2)}`;
      cands.push({ roomId, kind: 'new_window', wallId: w.id, openingId: id, facing: face, widthM: nw, sillM: WINDOW_SILL, beforeH: before, glazingM2: r2(nw * WINDOW_HEIGHT),
        floor: withOpening(fl, w.id, x => ({ ...x, openings: [...x.openings, { id, kind: 'window', offset: r2(a), width: nw }] })) }); } }
  const scored = cands.map(c => { const afterH = hoursOf(c.floor); return { ...c, afterH, meets: afterH + 1e-9 >= NORM_HOURS }; }).filter(c => c.afterH > c.beforeH + 1e-9);
  // cea mai ieftină soluție care respectă norma întâi (o fereastră nouă costă și buiandrug), apoi cel mai mare câștig
  const cost = (c: typeof scored[number]) => c.glazingM2 + (c.kind === 'new_window' ? NEW_WINDOW_PENALTY_M2 : 0);
  scored.sort((x, y) => Number(y.meets) - Number(x.meets) || (x.meets ? cost(x) - cost(y) : y.afterH - x.afterH) || y.afterH - x.afterH || cost(x) - cost(y));
  // frontul Pareto: o variantă rămâne doar dacă nicio alta nu dă cel puțin la fel de mult soare mai ieftin
  const dominates = (o: typeof scored[number], c: typeof scored[number]) => o.afterH >= c.afterH - 1e-9 && cost(o) <= cost(c) + 1e-9 && (o.afterH > c.afterH + 1e-9 || cost(o) < cost(c) - 1e-9);
  const front = scored.filter(c => !scored.some(o => o !== c && dominates(o, c)));
  // dacă măcar o variantă respectă norma, celelalte (care nu o respectă) nu mai sunt soluții
  const shown = front.some(c => c.meets) ? front.filter(c => c.meets) : front;
  // câte una pe perete și tip: variantele diferă cu adevărat
  const seen = new Set<string>(), out: Remedy[] = [];
  for (const c of shown){ const k = `${c.kind}:${c.wallId}:${c.kind === 'new_window' ? '' : c.openingId}`; if (seen.has(k)) continue; seen.add(k);
    let floor = c.floor, openingId = c.openingId;
    if (c.kind === 'new_window'){ const real = newId(); floor = withOpening(c.floor, c.wallId, w => ({ ...w, openings: w.openings.map(o => o.id === c.openingId ? { ...o, id: real } : o) })); openingId = real; }
    out.push({ ...c, floor, openingId, wall: floor.walls.find(w => w.id === c.wallId)!, baseOpenings: wallState(fl, c.wallId), id: `${c.kind}:${c.wallId}:${openingId}` }); if (out.length >= (opts.max ?? 3)) break; }
  return out;
}

/** Când ferestrele nu ajung: o cameră nelocuibilă (bucătărie, birou…) a nivelului care respectă norma și e cel puțin la fel de
 *  mare poate primi funcțiunea camerei umbrite. Doar o idee de reorganizare (mobilierul trebuie refăcut), fără aplicare automată. */
export interface UseSwap { roomId: string; withRoomId: string; withName: string; withType: string; hours: number; areaM2: number }
export const SWAP_FROM = new Set(['bucatarie', 'birou']);
export function swapIdeas(fl: Floor, site: SiteInfo, toUtc: (d: string, m: number) => Date, roomId: string, obs?: readonly Obstacle[]): UseSwap[] {
  const room = fl.rooms.find(r => r.id === roomId); if (!room) return [];
  const st = insolationStudy(fl, site, toUtc, { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false, obs }), area = (r: Room) => roomArea(r), need = area(room) * .9;
  return st.rooms.filter(r => r.roomId !== roomId && SWAP_FROM.has(r.type) && r.hours + 1e-9 >= NORM_HOURS)
    .map(r => { const x = fl.rooms.find(q => q.id === r.roomId)!; return { roomId, withRoomId: x.id, withName: x.name, withType: x.type, hours: r.hours, areaM2: r2(area(x)) }; })
    .filter(x => x.areaM2 >= need).sort((a, b) => b.hours - a.hours);
}
