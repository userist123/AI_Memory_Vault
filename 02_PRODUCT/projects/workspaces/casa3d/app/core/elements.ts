// Indexul de elemente (ghidul BIM, docs/bim/GHID_BIM.md §3.2, §3.6 și §5): fiecare element al proiectului cu id-ul lui stabil,
// disciplina, tipul și nivelul. Se derivă din Snapshot și nu se salvează: Snapshot rămâne singura sursă de adevăr
// (CONSTITUTION art. 1), iar vederea „federată” e un filtru pe disciplină, nu un al doilea model.
// Proveniența (sursă, încredere, stare de revizie) NU se completează aici: elementele de azi nu o au declarată,
// iar o valoare implicită ar fi inventată (art. 4). Câmpurile vin în faza de import (docs/bim/FAZA0_INVENTAR.md).
import type { Snapshot, WallOrigin } from './types';
import { floors } from './levels';
import { TECH_KINDS } from './technical';

export const DISCIPLINES = ['architecture', 'structure', 'mechanical', 'plumbing', 'electrical', 'interior'] as const;
export type Discipline = typeof DISCIPLINES[number];
/** `levelId` null = elementul trimite la o cameră care nu există (nivelul nu se ghicește). `origin` = proveniența declarată la import. */
export interface ElementRef { elementId: string; discipline: Discipline; elementType: string; levelId: string | null; units: 'm'; origin?: WallOrigin }

const TECH_DISCIPLINE: Record<'electric' | 'plumbing' | 'hvac', Discipline> = { electric: 'electrical', plumbing: 'plumbing', hvac: 'mechanical' };

/** Toate elementele, în ordinea modelului (nivel cu nivel, apoi piesele și stratul tehnic). */
export function elementIndex(s: Snapshot): ElementRef[] {
  const out: ElementRef[] = [];
  const add = (elementId: string, discipline: Discipline, elementType: string, levelId: string | null, origin?: WallOrigin) =>
    out.push({ elementId, discipline, elementType, levelId, units: 'm', ...(origin ? { origin } : {}) });
  const levelOfRoom = new Map<string, string>();
  for (const f of floors(s)) {
    for (const r of f.rooms) { levelOfRoom.set(r.id, f.id); add(r.id, 'architecture', 'room', f.id); }
    for (const w of f.walls) {
      add(w.id, 'architecture', 'wall', f.id, w.origin);
      for (const o of w.openings) add(o.id, 'architecture', o.kind, f.id);
    }
    for (const x of f.stairs ?? []) add(x.id, 'architecture', 'stair', f.id);
    // stâlpul fără rol e structural; ghena și casetarea sunt închideri de arhitectură în jurul instalațiilor
    for (const c of f.columns ?? []) { const role = c.role ?? 'structural'; add(c.id, role === 'structural' ? 'structure' : 'architecture', role === 'structural' ? 'column' : role, f.id); }
    for (const b of f.beams ?? []) add(b.id, 'structure', 'beam', f.id);
    for (const g of f.glassWalls ?? []) add(g.id, 'architecture', 'glass_wall', f.id);
    for (const n of f.niches ?? []) add(n.id, 'architecture', 'niche', f.id);
    for (const r of f.railings ?? []) add(r.id, 'architecture', 'railing', f.id);
    for (const z of f.ceilingZones ?? []) add(z.id, 'architecture', 'ceiling_zone', f.id);
    for (const z of f.zones ?? []) add(z.id, 'interior', 'floor_zone', f.id);
    for (const w of f.wallItems ?? []) add(w.id, 'interior', 'wall_item', f.id);
  }
  for (const p of s.placements) add(p.id, 'interior', 'furniture', levelOfRoom.get(p.roomId) ?? null);
  for (const t of s.tech ?? []) {
    const k = TECH_KINDS[t.kind];
    if (k) add(t.id, TECH_DISCIPLINE[k.category], t.kind, levelOfRoom.get(t.roomId) ?? null);
  }
  return out;
}

export function elementsOf(s: Snapshot, d: Discipline): ElementRef[] { return elementIndex(s).filter(e => e.discipline === d); }

/** Id-urile folosite de mai multe elemente: identitatea stabilă cere ca un id să numească un singur element pe toată casa. */
export function duplicateElementIds(s: Snapshot): string[] {
  const seen = new Set<string>(), dup = new Set<string>();
  for (const e of elementIndex(s)) { if (seen.has(e.elementId)) dup.add(e.elementId); seen.add(e.elementId); }
  return [...dup].sort();
}
