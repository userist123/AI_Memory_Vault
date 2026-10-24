// Finisaje de designer: modul de așezare a pardoselii, placările pe pereți și tavanele false.
// Funcții pure: cantitățile (BOQ), avertismentele tehnice, panoul din editor și motorul 3D citesc de aici.
// Regulile tehnice și pierderile sunt ipoteze de planificare cu sursă (vezi FINISH_RULES), nu date de catalog.
import type { Floor, FloorLayout, FloorPattern, Material, MaterialsCatalog, PlasterEffect, Room, RoomFinishes, Snapshot, WallFeature, WallFeatureKind } from './types';
import { lightIssues } from './light-design';
import { textileIssues, sanitizeTextiles } from './textiles';
import { sanitizeKitchen } from './kitchen';
import { sanitizeBath, bathTileHeight } from './bath';
import { sanitizeFixtures } from './fixtures';
import { openingsOnSide } from './validate';
import { WALLPAPER_RULES } from './rules';

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
export const FEATURE_CATEGORY: Record<WallFeatureKind, Material['category']> = { wallpaper: 'wallpaper', slats: 'wall_panel', plaster: 'decorative_plaster', brick: 'brick_cladding', stone: 'stone_cladding', tile: 'wall_tile',
  paint: 'paint', panel: 'wall_panel', rail: 'moulding' };
export const FEATURE_LABOR: Partial<Record<WallFeatureKind, string>> = { wallpaper: 'manopera-tapet', plaster: 'manopera-tencuiala-decorativa', tile: 'manopera-faianta', paint: 'manopera-zugravit', rail: 'manopera-bagheta' };
/** Efectele de vopsea decorativă. `textured` = tencuiala structurată obișnuită (tariful existent); celelalte cer aplicator
 *  și mai multe straturi, deci au tarif propriu: până există un tarif cu sursă în catalog, manopera lor ajunge în „necunoscute”. */
export const PLASTER_EFFECTS: PlasterEffect[] = ['venetian', 'concrete', 'microcement', 'travertine', 'silk', 'textured'];
export const EFFECT_LABOR: Record<PlasterEffect, string> = { venetian: 'manopera-veneziana', concrete: 'manopera-efect-beton', microcement: 'manopera-microciment',
  travertine: 'manopera-travertin', silk: 'manopera-efect-matase', textured: 'manopera-tencuiala-decorativa' };
/** Efectul unei benzi de tencuială: cel declarat de produs (`specs.effect`), altfel cel ales pe bandă. */
export const effectOf = (w: Pick<WallFeature, 'kind' | 'effect'>, m?: Material): PlasterEffect | null => w.kind !== 'plaster' ? null : m?.specs?.effect ?? w.effect ?? null;
/** Tariful de manoperă al unei placări (la tencuială, după efect). */
export const featureLabor = (w: Pick<WallFeature, 'kind' | 'effect'>, m?: Material): string | undefined => { const e = effectOf(w, m); return e ? EFFECT_LABOR[e] : FEATURE_LABOR[w.kind]; };
/** Banda unei placări: de jos (`fromM`) până sus (`heightM` sau tavanul); bagheta e o linie la `heightM`. */
export const bandOf = (w: WallFeature, H: number): [number, number] => w.kind === 'rail' ? [Math.min(H, w.heightM ?? 1), Math.min(H, w.heightM ?? 1)] : [Math.max(0, Math.min(H, w.fromM ?? 0)), Math.min(H, w.heightM ?? H)];
/** Lungimea laturii camerei (dreptunghiul ei), pe care se măsoară porțiunea unei placări. */
export const sideSpan = (room: Pick<Room, 'rect'>, side: WallFeature['side']) => side === 'N' || side === 'S' ? room.rect.x1 - room.rect.x0 : room.rect.z1 - room.rect.z0;
/** Porțiunea pe lungime a unei placări, [de la, până la] în m de la colțul x0/z0, tăiată la latură; lipsă = toată latura. */
export const alongOf = (w: Pick<WallFeature, 'alongFromM' | 'alongToM'>, len: number): [number, number] => [Math.max(0, Math.min(len, w.alongFromM ?? 0)), Math.max(0, Math.min(len, w.alongToM ?? len))];
/** O placare pe o porțiune (nu pe toată latura). */
export const isPartial = (w: Pick<WallFeature, 'alongFromM' | 'alongToM'>) => w.alongFromM !== undefined || w.alongToM !== undefined;
/** Suprafața netă a unei benzi de perete (fără uși și ferestre) = net(sus) − net(jos), pe porțiunea ei de lungime. */
export function bandGeometry(fl: Floor, room: Room, w: WallFeature){ const [a, b] = bandOf(w, fl.ceilingHeight), along = isPartial(w) ? alongOf(w, sideSpan(room, w.side)) : undefined;
  const top = sideGeometry(fl, room, w.side, b, along), low = a > 0 ? sideGeometry(fl, room, w.side, a, along).netM2 : 0;
  return { lengthM: top.lengthM, fromM: a, toM: b, heightM: r2(b - a), netM2: r2(Math.max(0, top.netM2 - low)), along: along ?? null }; }
const spanOf = (w: WallFeature): [number, number] => [w.alongFromM ?? 0, w.alongToM ?? Infinity];
const cross = (a: [number, number], b: [number, number]) => Math.min(a[1], b[1]) - Math.max(a[0], b[0]) > 1e-6;
/** Benzile de pe aceeași latură nu au voie să se suprapună în elevație: pe înălțime ȘI pe lungime (bagheta poate sta pe granița
 *  dintre două benzi). Așa stau pe aceeași latură „riflaj 1,6–3,6 m” și „vopsea 0–1,6 m”. */
export function bandsOverlap(features: WallFeature[], H: number): boolean {
  for (const s of SIDES){ const bs = features.filter(w => w.side === s && w.kind !== 'rail');
    for (let i = 0; i < bs.length; i++) for (let j = i + 1; j < bs.length; j++) if (cross(bandOf(bs[i]!, H), bandOf(bs[j]!, H)) && cross(spanOf(bs[i]!), spanOf(bs[j]!))) return true; }
  return false; }
/** Adaugă o placare pe o porțiune: benzile existente de pe aceeași latură care o acoperă (pe înălțime și pe lungime) se taie în
 *  jurul ei (o vopsea pe toată latura devine două bucăți, stânga și dreapta), ca rezultatul să nu se suprapună. Bucățile sub 5 cm dispar. */
export function insertBand(features: WallFeature[], add: WallFeature, sideLen: number, H: number): WallFeature[] {
  H = Math.max(H, 10);   // aceeași convenție ca serverul (bandsOverlap pe 10 m): o bandă de deasupra unui tavan coborât se taie și ea
  const [a, b] = alongOf(add, sideLen), out: WallFeature[] = [];
  for (const w of features){ if (w.side !== add.side || w.kind === 'rail' || add.kind === 'rail' || !cross(bandOf(w, H), bandOf(add, H))){ out.push(w); continue; }
    const [c, d] = alongOf(w, sideLen); if (!cross([c, d], [a, b])){ out.push(w); continue; }
    const piece = (x: number, y: number): WallFeature => { const q: WallFeature = { ...w }; delete q.alongFromM; delete q.alongToM; return x <= 1e-6 && y >= sideLen - 1e-6 ? q : { ...q, alongFromM: r2(x), alongToM: r2(y) }; };
    if (a - c >= .05) out.push(piece(c, a)); if (d - b >= .05) out.push(piece(b, d)); }
  return [...out, add];
}
/** Ținta uneltei „Placare”: camera și latura cea mai apropiată de punct (fața interioară a unui perete real) și poziția pe
 *  lungime (m de la colțul x0/z0, rotunjită la 5 cm). Clicul poate fi în cameră sau pe grosimea peretelui, nu dincolo de el. */
export function cladTarget(fl: Floor, pt: [number, number], maxD = .4): { roomId: string; side: WallFeature['side']; along: number; len: number } | null {
  let best: { roomId: string; side: WallFeature['side']; along: number; len: number } | null = null, bd = Infinity;
  for (const room of fl.rooms){ const r = room.rect, inside = pt[0] > r.x0 && pt[0] < r.x1 && pt[1] > r.z0 && pt[1] < r.z1;
    for (const side of SIDES){ const horiz = side === 'N' || side === 'S', edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[side], inward = side === 'N' || side === 'W' ? 1 : -1;
      const len = sideSpan(room, side), u = horiz ? pt[0] - r.x0 : pt[1] - r.z0, depth = ((horiz ? pt[1] : pt[0]) - edge) * inward;
      if (u < -.05 || u > len + .05 || depth < -.2 || depth > maxD) continue;
      const score = Math.abs(depth) + (inside ? 0 : .05); if (score >= bd) continue;
      const at = Math.max(0, Math.min(len, u)); if (sideGeometry(fl, room, side, undefined, [Math.max(0, at - .05), Math.min(len, at + .05)]).lengthM <= 0) continue;
      bd = score; best = { roomId: room.id, side, along: r2(Math.max(0, Math.min(len, Math.round(at * 20) / 20))), len: r2(len) }; } }
  return best;
}
/** Banda unei placări în planul 2D: 4 cm pe fața interioară a peretelui, pe porțiunea ei (null dacă latura nu are perete). */
export function featurePlanRect(fl: Floor, room: Room, w: WallFeature, bandM = .04): { x0: number; z0: number; x1: number; z1: number } | null {
  const r = room.rect, horiz = w.side === 'N' || w.side === 'S', edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[w.side], inward = w.side === 'N' || w.side === 'W' ? 1 : -1, base = horiz ? r.x0 : r.z0;
  const [a, b] = alongOf(w, sideSpan(room, w.side)); if (b - a < .01) return null;
  const wall = fl.walls.find(x => { const h2 = Math.abs(x.a[1] - x.b[1]) < 1e-6, v2 = Math.abs(x.a[0] - x.b[0]) < 1e-6; if (horiz ? !(h2 && Math.abs(x.a[1] - edge) < 1e-6) : !(v2 && Math.abs(x.a[0] - edge) < 1e-6)) return false;
    const a0 = Math.min(horiz ? x.a[0] : x.a[1], horiz ? x.b[0] : x.b[1]), a1 = Math.max(horiz ? x.a[0] : x.a[1], horiz ? x.b[0] : x.b[1]); return Math.min(a1, base + b) - Math.max(a0, base + a) > .005; });
  if (!wall) return null; const f0 = edge + inward * wall.thickness / 2, f1 = f0 + inward * bandM;
  return horiz ? { x0: base + a, x1: base + b, z0: Math.min(f0, f1), z1: Math.max(f0, f1) } : { z0: base + a, z1: base + b, x0: Math.min(f0, f1), x1: Math.max(f0, f1) };
}
/** Marcajele placărilor pentru planul de finisaje: numărul fiecărei benzi în tipul ei, pe tot nivelul („R1”, „T1”, „V2”). Bagheta nu are marcaj. */
export function featureMarks(snap: Snapshot, fl: Floor, finishesOf: (s: Snapshot, r: Room) => RoomFinishes): { roomId: string; index: number; kind: WallFeatureKind; n: number }[] {
  const seen: Partial<Record<WallFeatureKind, number>> = {}, out: { roomId: string; index: number; kind: WallFeatureKind; n: number }[] = [];
  for (const room of fl.rooms) (finishesOf(snap, room).wallFeatures || []).forEach((w, index) => { if (w.kind === 'rail') return; const n = (seen[w.kind] ?? 0) + 1; seen[w.kind] = n; out.push({ roomId: room.id, index, kind: w.kind, n }); });
  return out;
}
export interface SideGeometry { side: WallFeature['side']; lengthM: number; heightM: number; openingsM2: number; netM2: number }
/** Lungimea unei laturi a camerei și suprafața netă până la înălțimea `h` (ușile până la 2,1 m, ferestrele între 0,9 și 2,2 m). */
export function sideGeometry(fl: Floor, room: Room, side: WallFeature['side'], h?: number, along?: [number, number]): SideGeometry {
  const r = room.rect, H = fl.ceilingHeight, top = Math.min(H, h ?? H), horiz = side === 'N' || side === 'S', base = horiz ? r.x0 : r.z0, edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[side];
  // porțiunea pe lungime (de la colțul x0/z0); golurile se scad doar din intersecția cu ea
  const lo = along ? base + along[0] : base, hi = along ? Math.min(base + along[1], horiz ? r.x1 : r.z1) : horiz ? r.x1 : r.z1;
  // doar lungimea acoperită de pereți reali (o latură deschisă spre altă cameră nu are ce placa)
  let len = 0;
  for (const w of fl.walls){ const hz = Math.abs(w.a[1] - w.b[1]) < 1e-6, vt = Math.abs(w.a[0] - w.b[0]) < 1e-6;
    if (horiz ? !(hz && Math.abs(w.a[1] - edge) < 1e-6) : !(vt && Math.abs(w.a[0] - edge) < 1e-6)) continue;
    const a0 = Math.min(horiz ? w.a[0] : w.a[1], horiz ? w.b[0] : w.b[1]), a1 = Math.max(horiz ? w.a[0] : w.a[1], horiz ? w.b[0] : w.b[1]);
    len += Math.max(0, Math.min(a1, hi) - Math.max(a0, lo)); }
  len = Math.max(0, Math.min(len, hi - lo));
  let openings = 0;
  for (const o of openingsOnSide(fl, room, side)){ const w = Math.max(0, Math.min(o.b, hi) - Math.max(o.a, lo));
    openings += o.kind === 'door' ? w * Math.min(top, 2.1) : w * Math.max(0, Math.min(top, 2.2) - .9); }
  return { side, lengthM: r2(len), heightM: r2(top), openingsM2: r2(openings), netM2: r2(Math.max(0, len * top - openings)) };
}
/** Role de tapet, pe fâșii: lungimea fâșiei = înălțimea + tăierea, rotunjită în sus la multiplu de raport (r); la potrivirea
 *  decalată (`offset`) se mai adaugă r/2. Fâșii = ⌈lungime / lățimea rolei⌉, fâșii pe rolă = ⌊lungimea rolei / fâșie⌋, role =
 *  ⌈fâșii / fâșii pe rolă⌉, plus rezerva (10 % drept sau liber, 15 % decalat), rotunjită în sus la rolă. Rolele se comandă din același lot.
 *  Potrivirea lipsă: liberă fără raport, dreaptă cu raport. */
export function wallpaperRolls(lengthM: number, heightM: number, roll: { widthM: number; lengthM: number; repeatCm: number; match?: 'free' | 'straight' | 'offset' }): { strips: number; perRoll: number; rolls: number; spare: number; dropM: number; match: 'free' | 'straight' | 'offset' } {
  const r = Math.max(0, roll.repeatCm) / 100, match = roll.match ?? (r > 0 ? 'straight' : 'free'), need = heightM + WALLPAPER_RULES.trimM;
  const dropM = r > 0 ? Math.round((Math.ceil(need / r - 1e-9) * r + (match === 'offset' ? r / 2 : 0)) * 1000) / 1000 : Math.round(need * 1000) / 1000;
  const perRoll = Math.max(1, Math.floor(roll.lengthM / dropM + 1e-9)), strips = Math.ceil(lengthM / roll.widthM - 1e-9), base = Math.ceil(strips / perRoll);
  const rolls = strips > 0 ? Math.ceil(base * (1 + WALLPAPER_RULES.spare[match]) - 1e-9) : 0;
  return { strips, perRoll, rolls, spare: rolls - base, dropM, match };
}
/** Panouri riflaj: coloane pe lungime × rânduri pe înălțime. */
export function panelCount(lengthM: number, heightM: number, sizeCm: [number, number]): number {
  const w = Math.min(sizeCm[0], sizeCm[1]) / 100, l = Math.max(sizeCm[0], sizeCm[1]) / 100, cols = Math.ceil(lengthM / w - 1e-9);
  // rânduri întregi + restul de sus tăiat din panouri: dintr-un panou ies floor(l / rest) bucăți de înălțimea restului
  const full = Math.floor(heightM / l + 1e-9), rest = heightM - full * l;
  return cols * full + (rest > 1e-6 ? Math.ceil(cols / Math.max(1, Math.floor(l / rest + 1e-9))) : 0);
}

// ---------- tavan ----------
export function ceilingOf(f: RoomFinishes){ const c = f.ceiling || { type: 'flat' as const };
  return { type: c.type, dropCm: c.type === 'flat' ? 0 : Math.max(5, Math.min(FINISH_RULES.maxDropCm, c.dropCm ?? 10)), coveCm: c.type === 'cove' ? Math.max(10, Math.min(FINISH_RULES.maxCoveCm, c.coveCm ?? 25)) : 0,
    led: c.type === 'cove' ? (c.led === undefined ? 'banda-led-hoff-3000k' : c.led) : null, cornice: c.cornice ?? null, spot: c.spot ?? null, spots: c.spot ? Math.max(0, Math.min(40, Math.round(c.spots ?? 4))) : 0 }; }
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
  for (const w of f.wallFeatures || []){ const m = materialOf(mc, w.material), eff = effectOf(w, m);
    // vopseaua decorativă cu efect: microcimentul într-o cameră umedă cere sistem hidroizolat; un efect fără produs declarat nu are preț
    if (eff === 'microcement' && wet && m?.specs?.wet !== true) add('fin.microcementWet', { side: w.side });
    if (eff && eff !== 'textured' && m?.specs?.effect !== eff){ add('fin.effectUnpriced', { side: w.side, effect: eff }); continue; }
    if (!m) { add('fin.unknownMaterial', { side: w.side }); continue; }
    if (wet && m.specs?.wet === false) add('fin.notForWet', { side: w.side, name: m.name }); }
  const c = ceilingOf(f);
  if (c.type !== 'flat' && clearHeight(fl, f) < FINISH_RULES.minCeilingM) add('fin.ceilingLow', { h: clearHeight(fl, f), min: FINISH_RULES.minCeilingM });
  if (wet) for (const id of [c.spot, c.led, f.light]){ const m = materialOf(mc, id), ip = m?.specs?.ip ? Number(m.specs.ip.replace(/\D/g, '').slice(-1)) : null;
    if (m && m.specs?.ip && ip != null && ip < 4) add('fin.ipLow', { name: m.name, ip: m.specs.ip, min: `IP${FINISH_RULES.bathMinIp}` }); }
  out.push(...lightIssues(mc, fl, room, f), ...textileIssues(mc, fl, room, f));
  if (f.baseboard && wet && materialOf(mc, f.baseboard)?.specs?.wet === false) add('fin.notForWet', { side: '-', name: materialOf(mc, f.baseboard)!.name });
  return out;
}
/** Toate avertismentele de finisaj din proiect, pe niveluri. */
export function projectFinishIssues(snap: Snapshot, mc: MaterialsCatalog, finishesOf: (s: Snapshot, r: Room) => RoomFinishes, floors: Floor[]): FinishIssue[] {
  return floors.flatMap(fl => fl.rooms.flatMap(r => finishIssues(snap, mc, fl, r, finishesOf(snap, r))));
}
/** Curăță și limitează ce vine de la client: tipuri cunoscute, numere în intervale rezonabile, culori hex. */
export function sanitizeFinishes(f: any): string | null {
  if (f == null) return null; if (typeof f !== 'object' || Array.isArray(f) || Object.keys(f).length > 400) return 'Finisajele sunt invalide.';
  const hex = (v: unknown) => v === undefined || (typeof v === 'string' && /^#[0-9a-fA-F]{6}$/.test(v));
  const num = (v: unknown, lo: number, hi: number) => v === undefined || (typeof v === 'number' && Number.isFinite(v) && v >= lo && v <= hi);
  for (const rf of Object.values<any>(f)){ if (!rf || typeof rf !== 'object') return 'Finisajele sunt invalide.';
    if ([rf.floor, rf.wallPaint, rf.wallTile, rf.baseboard, rf.light].some(x => x != null && (typeof x !== 'string' || x.length > 80))) return 'Finisajele sunt invalide.';
    const l = rf.floorLayout; if (l !== undefined && (l === null || typeof l !== 'object' || !PATTERNS.includes(l.pattern) || !num(l.groutMm, 0, 20) || !hex(l.groutColor) || ![undefined, 0, 90].includes(l.angle))) return 'Modul de așezare a pardoselii este invalid.';
    const wf = rf.wallFeatures; if (wf !== undefined && (!Array.isArray(wf) || wf.length > 24 || wf.some((w: any) => !w || !SIDES.includes(w.side) || !Object.hasOwn(FEATURE_CATEGORY, w.kind) || typeof w.material !== 'string' || w.material.length > 80 || !hex(w.color) || !num(w.heightM, .05, 10) || !num(w.fromM, 0, 10)
      || (w.fromM !== undefined && w.heightM !== undefined && w.kind !== 'rail' && w.heightM <= w.fromM)
      // porțiunea pe lungime: 0–1000 m (cât coordonatele planului), „de la” înaintea lui „până la”; lamelele doar la riflaj, efectul doar la tencuială
      || !num(w.alongFromM, 0, 1000) || !num(w.alongToM, 0, 1000) || (w.alongFromM !== undefined && w.alongToM !== undefined && w.alongToM <= w.alongFromM)
      || (w.vertical !== undefined && (typeof w.vertical !== 'boolean' || w.kind !== 'slats')) || (w.effect !== undefined && (!PLASTER_EFFECTS.includes(w.effect) || w.kind !== 'plaster'))) || bandsOverlap(wf, 10))) return 'Placările de pe pereți sunt invalide.';
    const c = rf.ceiling; if (c !== undefined && (c === null || typeof c !== 'object' || !['flat', 'drop', 'cove'].includes(c.type) || !num(c.dropCm, 0, FINISH_RULES.maxDropCm) || !num(c.coveCm, 0, FINISH_RULES.maxCoveCm) || !num(c.spots, 0, 40)
      || [c.led, c.cornice, c.spot].some(x => x != null && (typeof x !== 'string' || x.length > 80)))) return 'Tavanul este invalid.';
    const tx = sanitizeTextiles(rf) ?? sanitizeKitchen(rf.kitchen) ?? sanitizeBath(rf.bath) ?? sanitizeFixtures(rf.fixtures); if (tx) return tx; }
  return null;
}

// ---------- ce desenează motorul 3D ----------
/** Faianța băii (din `wallTile`), până la înălțimea aleasă (1,2 m / 2,1 m / tavan), pe porțiunile laturilor fără altă placare —
 *  la fel ca în BOQ. O placare pe toată latura scoate faianța de pe latură (ca înainte); una pe o porțiune (unealta „Placare”)
 *  o scoate doar de pe porțiunea ei, dacă știm camera (lungimea laturii); fără cameră, ca înainte: toată latura. */
export const bathTiles = (f: RoomFinishes, roomType: string, room?: Pick<Room, 'rect'>): WallFeature[] => { if (roomType !== 'baie' || !f.wallTile) return [];
  const h = bathTileHeight(f), base = (side: WallFeature['side']): WallFeature => ({ side, kind: 'tile', material: f.wallTile!, ...(h !== undefined ? { heightM: h } : {}) });
  // contează doar placările care acoperă faianța pe înălțime: nu un brâu și nu o bandă care începe deasupra ei (vopsea de la 2,2 m)
  return SIDES.flatMap(side => { const on = (f.wallFeatures || []).filter(w => w.side === side && w.kind !== 'rail' && (w.fromM ?? 0) < (h ?? Infinity) - 1e-6);
    if (!on.length) return [base(side)];
    if (!room || on.some(w => w.alongFromM === undefined && w.alongToM === undefined)) return [];
    const len = sideSpan(room, side), cut = on.map(w => alongOf(w, len)).sort((p, q) => p[0] - q[0]), out: WallFeature[] = []; let at = 0;
    for (const [a, b] of [...cut, [len, len] as [number, number]]){ if (r2(a - at) >= .05) out.push({ ...base(side), alongFromM: r2(at), alongToM: r2(a) }); at = Math.max(at, b); }
    return out.length === 1 && out[0]!.alongFromM === 0 && out[0]!.alongToM === r2(len) ? [base(side)] : out; }); };
/** Culoarea folosită doar la randare (aproximată după numele culorii de pe pagina produsului). */
export const renderColor = (m: Material | undefined, fallback: string) => m?.specs?.color || fallback;
export interface FloorVisual { kind: 'parquet' | 'tile'; pattern: FloorPattern; angle: 0 | 90; pieceL: number; pieceW: number; grout: number; groutColor: string; color: string; marble?: boolean }
/** `along` = porțiunea pe lungime (m de la colțul x0/z0), null = toată latura; `vertical` = lamelele riflajului (false = orizontale). */
export interface WallVisual { side: WallFeature['side']; kind: WallFeatureKind; color: string; fromM: number; heightM: number | null; sizeCm: [number, number] | null; marble: boolean;
  along: [number, number] | null; vertical: boolean; effect: PlasterEffect | null }
export interface CeilingVisual { type: 'flat' | 'drop' | 'cove'; drop: number; cove: number; led: string | null; cornice: [number, number] | null; spots: number }
/** Ce trebuie să deseneze motorul 3D pentru finisajele unei camere: dimensiuni în metri, culori, modul de așezare. */
export function roomVisual(mc: MaterialsCatalog, f: RoomFinishes, roomType = '', room?: Pick<Room, 'rect'>): { floor: FloorVisual | null; walls: WallVisual[]; ceiling: CeilingVisual } {
  const fm = materialOf(mc, f.floor), lay = layoutOf(f, fm), [L, W] = pieceSizeCm(fm), tile = fm?.category === 'floor_tile', c = ceilingOf(f);
  const led = materialOf(mc, c.led), cct = led?.specs?.cctK ?? 3000;
  return {
    floor: fm ? { kind: tile ? 'tile' : 'parquet', pattern: lay.pattern, angle: lay.angle ?? 0, pieceL: L / 100, pieceW: W / 100, grout: tile ? (lay.groutMm ?? 3) / 1000 : .0006,
      groutColor: tile ? lay.groutColor || '#bdb8ae' : '#3a2a1c', color: renderColor(fm, tile ? '#e6e4df' : '#b88a5a'), ...(tile && /marm|marble|onyx|calacatta|carrara/i.test(fm.name) ? { marble: true } : {}) } : null,
    walls: [...(f.wallFeatures || []), ...bathTiles(f, roomType, room)].map(w => { const m = materialOf(mc, w.material);
      return { side: w.side, kind: w.kind, color: w.color || renderColor(m, w.kind === 'paint' || w.kind === 'rail' || w.kind === 'panel' ? '#f3f1ec' : '#d8d4cc'), fromM: w.kind === 'rail' ? (w.heightM ?? 1) : (w.fromM ?? 0), heightM: w.heightM ?? null, sizeCm: m?.specs?.sizeCm ?? null, marble: /marm|marble/i.test(m?.name || ''),
        along: isPartial(w) ? [w.alongFromM ?? 0, w.alongToM ?? 1e3] : null, vertical: w.vertical !== false, effect: effectOf(w, m) }; }),
    ceiling: { type: c.type, drop: c.dropCm / 100, cove: c.coveCm / 100, led: c.type === 'cove' && led ? (cct <= 3000 ? '#ffd29a' : cct <= 4000 ? '#fff1dc' : '#f4f7ff') : null,
      cornice: (() => { const m = materialOf(mc, c.cornice); return m ? (m.specs?.sizeCm ? [m.specs.sizeCm[0] / 100, m.specs.sizeCm[1] / 100] as [number, number] : [.1, .1] as [number, number]) : null; })(), spots: c.spots },
  };
}
