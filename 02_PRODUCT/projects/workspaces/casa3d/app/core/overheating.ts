// Vara: riscul de supraîncălzire pe cameră, fără simulare termică. România nu are un prag numeric pentru câștigul solar prin
// ferestre (C 107/7 cere doar măsuri calitative pentru vitraje), deci folosim singura metodă oficială fără simulare găsită:
// metoda simplificată din Approved Document O (Anglia, 2021) — aria de sticlă ca procent din aria pardoselii, după orientarea
// dominantă, cu tabele separate pentru locuințele cu și fără ventilare transversală — plus cerința AD O de protecție
// solară pentru vitrajele dintre nord-est și nord-vest prin sud. Coloana „risc ridicat” e implicită pentru România: iulie
// la București e mai cald decât la Londra. Aceasta e o inferență a noastră, nu o echivalare oficială.
// Factorii Fc (DIN 4108-2): jaluzea exterioară ~0,25, marchiză ~0,5, protecție interioară 0,75–0,9; pragul german pentru o
// protecție exterioară eficientă e Fc ≤ 0,3. Pentru ruloul exterior nu există o valoare tabelară găsită: îl socotim la prag.
// Prețurile sunt cele dintr-o singură sursă comercială (brig.ro, nedatată); unde nu avem sursă, prețul e necunoscut.
import type { Floor, Room } from './types';
import { windowsOf, WINDOW_SILL, WINDOW_HEIGHT } from './insolation';
import { roomArea } from './room-geometry';

export type ShadeKind = 'roller_pvc' | 'roller_al' | 'roller_al_motor' | 'raffstore' | 'awning';
export type GlazingKind = 'double' | 'triple' | 'solar';
export interface ShadeSpec { fc: number; /** lei/m² (min–max) și lei pe bucată în plus (motor), din sursa citată; null = fără sursă */ priceM2: [number, number] | null; perPiece?: [number, number]; source: string | null; fcNote: 'table' | 'threshold' }
export const SHADES: Record<ShadeKind, ShadeSpec> = {
  roller_pvc: { fc: .3, priceM2: [280, 400], source: 'brig.ro', fcNote: 'threshold' },
  roller_al: { fc: .3, priceM2: [450, 620], source: 'brig.ro', fcNote: 'threshold' },
  roller_al_motor: { fc: .3, priceM2: [450, 620], perPiece: [650, 800], source: 'brig.ro', fcNote: 'threshold' },
  raffstore: { fc: .25, priceM2: null, source: null, fcNote: 'table' },
  awning: { fc: .5, priceM2: null, source: null, fcNote: 'table' },
};
/** Factorul solar g al geamului (valori uzuale, NEVERIFICAT pe produs: dublu low-e ~0,6, triplu ~0,5, control solar 0,3–0,4). */
export const GLAZING: Record<GlazingKind, number> = { double: .6, triple: .5, solar: .35 };
export const EFFECTIVE_FC = .3, SOLAR_GLASS_G = .4;
/** Partea de sticlă din golul ferestrei (rama lipsește din model): 0,7–0,75, NEVERIFICAT. */
export const GLASS_FRACTION = .72;
type Orient = 'N' | 'E' | 'S' | 'W';
/** AD O, metoda simplificată: aria maximă de sticlă (% din pardoseala camerei), după orientarea dominantă. */
export const ADO_LIMITS: Record<'cross' | 'single', Record<'high' | 'moderate', Record<Orient, number>>> = {
  cross: { high: { N: 37, E: 37, S: 22, W: 37 }, moderate: { N: 37, E: 37, S: 30, W: 22 } },
  single: { high: { N: 26, E: 18, S: 11, W: 18 }, moderate: { N: 26, E: 26, S: 15, W: 11 } },
};
/** Camerele în care stau oamenii (AD O privește încăperile ocupate, nu băile și holurile). */
export const OCCUPIED = new Set(['living', 'dormitor', 'bucatarie', 'birou']);

export function checkShading(shades: unknown, glazing: unknown): { shades?: Record<string, ShadeKind>; glazing?: GlazingKind } {
  const out: { shades?: Record<string, ShadeKind>; glazing?: GlazingKind } = {};
  if (glazing != null){ if (typeof glazing !== 'string' || !Object.hasOwn(GLAZING, glazing)) throw new Error('Tipul de geam e necunoscut.'); out.glazing = glazing as GlazingKind; }
  if (shades != null){ if (typeof shades !== 'object' || Array.isArray(shades)) throw new Error('Protecțiile solare sunt invalide.');
    const e = Object.entries(shades as Record<string, unknown>); if (e.length > 400) throw new Error('Prea multe protecții solare.');
    const m: Record<string, ShadeKind> = {};
    for (const [k, v] of e){ if (!/^[\w-]{1,60}$/.test(k) || typeof v !== 'string' || !Object.hasOwn(SHADES, v)) throw new Error('Protecție solară necunoscută.'); m[k] = v as ShadeKind; }
    if (Object.keys(m).length) out.shades = m; }
  return out;
}
/** Azimutul adevărat (0 = nord, în sensul acelor de ceasornic) al normalei spre exterior. */
export const trueAz = (n: [number, number], northDeg: number) => ((Math.atan2(n[0], -n[1]) * 180 / Math.PI + northDeg) % 360 + 360) % 360;
export const orient4 = (az: number): Orient => (['N', 'E', 'S', 'W'] as const)[Math.floor(((az + 45) % 360) / 90)]!;
/** Vitrajele dintre nord-est și nord-vest prin sud (45°–315°) au nevoie de protecție solară în zonele cu risc ridicat (AD O 1.9). */
export const exposed = (az: number) => az > 45 + 1e-9 && az < 315 - 1e-9;
/** Factorul solar efectiv al unei ferestre: g × Fc. */
export const effectiveG = (glazing: GlazingKind | undefined, shade: ShadeKind | undefined) => (glazing && Object.hasOwn(GLAZING, glazing) ? GLAZING[glazing] : GLAZING.double) * (shade && Object.hasOwn(SHADES, shade) ? SHADES[shade].fc : 1);
/** Protecția ferestrei, citită fără să atingă prototipul (un gol cu id-ul „constructor” nu are protecție). */
export const shadeOf = (shades: Record<string, ShadeKind> | undefined, id: string): ShadeKind | null => shades && Object.hasOwn(shades, id) && Object.hasOwn(SHADES, shades[id]!) ? shades[id]! : null;

export interface WindowSummer { wallId: string; openingId: string; roomId: string; az: number; glassM2: number; openingM2: number; shade: ShadeKind | null; exposed: boolean; protected: boolean }
export interface RoomSummer { roomId: string; name: string; floorM2: number; glassM2: number; pct: number; orient: Orient | null; cross: boolean; limitPct: number | null;
  verdict: 'ok' | 'over' | 'na'; /** ferestre expuse fără protecție eficientă (Fc ≤ 0,3 sau geam cu g ≤ 0,4) */ unshaded: string[] }
/** Ventilarea transversală a nivelului: ferestre exterioare pe fațade aproape opuse (≥ 135° între normale). Detecție automată, aproximativă. */
export function crossVentilated(fl: Floor): boolean {
  const n = windowsOf(fl).map(w => w.nOut);
  return n.some((a, i) => n.slice(i + 1).some(b => a[0] * b[0] + a[1] * b[1] <= Math.cos(135 * Math.PI / 180) + 1e-9));
}
export function summerCheck(fl: Floor, s: { northDeg: number; shades?: Record<string, ShadeKind>; glazing?: GlazingKind }, opts: { risk?: 'high' | 'moderate'; cross?: boolean } = {}): { windows: WindowSummer[]; rooms: RoomSummer[]; cross: boolean } {
  const H = fl.ceilingHeight, cross = opts.cross ?? crossVentilated(fl), risk = opts.risk ?? 'high', solarGlass = effectiveG(s.glazing, undefined) <= SOLAR_GLASS_G + 1e-9;
  const windows: WindowSummer[] = windowsOf(fl).map(({ wall, o, room, nOut }) => {
    const sill = o.sill ?? WINDOW_SILL, top = Math.min(H - .05, sill + (o.height ?? WINDOW_HEIGHT)), area = o.width * Math.max(0, top - sill), az = trueAz(nOut, s.northDeg);
    const shade = shadeOf(s.shades, o.id), ok = solarGlass || (!!shade && SHADES[shade].fc <= EFFECTIVE_FC + 1e-9);
    return { wallId: wall.id, openingId: o.id, roomId: room.id, az: Math.round(az), openingM2: r2(area), glassM2: r2(area * GLASS_FRACTION), shade, exposed: exposed(az), protected: ok }; });
  const rooms: RoomSummer[] = fl.rooms.filter(r => !r.spaceKind || r.spaceKind === 'interior').map((r: Room) => {
    const ws = windows.filter(w => w.roomId === r.id), floorM2 = r2(roomArea(r)), glassM2 = r2(ws.reduce((a, w) => a + w.glassM2, 0)), pct = floorM2 ? Math.round(glassM2 / floorM2 * 1000) / 10 : 0;
    // orientarea dominantă: fațada cu cea mai multă sticlă
    const by = new Map<Orient, number>(); for (const w of ws){ const k = orient4(w.az); by.set(k, (by.get(k) ?? 0) + w.glassM2); }
    const orient = ws.length ? [...by.entries()].sort((a, b) => b[1] - a[1])[0]![0] : null, limitPct = orient ? ADO_LIMITS[cross ? 'cross' : 'single'][risk][orient] : null;
    const applies = OCCUPIED.has(r.type) && ws.length > 0;
    return { roomId: r.id, name: r.name, floorM2, glassM2, pct, orient, cross, limitPct, verdict: !applies ? 'na' : pct > limitPct! + 1e-9 ? 'over' : 'ok',
      unshaded: applies ? ws.filter(w => w.exposed && !w.protected).map(w => w.openingId) : [] }; });
  return { windows, rooms, cross };
}

export interface ShadeOption { kind: ShadeKind; fc: number; costLei: [number, number] | null; source: string | null; fcNote: ShadeSpec['fcNote']; /** kWh evitați într-o zi tipică de iulie */ kwhSaved: number; meets: boolean }
/** Variantele de umbrire pentru ferestrele expuse ale unei camere, cu costul (unde avem sursă) și căldura evitată.
 *  `gainOf(openingId)` = câștigul zilei tipice de vară al ferestrei fără protecție (kWh), din sun-effects. */
export function shadeOptions(room: RoomSummer, windows: WindowSummer[], gainOf: (openingId: string) => number): ShadeOption[] {
  const ws = windows.filter(w => room.unshaded.includes(w.openingId)); if (!ws.length) return [];
  const area = ws.reduce((a, w) => a + w.openingM2, 0), base = ws.reduce((a, w) => a + gainOf(w.openingId), 0);
  return (Object.keys(SHADES) as ShadeKind[]).map(kind => { const sp = SHADES[kind];
    const cost: [number, number] | null = sp.priceM2 ? [0, 1].map(i => Math.round(sp.priceM2![i]! * area + (sp.perPiece?.[i] ?? 0) * ws.length)) as [number, number] : null;
    return { kind, fc: sp.fc, costLei: cost, source: sp.source, fcNote: sp.fcNote, kwhSaved: Math.round(base * (1 - sp.fc) * 100) / 100, meets: sp.fc <= EFFECTIVE_FC + 1e-9 }; })
    .sort((a, b) => Number(b.meets) - Number(a.meets) || (a.costLei?.[0] ?? Infinity) - (b.costLei?.[0] ?? Infinity) || a.fc - b.fc);
}
const r2 = (v: number) => Math.round(v * 100) / 100;
