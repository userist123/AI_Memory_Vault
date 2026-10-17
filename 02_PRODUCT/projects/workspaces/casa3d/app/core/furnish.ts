// Trecerea a doua a amenajării automate: repară ce motorul din prototip (core/layout.js, ținut neschimbat pentru paritate) nu știe.
// Rulează după motor, cameră cu cameră, și refurnizează doar camerele care încalcă o regulă:
//  - bucătăria primește frontul de bucătărie (variantele mai scurte, pe rând) și frigiderul;
//  - camera de zi a unei garsoniere (fără dormitor) primește patul;
//  - dormitorul primește dulapul (lanț de variante mai mici / cu uși glisante) și biroul doar de la ~11 m²;
//  - comoda TV și piesele înalte nu stau niciodată în fața unei ferestre (perechea canapea–TV se reorientează);
//  - patul nu intră în zona de deschidere a ușii;
//  - WC-ul mic (sub 1,6 m lățime sau sub 4 m²) primește doar vas WC și lavoar, fără cabină de duș;
//  - holul primește pantofar/consolă și oglindă, fără corp înalt în fața intrării.
// Funcții pure: aceleași reguli de circulație ca motorul și validatorul (core/rules.ts), fără nicio piesă inventată în afara catalogului.
import type { Catalog, Floor, FurniturePlacement, Room, RoomRect } from './types';
import { resolve, groupOf } from './catalog';
import { openingsOnSide, doorZones } from './validate';
import { rectHit, insideRect, r3 } from './geometry';
import { DINING_CLEARANCE } from './rules';

type Side = 'N' | 'S' | 'W' | 'E';
const SIDES: Side[] = ['N', 'E', 'S', 'W'];
const OPP: Record<Side, Side> = { N: 'S', S: 'N', W: 'E', E: 'W' };
const ROT: Record<Side, number> = { N: 0, S: Math.PI, W: Math.PI / 2, E: -Math.PI / 2 };
/** Distanța de la linia peretelui la spatele piesei (jumătate din peretele de 15 cm al motorului + 1 cm). */
const WALL_GAP = .085;
/** Peste această înălțime piesa nu stă în fața ferestrei (core/rules.ts TALL_ITEM_M); comoda TV nu stă niciodată (ecranul e deasupra ei). */
export const TALL_M = .95;
/** Pragurile regulilor de mai sus, în metri / m². */
export const FURNISH_RULES = { deskMinArea: 11, smallWcWidth: 1.6, smallWcArea: 4, studioMinArea: 12, hallMaxH: 1.3, sofaTvMin: 1.5, sofaTvMax: 4.6, entranceKeepOut: 1.0 };

const area = (r: RoomRect) => (r.x1 - r.x0) * (r.z1 - r.z0);
const horizontal = (s: Side) => s === 'N' || s === 'S';
const uid = () => (globalThis.crypto as Crypto).randomUUID();

interface Item { group: string; variantId: string; fp: RoomRect; front: RoomRect | null; x: number; z: number; rot: number; side?: Side; along?: number; ghost?: boolean; keep?: boolean; id?: string; source?: 'auto' | 'manual'; size?: FurniturePlacement['size'] }
/** `extra`: zone interzise amprentei și zonei din față; `extraFp`: zone interzise doar amprentei (ex. bătaia ușii pentru pat). */
interface FreeOpts { ignore?: Item[]; extra?: RoomRect[]; extraFp?: RoomRect[]; frontMayCrossDoor?: boolean; allowWith?: string[]; accept?: (fp: RoomRect, side: Side) => boolean }
interface Dims { w: number; d: number; h: number; name: string }

/** Amprenta unei piese lipite de perete: spatele pe peretele `side`, centrul la `along` pe lungimea peretelui. */
export function wallFootprint(r: RoomRect, side: Side, along: number, w: number, d: number): RoomRect {
  if (side === 'N') return { x0: along - w / 2, x1: along + w / 2, z0: r.z0 + WALL_GAP, z1: r.z0 + WALL_GAP + d };
  if (side === 'S') return { x0: along - w / 2, x1: along + w / 2, z0: r.z1 - WALL_GAP - d, z1: r.z1 - WALL_GAP };
  if (side === 'W') return { z0: along - w / 2, z1: along + w / 2, x0: r.x0 + WALL_GAP, x1: r.x0 + WALL_GAP + d };
  return { z0: along - w / 2, z1: along + w / 2, x0: r.x1 - WALL_GAP - d, x1: r.x1 - WALL_GAP };
}
/** Zona din fața unei piese lipite de perete, de adâncime `depth`. */
export function frontOf(fp: RoomRect, side: Side, depth: number): RoomRect | null {
  if (depth <= 0) return null;
  if (side === 'N') return { x0: fp.x0, x1: fp.x1, z0: fp.z1, z1: fp.z1 + depth };
  if (side === 'S') return { x0: fp.x0, x1: fp.x1, z0: fp.z0 - depth, z1: fp.z0 };
  if (side === 'W') return { z0: fp.z0, z1: fp.z1, x0: fp.x1, x1: fp.x1 + depth };
  return { z0: fp.z0, z1: fp.z1, x0: fp.x0 - depth, x1: fp.x0 };
}
const center = (r: RoomRect): [number, number] => [(r.x0 + r.x1) / 2, (r.z0 + r.z1) / 2];
/** Latura camerei lângă care stă o amprentă (cea mai apropiată). */
export function sideOfFootprint(r: RoomRect, fp: RoomRect): Side {
  const g: [Side, number][] = [['N', fp.z0 - r.z0], ['S', r.z1 - fp.z1], ['W', fp.x0 - r.x0], ['E', r.x1 - fp.x1]];
  return g.sort((a, b) => a[1] - b[1])[0]![0];
}
/** Golul de fereastră de pe latura `side` care se suprapune cu intervalul [a, b] (aceeași regulă ca WINDOW_BLOCKED din validator). */
export function windowOverlap(floor: Floor, room: Room, side: Side, a: number, b: number): boolean {
  return openingsOnSide(floor, room, side).some(o => o.kind === 'window' && b > o.a + .01 && a < o.b - .01);
}
/** Piesa acoperă o fereastră: e înaltă (sau e comoda TV) și stă lângă un perete cu fereastră în dreptul ei. */
export function blocksWindow(floor: Floor, room: Room, group: string, h: number, fp: RoomRect): boolean {
  if (!(h > TALL_M || group === 'comodaTv')) return false; const r = room.rect;
  const near: [Side, number][] = [['N', fp.z0 - r.z0], ['S', r.z1 - fp.z1], ['W', fp.x0 - r.x0], ['E', r.x1 - fp.x1]];
  return near.some(([s, gap]) => gap <= .35 && windowOverlap(floor, room, s, horizontal(s) ? fp.x0 : fp.z0, horizontal(s) ? fp.x1 : fp.z1));
}
/** Zona în care se deschide o ușă (lățimea golului ± 5 cm, adâncimea = lățimea foii + 5 cm, cel puțin cât zona motorului). */
export function doorSwingZones(floor: Floor, room: Room): RoomRect[] {
  const r = room.rect, out: RoomRect[] = [];
  for (const s of SIDES) for (const o of openingsOnSide(floor, room, s)){ if (o.kind !== 'door') continue; const depth = Math.max(.95, o.b - o.a + .05);
    if (s === 'N') out.push({ x0: o.a - .05, x1: o.b + .05, z0: r.z0, z1: r.z0 + depth }); else if (s === 'S') out.push({ x0: o.a - .05, x1: o.b + .05, z0: r.z1 - depth, z1: r.z1 });
    else if (s === 'W') out.push({ z0: o.a - .05, z1: o.b + .05, x0: r.x0, x1: r.x0 + depth }); else out.push({ z0: o.a - .05, z1: o.b + .05, x0: r.x1 - depth, x1: r.x1 }); }
  return out;
}
/** WC mic: o „baie” îngustă sau sub 4 m², dar numai când casa mai are o baie (unde rămâne dușul). */
export function isSmallWc(floor: Floor, room: Room): boolean {
  if (room.type !== 'baie') return false; const r = room.rect, small = Math.min(r.x1 - r.x0, r.z1 - r.z0) < FURNISH_RULES.smallWcWidth - 1e-6 || area(r) < FURNISH_RULES.smallWcArea - 1e-6;
  return small && floor.rooms.some(o => o.id !== room.id && o.type === 'baie' && !isSmallBath(o));
}
const isSmallBath = (o: Room) => Math.min(o.rect.x1 - o.rect.x0, o.rect.z1 - o.rect.z0) < FURNISH_RULES.smallWcWidth - 1e-6 || area(o.rect) < FURNISH_RULES.smallWcArea - 1e-6;
/** Camera de zi a unei garsoniere: nu există dormitor, există bucătărie, iar camera e destul de mare pentru zona de dormit. */
export function isStudioLiving(floor: Floor, room: Room): boolean {
  return room.type === 'living' && !floor.rooms.some(r => r.type === 'dormitor') && floor.rooms.some(r => r.type === 'bucatarie') && area(room.rect) >= FURNISH_RULES.studioMinArea;
}

class Ctx {
  items: Item[] = [];
  private dz: RoomRect[]; private ops: Record<Side, { kind: string; a: number; b: number }[]>;
  constructor(readonly floor: Floor, readonly room: Room, readonly dims: (id: string) => Dims | null) {
    this.dz = doorZones(floor, room); this.ops = { N: openingsOnSide(floor, room, 'N'), S: openingsOnSide(floor, room, 'S'), W: openingsOnSide(floor, room, 'W'), E: openingsOnSide(floor, room, 'E') };
  }
  get r(){ return this.room.rect; }
  doors(){ return this.dz; }
  openings(s: Side){ return this.ops[s]; }
  /** Ca blocksWindow, cu golurile camerei calculate o singură dată. */
  blocks(group: string, h: number, fp: RoomRect): boolean {
    if (!(h > TALL_M || group === 'comodaTv')) return false; const r = this.r;
    const near: [Side, number][] = [['N', fp.z0 - r.z0], ['S', r.z1 - fp.z1], ['W', fp.x0 - r.x0], ['E', r.x1 - fp.x1]];
    return near.some(([s, gap]) => { if (gap > .35) return false; const a = horizontal(s) ? fp.x0 : fp.z0, b = horizontal(s) ? fp.x1 : fp.z1;
      return this.ops[s].some(o => o.kind === 'window' && b > o.a + .01 && a < o.b - .01); });
  }
  /** Se poate pune amprenta `fp` cu zona din față `front`? Zonele din față se pot suprapune între ele, nu peste piese și nu peste uși. */
  free(fp: RoomRect, front: RoomRect | null, o: FreeOpts = {}): boolean {
    if (!insideRect(this.r, fp) || (front && !insideRect(this.r, front))) return false;
    const dz = this.doors(); if (dz.some(z => rectHit(z, fp)) || (front && !o.frontMayCrossDoor && dz.some(z => rectHit(z, front)))) return false;
    if ((o.extra || []).some(z => rectHit(z, fp) || (front && rectHit(z, front))) || (o.extraFp || []).some(z => rectHit(z, fp))) return false;
    const fpHit = (a: RoomRect, b: RoomRect) => a.x0 < b.x1 - 1e-4 && a.x1 > b.x0 + 1e-4 && a.z0 < b.z1 - 1e-4 && a.z1 > b.z0 + 1e-4;
    for (const it of this.items){ if (o.ignore?.includes(it) || o.allowWith?.includes(it.group)) continue;
      if (fpHit(it.fp, fp) || (it.front && rectHit(it.front, fp)) || (front && rectHit(it.fp, front))) return false; }
    return true;
  }
  /** Toate pozițiile lipite de perete pentru o variantă, cu pas de 5 cm; fără fereastră în spatele pieselor înalte și al comodei TV. */
  wallSpots(group: string, variantId: string, frontDepth: number, sides: Side[] = SIDES): { side: Side; along: number; fp: RoomRect; front: RoomRect | null; t: number }[] {
    const dm = this.dims(variantId); if (!dm || !dm.w) return []; const out: { side: Side; along: number; fp: RoomRect; front: RoomRect | null; t: number }[] = [];
    for (const s of sides){ const lo0 = horizontal(s) ? this.r.x0 : this.r.z0, hi0 = horizontal(s) ? this.r.x1 : this.r.z1, lo = lo0 + dm.w / 2 + .06, hi = hi0 - dm.w / 2 - .06; if (hi < lo - 1e-9) continue;
      const n = Math.max(0, Math.floor((hi - lo) / .05 + 1e-6));
      for (let k = 0; k <= n; k++){ const c = n ? lo + k * (hi - lo) / n : (lo + hi) / 2;
        const fp = wallFootprint(this.r, s, c, dm.w, dm.d);
        // piesa înaltă nu acoperă nicio fereastră: nici pe peretele din spate, nici pe cel de lângă care stă cu latura
        if (this.blocks(group, dm.h, fp)) continue; out.push({ side: s, along: c, fp, front: frontOf(fp, s, frontDepth), t: hi > lo ? (c - lo) / (hi - lo) : .5 }); } }
    return out;
  }
  add(group: string, variantId: string, fp: RoomRect, front: RoomRect | null, rot: number, side?: Side, along?: number): Item {
    const [x, z] = center(fp), it: Item = { group, variantId, fp, front, x, z, rot, ...(side ? { side } : {}), ...(along != null ? { along } : {}) }; this.items.push(it); return it;
  }
  ghost(fp: RoomRect){ const it: Item = { group: '-', variantId: '-', fp, front: null, x: 0, z: 0, rot: 0, ghost: true }; this.items.push(it); return it; }
  remove(it: Item){ this.items = this.items.filter(x => x !== it); }
  /** Prima poziție bună dintr-un lanț de variante, după scorul dat (mai mic = mai bun). */
  best(group: string, chain: string[], frontFor: (v: string) => number, score: (s: { side: Side; along: number; fp: RoomRect; t: number }, vi: number) => number, o: FreeOpts = {}, sides?: Side[]){
    let best: { v: string; side: Side; along: number; fp: RoomRect; front: RoomRect | null; sc: number } | null = null;
    chain.forEach((v, vi) => { for (const s of this.wallSpots(group, v, frontFor(v), sides)){ if (!this.free(s.fp, s.front, o) || (o.accept && !o.accept(s.fp, s.side))) continue; const sc = score(s, vi);
      if (!best || sc < best.sc - 1e-9) best = { v, side: s.side, along: s.along, fp: s.fp, front: s.front, sc }; } });
    return best as { v: string; side: Side; along: number; fp: RoomRect; front: RoomRect | null; sc: number } | null;
  }
  place(group: string, chain: string[], frontFor: (v: string) => number, score: (s: { side: Side; along: number; fp: RoomRect; t: number }, vi: number) => number, o: FreeOpts = {}, sides?: Side[]): Item | null {
    const b = this.best(group, chain, frontFor, score, o, sides); return b ? this.add(group, b.v, b.fp, b.front, ROT[b.side], b.side, b.along) : null;
  }
  /** O piesă liberă (masa cu scaune) în mijlocul camerei: amprenta plus spațiul pentru scaune trebuie să fie libere. */
  placeFree(group: string, chain: string[], pad: (v: string) => [number, number], score: (cx: number, cz: number, vi: number) => number): Item | null {
    let best: { v: string; fp: RoomRect; zone: RoomRect; sc: number; rot: number } | null = null;
    // drept sau rotit cu 90° (scaunele rămân pe laturile lungi)
    // variantele pe rând: prima care încape (cu cea mai bună poziție a ei)
    chain.forEach((v, vi) => { if (best) return; const dm = this.dims(v); if (!dm || !dm.w) return; for (const rot of [0, Math.PI / 2]){ const q = rot !== 0, w = q ? dm.d : dm.w, d = q ? dm.w : dm.d, [p0, p1] = pad(v), px = q ? p1 : p0, pz = q ? p0 : p1;
      for (let x = this.r.x0 + px + w / 2; x <= this.r.x1 - px - w / 2 + 1e-9; x += .1) for (let z = this.r.z0 + pz + d / 2; z <= this.r.z1 - pz - d / 2 + 1e-9; z += .1){
        const fp = { x0: x - w / 2, x1: x + w / 2, z0: z - d / 2, z1: z + d / 2 }, zone = { x0: fp.x0 - px, x1: fp.x1 + px, z0: fp.z0 - pz, z1: fp.z1 + pz };
        if (!this.free(zone, null)) continue; const sc = score(x, z, vi) + (q ? .3 : 0); if (!best || sc < best.sc - 1e-9) best = { v, fp, zone, sc, rot }; } } });
    if (!best) return null; const b = best as { v: string; fp: RoomRect; zone: RoomRect; sc: number; rot: number };
    return this.add(group, b.v, b.fp, b.zone, b.rot);
  }
}

/** Ușile și ferestrele de pe o latură, pentru scoruri. */
const winCount = (c: Ctx, s: Side) => c.openings(s).filter(o => o.kind === 'window').length;
const doorCount = (c: Ctx, s: Side) => c.openings(s).filter(o => o.kind === 'door').length;
const sideMid = (r: RoomRect, s: Side) => horizontal(s) ? (r.x0 + r.x1) / 2 : (r.z0 + r.z1) / 2;
const sideLen = (r: RoomRect, s: Side) => horizontal(s) ? r.x1 - r.x0 : r.z1 - r.z0;

export interface RefineInput { floor: Floor; placements: FurniturePlacement[]; selections: Record<string, string>; picked?: string[]; roomId?: string }
/** Ce s-a schimbat: camerele refăcute și piesele care tot nu încap. */
export interface RefineResult { placements: FurniturePlacement[]; rooms: string[]; notFit: { key: string; room: string }[] }

export function refineFurnishing(cat: Catalog, inp: RefineInput): RefineResult {
  const dimsCache = new Map<string, Dims | null>();
  const dims = (id: string): Dims | null => { if (!dimsCache.has(id)){ const rv = resolve(cat, id); dimsCache.set(id, rv && rv.w ? { w: rv.w, d: rv.d, h: rv.h, name: rv.product.name } : null); } return dimsCache.get(id)!; };
  const variantsOf = (g: string) => cat.variants.filter(v => groupOf(v.id) === g && v.dimensionsCm).sort((a, b) => a.legacyIndex - b.legacyIndex).map(v => v.id);
  const picked = new Set(inp.picked || []);
  /** Lanțul de variante: alegerea proiectului, apoi (dacă grupa nu e aleasă explicit) alternativele date, existente în catalog. */
  const chain = (g: string, alts: string[]) => { const sel = inp.selections[g], all = new Set(variantsOf(g)), out: string[] = [];
    if (sel && all.has(sel)) out.push(sel); if (!picked.has(g) || !out.length) for (const a of alts) if (all.has(a) && !out.includes(a)) out.push(a); return out; };
  /** Alternativele mai mici decât alegerea (după lățime, apoi adâncime), în ordine descrescătoare. */
  const smaller = (g: string) => { const sel = dims(inp.selections[g] ?? '') ; return variantsOf(g).filter(id => { const d = dims(id); return d && (!sel || d.w <= sel.w + 1e-9); })
    .sort((a, b) => dims(b)!.w - dims(a)!.w || dims(b)!.d - dims(a)!.d); };
  const sliding = (v: string) => /glisant/i.test(dims(v)?.name ?? '');
  const floor = inp.floor, out: FurniturePlacement[] = [], redone: string[] = [], notFit: { key: string; room: string }[] = [];
  let placements = inp.placements;
  const roomsToCheck = floor.rooms.filter(r => !inp.roomId || r.id === inp.roomId);
  // ordinea contează: bucătăria înaintea livingului (masa poate trece dintr-una în alta)
  const order = ['bucatarie', 'living', 'dormitor', 'baie', 'hol'], rank = (t: string) => order.includes(t) ? order.indexOf(t) : order.length;
  const sorted = [...roomsToCheck].sort((a, b) => rank(a.type) - rank(b.type));
  for (const room of sorted){
    const mine = placements.filter(p => p.roomId === room.id), auto = mine.filter(p => p.source === 'auto');
    const ctx = new Ctx(floor, room, dims);
    const toItem = (p: FurniturePlacement): Item => { const d = dims(p.variantId), w = p.size ? p.size.w / 100 : d?.w ?? 0, dd = p.size ? p.size.d / 100 : d?.d ?? 0;
      const q = Math.round(p.rotation / (Math.PI / 2)) % 2 !== 0, hw = (q ? dd : w) / 2, hd = (q ? w : dd) / 2, fp = { x0: p.x - hw, x1: p.x + hw, z0: p.z - hd, z1: p.z + hd };
      return { group: p.group, variantId: p.variantId, fp, front: null, x: p.x, z: p.z, rot: p.rotation, keep: true, id: p.id, source: p.source, ...(p.size ? { size: p.size } : {}) }; };
    const has = (g: string) => mine.some(p => p.group === g);
    const hOf = (p: FurniturePlacement) => p.size ? p.size.h / 100 : dims(p.variantId)?.h ?? 0;
    const blocking = mine.some(p => { const it = toItem(p); return blocksWindow(floor, room, p.group, hOf(p), it.fp); });
    let need = false;
    const roomArea = area(room.rect);
    const isTargetRoom = inp.roomId === room.id;
    if (isTargetRoom) need = true;
    else if (room.type === 'bucatarie') need = !has('bucatarie') || !has('frigider') || blocking || (roomArea >= 10.5 && !has('comoda'));
    // livingul se reface și când casa a rămas fără masă (motorul nu a găsit loc nici în bucătărie, nici aici) sau e spațios și sub-mobilat
    else if (room.type === 'living') need = blocking || (has('canapea') && !has('comodaTv')) || (isStudioLiving(floor, room) && !has('pat'))
      || (!isStudioLiving(floor, room) && !placements.some(p => p.group === 'masa'))
      || (!isStudioLiving(floor, room) && roomArea >= 22.5 && (!has('fotoliu') || (roomArea >= 24.0 && !has('comoda'))));
    else if (room.type === 'dormitor'){ const swing = doorSwingZones(floor, room), bed = mine.find(p => p.group === 'pat');
      need = blocking || !bed || !has('dulap') || swing.some(z => rectHit(z, toItem(bed!).fp)) || (has('birou') && roomArea < FURNISH_RULES.deskMinArea)
        || (roomArea >= 18.2 && !has('comoda')) || (roomArea >= 21.0 && !has('fotoliu')); }
    else if (room.type === 'baie') need = !has('wc') || !has('lavoar') || (isSmallWc(floor, room) ? has('dus') || has('cada') : !has('dus') && !has('cada'))
      || (!isSmallWc(floor, room) && roomArea >= 6.8 && (!has('caloriferBaie') || (roomArea >= 7.8 && !has('coloanaBaie'))));
    else if (room.type === 'hol') need = blocking || mine.some(p => hOf(p) > FURNISH_RULES.hallMaxH + 1e-9) || !has('oglinda') || !has('pantofar')
      || (roomArea >= 8.0 && !has('comoda')) || (roomArea >= 9.5 && !has('fotoliu'));
    if (!need) continue;
    // piesele puse de mână rămân ca obstacole; cele automate se refac
    for (const p of mine.filter(p => p.source !== 'auto')) ctx.items.push(toItem(p));
    const hadDining = auto.some(p => p.group === 'masa');
    const r = room.rect;
    const ok = (() => {
      if (room.type === 'bucatarie') return furnishKitchen(ctx, chain('bucatarie', smaller('bucatarie')), chain('frigider', smaller('frigider')), hadDining || area(r) >= 9.5 ? distinct(chain('masa', diningAlts())) : null, notFit);
      if (room.type === 'living') return furnishLiving(ctx);
      if (room.type === 'dormitor') return furnishBedroom(ctx, chain('pat', ['pat-5', 'pat-8']), chain('noptiera', []), chain('dulap', ['dulap-9', 'dulap-2', 'dulap-3']), chain('birou', ['birou-2', 'birou-3']), chain('scaunBirou', []), sliding);
      if (room.type === 'baie') return furnishBath(ctx);
      if (room.type === 'hol') return furnishHall(ctx);
      return false;
    })();
    if (!ok) continue;
    const fresh = ctx.items.filter(i => !i.ghost && !i.keep).map<FurniturePlacement>(i => ({ id: uid(), roomId: room.id, group: i.group, variantId: i.variantId, x: r3(i.x), z: r3(i.z), rotation: i.rot, source: 'auto' }));
    // nu se pierde nicio grupă pe care motorul o pusese, în afară de cele scoase intenționat de reguli
    // (dușul din WC-ul mic, biroul din dormitorul mic); altfel rămâne amenajarea motorului
    const dropOk = new Set([...(isSmallWc(floor, room) ? ['dus', 'cada'] : []), ...(room.type === 'dormitor' && area(room.rect) < FURNISH_RULES.deskMinArea ? ['birou', 'scaunBirou'] : []), 'fotoliu', 'comoda', 'biblioteca', 'caloriferBaie', 'coloanaBaie', 'bideu']);
    if (auto.some(p => !dropOk.has(p.group) && !fresh.some(f => f.group === p.group))) continue;
    redone.push(room.id);
    placements = [...placements.filter(p => p.roomId !== room.id || p.source !== 'auto'), ...fresh];
  }
  out.push(...placements);
  return { placements: out, rooms: redone, notFit };

  function diningAlts(){ return variantsOf('masa').sort((a, b) => dims(b)!.w * dims(b)!.d - dims(a)!.w * dims(a)!.d); }
  /** Un singur reprezentant pentru variantele cu aceleași dimensiuni (și același număr de scaune). */
  function distinct(ids: string[]){ const seen = new Set<string>(); return ids.filter(id => { const d = dims(id), k = d ? `${d.w}|${d.d}|${d.h}|${cat.variants.find(v => v.id === id)?.chairs ?? ''}` : id; if (seen.has(k)) return false; seen.add(k); return true; }); }
  function diningPad(v: string): [number, number] { const c = cat.variants.find(x => x.id === v)?.chairs ?? 4; return [c === 2 ? DINING_CLEARANCE.chairs2 : DINING_CLEARANCE.chairs4, DINING_CLEARANCE.ends]; }

  function furnishKitchen(ctx: Ctx, runs: string[], fridges: string[], dining: string[] | null, nf: { key: string; room: string }[]): boolean {
    const r = ctx.r, base = ctx.items.slice(); let best: { items: Item[]; sc: number } | null = null;
    // întâi cu culoarul de lucru complet (1,07 m) și fără uși în el, apoi cu 0,9 m
    for (const relaxed of [false, true]){
      runs.forEach((v, vi) => { for (const s of ctx.wallSpots('bucatarie', v, relaxed ? .9 : 1.07)){
        ctx.items = base.slice(); if (!ctx.free(s.fp, s.front, { frontMayCrossDoor: relaxed })) continue;
        ctx.add('bucatarie', v, s.fp, s.front, ROT[s.side], s.side, s.along); const [rx, rz] = center(s.fp);
        const fr = ctx.place('frigider', fridges, () => relaxed ? .75 : .9, (f, fi) => { const [fx, fz] = center(f.fp); return Math.hypot(fx - rx, fz - rz) * .5 + fi * .3 + (f.side === s.side ? 0 : .4); }, { frontMayCrossDoor: relaxed });
        const sc = vi * 2 + Math.min(s.t, 1 - s.t) * 2 + (fr ? 0 : 6) + (relaxed ? 3 : 0) + winCount(ctx, s.side) * .5;
        if (!best || sc < best.sc - 1e-9) best = { items: ctx.items.slice(), sc }; } });
      if (best) break;
    }
    if (!best) { ctx.items = base; return false; }
    ctx.items = (best as { items: Item[] }).items;
    if (dining){ const [cx, cz] = center(r); const t = ctx.placeFree('masa', dining, diningPad, (x, z, vi) => Math.hypot(x - cx, z - cz) + vi * .2); if (!t && dining.length && area(r) >= 9.5) nf.push({ key: 'masa', room: ctx.room.name }); }
    if (area(r) >= 10.5) {
      ctx.place('comoda', chain('comoda', smaller('comoda')), () => .6, (s, vi) => vi * .6 + Math.min(s.t, 1 - s.t) * 2);
    }
    return true;
  }

  type Pair = { sofa: [string, Side, number, RoomRect]; tv: [string, Side, number, RoomRect] | null; table: [string, RoomRect, number] | null; zone: RoomRect; sc: number };
  /** Perechea canapea–TV pe același ax, ordonată după scor (cea mai bună întâi); doar câte o variantă pe fiecare poziție a canapelei. */
  function sofaTvOptions(ctx: Ctx, farFrom: [number, number] | null): Pair[] {
    const r = ctx.r, sofas = distinct(chain('canapea', smaller('canapea'))).slice(0, 3), tvs = distinct(chain('comodaTv', smaller('comodaTv'))).slice(0, 3), tables = distinct(chain('masuta', smaller('masuta'))).slice(0, 4);
    const opts: Pair[] = [], tvSpots = new Map<string, { along: number; fp: RoomRect }[]>();
    // pozițiile libere ale comodei TV nu depind de canapea: se calculează o dată pe variantă și perete
    const tvFree = (tv: string, side: Side) => { const k = tv + side; if (!tvSpots.has(k)) tvSpots.set(k, ctx.wallSpots('comodaTv', tv, 0, [side]).filter(t => ctx.free(t.fp, null)).map(t => ({ along: t.along, fp: t.fp }))); return tvSpots.get(k)!; };
    sofas.forEach((sv, si) => { const sd = dims(sv)!; for (const s of ctx.wallSpots('canapea', sv, 0)){
      if (!ctx.free(s.fp, null)) continue; const tvSide = OPP[s.side];
      // distanța liberă până la peretele opus
      const depth = horizontal(s.side) ? r.z1 - r.z0 : r.x1 - r.x0, gap = depth - 2 * WALL_GAP - sd.d;
      for (const tv of tvs){ const td = dims(tv)!; const dist = gap - td.d; if (dist < FURNISH_RULES.sofaTvMin || dist > FURNISH_RULES.sofaTvMax) continue;
        // comoda TV pe peretele opus, cât mai în dreptul canapelei, niciodată în fața ferestrei
        let tvBest: { along: number; fp: RoomRect } | null = null;
        for (const t of tvFree(tv, tvSide)){ const off = Math.abs(t.along - s.along); if (off <= .6 && (!tvBest || off < Math.abs(tvBest.along - s.along))) tvBest = t; }
        if (!tvBest) continue;
        // zona dintre canapea și TV rămâne liberă de piese (măsuța stă în ea); o ușă care se deschide în ea e acceptabilă
        const zone = frontOf(s.fp, s.side, dist)!; if (ctx.items.some(i => !i.ghost && rectHit(i.fp, zone))) continue;
        let table: [string, RoomRect, number] | null = null;
        for (const mv of tables){ const md = dims(mv)!; if (.45 + md.d + .45 > dist + 1e-9) continue; const horiz = horizontal(s.side), [cx, cz] = center(s.fp), mt = frontOf(s.fp, s.side, .45)!;
          const fp = horiz ? { x0: cx - md.w / 2, x1: cx + md.w / 2, z0: s.side === 'N' ? mt.z1 : mt.z0 - md.d, z1: s.side === 'N' ? mt.z1 + md.d : mt.z0 }
            : { z0: cz - md.w / 2, z1: cz + md.w / 2, x0: s.side === 'W' ? mt.x1 : mt.x0 - md.d, x1: s.side === 'W' ? mt.x1 + md.d : mt.x0 };
          if (ctx.free(fp, null)) { table = [mv, fp, horiz ? 0 : Math.PI / 2]; break; } }
        const [sx, sz] = center(s.fp), far = farFrom ? -Math.hypot(sx - farFrom[0], sz - farFrom[1]) * .4 : 0;
        const sc = si * 1.5 + tvs.indexOf(tv) * .5 + Math.abs(tvBest.along - s.along) * 2 + Math.abs(s.along - sideMid(r, s.side)) * .4 + winCount(ctx, s.side) * 1.2 + doorCount(ctx, s.side) * .5
          + Math.abs(dist - 2.6) * .4 + (table ? 0 : 3) + far;
        opts.push({ sofa: [sv, s.side, s.along, s.fp], tv: [tv, tvSide, tvBest.along, tvBest.fp], table, zone, sc }); } } });
    return opts.sort((a, b) => a.sc - b.sc);
  }
  function applyPair(ctx: Ctx, b: Pair){
    ctx.add('canapea', b.sofa[0], b.sofa[3], frontOf(b.sofa[3], b.sofa[1], .45), ROT[b.sofa[1]], b.sofa[1], b.sofa[2]);
    if (b.tv) ctx.add('comodaTv', b.tv[0], b.tv[3], null, ROT[b.tv[1]], b.tv[1], b.tv[2]);
    if (b.table) ctx.add('masuta', b.table[0], b.table[1], null, b.table[2]);
    // zona de conversație rămâne liberă pentru celelalte piese
    ctx.ghost(b.zone);
  }
  /** Prima pereche canapea–TV după care încape și restul camerei (masa); altfel cea mai bună pereche. */
  function sofaTv(ctx: Ctx, farFrom: [number, number] | null, rest: () => boolean, probe: (ctx: Ctx) => boolean = () => true): boolean {
    const opts = sofaTvOptions(ctx, farFrom); if (!opts.length) return false; const base = ctx.items.slice();
    // opțiuni diferite între ele (perete, variantă, poziție la 30 cm), verificate întâi cu cea mai mică masă
    const seen = new Set<string>(), diverse = opts.filter(o => { const k = `${o.sofa[0]}|${o.sofa[1]}|${Math.round(o.sofa[2] / .3)}`; if (seen.has(k)) return false; seen.add(k); return true; });
    for (const o of diverse.slice(0, 30)){ ctx.items = base.slice(); applyPair(ctx, o); if (probe(ctx) && rest()) return true; }
    ctx.items = base.slice(); applyPair(ctx, opts[0]!); if (!rest()) notFit.push({ key: 'masa', room: ctx.room.name }); return true;
  }

  function furnishLiving(ctx: Ctx): boolean {
    const r = ctx.r, kitchen = floor.rooms.find(x => x.type === 'bucatarie'), kc: [number, number] | null = kitchen ? center(kitchen.rect) : null;
    const diningElsewhere = placements.some(p => p.group === 'masa' && p.roomId !== ctx.room.id && floor.rooms.find(x => x.id === p.roomId)?.type === 'bucatarie');
    if (isStudioLiving(floor, ctx.room)){
      // garsonieră: întâi zona de dormit (pat de 140 cm, departe de uși), apoi perechea canapea–TV în ce rămâne
      const beds = chain('pat', ['pat-5', 'pat-8']).sort((a, b) => dims(a)!.w - dims(b)!.w), swing = doorSwingZones(floor, ctx.room);
      const cands: { v: string; s: ReturnType<Ctx['wallSpots']>[number]; sc: number }[] = [];
      // la picioarele patului (60 cm) poate intra colțul zonei unei uși, dar patul însuși niciodată
      for (const relaxed of [false, true]) beds.forEach((v, vi) => { for (const s of ctx.wallSpots('pat', v, .6)){ if (!ctx.free(s.fp, s.front, { extraFp: swing, frontMayCrossDoor: relaxed })) continue;
        if (relaxed && ctx.free(s.fp, s.front, { extraFp: swing })) continue;
        const [bx, bz] = center(s.fp), doorDist = Math.min(...swing.map(z => Math.hypot(bx - center(z)[0], bz - center(z)[1])), 9);
        cands.push({ v, s, sc: vi * .5 - doorDist * .6 + Math.min(s.t, 1 - s.t) * 1.5 + (relaxed ? 2 : 0) }); } });
      cands.sort((a, b) => a.sc - b.sc);
      for (const c of diverseBeds(cands).slice(0, 40)){
        const snapshot = ctx.items.slice(); const bed = ctx.add('pat', c.v, c.s.fp, c.s.front, ROT[c.s.side], c.s.side, c.s.along);
        nightstands(ctx, bed, 1);
        // fâșiile de lângă pat rămân libere
        for (const z of bedPads(bed)) ctx.ghost(z);
        if (sofaTv(ctx, null, () => finishLivingRest(ctx, kc, diningElsewhere, true))) return true;
        ctx.items = snapshot;
      }
      return false;
    }
    const farEnd: [number, number] | null = diningElsewhere || !kc ? null : kc;
    return sofaTv(ctx, farEnd, () => finishLivingRest(ctx, kc, diningElsewhere, false), c => diningElsewhere || diningFits(c));
  }
  /** Încape măcar cea mai mică masă din catalog? (verificare rapidă, nu lasă nimic în cameră) */
  function diningFits(ctx: Ctx): boolean {
    const small = distinct(variantsOf('masa')).sort((a, b) => { const A = dims(a)!, B = dims(b)!, pa = diningPad(a), pb = diningPad(b); return (A.w + 2 * pa[0]) * (A.d + 2 * pa[1]) - (B.w + 2 * pb[0]) * (B.d + 2 * pb[1]); })[0];
    if (!small) return false; const it = ctx.placeFree('masa', [small], diningPad, () => 0); if (it) ctx.remove(it); return !!it;
  }
  function finishLivingRest(ctx: Ctx, kc: [number, number] | null, diningElsewhere: boolean, studio: boolean){
    const r = ctx.r, [cx, cz] = center(r), roomArea = area(r), dining = distinct(chain('masa', diningAlts()));
    let dined = true;
    if (!diningElsewhere){ dined = !!ctx.placeFree('masa', studio ? [...dining].sort((a, b) => dims(a)!.w * dims(a)!.d - dims(b)!.w * dims(b)!.d) : dining, diningPad,
      (x, z, vi) => (kc ? Math.hypot(x - kc[0], z - kc[1]) * .5 : Math.hypot(x - cx, z - cz)) + vi * .3); }
    else if (roomArea >= 26) {
      ctx.placeFree('masa', dining, diningPad, (x, z, vi) => (kc ? Math.hypot(x - kc[0], z - kc[1]) * .5 : Math.hypot(x - cx, z - cz)) + vi * .3);
    }

    const maxBiblio = studio ? 1 : roomArea >= 28 ? 4 : roomArea >= 25 ? 3 : 2;
    for (let k = 0; k < maxBiblio; k++) ctx.place('biblioteca', chain('biblioteca', []), () => .6, s => Math.min(s.t, 1 - s.t) * 4);

    if (roomArea >= (studio ? 26 : 22.5)) {
      const armchairs = chain('fotoliu', smaller('fotoliu'));
      const maxFotolii = roomArea >= (studio ? 27 : 26.0) ? 2 : 1;
      for (let k = 0; k < maxFotolii; k++) {
        ctx.place('fotoliu', armchairs, () => .45, (s, vi) => vi * .5 + Math.min(s.t, 1 - s.t) * 2);
      }
    }
    if (roomArea >= (studio ? 26 : 24.0)) {
      const sideboards = chain('comoda', smaller('comoda'));
      ctx.place('comoda', sideboards, () => .6, (s, vi) => vi * .6 + Math.min(s.t, 1 - s.t) * 2);
    }

    // în garsonieră masa e opțională (bucătăria e mică, zona de dormit are prioritate)
    return dined || studio;
  }
  /** Pozițiile de pat diferite între ele (variantă, perete, poziție la 20 cm), în ordinea scorului. */
  function diverseBeds<T extends { v: string; s: { side: Side; along: number } }>(cands: T[]): T[] {
    const seen = new Set<string>(); return cands.filter(c => { const k = `${c.v}|${c.s.side}|${Math.round(c.s.along / .2)}`; if (seen.has(k)) return false; seen.add(k); return true; });
  }
  function bedPads(bed: Item): RoomRect[] {
    const f = bed.fp; return horizontal(bed.side!) ? [{ x0: f.x0 - .6, x1: f.x0, z0: f.z0, z1: f.z1 }, { x0: f.x1, x1: f.x1 + .6, z0: f.z0, z1: f.z1 }] : [{ z0: f.z0 - .6, z1: f.z0, x0: f.x0, x1: f.x1 }, { z0: f.z1, z1: f.z1 + .6, x0: f.x0, x1: f.x1 }];
  }
  function nightstands(ctx: Ctx, bed: Item, max = 2): number {
    const ns = chain('noptiera', [])[0]; if (!ns) return 0; const d = dims(ns)!, bw = horizontal(bed.side!) ? bed.fp.x1 - bed.fp.x0 : bed.fp.z1 - bed.fp.z0; let n = 0;
    for (const k of [-1, 1]){ if (n >= max) break; const c = bed.along! + k * (bw / 2 + d.w / 2 + .015), fp = wallFootprint(ctx.r, bed.side!, c, d.w, d.d);
      if (ctx.free(fp, null, { ignore: [bed] }) && !rectHit(fp, bed.fp)) { ctx.add('noptiera', ns, fp, null, ROT[bed.side!], bed.side, c); n++; } }
    return n;
  }

  function furnishBedroom(ctx: Ctx, beds: string[], _ns: string[], wardrobes: string[], desks: string[], chairs: string[], isSliding: (v: string) => boolean): boolean {
    const r = ctx.r, swing = doorSwingZones(floor, ctx.room), wantDesk = area(r) >= FURNISH_RULES.deskMinArea - 1e-9;
    const cands: { v: string; vi: number; s: ReturnType<Ctx['wallSpots']>[number]; sc: number }[] = [];
    for (const relaxed of [false, true]) beds.forEach((v, vi) => { for (const s of ctx.wallSpots('pat', v, .6)){ if (!ctx.free(s.fp, s.front, { extraFp: swing, frontMayCrossDoor: relaxed })) continue;
      if (relaxed && ctx.free(s.fp, s.front, { extraFp: swing })) continue;
      // patul cât mai pe axa camerei (covorul se desenează centrat în cameră)
      cands.push({ v, vi, s, sc: vi * 3 + Math.abs(s.along - sideMid(r, s.side)) * 1.6 + doorCount(ctx, s.side) * .6 + (relaxed ? 2 : 0) }); } });
    cands.sort((a, b) => a.sc - b.sc);
    let best: { items: Item[]; sc: number } | null = null; const base = ctx.items.slice();
    for (const c of diverseBeds(cands).slice(0, 24)){
      ctx.items = base.slice(); const bed = ctx.add('pat', c.v, c.s.fp, c.s.front, ROT[c.s.side], c.s.side, c.s.along);
      const pads = bedPads(bed), padsFree = pads.filter(p => insideRect(r, p) && !ctx.items.some(i => i !== bed && rectHit(i.fp, p))).length;
      const n = nightstands(ctx, bed);
      const wd = ctx.place('dulap', wardrobes, v => isSliding(v) ? .6 : .9, (s, wi) => wi * .8 + Math.min(s.t, 1 - s.t) * 2, { extra: pads });
      let desk: Item | null = null;
      // scaunul (spătar înalt) stă în fața biroului; nici el nu are voie să acopere o fereastră
      const chairAt = (dfp: RoomRect, side: Side) => { const [x, z] = center(frontOf(dfp, side, .45)!); return { x0: x - .3, x1: x + .3, z0: z - .3, z1: z + .3 }; };
      const chairOk = (dfp: RoomRect, side: Side) => { if (!chairs[0]) return true; const c = chairAt(dfp, side);
        return !blocksWindow(floor, ctx.room, 'scaunBirou', dims(chairs[0])?.h ?? 0, c) && !ctx.items.some(i => !i.ghost && (rectHit(i.fp, c) || (i.front && rectHit(i.front, c)))); };
      if (wantDesk){ desk = ctx.place('birou', desks, () => .8, (s, di) => di * .5 - winCount(ctx, s.side) * 1.5 + Math.min(s.t, 1 - s.t), { extra: pads, accept: chairOk });
        if (desk && chairs[0]) ctx.add('scaunBirou', chairs[0], chairAt(desk.fp, desk.side!), null, desk.rot + Math.PI); }
      const sc = c.sc + (wd ? wardrobes.indexOf(wd.variantId) * .8 : 25) + (2 - n) * 1.2 + (wantDesk && !desk ? 4 : 0) + (2 - padsFree) * 1.5;
      if (!best || sc < best.sc - 1e-9) best = { items: ctx.items.slice(), sc };
    }
    if (!best) { ctx.items = base; return false; }
    ctx.items = (best as { items: Item[] }).items;

    const chosenBed = ctx.items.find(i => i.group === 'pat');
    const pads = chosenBed ? bedPads(chosenBed) : [];
    const roomArea = area(r);
    if (roomArea >= 18.2) {
      ctx.place('comoda', chain('comoda', smaller('comoda')), () => .6, (s, vi) => vi * .6 + Math.min(s.t, 1 - s.t) * 2, { extra: pads });
    }
    if (roomArea >= 21.0) {
      ctx.place('dulap', wardrobes, v => isSliding(v) ? .6 : .9, (s, wi) => wi * .8 + Math.min(s.t, 1 - s.t) * 2, { extra: pads });
      ctx.place('fotoliu', chain('fotoliu', smaller('fotoliu')), () => .45, (s, vi) => vi * .6 + Math.min(s.t, 1 - s.t) * 2, { extra: pads });
      ctx.place('biblioteca', chain('biblioteca', []), () => .6, s => Math.min(s.t, 1 - s.t) * 4, { extra: pads });
    }

    return true;
  }

  function furnishBath(ctx: Ctx): boolean {
    const r = ctx.r, small = isSmallWc(floor, ctx.room), door = SIDES.flatMap(s => openingsOnSide(floor, ctx.room, s).filter(o => o.kind === 'door').map(o => ({ s, o })))[0];
    const dc: [number, number] = door ? (horizontal(door.s) ? [(door.o.a + door.o.b) / 2, door.s === 'N' ? r.z0 : r.z1] : [door.s === 'W' ? r.x0 : r.x1, (door.o.a + door.o.b) / 2]) : center(r);
    const groups = small ? ['wc', 'lavoar'] : ['dus', 'wc', 'lavoar'];
    const chains: Record<string, string[]> = { dus: chain('dus', smaller('dus')), wc: chain('wc', smaller('wc')), lavoar: chain('lavoar', smaller('lavoar')) };
    const perms = (xs: string[]): string[][] => xs.length <= 1 ? [xs] : xs.flatMap((x, i) => perms([...xs.slice(0, i), ...xs.slice(i + 1)]).map(p => [x, ...p]));
    const base = ctx.items.slice(); let best: { items: Item[]; n: number; sc: number } | null = null;
    for (const order of perms(groups)){
      ctx.items = base.slice(); let n = 0, sc = 0;
      for (const g of order){ const it = ctx.place(g, chains[g]!, () => g === 'dus' ? 0 : .6, (s, vi) => { const [x, z] = center(s.fp), dd = Math.hypot(x - dc[0], z - dc[1]);
        return vi * .5 + (g === 'dus' ? -dd + Math.min(s.t, 1 - s.t) * 2 : g === 'wc' ? -dd * .3 : dd * .2); });
        if (it) { n++; sc += chains[g]!.indexOf(it.variantId); } }
      if (!best || n > best.n || (n === best.n && sc < best.sc)) best = { items: ctx.items.slice(), n, sc };
      if (best.n === groups.length) break;
    }
    ctx.items = best!.items;

    if (!small && area(r) >= 6.8) {
      ctx.place('caloriferBaie', chain('caloriferBaie', []), () => 0, (s, vi) => vi * .5 + Math.min(s.t, 1 - s.t));
      ctx.place('coloanaBaie', chain('coloanaBaie', []), () => .5, (s, vi) => vi * .5 + Math.min(s.t, 1 - s.t));
      if (area(r) >= 7.8) {
        ctx.place('bideu', chain('bideu', []), () => .6, (s, vi) => vi * .5 + Math.min(s.t, 1 - s.t));
      }
    }

    return best!.n > 0;
  }

  function furnishHall(ctx: Ctx): boolean {
    const r = ctx.r, low = (v: string) => (dims(v)?.h ?? 9) <= FURNISH_RULES.hallMaxH + 1e-9;
    // în fața ușii de intrare rămâne un spațiu liber mai adânc decât zona ușii
    const entrance: RoomRect[] = []; for (const w of floor.walls) for (const o of w.openings){ if (!o.entrance) continue;
      for (const s of SIDES){ const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, mx = w.a[0] + ux * (o.offset + o.width / 2), mz = w.a[1] + uz * (o.offset + o.width / 2);
        const onSide = s === 'N' ? Math.abs(mz - r.z0) < 1e-6 : s === 'S' ? Math.abs(mz - r.z1) < 1e-6 : s === 'W' ? Math.abs(mx - r.x0) < 1e-6 : Math.abs(mx - r.x1) < 1e-6;
        if (!onSide || mx < r.x0 - 1e-6 || mx > r.x1 + 1e-6 || mz < r.z0 - 1e-6 || mz > r.z1 + 1e-6) continue; const k = FURNISH_RULES.entranceKeepOut, h = o.width / 2 + .05;
        entrance.push(s === 'N' ? { x0: mx - h, x1: mx + h, z0: r.z0, z1: r.z0 + k } : s === 'S' ? { x0: mx - h, x1: mx + h, z0: r.z1 - k, z1: r.z1 } : s === 'W' ? { z0: mz - h, z1: mz + h, x0: r.x0, x1: r.x0 + k } : { z0: mz - h, z1: mz + h, x0: r.x1 - k, x1: r.x1 }); } }
    const ent = entrance[0] ? center(entrance[0]) : center(r);
    const consoles = chain('pantofar', ['pantofar-4', 'pantofar-0', 'pantofar-8', 'pantofar-2']).filter(low);
    const mirrors = chain('oglinda', ['oglinda-1', 'oglinda-2', 'oglinda-5']).filter(low);
    const con = ctx.place('pantofar', consoles, () => .5, (s, vi) => { const [x, z] = center(s.fp); return vi * .6 + Math.hypot(x - ent[0], z - ent[1]) * .5; }, { extraFp: entrance, frontMayCrossDoor: true });
    const near = con ? center(con.fp) : ent;
    ctx.place('oglinda', mirrors, () => .6, (s, vi) => { const [x, z] = center(s.fp); return vi * .6 + Math.hypot(x - near[0], z - near[1]) * .5; }, { extraFp: entrance, frontMayCrossDoor: true });

    if (area(r) >= 8.0) {
      const lowComode = chain('comoda', smaller('comoda')).filter(low);
      ctx.place('comoda', lowComode, () => .6, (s, vi) => vi * .6 + Math.min(s.t, 1 - s.t) * 2, { extraFp: entrance, frontMayCrossDoor: true });
      if (area(r) >= 9.5) {
        const lowChairs = chain('fotoliu', smaller('fotoliu')).filter(low);
        ctx.place('fotoliu', lowChairs, () => .45, (s, vi) => vi * .6 + Math.min(s.t, 1 - s.t) * 2, { extraFp: entrance, frontMayCrossDoor: true });
      }
    }

    return true;
  }
}

