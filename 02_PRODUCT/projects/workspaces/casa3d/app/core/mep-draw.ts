// Unealta „Traseu instalații” (etapa Instalații): traseul desenat direct în plan, clic cu clic. Funcții pure, fără React:
// lanțul de puncte din plan, blocarea pe orizontală / verticală (Shift), lungimea și traseul de salvat în Floor.mepRuns.
// Aceleași reguli ca în panou (components/MepPanel.tsx): cota traseului o declară utilizatorul și stă între pardoseală și
// tavan; diametrul rămâne nedeclarat dacă nu e completat (art. 4: nicio valoare inventată).
import type { MepRun, MepSystem } from './types';
import { MEP_LIMITS, MEP_SYSTEMS } from './mep';
import { r3 } from './geometry';

export type P2 = [number, number];
/** Sub un milimetru două puncte sunt același punct: coordonatele planului se rotunjesc la mm (r3). */
const SAME_M = 1e-3;
const dist = (a: P2, b: P2) => Math.hypot(b[0] - a[0], b[1] - a[1]);

/** Punctul nou adăugat la lanț (rotunjit la mm). Întoarce ACELAȘI tablou dacă punctul e respins: cade peste ultimul
 *  punct (al doilea clic dintr-un dublu-clic) sau lanțul are deja numărul maxim de puncte al unui traseu. */
export function appendRunPoint(pts: P2[], p: P2): P2[] {
  const q: P2 = [r3(p[0]), r3(p[1])], last = pts[pts.length - 1];
  if (pts.length >= MEP_LIMITS.maxPoints || (last && dist(last, q) < SAME_M)) return pts;
  return [...pts, q];
}
/** Escape în timpul desenării: lanțul fără ultimul punct (gol = desenul e anulat). */
export const dropLastRunPoint = (pts: P2[]): P2[] => pts.slice(0, -1);

/** Shift: punctul adus pe orizontala sau pe verticala lui `anchor`, pe axa cu deplasarea mai mare (la egalitate, orizontala). */
export function orthoPoint(anchor: P2, p: P2): P2 {
  return Math.abs(p[0] - anchor[0]) >= Math.abs(p[1] - anchor[1]) ? [r3(p[0]), anchor[1]] : [anchor[0], r3(p[1])];
}
/** Lungimea lanțului din plan, în metri (la cotă constantă este și lungimea traseului). */
export function planRunLength(pts: P2[]): number {
  let L = 0; for (let i = 1; i < pts.length; i++) L += dist(pts[i - 1]!, pts[i]!);
  return r3(L);
}
/** Următorul id liber de forma `mep-N`, ca la traseele adăugate din panou. */
export function nextRunId(taken: Iterable<string>): string {
  const s = new Set(taken); let n = 1; while (s.has(`mep-${n}`)) n++; return `mep-${n}`;
}

export type DrawnRunError = 'system' | 'points' | 'height' | 'diameter';
export interface DrawnRunInput { id: string; system: MepSystem; points: P2[];
  /** cota traseului de la pardoseala nivelului, în metri (obligatorie: o declară utilizatorul) */
  heightM: number | null | undefined;
  /** înălțimea nivelului: cota trebuie să fie între 0 și tavan, ca în panou */
  ceilingM: number;
  /** diametrul declarat, în mm; lipsă = nedeclarat */
  diameterMm?: number | null }

/** Traseul de salvat dintr-un lanț desenat în plan: toate punctele la cota declarată, în ordinea clicurilor (sensul curgerii).
 *  Nu completează nimic în locul utilizatorului: fără cotă validă sau cu un diametru în afara limitelor întoarce eroarea. */
export function runFromPlanPoints(o: DrawnRunInput): { run: MepRun } | { error: DrawnRunError } {
  if (!Object.prototype.hasOwnProperty.call(MEP_SYSTEMS, o.system)) return { error: 'system' };
  const pts = o.points.reduce<P2[]>((acc, p) => appendRunPoint(acc, p), []);
  if (pts.length < 2 || o.points.length > MEP_LIMITS.maxPoints || !pts.every(p => Number.isFinite(p[0]) && Number.isFinite(p[1]))) return { error: 'points' };
  const h = o.heightM;
  if (typeof h !== 'number' || !Number.isFinite(h) || h < 0 || h > o.ceilingM + 1e-6) return { error: 'height' };   // 2,55 × 100 = 254,99999…: toleranță de rotunjire
  const d = o.diameterMm;
  if (d != null && !(Number.isFinite(d) && d >= MEP_LIMITS.minDiameterMm && d <= MEP_LIMITS.maxDiameterMm)) return { error: 'diameter' };
  const y = r3(h);
  return { run: { id: o.id, system: o.system, points: pts.map(p => [p[0], y, p[1]] as [number, number, number]), ...(d != null ? { diameterMm: d } : {}) } };
}

/** Câmpul numeric opțional al barei uneltei: gol = nedeclarat (null), altfel numărul (NaN pentru text nevalid). */
export const optionalNumber = (v: string): number | null => v.trim() === '' ? null : Number(v.trim().replace(',', '.'));
