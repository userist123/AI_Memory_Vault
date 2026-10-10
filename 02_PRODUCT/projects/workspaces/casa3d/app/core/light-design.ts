// Calculul de iluminat al unei camere, cu metoda fluxului luminos (estimare de proiectare, nu calcul fotometric):
// E [lx] = Σ flux [lm] × factor de utilizare × factor de menținere / suprafață. Banda LED din scafă luminează indirect (tavanul),
// deci contribuie doar cu o parte. Țintele de iluminare și temperaturile de culoare vin din ghidurile rezidențiale citate în
// FINISHES_RESEARCH_2026-10-10 (EN 12464-1 nu acoperă locuințele): living 100–300 lx, bucătărie 300–750, dormitor 100–200, baie 200–300.
import type { Floor, MaterialsCatalog, Room, RoomFinishes } from './types';
import { materialOf, ceilingOf } from './finishes';
import { LIGHTS_EXTRA_PER_M2 } from './rules.boq';

export const UTILIZATION = .5, MAINTENANCE = .8, COVE_SHARE = .5;
/** Ținta de iluminare pe tip de cameră (lx); camerele fără valoare în sursă nu primesc verificare. */
export const LUX_TARGET: Record<string, [number, number]> = { living: [100, 300], bucatarie: [300, 750], dormitor: [100, 200], baie: [200, 300] };
/** Temperatura de culoare recomandată (K). */
export const CCT_RANGE: Record<string, [number, number]> = { living: [2700, 3000], dormitor: [2700, 2700], bucatarie: [3000, 4000], baie: [3000, 4000] };

export interface LightSource { id: string; count: number; lumens: number | null; cctK: number | null; indirect: boolean }
export interface LightReport { area: number; sources: LightSource[]; lumens: number; unknownLumens: boolean; lux: number; target: [number, number] | null;
  ccts: number[]; spotsForTarget: number | null }

export const autoLightCount = (area: number) => 1 + Math.max(0, Math.ceil((area - LIGHTS_EXTRA_PER_M2) / LIGHTS_EXTRA_PER_M2));
export function lightReport(mc: MaterialsCatalog, _fl: Floor, room: Room, f: RoomFinishes): LightReport {
  const r = room.rect, area = (r.x1 - r.x0) * (r.z1 - r.z0), c = ceilingOf(f), sources: LightSource[] = [];
  const add = (id: string | null | undefined, count: number, indirect = false, perUnit?: number) => { const m = materialOf(mc, id); if (!m || count <= 0) return;
    const lm = perUnit ?? m.specs?.lumens ?? null; sources.push({ id: m.id, count, lumens: lm, cctK: m.specs?.cctK ?? null, indirect }); };
  add(f.light, f.lights ?? autoLightCount(area));
  add(c.spot, c.spots);
  if (c.type === 'cove' && c.led){ const m = materialOf(mc, c.led), len = 2 * (Math.max(0, r.x1 - r.x0 - 2 * c.coveCm / 100) + Math.max(0, r.z1 - r.z0 - 2 * c.coveCm / 100));
    // banda: lumenii pe metru = fluxul setului / lungimea lui
    if (m?.specs?.lumens && m.specs.pieceM) add(c.led, len, true, m.specs.lumens / m.specs.pieceM); else add(c.led, len, true); }
  const lumens = sources.reduce((a, s) => a + (s.lumens ?? 0) * s.count * (s.indirect ? COVE_SHARE : 1), 0);
  const lux = area > 0 ? Math.round(lumens * UTILIZATION * MAINTENANCE / area) : 0, target = LUX_TARGET[room.type] ?? null;
  const spot = materialOf(mc, c.spot) ?? mc.materials.find(m => m.category === 'spot'), spotLm = spot?.specs?.lumens;
  const missing = target ? Math.max(0, target[0] * area / (UTILIZATION * MAINTENANCE) - lumens) : 0;
  return { area: Math.round(area * 100) / 100, sources, lumens: Math.round(lumens), unknownLumens: sources.some(s => s.lumens == null), lux, target,
    ccts: [...new Set(sources.map(s => s.cctK).filter((k): k is number => k != null))].sort(), spotsForTarget: target && spotLm && missing > 0 ? Math.ceil(missing / spotLm) : null };
}
/** Avertismentele de iluminat: prea puțină lumină pentru tipul camerei, temperatură de culoare nepotrivită sau amestecată. */
export function lightIssues(mc: MaterialsCatalog, fl: Floor, room: Room, f: RoomFinishes): { key: string; roomId: string; vars?: Record<string, string | number> }[] {
  const rep = lightReport(mc, fl, room, f), out: { key: string; roomId: string; vars?: Record<string, string | number> }[] = [];
  if (rep.target && !rep.unknownLumens && rep.lux < rep.target[0]) out.push({ key: 'light.low', roomId: room.id, vars: { lux: rep.lux, min: rep.target[0], max: rep.target[1], ...(rep.spotsForTarget ? { n: rep.spotsForTarget } : { n: '-' }) } });
  const cr = CCT_RANGE[room.type];
  if (cr) for (const k of rep.ccts) if (k < cr[0] || k > cr[1]) out.push({ key: 'light.cct', roomId: room.id, vars: { k, lo: cr[0], hi: cr[1] } });
  if (rep.ccts.length > 1 && rep.ccts.at(-1)! - rep.ccts[0]! > 500) out.push({ key: 'light.cctMixed', roomId: room.id, vars: { list: rep.ccts.join(' / ') } });
  return out;
}
