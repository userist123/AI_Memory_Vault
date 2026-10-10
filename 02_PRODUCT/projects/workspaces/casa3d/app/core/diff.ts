// Compararea a două instantanee (revizii sau draft): camere, pereți, mobilier, cost. Pur, fără I/O.
import type { Catalog, Snapshot, Room, Wall, FurniturePlacement } from './types';
import { resolve, pricedOffer } from './catalog';

export interface RoomRef { id: string; name: string }
export interface FurnitureChange { id: string; roomName: string; name: string; detail?: string }
export interface CurrencyCost { before: number; after: number; delta: number; unknownBefore: number; unknownAfter: number }
export interface SnapshotDiff {
  rooms: { added: RoomRef[]; removed: RoomRef[]; changed: (RoomRef & { changes: string[] })[] };
  walls: { added: number; removed: number; changed: number };
  furniture: { added: FurnitureChange[]; removed: FurnitureChange[]; moved: FurnitureChange[]; swapped: FurnitureChange[]; resized: FurnitureChange[] };
  /** câte elemente și-au schimbat culoarea sau materialul (pereți, accente, podele, tavane, goluri, piese) */
  looks: number;
  /** Pe moneda ofertei (ISO 4217); piesele fără preț se numără separat, nu ca 0, și sunt atribuite monedei implicite a catalogului. */
  cost: Record<string, CurrencyCost>;
  isEmpty: boolean;
}

const MOVE_EPS_M = 0.01; // 1 cm
const cm = (m: number) => Math.round(m * 100);
const size = (r: Room) => ({ w: cm(r.rect.x1 - r.rect.x0), d: cm(r.rect.z1 - r.rect.z0) });
const sameRot = (a: number, b: number) => { const d = Math.abs(((a - b) % 360 + 360) % 360); return Math.min(d, 360 - d) < 1e-6; };
const wallKey = (w: Wall) => JSON.stringify([w.a, w.b, w.thickness, w.openings]);

function roomChanges(a: Room, b: Room): string[] {
  const out: string[] = [];
  if (a.name !== b.name) out.push(`redenumită „${a.name}” → „${b.name}”`);
  if (a.type !== b.type) out.push(`tip ${a.type} → ${b.type}`);
  const sa = size(a), sb = size(b);
  if (sa.w !== sb.w || sa.d !== sb.d) out.push(`redimensionată ${sa.w}×${sa.d} cm → ${sb.w}×${sb.d} cm`);
  return out;
}

export function diffSnapshots(before: Snapshot, after: Snapshot, cat: Catalog): SnapshotDiff {
  const ra = new Map(before.floor.rooms.map(r => [r.id, r])), rb = new Map(after.floor.rooms.map(r => [r.id, r]));
  const rooms: SnapshotDiff['rooms'] = { added: [], removed: [], changed: [] };
  for (const [id, r] of rb) if (!ra.has(id)) rooms.added.push({ id, name: r.name });
  for (const [id, r] of ra){ const n = rb.get(id); if (!n){ rooms.removed.push({ id, name: r.name }); continue; }
    const changes = roomChanges(r, n); if (changes.length) rooms.changed.push({ id, name: n.name, changes }); }

  const wa = new Map(before.floor.walls.map(w => [w.id, w])), wb = new Map(after.floor.walls.map(w => [w.id, w]));
  const walls = { added: 0, removed: 0, changed: 0 };
  for (const id of wb.keys()) if (!wa.has(id)) walls.added++;
  for (const [id, w] of wa){ const n = wb.get(id); if (!n) walls.removed++; else if (wallKey(w) !== wallKey(n)) walls.changed++; }

  const roomName = (id: string) => rb.get(id)?.name ?? ra.get(id)?.name ?? id;
  const prodName = (p: FurniturePlacement) => resolve(cat, p.variantId)?.product.name ?? p.group;
  const variantName = (v: string) => resolve(cat, v)?.variant.name ?? v;
  const ref = (p: FurniturePlacement, detail?: string): FurnitureChange => ({ id: p.id, roomName: roomName(p.roomId), name: prodName(p), ...(detail ? { detail } : {}) });
  const pa = new Map(before.placements.map(p => [p.id, p])), pb = new Map(after.placements.map(p => [p.id, p]));
  const furniture: SnapshotDiff['furniture'] = { added: [], removed: [], moved: [], swapped: [], resized: [] };
  for (const [id, p] of pb) if (!pa.has(id)) furniture.added.push(ref(p));
  for (const [id, p] of pa){ const n = pb.get(id); if (!n){ furniture.removed.push(ref(p)); continue; }
    if (p.variantId !== n.variantId) furniture.swapped.push(ref(n, `${variantName(p.variantId)} → ${variantName(n.variantId)}`));
    const sz = (q: FurniturePlacement) => q.size ? `${q.size.w}×${q.size.d}×${q.size.h} cm (pe comandă)` : 'din catalog';
    if (JSON.stringify(p.size ?? null) !== JSON.stringify(n.size ?? null)) furniture.resized.push(ref(n, `${sz(p)} → ${sz(n)}`));
    const dist = Math.hypot(n.x - p.x, n.z - p.z), far = dist > MOVE_EPS_M + 1e-9, rot = !sameRot(p.rotation, n.rotation);
    if (far || rot){
      const parts: string[] = []; if (far) parts.push(`${Math.round(dist * 100)} cm`); if (rot) parts.push(`rotire ${p.rotation}° → ${n.rotation}°`);
      furniture.moved.push(ref(n, parts.join(', '))); } }

  const defCur = cat.offers[0]?.currency ?? 'XXX';
  const tot = (pl: FurniturePlacement[]) => { const m: Record<string, { known: number; unknown: number }> = {};
    // prețul vine din pricedOffer (pe comandă = necunoscut); moneda piesei fără preț e a ofertei ei, dacă există
    for (const p of pl){ const o = pricedOffer(cat, p), c = o ? o.currency as string : (resolve(cat, p.variantId)?.offer?.currency ?? defCur);
      const e = m[c] ??= { known: 0, unknown: 0 }; if (o) e.known += o.price; else e.unknown++; }
    return m; };
  const tb = tot(before.placements), ta = tot(after.placements), cost: Record<string, CurrencyCost> = {};
  for (const c of new Set([...Object.keys(tb), ...Object.keys(ta)])){ const b = tb[c] ?? { known: 0, unknown: 0 }, a = ta[c] ?? { known: 0, unknown: 0 };
    const r2 = (v: number) => Math.round(v * 100) / 100;
    cost[c] = { before: r2(b.known), after: r2(a.known), delta: r2(a.known - b.known), unknownBefore: b.unknown, unknownAfter: a.unknown }; }
  const costSame = Object.values(cost).every(k => k.delta === 0 && k.unknownBefore === k.unknownAfter);
  // aspect: fiecare intrare (cameră, față de perete, gol, piesă) adăugată, ștearsă sau schimbată contează o dată
  const flat = (a: Snapshot['appearance']) => { const m = new Map<string, string>(); for (const [k, v] of Object.entries(a || {})) for (const [id, f] of Object.entries(v || {})) m.set(`${k}:${id}`, JSON.stringify(f)); return m; };
  const la = flat(before.appearance), lb = flat(after.appearance); let looks = 0; for (const k of new Set([...la.keys(), ...lb.keys()])) if (la.get(k) !== lb.get(k)) looks++;
  const isEmpty = !rooms.added.length && !rooms.removed.length && !rooms.changed.length && !walls.added && !walls.removed && !walls.changed
    && !furniture.added.length && !furniture.removed.length && !furniture.moved.length && !furniture.swapped.length && !furniture.resized.length && costSame && !looks;
  return { rooms, walls, furniture, looks, cost, isEmpty };
}
