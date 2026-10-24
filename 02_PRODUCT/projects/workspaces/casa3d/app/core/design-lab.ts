// Laborator de variante de design: comparare comparativă a opțiunilor de amenajare (A / B / C)
// Calcul determinist al compromisurilor (trade-offs): spațiu liber, depozitare, locuri de ședere, buget și erori.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { resolve } from './catalog';
import { area } from './geometry';
import { footprintOf, validateFloor, validatePlacement } from './validate';
import { computeBudget } from './boq';

export interface SpatialMetrics {
  storageMeters: number; // metri liniari de depozitare (dulapuri, comode, etc.)
  freeSpacePct: number;  // procent spațiu liber de circulație (aria camerelor - amprenta mobilierului)
  seatingCapacity: number; // total locuri de ședere (dining, canapea, fotolii)
  itemCount: number;
  budgetKnown: number;
  budgetDeltaKnown?: number;
  unknownPricesCount: number;
  errorsCount: number;
  warningsCount: number;
}

export interface DesignOptionEvaluation {
  id: string;
  title: string;
  snapshot: Snapshot;
  metrics: SpatialMetrics;
  pros: { ro: string; en: string }[];
  cons: { ro: string; en: string }[];
}

export interface DesignComparison {
  base: DesignOptionEvaluation;
  options: DesignOptionEvaluation[];
}

export function evaluateDesignOption(
  title: string,
  snap: Snapshot,
  cat: Catalog,
  mc: MaterialsCatalog,
  id: string = 'base'
): DesignOptionEvaluation {
  const rooms = snap.floor.rooms ?? [];
  const placements = snap.placements ?? [];

  // 1. Aria totală a încăperilor
  const totalFloorArea = rooms.reduce((acc, r) => acc + area(r.rect), 0);

  // 2. Aria ocupată de mobilier și calcul depozitare / locuri
  let occupiedArea = 0;
  let storageWidthM = 0;
  let seating = 0;

  for (const p of placements) {
    const res = resolve(cat, p.variantId);
    const fp = footprintOf(cat, p);
    if (fp) {
      occupiedArea += area(fp);
    }

    const w = p.size?.w ? p.size.w / 100 : res?.w || 0.6;
    if (['dulap', 'comoda', 'pantofar', 'biblioteca'].includes(p.group)) {
      storageWidthM += w;
    } else if (p.group === 'bucatarie') {
      storageWidthM += w * 0.8;
    }

    if (p.group === 'scaunDining' || p.group === 'scaunBirou') {
      seating += 1;
    } else if (p.group === 'fotoliu') {
      seating += 1;
    } else if (p.group === 'canapea') {
      seating += Math.max(2, Math.round(w / 0.75));
    }
  }

  const freeSpacePct =
    totalFloorArea > 0
      ? Math.max(0, Math.min(100, Math.round(((totalFloorArea - occupiedArea) / totalFloorArea) * 100)))
      : 80;

  // 3. Validare erori / avertismente
  let errCount = 0;
  let warnCount = 0;
  const floorIssues = validateFloor(snap.floor);
  for (const fi of floorIssues) {
    if (fi.severity === 'ERROR') errCount++;
    else if (fi.severity === 'WARNING') warnCount++;
  }
  for (const p of placements) {
    const pIssues = validatePlacement(snap, cat, p);
    for (const pi of pIssues) {
      if (pi.severity === 'ERROR') errCount++;
      else if (pi.severity === 'WARNING') warnCount++;
    }
  }

  // 4. Buget
  let budgetKnown = 0;
  let unknownPricesCount = 0;
  try {
    const b = computeBudget(snap, cat, mc);
    budgetKnown = Math.round(b.chosen.total);
    unknownPricesCount = b.unknownItems.length + b.unknownLines.length;
  } catch {
    // fallback safe
  }

  const pros: { ro: string; en: string }[] = [];
  const cons: { ro: string; en: string }[] = [];

  if (storageWidthM >= 4.0) {
    pros.push({
      ro: `Capacitate excelentă de depozitare: ${storageWidthM.toFixed(1)} ml mobilier dedicat.`,
      en: `Excellent storage capacity: ${storageWidthM.toFixed(1)} linear meters of dedicated cabinetry.`,
    });
  } else if (storageWidthM < 2.0) {
    cons.push({
      ro: `Depozitare restrânsă (${storageWidthM.toFixed(1)} ml). Recomandată suplimentare pentru haine.`,
      en: `Limited storage (${storageWidthM.toFixed(1)} lm). Additional wardrobe recommended.`,
    });
  }

  if (freeSpacePct >= 70) {
    pros.push({
      ro: `Circulație fluidă și aerisită: ${freeSpacePct}% din pardoseală liberă de obstacole.`,
      en: `Fluid and open circulation: ${freeSpacePct}% of floor unobstructed.`,
    });
  } else if (freeSpacePct < 55) {
    cons.push({
      ro: `Densitate ridicată de mobilier: doar ${freeSpacePct}% spațiu liber de trecere.`,
      en: `High furniture density: only ${freeSpacePct}% clear walking space.`,
    });
  }

  if (seating >= 6) {
    pros.push({
      ro: `Generos pentru oaspeți: ${seating} locuri de ședere cumulate.`,
      en: `Generous for entertaining: ${seating} total seating spots.`,
    });
  }

  if (errCount > 0) {
    cons.push({
      ro: `${errCount} erori geometrice sau coliziuni de rezolvat.`,
      en: `${errCount} geometric errors or collisions to resolve.`,
    });
  }

  if (warnCount > 0) {
    cons.push({
      ro: `${warnCount} avertismente de distanță liberă sau lumină.`,
      en: `${warnCount} clearance or daylight proximity warnings.`,
    });
  }

  return {
    id,
    title,
    snapshot: snap,
    metrics: {
      storageMeters: Math.round(storageWidthM * 10) / 10,
      freeSpacePct,
      seatingCapacity: seating,
      itemCount: placements.length,
      budgetKnown,
      unknownPricesCount,
      errorsCount: errCount,
      warningsCount: warnCount,
    },
    pros,
    cons,
  };
}

export function compareDesignOptions(
  baseSnap: Snapshot,
  alternatives: { id: string; title: string; snapshot: Snapshot }[],
  cat: Catalog,
  mc: MaterialsCatalog
): DesignComparison {
  const baseEval = evaluateDesignOption('Varianta Curentă (Bază)', baseSnap, cat, mc, 'base');
  const evaluatedOptions: DesignOptionEvaluation[] = [baseEval];

  for (const alt of alternatives) {
    const altEval = evaluateDesignOption(alt.title, alt.snapshot, cat, mc, alt.id);
    altEval.metrics.budgetDeltaKnown = altEval.metrics.budgetKnown - baseEval.metrics.budgetKnown;
    evaluatedOptions.push(altEval);
  }

  return {
    base: baseEval,
    options: evaluatedOptions,
  };
}
