// Intrarea motorului 3D, construită dintr-un snapshot: planul + piesele cu amprentă și varianta de catalog.
import type { Catalog, Snapshot } from '@/core/types';
import { planWithLook } from '@/lib/plan-look';
import { itemStyle, itemSizeCm } from '@/core/appearance';
import { toEngineCatalog, groupOf, indexOf } from '@/core/catalog';
import { footprintOf } from '@/core/validate';

export function viewerInput(snap: Snapshot, catalog: Catalog, engineCat: ReturnType<typeof toEngineCatalog> = toEngineCatalog(catalog)){
  const items = snap.placements.map(p => { const g = engineCat[groupOf(p.variantId)], vv = g?.v[indexOf(p.variantId)];
    // culoarea/materialul aleși și dimensiunile pe comandă înlocuiesc stilul și mărimea variantei
    const size = p.size ? itemSizeCm(catalog, p) : null, variant = vv ? { ...vv, model: g.model, s: itemStyle(snap, p, vv.s, g.model ?? ''), ...(size ? { w: size.w, d: size.d, h: size.h } : {}) } : null;
    return { id: p.id, group: p.group, x: p.x, z: p.z, rotation: p.rotation, fp: footprintOf(catalog, p), variant }; });
  return { plan: planWithLook(snap), items };
}
