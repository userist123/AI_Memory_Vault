// Oglindirea pieselor de mobilier (`mirrored: true`): piesa e oglindită față de axa ei de lățime (stânga ↔ dreapta), în
// sistemul ei local, înainte de rotație — canapeaua de colț cu brațul pe cealaltă parte, biroul cu sertarele mutate.
// Amprenta (footprintOf) e un dreptunghi centrat în piesă, deci nu se schimbă: validarea și coliziunile rămân aceleași.
// Planul, planșele și 3D-ul aplică aceeași transformare: translate · rotate · scale(-1, 1). Funcții pure.
import type { FurniturePlacement, Snapshot } from './types';

type Posed = Pick<FurniturePlacement, 'x' | 'z' | 'rotation' | 'mirrored'>;
const r3 = (v: number) => Math.round(v * 1000) / 1000;

/** Comută oglindirea pieselor `ids` (fiecare piesă își schimbă starea); `changed` = câte piese s-au schimbat. Nu mută nimic. */
export function toggleMirror(snap: Snapshot, ids: readonly string[]): { snapshot: Snapshot; changed: number } {
  const n = structuredClone(snap), set = new Set(ids); let changed = 0;
  for (const p of n.placements){ if (!set.has(p.id)) continue; if (p.mirrored === true) delete p.mirrored; else p.mirrored = true; changed++; }
  return { snapshot: changed ? n : snap, changed };
}

/** Un punct din sistemul local al simbolului (x pe lățime, y pe adâncime, fața spre +y) oglindit stânga ↔ dreapta. */
export const mirrorLocal = (q: readonly [number, number], mirrored?: boolean): [number, number] => mirrored ? [-q[0], q[1]] : [q[0], q[1]];

/** Transformarea SVG a simbolului unei piese: mutare, rotire spre fața ei, apoi (în sistemul piesei) oglindirea. */
export const symbolTransform = (p: Posed): string => `translate(${p.x} ${p.z}) rotate(${-p.rotation * 180 / Math.PI})${p.mirrored === true ? ' scale(-1 1)' : ''}`;

/** Același lucru pe puncte: unde ajunge în plan (x, z în m) punctul local `q` al simbolului — pentru DXF și teste. */
export function symbolPoint(p: Posed, q: readonly [number, number]): [number, number] {
  const [lx, ly] = mirrorLocal(q, p.mirrored === true), c = Math.cos(p.rotation), s = Math.sin(p.rotation);
  return [r3(p.x + lx * c + ly * s), r3(p.z - lx * s + ly * c)];
}

/** Triunghiurile dreptunghiului de umbră de contact (colțuri în ordinea [x0,z0], [x1,z0], [x1,z1], [x0,z1], trecute prin
 *  transformarea piesei). Oglindirea (determinant negativ) întoarce ordinea colțurilor în plan, deci indicii se inversează,
 *  ca fața să rămână spre +y (materialul umbrei are o singură față). Piesa neoglindită păstrează indicii de până acum. */
export const contactQuadIndices = (base: number, flipped: boolean): number[] =>
  flipped ? [base, base + 1, base + 2, base, base + 2, base + 3] : [base, base + 2, base + 1, base, base + 3, base + 2];
