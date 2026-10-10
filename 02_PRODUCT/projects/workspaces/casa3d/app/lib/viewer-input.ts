// Intrarea motorului 3D, construită dintr-un snapshot: planul + piesele cu amprentă și varianta de catalog.
import type { Catalog, Snapshot, MaterialsCatalog } from '@/core/types';
import { roomVisual } from '@/core/finishes';
import { textileVisual } from '@/core/textiles';
import { doorVisual } from '@/core/doors';
import { finishesOf } from '@/core/boq';
import { planWithLook } from '@/lib/plan-look';
import { itemStyle, itemSizeCm } from '@/core/appearance';
import { toEngineCatalog, groupOf, indexOf } from '@/core/catalog';
import { footprintOf } from '@/core/validate';
import { stairGeometry, SLAB } from '@/core/levels';

/** Gol în placa nivelului arătat (golul scării de dedesubt); `dir` = sensul urcării scării, marginea de sosire e pe partea +dir. */
export interface PlateVoid { x0: number; z0: number; x1: number; z1: number; dir: [number, number] }

export function viewerInput(snap: Snapshot, catalog: Catalog, engineCat: ReturnType<typeof toEngineCatalog> = toEngineCatalog(catalog), extra: { voids?: PlateVoid[]; mc?: MaterialsCatalog | null } = {}){
  const items = snap.placements.map(p => { const g = engineCat[groupOf(p.variantId)], vv = g?.v[indexOf(p.variantId)];
    // culoarea/materialul aleși și dimensiunile pe comandă înlocuiesc stilul și mărimea variantei
    const size = p.size ? itemSizeCm(catalog, p) : null, variant = vv ? { ...vv, model: g.model, s: itemStyle(snap, p, vv.s, g.model ?? ''), ...(size ? { w: size.w, d: size.d, h: size.h } : {}) } : null;
    return { id: p.id, group: p.group, x: p.x, z: p.z, rotation: p.rotation, fp: footprintOf(catalog, p), variant }; });
  const rise = snap.floor.ceilingHeight + SLAB;
  const scari = (snap.floor.stairs ?? []).map(st => ({ id: st.id, x: st.x, z: st.z, w: st.width, l: st.length, rot: st.rotation, ...stairGeometry(st, rise) }));
  const plan = planWithLook(snap), mc = extra.mc;
  // finisajele de designer ale fiecărei camere (modul de așezare, placări, tavan), doar când avem catalogul de materiale
  if (mc) plan.camere.forEach((c: any) => { const r = snap.floor.rooms.find(x => x.id === c.id); if (!r) return; const f = finishesOf(snap, r), tv = textileVisual(mc, snap.floor, r, f);
    c.fin = { ...roomVisual(mc, f, r.type), rug: tv.rug };
    // textilele de la ferestre stau pe golul lor, pe partea dinspre cameră
    for (const w of tv.windows){ const g = plan.pereti[w.wallIndex]?.goluri[w.openingIndex]; if (g) g.trat = { inward: w.inward, rodH: w.rodH, curtain: w.curtain, sheer: w.sheer, blind: w.blind }; } });
  if (mc) for (const d of doorVisual(snap, mc, 0)){ const g = plan.pereti[d.wallIndex]?.goluri[d.openingIndex]; if (g) g.usa = { style: d.style, color: d.color, wood: d.wood, handle: d.handle }; }
  return { plan: { ...plan, scari, goluriPlaca: extra.voids ?? [] }, items };
}
