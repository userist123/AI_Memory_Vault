// BOQ-aware evaluation of design alternatives: quantities and costs derived from the twin and the catalog.
// Unknown prices stay UNKNOWN and are counted, never estimated (Constitution art. 8, 9).
import { type Twin, roomEdges } from './twin.js';
import type { Catalog, Money, Provenance } from './catalog.js';
import type { Alternative } from './solver.js';
import { polygonArea, segmentLength } from './geometry.js';

export interface LineItem {
  placementId: string;
  catalogId: string;
  name: string;
  quantity: 1;
  price: Money;
  retailer?: string;
  provenance?: Provenance;
}

export interface RoomQuantities {
  roomId: string;
  floorArea: number;      // m²
  perimeter: number;      // m
  wallArea: number;       // m² gross (perimeter × height), openings not subtracted here
  ceilingArea: number;    // m²
}

export interface BoqEvaluation {
  currency: 'RON';
  items: LineItem[];
  knownTotal: number;        // sum of known RON prices
  unknownCount: number;      // items whose price is UNKNOWN
  unknownIds: string[];
  quantities: RoomQuantities[];
  /** Target budget comparison; absent when no target was given. */
  budget?: { target: number; delta: number; status: 'UNDER' | 'OVER' | 'UNKNOWN' };
  retailers: string[];
}

const r2 = (v: number): number => Math.round(v * 100) / 100;

export function evaluateBoq(twin: Twin, catalog: Catalog, targetBudget?: number): BoqEvaluation {
  const items: LineItem[] = [];
  const unknownIds: string[] = [];
  let knownTotal = 0;
  for (const p of [...twin.placements].sort((a, b) => (a.id < b.id ? -1 : 1))) {
    const item = catalog.get(p.catalogId);
    const price: Money = item ? priceInRon(item.price) : 'UNKNOWN';
    const line: LineItem = { placementId: p.id, catalogId: p.catalogId, name: item?.name ?? p.catalogId, quantity: 1, price };
    if (item?.retailer) line.retailer = item.retailer;
    if (item?.provenance) line.provenance = item.provenance;
    items.push(line);
    if (price === 'UNKNOWN') unknownIds.push(p.id); else knownTotal += price.amount;
  }
  const quantities: RoomQuantities[] = twin.rooms.map(r => {
    const perimeter = roomEdges(r).reduce((s, e) => s + segmentLength(e), 0);
    const floor = polygonArea(r.polygon);
    return { roomId: r.id, floorArea: r2(floor), perimeter: r2(perimeter), wallArea: r2(perimeter * r.height), ceilingArea: r2(floor) };
  });
  const out: BoqEvaluation = {
    currency: 'RON', items, knownTotal: r2(knownTotal), unknownCount: unknownIds.length, unknownIds, quantities,
    retailers: Array.from(new Set(items.map(i => i.retailer).filter((x): x is string => !!x))).sort(),
  };
  if (targetBudget !== undefined) {
    out.budget = unknownIds.length > 0
      ? { target: targetBudget, delta: r2(targetBudget - knownTotal), status: 'UNKNOWN' }
      : { target: targetBudget, delta: r2(targetBudget - knownTotal), status: knownTotal <= targetBudget ? 'UNDER' : 'OVER' };
  }
  return out;
}

/** Only RON is summed; other currencies stay UNKNOWN here rather than converted with an invented rate. */
function priceInRon(m: Money): Money {
  if (m === 'UNKNOWN') return 'UNKNOWN';
  return m.currency === 'RON' ? m : 'UNKNOWN';
}

export interface EvaluatedAlternative extends Alternative { boq: BoqEvaluation }

export function evaluateAlternatives(alternatives: readonly Alternative[], catalog: Catalog, targetBudget?: number): EvaluatedAlternative[] {
  return alternatives.map(a => ({ ...a, boq: evaluateBoq(a.result.twin, catalog, targetBudget) }));
}
