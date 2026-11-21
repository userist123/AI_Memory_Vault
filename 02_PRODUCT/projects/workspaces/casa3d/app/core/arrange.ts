// Selecția multiplă de piese din plan și așezarea lor: aliniere, distribuire, mutare / rotire / duplicare de grup.
// Totul e pur (fără React, fără DOM): PlanView și editorul aplică rezultatele într-un singur pas de anulare.
// Convenție: un grup e o listă de id-uri de piese, cu piesa principală pe primul loc (cea arătată în inspector și în 3D).
import { footprintOf, validatePlacement } from './validate';
import { r3 } from './geometry';
import { DUPLICATE_STEP_CM } from './edit-ops';
import type { Catalog, FurniturePlacement, Issue, Snapshot } from './types';

export interface Box { x0: number; x1: number; z0: number; z1: number }
/** O piesă pentru aliniere: centrul ei și cutia (amprenta în plan, după rotație). */
export interface ArrangeItem { id: string; x: number; z: number; box: Box }
export interface PieceMove { id: string; x: number; z: number }
/** `top` = z mic (sus pe plan); `centerX` = centrele pe aceeași verticală (același x); `centerZ` = pe aceeași orizontală (același z). */
export type AlignMode = 'left' | 'right' | 'top' | 'bottom' | 'centerX' | 'centerZ';
export const ALIGN_MODES: AlignMode[] = ['left', 'centerX', 'right', 'top', 'centerZ', 'bottom'];
export type Axis = 'x' | 'z';

/** Grupul selectat: piesa principală, apoi restul, fără dubluri. Altă selecție decât o piesă nu are grup. */
export function groupIds(sel: { kind: string; id: string } | null, multi: readonly string[]): string[] {
  return sel?.kind === 'placement' ? [sel.id, ...multi.filter((id, i) => id !== sel.id && multi.indexOf(id) === i)] : [];
}
/** Shift+clic: piesa intră în grup (la coadă) sau iese din el; dacă iese principala, următoarea îi ia locul. */
export function toggleMulti(group: readonly string[], id: string): string[] {
  return group.includes(id) ? group.filter(x => x !== id) : [...group, id];
}
/** Grupul după un dreptunghi de selecție: ce era rămâne (principala la fel), piesele noi se adaugă la coadă. */
export function addToGroup(group: readonly string[], ids: readonly string[]): string[] {
  const out = [...group]; for (const id of ids) if (!out.includes(id)) out.push(id); return out;
}
/** Piesele cu centrul în dreptunghi (marginea inclusă); colțurile pot veni în orice ordine. */
export function piecesInRect(placements: readonly Pick<FurniturePlacement, 'id' | 'x' | 'z'>[], a: [number, number], b: [number, number]): string[] {
  const x0 = Math.min(a[0], b[0]), x1 = Math.max(a[0], b[0]), z0 = Math.min(a[1], b[1]), z1 = Math.max(a[1], b[1]);
  return placements.filter(p => p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1).map(p => p.id);
}
/** Piesele grupului cu cutiile lor (footprintOf); cele fără variantă în catalog sau inexistente pe nivel lipsesc. */
export function arrangeItems(snap: Pick<Snapshot, 'placements'>, cat: Catalog, ids: readonly string[]): ArrangeItem[] {
  return ids.flatMap(id => { const p = snap.placements.find(q => q.id === id), box = p ? footprintOf(cat, p) : null; return p && box ? [{ id, x: p.x, z: p.z, box }] : []; });
}

/**
 * Aliniere după cutia fiecărei piese, față de cutia întregului grup: `left` aduce toate marginile stângi la cea mai din
 * stânga, `centerX` aduce centrele cutiilor pe mijlocul grupului etc. Întoarce noile centre, în ordinea primită.
 * Sub două piese nu e nimic de aliniat (pozițiile rămân).
 */
export function alignPieces(items: readonly ArrangeItem[], mode: AlignMode): PieceMove[] {
  const keep = items.map(i => ({ id: i.id, x: i.x, z: i.z })); if (items.length < 2) return keep;
  const x0 = Math.min(...items.map(i => i.box.x0)), x1 = Math.max(...items.map(i => i.box.x1)), z0 = Math.min(...items.map(i => i.box.z0)), z1 = Math.max(...items.map(i => i.box.z1));
  return items.map(i => {
    const dx = mode === 'left' ? x0 - i.box.x0 : mode === 'right' ? x1 - i.box.x1 : mode === 'centerX' ? (x0 + x1) / 2 - (i.box.x0 + i.box.x1) / 2 : 0;
    const dz = mode === 'top' ? z0 - i.box.z0 : mode === 'bottom' ? z1 - i.box.z1 : mode === 'centerZ' ? (z0 + z1) / 2 - (i.box.z0 + i.box.z1) / 2 : 0;
    return { id: i.id, x: r3(i.x + dx), z: r3(i.z + dz) }; });
}

/**
 * Distribuire: spații egale între cutii pe axa dată. Piesele se iau în ordinea de pe axă; prima și ultima rămân pe loc,
 * cele dintre ele se așază la intervale egale (spațiul poate fi negativ când piesele nu încap: se suprapun egal).
 * Sub trei piese nu e nimic de distribuit. Întoarce noile centre, în ordinea primită.
 */
export function distributePieces(items: readonly ArrangeItem[], axis: Axis): PieceMove[] {
  const keep = items.map(i => ({ id: i.id, x: i.x, z: i.z })); if (items.length < 3) return keep;
  const lo = (i: ArrangeItem) => axis === 'x' ? i.box.x0 : i.box.z0, hi = (i: ArrangeItem) => axis === 'x' ? i.box.x1 : i.box.z1;
  const sorted = [...items].sort((a, b) => (lo(a) + hi(a)) - (lo(b) + hi(b)) || lo(a) - lo(b) || a.id.localeCompare(b.id));
  const first = sorted[0]!, last = sorted[sorted.length - 1]!, used = sorted.reduce((s, i) => s + hi(i) - lo(i), 0), gap = (hi(last) - lo(first) - used) / (sorted.length - 1);
  const shift = new Map<string, number>(); let at = hi(first) + gap;
  for (const i of sorted.slice(1, -1)){ shift.set(i.id, at - lo(i)); at += hi(i) - lo(i) + gap; }
  return items.map(i => { const d = shift.get(i.id) ?? 0; return { id: i.id, x: axis === 'x' ? r3(i.x + d) : i.x, z: axis === 'z' ? r3(i.z + d) : i.z }; });
}

export type GroupResult = { ok: true; snapshot: Snapshot; ids: string[]; issues: Issue[]; changed: boolean } | { ok: false; cause?: Issue; key: string };

// validarea grupului în starea finală: o singură piesă cu ERROR refuză toată operația (ca la mutarea unei singure piese)
function checked(n: Snapshot, cat: Catalog, ids: string[], changed: boolean): GroupResult {
  const issues: Issue[] = [];
  for (const id of ids){ const p = n.placements.find(q => q.id === id); if (!p) continue; const iss = validatePlacement(n, cat, p), err = iss.find(i => i.severity === 'ERROR');
    if (err) return { ok: false, cause: err, key: 'edit.refused' }; issues.push(...iss); }
  return { ok: true, snapshot: n, ids, issues, changed };
}
/** Pune piesele la pozițiile date (aliniere, distribuire), validat pe tot grupul. Piesele nemișcate nu devin „manuale”. */
export function applyMoves(snap: Snapshot, cat: Catalog, moves: readonly PieceMove[]): GroupResult {
  const n = structuredClone(snap), ids: string[] = []; let changed = false;
  for (const m of moves){ const p = n.placements.find(q => q.id === m.id); if (!p) continue; ids.push(p.id);
    if (p.x !== m.x || p.z !== m.z){ p.x = m.x; p.z = m.z; p.source = 'manual'; changed = true; } }
  if (!ids.length) return { ok: false, key: 'edit.noPiece' };
  return checked(n, cat, ids, changed);
}
/** Mută tot grupul cu (dxCm, dzCm): săgețile. */
export function nudgeGroup(snap: Snapshot, cat: Catalog, ids: readonly string[], dxCm: number, dzCm: number): GroupResult {
  return applyMoves(snap, cat, ids.flatMap(id => { const p = snap.placements.find(q => q.id === id); return p ? [{ id, x: r3(p.x + dxCm / 100), z: r3(p.z + dzCm / 100) }] : []; }));
}
/** Rotește fiecare piesă cu 90° în jurul centrului ei (pozițiile rămân). */
export function rotateGroup(snap: Snapshot, cat: Catalog, ids: readonly string[]): GroupResult {
  const n = structuredClone(snap), done: string[] = [];
  for (const id of ids){ const p = n.placements.find(q => q.id === id); if (!p) continue; p.rotation = r3((p.rotation + Math.PI / 2) % (Math.PI * 2)); p.source = 'manual'; done.push(id); }
  if (!done.length) return { ok: false, key: 'edit.noPiece' };
  return checked(n, cat, done, true);
}

const DIRS: [number, number][] = [[1, 0], [0, 1], [-1, 0], [0, -1], [1, 1], [-1, 1], [1, -1], [-1, -1]], MAX_STEPS = 14;
/**
 * Copii ale pieselor grupului, toate cu același decalaj (ca la duplicarea unei piese: 30 cm în prima direcție liberă,
 * apoi din 30 în 30 cm), deci aranjamentul copiat rămâne întreg. Se preferă o poziție fără avertismente; altfel prima
 * fără ERROR. `newId(i)` dă id-ul copiei piesei i. Aspectul fiecărei piese se copiază.
 */
export function duplicateGroup(snap: Snapshot, cat: Catalog, ids: readonly string[], newId: (i: number) => string): GroupResult {
  const src = ids.flatMap(id => snap.placements.filter(p => p.id === id)); if (!src.length) return { ok: false, key: 'edit.noPiece' };
  const copyIds = src.map((_, i) => newId(i)); let fallback: GroupResult | null = null;
  for (let k = 1; k <= MAX_STEPS; k++) for (const [dx, dz] of DIRS){ const d = k * DUPLICATE_STEP_CM / 100, n = structuredClone(snap);
    src.forEach((s, i) => { n.placements.push({ ...structuredClone(s), id: copyIds[i]!, x: r3(s.x + dx * d), z: r3(s.z + dz * d), source: 'manual' });
      const look = n.appearance?.items?.[s.id]; if (look) n.appearance!.items![copyIds[i]!] = { ...look }; });
    const r = checked(n, cat, copyIds, true); if (!r.ok) continue;
    if (!r.issues.length) return r; fallback ||= r; }
  return fallback ?? { ok: false, key: 'edit.noFreeSpot' };
}
