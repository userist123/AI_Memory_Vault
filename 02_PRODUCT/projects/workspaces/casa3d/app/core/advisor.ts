// Consilierul de design: funcție pură și deterministă care spune ce nu e bine în proiect, de ce și cum se repară.
// Toate pragurile de mai jos sunt reguli de bun-simț din proiectarea interioară, NU norme legale.
import { MIN_OUTLETS } from './technical';
import type { Catalog, FurniturePlacement, Room, RoomRect, Snapshot } from './types';
import { resolve } from './catalog';
import { area, insideRect, rectHit } from './geometry';
import { doorZones, footprintOf, openingsOnSide, validateFloor, validatePlacement } from './validate';
import { finishesOf, roomGeometry } from './boq';
import { FRONT_CLEARANCE, TALL_ITEM_M, WALL_PROXIMITY_M } from './rules';
import { WINDOW_HEIGHT, LIGHTS_EXTRA_PER_M2 } from './rules.boq';
import { contrastRatio, hexToRgb, hueDistance, isNeutral, relativeLuminance, rgbToHsl } from './color';

export type AdviceSeverity = 'BLOCKER' | 'WARNING' | 'TIP';
export type AdviceCategory = 'circulatie' | 'proportii' | 'culori' | 'lumina' | 'ergonomie' | 'buget' | 'siguranta' | 'instalatii';
export interface AdviceRefs { roomId?: string; placementIds?: string[]; wallIds?: string[] }
/** Un sfat nu poartă text: `code` + `vars` sunt randate în limba aleasă prin dicționar (lib/i18n.ts, `advice.<code>.title|why|fix`).
 *  Convenția variabilelor: `*_m` = metri, `*_mu` = metri rotunjiți în sus (cel puțin), `*_m2` = m², `*_money` = sumă (moneda în `cur`), `*_n` = număr zecimal; restul sunt text sau numere simple.
 *  Un șir gol la `nm`/`other`/`rn` înseamnă „nu avem nume” și se înlocuiește la randare. */
export type AdviceVars = Record<string, string | number>;
export interface Advice { id: string; severity: AdviceSeverity; category: AdviceCategory; code: string; vars: AdviceVars; refs: AdviceRefs }
export interface ColorEntry { roomId: string; kind: 'wall' | 'floor' | 'item'; refId: string; hex: string }
export interface AdvisorOptions {
  accessibility?: boolean;
  colorsOf?: (snap: Snapshot, cat: Catalog) => ColorEntry[];
  budget?: { total: number | null; target: number | null; unknown: number };
  /** Moneda bugetului (implicit RON). */
  currency?: string;
}

// ---- Praguri (reguli de bun-simț, nu norme legale) ----
export const WARDROBE_CLEARANCE_M = 0.9;      // spațiu liber uzual în fața dulapurilor/comodelor, ca să se deschidă ușile și sertarele
export const BED_SIDE_CLEARANCE_M = 0.6;      // culoar uzual de ~60 cm pe cel puțin o latură a patului
export const ACCESSIBLE_CLEARANCE_M = 1.2;    // culoar uzual pentru scaun cu rotile / mers asistat
export const SOFA_TABLE_MIN_M = 0.35;         // distanță uzuală sofa–măsuță de cafea: sub 35 cm nu treci, peste 60 cm nu ajungi la ea
export const SOFA_TABLE_MAX_M = 0.6;
export const CROWDED_RATIO = 0.55;            // peste 55% din podea ocupată de mobilier = cameră aglomerată
export const EMPTY_LIVING_RATIO = 0.15;       // sub 15% într-un living = cameră „goală”
export const BIG_BED_WIDTH_M = 1.6;           // pat matrimonial (160 cm+)
export const SMALL_BEDROOM_M2 = 9;            // dormitor mic pentru un pat mare
export const SOFA_WALL_RATIO = 2 / 3;         // canapeaua nu ar trebui să ocupe mai mult de 2/3 din peretele de care stă lipită
export const WINDOW_TO_FLOOR_MIN = 1 / 8;     // aria ferestrelor ≥ 1/8 din aria podelei (regulă uzuală de lumină naturală)
export const TV_DISTANCE_MIN_M = 1.5;         // distanță uzuală de vizionare TV
export const TV_DISTANCE_MAX_M = 3.5;
export const DESK_WINDOW_MAX_M = 1.5;         // fereastră în spatele scaunului, la mai puțin de 1,5 m = reflexii pe ecran
export const MAX_DISTINCT_HUES = 4;           // mai mult de 4 nuanțe vii într-o cameră obosește privirea
export const HUE_CLUSTER_DEG = 30;            // două nuanțe la mai puțin de 30° se socotesc aceeași familie
export const MIN_ITEM_FLOOR_CONTRAST = 1.3;   // sub 1,3:1 piesa „se topește” în podea
export const LARGE_ITEM_M2 = 0.8;             // piesă mare = amprentă de cel puțin 0,8 m²
export const DARK_WALL_LUMINANCE = 0.15;      // pereți închiși la culoare (luminanță WCAG)
export const SMALL_ROOM_DARK_M2 = 10;         // camera mică pentru pereți închiși
export const SATURATED_MIN = 0.6;             // saturație peste care o culoare e „vie”
export const CLASH_HUE_MIN = 150;             // nuanțe aproape opuse (complementare)
export const CLASH_HUE_MAX = 210;
export const ASSUMED_WALL_HEX = '#f2efe8';    // ipoteză: pereți vopsiți deschis
export const ASSUMED_FLOOR_HEX = '#d8c9ae';   // ipoteză: pardoseală deschisă (parchet/gresie)

type Side = 'N' | 'S' | 'W' | 'E';
const SEV_ORDER: Record<AdviceSeverity, number> = { BLOCKER: 0, WARNING: 1, TIP: 2 };
const LIT_ROOMS = new Set(['living', 'dormitor', 'bucatarie']);
const QUARTER = Math.PI / 2;
const kOf = (rot: number) => ((Math.round(rot / QUARTER) % 4) + 4) % 4;
const FRONT_SIDE: Side[] = ['S', 'E', 'N', 'W'];            // direcția feței pentru k = 0..3 (convenția din frontRect)
const OPPOSITE: Record<Side, Side> = { N: 'S', S: 'N', W: 'E', E: 'W' };
const frontSideOf = (rot: number): Side => FRONT_SIDE[kOf(rot)];
const gapToWall = (r: RoomRect, fp: RoomRect, s: Side) => s === 'N' ? fp.z0 - r.z0 : s === 'S' ? r.z1 - fp.z1 : s === 'W' ? fp.x0 - r.x0 : r.x1 - fp.x1;
const sideStrip = (fp: RoomRect, s: Side, depth: number): RoomRect =>
  s === 'N' ? { x0: fp.x0, x1: fp.x1, z0: fp.z0 - depth, z1: fp.z0 } : s === 'S' ? { x0: fp.x0, x1: fp.x1, z0: fp.z1, z1: fp.z1 + depth }
  : s === 'W' ? { x0: fp.x0 - depth, x1: fp.x0, z0: fp.z0, z1: fp.z1 } : { x0: fp.x1, x1: fp.x1 + depth, z0: fp.z0, z1: fp.z1 };
const overlapsSpan = (s: Side, fp: RoomRect, o: { a: number; b: number }) => { const a = s === 'N' || s === 'S' ? fp.x0 : fp.z0, b = s === 'N' || s === 'S' ? fp.x1 : fp.z1; return b > o.a + .01 && a < o.b - .01; };
const rectGap = (a: RoomRect, b: RoomRect) => Math.hypot(Math.max(0, a.x0 - b.x1, b.x0 - a.x1), Math.max(0, a.z0 - b.z1, b.z0 - a.z1));
const centre = (r: RoomRect) => ({ x: (r.x0 + r.x1) / 2, z: (r.z0 + r.z1) / 2 });
const cm = (m: number) => Math.round(m * 100);
const cmUp = (m: number) => Math.max(1, Math.ceil(m * 100 - 1e-6));
const overlapDepth = (a: RoomRect, b: RoomRect) => Math.min(Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0), Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0));
const r1 = (x: number) => Math.round(x * 10) / 10;

/** Culorile implicite: culoarea mobilierului din catalog (style.col) + ipoteza unor pereți și pardoseli deschise. */
export function defaultColors(snap: Snapshot, cat: Catalog): ColorEntry[] {
  const out: ColorEntry[] = [];
  for (const r of snap.floor.rooms){ out.push({ roomId: r.id, kind: 'wall', refId: r.id, hex: ASSUMED_WALL_HEX }, { roomId: r.id, kind: 'floor', refId: r.id, hex: ASSUMED_FLOOR_HEX }); }
  for (const p of snap.placements){ const col = resolve(cat, p.variantId)?.variant.style?.col; if (typeof col === 'string' && hexToRgb(col)) out.push({ roomId: p.roomId, kind: 'item', refId: p.id, hex: col }); }
  return out;
}

/** Distanța maximă (m) între un obiect sanitar și scurgerea lui — regulă uzuală, orientativă. */
export const TECH_NEAR_M = 1.5;
function doorTouches(w: { a: [number, number]; b: [number, number] }, o: { offset: number; width: number }, room: Room){
  const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, m = o.offset + o.width / 2, x = w.a[0] + (w.b[0] - w.a[0]) / L * m, z = w.a[1] + (w.b[1] - w.a[1]) / L * m, r = room.rect;
  return x >= r.x0 - 0.2 && x <= r.x1 + 0.2 && z >= r.z0 - 0.2 && z <= r.z1 + 0.2;
}
export function adviseProject(snap: Snapshot, cat: Catalog, opts: AdvisorOptions = {}): Advice[] {
  const out: Advice[] = [], seen = new Set<string>();
  const clear = opts.accessibility ? ACCESSIBLE_CLEARANCE_M : null;
  const roomOf = (id: string) => snap.floor.rooms.find(r => r.id === id);
  const nameOf = (p: FurniturePlacement) => resolve(cat, p.variantId)?.product.name ?? '';
  const add = (rule: string, a: Omit<Advice, 'id'>) => {
    const id = `${rule}:${a.refs.roomId ?? ''}:${(a.refs.placementIds ?? []).slice().sort().join(',')}${a.refs.wallIds?.length ? ':' + a.refs.wallIds.join(',') : ''}`;
    if (seen.has(id)) return; seen.add(id); out.push({ id, ...a });
  };
  const fps = new Map<string, RoomRect>(); for (const p of snap.placements){ const f = footprintOf(cat, p); if (f) fps.set(p.id, f); }
  const inRoom = (room: Room) => snap.placements.filter(p => p.roomId === room.id && fps.has(p.id));
  const hasClearanceIssue = new Set<string>();

  // ---------- 1) Validare ----------
  for (const p of snap.placements){
    const room = roomOf(p.roomId), nm = nameOf(p), rn = room?.name ?? '', fp = fps.get(p.id);
    for (const i of validatePlacement(snap, cat, p)){
      const sev: AdviceSeverity = i.severity === 'ERROR' ? 'BLOCKER' : 'WARNING', other = i.with ? snap.placements.find(o => o.id === i.with) : undefined;
      const refs: AdviceRefs = { roomId: p.roomId, placementIds: other ? [p.id, other.id] : [p.id] };
      switch (i.code){
        case 'UNKNOWN_VARIANT': add('UNKNOWN_VARIANT', { severity: sev, category: 'siguranta', refs, code: 'UNKNOWN_VARIANT', vars: {} }); break;
        case 'OUT_OF_ROOM': add('OUT_OF_ROOM', { severity: sev, category: 'siguranta', refs, code: room ? 'OUT_OF_ROOM' : 'OUT_OF_ROOM_NO_ROOM', vars: { nm, rn } }); break;
        case 'OVERLAP': { const d = other && fp && fps.get(other.id) ? overlapDepth(fp, fps.get(other.id)!) : 0.1;
          add('OVERLAP', { severity: sev, category: 'siguranta', refs: { ...refs, placementIds: [p.id, ...(other ? [other.id] : [])].sort() }, code: 'OVERLAP', vars: { nm, other: other ? nameOf(other) : '', d_mu: d } }); break; }
        case 'DOOR_ZONE': { let d = 0.1; if (room && fp) for (const z of doorZones(snap.floor, room)) if (rectHit(z, fp)) { d = Math.max(d, overlapDepth(z, fp)); }
          add('DOOR_ZONE', { severity: sev, category: 'siguranta', refs, code: 'DOOR_ZONE', vars: { nm, rn, d_mu: d } }); break; }
        case 'WINDOW_BLOCKED': add('WINDOW_BLOCKED', { severity: sev, category: 'lumina', refs, code: 'WINDOW_BLOCKED', vars: { nm, h_m: TALL_ITEM_M } }); break;
        case 'CLEARANCE': { hasClearanceIssue.add(p.id);
          if (other) add('CLEARANCE', { severity: sev, category: 'circulatie', refs, code: 'CLEARANCE_PAIR', vars: { nm, other: nameOf(other), gap_m: 0.3 } });
          else add('CLEARANCE', { severity: sev, category: 'circulatie', refs, code: 'CLEARANCE_FRONT', vars: { nm, need_m: cm(FRONT_CLEARANCE[p.group] ?? 0.6) / 100 } });
          break; }
      }
    }
  }
  for (const i of validateFloor(snap.floor)){
    const esc = (t: string) => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), wallId = snap.floor.walls.find(w => new RegExp(`(^|[^\\w-])${esc(w.id)}(?![\\w-])`).test(i.message))?.id;
    const refs: AdviceRefs = wallId ? { wallIds: [wallId] } : {};
    if (i.code === 'WALL_TOO_SHORT') add('WALL_TOO_SHORT', { severity: 'BLOCKER', category: 'siguranta', refs, code: 'WALL_TOO_SHORT', vars: { min_m: 0.2 } });
    else add('OPENING_OUTSIDE_WALL', { severity: 'BLOCKER', category: 'siguranta', refs, code: 'OPENING_OUTSIDE_WALL', vars: {} });
  }

  // ---------- 2) Pe cameră ----------
  const mainClear = (base: number) => clear ? Math.max(base, clear) : base;
  const freeRect = (room: Room, r: RoomRect, ignore: Set<string>) => insideRect(room.rect, r) && !inRoom(room).some(o => !ignore.has(o.id) && rectHit(r, fps.get(o.id)!));
  for (const room of snap.floor.rooms){
    const ps = inRoom(room), g = roomGeometry(snap, room), A = g.floorArea, rid = room.id, rn = room.name;
    const ofGroup = (grp: string) => ps.filter(p => p.group === grp);

    // Circulație
    const wc = mainClear(WARDROBE_CLEARANCE_M);
    for (const p of ofGroup('dulap')){ if (!clear && hasClearanceIssue.has(p.id)) continue;
      const fp = fps.get(p.id)!, fr = sideStrip(fp, frontSideOf(p.rotation), wc);
      if (!freeRect(room, fr, new Set([p.id]))) add('WARDROBE_FRONT', { severity: 'WARNING', category: 'circulatie', refs: { roomId: rid, placementIds: [p.id] }, code: 'WARDROBE_FRONT', vars: { rn, wc_m: cm(wc) / 100 } }); }
    const bc = mainClear(BED_SIDE_CLEARANCE_M);
    for (const p of ofGroup('pat')){ const fp = fps.get(p.id)!, back = OPPOSITE[frontSideOf(p.rotation)];
      const lateral = (['N', 'S', 'W', 'E'] as Side[]).filter(s => s !== back && s !== OPPOSITE[back] && s !== frontSideOf(p.rotation));
      const ignore = new Set([p.id, ...ofGroup('noptiera').map(n => n.id)]);
      if (!lateral.some(s => freeRect(room, sideStrip(fp, s, bc), ignore))) add('BED_ACCESS', { severity: 'WARNING', category: 'circulatie', refs: { roomId: rid, placementIds: [p.id] }, code: 'BED_ACCESS', vars: { rn, bc_m: cm(bc) / 100 } }); }
    for (const s of ofGroup('canapea')){ const masutas = ofGroup('masuta'); if (!masutas.length) continue;
      const fs = fps.get(s.id)!, best = masutas.map(m => ({ m, d: rectGap(fs, fps.get(m.id)!) })).sort((a, b) => a.d - b.d || (a.m.id < b.m.id ? -1 : 1))[0];
      const refs = { roomId: rid, placementIds: [s.id, best.m.id] }, range = { rn, lo_m: cm(SOFA_TABLE_MIN_M) / 100, hi_m: cm(SOFA_TABLE_MAX_M) / 100, d_m: cm(best.d) / 100 };
      if (best.d < SOFA_TABLE_MIN_M - 1e-6) add('SOFA_TABLE', { severity: 'TIP', category: 'circulatie', refs, code: 'SOFA_TABLE_NEAR', vars: { ...range, by_mu: SOFA_TABLE_MIN_M - best.d } });
      else if (best.d > SOFA_TABLE_MAX_M + 1e-6) add('SOFA_TABLE', { severity: 'TIP', category: 'circulatie', refs, code: 'SOFA_TABLE_FAR', vars: { ...range, by_mu: best.d - SOFA_TABLE_MAX_M } }); }

    // Proporții
    const used = ps.filter(p => p.group !== 'scaunBirou').reduce((a, p) => { const f = fps.get(p.id)!; return a + area(f); }, 0);
    if (A > 0 && used / A > CROWDED_RATIO) add('CROWDED', { severity: 'WARNING', category: 'proportii', refs: { roomId: rid }, code: 'CROWDED', vars: { rn, pct: Math.round(used / A * 100), used_m2: used, area_m2: A, max_pct: Math.round(CROWDED_RATIO * 100), cut_m2: used - CROWDED_RATIO * A } });
    if (room.type === 'living' && A > 0 && used / A < EMPTY_LIVING_RATIO) add('EMPTY_LIVING', { severity: 'TIP', category: 'proportii', refs: { roomId: rid }, code: 'EMPTY_LIVING', vars: { rn, pct: Math.round(used / A * 100), min_pct: Math.round(EMPTY_LIVING_RATIO * 100) } });
    if (room.type === 'dormitor' && A < SMALL_BEDROOM_M2) for (const p of ofGroup('pat')){ const rv = resolve(cat, p.variantId)!;
      if (Math.min(rv.w, rv.d) >= BIG_BED_WIDTH_M - 1e-6) add('BIG_BED_SMALL_ROOM', { severity: 'WARNING', category: 'proportii', refs: { roomId: rid, placementIds: [p.id] }, code: 'BIG_BED_SMALL_ROOM', vars: { rn, w_m: cm(Math.min(rv.w, rv.d)) / 100, area_m2: A, small_m2: SMALL_BEDROOM_M2, narrow_m: 1.4 } }); }
    for (const s of ofGroup('canapea')){ const rv = resolve(cat, s.variantId)!, fp = fps.get(s.id)!, back = OPPOSITE[frontSideOf(s.rotation)];
      if (gapToWall(room.rect, fp, back) > WALL_PROXIMITY_M) continue;
      const wl = back === 'N' || back === 'S' ? room.rect.x1 - room.rect.x0 : room.rect.z1 - room.rect.z0, len = Math.max(rv.w, rv.d);
      if (len > wl * SOFA_WALL_RATIO + 1e-6) add('SOFA_LONG', { severity: 'TIP', category: 'proportii', refs: { roomId: rid, placementIds: [s.id] }, code: 'SOFA_LONG', vars: { rn, len_m: cm(len) / 100, wall_m: cm(wl) / 100, max_m: cm(wl * SOFA_WALL_RATIO) / 100 } }); }

    // Lumină
    if (LIT_ROOMS.has(room.type)){
      if (g.windowWidth <= 0) add('NO_WINDOW', { severity: 'WARNING', category: 'lumina', refs: { roomId: rid }, code: 'NO_WINDOW', vars: { rn } });
      else { const wa = g.windowWidth * WINDOW_HEIGHT;
        if (A > 0 && wa < A * WINDOW_TO_FLOOR_MIN - 1e-6) add('SMALL_WINDOW', { severity: 'TIP', category: 'lumina', refs: { roomId: rid }, code: 'SMALL_WINDOW', vars: { rn, win_m2: wa, pct: Math.round(wa / A * 100), min_m2: A * WINDOW_TO_FLOOR_MIN, add_mu: (A * WINDOW_TO_FLOOR_MIN - wa) / WINDOW_HEIGHT } }); }
    }
    const lights = finishesOf(snap, room).lights, need = 1 + Math.max(0, Math.ceil((A - LIGHTS_EXTRA_PER_M2) / LIGHTS_EXTRA_PER_M2));
    if (lights != null && lights < need) add('FEW_LIGHTS', { severity: 'TIP', category: 'lumina', refs: { roomId: rid }, code: 'FEW_LIGHTS', vars: { rn, area_m2: A, per_m2: LIGHTS_EXTRA_PER_M2, need, have: lights } });

    // Ergonomie
    for (const t of ofGroup('comodaTv')){ const sofas = ofGroup('canapea'); if (!sofas.length) continue;
      const ct = centre(fps.get(t.id)!), best = sofas.map(s => { const c = centre(fps.get(s.id)!); return { s, d: Math.hypot(c.x - ct.x, c.z - ct.z) }; }).sort((a, b) => a.d - b.d || (a.s.id < b.s.id ? -1 : 1))[0];
      const refs = { roomId: rid, placementIds: [t.id, best.s.id] }, range = { rn, d_m: r1(best.d), min_m: TV_DISTANCE_MIN_M, max_m: TV_DISTANCE_MAX_M };
      if (best.d < TV_DISTANCE_MIN_M - 1e-6) add('TV_DISTANCE', { severity: 'TIP', category: 'ergonomie', refs, code: 'TV_NEAR', vars: { ...range, by_mu: TV_DISTANCE_MIN_M - best.d } });
      else if (best.d > TV_DISTANCE_MAX_M + 1e-6) add('TV_DISTANCE', { severity: 'TIP', category: 'ergonomie', refs, code: 'TV_FAR', vars: { ...range, by_mu: best.d - TV_DISTANCE_MAX_M } }); }
    for (const d of ofGroup('birou')){ const fp = fps.get(d.id)!, s = frontSideOf(d.rotation), gap = gapToWall(room.rect, fp, s);
      if (gap <= DESK_WINDOW_MAX_M && openingsOnSide(snap.floor, room, s).some(o => o.kind === 'window' && overlapsSpan(s, fp, o))) add('DESK_GLARE', { severity: 'TIP', category: 'ergonomie', refs: { roomId: rid, placementIds: [d.id] }, code: 'DESK_GLARE', vars: { rn } }); }
    for (const b of ofGroup('pat')){ const fp = fps.get(b.id)!, back = OPPOSITE[frontSideOf(b.rotation)];
      if (gapToWall(room.rect, fp, back) <= WALL_PROXIMITY_M && openingsOnSide(snap.floor, room, back).some(o => o.kind === 'window' && overlapsSpan(back, fp, o))) add('HEADBOARD_WINDOW', { severity: 'TIP', category: 'ergonomie', refs: { roomId: rid, placementIds: [b.id] }, code: 'HEADBOARD_WINDOW', vars: { rn } }); }
  }

  // ---------- 3) Culori ----------
  const colors = (opts.colorsOf ?? defaultColors)(snap, cat);
  for (const room of snap.floor.rooms){
    const cs = colors.filter(c => c.roomId === room.id && hexToRgb(c.hex)), A = roomGeometry(snap, room).floorArea, rid = room.id, rn = room.name;
    const vivid = cs.filter(c => !isNeutral(c.hex)).map(c => ({ c, hsl: rgbToHsl(hexToRgb(c.hex)!) })).sort((a, b) => a.hsl.h - b.hsl.h || (a.c.hex < b.c.hex ? -1 : a.c.hex > b.c.hex ? 1 : 0));
    const reps: number[] = []; for (const v of vivid) if (!reps.some(h => hueDistance(h, v.hsl.h) < HUE_CLUSTER_DEG)) reps.push(v.hsl.h);
    if (reps.length > MAX_DISTINCT_HUES) add('TOO_MANY_HUES', { severity: 'WARNING', category: 'culori', refs: { roomId: rid }, code: 'TOO_MANY_HUES', vars: { rn, count: reps.length, max: MAX_DISTINCT_HUES, drop: reps.length - MAX_DISTINCT_HUES } });
    const floor = cs.filter(c => c.kind === 'floor').at(-1);
    if (floor) for (const c of cs.filter(c => c.kind === 'item')){ const p = snap.placements.find(x => x.id === c.refId), fp = p && fps.get(p.id); if (!p || !fp || area(fp) < LARGE_ITEM_M2) continue;
      const ratio = contrastRatio(c.hex, floor.hex); if (ratio < MIN_ITEM_FLOOR_CONTRAST) add('LOW_CONTRAST', { severity: 'TIP', category: 'culori', refs: { roomId: rid, placementIds: [p.id] }, code: 'LOW_CONTRAST', vars: { nm: nameOf(p), rn, ratio_n: Math.round(ratio * 100) / 100, min_n: MIN_ITEM_FLOOR_CONTRAST } }); }
    if (A < SMALL_ROOM_DARK_M2 && cs.some(c => c.kind === 'wall' && relativeLuminance(c.hex) < DARK_WALL_LUMINANCE)) add('DARK_WALLS', { severity: 'TIP', category: 'culori', refs: { roomId: rid }, code: 'DARK_WALLS', vars: { rn, area_m2: A, small_m2: SMALL_ROOM_DARK_M2 } });
    const sat = vivid.filter(v => v.hsl.s >= SATURATED_MIN); let clash = false;
    for (let i = 0; i < sat.length && !clash; i++) for (let j = i + 1; j < sat.length; j++){ const d = hueDistance(sat[i].hsl.h, sat[j].hsl.h);
      if (d >= CLASH_HUE_MIN && d <= CLASH_HUE_MAX){ clash = true; break; } }
    if (clash) add('COLOR_CLASH', { severity: 'TIP', category: 'culori', refs: { roomId: rid }, code: 'COLOR_CLASH', vars: { rn } });
  }

  // ---------- 4) Buget ----------
  // instalații: doar când utilizatorul a generat sau desenat stratul tehnic (fără el nu știm ce există)
  if (snap.tech){
    const pts = snap.tech, near = (p: FurniturePlacement, kinds: string[], max = TECH_NEAR_M) => pts.some(t => t.roomId === p.roomId && kinds.includes(t.kind) && Math.hypot(t.x - p.x, t.z - p.z) <= max);
    for (const room of snap.floor.rooms){ const rn = room.name, n = pts.filter(t => t.roomId === room.id && (t.kind === 'outlet' || t.kind === 'outlet_double')).length, min = MIN_OUTLETS[room.type] ?? 1;
      if (n < min) add('TECH_FEW_OUTLETS', { severity: 'TIP', category: 'instalatii', refs: { roomId: room.id }, code: 'TECH_FEW_OUTLETS', vars: { rn, count: n, min } });
      const doors = snap.floor.walls.some(w => w.openings.some(o => o.kind === 'door' && doorTouches(w, o, room)));
      if (doors && !pts.some(t => t.roomId === room.id && t.kind === 'switch')) add('TECH_NO_SWITCH', { severity: 'TIP', category: 'instalatii', refs: { roomId: room.id }, code: 'TECH_NO_SWITCH', vars: { rn } }); }
    for (const p of snap.placements.filter(q => ['lavoar', 'dus', 'wc', 'bucatarie'].includes(q.group)))
      if (!near(p, ['drain'])) add('TECH_NO_DRAIN', { severity: 'WARNING', category: 'instalatii', refs: { roomId: p.roomId, placementIds: [p.id] }, code: 'TECH_NO_DRAIN', vars: { nm: nameOf(p), rn: roomOf(p.roomId)?.name ?? '' } });
  }
  const b = opts.budget, cur = opts.currency ?? 'RON';
  if (b){
    if (b.total != null && b.target != null && b.total > b.target) add('OVER_BUDGET', { severity: 'WARNING', category: 'buget', refs: {}, code: 'OVER_BUDGET', vars: { cur, total_money: b.total, over_money: b.total - b.target, target_money: b.target } });
    if (b.unknown > 0) add('UNKNOWN_PRICES', { severity: 'TIP', category: 'buget', refs: {}, code: b.unknown === 1 ? 'UNKNOWN_PRICES_ONE' : 'UNKNOWN_PRICES', vars: { count: b.unknown } });
  }

  const roomIdx = new Map(snap.floor.rooms.map((r, i) => [r.id, i])), rank = (a: Advice) => a.refs.roomId != null && roomIdx.has(a.refs.roomId) ? roomIdx.get(a.refs.roomId)! : 1e6;
  return out.sort((a, b) => SEV_ORDER[a.severity] - SEV_ORDER[b.severity] || rank(a) - rank(b) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}
