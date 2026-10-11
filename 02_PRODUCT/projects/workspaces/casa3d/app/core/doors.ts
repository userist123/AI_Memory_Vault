// Uși de interior: produsul ales pe fiecare gol de ușă (foaie + toc) și mânerul; mărimea comparată cu golul.
import type { Floor, MaterialsCatalog, Snapshot } from './types';
import { floors } from './levels';
import { materialOf } from './finishes';

export const DOOR_RULES = { sizeTolM: .1 };
export interface DoorOpening { id: string; level: number; widthM: number; entrance: boolean; wallIndex: number; openingIndex: number; roomIds: string[] }
export function doorOpenings(snap: Snapshot): DoorOpening[] {
  // camerele de o parte și de alta a ușii: cele al căror dreptunghi atinge mijlocul golului
  return floors(snap).flatMap((fl: Floor, level) => fl.walls.flatMap((w, wi) => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    return w.openings.flatMap((o, oi) => { if (o.kind !== 'door') return []; const m = o.offset + o.width / 2, x = w.a[0] + ux * m, z = w.a[1] + uz * m, e = .01;
      const roomIds = fl.rooms.filter(r => x >= r.rect.x0 - e && x <= r.rect.x1 + e && z >= r.rect.z0 - e && z <= r.rect.z1 + e).map(r => r.id);
      return [{ id: o.id, level, widthM: o.width, entrance: !!o.entrance, wallIndex: wi, openingIndex: oi, roomIds }]; }); }));
}
export interface DoorIssue { key: string; openingId: string; vars?: Record<string, string | number> }
export function doorIssues(snap: Snapshot, mc: MaterialsCatalog): DoorIssue[] {
  const out: DoorIssue[] = [];
  for (const d of doorOpenings(snap)){ const c = snap.doors?.[d.id], m = materialOf(mc, c?.product); if (!m) continue;
    if (d.entrance) out.push({ key: 'door.entrance', openingId: d.id, vars: { name: m.name } });
    const w = (m.specs?.sizeCm?.[0] ?? 0) / 100; if (w && Math.abs(w - d.widthM) > DOOR_RULES.sizeTolM) out.push({ key: 'door.size', openingId: d.id, vars: { door: Math.round(w * 100), gap: Math.round(d.widthM * 100) } });
    if (!c?.handle) out.push({ key: 'door.noHandle', openingId: d.id }); }
  return out;
}
/** Păstrează doar golurile de ușă existente și id-uri de produs scurte; altceva → mesaj de eroare (400). */
export function sanitizeDoors(doors: unknown, snap: Snapshot): { value?: Record<string, { product?: string | null; handle?: string | null }>; error?: string } {
  if (doors == null) return {}; if (typeof doors !== 'object' || Array.isArray(doors) || Object.keys(doors).length > 200) return { error: 'Ușile sunt invalide.' };
  const ids = new Set(doorOpenings(snap).map(d => d.id)), id = (x: unknown) => x == null || (typeof x === 'string' && x.length <= 80), out: Record<string, { product?: string | null; handle?: string | null }> = {};
  for (const [k, v] of Object.entries(doors as Record<string, any>)){ if (!v || typeof v !== 'object' || !id(v.product) || !id(v.handle)) return { error: 'Ușile sunt invalide.' };
    if (ids.has(k)) out[k] = { ...(v.product ? { product: v.product } : {}), ...(v.handle ? { handle: v.handle } : {}) }; }
  return { value: out };
}
/** Ce desenează motorul 3D: stilul foii, culoarea ei și culoarea mânerului, pe golul de ușă. */
export function doorVisual(snap: Snapshot, mc: MaterialsCatalog, level: number){
  return doorOpenings(snap).filter(d => d.level === level).flatMap(d => { const c = snap.doors?.[d.id], m = materialOf(mc, c?.product), h = materialOf(mc, c?.handle);
    return m || h ? [{ wallIndex: d.wallIndex, openingIndex: d.openingIndex, style: m?.specs?.door ?? 'plain', color: m?.specs?.color ?? null, wood: !!m?.specs?.wood, handle: h?.specs?.color ?? null }] : []; });
}

// ---------- sensul de deschidere ----------
export interface DoorSwing { hinge: 'start' | 'end'; swing: 1 | -1; auto: boolean }
/** Cum se deschide o ușă: ce aleg utilizatorul, altfel regula de practică — ușa de intrare se deschide spre interior,
 *  ușile de cameră se deschid în cameră (nu în hol), iar balamaua stă spre colțul cel mai apropiat, ca foaia să se așeze lângă perete. */
export function doorSwing(fl: Floor, wallIndex: number, openingIndex: number): DoorSwing {
  const w = fl.walls[wallIndex]!, o = w.openings[openingIndex]!, L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, nx = -uz, nz = ux;
  const m = o.offset + o.width / 2, mx = w.a[0] + ux * m, mz = w.a[1] + uz * m, probe = (sg: number) => { const x = mx + nx * sg * .3, z = mz + nz * sg * .3;
    return fl.rooms.find(r => x > r.rect.x0 && x < r.rect.x1 && z > r.rect.z0 && z < r.rect.z1) ?? null; };
  const A = probe(1), B = probe(-1);
  let swing: 1 | -1;
  if (o.entrance || !A || !B) swing = A ? 1 : -1;                       // intrarea (sau un perete exterior): spre interior
  else if (A.type === 'hol' && B.type !== 'hol') swing = -1;             // din hol spre cameră
  else if (B.type === 'hol' && A.type !== 'hol') swing = 1;
  else { const ar = (r: typeof A) => (r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0); swing = ar(A) >= ar(B) ? 1 : -1; }  // între două camere: spre cea mai mare
  // balamaua: capătul golului mai aproape de colțul camerei în care se deschide (foaia se sprijină de perete)
  const room = swing === 1 ? A : B; let hinge: 'start' | 'end' = 'start';
  if (room){ const lo = Math.abs(ux) > .5 ? room.rect.x0 : room.rect.z0, hi = Math.abs(ux) > .5 ? room.rect.x1 : room.rect.z1, along = (t: number) => Math.abs(ux) > .5 ? w.a[0] + ux * t : w.a[1] + uz * t;
    const p0 = along(o.offset), p1 = along(o.offset + o.width), d0 = Math.min(Math.abs(p0 - lo), Math.abs(p0 - hi)), d1 = Math.min(Math.abs(p1 - lo), Math.abs(p1 - hi)); hinge = d1 < d0 ? 'end' : 'start'; }
  return { hinge: o.hinge ?? hinge, swing: o.swing ?? swing, auto: o.hinge == null && o.swing == null };
}
