// Uși de interior: produsul ales pe fiecare gol de ușă (foaie + toc) și mânerul; mărimea comparată cu golul.
import type { DoorChoice, DoorType, Floor, MaterialsCatalog, Snapshot } from './types';
import { floors } from './levels';
import { materialOf } from './finishes';
import { doorTypeOf, isSlidingType, slideZone, inSlideZone } from './door-geometry';
export { doorTypeOf, isSlidingType, slideSide, slideZone, doorSwing, type DoorSwing } from './door-geometry';

export const DOOR_RULES = { sizeTolM: .1 };
export interface DoorOpening { id: string; level: number; widthM: number; entrance: boolean; wallIndex: number; openingIndex: number; roomIds: string[]; type: DoorType; flush: boolean }
export function doorOpenings(snap: Snapshot): DoorOpening[] {
  // camerele de o parte și de alta a ușii: cele al căror dreptunghi atinge mijlocul golului
  return floors(snap).flatMap((fl: Floor, level) => fl.walls.flatMap((w, wi) => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    // golul de trecere nu are foaie: nu cere produs de ușă, mâner sau montaj
    return w.openings.flatMap((o, oi) => { if (o.kind !== 'door' || o.passage) return []; const m = o.offset + o.width / 2, x = w.a[0] + ux * m, z = w.a[1] + uz * m, e = .01;
      const roomIds = fl.rooms.filter(r => x >= r.rect.x0 - e && x <= r.rect.x1 + e && z >= r.rect.z0 - e && z <= r.rect.z1 + e).map(r => r.id);
      return [{ id: o.id, level, widthM: o.width, entrance: !!o.entrance, wallIndex: wi, openingIndex: oi, roomIds, type: doorTypeOf(o)!, flush: !!o.flush }]; }); }));
}
export interface DoorIssue { key: string; openingId: string; vars?: Record<string, string | number> }
export function doorIssues(snap: Snapshot, mc: MaterialsCatalog): DoorIssue[] {
  const out: DoorIssue[] = [];
  for (const d of doorOpenings(snap)){ const c = snap.doors?.[d.id], m = materialOf(mc, c?.product); if (!m) continue;
    if (d.entrance) out.push({ key: 'door.entrance', openingId: d.id, vars: { name: m.name } });
    const w = (m.specs?.sizeCm?.[0] ?? 0) / 100; if (w && Math.abs(w - d.widthM) > DOOR_RULES.sizeTolM) out.push({ key: 'door.size', openingId: d.id, vars: { door: Math.round(w * 100), gap: Math.round(d.widthM * 100) } });
    if (!c?.handle) out.push({ key: 'door.noHandle', openingId: d.id });
    // produsul declară tipurile pentru care e făcut (specs.doorTypes): altul decât al golului → avertisment, produsul rămâne
    const dt = m.specs?.doorTypes; if (dt?.length && !dt.includes(d.type)) out.push({ key: 'door.typeMismatch', openingId: d.id, vars: { name: m.name, type: dt.join(', '), gap: d.type } }); }
  // glisantele: o priză, un întrerupător sau un termostat pe zona unde parchează foaia (pe fața peretelui sau în casetă)
  for (const d of doorOpenings(snap)){ if (!isSlidingType(d.type) || !snap.tech?.length) continue; const fl = floors(snap)[d.level]!, z = slideZone(fl, d.wallIndex, d.openingIndex); if (!z) continue;
    const rooms = new Set(fl.rooms.map(r => r.id)), hit = snap.tech.find(p => rooms.has(p.roomId) && WALL_TECH.has(p.kind) && inSlideZone(z, p.x, p.z));
    if (hit) out.push({ key: 'door.techInSlide', openingId: d.id, vars: { point: hit.kind } }); }
  return out;
}
// punctele tehnice montate pe perete (fără punctul de lumină din tavan și fără instalațiile sanitare)
const WALL_TECH = new Set(['outlet', 'outlet_double', 'switch', 'data', 'cooker', 'thermostat']);
/** Păstrează doar golurile de ușă existente și id-uri de produs scurte; altceva → mesaj de eroare (400). */
export function sanitizeDoors(doors: unknown, snap: Snapshot): { value?: Record<string, DoorChoice>; error?: string } {
  if (doors == null) return {}; if (typeof doors !== 'object' || Array.isArray(doors) || Object.keys(doors).length > 200) return { error: 'Ușile sunt invalide.' };
  const ids = new Set(doorOpenings(snap).map(d => d.id)), id = (x: unknown) => x == null || (typeof x === 'string' && x.length <= 80), out: Record<string, DoorChoice> = {};
  for (const [k, v] of Object.entries(doors as Record<string, any>)){ if (!v || typeof v !== 'object' || !id(v.product) || !id(v.handle) || !id(v.system)) return { error: 'Ușile sunt invalide.' };
    if (ids.has(k)) out[k] = { ...(v.product ? { product: v.product } : {}), ...(v.handle ? { handle: v.handle } : {}), ...(v.system ? { system: v.system } : {}) }; }
  return { value: out };
}
/** Ce desenează motorul 3D: stilul foii, culoarea ei și culoarea mânerului, pe golul de ușă. */
export function doorVisual(snap: Snapshot, mc: MaterialsCatalog, level: number){
  return doorOpenings(snap).filter(d => d.level === level).flatMap(d => { const c = snap.doors?.[d.id], m = materialOf(mc, c?.product), h = materialOf(mc, c?.handle);
    return m || h ? [{ wallIndex: d.wallIndex, openingIndex: d.openingIndex, style: m?.specs?.door ?? 'plain', color: m?.specs?.color ?? null, wood: !!m?.specs?.wood, handle: h?.specs?.color ?? null }] : []; });
}
