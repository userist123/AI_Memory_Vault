// Operații pure de editare a planului: duplicare, mutare fină și măsurare. Fără efecte; editorul le aplică prin commit().
import { validatePlacement, severityOf } from './validate';
import { r3, wallLength } from './geometry';
import type { Catalog, Issue, Snapshot } from './types';

export const DUPLICATE_STEP_CM = 30;
export const NUDGE_CM = 5, NUDGE_BIG_CM = 50;
/** Câți pași de 30 cm se încearcă la duplicare (30, 60, … cm) înainte să renunțe. */
const DUPLICATE_MAX_STEPS = 14;
// Direcții în ordinea încercării: +x, +z, −x, −z, apoi diagonalele.
const DIRS: [number, number][] = [[1, 0], [0, 1], [-1, 0], [0, -1], [1, 1], [-1, 1], [1, -1], [-1, -1]];

export type EditResult = { ok: true; snapshot: Snapshot; id: string; issues: Issue[] } | { ok: false; message: string; key?: string; vars?: Record<string, string | number>; cause?: Issue };

/** Copie a piesei `id` cu id nou. Se încearcă o deplasare de 30 cm în prima direcție liberă; dacă piesa e mai mare
 *  și 30 cm încă se suprapun, deplasarea crește din 30 în 30 cm. Variantă, rotație, dimensiune și aspect se păstrează.
 *  Se preferă o poziție fără avertismente; altfel prima fără ERROR. Dacă nu există, nu se face nimic. */
export function duplicatePlacement(snap: Snapshot, cat: Catalog, id: string, newId: string): EditResult {
  const src = snap.placements.find(p => p.id === id); if (!src) return { ok: false, message: 'Piesa nu există.', key: 'edit.noPiece' };
  let fallback: { x: number; z: number; issues: Issue[] } | null = null;
  for (let k = 1; k <= DUPLICATE_MAX_STEPS; k++) for (const [dx, dz] of DIRS){
    const d = k * DUPLICATE_STEP_CM / 100, x = r3(src.x + dx * d), z = r3(src.z + dz * d);
    const probe = { ...structuredClone(src), id: newId, x, z, source: 'manual' as const };
    const issues = validatePlacement({ ...snap, placements: [...snap.placements, probe] }, cat, probe), sev = severityOf(issues);
    if (sev === 'ERROR') continue;
    if (sev === 'PASS') return build(snap, src.id, probe, issues);
    fallback ||= { x, z, issues };
  }
  if (fallback) return build(snap, src.id, { ...structuredClone(src), id: newId, x: fallback.x, z: fallback.z, source: 'manual' }, fallback.issues);
  return { ok: false, message: 'Nu am găsit loc liber pentru copie lângă piesă.', key: 'edit.noFreeSpot' };
}
function build(snap: Snapshot, srcId: string, copy: Snapshot['placements'][number], issues: Issue[]): EditResult {
  const n = structuredClone(snap); n.placements.push(copy);
  const look = n.appearance?.items?.[srcId]; if (look) n.appearance!.items![copy.id] = { ...look };
  return { ok: true, snapshot: n, id: copy.id, issues };
}

/** Mută piesa cu (dxCm, dzCm), validat ca la tragere: ERROR refuză. */
export function nudgePlacement(snap: Snapshot, cat: Catalog, id: string, dxCm: number, dzCm: number): EditResult {
  if (!snap.placements.some(p => p.id === id)) return { ok: false, message: 'Piesa nu există.', key: 'edit.noPiece' };
  const n = structuredClone(snap), q = n.placements.find(p => p.id === id)!;
  q.x = r3(q.x + dxCm / 100); q.z = r3(q.z + dzCm / 100); q.source = 'manual';
  const issues = validatePlacement(n, cat, q), err = issues.find(i => i.severity === 'ERROR');
  if (err) return { ok: false, message: `Poziție refuzată: ${err.message}`, key: 'edit.refused', cause: err };
  return { ok: true, snapshot: n, id, issues };
}

/** Distanța în metri între două puncte ale planului (rotunjită la mm). */
export const measure = (a: [number, number], b: [number, number]) => r3(wallLength(a, b));
