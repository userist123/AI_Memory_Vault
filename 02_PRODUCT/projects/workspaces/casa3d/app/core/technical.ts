// Stratul tehnic: prize, întrerupătoare, puncte de lumină și de date, apă rece/caldă, scurgeri.
// Sugestiile sunt deterministe, din mobila așezată și tipul camerei; pragurile sunt reguli uzuale de bun-simț,
// nu norme legale (normativele diferă pe țări). Utilizatorul acceptă sau modifică punctele.
import type { Floor, FurniturePlacement, Room, Snapshot } from './types';
import { finishesOf } from './boq';
import { LIGHTS_EXTRA_PER_M2 } from './rules.boq';
import { area } from './geometry';

export type TechKind = 'outlet' | 'outlet_double' | 'switch' | 'light_point' | 'data' | 'water_cold' | 'water_hot' | 'drain' | 'cooker';
export interface TechPoint { id: string; kind: TechKind; roomId: string; x: number; z: number; height: number; reason: string }
export const TECH_KINDS: Record<TechKind, { category: 'electric' | 'plumbing'; height: number; label: { ro: string; en: string } }> = {
  outlet: { category: 'electric', height: 0.3, label: { ro: 'Priză simplă', en: 'Single socket' } },
  outlet_double: { category: 'electric', height: 0.3, label: { ro: 'Priză dublă', en: 'Double socket' } },
  switch: { category: 'electric', height: 1.1, label: { ro: 'Întrerupător', en: 'Light switch' } },
  light_point: { category: 'electric', height: 0, label: { ro: 'Punct de lumină (tavan)', en: 'Ceiling light point' } },
  data: { category: 'electric', height: 0.3, label: { ro: 'Priză date / TV', en: 'Data / TV outlet' } },
  cooker: { category: 'electric', height: 0.3, label: { ro: 'Circuit dedicat plită/cuptor', en: 'Cooker circuit' } },
  water_cold: { category: 'plumbing', height: 0.55, label: { ro: 'Apă rece', en: 'Cold water' } },
  water_hot: { category: 'plumbing', height: 0.55, label: { ro: 'Apă caldă', en: 'Hot water' } },
  drain: { category: 'plumbing', height: 0.45, label: { ro: 'Scurgere', en: 'Drain' } },
};
/** Numărul minim de prize (simple sau duble) pe tip de cameră — regulă uzuală, orientativă. */
export const MIN_OUTLETS: Record<string, number> = { living: 5, dormitor: 4, bucatarie: 6, hol: 1, baie: 2, birou: 4 };
const SWITCH_FROM_DOOR = 0.15, EDGE_INSET = 0.02, COUNTER_HEIGHT = 1.1, MAX_SWITCHES = 2;

type Edge = { x0: number; z0: number; x1: number; z1: number };
const edges = (r: Room): Edge[] => { const { x0, z0, x1, z1 } = r.rect; return [{ x0, z0, x1, z1: z0 }, { x0: x1, z0, x1, z1 }, { x0, z0: z1, x1, z1 }, { x0, z0, x1: x0, z1 }]; };
const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));
/** Punctul de pe peretele camerei cel mai apropiat de (x, z), ușor în interior. */
export function nearestWallPoint(room: Room, x: number, z: number): { x: number; z: number; edge: number } {
  let best = { x, z, edge: 0, d: Infinity };
  edges(room).forEach((e, i) => { const px = clamp(x, Math.min(e.x0, e.x1), Math.max(e.x0, e.x1)), pz = clamp(z, Math.min(e.z0, e.z1), Math.max(e.z0, e.z1)), d = Math.hypot(px - x, pz - z);
    if (d < best.d) best = { x: px, z: pz, edge: i, d }; });
  const r = room.rect; return { x: clamp(best.x, r.x0 + EDGE_INSET, r.x1 - EDGE_INSET), z: clamp(best.z, r.z0 + EDGE_INSET, r.z1 - EDGE_INSET), edge: best.edge };
}
/** Golurile (uși, ferestre) ca intervale în coordonate de plan, pentru a nu pune puncte în ele. */
function openingSpans(floor: Floor){ return floor.walls.flatMap(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
  return w.openings.map(o => ({ kind: o.kind, entrance: !!o.entrance, ax: w.a[0] + ux * o.offset, az: w.a[1] + uz * o.offset, bx: w.a[0] + ux * (o.offset + o.width), bz: w.a[1] + uz * (o.offset + o.width), ux, uz, th: w.thickness })); }); }
function insideOpening(floor: Floor, x: number, z: number){ return openingSpans(floor).find(s => { const t = (x - s.ax) * s.ux + (z - s.az) * s.uz, L = Math.hypot(s.bx - s.ax, s.bz - s.az), perp = Math.abs((x - s.ax) * -s.uz + (z - s.az) * s.ux);
  return t > -0.05 && t < L + 0.05 && perp < s.th / 2 + 0.1; }); }
/** Mută punctul de pe perete în afara unei uși/ferestre, de-a lungul peretelui. */
function avoidOpenings(floor: Floor, room: Room, p: { x: number; z: number }): { x: number; z: number } {
  const s = insideOpening(floor, p.x, p.z); if (!s) return p; const r = room.rect;
  for (const [x, z] of [[s.bx + s.ux * 0.15, s.bz + s.uz * 0.15], [s.ax - s.ux * 0.15, s.az - s.uz * 0.15]] as [number, number][]){
    const q = { x: clamp(x, r.x0 + EDGE_INSET, r.x1 - EDGE_INSET), z: clamp(z, r.z0 + EDGE_INSET, r.z1 - EDGE_INSET) }; if (!insideOpening(floor, q.x, q.z)) return q; }
  return p;
}

/** Punctele tehnice sugerate pentru tot proiectul (ids stabile: tip, cameră, ordine). */
export function suggestTechPoints(snap: Snapshot): TechPoint[] {
  const out: TechPoint[] = [], n: Record<string, number> = {};
  const add = (kind: TechKind, room: Room, at: { x: number; z: number }, reason: string, height = TECH_KINDS[kind].height) => {
    const k = `${kind}:${room.id}`; n[k] = (n[k] ?? 0) + 1; const p = kind === 'light_point' ? at : avoidOpenings(snap.floor, room, at);
    out.push({ id: `${k}:${n[k]}`, kind, roomId: room.id, x: Math.round(p.x * 1000) / 1000, z: Math.round(p.z * 1000) / 1000, height, reason }); };
  for (const room of snap.floor.rooms){
    const pieces = snap.placements.filter(p => p.roomId === room.id), at = (p: FurniturePlacement) => nearestWallPoint(room, p.x, p.z), ceil = snap.floor.ceilingHeight || 2.6;
    // lumină: punctele de tavan, câte corpuri are camera în buget, pe o grilă simplă
    const f = finishesOf(snap, room), A = area(room.rect), lights = f.lights ?? (1 + Math.max(0, Math.ceil((A - LIGHTS_EXTRA_PER_M2) / LIGHTS_EXTRA_PER_M2)));
    const r = room.rect, cols = Math.ceil(Math.sqrt(lights)), rows = Math.ceil(lights / cols);
    for (let i = 0; i < lights; i++){ const c = i % cols, rr = Math.floor(i / cols); add('light_point', room, { x: r.x0 + (r.x1 - r.x0) * (c + 0.5) / cols, z: r.z0 + (r.z1 - r.z0) * (rr + 0.5) / rows }, 'iluminat general', ceil); }
    // întrerupător lângă ușile camerei, pe partea camerei, la 15 cm de toc; cel mult MAX_SWITCHES pe cameră,
    // întâi la ușa de intrare (un hol cu cinci uși nu are cinci întrerupătoare: fiecare cameră își are întrerupătorul ei)
    const doors = openingSpans(snap.floor).filter(s => s.kind === 'door' && (() => { const mx = (s.ax + s.bx) / 2, mz = (s.az + s.bz) / 2; return mx >= r.x0 - 0.2 && mx <= r.x1 + 0.2 && mz >= r.z0 - 0.2 && mz <= r.z1 + 0.2; })())
      .sort((a, b) => Number(b.entrance) - Number(a.entrance));
    for (const s of doors.slice(0, MAX_SWITCHES)){ const mx = (s.ax + s.bx) / 2, mz = (s.az + s.bz) / 2;
      const q = nearestWallPoint(room, s.bx + s.ux * SWITCH_FROM_DOOR, s.bz + s.uz * SWITCH_FROM_DOOR); if (Math.hypot(q.x - mx, q.z - mz) > 1.5) continue; add('switch', room, q, 'lângă ușă'); }
    for (const p of pieces){ const w = at(p);
      switch (p.group){
        case 'noptiera': add('outlet_double', room, w, 'lângă noptieră (veioză, încărcător)'); break;
        case 'pat': if (!pieces.some(q => q.group === 'noptiera')) add('outlet_double', room, w, 'la capătul patului'); break;
        case 'birou': add('outlet_double', room, w, 'la birou'); add('data', room, w, 'internet la birou'); break;
        case 'comodaTv': add('outlet_double', room, w, 'în spatele televizorului'); add('data', room, w, 'TV / internet'); break;
        case 'canapea': add('outlet_double', room, w, 'lângă canapea'); break;
        case 'frigider': add('outlet', room, w, 'frigider (circuit propriu recomandat)'); break;
        case 'bucatarie': add('outlet_double', room, w, 'deasupra blatului', COUNTER_HEIGHT); add('outlet_double', room, w, 'deasupra blatului', COUNTER_HEIGHT); add('outlet_double', room, w, 'deasupra blatului', COUNTER_HEIGHT);
          add('cooker', room, w, 'plită și cuptor'); add('outlet', room, w, 'hotă', 2.0); add('water_cold', room, w, 'chiuveta'); add('water_hot', room, w, 'chiuveta'); add('drain', room, w, 'chiuveta'); break;
        case 'lavoar': add('water_cold', room, w, 'lavoar'); add('water_hot', room, w, 'lavoar'); add('drain', room, w, 'lavoar'); add('outlet', room, w, 'lângă oglindă (protejată la umezeală)', 1.2); break;
        case 'wc': add('water_cold', room, w, 'rezervor WC', 0.2); add('drain', room, w, 'WC', 0.1); break;
        case 'dus': add('water_cold', room, w, 'baterie duș', 1.1); add('water_hot', room, w, 'baterie duș', 1.1); add('drain', room, w, 'sifon duș', 0); break;
      } }
    // completează până la minimul uzual pe tipul camerei, pe pereți, la distanță de cele existente
    const outlets = () => out.filter(t => t.roomId === room.id && (t.kind === 'outlet' || t.kind === 'outlet_double')).length, need = MIN_OUTLETS[room.type] ?? 1;
    const perim = edges(room).flatMap(e => [0.25, 0.5, 0.75].map(t => ({ x: e.x0 + (e.x1 - e.x0) * t, z: e.z0 + (e.z1 - e.z0) * t })));
    for (const c of perim){ if (outlets() >= need) break; const q = nearestWallPoint(room, c.x, c.z);
      if (out.some(t => t.roomId === room.id && Math.hypot(t.x - q.x, t.z - q.z) < 0.8) || insideOpening(snap.floor, q.x, q.z)) continue; add('outlet', room, q, 'minimum uzual pentru cameră'); }
  }
  return out;
}
/** Numărătoarea pe camere și pe tip, pentru electrician și instalator. */
export function techCounts(points: TechPoint[]): { byRoom: Record<string, Partial<Record<TechKind, number>>>; total: Partial<Record<TechKind, number>> } {
  const byRoom: Record<string, Partial<Record<TechKind, number>>> = {}, total: Partial<Record<TechKind, number>> = {};
  for (const p of points){ const r = (byRoom[p.roomId] ||= {}); r[p.kind] = (r[p.kind] ?? 0) + 1; total[p.kind] = (total[p.kind] ?? 0) + 1; }
  return { byRoom, total };
}

export const MAX_TECH_POINTS = 600;
/** Curăță punctele tehnice venite din client: tipuri cunoscute, camere existente, coordonate în cameră, înălțime sub tavan. */
export function sanitizeTech(x: unknown, snap: Pick<Snapshot, 'floor'>): TechPoint[] | undefined {
  if (!Array.isArray(x)) return undefined; const rooms = new Map(snap.floor.rooms.map(r => [r.id, r])), ceil = snap.floor.ceilingHeight || 2.6, seen = new Set<string>(), out: TechPoint[] = [];
  for (const v of x.slice(0, MAX_TECH_POINTS)){ if (!v || typeof v !== 'object') continue; const q = v as any, r = rooms.get(q.roomId);
    if (!r || !(q.kind in TECH_KINDS) || typeof q.id !== 'string' || !/^[\w:.-]{1,80}$/.test(q.id) || seen.has(q.id)) continue;
    if (![q.x, q.z, q.height].every(n => typeof n === 'number' && Number.isFinite(n))) continue;
    if (q.x < r.rect.x0 - 0.05 || q.x > r.rect.x1 + 0.05 || q.z < r.rect.z0 - 0.05 || q.z > r.rect.z1 + 0.05 || q.height < 0 || q.height > ceil + 1e-9) continue;
    seen.add(q.id); out.push({ id: q.id, kind: q.kind, roomId: q.roomId, x: q.x, z: q.z, height: q.height, reason: typeof q.reason === 'string' ? q.reason.slice(0, 120) : '' }); }
  return out;
}
/** Punctul plasat manual: pe peretele cel mai apropiat al camerei în care s-a dat clic. */
export function placeTechPoint(snap: Snapshot, kind: TechKind, x: number, z: number): TechPoint | null {
  const room = snap.floor.rooms.find(r => x >= r.rect.x0 && x <= r.rect.x1 && z >= r.rect.z0 && z <= r.rect.z1); if (!room) return null;
  const ceil = snap.floor.ceilingHeight || 2.6, at = kind === 'light_point' ? { x, z } : nearestWallPoint(room, x, z);
  const used = new Set((snap.tech || []).map(t => t.id)); let i = 1; while (used.has(`${kind}:${room.id}:m${i}`)) i++;
  return { id: `${kind}:${room.id}:m${i}`, kind, roomId: room.id, x: Math.round(at.x * 1000) / 1000, z: Math.round(at.z * 1000) / 1000, height: kind === 'light_point' ? ceil : TECH_KINDS[kind].height, reason: 'manual' };
}
