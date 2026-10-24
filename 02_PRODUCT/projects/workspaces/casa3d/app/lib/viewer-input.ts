// Intrarea motorului 3D, construită dintr-un snapshot: planul + piesele cu amprentă și varianta de catalog.
import type { Catalog, Snapshot, MaterialsCatalog } from '@/core/types';
import { roomVisual } from '@/core/finishes';
import { textileVisual } from '@/core/textiles';
import { doorVisual, doorSwing } from '@/core/doors';
import { kitchenVisual } from '@/core/kitchen';
import { bathVisual } from '@/core/bath';
import { allFixturePoints } from '@/core/fixtures';
import { materialOf } from '@/core/finishes';
import { finishesOf } from '@/core/boq';
import { planWithLook } from '@/lib/plan-look';
import { itemStyle, itemSizeCm } from '@/core/appearance';
import { toEngineCatalog, groupOf, indexOf } from '@/core/catalog';
import { footprintOf } from '@/core/validate';
import { stairGeometry, SLAB } from '@/core/levels';
import { wallItemGeometry, wallItemFace, wallItemDepth } from '@/core/wall-items';
import { bandOf, alongOf, bathTiles, sideSpan } from '@/core/finishes';
import { techFrame, isSizedRadiator, radiatorSize, hvacSize } from '@/core/tech-geometry';
import { intersect } from '@/core/floor-zones';
import type { RoomFinishes } from '@/core/types';

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
    c.fin = { ...roomVisual(mc, f, r.type, r), rug: tv.rug };
    // textilele de la ferestre stau pe golul lor, pe partea dinspre cameră
    for (const w of tv.windows){ const g = plan.pereti[w.wallIndex]?.goluri[w.openingIndex]; if (g) g.trat = { inward: w.inward, rodH: w.rodH, curtain: w.curtain, sheer: w.sheer, blind: w.blind }; } });
  // bucătăria ca sistem: fronturile, blatul, placarea și LED-ul ajung în stilul piesei de bucătărie
  if (mc) for (const it of items) if (it.group === 'bucatarie' && it.variant){ const r = snap.floor.rooms.find(x => x.id === snap.placements.find(p => p.id === it.id)?.roomId);
    const kv = r ? kitchenVisual(mc, finishesOf(snap, r)) : null; if (kv) it.variant = { ...it.variant, s: { ...(it.variant.s || {}), k: kv } }; }
  if (mc) for (const it of items) if ((it.group === 'dus' || it.group === 'lavoar' || it.group === 'wc') && it.variant){ const r = snap.floor.rooms.find(x => x.id === snap.placements.find(p => p.id === it.id)?.roomId);
    const bv = r ? bathVisual(finishesOf(snap, r)) : null; if (bv) it.variant = { ...it.variant, s: { ...(it.variant.s || {}), b: bv } }; }
  if (mc) for (const d of doorVisual(snap, mc, 0)){ const g = plan.pereti[d.wallIndex]?.goluri[d.openingIndex]; if (g) g.usa = { style: d.style, color: d.color, wood: d.wood, handle: d.handle }; }
  // sensul de deschidere al fiecărei uși (balamaua și partea), același ca în plan
  snap.floor.walls.forEach((w, wi) => w.openings.forEach((o, oi) => { if (o.kind !== 'door') return; const g = plan.pereti[wi]?.goluri[oi]; if (g) g.sw = doorSwing(snap.floor, wi, oi); }));
  // corpurile de iluminat plasate (culoarea: aleasă, a produsului, sau negru)
  const lumini = allFixturePoints(snap, catalog, snap.floor.rooms, finishesOf).map(f => ({ kind: f.kind, x: f.x, z: f.z, y: f.y, nx: f.nx, nz: f.nz, len: f.len, alongX: f.alongX, top: f.top, color: f.color ?? (mc ? materialOf(mc, f.material)?.specs?.color : undefined) ?? '#1c1c1c' }));
  const stalpi = snap.floor.columns ?? [];
  const grinzi = snap.floor.beams ?? [];
  const peretiSticla = snap.floor.glassWalls ?? [];
  // nișele: peretele (index în plan.pereti), poziția pe el, cota, mărimea și fața (motorul o desenează pe fața peretelui)
  const nise = (snap.floor.niches ?? []).flatMap(n => { const wi = snap.floor.walls.findIndex(w => w.id === n.wallId); return wi < 0 ? [] : [{ id: n.id, wi, la: n.offset, l: n.width, y: n.elevation, h: n.height, ad: n.depth, side: n.side ?? 1 }]; });
  // obiectele de pe perete: peretele (index), centrul pe el și cota, mărimea, fața; camera și latura (pentru decorul automat) și
  // grosimea placării din spatele lor (riflaj, piatră…), ca obiectul să stea în fața ei
  const CLAD: Record<string, number> = { stone: .022, brick: .014, slats: .021, panel: .012, tile: .01 };
  const obiectePerete = (snap.floor.wallItems ?? []).flatMap(it => { const g = wallItemGeometry(snap.floor, it); if (!g) return []; const face = wallItemFace(snap.floor, it);
    let clad = 0; if (face){ const f = finishesOf(snap, face.room), len = sideSpan(face.room, face.side);
      for (const w of [...(f.wallFeatures || []), ...bathTiles(f, face.room.type, face.room)]){ if (w.side !== face.side || !(w.kind in CLAD)) continue; const [y0, y1] = bandOf(w, snap.floor.ceilingHeight), [a, b] = alongOf(w, len);
        if (face.u > a && face.u < b && it.elevation > y0 && it.elevation < y1) clad = Math.max(clad, CLAD[w.kind]!); } }
    return [{ id: it.id, wi: g.wallIndex, la: it.offset, l: it.width, y: it.elevation, h: it.height, ad: wallItemDepth(it), side: it.side === -1 ? -1 : 1, kind: it.kind, color: it.color ?? null, room: face?.room.id ?? null, fata: face?.side ?? null, clad }]; });
  // punctele tehnice cu corp: unitatea de aer condiționat (cutie albă pe perete) și caloriferul nou (cu nervuri), pe fața peretelui
  // din dreptul lor; grosimea e cea a peretelui din 3D (motorul desenează pereții de 25 / 15 cm)
  const ceil = snap.floor.ceilingHeight;
  const tehnice = (snap.tech ?? []).flatMap(t => { const ac = t.kind === 'hvac_split'; if (!ac && !isSizedRadiator(t)) return [];
    const f = techFrame(snap.floor, t); if (!f) return []; const th = f.wall?.exterior ? .25 : .15, k = th / 2 - f.thickness / 2, s = ac ? hvacSize(t) : radiatorSize(t);
    const y = Math.max(0, Math.min(ceil - s.h - .02, t.height));
    return [{ id: t.id, kind: ac ? 'ac' : 'radiator', model: t.model ?? 'panel', x: f.face[0] + f.n[0] * k, z: f.face[1] + f.n[1] * k, nx: f.n[0], nz: f.n[1], ux: f.u[0], uz: f.u[1], w: s.w, h: s.h, d: s.d, y }]; });
  // zonele de pardoseală: dreptunghiul decupat la cameră și pardoseala aleasă (modul de așezare, placa); motorul le pune la +1 mm
  const zonePardoseala = (snap.floor.zones ?? []).flatMap(z => { const room = snap.floor.rooms.find(r => r.id === z.roomId), c = room && intersect(room.rect, z.rect); if (!c) return [];
    const fin = mc ? roomVisual(mc, { floor: z.floor, wallPaint: '', light: '', ...(z.layout ? { floorLayout: z.layout } : {}) } as RoomFinishes).floor : null;
    return [{ id: z.id, room: z.roomId, ...c, fin }]; });
  return { plan: { ...plan, scari, lumini, stalpi, grinzi, peretiSticla, nise, obiectePerete, ...(tehnice.length ? { tehnice } : {}), ...(zonePardoseala.length ? { zonePardoseala } : {}), goluriPlaca: extra.voids ?? [] }, items };
}
