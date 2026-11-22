// Trasee de instalații și fundații (docs/bim/GHID_BIM.md §3.4–3.5, Faza 4). Reprezentare și verificare geometrică, fără
// dimensionare: diametrul, materialul și panta sunt cele declarate de utilizator; lipsa lor rămâne „necunoscut” (art. 4 și 12).
// Punctele traseului sunt [x, y, z] în metri: x/z pe plan, y = cota de la pardoseala nivelului. Ordinea punctelor = sensul
// curgerii (la canalizare: de la obiectul sanitar spre coloană).
import type { Floor, MepRun, MepSystem, Footing } from './types';
import { PlanLimitError, PLAN_LIMITS } from './plan-limits';
import type { Discipline } from './elements';
import { r3 } from './geometry';

export const MEP_SYSTEMS: Record<MepSystem, { discipline: Discipline; color: string; label: { ro: string; en: string } }> = {
  water_cold: { discipline: 'plumbing', color: '#2f6fd0', label: { ro: 'Apă rece', en: 'Cold water' } },
  water_hot: { discipline: 'plumbing', color: '#d0402f', label: { ro: 'Apă caldă', en: 'Hot water' } },
  drain: { discipline: 'plumbing', color: '#7a5c32', label: { ro: 'Canalizare', en: 'Drainage' } },
  hvac_supply: { discipline: 'mechanical', color: '#2a9d8f', label: { ro: 'Ventilație — tur', en: 'Ventilation — supply' } },
  hvac_return: { discipline: 'mechanical', color: '#8ab17d', label: { ro: 'Ventilație — retur', en: 'Ventilation — return' } },
  refrigerant: { discipline: 'mechanical', color: '#6a4c93', label: { ro: 'Agent frigorific (AC)', en: 'Refrigerant (AC)' } },
  electric: { discipline: 'electrical', color: '#e9a23b', label: { ro: 'Electric (tub / jgheab)', en: 'Electrical (conduit / tray)' } },
};
export const MEP_LIMITS = { maxRuns: 300, maxPoints: 50, minDiameterMm: 6, maxDiameterMm: 800, maxSlopePct: 15, materialMax: 60,
  footing: { max: 200, minM: .1, maxM: 10, depthMinM: .1, depthMaxM: 5 } } as const;
export const FOOTING_KINDS = ['pad', 'strip', 'raft'] as const;

type P3 = [number, number, number];
/** id-ul unui traseu sau al unei fundații: litere, cifre, -, _, :, . (ca la celelalte elemente, fără marcaj HTML) */
export const ID_RE = /^[\w:.-]{1,64}$/;
const bad = (m: string): never => { throw new PlanLimitError(m); };
const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const short = (v: unknown, max: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f<>]/g, '').replace(/\s+/g, ' ').trim().slice(0, max) : '';

/** Propunere de traseu ortogonal: urcă/coboară din A la cota de traseu, merge pe X, apoi pe Z, coboară/urcă în B.
 *  Cota (`routeY`) o alege utilizatorul (sub tavan, în șapă, în perete); propunerea nu se aplică fără aprobarea lui. */
export function proposeRun(a: { x: number; z: number; height: number }, b: { x: number; z: number; height: number }, routeY: number): P3[] {
  const raw: P3[] = [[a.x, a.height, a.z], [a.x, routeY, a.z], [b.x, routeY, a.z], [b.x, routeY, b.z], [b.x, b.height, b.z]].map(p => p.map(r3) as P3);
  const out: P3[] = [];
  for (const p of raw) {
    const q = out[out.length - 1]; if (q && q[0] === p[0] && q[1] === p[1] && q[2] === p[2]) continue;
    const o = out[out.length - 2];
    if (o && q) { const d1 = [q[0] - o[0], q[1] - o[1], q[2] - o[2]], d2 = [p[0] - q[0], p[1] - q[1], p[2] - q[2]];
      const cx = d1[1]! * d2[2]! - d1[2]! * d2[1]!, cy = d1[2]! * d2[0]! - d1[0]! * d2[2]!, cz = d1[0]! * d2[1]! - d1[1]! * d2[0]!;
      if (Math.hypot(cx, cy, cz) < 1e-9 && d1[0]! * d2[0]! + d1[1]! * d2[1]! + d1[2]! * d2[2]! > 0) { out[out.length - 1] = p; continue; } }
    out.push(p);
  }
  return out;
}

export function runLength(r: Pick<MepRun, 'points'>): number {
  let L = 0; for (let i = 1; i < r.points.length; i++) { const a = r.points[i - 1]!, b = r.points[i]!; L += Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]); }
  return r3(L);
}

/** Validarea unui traseu salvat: doar câmpurile modelului; geometria în limitele planului și sub tavanul nivelului. */
export function sanitizeMepRun(r: any, ceiling: number): MepRun {
  if (!r || typeof r !== 'object' || typeof r.id !== 'string' || !ID_RE.test(r.id)) bad('Traseul de instalații nu are un id valid.');
  if (!Object.prototype.hasOwnProperty.call(MEP_SYSTEMS, r.system)) bad('Sistemul traseului de instalații este invalid.');
  if (!Array.isArray(r.points) || r.points.length < 2 || r.points.length > MEP_LIMITS.maxPoints) bad(`Traseul trebuie să aibă între 2 și ${MEP_LIMITS.maxPoints} puncte.`);
  const points = (r.points as unknown[]).map(p => {
    if (!Array.isArray(p) || p.length !== 3 || !p.every(num) || Math.abs(p[0]) > PLAN_LIMITS.coordM || Math.abs(p[2]) > PLAN_LIMITS.coordM) bad('Un punct al traseului este invalid.');
    const q = p as P3; if (q[1] < -1 || q[1] > ceiling + 1) bad('Cota unui punct al traseului este în afara nivelului (între −1 m și tavan + 1 m).');
    return [q[0], q[1], q[2]] as P3; });
  const out: MepRun = { id: r.id, system: r.system, points };
  if (r.diameterMm != null) { if (!num(r.diameterMm) || r.diameterMm < MEP_LIMITS.minDiameterMm || r.diameterMm > MEP_LIMITS.maxDiameterMm) bad(`Diametrul traseului trebuie să fie între ${MEP_LIMITS.minDiameterMm} și ${MEP_LIMITS.maxDiameterMm} mm.`); out.diameterMm = r.diameterMm; }
  const material = short(r.material, MEP_LIMITS.materialMax); if (material) out.material = material;
  if (r.slopePct != null) { if (!num(r.slopePct) || r.slopePct < 0 || r.slopePct > MEP_LIMITS.maxSlopePct) bad(`Panta traseului trebuie să fie între 0 și ${MEP_LIMITS.maxSlopePct}%.`); out.slopePct = r.slopePct; }
  for (const k of ['fromId', 'toId'] as const) { const v = short(r[k], 64); if (v) out[k] = v; }
  return out;
}

export function sanitizeFooting(f: any): Footing {
  const L = MEP_LIMITS.footing;
  const ok = f && typeof f === 'object' && typeof f.id === 'string' && ID_RE.test(f.id) && num(f.x) && num(f.z) && Math.abs(f.x) <= PLAN_LIMITS.coordM && Math.abs(f.z) <= PLAN_LIMITS.coordM
    && num(f.w) && f.w >= L.minM && f.w <= L.maxM && num(f.d) && f.d >= L.minM && f.d <= L.maxM && num(f.depth) && f.depth >= L.depthMinM && f.depth <= L.depthMaxM
    && (f.kind == null || (FOOTING_KINDS as readonly string[]).includes(f.kind));
  if (!ok) bad(`Fundația este invalidă (laturi ${L.minM}–${L.maxM} m, adâncime ${L.depthMinM}–${L.depthMaxM} m).`);
  return { id: f.id, x: f.x, z: f.z, w: f.w, d: f.d, depth: f.depth, ...(f.kind ? { kind: f.kind } : {}) };
}

/** Traseele și fundațiile unui nivel, curățate pe loc (apelat din sanitizeFloorElements). */
export function sanitizeFloorMep(f: Floor & Record<string, any>, ceiling: number): void {
  const uniq = <T extends { id: string }>(xs: T[], what: string) => { const s = new Set<string>(); for (const x of xs) { if (s.has(x.id)) bad(`Două ${what} au același id.`); s.add(x.id); } return xs; };
  if (f.mepRuns != null) { if (!Array.isArray(f.mepRuns) || f.mepRuns.length > MEP_LIMITS.maxRuns) bad(`Traseele de instalații: cel mult ${MEP_LIMITS.maxRuns} pe nivel.`);
    const v = uniq(f.mepRuns.map(r => sanitizeMepRun(r, ceiling)), 'trasee de instalații'); if (v.length) f.mepRuns = v; else delete f.mepRuns; } else delete f.mepRuns;
  if (f.footings != null) { if (!Array.isArray(f.footings) || f.footings.length > MEP_LIMITS.footing.max) bad(`Fundațiile: cel mult ${MEP_LIMITS.footing.max} pe nivel.`);
    const v = uniq(f.footings.map(sanitizeFooting), 'fundații'); if (v.length) f.footings = v; else delete f.footings; } else delete f.footings;
  for (const w of f.walls || []) { const x = (w as any).structural; if (x == null || x === false) delete (w as any).structural; else if (x !== true) bad('Marcajul de perete structural este invalid.'); }
}

/** `message` = textul românesc (raportul de coordonare); `key` + `vars` = același text în limba interfeței (lib/locales, mep.issue.*). */
export interface MepIssue { code: 'MEP_DIAMETER_UNKNOWN' | 'MEP_DRAIN_SLOPE_UNDECLARED' | 'MEP_DRAIN_SLOPE_REVERSED'; severity: 'WARNING' | 'UNKNOWN'; refs: string[]; message: string; key: string; vars: Record<string, string | number> }

/** Ce lipsește sau contrazice geometria. Nu verifică normative (diametre minime, pante minime): acelea țin de proiectantul de instalații. */
export function mepIssues(f: Floor): MepIssue[] {
  const out: MepIssue[] = [];
  for (const r of f.mepRuns ?? []) {
    if (r.diameterMm == null) out.push({ code: 'MEP_DIAMETER_UNKNOWN', severity: 'UNKNOWN', refs: [r.id], key: 'mep.issue.diameterUnknown', vars: { id: r.id, system: r.system }, message: `Traseul ${r.id} (${MEP_SYSTEMS[r.system].label.ro}) nu are diametrul declarat; verificarea interferențelor îl tratează ca pe o linie.` });
    if (r.system !== 'drain') continue;
    const first = r.points[0]!, last = r.points[r.points.length - 1]!;
    if (last[1] > first[1] + 1e-6) out.push({ code: 'MEP_DRAIN_SLOPE_REVERSED', severity: 'WARNING', refs: [r.id], key: 'mep.issue.drainReversed', vars: { id: r.id, from: first[1], to: last[1] }, message: `Canalizarea ${r.id} urcă în sensul curgerii (de la ${first[1]} m la ${last[1]} m).` });
    else if (r.slopePct == null) out.push({ code: 'MEP_DRAIN_SLOPE_UNDECLARED', severity: 'WARNING', refs: [r.id], key: 'mep.issue.drainSlope', vars: { id: r.id }, message: `Canalizarea ${r.id} nu are panta declarată.` });
  }
  return out;
}
