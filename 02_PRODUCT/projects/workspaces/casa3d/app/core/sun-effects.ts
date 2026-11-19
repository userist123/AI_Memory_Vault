// Ce face soarele în casă, dincolo de ore: unde cade pe mobilier (orbire pe televizor, soare pe birou, soare pe pat
// devreme) și câtă căldură intră prin fiecare fereastră (câștig solar direct). Pur; folosește razele din insolation.ts.
//
// Câștigul solar: radiația directă pe cer senin după modelul Meinel (DNI = 1361 × 0,7^(AM^0,678), masa de aer după
// Kasten–Young), înmulțită cu cosinusul unghiului de incidență pe geam și cu partea geamului însorită (9 puncte pe geam,
// glaful umbrește), plus radiația difuză a cerului (izotropă, Liu–Jordan) și cea reflectată de sol (albedo 0,2), totul
// înmulțit cu factorul solar efectiv g × Fc (geamul proiectului și protecția exterioară, core/overheating.ts).
// „Cer senin” = limita de sus; „zi tipică” ≈ 0,6 × directă + difuză (durata de strălucire a soarelui în iulie la București,
// ~61–65 %, după surse climatice secundare; nevalidat cu PVGIS). Ordin de mărime pentru comparat ferestre, nu o factură.
import type { Catalog, Floor, FurniturePlacement, RoomRect } from './types';
import { seesSky, slabsOf, slabMap, windowsOf, WINDOW_SILL, WINDOW_HEIGHT } from './insolation';
import { sunPosition } from './solar';
import { sunVector, type SiteInfo } from './site-location';
import type { Obstacle } from './neighbours';
import { effectiveG, shadeOf } from './overheating';
import { footprintOf } from './validate';
import { roomArea } from './room-geometry';

type V3 = [number, number, number];
export const SUN_SHARE_TYPICAL = .6, ALBEDO = .2;
/** Piesele pentru care contează soarele direct: înălțimea suprafeței expuse (m) și ce înseamnă soarele pe ea. */
export const SUN_PIECES: Record<string, { h: number; effect: 'glare' | 'desk' | 'wake' }> = { comodaTv: { h: 1.0, effect: 'glare' }, birou: { h: .76, effect: 'desk' }, pat: { h: .55, effect: 'wake' } };

/** Pașii zilei cu soarele deasupra orizontului: ora locală (minute), direcția în plan, înălțimea (°). */
export function daySteps(site: SiteInfo, toUtc: (d: string, m: number) => Date, date: string, stepMin = 10){
  const out: { min: number; dir: V3; el: number; az: number }[] = [];
  for (let m = stepMin / 2; m < 1440; m += stepMin){ const s = sunPosition(toUtc(date, m), site.lat, site.lon); if (s.elevationDeg <= 0) continue;
    out.push({ min: m, dir: sunVector(s.azimuthDeg, s.elevationDeg, site) as V3, el: s.elevationDeg, az: s.azimuthDeg }); }
  return out;
}
const hhmm = (min: number) => { const m = ((Math.round(min) % 1440) + 1440) % 1440; return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`; };
/** Intervalele continue (pașii consecutivi) ale unei măști, ca „HH:MM–HH:MM”. */
export function spans(mins: number[], mask: boolean[], step: number): { from: string; to: string; minutes: number }[] {
  const out: { from: string; to: string; minutes: number }[] = []; let a = -1;
  for (let i = 0; i <= mask.length; i++){ const on = i < mask.length && mask[i]!, joined = on && a >= 0 && mins[i]! - mins[i - 1]! <= step + 1e-9;
    if (a >= 0 && !joined){ out.push({ from: hhmm(mins[a]! - step / 2), to: hhmm(mins[i - 1]! + step / 2), minutes: (i - a) * step }); a = -1; }
    if (on && a < 0) a = i; }
  return out;
}

export interface PieceSun { placementId: string; group: string; roomId: string; effect: 'glare' | 'desk' | 'wake'; minutes: number; spans: { from: string; to: string; minutes: number }[]; firstMin: number | null }
/** Soarele direct pe piesele sensibile, într-o zi: o piesă e „în soare” când oricare din 5 puncte ale suprafeței ei vede soarele. */
export function sunOnPieces(fl: Floor, placements: FurniturePlacement[], cat: Catalog, site: SiteInfo, toUtc: (d: string, m: number) => Date, date: string, stepMin = 10, obs?: readonly Obstacle[]): PieceSun[] {
  const steps = daySteps(site, toUtc, date, stepMin), slabs = slabsOf(fl), H = fl.ceilingHeight, mins = steps.map(s => s.min), out: PieceSun[] = [];
  for (const p of placements){ const spec = SUN_PIECES[p.group]; if (!spec || !fl.rooms.some(r => r.id === p.roomId)) continue;
    const fp: RoomRect | null = footprintOf(cat, p); if (!fp) continue;
    const ix = (fp.x1 - fp.x0) * .3, iz = (fp.z1 - fp.z0) * .3, cx = (fp.x0 + fp.x1) / 2, cz = (fp.z0 + fp.z1) / 2;
    const pts: V3[] = [[cx, spec.h, cz], [cx - ix, spec.h, cz - iz], [cx + ix, spec.h, cz - iz], [cx - ix, spec.h, cz + iz], [cx + ix, spec.h, cz + iz]];
    const mask = steps.map(s => pts.some(q => seesSky(q, s.dir, slabs, fl.rooms, H, undefined, obs)));
    const n = mask.filter(Boolean).length; if (!n) continue; const fi = mask.indexOf(true);
    out.push({ placementId: p.id, group: p.group, roomId: p.roomId, effect: spec.effect, minutes: n * stepMin, spans: spans(mins, mask, stepMin), firstMin: fi >= 0 ? mins[fi]! - stepMin / 2 : null }); }
  return out;
}

/** Radiația directă pe cer senin (W/m², pe normala la soare) la înălțimea dată (°). */
export function clearSkyDni(elevationDeg: number): number {
  if (elevationDeg <= 0) return 0; const z = 90 - elevationDeg;
  const am = 1 / (Math.cos(z * Math.PI / 180) + .50572 * Math.pow(96.07995 - z, -1.6364));
  return 1361 * Math.pow(.7, Math.pow(am, .678));
}
/** Radiația difuză pe orizontală, cer senin (Liu–Jordan): DHI = I0 · sin(h) · (0,271 − 0,294 · τb), τb = DNI / I0. */
export function clearSkyDhi(elevationDeg: number): number {
  if (elevationDeg <= 0) return 0; const tb = clearSkyDni(elevationDeg) / 1361;
  return Math.max(0, 1361 * Math.sin(elevationDeg * Math.PI / 180) * (.271 - .294 * tb));
}
export interface WindowGain { wallId: string; openingId: string; roomId: string; /** cer senin (limita de sus) și zi tipică, kWh */ kwh: number; kwhTypical: number; peakW: number;
  /** aceleași, fără protecția exterioară (pentru comparatorul de umbrire) */ kwhTypicalBare: number; /** puterea pe fiecare pas al zilei (W), cer senin */ series: number[] }
export interface RoomGain { roomId: string; kwh: number; kwhTypical: number; kwhPerM2: number; peakW: number }
/** Câștigul solar al fiecărei ferestre și al fiecărei camere într-o zi (cer senin și zi tipică). */
export function solarGain(fl: Floor, site: SiteInfo, toUtc: (d: string, m: number) => Date, date: string, stepMin = 10, obs?: readonly Obstacle[]): { windows: WindowGain[]; rooms: RoomGain[] } {
  const steps = daySteps(site, toUtc, date, stepMin), slabs = slabsOf(fl), byWall = slabMap(fl, slabs), H = fl.ceilingHeight, dt = stepMin / 60;
  const windows: WindowGain[] = windowsOf(fl).map(({ wall, o, room, nOut }) => {
    const L = Math.hypot(wall.b[0] - wall.a[0], wall.b[1] - wall.a[1]), ux = (wall.b[0] - wall.a[0]) / L, uz = (wall.b[1] - wall.a[1]) / L;
    const sill = o.sill ?? WINDOW_SILL, t = Math.min(H - .05, sill + (o.height ?? WINDOW_HEIGHT)), area = o.width * (t - sill);
    const g = effectiveG(site.glazing, shadeOf(site.shades, o.id) ?? undefined), g0 = effectiveG(site.glazing, undefined);
    const pts: V3[] = []; for (const fu of [.17, .5, .83]) for (const fy of [.17, .5, .83]){ const u = o.offset + o.width * fu; pts.push([wall.a[0] + ux * u, sill + (t - sill) * fy, wall.a[1] + uz * u]); }
    // pe fiecare pas: directa (doar cu soarele în fața geamului și partea însorită) și difuza + reflectata (W/m² pe geam)
    const parts = steps.map(s => { const cos = s.dir[0] * nOut[0] + s.dir[2] * nOut[1], dni = clearSkyDni(s.el), dhi = clearSkyDhi(s.el);
      const lit = cos > 0 ? pts.filter(q => seesSky(q, s.dir, slabs, fl.rooms, H, byWall.get(wall.id), obs)).length / pts.length : 0;
      // directa pe geam și reflexia ei din sol scad într-o zi cu nori; difuza cerului și reflexia ei rămân
      return { beam: (lit ? dni * cos * lit : 0) + dni * Math.sin(s.el * Math.PI / 180) * ALBEDO * .5, dif: dhi * .5 + dhi * ALBEDO * .5 }; });
    const series = parts.map(p => (p.beam + p.dif) * area * g);
    const wh = (k: number, gg: number) => parts.reduce((a, p) => a + (k * p.beam + p.dif) * area * gg * dt, 0);
    return { wallId: wall.id, openingId: o.id, roomId: room.id, kwh: kwh(wh(1, g)), kwhTypical: kwh(wh(SUN_SHARE_TYPICAL, g)), kwhTypicalBare: kwh(wh(SUN_SHARE_TYPICAL, g0)), peakW: Math.round(Math.max(0, ...series)), series }; });
  const rooms: RoomGain[] = fl.rooms.map(r => { const ws = windows.filter(w => w.roomId === r.id), sum = (f: (w: WindowGain) => number) => Math.round(ws.reduce((a, w) => a + f(w), 0) * 100) / 100;
    const k = sum(w => w.kwh), area = Math.max(.01, roomArea(r));
    // vârful camerei: cea mai mare sumă la același moment (ferestrele pe fețe diferite nu au vârful deodată)
    const peakW = Math.round(Math.max(0, ...steps.map((_, i) => ws.reduce((a, w) => a + w.series[i]!, 0))));
    return { roomId: r.id, kwh: k, kwhTypical: sum(w => w.kwhTypical), kwhPerM2: Math.round(k / area * 1000) / 1000, peakW }; });
  return { windows, rooms };
}
const kwh = (wh: number) => Math.round(wh / 10) / 100;
