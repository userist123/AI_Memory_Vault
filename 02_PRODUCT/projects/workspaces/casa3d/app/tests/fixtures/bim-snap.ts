// Proiectul de test comun pentru exporturile BIM (IFC, registrul de adaptoare): toate tipurile de elemente, pe două niveluri.
import type { Snapshot } from '../../core/types';
export const ifcSnap = (): Snapshot => ({ name: 'Casa Ștefan', floor: { id: 'f0', name: 'Parter', ceilingHeight: 2.6,
  rooms: [{ id: 'r1', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 5, z1: 4 } }],
  walls: [{ id: 'w1', a: [0, 0], b: [5, 0], thickness: .25, exterior: true, structural: true, openings: [{ id: 'o1', kind: 'door', offset: 1, width: .9 }, { id: 'o2', kind: 'window', offset: 3, width: 1.2 }] },
    { id: 'w2', a: [5, 0], b: [5, 4], thickness: .15, exterior: false, openings: [] }],
  columns: [{ id: 'c1', x: 2.5, z: 2, w: .3, d: .3, shape: 'round' }], beams: [{ id: 'b1', a: [0, 3], b: [5, 3], width: .25, depth: .4 }],
  footings: [{ id: 'fd1', x: 2.5, z: 2, w: 1, d: 1, depth: .6, kind: 'pad' }],
  ceilingZones: [{ id: 'cz1', roomId: 'r1', rect: { x0: 1, z0: 1, x1: 3, z1: 2 }, dropCm: 15 }],
  mepRuns: [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[.5, 1, .2], [.5, 2.4, .2], [4.5, 2.4, .2]] }, { id: 'm2', system: 'electric', points: [[1, 2.5, 1], [4, 2.5, 1]] }] },
  levels: [{ id: 'f1', name: 'Etaj', ceilingHeight: 2.5, rooms: [{ id: 'r2', name: 'Dormitor', type: 'bedroom', rect: { x0: 0, z0: 0, x1: 4, z1: 4 } }], walls: [] }],
  placements: [{ id: 'pl1', roomId: 'r1', group: 'dulap', variantId: 'dulap-0', x: 4, z: 3, rotation: 0, source: 'manual' }, { id: 'pl2', roomId: 'r1', group: 'x', variantId: 'necunoscut', x: 1, z: 1, rotation: 0, source: 'manual' }],
  selections: {}, picked: [] });
