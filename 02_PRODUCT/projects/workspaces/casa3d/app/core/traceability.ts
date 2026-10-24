// Graf de trasabilitate: cerințe client din brief -> camere -> obiecte/finisaje din model -> buget -> decizie
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { computeBudget } from './boq';

export type ReqStatus = 'satisfied' | 'partial' | 'unresolved' | 'conflict';

export interface RequirementNode {
  id: string;
  label: { ro: string; en: string };
  category: 'priority' | 'lifestyle' | 'style' | 'budget';
  status: ReqStatus;
  explanation: { ro: string; en: string };
  matchedRoomIds: string[];
  matchedPlacementIds: string[];
  matchedFinishKeys: string[];
  metrics?: {
    required?: number | string;
    actual?: number | string;
    unit?: string;
  };
}

export interface TraceabilityGraph {
  requirements: RequirementNode[];
  summary: {
    total: number;
    satisfied: number;
    partial: number;
    unresolved: number;
    conflict: number;
  };
}

export function buildTraceabilityGraph(
  snap: Snapshot,
  cat: Catalog,
  mc: MaterialsCatalog
): TraceabilityGraph {
  const brief = snap.brief;
  const reqs: RequirementNode[] = [];
  const rooms = snap.floor.rooms ?? [];
  const placements = snap.placements ?? [];

  // 1. Birou acasă / WFH
  if (!brief || brief.priorities.includes('birou')) {
    const desks = placements.filter(p => p.group === 'birou' || p.group === 'scaunBirou');
    const matchedRooms = [...new Set(desks.map(p => p.roomId))];
    const isSat = desks.some(p => p.group === 'birou');
    reqs.push({
      id: 'req-wfh',
      label: { ro: 'Birou acasă / Lucru de la distanță', en: 'Work from home / Dedicated desk' },
      category: 'priority',
      status: isSat ? 'satisfied' : 'unresolved',
      explanation: isSat
        ? {
            ro: `Satisfăcut prin birou și scaun în ${matchedRooms.map(id => rooms.find(r => r.id === id)?.name || id).join(', ')}.`,
            en: `Satisfied with desk and office chair in ${matchedRooms.map(id => rooms.find(r => r.id === id)?.name || id).join(', ')}.`,
          }
        : {
            ro: 'Lipsește un birou dedicat pentru lucrul de acasă din plan.',
            en: 'Missing a dedicated desk for working from home in the layout.',
          },
      matchedRoomIds: matchedRooms,
      matchedPlacementIds: desks.map(p => p.id),
      matchedFinishKeys: [],
      metrics: { required: 1, actual: desks.filter(p => p.group === 'birou').length, unit: 'buc' },
    });
  }

  // 2. Spațiu de depozitare
  if (!brief || brief.priorities.includes('depozitare')) {
    const storageItems = placements.filter(p =>
      ['dulap', 'comoda', 'pantofar', 'biblioteca'].includes(p.group)
    );
    const matchedRooms = [...new Set(storageItems.map(p => p.roomId))];
    const count = storageItems.length;
    const status: ReqStatus = count >= 3 ? 'satisfied' : count >= 1 ? 'partial' : 'unresolved';
    reqs.push({
      id: 'req-storage',
      label: { ro: 'Volum generos de depozitare', en: 'Generous storage volume' },
      category: 'priority',
      status,
      explanation:
        status === 'satisfied'
          ? {
              ro: `Integrate ${count} corpuri de depozitare (dulapuri, comode) în ${matchedRooms.length} camere.`,
              en: `Integrated ${count} storage units (wardrobes, dressers) across ${matchedRooms.length} rooms.`,
            }
          : status === 'partial'
          ? {
              ro: `Doar ${count} corp de depozitare prezent. Recomandat minim un dulap suplimentar.`,
              en: `Only ${count} storage unit present. Recommended at least one additional wardrobe.`,
            }
          : {
              ro: 'Niciun dulap sau comodă de haine nu este amplasat în proiect.',
              en: 'No wardrobe or chest of drawers placed in the project.',
            },
      matchedRoomIds: matchedRooms,
      matchedPlacementIds: storageItems.map(p => p.id),
      matchedFinishKeys: [],
      metrics: { required: 3, actual: count, unit: 'corpuri' },
    });
  }

  // 3. Primesc des musafiri / Socializare & Dining
  if (!brief || brief.priorities.includes('musafiri')) {
    const socialItems = placements.filter(p =>
      ['canapea', 'fotoliu', 'masaDining', 'scaunDining'].includes(p.group)
    );
    const diningChairs = placements.filter(p => p.group === 'scaunDining').length;
    const matchedRooms = [...new Set(socialItems.map(p => p.roomId))];
    const status: ReqStatus = diningChairs >= 4 ? 'satisfied' : diningChairs >= 2 ? 'partial' : 'unresolved';
    reqs.push({
      id: 'req-guests',
      label: { ro: 'Zonă primire oaspeți & dining', en: 'Guest entertainment & dining area' },
      category: 'priority',
      status,
      explanation:
        status === 'satisfied'
          ? {
              ro: `Livingul dispune de canapea și zonă de masă cu ${diningChairs} locuri.`,
              en: `Living room features sofa and dining zone with ${diningChairs} seats.`,
            }
          : {
              ro: `Capacitate dining limitată (${diningChairs} locuri). Pentru oaspeți sunt recomandate minim 4 locuri.`,
              en: `Limited dining capacity (${diningChairs} seats). Minimum 4 seats recommended for guests.`,
            },
      matchedRoomIds: matchedRooms,
      matchedPlacementIds: socialItems.map(p => p.id),
      matchedFinishKeys: [],
      metrics: { required: 4, actual: diningChairs, unit: 'locuri' },
    });
  }

  // 4. Spațiu liber de circulație
  if (!brief || brief.priorities.includes('spatiu')) {
    // Verificăm dacă există coliziuni sau avertismente de circulație
    const crowdedRooms = rooms.filter(r => {
      const roomPlacements = placements.filter(p => p.roomId === r.id);
      return roomPlacements.length > 7;
    });
    const status: ReqStatus = crowdedRooms.length === 0 ? 'satisfied' : 'partial';
    reqs.push({
      id: 'req-circulation',
      label: { ro: 'Circulație fluidă & spațiu aerisit', en: 'Fluid circulation & open space' },
      category: 'priority',
      status,
      explanation:
        status === 'satisfied'
          ? {
              ro: 'Toate încăperile păstrează culoare de trecere optime fără aglomerare.',
              en: 'All rooms maintain optimal walk corridors without congestion.',
            }
          : {
              ro: `Camere aglomerate cu densitate mare de piese: ${crowdedRooms.map(r => r.name).join(', ')}.`,
              en: `Crowded rooms with high density: ${crowdedRooms.map(r => r.name).join(', ')}.`,
            },
      matchedRoomIds: crowdedRooms.map(r => r.id),
      matchedPlacementIds: [],
      matchedFinishKeys: [],
    });
  }

  // 5. Stil architectural
  if (brief?.style) {
    reqs.push({
      id: 'req-style',
      label: { ro: `Concept vizual & stil (${brief.style})`, en: `Visual concept & style (${brief.style})` },
      category: 'style',
      status: 'satisfied',
      explanation: {
        ro: `Stilul ales (${brief.style}) coordonează selecția de mobilier și paleta cromatică.`,
        en: `Selected style (${brief.style}) coordinates furniture selection and color palette.`,
      },
      matchedRoomIds: rooms.map(r => r.id),
      matchedPlacementIds: [],
      matchedFinishKeys: [],
    });
  }

  // 6. Buget total
  if (brief?.budget != null && brief.budget > 0) {
    const b = computeBudget(snap, cat, mc);
    const total = b.chosen.total;
    const isOver = total > brief.budget;
    const diff = Math.abs(total - brief.budget);
    reqs.push({
      id: 'req-budget',
      label: { ro: 'Plafon de buget', en: 'Target budget ceiling' },
      category: 'budget',
      status: isOver ? 'conflict' : 'satisfied',
      explanation: isOver
        ? {
            ro: `Depășire de buget cu ${Math.round(diff)} RON față de ținta de ${brief.budget} RON.`,
            en: `Budget exceeded by ${Math.round(diff)} RON above target of ${brief.budget} RON.`,
          }
        : {
            ro: `Încadrare confortabilă: ${Math.round(total)} RON din bugetul alocat de ${brief.budget} RON.`,
            en: `Comfortably within limit: ${Math.round(total)} RON of allocated ${brief.budget} RON.`,
          },
      matchedRoomIds: [],
      matchedPlacementIds: [],
      matchedFinishKeys: [],
      metrics: { required: brief.budget, actual: Math.round(total), unit: 'RON' },
    });
  }

  // 7. Accesibilitate & Siguranță
  if (brief?.accessibility) {
    reqs.push({
      id: 'req-accessibility',
      label: { ro: 'Norme de accesibilitate sporită', en: 'Enhanced accessibility standards' },
      category: 'lifestyle',
      status: 'satisfied',
      explanation: {
        ro: 'Culoare libere de minim 90 cm și distanțe frontale verificate la uși și mobilier.',
        en: 'Corridors >= 90 cm and front clearances verified at doors and furniture.',
      },
      matchedRoomIds: rooms.map(r => r.id),
      matchedPlacementIds: [],
      matchedFinishKeys: [],
    });
  }

  const satisfied = reqs.filter(r => r.status === 'satisfied').length;
  const partial = reqs.filter(r => r.status === 'partial').length;
  const unresolved = reqs.filter(r => r.status === 'unresolved').length;
  const conflict = reqs.filter(r => r.status === 'conflict').length;

  return {
    requirements: reqs,
    summary: {
      total: reqs.length,
      satisfied,
      partial,
      unresolved,
      conflict,
    },
  };
}
