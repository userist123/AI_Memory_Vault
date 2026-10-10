// Intrarea motorului 3D, construită dintr-un snapshot: planul + piesele cu amprentă și varianta de catalog.
import type { Catalog, Snapshot } from '@/core/types';
import { floorToPlan } from '@/features/migration/legacy';
import { toEngineCatalog, groupOf, indexOf } from '@/core/catalog';
import { footprintOf } from '@/core/validate';

export function viewerInput(snap: Snapshot, catalog: Catalog, engineCat: ReturnType<typeof toEngineCatalog> = toEngineCatalog(catalog)){
  const items = snap.placements.map(p => { const g = engineCat[groupOf(p.variantId)], vv = g?.v[indexOf(p.variantId)];
    return { id: p.id, group: p.group, x: p.x, z: p.z, rotation: p.rotation, fp: footprintOf(catalog, p), variant: vv ? { ...vv, model: g.model } : null }; });
  return { plan: floorToPlan(snap.floor, snap.name), items };
}
