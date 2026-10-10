import { createLayoutEngine } from './layout';
import { floorToPlan } from '../features/migration/legacy';
import { toEngineCatalog, resolve, groupOf, indexOf } from './catalog';
import { r3 } from './geometry';
import type { Catalog, Floor, FurniturePlacement, Snapshot } from './types';
import { getTemplate, localizeRoomNames } from './templates';

const uid = () => (globalThis.crypto as Crypto).randomUUID();

export function defaultSelections(cat: Catalog){ const s: Record<string, string> = {}; for (const v of cat.variants){ const g = groupOf(v.id); if (v.legacyIndex === 0) s[g] = v.id; } return s; }
export function blankFloor(w = 5, d = 4, h = 2.6): Floor {
  const walls: Floor['walls'] = [[[0, 0], [w, 0]], [[w, 0], [w, d]], [[w, d], [0, d]], [[0, d], [0, 0]]].map((ab, i) => ({ id: `wall-${i + 1}`, a: ab[0] as [number, number], b: ab[1] as [number, number], thickness: .25, exterior: true, openings: i === 3 ? [{ id: 'op-1', kind: 'door' as const, offset: d / 2 - .45, width: .9, entrance: true }] : i === 0 ? [{ id: 'op-2', kind: 'window' as const, offset: w / 2 - .7, width: 1.4 }] : [] }));
  return { id: 'floor-1', name: 'Etaj', ceilingHeight: h, rooms: [{ id: 'camera-1', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: w, z1: d } }], walls };
}
export function newSnapshot(cat: Catalog, name: string, template: string, lang: 'ro' | 'en' = 'ro'): Snapshot {
  const tpl = template === 'blank' ? undefined : getTemplate(template);
  if (template !== 'blank' && !tpl) throw new Error(`Șablon necunoscut: ${template}`);
  const floor = localizeRoomNames(tpl ? structuredClone(tpl.floor) : blankFloor(), lang);
  const snap: Snapshot = { name, floor, placements: [], selections: defaultSelections(cat), picked: [] };
  return autoLayout(snap, cat).snapshot;
}
// Rulează motorul extras din prototip. Nu modifică selecțiile utilizatorului: lucrează pe o copie și
// întoarce varianta efectiv folosită în fiecare plasare (repară efectul secundar din placeDining).
export function autoLayout(snap: Snapshot, cat: Catalog, opts: { roomId?: string } = {}){
  const plan = floorToPlan(snap.floor, snap.name), engineCat = toEngineCatalog(cat);
  const sel: Record<string, number> = {}; for (const [g, vid] of Object.entries(snap.selections)) sel[g] = indexOf(vid);
  const engine = createLayoutEngine({ plan, catalog: engineCat, selection: sel, picked: new Set(snap.picked) });
  const { placed, notFit } = engine.run();
  const fresh: FurniturePlacement[] = placed.filter(p => !opts.roomId || p.room === opts.roomId).map(p => {
    const x = p.free ? p.x! : (p.fp.x0 + p.fp.x1) / 2, z = p.free ? p.z! : (p.fp.z0 + p.fp.z1) / 2, rot = p.free ? (p.rot || 0) : p.fp.rot;
    return { id: uid(), roomId: p.room, group: p.key, variantId: `${p.key}-${sel[p.key] ?? 0}`, x: r3(x), z: r3(z), rotation: rot, source: 'auto' }; });
  const kept = opts.roomId ? snap.placements.filter(p => p.roomId !== opts.roomId) : [];
  return { snapshot: { ...snap, placements: [...kept, ...fresh] }, notFit: notFit.filter(n => !opts.roomId || snap.floor.rooms.find(r => r.id === opts.roomId)?.name === n.room) };
}
export function addPlacement(snap: Snapshot, cat: Catalog, roomId: string, variantId: string){
  // folosește findSpot din motor ca să găsească primul loc valid lângă un perete (sau în mijloc pentru piesele libere)
  const plan = floorToPlan(snap.floor, snap.name), engineCat = toEngineCatalog(cat), g = groupOf(variantId);
  const sel: Record<string, number> = {}; for (const [k, v] of Object.entries(snap.selections)) sel[k] = indexOf(v); sel[g] = indexOf(variantId);
  const engine = createLayoutEngine({ plan, catalog: engineCat, selection: sel });
  const room = plan.camere.find(r => r.id === roomId); if (!room) return null;
  const others = snap.placements.filter(p => p.roomId === roomId).map(p => { const rv = resolve(cat, p.variantId)!; return { id: p.id, key: p.group, room: p.roomId, fp: engine.fpFrom(p.x, p.z, p.rotation, rv.w, rv.d), front: null }; });
  const spot = engine.findSpot(room, g, others as any, { front: .6 });
  if (!spot) return null;
  const pl: FurniturePlacement = { id: uid(), roomId, group: g, variantId, x: r3((spot.fp.x0 + spot.fp.x1) / 2), z: r3((spot.fp.z0 + spot.fp.z1) / 2), rotation: spot.fp.rot, source: 'manual' };
  return { ...snap, placements: [...snap.placements, pl] };
}
