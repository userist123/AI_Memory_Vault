// Fișa de finisaje (finish schedule): ce primește clientul și echipa de execuție, cameră cu cameră —
// element, produs, furnizor, cod, culoare/model, dimensiuni, cantitate de comandat, preț, link și data verificării.
// Se construiește din același BOQ ca bugetul, deci cantitățile și prețurile sunt aceleași peste tot.
import type { Catalog, MaterialsCatalog, Snapshot, Confidence, RoomFinishes } from './types';
import { computeBOQ, finishesOf } from './boq';
import { floors } from './levels';
import { wallWorks } from './renovation';
import { layoutOf, materialOf, pieceSizeCm, ceilingOf, bandOf, alongOf, isPartial, sideSpan } from './finishes';

export type ScheduleElement = 'floor' | 'walltile' | 'wall' | 'paint' | 'baseboard' | 'ceiling' | 'led' | 'cornice' | 'spots' | 'light' | 'fixture' | 'countertop' | 'sink' | 'tap' | 'kitchen-led' | 'bath-tap' | 'shower-set' | 'towel-radiator' | 'led-mirror' | 'curtain' | 'sheer' | 'blind' | 'rug' | 'door' | 'door_handle' | 'door_system' | 'drywall' | 'adhesive';
export interface ScheduleRow { level: number; roomId: string | null; room: string; element: ScheduleElement; side?: string; kind?: string;
  product: string; supplier: string; code: string | null; details: { key: string; vars?: Record<string, string | number> }[];
  netQty: number; unit: string; wastePct: number; orderedQty: number; packs: string | null; unitPrice: number | null; total: number | null;
  url: string | null; verifiedAt: string | null; confidence: Confidence }

/** Categoriile de buget care intră în fișă (aceleași pe care le arată bugetul la finisaje). */
export const SCHEDULE_CATEGORIES = new Set(['finishes', 'lighting', 'textiles']);
const ELEMENT_ORDER: ScheduleElement[] = ['floor', 'walltile', 'wall', 'paint', 'baseboard', 'ceiling', 'cornice', 'led', 'spots', 'light', 'fixture', 'countertop', 'sink', 'tap', 'kitchen-led', 'bath-tap', 'shower-set', 'towel-radiator', 'led-mirror', 'curtain', 'sheer', 'blind', 'rug', 'door', 'door_handle', 'door_system', 'drywall', 'adhesive'];
/** Codul de produs al magazinului, din link: Dedeman …/p/4026660 (sau 1070874-1048524), IKEA …/p/virrmo-…-70430780/ → 70430780. */
const newWallOf = (snap: Snapshot, key: string) => { const wid = key.slice('wall-new:'.length), fls = floors(snap), level = fls.findIndex(f => f.walls.some(w => w.id === wid));
  const ww = level >= 0 ? wallWorks(fls[level]!).find(x => x.wallId === wid) : undefined; return ww ? { level, ww } : null; };
export function productCode(url: string | null): string | null { const seg = url?.match(/\/p\/([\w-]+)\/?$/)?.[1]; if (!seg) return null;
  return /[a-z]/i.test(seg) ? seg.match(/-s?(\d{6,})$/)?.[1] ?? seg : seg; }
// `<cameră>:zone:<id>` = zona de pardoseală a camerei (element pardoseală); `wall-new:<perete>` = placa peretelui nou din gips-carton
const elementOf = (key: string): ScheduleElement | null => { if (key.startsWith('wall-new:')) return 'drywall'; if (key.split(':')[1] === 'zone') return 'floor'; if (key.startsWith('door:')) return 'door'; if (key.startsWith('handle:')) return 'door_handle'; if (key.startsWith('doorsys:')) return 'door_system'; const k = key.split(':')[1] ?? key;
  return k === 'adhesive' || key === 'adhesive' ? 'adhesive' : (['floor', 'walltile', 'wall', 'paint', 'baseboard', 'ceiling', 'led', 'cornice', 'spots', 'light', 'fixture', 'countertop', 'sink', 'tap', 'kitchen-led', 'bath-tap', 'shower-set', 'towel-radiator', 'led-mirror', 'curtain', 'sheer', 'blind', 'rug'] as const).find(e => e === k) ?? null; };

export function finishSchedule(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog): ScheduleRow[] {
  const b = computeBOQ(snap, cat, mc), fl = floors(snap), rooms = fl.flatMap((f, i) => f.rooms.map(r => ({ r, level: i })));
  const rows: ScheduleRow[] = [];
  for (const it of b.items){ if (!SCHEDULE_CATEGORIES.has(it.category)) continue;
    const el = elementOf(it.key); if (!el) continue;
    const at = rooms.find(x => x.r.id === it.roomId), m = materialOf(mc, it.refId), details: ScheduleRow['details'] = [];
    let side: string | undefined, kind: string | undefined;
    if (at){ const f = finishesOf(snap, at.r);
      if (el === 'floor'){ const zid = it.key.split(':zone:')[1], zone = zid != null ? floors(snap)[at.level]!.zones?.find(z => z.id === zid) : undefined;
        // zona are materialul și așezarea ei, nu pe ale camerei
        const lay = layoutOf(zone ? { floorLayout: zone.layout } as RoomFinishes : f, m), [L, W] = pieceSizeCm(m);
        if (zone) details.push({ key: 'sched.zone', vars: { label: zone.label ? `${zone.label} ` : '', w: Math.round((zone.rect.x1 - zone.rect.x0) * 100) / 100, d: Math.round((zone.rect.z1 - zone.rect.z0) * 100) / 100 } });
        details.push({ key: `fin.pattern.${lay.pattern}` }, { key: m?.specs?.sizeCm ? 'sched.size' : 'sched.sizeTypical', vars: { l: L, w: W } });
        if (lay.angle === 90) details.push({ key: 'fin.dirAlongZ' });
        if (m?.category === 'floor_tile') details.push({ key: 'sched.grout', vars: { mm: lay.groutMm ?? 3, color: lay.groutColor ?? '#bdb8ae' } });
        if (m?.specs?.slip) details.push({ key: 'sched.slip', vars: { slip: m.specs.slip } }); if (m?.specs?.rectified) details.push({ key: 'fin.rectified' }); }
      if (el === 'wall'){ const [, , s, i] = it.key.split(':'), wf = f.wallFeatures?.[Number(i)]; side = s; kind = wf?.kind;
        if (wf){ const [a, b] = bandOf(wf, floors(snap)[at.level]!.ceilingHeight);
          details.push({ key: `fin.kind.${wf.kind}` }, { key: 'sched.wallSide', vars: { side: s ?? '' } }, wf.kind === 'rail' ? { key: 'sched.at', vars: { h: b } } : a > 0 ? { key: 'sched.band', vars: { a, b } } : wf.heightM ? { key: 'sched.upTo', vars: { h: wf.heightM } } : { key: 'fin.fullHeight' });
          if (isPartial(wf)){ const [u0, u1] = alongOf(wf, sideSpan(at.r, wf.side)); details.push({ key: 'sched.along', vars: { a: Math.round(u0 * 100) / 100, b: Math.round(u1 * 100) / 100 } }); }
          if (wf.color) details.push({ key: 'sched.color', vars: { color: wf.color } }); } }
      if (el === 'walltile') details.push({ key: 'sched.size', vars: { l: pieceSizeCm(m)[0], w: pieceSizeCm(m)[1] } });
      if (el === 'ceiling'){ const c = ceilingOf(f); details.push({ key: `fin.ceiling.${c.type}` }, { key: 'sched.drop', vars: { cm: c.dropCm } }); if (c.type === 'cove') details.push({ key: 'sched.cove', vars: { cm: c.coveCm } }); }
    }
    if ((el === 'curtain' || el === 'sheer' || el === 'blind' || el === 'rug' || el === 'door') && m?.specs?.sizeCm) details.push({ key: 'sched.size', vars: { l: m.specs.sizeCm[0], w: m.specs.sizeCm[1] } });
    // peretele nou din gips-carton: nivelul lui (rândul nu are cameră) și detaliile traduse, nu eticheta românească din deviz
    const nw = el === 'drywall' ? newWallOf(snap, it.key) : null;
    if (nw) details.push({ key: 'sched.newWall', vars: { t: Math.round(nw.ww.thicknessM * 100), l: nw.ww.lengthM, h: nw.ww.heightM, area: nw.ww.areaM2 } });
    if (it.label.includes(': ')) details.push({ key: 'sched.note', vars: { note: it.label.split(': ').slice(1).join(': ') } });
    if (m?.specs?.cctK) details.push({ key: 'sched.cct', vars: { k: m.specs.cctK } }); if (m?.specs?.ip) details.push({ key: 'sched.ip', vars: { ip: m.specs.ip } });
    if (it.note) details.push({ key: 'sched.note', vars: { note: it.note } });
    rows.push({ level: nw ? nw.level : at?.level ?? 0, roomId: it.roomId, room: at?.r.name ?? '', element: el, ...(side ? { side } : {}), ...(kind ? { kind } : {}),
      product: m?.name ?? it.label, supplier: it.supplier, code: productCode(it.sourceUrl), details,
      netQty: it.netQty, unit: it.unit, wastePct: it.wastePct, orderedQty: it.orderedQty, packs: it.packs != null ? `${it.packs} × ${it.packLabel}` : null,
      unitPrice: it.unitPrice, total: it.total, url: it.sourceUrl, verifiedAt: it.verifiedAt, confidence: it.confidence }); }
  // pe niveluri, cameră cu cameră, în ordinea în care se execută lucrarea; materialele comune (adezivul) la final
  const roomIx = new Map(rooms.map((x, i) => [x.r.id, i]));
  return rows.sort((a, b) => (a.roomId == null ? 1 : 0) - (b.roomId == null ? 1 : 0) || (roomIx.get(a.roomId ?? '') ?? 0) - (roomIx.get(b.roomId ?? '') ?? 0) || ELEMENT_ORDER.indexOf(a.element) - ELEMENT_ORDER.indexOf(b.element));
}
