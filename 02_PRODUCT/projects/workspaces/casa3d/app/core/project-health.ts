// Diagnostic de sănătate a proiectului bazat pe dovezi (Evidence-based Project Health & Next-Best-Action Queue)
// Fără scoruri arbitrare sau inventate: doar fapte calculate determinist din geometrie, instalații, prețuri și decizii.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { validateFloor, validatePlacement } from './validate';
import { roomCeilingDropCm } from './finishes';
import { resolve } from './catalog';

export type HealthSeverity = 'BLOCKER' | 'WARNING' | 'TIP';

export interface HealthAction {
  id: string;
  category: 'geometry' | 'technical' | 'ffe' | 'client' | 'site';
  severity: HealthSeverity;
  title: { ro: string; en: string };
  actionLabel: { ro: string; en: string };
  targetKind: 'room' | 'placement' | 'wall' | 'stage';
  targetId?: string;
  stageTarget?: string;
}

export interface ProjectHealthReport {
  score: number; // 0 - 100 bazat strict pe deduceri justificate
  facts: {
    errorsCount: number;
    warningsCount: number;
    unknownPricesCount: number;
    missingSwitchesCount: number;
    missingDrainsCount: number;
  };
  actionsQueue: HealthAction[];
}

export function evaluateProjectHealth(
  snap: Snapshot,
  cat: Catalog,
  _mc: MaterialsCatalog
): ProjectHealthReport {
  const rooms = snap.floor.rooms ?? [];
  const placements = snap.placements ?? [];
  const tech = snap.tech ?? [];
  const actions: HealthAction[] = [];

  let errorsCount = 0;
  let warningsCount = 0;

  // 1. Erori / Avertismente etaj și pereți
  const floorIssues = validateFloor(snap.floor, id => roomCeilingDropCm(snap, id));
  for (const fi of floorIssues) {
    if (fi.severity === 'ERROR') {
      errorsCount++;
      actions.push({
        id: `geo-floor-${fi.code}`,
        category: 'geometry',
        severity: 'BLOCKER',
        title: { ro: `Problemă geometrică pereți: ${fi.message}`, en: `Floor geometry problem: ${fi.message}` },
        actionLabel: { ro: 'Corectează în Releveu', en: 'Fix in Survey' },
        targetKind: 'wall',
        stageTarget: 'survey',
      });
    } else {
      warningsCount++;
    }
  }

  // 2. Erori / Avertismente mobilier
  for (const p of placements) {
    const pIssues = validatePlacement(snap, cat, p);
    const pName = resolve(cat, p.variantId)?.product.name || p.group;
    for (const pi of pIssues) {
      if (pi.severity === 'ERROR') {
        errorsCount++;
        actions.push({
          id: `ffe-${p.id}-${pi.code}`,
          category: 'ffe',
          severity: 'BLOCKER',
          title: {
            ro: `${pName}: poziție invalidă (${pi.code === 'OUT_OF_ROOM' ? 'în afara camerei' : 'coliziune critică'}).`,
            en: `${pName}: invalid position (${pi.code === 'OUT_OF_ROOM' ? 'outside room' : 'critical collision'}).`,
          },
          actionLabel: { ro: 'Repoziționează piesa', en: 'Reposition piece' },
          targetKind: 'placement',
          targetId: p.id,
          stageTarget: 'ffe',
        });
      } else if (pi.severity === 'WARNING') {
        warningsCount++;
        actions.push({
          id: `ffe-${p.id}-${pi.code}`,
          category: 'ffe',
          severity: 'WARNING',
          title: {
            ro: `${pName}: distanță liberă frontală redusă sau suprapunere parțială.`,
            en: `${pName}: reduced front clearance or partial overlap.`,
          },
          actionLabel: { ro: 'Verifică distanța', en: 'Check clearance' },
          targetKind: 'placement',
          targetId: p.id,
          stageTarget: 'ffe',
        });
      }
    }
  }

  // 3. Verificări instalații
  let missingSwitchesCount = 0;
  let missingDrainsCount = 0;

  for (const r of rooms) {
    const hasSwitch = tech.some(t => t.roomId === r.id && t.kind === 'switch');
    if (!hasSwitch) {
      missingSwitchesCount++;
      actions.push({
        id: `tech-switch-${r.id}`,
        category: 'technical',
        severity: 'TIP',
        title: {
          ro: `Camera ${r.name} nu are un întrerupător de lumină amplasat.`,
          en: `Room ${r.name} has no light switch placed.`,
        },
        actionLabel: { ro: 'Adaugă întrerupător', en: 'Add switch' },
        targetKind: 'room',
        targetId: r.id,
        stageTarget: 'mep',
      });
    }
  }

  for (const p of placements) {
    if (['lavoar', 'dus', 'wc'].includes(p.group)) {
      const hasDrain = tech.some(t => t.kind === 'drain' && t.roomId === p.roomId);
      if (!hasDrain) {
        missingDrainsCount++;
        actions.push({
          id: `tech-drain-${p.id}`,
          category: 'technical',
          severity: 'WARNING',
          title: {
            ro: `Obiectul sanitar (${p.group}) nu are un punct de scurgere asociat în încăpere.`,
            en: `Sanitary fixture (${p.group}) lacks an associated drain in the room.`,
          },
          actionLabel: { ro: 'Adaugă scurgere', en: 'Add drain' },
          targetKind: 'placement',
          targetId: p.id,
          stageTarget: 'mep',
        });
        break; // one warning per sanitary room is enough
      }
    }
  }

  // 4. Prețuri necunoscute
  let unknownPricesCount = 0;
  for (const p of placements) {
    const res = resolve(cat, p.variantId);
    if (!res || !res.offer || res.offer.price == null) {
      unknownPricesCount++;
    }
  }

  if (unknownPricesCount > 0) {
    actions.push({
      id: 'budget-unknown-prices',
      category: 'ffe',
      severity: 'TIP',
      title: {
        ro: `${unknownPricesCount} piese de mobilier au preț de achiziție nespecificat.`,
        en: `${unknownPricesCount} furniture items have unspecified purchase prices.`,
      },
      actionLabel: { ro: 'Completează în Deviz', en: 'Complete in BOQ' },
      targetKind: 'stage',
      stageTarget: 'quote',
    });
  }

  // Calcul scor matematic determinist (100 - deduceri obiective)
  let score = 100;
  score -= Math.min(50, errorsCount * 25);
  score -= Math.min(25, warningsCount * 5);
  score -= Math.min(10, missingDrainsCount * 5);
  score -= Math.min(10, unknownPricesCount * 2);
  score -= Math.min(5, missingSwitchesCount * 1);
  score = Math.max(10, Math.min(100, Math.round(score)));

  // Sortare acțiuni: BLOCKER -> WARNING -> TIP
  const sevWeight: Record<HealthSeverity, number> = { BLOCKER: 0, WARNING: 1, TIP: 2 };
  actions.sort((a, b) => sevWeight[a.severity] - sevWeight[b.severity]);

  return {
    score,
    facts: {
      errorsCount,
      warningsCount,
      unknownPricesCount,
      missingSwitchesCount,
      missingDrainsCount,
    },
    actionsQueue: actions,
  };
}
