// Coloane verticale pe un traseu de instalații: urcare/coborâre într-un punct al traseului (ex. din șapă în perete, de sub
// tavan la lavoar). Traseul desenat în plan are toate punctele la aceeași cotă; aici se inserează un segment vertical, iar
// restul traseului continuă la cota nouă, sau se adaugă o coborâre/urcare la un capăt. Cotele le dă omul (art. 4). Pur.
import type { MepRun } from './types';
import { MEP_LIMITS } from './mep';

type P3 = [number, number, number];
export type RiserResult = { ok: true; run: MepRun } | { ok: false; key: 'mepRiser.badIndex' | 'mepRiser.badHeight' | 'mepRiser.same' | 'mepRiser.tooMany' };
const r3 = (n: number) => Math.round(n * 1000) / 1000;
const okHeight = (h: number, ceiling: number) => Number.isFinite(h) && h >= -1 && h <= ceiling + 1 + 1e-9;

/** În punctul `index` traseul urcă/coboară la `toHeight` (m); punctele de după se deplasează cu aceeași diferență de cotă, deci
 *  coloanele și coborârile de mai departe (ex. racordul lavoarului) își păstrează forma. Dacă vreunul ar ieși din limite, se refuză. */
export function addRiser(run: MepRun, index: number, toHeight: number, ceiling: number): RiserResult {
  if (!Number.isInteger(index) || index < 0 || index >= run.points.length) return { ok: false, key: 'mepRiser.badIndex' };
  if (!okHeight(toHeight, ceiling)) return { ok: false, key: 'mepRiser.badHeight' };
  const h = r3(toHeight), at = run.points[index]!; if (Math.abs(at[1] - h) < 1e-6) return { ok: false, key: 'mepRiser.same' };
  if (run.points.length + 1 > MEP_LIMITS.maxPoints) return { ok: false, key: 'mepRiser.tooMany' };
  const dh = h - at[1], after = run.points.slice(index + 1).map(p => [p[0], r3(p[1] + dh), p[2]] as P3);
  if (after.some(p => !okHeight(p[1], ceiling))) return { ok: false, key: 'mepRiser.badHeight' };
  const points: P3[] = [...run.points.slice(0, index + 1).map(p => [...p] as P3), [at[0], h, at[2]], ...after];
  return { ok: true, run: { ...run, points: dedupe(points) } };
}
/** La un capăt (`start` / `end`) traseul coboară sau urcă vertical până la `toHeight` (ex. racordul lavoarului la 0,55 m). */
export function addEndDrop(run: MepRun, end: 'start' | 'end', toHeight: number, ceiling: number): RiserResult {
  if (!okHeight(toHeight, ceiling)) return { ok: false, key: 'mepRiser.badHeight' };
  const h = r3(toHeight), p = end === 'start' ? run.points[0]! : run.points[run.points.length - 1]!; if (Math.abs(p[1] - h) < 1e-6) return { ok: false, key: 'mepRiser.same' };
  if (run.points.length + 1 > MEP_LIMITS.maxPoints) return { ok: false, key: 'mepRiser.tooMany' };
  const q: P3 = [p[0], h, p[2]], points = end === 'start' ? [q, ...run.points.map(x => [...x] as P3)] : [...run.points.map(x => [...x] as P3), q];
  return { ok: true, run: { ...run, points } };
}
/** Segmentele verticale ale traseului (indexul punctului de jos/sus și înălțimea), pentru inspector. */
export function risers(run: Pick<MepRun, 'points'>): { from: number; to: number; dh: number }[] {
  const out: { from: number; to: number; dh: number }[] = [];
  for (let i = 1; i < run.points.length; i++){ const a = run.points[i - 1]!, b = run.points[i]!; if (Math.abs(a[0] - b[0]) < 1e-6 && Math.abs(a[2] - b[2]) < 1e-6 && Math.abs(a[1] - b[1]) >= 1e-6) out.push({ from: i - 1, to: i, dh: r3(b[1] - a[1]) }); }
  return out;
}
// un punct identic cu precedentul (după ce restul traseului a primit cota nouă) nu adaugă nimic
function dedupe(ps: P3[]): P3[] { const out: P3[] = []; for (const p of ps){ const q = out[out.length - 1]; if (q && q[0] === p[0] && q[1] === p[1] && q[2] === p[2]) continue; out.push(p); } return out; }
