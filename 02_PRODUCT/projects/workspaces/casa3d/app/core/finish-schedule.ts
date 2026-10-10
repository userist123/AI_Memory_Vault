// Fișa de finisaje (finish schedule): ce primește clientul și echipa de execuție, cameră cu cameră —
// element, produs, furnizor, cod, culoare/model, dimensiuni, cantitate de comandat, preț, link și data verificării.
// Se construiește din același BOQ ca bugetul, deci cantitățile și prețurile sunt aceleași peste tot.
import type { Catalog, MaterialsCatalog, Snapshot, Confidence } from './types';
import { computeBOQ, finishesOf } from './boq';
import { floors } from './levels';
import { layoutOf, materialOf, pieceSizeCm, ceilingOf } from './finishes';

export type ScheduleElement = 'floor' | 'walltile' | 'wall' | 'paint' | 'baseboard' | 'ceiling' | 'led' | 'cornice' | 'spots' | 'light' | 'adhesive';
export interface ScheduleRow { level: number; roomId: string | null; room: string; element: ScheduleElement; side?: string; kind?: string;
  product: string; supplier: string; code: string | null; details: { key: string; vars?: Record<string, string | number> }[];
  netQty: number; unit: string; wastePct: number; orderedQty: number; packs: string | null; unitPrice: number | null; total: number | null;
  url: string | null; verifiedAt: string | null; confidence: Confidence }

const ELEMENT_ORDER: ScheduleElement[] = ['floor', 'walltile', 'wall', 'paint', 'baseboard', 'ceiling', 'cornice', 'led', 'spots', 'light', 'adhesive'];
/** Codul de produs al magazinului, din link: Dedeman …/p/4026660 (sau 1070874-1048524), IKEA …/p/virrmo-…-70430780/ → 70430780. */
export function productCode(url: string | null): string | null { const seg = url?.match(/\/p\/([\w-]+)\/?$/)?.[1]; if (!seg) return null;
  return /[a-z]/i.test(seg) ? seg.match(/-s?(\d{6,})$/)?.[1] ?? seg : seg; }
const elementOf = (key: string): ScheduleElement | null => { const k = key.split(':')[1] ?? key;
  return k === 'adhesive' || key === 'adhesive' ? 'adhesive' : (['floor', 'walltile', 'wall', 'paint', 'baseboard', 'ceiling', 'led', 'cornice', 'spots', 'light'] as const).find(e => e === k) ?? null; };

export function finishSchedule(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog): ScheduleRow[] {
  const b = computeBOQ(snap, cat, mc), fl = floors(snap), rooms = fl.flatMap((f, i) => f.rooms.map(r => ({ r, level: i })));
  const rows: ScheduleRow[] = [];
  for (const it of b.items){ if (it.category !== 'finishes' && it.category !== 'lighting') continue;
    const el = elementOf(it.key); if (!el) continue;
    const at = rooms.find(x => x.r.id === it.roomId), m = materialOf(mc, it.refId), details: ScheduleRow['details'] = [];
    let side: string | undefined, kind: string | undefined;
    if (at){ const f = finishesOf(snap, at.r);
      if (el === 'floor'){ const lay = layoutOf(f, m), [L, W] = pieceSizeCm(m);
        details.push({ key: `fin.pattern.${lay.pattern}` }, { key: m?.specs?.sizeCm ? 'sched.size' : 'sched.sizeTypical', vars: { l: L, w: W } });
        if (lay.angle === 90) details.push({ key: 'fin.dirAlongZ' });
        if (m?.category === 'floor_tile') details.push({ key: 'sched.grout', vars: { mm: lay.groutMm ?? 3, color: lay.groutColor ?? '#bdb8ae' } });
        if (m?.specs?.slip) details.push({ key: 'sched.slip', vars: { slip: m.specs.slip } }); if (m?.specs?.rectified) details.push({ key: 'fin.rectified' }); }
      if (el === 'wall'){ const [, , s, i] = it.key.split(':'), wf = f.wallFeatures?.[Number(i)]; side = s; kind = wf?.kind;
        if (wf){ details.push({ key: `fin.kind.${wf.kind}` }, { key: 'sched.wallSide', vars: { side: s ?? '' } }, wf.heightM ? { key: 'sched.upTo', vars: { h: wf.heightM } } : { key: 'fin.fullHeight' });
          if (wf.color) details.push({ key: 'sched.color', vars: { color: wf.color } }); } }
      if (el === 'walltile') details.push({ key: 'sched.size', vars: { l: pieceSizeCm(m)[0], w: pieceSizeCm(m)[1] } });
      if (el === 'ceiling'){ const c = ceilingOf(f); details.push({ key: `fin.ceiling.${c.type}` }, { key: 'sched.drop', vars: { cm: c.dropCm } }); if (c.type === 'cove') details.push({ key: 'sched.cove', vars: { cm: c.coveCm } }); }
    }
    if (m?.specs?.cctK) details.push({ key: 'sched.cct', vars: { k: m.specs.cctK } }); if (m?.specs?.ip) details.push({ key: 'sched.ip', vars: { ip: m.specs.ip } });
    if (it.note) details.push({ key: 'sched.note', vars: { note: it.note } });
    rows.push({ level: at?.level ?? 0, roomId: it.roomId, room: at?.r.name ?? '', element: el, ...(side ? { side } : {}), ...(kind ? { kind } : {}),
      product: m?.name ?? it.label, supplier: it.supplier, code: productCode(it.sourceUrl), details,
      netQty: it.netQty, unit: it.unit, wastePct: it.wastePct, orderedQty: it.orderedQty, packs: it.packs != null ? `${it.packs} × ${it.packLabel}` : null,
      unitPrice: it.unitPrice, total: it.total, url: it.sourceUrl, verifiedAt: it.verifiedAt, confidence: it.confidence }); }
  // pe niveluri, cameră cu cameră, în ordinea în care se execută lucrarea; materialele comune (adezivul) la final
  const roomIx = new Map(rooms.map((x, i) => [x.r.id, i]));
  return rows.sort((a, b) => (a.roomId == null ? 1 : 0) - (b.roomId == null ? 1 : 0) || (roomIx.get(a.roomId ?? '') ?? 0) - (roomIx.get(b.roomId ?? '') ?? 0) || ELEMENT_ORDER.indexOf(a.element) - ELEMENT_ORDER.indexOf(b.element));
}
