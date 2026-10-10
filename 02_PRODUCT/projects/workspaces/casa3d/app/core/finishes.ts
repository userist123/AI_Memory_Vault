// Finisaje de designer: modul de așezare a pardoselii, placările pe pereți și tavanele false.
// Funcții pure: cantitățile (BOQ), avertismentele tehnice, panoul din editor și motorul 3D citesc de aici.
// Regulile tehnice și pierderile sunt ipoteze de planificare cu sursă (vezi FINISH_RULES), nu date de catalog.
import type { Floor, FloorLayout, FloorPattern, Material, MaterialsCatalog, Room, RoomFinishes, Snapshot, WallFeature, WallFeatureKind } from './types';
import { openingsOnSide } from './validate';

const r2 = (v: number) => Math.round(v * 100) / 100;
export const SIDES = ['N', 'E', 'S', 'W'] as const;
export const PATTERNS: FloorPattern[] = ['straight', 'brick', 'third', 'diagonal', 'herringbone', 'chevron', 'checker'];

/** Pierderi la tăiere după modul de așezare (fracție din suprafața netă). Sursa: ghidurile de calcul citate în raportul de cercetare
 *  (plăci drepte ~10 %, diagonală și spic 15 %, chevron 15–20 %); formatele mari (latura ≥ 100 cm) mai adaugă 5 %. */
export const PATTERN_WASTE: Record<FloorPattern, number> = { straight: .10, brick: .10, third: .10, checker: .10, diagonal: .15, herringbone: .15, chevron: .18 };
export const LARGE_FORMAT_CM = 100, LARGE_FORMAT_EXTRA = .05;

/** Reguli tehnice folosite de avertismente; fiecare are sursa în raportul de cercetare din 2026-10-10. */
export const FINISH_RULES = {
  minGroutRectifiedMm: 3,       // plăci rectificate
  minGroutMm: 5,                // plăci nerectificate (≈ 3/16")
  bathMinSlip: 'R10' as const,  // pardoseală în baie
  bathMinIp: 44,                // corpuri de iluminat în zonele 1–2 ale băii (IEC 60364-7-701)
  minCeilingM: 2.4,             // înălțimea liberă sub un tavan fals
  maxDropCm: 60, maxCoveCm: 60,
};
const SLIP_RANK = { R9: 9, R10: 10, R11: 11, R12: 12, R13: 13 } as const;
const WET = new Set(['baie']);

export function materialOf(mc: MaterialsCatalog, id?: string | null): Material | undefined { return id ? mc.materials.find(m => m.id === id) : undefined; }
/** Mărimea unei plăci sau a unei lamele în cm [lungime, lățime]; pentru parchet fără date, o lamelă tipică de 129 × 19 cm. */
export function pieceSizeCm(m: Material | undefined): [number, number] {
  const s = m?.specs?.sizeCm; if (s) return [Math.max(s[0], s[1]), Math.min(s[0], s[1])];
  return m?.category === 'parquet' ? [129, 19] : [60, 60];
}
/** Așezarea implicită: parchetul „decalat cu o treime”, parchetul de spic în spic, plăcile drept cu rost de 3 mm (rectificate) sau 5 mm. */
export function defaultLayout(m: Material | undefined): FloorLayout {
  if (m?.category === 'parquet') return { pattern: m.specs?.patterns?.[0] === 'herringbone' ? 'herringbone' : 'third' };
  return { pattern: 'straight', groutMm: m?.specs?.rectified ? 3 : 5, groutColor: '#bdb8ae' };
}
export const layoutOf = (f: RoomFinishes, m: Material | undefined): FloorLayout => ({ ...defaultLayout(m), ...(f.floorLayout || {}) });
export function floorWaste(layout: FloorLayout, m: Material | undefined): number {
  const [L] = pieceSizeCm(m); return PATTERN_WASTE[layout.pattern] + (m?.category === 'floor_tile' && L >= LARGE_FORMAT_CM ? LARGE_FORMAT_EXTRA : 0);
}
/** Manopera pentru pardoseală după material și modul de așezare. */
export function floorLaborId(layout: FloorLayout, m: Material | undefined): string {
  if (m?.category !== 'floor_tile') return layout.pattern === 'herringbone' || layout.pattern === 'chevron' ? 'manopera-parchet-spic' : 'manopera-parchet';
  if (pieceSizeCm(m)[0] >= LARGE_FORMAT_CM) return 'manopera-gresie-mare';
  return layout.pattern === 'diagonal' || layout.pattern === 'herringbone' || layout.pattern === 'chevron' ? 'manopera-gresie-diagonal' : 'manopera-gresie';
}

// ---------- pereți ----------
/** Ce categorie de material se potrivește fiecărui tip de placare. */
export const FEATURE_CATEGORY: Record<WallFeatureKind, Material['category']> = { wallpaper: 'wallpaper', slats: 'wall_panel', plaster: 'decorative_plaster', brick: 'brick_cladding', stone: 'stone_cladding', tile: 'wall_tile' };
export const FEATURE_LABOR: Partial<Record<WallFeatureKind, string>> = { wallpaper: 'manopera-tapet', plaster: 'manopera-tencuiala-decorativa', tile: 'manopera-faianta' };
export interface SideGeometry { side: WallFeature['side']; lengthM: number; heightM: number; openingsM2: number; netM2: number }
/** Lungimea unei laturi a camerei și suprafața netă până la înălțimea `h` (ușile până la 2,1 m, ferestrele între 0,9 și 2,2 m). */
export function sideGeometry(fl: Floor, room: Room, side: WallFeature['side'], h?: number): SideGeometry {
  const r = room.rect, H = fl.ceilingHeight, top = Math.min(H, h ?? H), horiz = side === 'N' || side === 'S', lo = horiz ? r.x0 : r.z0, hi = horiz ? r.x1 : r.z1, len = hi - lo;
  let openings = 0;
  for (const o of openingsOnSide(fl, room, side)){ const w = Math.max(0, Math.min(o.b, hi) - Math.max(o.a, lo));
    openings += o.kind === 'door' ? w * Math.min(top, 2.1) : w * Math.max(0, Math.min(top, 2.2) - .9); }
  return { side, lengthM: r2(len), heightM: r2(top), openingsM2: r2(openings), netM2: r2(Math.max(0, len * top - openings)) };
}
/** Role de tapet: fâșii pe lungimea peretelui, câte fâșii întregi ies dintr-o rolă (înălțimea plus raportul modelului). */
export function wallpaperRolls(lengthM: number, heightM: number, roll: { widthM: number; lengthM: number; repeatCm: number }): { strips: number; perRoll: number; rolls: number } {
  const drop = heightM + roll.repeatCm / 100 + .05, perRoll = Math.max(1, Math.floor(roll.lengthM / drop)), strips = Math.ceil(lengthM / roll.widthM - 1e-9);
  return { strips, perRoll, rolls: Math.ceil(strips / perRoll) };
}
/** Panouri riflaj: coloane pe lungime × rânduri pe înălțime. */
export function panelCount(lengthM: number, heightM: number, sizeCm: [number, number]): number {
  const w = Math.min(sizeCm[0], sizeCm[1]) / 100, l = Math.max(sizeCm[0], sizeCm[1]) / 100;
  return Math.ceil(lengthM / w - 1e-9) * Math.ceil(heightM / l - 1e-9);
}

// ---------- tavan ----------
export function ceilingOf(f: RoomFinishes){ const c = f.ceiling || { type: 'flat' as const };
  return { type: c.type, dropCm: c.type === 'flat' ? 0 : Math.max(5, Math.min(FINISH_RULES.maxDropCm, c.dropCm ?? 10)), coveCm: c.type === 'cove' ? Math.max(10, Math.min(FINISH_RULES.maxCoveCm, c.coveCm ?? 25)) : 0,
    led: c.type === 'cove' ? c.led ?? 'banda-led-hoff-3000k' : null, cornice: c.cornice ?? null, spot: c.spot ?? null, spots: c.spot ? Math.max(0, Math.min(40, Math.round(c.spots ?? 4))) : 0 }; }
/** Înălțimea liberă a camerei sub tavanul fals. */
export const clearHeight = (fl: Floor, f: RoomFinishes) => r2(fl.ceilingHeight - ceilingOf(f).dropCm / 100);

// ---------- avertismente tehnice ----------
export interface FinishIssue { key: string; roomId: string; vars?: Record<string, string | number> }
export function finishIssues(snap: Snapshot, mc: MaterialsCatalog, fl: Floor, room: Room, f: RoomFinishes): FinishIssue[] {
  const out: FinishIssue[] = [], add = (key: string, vars?: Record<string, string | number>) => out.push({ key, roomId: room.id, ...(vars ? { vars } : {}) });
  const fm = materialOf(mc, f.floor), lay = layoutOf(f, fm), wet = WET.has(room.type);
  if (fm?.category === 'floor_tile'){
    const min = fm.specs?.rectified ? FINISH_RULES.minGroutRectifiedMm : FINISH_RULES.minGroutMm;
    if ((lay.groutMm ?? min) < min) add('fin.groutTooThin', { mm: lay.groutMm ?? 0, min });
    if (wet){ const s = fm.specs?.slip; if (!s) add('fin.slipUnknown', { min: FINISH_RULES.bathMinSlip }); else if (SLIP_RANK[s] < SLIP_RANK[FINISH_RULES.bathMinSlip]) add('fin.slipLow', { slip: s, min: FINISH_RULES.bathMinSlip }); }
  }
  if ((lay.pattern === 'herringbone' || lay.pattern === 'chevron') && fm?.category === 'parquet' && !fm.specs?.patterns?.includes(lay.pattern)) add('fin.patternProduct', { pattern: lay.pattern });
  if (fm?.category === 'parquet' && fm.specs?.patterns?.includes('herringbone') && lay.pattern !== 'herringbone') add('fin.herringboneOnly');
  for (const w of f.wallFeatures || []){ const m = materialOf(mc, w.material);
    if (!m) { add('fin.unknownMaterial', { side: w.side }); continue; }
    if (wet && m.specs?.wet === false) add('fin.notForWet', { side: w.side, name: m.name }); }
  const c = ceilingOf(f);
  if (c.type !== 'flat' && clearHeight(fl, f) < FINISH_RULES.minCeilingM) add('fin.ceilingLow', { h: clearHeight(fl, f), min: FINISH_RULES.minCeilingM });
  if (wet) for (const id of [c.spot, c.led, f.light]){ const m = materialOf(mc, id), ip = m?.specs?.ip ? Number(m.specs.ip.replace(/\D/g, '').slice(-1)) : null;
    if (m && m.specs?.ip && ip != null && ip < 4) add('fin.ipLow', { name: m.name, ip: m.specs.ip, min: `IP${FINISH_RULES.bathMinIp}` }); }
  if (f.baseboard && wet && materialOf(mc, f.baseboard)?.specs?.wet === false) add('fin.notForWet', { side: '-', name: materialOf(mc, f.baseboard)!.name });
  return out;
}
/** Toate avertismentele de finisaj din proiect, pe niveluri. */
export function projectFinishIssues(snap: Snapshot, mc: MaterialsCatalog, finishesOf: (s: Snapshot, r: Room) => RoomFinishes, floors: Floor[]): FinishIssue[] {
  return floors.flatMap(fl => fl.rooms.flatMap(r => finishIssues(snap, mc, fl, r, finishesOf(snap, r))));
}
/** Curăță și limitează ce vine de la client: tipuri cunoscute, numere în intervale rezonabile, culori hex. */
export function sanitizeFinishes(f: any): string | null {
  if (f == null) return null; if (typeof f !== 'object' || Array.isArray(f)) return 'Finisajele sunt invalide.';
  const hex = (v: unknown) => v === undefined || (typeof v === 'string' && /^#[0-9a-fA-F]{6}$/.test(v));
  const num = (v: unknown, lo: number, hi: number) => v === undefined || (typeof v === 'number' && Number.isFinite(v) && v >= lo && v <= hi);
  for (const rf of Object.values<any>(f)){ if (!rf || typeof rf !== 'object') return 'Finisajele sunt invalide.';
    const l = rf.floorLayout; if (l !== undefined && (typeof l !== 'object' || !PATTERNS.includes(l.pattern) || !num(l.groutMm, 0, 20) || !hex(l.groutColor) || ![undefined, 0, 90].includes(l.angle))) return 'Modul de așezare a pardoselii este invalid.';
    const wf = rf.wallFeatures; if (wf !== undefined && (!Array.isArray(wf) || wf.length > 8 || wf.some((w: any) => !w || !SIDES.includes(w.side) || !(w.kind in FEATURE_CATEGORY) || typeof w.material !== 'string' || w.material.length > 80 || !hex(w.color) || !num(w.heightM, .3, 10)))) return 'Placările de pe pereți sunt invalide.';
    const c = rf.ceiling; if (c !== undefined && (typeof c !== 'object' || !['flat', 'drop', 'cove'].includes(c.type) || !num(c.dropCm, 0, FINISH_RULES.maxDropCm) || !num(c.coveCm, 0, FINISH_RULES.maxCoveCm) || !num(c.spots, 0, 40)
      || [c.led, c.cornice, c.spot].some(x => x != null && (typeof x !== 'string' || x.length > 80)))) return 'Tavanul este invalid.'; }
  return null;
}
