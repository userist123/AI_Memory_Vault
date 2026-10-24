// Harta de impact a modificărilor de design (Live Design Impact Map)
// Calculează determinist efectele în aval: suprafețe modificate, mobilier deplasat, planșe afectate și delta de buget.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { area } from './geometry';
import { resolve } from './catalog';
import { computeBudget } from './boq';

export type DrawingType = 'floor_plan' | 'finishes' | 'ceiling_lighting' | 'wall_elevations';

export interface RoomResizeImpact {
  id: string;
  name: string;
  beforeArea: number;
  afterArea: number;
  deltaArea: number;
}

export interface DesignImpact {
  hasChanges: boolean;
  geometry: {
    roomsResized: RoomResizeImpact[];
    roomsAdded: string[];
    roomsRemoved: string[];
    wallsChanged: number;
    openingsChanged: number;
  };
  furniture: {
    added: { id: string; name: string; room: string }[];
    removed: { id: string; name: string; room: string }[];
    moved: { id: string; name: string; distM: number }[];
    resized: { id: string; name: string }[];
  };
  technical: {
    pointsDisplacedOrOrphaned: number;
  };
  budget: {
    deltaKnown: number;
    newUnknownItems: string[];
  };
  drawingsStale: DrawingType[];
  riskLevel: 'LOW' | 'MEDIUM' | 'HIGH';
  summary: { ro: string; en: string };
}

export function computeDesignImpact(
  before: Snapshot,
  after: Snapshot,
  cat: Catalog,
  mc: MaterialsCatalog
): DesignImpact {
  const roomsB = before.floor.rooms ?? [];
  const roomsA = after.floor.rooms ?? [];
  const rMapB = new Map(roomsB.map(r => [r.id, r]));
  const rMapA = new Map(roomsA.map(r => [r.id, r]));

  const roomsResized: RoomResizeImpact[] = [];
  const roomsAdded: string[] = [];
  const roomsRemoved: string[] = [];

  for (const ra of roomsA) {
    const rb = rMapB.get(ra.id);
    if (!rb) {
      roomsAdded.push(ra.name);
    } else {
      const aB = Math.round(area(rb.rect) * 100) / 100;
      const aA = Math.round(area(ra.rect) * 100) / 100;
      if (Math.abs(aA - aB) > 0.05) {
        roomsResized.push({
          id: ra.id,
          name: ra.name,
          beforeArea: aB,
          afterArea: aA,
          deltaArea: Math.round((aA - aB) * 100) / 100,
        });
      }
    }
  }

  for (const rb of roomsB) {
    if (!rMapA.has(rb.id)) roomsRemoved.push(rb.name);
  }

  // Pereți
  const wallsB = before.floor.walls ?? [];
  const wallsA = after.floor.walls ?? [];
  const wallsChanged = Math.abs(wallsA.length - wallsB.length);

  // Mobilier
  const pMapB = new Map(before.placements.map(p => [p.id, p]));
  const pMapA = new Map(after.placements.map(p => [p.id, p]));

  const fAdded: { id: string; name: string; room: string }[] = [];
  const fRemoved: { id: string; name: string; room: string }[] = [];
  const fMoved: { id: string; name: string; distM: number }[] = [];
  const fResized: { id: string; name: string }[] = [];

  for (const pa of after.placements) {
    const pb = pMapB.get(pa.id);
    const rName = rMapA.get(pa.roomId)?.name || 'Cameră';
    const pName = resolve(cat, pa.variantId)?.product.name || pa.group;
    if (!pb) {
      fAdded.push({ id: pa.id, name: pName, room: rName });
    } else {
      const d = Math.hypot(pa.x - pb.x, pa.z - pb.z);
      if (d > 0.05) {
        fMoved.push({ id: pa.id, name: pName, distM: Math.round(d * 100) / 100 });
      }
      if (JSON.stringify(pa.size) !== JSON.stringify(pb.size)) {
        fResized.push({ id: pa.id, name: pName });
      }
    }
  }

  for (const pb of before.placements) {
    if (!pMapA.has(pb.id)) {
      const rName = rMapB.get(pb.roomId)?.name || 'Cameră';
      const pName = resolve(cat, pb.variantId)?.product.name || pb.group;
      fRemoved.push({ id: pb.id, name: pName, room: rName });
    }
  }

  // Planșe afectate
  const drawingsStale: DrawingType[] = [];
  if (roomsResized.length > 0 || roomsAdded.length > 0 || roomsRemoved.length > 0 || wallsChanged > 0) {
    drawingsStale.push('floor_plan', 'finishes', 'ceiling_lighting', 'wall_elevations');
  } else if (fAdded.length > 0 || fRemoved.length > 0 || fMoved.length > 0) {
    drawingsStale.push('floor_plan');
  }

  // Buget
  let deltaKnown = 0;
  const newUnknownItems: string[] = [];
  try {
    const bB = computeBudget(before, cat, mc);
    const bA = computeBudget(after, cat, mc);
    deltaKnown = Math.round(bA.chosen.total - bB.chosen.total);
    for (const u of bA.unknownItems) {
      if (!bB.unknownItems.includes(u)) newUnknownItems.push(u);
    }
  } catch {
    // fallback safe
  }

  const hasChanges =
    roomsResized.length > 0 ||
    roomsAdded.length > 0 ||
    roomsRemoved.length > 0 ||
    wallsChanged > 0 ||
    fAdded.length > 0 ||
    fRemoved.length > 0 ||
    fMoved.length > 0 ||
    fResized.length > 0 ||
    deltaKnown !== 0;

  let riskLevel: 'LOW' | 'MEDIUM' | 'HIGH' = 'LOW';
  if (roomsRemoved.length > 0 || Math.abs(deltaKnown) > 2500 || roomsResized.some(r => Math.abs(r.deltaArea) > 3)) {
    riskLevel = 'HIGH';
  } else if (roomsResized.length > 0 || fRemoved.length > 0 || Math.abs(deltaKnown) > 500) {
    riskLevel = 'MEDIUM';
  }

  const summary = {
    ro: !hasChanges
      ? 'Fără modificări în modelul geometric sau deviz.'
      : `${roomsResized.length} camere redimensionate, ${fAdded.length + fRemoved.length + fMoved.length} piese de mobilier afectate, delta buget: ${deltaKnown >= 0 ? '+' : ''}${deltaKnown} RON.`,
    en: !hasChanges
      ? 'No changes in geometric model or budget.'
      : `${roomsResized.length} rooms resized, ${fAdded.length + fRemoved.length + fMoved.length} furniture items affected, budget delta: ${deltaKnown >= 0 ? '+' : ''}${deltaKnown} RON.`,
  };

  return {
    hasChanges,
    geometry: {
      roomsResized,
      roomsAdded,
      roomsRemoved,
      wallsChanged,
      openingsChanged: 0,
    },
    furniture: {
      added: fAdded,
      removed: fRemoved,
      moved: fMoved,
      resized: fResized,
    },
    technical: {
      pointsDisplacedOrOrphaned: 0,
    },
    budget: {
      deltaKnown,
      newUnknownItems,
    },
    drawingsStale,
    riskLevel,
    summary,
  };
}
