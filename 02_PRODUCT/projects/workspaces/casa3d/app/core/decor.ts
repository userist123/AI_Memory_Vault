// Decorul unui proiect nou pornit dintr-un șablon: covoare, textile la ferestre, corpuri de iluminat, LED sub suspendate, oglinda LED
// și o paletă caldă (crem pe pereți, perete de accent în spatele patului). Folosește doar ce modelul de date are deja
// (RoomFinishes: rug, windows, fixtures, kitchen, bath, wallFeatures) și doar materiale care există în catalogul de materiale;
// un id care lipsește se sare, nu se inventează. Regulile de covor sunt cele din research_showcase_ui.md:
//  - living: covorul trece pe sub picioarele din față ale canapelei (cel puțin 10 cm);
//  - dormitor: covorul stă sub cele două treimi de jos ale patului și iese în lateral.
// Covorul se desenează centrat în cameră (RugChoice), deci se alege mărimea (și rotirea) care respectă regula din centrul camerei.
import type { Catalog, Floor, FurniturePlacement, LightFixture, MaterialsCatalog, Room, RoomFinishes, RoomRect, Snapshot, WindowTreatment } from './types';
import { defaultFinishes } from './boq';
import { footprintOf } from './validate';
import { backSide } from './fixtures';
import { roomWindows, rugSize, TEXTILE_RULES } from './textiles';
import { materialOf } from './finishes';
import { kitchenOf } from './kitchen';
import { floors } from './levels';

/** Produsele folosite de decor (toate din data/materials.v1.json). */
export const DECOR = {
  curtain: 'draperie-annakajsa-bej', sheer: 'perdea-bergnejlika-alb',
  tablePendant: 'ikea-narrkolv-lustra-ratan-56', bedSconce: 'dedeman-smarter-timber-01-1663-aplica-e27-negru-fag',
  underLed: 'banda-led-hoff-3000k', ledMirror: 'dedeman-savini-due-spr101-oglinda-led-60x80',
  warmPaint: 'dedeman-vopsea-oskar-coloris-classic-cream-5l', accentPaint: 'dedeman-vopsea-savana-superculoare-bej-armonie-5l',
};
/** Cât trece covorul pe sub picioarele din față ale canapelei; cât iese în lateralul patului. */
/** Covorul e centrat în cameră (RugChoice): de aceea regulile au toleranțe — sub canapea cel puțin o treime din lățimea ei,
 *  iar la pat covorul începe între 1/5 și 1/2 din lungime de la tăblie (ideal 1/3). */
export const RUG_RULES = { underSofaM: .1, bedSideM: .3, bedHeadFraction: 1 / 3, bedHeadMin: 1 / 5 };

type Axis = { lo: number; hi: number };
/** Dreptunghiul covorului centrat în cameră. */
export function rugRect(room: Room, size: { w: number; d: number }): RoomRect {
  const r = room.rect, cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2; return { x0: cx - size.w / 2, x1: cx + size.w / 2, z0: cz - size.d / 2, z1: cz + size.d / 2 };
}
const fits = (room: Room, s: { w: number; d: number }) => { const g = TEXTILE_RULES.rugWallGapM; return s.w <= room.rect.x1 - room.rect.x0 - 2 * g + 1e-9 && s.d <= room.rect.z1 - room.rect.z0 - 2 * g + 1e-9; };
/** Pe axa adâncimii piesei: [spate, față]; pe axa lățimii: [stânga, dreapta]. Cu `rug` pe aceleași axe. */
function axes(side: 'N' | 'S' | 'W' | 'E', fp: RoomRect, rug: RoomRect){
  const depth = side === 'N' || side === 'S', pieceD: Axis = depth ? { lo: fp.z0, hi: fp.z1 } : { lo: fp.x0, hi: fp.x1 }, pieceW: Axis = depth ? { lo: fp.x0, hi: fp.x1 } : { lo: fp.z0, hi: fp.z1 };
  const rugD: Axis = depth ? { lo: rug.z0, hi: rug.z1 } : { lo: rug.x0, hi: rug.x1 }, rugW: Axis = depth ? { lo: rug.x0, hi: rug.x1 } : { lo: rug.z0, hi: rug.z1 };
  const flip = side === 'S' || side === 'E'; // spatele piesei e la capătul „hi” al axei
  // distanțe măsurate de la perete spre cameră
  const back = flip ? -pieceD.hi : pieceD.lo, front = flip ? -pieceD.lo : pieceD.hi, rugNear = flip ? -rugD.hi : rugD.lo, rugFar = flip ? -rugD.lo : rugD.hi;
  return { back, front, rugNear, rugFar, pieceW, rugW };
}
/** Covorul trece pe sub picioarele din față ale canapelei (cel puțin RUG_RULES.underSofaM), pe cel puțin o treime din lățimea ei. */
export function rugUnderSofa(sofa: { fp: RoomRect; rotation: number }, rug: RoomRect): boolean {
  const a = axes(backSide(sofa.rotation), sofa.fp, rug), overlapW = Math.min(a.pieceW.hi, a.rugW.hi) - Math.max(a.pieceW.lo, a.rugW.lo);
  return a.rugNear <= a.front - RUG_RULES.underSofaM + 1e-9 && a.rugFar > a.front + .3 && overlapW >= (a.pieceW.hi - a.pieceW.lo) / 3 - 1e-9;
}
/** Covorul e sub cele două treimi de jos ale patului: începe după prima treime (de la tăblie), ajunge până la picioare și iese în lateral. */
export function rugUnderBed(bed: { fp: RoomRect; rotation: number }, rug: RoomRect): boolean {
  const a = axes(backSide(bed.rotation), bed.fp, rug), L = a.front - a.back;
  return a.rugNear >= a.back + L * RUG_RULES.bedHeadMin - 1e-9 && a.rugNear <= a.back + L / 2 + 1e-9 && a.rugFar >= a.front - 1e-9 && a.rugW.lo < a.pieceW.lo - 1e-9 && a.rugW.hi > a.pieceW.hi + 1e-9;
}

interface Piece { p: FurniturePlacement; fp: RoomRect }
function chooseRug(mc: MaterialsCatalog, room: Room, score: (rug: RoomRect) => number): { material: string; rotate?: boolean } | null {
  let best: { material: string; rotate: boolean; sc: number } | null = null;
  for (const m of mc.materials.filter(m => m.category === 'rug' && m.specs?.sizeCm)) for (const rotate of [false, true]){
    const s = rugSize(room, m, rotate); if (!s || !fits(room, s)) continue; const sc = score(rugRect(room, s));
    if (!best || sc < best.sc - 1e-9) best = { material: m.id, rotate, sc }; }
  return best ? { material: best.material, ...(best.rotate ? { rotate: true } : {}) } : null;
}
const overlapArea = (a: RoomRect, b: RoomRect) => Math.max(0, Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0)) * Math.max(0, Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0));

function livingRug(mc: MaterialsCatalog, room: Room, sofa: Piece, others: Piece[]){
  return chooseRug(mc, room, rug => { const a = axes(backSide(sofa.p.rotation), sofa.fp, rug), sofaW = a.pieceW.hi - a.pieceW.lo, across = a.rugW.hi - a.rugW.lo;
    const under = a.front - a.rugNear;   // cât intră sub canapea
    return (rugUnderSofa({ fp: sofa.fp, rotation: sofa.p.rotation }, rug) ? 0 : 50 + Math.max(0, RUG_RULES.underSofaM - under) * 20)
      + Math.max(0, under - (a.front - a.back)) * 5 + Math.abs(across - (sofaW + .2)) * .8
      + others.filter(o => o.p.group !== 'masuta' && o.p.group !== 'canapea').reduce((s, o) => s + overlapArea(o.fp, rug) * 6, 0); });
}
function bedRug(mc: MaterialsCatalog, room: Room, bed: Piece, others: Piece[]){
  return chooseRug(mc, room, rug => { const a = axes(backSide(bed.p.rotation), bed.fp, rug), L = a.front - a.back, bw = a.pieceW.hi - a.pieceW.lo;
    return (rugUnderBed({ fp: bed.fp, rotation: bed.p.rotation }, rug) ? 0 : 50) + Math.abs(a.rugNear - (a.back + L * RUG_RULES.bedHeadFraction)) * 3
      + Math.max(0, a.front + .3 - a.rugFar) * 2 + Math.abs((a.rugW.hi - a.rugW.lo) - (bw + 2 * RUG_RULES.bedSideM + .2)) * .8
      + others.filter(o => o.p.group !== 'pat').reduce((s, o) => s + overlapArea(o.fp, rug) * 6, 0); });
}

/** Decorul pentru un proiect nou. Nu schimbă mobilierul; adaugă finisaje doar pe camerele care primesc ceva. */
export function decorateSnapshot(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog): Snapshot {
  const has = (id: string) => !!materialOf(mc, id), finishes: Record<string, RoomFinishes> = { ...(snap.finishes || {}) };
  const appearanceRooms: NonNullable<NonNullable<Snapshot['appearance']>['rooms']> = { ...(snap.appearance?.rooms || {}) };
  for (const fl of floors(snap) as Floor[]) for (const room of fl.rooms){
    const f: RoomFinishes = { ...defaultFinishes(room), ...(finishes[room.id] || {}) };
    const pieces: Piece[] = snap.placements.filter(p => p.roomId === room.id).flatMap(p => { const fp = footprintOf(cat, p); return fp ? [{ p, fp }] : []; });
    const piece = (g: string) => pieces.find(x => x.p.group === g);
    const fixtures: LightFixture[] = [...(f.fixtures || [])];
    let changed = false;
    const textiles = () => { const w: WindowTreatment[] = roomWindows(fl, room).map(x => ({ openingId: x.openingId, ...(has(DECOR.curtain) ? { curtain: DECOR.curtain } : {}), ...(has(DECOR.sheer) ? { sheer: DECOR.sheer } : {}) }))
      .filter(t => t.curtain || t.sheer); if (w.length){ f.windows = w; changed = true; } };
    const warm = () => { if (has(DECOR.warmPaint)){ f.wallPaint = DECOR.warmPaint; appearanceRooms[room.id] = { ...(appearanceRooms[room.id] || {}), walls: { color: mc.materials.find(m => m.id === DECOR.warmPaint)?.specs?.color ?? '#ede3cc' } }; changed = true; } };
    if (piece('masa') && has(DECOR.tablePendant)){ fixtures.push({ kind: 'pendant', anchor: 'table', material: DECOR.tablePendant }); changed = true; }
    if (room.type === 'living'){
      const sofa = piece('canapea'); if (sofa){ const rug = livingRug(mc, room, sofa, pieces); if (rug){ f.rug = rug; changed = true; } }
      textiles(); warm();
      // garsonieră: și zona de dormit primește aplicele lângă pat
      if (piece('pat') && has(DECOR.bedSconce)){ fixtures.push({ kind: 'sconce', anchor: 'bed', material: DECOR.bedSconce }); changed = true; }
    }
    if (room.type === 'dormitor'){
      const bed = piece('pat'); if (bed){ const rug = bedRug(mc, room, bed, pieces); if (rug){ f.rug = rug; changed = true; }
        if (has(DECOR.bedSconce)){ fixtures.push({ kind: 'sconce', anchor: 'bed', material: DECOR.bedSconce }); changed = true; }
        if (has(DECOR.accentPaint)){ f.wallFeatures = [...(f.wallFeatures || []).filter(w => w.side !== backSide(bed.p.rotation)), { side: backSide(bed.p.rotation), kind: 'paint', material: DECOR.accentPaint, ...(materialOf(mc, DECOR.accentPaint)?.specs?.color ? { color: materialOf(mc, DECOR.accentPaint)!.specs!.color! } : {}) }]; changed = true; } }
      textiles(); warm();
    }
    if (room.type === 'hol') warm();
    // LED sub suspendate: doar dacă bucătăria are front și corpuri suspendate (KitchenSpec.upper ≠ 'none')
    if (room.type === 'bucatarie' && piece('bucatarie') && has(DECOR.underLed) && kitchenOf(f).upper !== 'none'){ f.kitchen = { ...(f.kitchen || {}), underLed: DECOR.underLed }; changed = true; }
    if (room.type === 'baie' && piece('lavoar') && has(DECOR.ledMirror)){ f.bath = { ...(f.bath || {}), mirror: DECOR.ledMirror }; changed = true; }
    if (fixtures.length) f.fixtures = fixtures;
    if (changed) finishes[room.id] = f;
  }
  const out: Snapshot = { ...snap };
  if (Object.keys(finishes).length) out.finishes = finishes;
  if (Object.keys(appearanceRooms).length) out.appearance = { ...(snap.appearance || {}), rooms: appearanceRooms };
  return out;
}
