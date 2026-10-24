import type { Catalog, Floor, Snapshot, Room, MaterialsCatalog, Material, RoomFinishes, BudgetSettings, Confidence } from './types';
import { resolve, groupOf } from './catalog';
import { area as rectArea } from './geometry';
import { openingsOnSide } from './validate';
import { floors, floorOfRoom } from './levels';
import { roomWindows, curtainPlan, blindPlan } from './textiles';
import { doorOpenings } from './doors';
import { kitchenOf, kitchenRun, kitchenQuantities, KITCHEN_DEFAULT } from './kitchen';
import { bathOf } from './bath';
import { fixtureCount } from './fixtures';
import { formatQty } from './format';
import { nicheRoom, nicheTileArea } from './niches';
import { wallItemRoom, wallItemFace, WALL_ITEM_CATEGORY } from './wall-items';
import { colorName } from './budget-excluded';
import { wallWorks } from './renovation';
import { zonesOfRoom, zoneAreaIn, zonesAreaIn } from './floor-zones';
import { isSizedRadiator, radiatorSize, hvacSize } from './tech-geometry';
import { ceilingZonesOfRoom, ceilingZoneQty, ceilingZoneHidden } from './ceiling-zones';
import { railingHeight, railingKind, railingLength } from './railings';
const RADIATOR_RO: Record<string, string> = { panel: 'panou', towel: 'port-prosop', decorative: 'decorativ' };
/** Pereții în documente: punctele cardinale în română, nu literele interne N/S/E/W. */
const SIDE_RO: Record<string, string> = { N: 'nord', S: 'sud', E: 'est', W: 'vest' };
const LIGHT_CATS = new Set(['lighting', 'spot', 'led_strip', 'pendant', 'wall_light', 'led_profile']);
const FIXTURE_LABEL = { pendant: 'Pendul', sconce: 'Aplică', track: 'Șină cu spoturi' } as const;
import { bandGeometry, bathTiles, layoutOf, floorWaste, floorLaborId, sideGeometry, wallpaperRolls, panelCount, pieceSizeCm, ceilingOf, FEATURE_CATEGORY, featureLabor, effectOf, alongOf, sideSpan, frameRects, frameMoulding } from './finishes';
import { WASTE, PAINT_COATS, DOOR_HEIGHT, WINDOW_HEIGHT, BATH_TILE_HEIGHT, BACKSPLASH_HEIGHT, LIGHTS_EXTRA_PER_M2, VAT_RATE, WET_ROOMS, SANITARY, APPLIANCES, DEFAULT_BUDGET } from './rules.boq';

const r2 = (v: number) => Math.round(v * 100) / 100;
export interface RoomGeometry { roomId: string; name: string; floorArea: number; perimeter: number; height: number; doorWidth: number; windowWidth: number; wallGross: number; openings: number; wallNet: number; ceiling: number }
// ---------- 1) geometrie pe cameră, calculată din Digital Twin ----------
export function roomGeometry(snap: Snapshot, room: Room): RoomGeometry {
  // înălțimea și golurile vin de pe nivelul camerei (parter sau etaj)
  const fl = floorOfRoom(snap, room.id), r = room.rect, w = r.x1 - r.x0, d = r.z1 - r.z0, H = fl.ceilingHeight;
  let doorWidth = 0, windowWidth = 0;
  for (const s of ['N', 'S', 'W', 'E'] as const) for (const o of openingsOnSide(fl, room, s)){ const lo = s === 'N' || s === 'S' ? r.x0 : r.z0, hi = s === 'N' || s === 'S' ? r.x1 : r.z1;
    const len = Math.max(0, Math.min(o.b, hi) - Math.max(o.a, lo)); if (o.kind === 'door') doorWidth += len; else windowWidth += len; }
  const perimeter = 2 * (w + d), wallGross = perimeter * H, openings = doorWidth * DOOR_HEIGHT + windowWidth * WINDOW_HEIGHT;
  return { roomId: room.id, name: room.name, floorArea: r2(rectArea(r)), perimeter: r2(perimeter), height: H, doorWidth: r2(doorWidth), windowWidth: r2(windowWidth), wallGross: r2(wallGross), openings: r2(openings), wallNet: r2(Math.max(0, wallGross - openings)), ceiling: r2(rectArea(r)) };
}
/** Ghenele și casetările al căror centru e în cameră: suprafața de gips-carton = perimetrul fețelor libere (nelipite de
 *  pereții camerei) × înălțimea (lipsă = tavanul). Stâlpul structural nu are cost. */
export function shaftsIn(fl: Floor, room: Room){
  const r = room.rect, e = .05;
  return (fl.columns ?? []).filter(c => (c.role === 'shaft' || c.role === 'boxing') && c.x >= r.x0 && c.x <= r.x1 && c.z >= r.z0 && c.z <= r.z1).map(c => {
    const height = Math.min(fl.ceilingHeight, c.height ?? fl.ceilingHeight), x0 = c.x - c.w / 2, x1 = c.x + c.w / 2, z0 = c.z - c.d / 2, z1 = c.z + c.d / 2;
    const free = c.shape === 'round' ? Math.PI * c.w : (Math.abs(z0 - r.z0) > e ? c.w : 0) + (Math.abs(z1 - r.z1) > e ? c.w : 0) + (Math.abs(x0 - r.x0) > e ? c.d : 0) + (Math.abs(x1 - r.x1) > e ? c.d : 0);
    return { id: c.id, role: c.role as 'shaft' | 'boxing', w: c.w, d: c.d, height, freePerimeter: r2(free), area: r2(free * height) }; });
}
export function defaultFinishes(room: Room): RoomFinishes {
  if (room.type === 'baie') return { floor: 'gresie-mckinley', wallPaint: 'vopsea-innenweiss', wallTile: 'faianta-grafen', baseboard: null, light: 'lampa-virrmo' };
  if (room.type === 'bucatarie') return { floor: 'gresie-mckinley', wallPaint: 'vopsea-innenweiss', wallTile: 'faianta-lumiere', baseboard: null, light: 'lampa-virrmo' };
  return { floor: 'parchet-egger-h2099', wallPaint: 'vopsea-innenweiss', wallTile: null, baseboard: 'plinta-mdf-60', light: 'lampa-virrmo' };
}
export const finishesOf = (snap: Snapshot, room: Room): RoomFinishes => ({ ...defaultFinishes(room), ...(snap.finishes?.[room.id] || {}) });
export const budgetOf = (snap: Snapshot): BudgetSettings => ({ ...DEFAULT_BUDGET, ...(snap.budget || {}) });

// ---------- 2) BOQ ----------
const TEXTILE_CATS = new Set(['curtain', 'sheer', 'blind', 'rug']);
const PATTERN_LABEL: Record<string, string> = { straight: 'drept', brick: 'decalat 1/2', third: 'decalat 1/3', diagonal: 'diagonală', herringbone: 'spic', chevron: 'chevron', checker: 'șah' };
/** Efectele vopselei decorative, în documente. */
/** Obiectele de pe perete, în documente. */
const WALL_ITEM_RO: Record<string, string> = { art: 'Tablou', mirror: 'Oglindă', shelf: 'Raft', tv: 'Televizor' };
const EFFECT_RO: Record<string, string> = { venetian: 'venețiană', concrete: 'beton', microcement: 'microciment', travertine: 'travertin', silk: 'mătase', textured: 'structurată' };
const FEATURE_LABEL: Record<string, string> = { paint: 'Vopsea', panel: 'Lambriu', rail: 'Baghetă', wallpaper: 'Tapet', slats: 'Riflaj', plaster: 'Tencuială decorativă', brick: 'Cărămidă aparentă', stone: 'Piatră decorativă', tile: 'Faianță' };
export type BoqCategory = 'furniture' | 'finishes' | 'lighting' | 'appliances' | 'sanitary' | 'textiles';
export interface BoqItem { key: string; category: BoqCategory; roomId: string | null; label: string; refId: string; netQty: number; unit: string; wastePct: number;
  orderedQty: number; packs: number | null; packLabel: string | null; unitPrice: number | null; total: number | null; supplier: string; sourceUrl: string | null; verifiedAt: string | null; confidence: Confidence; note?: string;
  /** culoarea exactă (#rrggbb) a unei vopsele nuanțate: pentru mostră și comandă; eticheta poartă doar numele culorii */
  color?: string }
export interface LaborItem { key: string; roomId: string; rateId: string; label: string; qty: number; unit: string; low: number; expected: number; high: number; confidence: Confidence; sources: { name: string; url: string }[] }

function materialLine(m: Material, key: string, roomId: string | null, label: string, net: number, catalogVerifiedAt: string, wasteOverride?: number): BoqItem {
  const waste = wasteOverride ?? WASTE[m.category] ?? 0, need = net * (1 + waste), verifiedAt = m.verifiedAt || catalogVerifiedAt;
  let packs: number | null = null, ordered = need, total: number;
  if (m.pack){ packs = Math.ceil(need / m.pack.size - 1e-9); ordered = packs * m.pack.size; total = m.pack.price != null ? packs * m.pack.price : ordered * m.unitPrice; }
  else { ordered = m.unit === 'buc' ? Math.ceil(need - 1e-9) : need; total = ordered * m.unitPrice; }
  return { key, category: LIGHT_CATS.has(m.category) ? 'lighting' : TEXTILE_CATS.has(m.category) ? 'textiles' : 'finishes', roomId, label, refId: m.id, netQty: r2(net), unit: m.unit, wastePct: waste, orderedQty: r2(ordered), packs, packLabel: m.pack?.label ?? null,
    unitPrice: m.unitPrice, total: r2(total), supplier: m.supplier, sourceUrl: m.sourceUrl, verifiedAt, confidence: m.confidence, note: m.note };
}
export function computeBOQ(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog){
  const items: BoqItem[] = [], labor: LaborItem[] = [], geometry: RoomGeometry[] = [], unknown: string[] = [];
  const M = (id?: string | null) => (id ? mc.materials.find(m => m.id === id) : undefined), L = (id: string) => mc.labor.find(l => l.id === id)!;
  const addLabor = (roomId: string, rateId: string, qty: number, part?: string) => { if (qty <= 0) return; const l = L(rateId);
    labor.push({ key: `${roomId}:${rateId}${part ? `:${part}` : ''}`, roomId, rateId, label: l.label, qty: r2(qty), unit: l.unit, low: r2(qty * l.low), expected: r2(qty * l.expected), high: r2(qty * l.high), confidence: l.confidence, sources: l.sources }); };
  let adhesiveArea = 0;
  for (const room of floors(snap).flatMap(f => f.rooms)){
    const g = roomGeometry(snap, room), f = finishesOf(snap, room), fl = floorOfRoom(snap, room.id); geometry.push(g);
    // pardoseală: pierderile și manopera depind de modul de așezare (drept, decalat, diagonală, spic, chevron) și de format
    // zonele de pardoseală (alt material pe o porțiune a camerei) se scad din pardoseala camerei și au linia lor
    const zoned = zonesAreaIn(fl, room.id), floorNet = r2(Math.max(0, g.floorArea - zoned));
    const fm = M(f.floor); if (fm){ const lay = layoutOf(f, fm), [pl, pw] = pieceSizeCm(fm);
      items.push(materialLine(fm, `${room.id}:floor`, room.id, `Pardoseală · ${room.name} (${PATTERN_LABEL[lay.pattern]}${fm.category === 'floor_tile' ? `, ${pl}×${pw} cm, rost ${lay.groutMm ?? 3} mm` : ''}${zoned > 0 ? `, fără zonele de ${r2(zoned)} m²` : ''})`, floorNet, mc.verifiedAt, floorWaste(lay, fm)));
      if (fm.category === 'floor_tile') adhesiveArea += floorNet; addLabor(room.id, floorLaborId(lay, fm), floorNet); }
    for (const z of zonesOfRoom(fl, room.id)){ const area = zoneAreaIn(fl, z), zm = M(z.floor), nm = z.label ? ` „${z.label}”` : '', dims = `${r2(z.rect.x1 - z.rect.x0)} × ${r2(z.rect.z1 - z.rect.z0)} m`;
      if (area <= 0) continue;
      if (!zm || (zm.category !== 'parquet' && zm.category !== 'floor_tile')){ unknown.push(`Pardoseală zonă${nm} · ${room.name} (${dims}, ${area} m²): materialul ales nu are preț verificat`); continue; }
      const lay = layoutOf({ floorLayout: z.layout } as RoomFinishes, zm), [pl, pw] = pieceSizeCm(zm);
      items.push(materialLine(zm, `${room.id}:zone:${z.id}`, room.id, `Pardoseală zonă${nm} · ${room.name} (${dims}, ${PATTERN_LABEL[lay.pattern]}${zm.category === 'floor_tile' ? `, ${pl}×${pw} cm, rost ${lay.groutMm ?? 3} mm` : ''})`, area, mc.verifiedAt, floorWaste(lay, zm)));
      if (zm.category === 'floor_tile') adhesiveArea += area; addLabor(room.id, floorLaborId(lay, zm), area, `zone:${z.id}`); }
    if (room.type === 'baie') addLabor(room.id, 'manopera-hidroizolatie', g.floorArea);
    // faianță: baie până la 2,1 m; bucătărie = zona dintre blat și dulapuri, pe lungimea mobilierului de bucătărie
    let tileArea = 0;
    // baie: până la 2,1 m pe fiecare perete care nu are altă placare (aceeași regulă ca în 3D)
    if (room.type === 'baie') tileArea = bathTiles(f, room.type, room).reduce((a, w) => a + sideGeometry(fl, room, w.side, w.heightM, w.alongFromM !== undefined ? alongOf(w, sideSpan(room, w.side)) : undefined).netM2, 0);
    if (room.type === 'bucatarie'){ const k = snap.placements.find(p => p.roomId === room.id && p.group === 'bucatarie'), rv = k && resolve(cat, k.variantId); tileArea = rv ? rv.w * BACKSPLASH_HEIGHT : 0;
      // placarea dintre blat și suspendate e faianță doar dacă așa s-a ales în sistemul de bucătărie
      if (f.kitchen && kitchenOf(f).backsplash !== 'tile') tileArea = 0; }
    const wt = M(f.wallTile); if (wt && tileArea > 0){ items.push(materialLine(wt, `${room.id}:walltile`, room.id, `Faianță · ${room.name}`, tileArea, mc.verifiedAt)); adhesiveArea += tileArea; addLabor(room.id, 'manopera-faianta', tileArea); }
    // ghene și casetări din gips-carton în cameră: placa pe fețele libere × înălțime; în baie și faianța peste ea
    for (const sh of shaftsIn(fl, room)){ const gk = mc.materials.find(m => m.category === 'plasterboard'), where = `${room.name} (${sh.role === 'shaft' ? 'ghenă' : 'casetare'} ${Math.round(sh.w * 100)}×${Math.round(sh.d * 100)} cm, h ${r2(sh.height)} m)`;
      if (sh.area <= 0) continue;
      if (gk) items.push(materialLine(gk, `${room.id}:shaft:${sh.id}`, room.id, `Casetare gips-carton · ${where}`, sh.area, mc.verifiedAt)); else unknown.push(`Gips-carton casetare · ${where}`);
      unknown.push(`Manoperă casetare gips-carton ${r2(sh.area)} m² · ${where}`);
      if (room.type === 'baie' && wt){ items.push(materialLine(wt, `${room.id}:shaft-tile:${sh.id}`, room.id, `Faianță casetare · ${where}`, sh.area, mc.verifiedAt)); adhesiveArea += sh.area; addLabor(room.id, 'manopera-faianta', sh.area); } }
    // nișe: în baie (sau cu finisaj de faianță) fundul și fețele interioare se placează; manopera și profilul de colț nu au preț verificat
    for (const n of (fl.niches ?? []).filter(x => nicheRoom(fl, x)?.id === room.id)){ const where = `${room.name} (${Math.round(n.width * 100)}×${Math.round(n.height * 100)}×${Math.round(n.depth * 100)} cm)`, area = r2(nicheTileArea(n));
      if ((room.type === 'baie' || n.finish === 'tile') && wt){ items.push(materialLine(wt, `${room.id}:niche:${n.id}`, room.id, `Faianță nișă · ${where}`, area, mc.verifiedAt)); adhesiveArea += area; addLabor(room.id, 'manopera-faianta', area); }
      unknown.push(`Manoperă nișă (1 buc) · ${where}`); if (room.type === 'baie' || n.finish === 'tile') unknown.push(`Profil de colț nișă ${r2(2 * (n.width + n.height))} ml · ${where}`);
      if (n.finish === 'led') unknown.push(`Bandă LED nișă ${r2(n.width)} m · ${where}`); }
    // obiecte pe perete: cu produs ales, o linie de 1 buc; fără produs e obiectul clientului (nicio linie); montajul nu are tarif verificat
    for (const it of (fl.wallItems ?? []).filter(x => wallItemRoom(fl, x)?.id === room.id)){ const face = wallItemFace(fl, it), nm = WALL_ITEM_RO[it.kind] ?? it.kind, m = M(it.product);
      const where = `${room.name}, perete ${face ? SIDE_RO[face.side] : '-'} (${Math.round(it.width * 100)}×${Math.round(it.height * 100)} cm, centrul la ${r2(it.elevation)} m)`;
      if (it.product){ if (m && m.category === WALL_ITEM_CATEGORY[it.kind]) items.push(materialLine(m, `${room.id}:wallitem:${it.id}`, room.id, `${nm} · ${where}`, 1, mc.verifiedAt, 0)); else unknown.push(`${nm} · ${where}: produsul ales nu are preț verificat`); }
      unknown.push(`Montaj ${nm.toLowerCase()} (1 buc) · ${where}`); }
    // placări pe pereți (tapet, riflaj, tencuială decorativă, cărămidă, piatră, faianță), fiecare pe latura ei, până la înălțimea aleasă
    let featureArea = 0;
    for (const [i, wf] of (f.wallFeatures || []).entries()){ const m = M(wf.material), sg = bandGeometry(fl, room, wf);
      if (wf.kind === 'rail'){ if (!m || m.category !== 'moulding' || sg.lengthM <= 0){ if (sg.lengthM > 0) unknown.push(`${room.name}: baghetă ${wf.side}`); continue; }
        items.push(materialLine(m, `${room.id}:wall:${wf.side}:${i}`, room.id, `Baghetă · ${room.name}, perete ${SIDE_RO[wf.side] ?? wf.side} la ${r2(sg.toM)} m`, sg.lengthM, mc.verifiedAt)); addLabor(room.id, 'manopera-bagheta', sg.lengthM); continue; }
      // boiserie: casete din baghetă peste vopsea (peretele se vopsește în continuare); bagheta în bucăți întregi, manopera în ml
      if (wf.kind === 'frames'){ const rects = frameRects(fl, room, wf), mo = frameMoulding(rects, m?.unit === 'buc' ? m.specs?.pieceM : undefined); if (!rects.length) continue;
        const where = `${room.name}, perete ${SIDE_RO[wf.side] ?? wf.side} (${rects.length} ${rects.length === 1 ? 'casetă' : 'casete'}, ${r2(sg.fromM)}–${r2(sg.toM)} m${sg.along ? `, porțiunea ${r2(sg.along[0])}–${r2(sg.along[1])} m pe lungime` : ''})`;
        if (!m || m.category !== 'moulding' || (m.unit === 'buc' && mo.pieces == null)){ unknown.push(`Boiserie baghetă ${mo.ml} ml · ${where}: produsul ales nu are preț verificat`); continue; }
        items.push(materialLine(m, `${room.id}:wall:${wf.side}:${i}`, room.id, `Boiserie baghetă · ${where}: ${mo.ml} ml${mo.pieces != null ? `, ${mo.pieces} bucăți de ${m.specs!.pieceM} m` : ''}`, mo.pieces ?? mo.ml, mc.verifiedAt, mo.pieces != null ? 0 : undefined));
        addLabor(room.id, 'manopera-bagheta', mo.ml, `frames:${i}`); continue; }
      if (sg.netM2 <= 0) continue;
      const eff = effectOf(wf, m);
      // vopsea decorativă cu efect (venețiană, beton, microciment…): fără un produs care declară efectul, materialul și manopera nu au preț verificat
      if (eff && eff !== 'textured' && m?.specs?.effect !== eff){ const where = `${room.name}, perete ${SIDE_RO[wf.side] ?? wf.side} (${r2(sg.netM2)} m²)`; featureArea += sg.netM2;
        unknown.push(`Vopsea decorativă efect ${EFFECT_RO[eff]} (material, amorsă, finisaj) · ${where}: fără produs verificat`, `Manoperă vopsea decorativă efect ${EFFECT_RO[eff]} · ${where}: tarif fără sursă verificată`); continue; }
      if (!m || m.category !== FEATURE_CATEGORY[wf.kind]){ unknown.push(`${room.name}: ${wf.kind} ${wf.side}`); continue; }
      featureArea += sg.netM2; const key = `${room.id}:wall:${wf.side}:${i}`, where = `${room.name}, perete ${SIDE_RO[wf.side] ?? wf.side} (${r2(sg.lengthM)} m × ${r2(sg.fromM)}–${r2(sg.toM)} m${sg.along ? `, porțiunea ${r2(sg.along[0])}–${r2(sg.along[1])} m pe lungime` : ''})`;
      if (wf.kind === 'paint'){ if (m.coverage){ const line = materialLine(m, key, room.id, `Vopsea${wf.color ? ` nuanță ${colorName(wf.color)}` : ''} · ${where} (${r2(sg.netM2)} m², ${PAINT_COATS} straturi)`, sg.netM2 * PAINT_COATS / m.coverage, mc.verifiedAt); items.push(wf.color ? { ...line, color: wf.color.toLowerCase() } : line); } else unknown.push(`Vopsea ${m.name} · ${where}: randamentul nu e publicat (${r2(sg.netM2)} m²)`); addLabor(room.id, 'manopera-zugravit', sg.netM2); continue; }
      if (wf.kind === 'panel' && m.specs?.sizeCm){ items.push(materialLine(m, key, room.id, `Lambriu · ${where}`, panelCount(sg.lengthM, sg.heightM, m.specs.sizeCm), mc.verifiedAt, 0)); unknown.push(`Manoperă lambriu · ${where}`); continue; }
      if (wf.kind === 'wallpaper' && m.specs?.roll){ const rr = wallpaperRolls(sg.lengthM, sg.heightM, m.specs.roll); items.push({ ...materialLine(m, key, room.id, `Tapet · ${where}: ${rr.strips} fâșii de ${r2(rr.dropM)} m, ${rr.perRoll} pe rolă, + ${rr.spare} ${rr.spare === 1 ? 'rolă' : 'role'} rezervă; toate din același lot`, rr.rolls, mc.verifiedAt, 0) }); }
      else if (wf.kind === 'slats' && m.specs?.sizeCm) items.push(materialLine(m, key, room.id, `Riflaj · ${where}`, panelCount(sg.lengthM, sg.heightM, m.specs.sizeCm), mc.verifiedAt, 0));
      else if (wf.kind === 'plaster') items.push(materialLine(m, key, room.id, `${eff && eff !== 'textured' ? `Vopsea decorativă efect ${EFFECT_RO[eff]}` : 'Tencuială decorativă'} · ${where} (${r2(sg.netM2)} m²)`, sg.netM2 * (m.consumption || 0), mc.verifiedAt));
      else { items.push(materialLine(m, key, room.id, `${FEATURE_LABEL[wf.kind]} · ${where}`, sg.netM2, mc.verifiedAt)); if (wf.kind === 'tile') adhesiveArea += sg.netM2; }
      const lab = featureLabor(wf, m); if (lab && mc.labor.some(l => l.id === lab)) addLabor(room.id, lab, sg.netM2); else unknown.push(`Manoperă ${FEATURE_LABEL[wf.kind].toLowerCase()}${eff && eff !== 'textured' ? ` efect ${EFFECT_RO[eff]}` : ''} · ${where}`); }
    // tavan fals / scafă luminoasă / cornișă / spoturi
    const c = ceilingOf(f), w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0;
    // tavan fals simplu: placa acoperă tot tavanul, iar pereții se vopsesc doar până sub el;
    // cu scafă: panoul din mijloc + fâșia verticală a scafei (conturul interior × coborârea); pereții rămân pe toată înălțimea
    let ceilingBand = 0, wallsHidden = 0;
    if (c.type !== 'flat'){ const gk = mc.materials.find(m => m.category === 'plasterboard'), iw = Math.max(0, w - 2 * c.coveCm / 100), id = Math.max(0, d - 2 * c.coveCm / 100), inner = 2 * (iw + id);
      if (c.type === 'cove') ceilingBand = inner * c.dropCm / 100; else wallsHidden = g.perimeter * c.dropCm / 100;
      const board = c.type === 'cove' ? iw * id + ceilingBand : g.ceiling;
      if (gk){ items.push(materialLine(gk, `${room.id}:ceiling`, room.id, `Tavan fals gips-carton · ${room.name} (coborât ${c.dropCm} cm${c.type === 'cove' ? `, scafă ${c.coveCm} cm` : ''})`, board, mc.verifiedAt)); addLabor(room.id, 'manopera-rigips-tavan', board); }
      if (c.type === 'cove'){ addLabor(room.id, 'manopera-scafa', inner);
        const led = M(c.led); if (led) items.push(materialLine(led, `${room.id}:led`, room.id, `Bandă LED scafă · ${room.name} (${r2(inner)} m)`, inner, mc.verifiedAt)); } }
    const cm = M(c.cornice); if (cm){ items.push(materialLine(cm, `${room.id}:cornice`, room.id, `Cornișă · ${room.name}`, g.perimeter, mc.verifiedAt)); addLabor(room.id, 'manopera-cornisa', g.perimeter); }
    // tavane false pe zonă (casete): placa de jos + banda laterală (+ raftul scafei), manopera de tavan fals și de scafă, LED pe contur în ml;
    // fețele casetei se vopsesc (banda laterală și scafa se adaugă la vopsea, placa de jos ia locul tavanului de deasupra)
    for (const z of ceilingZonesOfRoom(fl, room.id)){ if (ceilingZoneHidden(z, c.dropCm)) continue; const q = ceilingZoneQty(fl, z, c.dropCm); if (!q) continue;
      const nm = z.label ? ` „${z.label}”` : '', where = `${room.name} (${q.w} × ${q.d} m, coborât ${z.dropCm} cm${z.coveCm ? `, scafă ${z.coveCm} cm` : ''})`, gk = mc.materials.find(m => m.category === 'plasterboard');
      if (gk){ items.push(materialLine(gk, `${room.id}:ceiling:czone:${z.id}`, room.id, `Tavan fals pe zonă${nm} gips-carton · ${where}: placă ${q.areaM2} m² + bandă laterală ${q.bandM2} m²${q.coveM2 > 0 ? ` + scafă ${q.coveM2} m²` : ''}`, q.boardM2, mc.verifiedAt)); addLabor(room.id, 'manopera-rigips-tavan', q.boardM2, `czone:${z.id}`); }
      else unknown.push(`Tavan fals pe zonă${nm} gips-carton ${q.boardM2} m² · ${where}: fără preț verificat`);
      if (z.coveCm) addLabor(room.id, 'manopera-scafa', q.perimeterM, `czone:${z.id}`);
      ceilingBand += q.bandM2 + q.coveM2;
      if (z.led){ const led = M(z.led); if (led && led.category === 'led_strip') items.push(materialLine(led, `${room.id}:led:czone:${z.id}`, room.id, `Bandă LED tavan fals pe zonă${nm} · ${where} (${q.ledM} m)`, q.ledM, mc.verifiedAt));
        else unknown.push(`Bandă LED tavan fals pe zonă${nm} ${q.ledM} ml · ${where}: produsul ales nu are preț verificat`); } }
    // textile: draperii și perdele în pachete (perechi de panouri), storuri pe fereastră, covorul camerei
    const wins = roomWindows(fl, room);
    for (const t of f.windows || []){ const win = wins.find(x => x.openingId === t.openingId); if (!win) continue; const n = wins.indexOf(win) + 1;
      for (const [k, id, lbl] of [['curtain', t.curtain, 'Draperii'], ['sheer', t.sheer, 'Perdele']] as const){ const m = M(id); if (!m) continue; const p = curtainPlan(win, fl.ceilingHeight, m, t.fullness);
        items.push(materialLine(m, `${room.id}:${k}:${t.openingId}`, room.id, `${lbl} · ${room.name}, fereastra ${n} (${r2(win.widthM)} m): bară ${p.rodW} m, ${p.panels} panouri, cădere ${p.drop} m${p.hemM > .02 ? `, tiv ${Math.round(p.hemM * 100)} cm` : ''}`, p.packs, mc.verifiedAt, 0)); }
      const bm2 = M(t.blind); if (bm2){ const p = blindPlan(win, bm2); items.push(materialLine(bm2, `${room.id}:blind:${t.openingId}`, room.id, `Stor · ${room.name}, fereastra ${n} (${r2(win.widthM)} m)`, p.count, mc.verifiedAt, 0)); } }
    const rug = M(f.rug?.material); if (rug) items.push(materialLine(rug, `${room.id}:rug`, room.id, `Covor · ${room.name}`, 1, mc.verifiedAt, 0));
    // covoarele în plus (al doilea covor, sub masă): o linie pe covor, cheia `<cameră>:rug:<n>` (n de la 2)
    (f.rugs ?? []).forEach((rc, i) => { const m = M(rc.material); if (m && m.category === 'rug') items.push(materialLine(m, `${room.id}:rug:${i + 2}`, room.id, `Covor ${i + 2} · ${room.name}`, 1, mc.verifiedAt, 0)); else unknown.push(`Covor ${i + 2} · ${room.name}: produsul ales nu are preț verificat`); });
    // bucătăria ca sistem: blat, chiuvetă, baterie, LED sub suspendate, mânere, placare din sticlă
    const run = f.kitchen ? kitchenRun(snap, cat, room.id) : null;
    if (run){ const k = kitchenOf(f), top = M(k.countertop), q = kitchenQuantities(run, k, top);
      if (top && q.countertopPieces) items.push(materialLine(top, `${room.id}:countertop`, room.id, `Blat bucătărie · ${room.name} (${r2(run.lengthM)} m, ${top.specs?.thicknessMm ?? k.countertopMm} mm)`, q.countertopPieces, mc.verifiedAt, 0));
      else unknown.push(`Blat bucătărie ${r2(run.lengthM)} m, ${k.countertopMm} mm, culoare ${k.countertopColor} (ofertă)`);
      for (const [id, key, lbl] of [[k.sink, 'sink', 'Chiuvetă'], [k.tap, 'tap', 'Baterie bucătărie']] as const){ const m = M(id); if (m) items.push(materialLine(m, `${room.id}:${key}`, room.id, `${lbl} · ${room.name}`, 1, mc.verifiedAt, 0)); }
      const led = M(k.underLed); if (led && k.upper !== 'none') items.push(materialLine(led, `${room.id}:kitchen-led`, room.id, `Bandă LED sub suspendate · ${room.name} (${q.ledM} m)`, q.ledM, mc.verifiedAt));
      if (q.handles > 0) unknown.push(`Mânere bucătărie: ${q.handles} buc (${k.handle})`);
      if (k.backsplash === 'glass') unknown.push(`Placare sticlă bucătărie ${q.backsplashM2} m² (la comandă)`);
      if (k.frontFinish !== 'matt' || k.frontColor !== KITCHEN_DEFAULT.frontColor) unknown.push(`Fronturi bucătărie ${q.fronts} buc, ${k.frontFinish}, ${k.frontColor} (diferență față de varianta din catalog)`); }
    // baia ca sistem: baterie, set de duș, calorifer port-prosop, oglindă LED; cadrul WC suspendat și dușul walk-in cer lucrări fără preț verificat
    if (room.type === 'baie' && f.bath){ const bb = bathOf(f);
      for (const [id, key, lbl] of [[bb.tap, 'bath-tap', 'Baterie lavoar'], [bb.shower, 'shower-set', 'Set duș'], [bb.towelRadiator, 'towel-radiator', 'Calorifer port-prosop'], [bb.mirror, 'led-mirror', 'Oglindă LED']] as const){
        const m = M(id); if (m) items.push(materialLine(m, `${room.id}:${key}`, room.id, `${lbl} · ${room.name}`, 1, mc.verifiedAt, 0)); }
      if (bb.wc === 'wall') unknown.push(`Cadru WC suspendat + clapetă · ${room.name}`);
      if (bb.showerType === 'walkin') unknown.push(`Duș walk-in: pantă, rigolă și hidroizolație suplimentară · ${room.name}`); }
    // corpurile de iluminat plasate (pendule, aplice, șină); fără produs ales rămân necunoscute; aplicele cer un punct electric pe perete
    (f.fixtures || []).forEach((fx, i) => { const m = M(fx.material), n = fixtureCount(fx), lbl = FIXTURE_LABEL[fx.kind];
      if (m) items.push(materialLine(m, `${room.id}:fixture:${i}`, room.id, `${lbl} · ${room.name}`, n, mc.verifiedAt, 0)); else unknown.push(`${lbl} ×${n} (produs neales) · ${room.name}`);
      if (fx.kind === 'sconce') unknown.push(`Punct electric pe perete pentru aplice ×${n} · ${room.name}`); });
    const sm = M(c.spot); if (sm && c.spots > 0) items.push(materialLine(sm, `${room.id}:spots`, room.id, `Spoturi încastrate · ${room.name}`, c.spots, mc.verifiedAt));
    // vopsea: pereți (fără zona placată) + tavan (și marginea tavanului fals), 2 straturi
    const paintArea = Math.max(0, g.wallNet - wallsHidden - (wt ? tileArea : 0) - featureArea) + g.ceiling + ceilingBand, pm = M(f.wallPaint);
    if (pm && pm.coverage){ const litres = paintArea * PAINT_COATS / pm.coverage; const line = materialLine(pm, `${room.id}:paint`, room.id, `Vopsea pereți + tavan · ${room.name} (${r2(paintArea)} m², ${PAINT_COATS} straturi)`, litres, mc.verifiedAt); items.push(line); addLabor(room.id, 'manopera-zugravit', paintArea); }
    else if (pm){ unknown.push(`Vopsea ${pm.name} · ${room.name}: randamentul nu e publicat (${r2(paintArea)} m²)`); addLabor(room.id, 'manopera-zugravit', paintArea); }
    // plintă (doar unde nu e placat)
    const bm = M(f.baseboard); if (bm && !WET_ROOMS.has(room.type)){ const len = Math.max(0, g.perimeter - g.doorWidth); items.push(materialLine(bm, `${room.id}:baseboard`, room.id, `Plintă · ${room.name}`, len, mc.verifiedAt)); addLabor(room.id, 'manopera-plinta', len); }
    // iluminat general
    const lm = M(f.light); if (lm){ const n = f.lights ?? (1 + Math.max(0, Math.ceil((g.floorArea - LIGHTS_EXTRA_PER_M2) / LIGHTS_EXTRA_PER_M2))); const line = materialLine(lm, `${room.id}:light`, room.id, `Iluminat · ${room.name}`, n, mc.verifiedAt); items.push(line); }
  }
  // starea pereților (renovare): demolarea cu evacuarea molozului și pereții noi (gips-carton până la 12,5 cm, altfel zidărie);
  // demolarea, zidăria și manopera nu au preț verificat, deci rămân necunoscute; placa de gips-carton are preț din catalog
  const gk = mc.materials.find(m => m.category === 'plasterboard');
  floors(snap).forEach((fl, li) => { const lvl = floors(snap).length > 1 ? ` · ${li === 0 ? 'parter' : fl.name || `nivelul ${li}`}` : '';
    for (const ww of wallWorks(fl)){ const where = `perete ${Math.round(ww.thicknessM * 100)} cm, ${ww.lengthM} m × ${ww.heightM} m${ww.exterior ? ', exterior' : ''}${lvl}`;
      if (ww.status === 'demolish'){ unknown.push(`Demolare ${ww.areaM2} m² cu evacuarea molozului (~${r2(ww.areaM2 * ww.thicknessM)} m³) · ${where}: fără preț verificat`); continue; }
      if (ww.system === 'drywall'){ if (gk) items.push(materialLine(gk, `wall-new:${ww.wallId}`, null, `Perete nou gips-carton · ${where} (${ww.areaM2} m², placă pe 2 fețe)`, ww.areaM2 * 2, mc.verifiedAt)); else unknown.push(`Placă gips-carton perete nou ${r2(ww.areaM2 * 2)} m² · ${where}`);
        unknown.push(`Manoperă perete nou gips-carton ${ww.areaM2} m² (structură, vată, 2 fețe) · ${where}: fără preț verificat`); }
      else unknown.push(`Zidărie nouă ${ww.areaM2} m² (material și manoperă) · ${where}: fără preț verificat`); }
    // balustradele: în ml, pe tip și înălțime; fără preț verificat în catalog (material și montaj)
    for (const r of fl.railings ?? []){ const len = railingLength(r); if (len <= 0) continue;
      unknown.push(`Balustradă ${railingKind(r) === 'glass' ? 'sticlă' : 'metal'} ${len} ml, h ${Math.round(railingHeight(r) * 100)} cm (material și montaj)${lvl}: fără preț verificat`); } });
  // punctele tehnice cu corp: aerul condiționat (unitatea interioară și traseul) și caloriferele noi; fără preț verificat
  const allRooms = floors(snap).flatMap(fl => fl.rooms);
  for (const tp of snap.tech ?? []){ const room = allRooms.find(r => r.id === tp.roomId); if (!room) continue;
    if (tp.kind === 'hvac_split') unknown.push(`Unitate AC interioară + traseu (1 buc, ${Math.round(hvacSize(tp).w * 100)} cm) · ${room.name}: fără preț verificat`);
    else if (isSizedRadiator(tp)){ const sz = radiatorSize(tp); unknown.push(`Calorifer ${RADIATOR_RO[tp.model ?? 'panel']} ${Math.round(sz.w * 100)}×${Math.round(sz.h * 100)} cm (1 buc, nou sau mutat, cu racordare) · ${room.name}: fără preț verificat`); } }
  // uși de interior (foaie + toc) și mânere, pe golul lor; montajul nu are tarif verificat
  for (const d of doorOpenings(snap)){ const c = snap.doors?.[d.id], dm = M(c?.product), hm = M(c?.handle), roomId = d.roomIds[0] ?? null;
    if (dm){ items.push(materialLine(dm, `door:${d.id}`, roomId, `Ușă · gol ${Math.round(d.widthM * 100)} cm`, 1, mc.verifiedAt, 0)); unknown.push(`Montaj ușă ${dm.name}`); }
    if (hm) items.push(materialLine(hm, `handle:${d.id}`, roomId, `Mâner ușă · gol ${Math.round(d.widthM * 100)} cm`, 1, mc.verifiedAt, 0));
    // sistemul ușii (șina glisantei, caseta ușii în perete, kitul pliantei): doar pe tipul pentru care e făcut
    const sm = M(c?.system); if (sm && sm.specs?.system === d.type) items.push(materialLine(sm, `doorsys:${d.id}`, roomId, `Sistem ușă · gol ${Math.round(d.widthM * 100)} cm`, 1, mc.verifiedAt, 0)); }
  // adeziv pentru toate suprafețele placate (o singură comandă)
  const ad = mc.materials.find(m => m.category === 'tile_adhesive');
  if (ad && adhesiveArea > 0) items.push(materialLine(ad, 'adhesive', null, `Adeziv gresie/faianță (${r2(adhesiveArea)} m²)`, adhesiveArea * (ad.consumption || 0), mc.verifiedAt));
  // mobilier, sanitare, electrocasnice din plan (o linie pe piesă; electrocasnicele bucătăriei din selecții)
  // o piesă pe comandă (dimensiuni proprii) nu are preț de catalog: rămâne necunoscută până la oferta producătorului
  const lineFor = (key: string, roomId: string | null, vid: string, custom?: { w: number; d: number; h: number }) => { const rv = resolve(cat, vid); const g = groupOf(vid);
    if (!rv){ unknown.push(vid); return; } const price = custom ? null : rv.offer?.price ?? null; const label = custom ? `${rv.variant.name} — pe comandă ${custom.w}×${custom.d}×${custom.h} cm` : rv.variant.name; if (price == null) unknown.push(label);
    items.push({ key, category: SANITARY.has(g) ? 'sanitary' : APPLIANCES.has(g) ? 'appliances' : 'furniture', roomId, label, refId: vid, netQty: 1, unit: 'buc', wastePct: 0, orderedQty: 1, packs: null, packLabel: null,
      unitPrice: price, total: price, supplier: rv.offer?.provenance.source || 'UNKNOWN', sourceUrl: rv.offer?.provenance.sourceUrl ?? null, verifiedAt: rv.offer?.provenance.verifiedAt ?? null, confidence: rv.offer?.provenance.confidence ?? 'UNKNOWN' }); };
  // fiecare piesă sanitară e produsul ales (walk-in, WC suspendat, cadă...): se plătește exact ce s-a ales
  for (const p of snap.placements){ const room = p.roomId ? floors(snap).flatMap(f => f.rooms).find(r => r.id === p.roomId) : undefined, bb = room?.type === 'baie' && snap.finishes?.[room.id]?.bath ? bathOf(finishesOf(snap, room)) : null;
    lineFor(p.id, p.roomId, p.variantId, p.size); }
  const kitchen = snap.placements.find(p => p.group === 'bucatarie');
  if (kitchen) for (const g of ['plita', 'cuptor', 'hota']) lineFor(`${kitchen.id}:${g}`, kitchen.roomId, snap.selections[g] || `${g}-0`);
  return { items, labor, geometry, unknown };
}

// ---------- 3) buget ----------
export interface BudgetLine { key: string; label: string; amount: number | null; confidence: Confidence; note?: string; sourceUrl?: string | null }
export function computeBudget(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, custom: { price: number | null; qty: number }[] = []){
  const s = budgetOf(snap), { items, labor, geometry, unknown } = computeBOQ(snap, cat, mc);
  const sum = (c: BoqCategory) => r2(items.filter(i => i.category === c).reduce((a, i) => a + (i.total ?? 0), 0));
  const cats: Record<string, number> = { furniture: sum('furniture'), finishes: sum('finishes'), lighting: sum('lighting'), appliances: sum('appliances'), sanitary: sum('sanitary'), textiles: sum('textiles') };
  // produsele proprii (din link, CSV sau scrise de mână) intră în deviz la prețul de vânzare, ca cele din catalog
  if (custom.length) cats.custom = r2(custom.reduce((a, i) => a + (i.price ?? 0) * i.qty, 0));
  const laborTotals = { low: r2(labor.reduce((a, l) => a + l.low, 0)), expected: r2(labor.reduce((a, l) => a + l.expected, 0)), high: r2(labor.reduce((a, l) => a + l.high, 0)) };
  const svc = (id: string) => mc.services.find(x => x.id === id);
  const extra: BudgetLine[] = [];
  const hasIkea = items.some(i => i.supplier === 'IKEA'), hasDedeman = items.some(i => i.supplier === 'Dedeman');
  if (hasIkea && s.deliveryIkea){ const d = svc('livrare-ikea-z1'); extra.push({ key: 'transport-ikea', label: 'Transport IKEA', amount: d?.price ?? null, confidence: d?.confidence ?? 'UNKNOWN', note: d?.note, sourceUrl: d?.sourceUrl }); }
  if (hasDedeman) extra.push({ key: 'transport-dedeman', label: 'Transport Dedeman', amount: s.deliveryDedeman, confidence: s.deliveryDedeman == null ? 'UNKNOWN' : 'MEDIUM', note: s.deliveryDedeman == null ? 'Se calculează la comandă pe dedeman.ro; introdu valoarea ofertei.' : 'Valoare introdusă manual.' });
  // celelalte magazine (JYSK, Mobexpert): transportul se află doar la comandă, deci rămâne necunoscut, nu 0
  for (const sup of [...new Set(items.filter(i => i.category === 'furniture' || i.category === 'appliances' || i.category === 'sanitary').map(i => i.supplier).filter((x): x is string => !!x && x !== 'IKEA' && x !== 'Dedeman' && x !== 'UNKNOWN'))].sort())
    extra.push({ key: `transport-${sup.toLowerCase()}`, label: `Transport ${sup}`, amount: null, confidence: 'UNKNOWN', note: 'Se calculează la comandă, pe site-ul magazinului.' });
  const kit = snap.placements.find(p => p.group === 'bucatarie'), kitW = kit ? (resolve(cat, kit.variantId)?.w || 0) : 0;
  if (kit && s.kitchenAssembly){ const k = svc('montaj-bucatarie-ikea'); extra.push({ key: 'montaj-bucatarie', label: `Montaj bucătărie (${formatQty(r2(kitW), 'ml', 'ro')})`, amount: k?.pricePerMeter ? r2(k.pricePerMeter * kitW) : null, confidence: k?.confidence ?? 'UNKNOWN', note: k?.note, sourceUrl: k?.sourceUrl }); }
  extra.push({ key: 'montaj-mobilier', label: 'Montaj mobilier', amount: s.furnitureAssembly, confidence: s.furnitureAssembly == null ? 'UNKNOWN' : 'MEDIUM', note: s.furnitureAssembly == null ? 'Depinde de ofertă (platformele de servicii arată intervale foarte largi); introdu valoarea primită.' : 'Valoare introdusă manual.' });
  // onorariul de proiectare NU mai e o linie a bugetului: are o singură sursă, contractul (core/contract.ts migratedFee preia
  // vechiul `design` de aici), și intră în total prin lanțul canonic (core/commercial.ts projectChain)
  const known = extra.filter(e => e.amount != null), unknownLines = extra.filter(e => e.amount == null);
  const extrasTotal = r2(known.reduce((a, e) => a + (e.amount || 0), 0));
  const mk = (scen: 'low' | 'expected' | 'high') => { const lab = s.includeLabor ? laborTotals[scen] : 0; const subtotal = r2(Object.values(cats).reduce((a, v) => a + v, 0) + lab + extrasTotal);
    const contingency = r2(subtotal * s.contingencyPct / 100), total = r2(subtotal + contingency), vat = r2(total - total / (1 + VAT_RATE)); return { labor: lab, subtotal, contingency, total, vat }; };
  const scen = { low: mk('low'), expected: mk('expected'), high: mk('high') }, chosen = scen[s.laborScenario];
  const target = s.target, diff = target != null ? r2(target - chosen.total) : null;
  return { settings: s, categories: cats, laborTotals, extras: known, unknownLines, unknownItems: unknown, scenarios: scen, chosen, target, diff,
    status: diff == null ? 'none' : diff >= 0 ? 'under' : 'over', items, labor, geometry, vatRate: VAT_RATE };
}
export type Budget = ReturnType<typeof computeBudget>;
