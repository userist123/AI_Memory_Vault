// Releveul de pe șantier: pe fiecare cameră se măsoară cele patru laturi și cele două diagonale (cm, cu telemetrul).
// Din ele se vede dacă laturile opuse sunt egale, dacă unghiurile sunt drepte (diagonale egale) și cât diferă de plan —
// înainte de mobilier pe comandă și placări. Planul nu se modifică singur: designerul decide.
import type { RoomRect } from './types';

export interface SurveyInput { n: number; e: number; s: number; w: number; d1?: number; d2?: number; note?: string }
export interface RoomSurvey extends SurveyInput { roomId: string; measuredAt: string }
export class SurveyError extends Error {}
const MIN = 10, MAX = 5000;
const cm = (v: unknown, req: boolean) => { if (v == null || v === ''){ if (req) throw new SurveyError('Completează toate laturile (cm).'); return undefined; }
  if (typeof v !== 'number' || !Number.isFinite(v) || v < MIN || v > MAX) throw new SurveyError(`Măsurătorile sunt în cm, între ${MIN} și ${MAX}.`); return Math.round(v * 10) / 10; };
export function checkSurvey(x: unknown): SurveyInput {
  const o = x as any; if (!o || typeof o !== 'object' || Array.isArray(o)) throw new SurveyError('Releveul nu e valid.');
  const out: SurveyInput = { n: cm(o.n, true)!, e: cm(o.e, true)!, s: cm(o.s, true)!, w: cm(o.w, true)! };
  const d1 = cm(o.d1, false), d2 = cm(o.d2, false); if ((d1 == null) !== (d2 == null)) throw new SurveyError('Măsoară ambele diagonale sau niciuna.');
  if (d1 != null) { out.d1 = d1; out.d2 = d2; }
  const note = typeof o.note === 'string' ? o.note.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').trim().slice(0, 500) : ''; if (note) out.note = note;
  return out;
}
export const SURVEY_TOL_CM = { ok: 1, warn: 3 };
export type SurveyLevel = 'ok' | 'warn' | 'bad';
const level = (d: number): SurveyLevel => d <= SURVEY_TOL_CM.ok ? 'ok' : d <= SURVEY_TOL_CM.warn ? 'warn' : 'bad';
const r1 = (v: number) => Math.round(v * 10) / 10;
/** Analiza: diferențe între laturile opuse, între diagonale (și unghiul rezultat), față de plan. */
export function analyzeSurvey(rect: RoomRect, m: SurveyInput){
  const planW = r1((rect.x1 - rect.x0) * 100), planD = r1((rect.z1 - rect.z0) * 100);
  const width = r1((m.n + m.s) / 2), depth = r1((m.e + m.w) / 2), opposite = { ns: r1(Math.abs(m.n - m.s)), ew: r1(Math.abs(m.e - m.w)) };
  let diagDiff: number | null = null, angleOffDeg: number | null = null;
  if (m.d1 != null && m.d2 != null){ diagDiff = r1(Math.abs(m.d1 - m.d2)); const c = (m.d1 ** 2 - m.d2 ** 2) / (4 * width * depth);
    angleOffDeg = Math.round(Math.abs(90 - Math.acos(Math.max(-1, Math.min(1, c))) * 180 / Math.PI) * 100) / 100; }
  const vsPlan = { width: r1(width - planW), depth: r1(depth - planD) };
  const worst = Math.max(opposite.ns, opposite.ew, diagDiff ?? 0, Math.abs(vsPlan.width), Math.abs(vsPlan.depth));
  return { width, depth, planW, planD, opposite, diagDiff, angleOffDeg, vsPlan, level: level(worst), squareLevel: level(Math.max(opposite.ns, opposite.ew, diagDiff ?? 0)) };
}
