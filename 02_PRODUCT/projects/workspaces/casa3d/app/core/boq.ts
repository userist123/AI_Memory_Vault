import type { Catalog, Snapshot, Room, MaterialsCatalog, Material, RoomFinishes, BudgetSettings, Confidence } from './types';
import { resolve, groupOf } from './catalog';
import { area as rectArea } from './geometry';
import { openingsOnSide } from './validate';
import { floors, floorOfRoom } from './levels';
import { layoutOf, floorWaste, floorLaborId, sideGeometry, wallpaperRolls, panelCount, pieceSizeCm, ceilingOf, FEATURE_CATEGORY, FEATURE_LABOR } from './finishes';
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
export function defaultFinishes(room: Room): RoomFinishes {
  if (room.type === 'baie') return { floor: 'gresie-mckinley', wallPaint: 'vopsea-innenweiss', wallTile: 'faianta-grafen', baseboard: null, light: 'lampa-virrmo' };
  if (room.type === 'bucatarie') return { floor: 'gresie-mckinley', wallPaint: 'vopsea-innenweiss', wallTile: 'faianta-lumiere', baseboard: null, light: 'lampa-virrmo' };
  return { floor: 'parchet-egger-h2099', wallPaint: 'vopsea-innenweiss', wallTile: null, baseboard: 'plinta-mdf-60', light: 'lampa-virrmo' };
}
export const finishesOf = (snap: Snapshot, room: Room): RoomFinishes => ({ ...defaultFinishes(room), ...(snap.finishes?.[room.id] || {}) });
export const budgetOf = (snap: Snapshot): BudgetSettings => ({ ...DEFAULT_BUDGET, ...(snap.budget || {}) });

// ---------- 2) BOQ ----------
const PATTERN_LABEL: Record<string, string> = { straight: 'drept', brick: 'decalat 1/2', third: 'decalat 1/3', diagonal: 'diagonală', herringbone: 'spic', chevron: 'chevron', checker: 'șah' };
const FEATURE_LABEL: Record<string, string> = { wallpaper: 'Tapet', slats: 'Riflaj', plaster: 'Tencuială decorativă', brick: 'Cărămidă aparentă', stone: 'Piatră decorativă', tile: 'Faianță' };
export type BoqCategory = 'furniture' | 'finishes' | 'lighting' | 'appliances' | 'sanitary';
export interface BoqItem { key: string; category: BoqCategory; roomId: string | null; label: string; refId: string; netQty: number; unit: string; wastePct: number;
  orderedQty: number; packs: number | null; packLabel: string | null; unitPrice: number | null; total: number | null; supplier: string; sourceUrl: string | null; verifiedAt: string | null; confidence: Confidence; note?: string }
export interface LaborItem { key: string; roomId: string; rateId: string; label: string; qty: number; unit: string; low: number; expected: number; high: number; confidence: Confidence; sources: { name: string; url: string }[] }

function materialLine(m: Material, key: string, roomId: string | null, label: string, net: number, catalogVerifiedAt: string, wasteOverride?: number): BoqItem {
  const waste = wasteOverride ?? WASTE[m.category] ?? 0, need = net * (1 + waste), verifiedAt = m.verifiedAt || catalogVerifiedAt;
  let packs: number | null = null, ordered = need, total: number;
  if (m.pack){ packs = Math.ceil(need / m.pack.size - 1e-9); ordered = packs * m.pack.size; total = m.pack.price != null ? packs * m.pack.price : ordered * m.unitPrice; }
  else { ordered = m.unit === 'buc' ? Math.ceil(need - 1e-9) : need; total = ordered * m.unitPrice; }
  return { key, category: m.category === 'lighting' || m.category === 'spot' || m.category === 'led_strip' ? 'lighting' : 'finishes', roomId, label, refId: m.id, netQty: r2(net), unit: m.unit, wastePct: waste, orderedQty: r2(ordered), packs, packLabel: m.pack?.label ?? null,
    unitPrice: m.unitPrice, total: r2(total), supplier: m.supplier, sourceUrl: m.sourceUrl, verifiedAt, confidence: m.confidence, note: m.note };
}
export function computeBOQ(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog){
  const items: BoqItem[] = [], labor: LaborItem[] = [], geometry: RoomGeometry[] = [], unknown: string[] = [];
  const M = (id?: string | null) => (id ? mc.materials.find(m => m.id === id) : undefined), L = (id: string) => mc.labor.find(l => l.id === id)!;
  const addLabor = (roomId: string, rateId: string, qty: number) => { if (qty <= 0) return; const l = L(rateId);
    labor.push({ key: `${roomId}:${rateId}`, roomId, rateId, label: l.label, qty: r2(qty), unit: l.unit, low: r2(qty * l.low), expected: r2(qty * l.expected), high: r2(qty * l.high), confidence: l.confidence, sources: l.sources }); };
  let adhesiveArea = 0;
  for (const room of floors(snap).flatMap(f => f.rooms)){
    const g = roomGeometry(snap, room), f = finishesOf(snap, room), fl = floorOfRoom(snap, room.id); geometry.push(g);
    // pardoseală: pierderile și manopera depind de modul de așezare (drept, decalat, diagonală, spic, chevron) și de format
    const fm = M(f.floor); if (fm){ const lay = layoutOf(f, fm), [pl, pw] = pieceSizeCm(fm);
      items.push(materialLine(fm, `${room.id}:floor`, room.id, `Pardoseală · ${room.name} (${PATTERN_LABEL[lay.pattern]}${fm.category === 'floor_tile' ? `, ${pl}×${pw} cm, rost ${lay.groutMm ?? 3} mm` : ''})`, g.floorArea, mc.verifiedAt, floorWaste(lay, fm)));
      if (fm.category === 'floor_tile') adhesiveArea += g.floorArea; addLabor(room.id, floorLaborId(lay, fm), g.floorArea); }
    if (room.type === 'baie') addLabor(room.id, 'manopera-hidroizolatie', g.floorArea);
    // faianță: baie până la 2,1 m; bucătărie = zona dintre blat și dulapuri, pe lungimea mobilierului de bucătărie
    let tileArea = 0;
    if (room.type === 'baie') tileArea = Math.max(0, g.perimeter * BATH_TILE_HEIGHT - g.doorWidth * DOOR_HEIGHT - g.windowWidth * Math.max(0, BATH_TILE_HEIGHT - .9));
    if (room.type === 'bucatarie'){ const k = snap.placements.find(p => p.roomId === room.id && p.group === 'bucatarie'), rv = k && resolve(cat, k.variantId); tileArea = rv ? rv.w * BACKSPLASH_HEIGHT : 0; }
    const wt = M(f.wallTile); if (wt && tileArea > 0){ items.push(materialLine(wt, `${room.id}:walltile`, room.id, `Faianță · ${room.name}`, tileArea, mc.verifiedAt)); adhesiveArea += tileArea; addLabor(room.id, 'manopera-faianta', tileArea); }
    // placări pe pereți (tapet, riflaj, tencuială decorativă, cărămidă, piatră, faianță), fiecare pe latura ei, până la înălțimea aleasă
    let featureArea = 0;
    for (const [i, wf] of (f.wallFeatures || []).entries()){ const m = M(wf.material), sg = sideGeometry(fl, room, wf.side, wf.heightM); if (sg.netM2 <= 0) continue;
      if (!m || m.category !== FEATURE_CATEGORY[wf.kind]){ unknown.push(`${room.name}: ${wf.kind} ${wf.side}`); continue; }
      featureArea += sg.netM2; const key = `${room.id}:wall:${wf.side}:${i}`, where = `${room.name}, perete ${wf.side} (${r2(sg.lengthM)} × ${r2(sg.heightM)} m)`;
      if (wf.kind === 'wallpaper' && m.specs?.roll){ const rr = wallpaperRolls(sg.lengthM, sg.heightM, m.specs.roll); items.push({ ...materialLine(m, key, room.id, `Tapet · ${where}: ${rr.strips} fâșii, ${rr.perRoll} pe rolă`, rr.rolls, mc.verifiedAt, 0) }); }
      else if (wf.kind === 'slats' && m.specs?.sizeCm) items.push(materialLine(m, key, room.id, `Riflaj · ${where}`, panelCount(sg.lengthM, sg.heightM, m.specs.sizeCm), mc.verifiedAt, 0));
      else if (wf.kind === 'plaster') items.push(materialLine(m, key, room.id, `Tencuială decorativă · ${where} (${r2(sg.netM2)} m²)`, sg.netM2 * (m.consumption || 0), mc.verifiedAt));
      else { items.push(materialLine(m, key, room.id, `${FEATURE_LABEL[wf.kind]} · ${where}`, sg.netM2, mc.verifiedAt)); if (wf.kind === 'tile') adhesiveArea += sg.netM2; }
      const lab = FEATURE_LABOR[wf.kind]; if (lab) addLabor(room.id, lab, sg.netM2); else unknown.push(`Manoperă ${FEATURE_LABEL[wf.kind].toLowerCase()} · ${where}`); }
    // tavan fals / scafă luminoasă / cornișă / spoturi
    const c = ceilingOf(f), w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0;
    let ceilingBand = 0;
    if (c.type !== 'flat'){ const gk = mc.materials.find(m => m.category === 'plasterboard'); ceilingBand = g.perimeter * c.dropCm / 100;
      if (gk){ items.push(materialLine(gk, `${room.id}:ceiling`, room.id, `Tavan fals gips-carton · ${room.name} (coborât ${c.dropCm} cm${c.type === 'cove' ? `, scafă ${c.coveCm} cm` : ''})`, g.ceiling + ceilingBand, mc.verifiedAt)); addLabor(room.id, 'manopera-rigips-tavan', g.ceiling + ceilingBand); }
      if (c.type === 'cove'){ const inner = 2 * (Math.max(0, w - 2 * c.coveCm / 100) + Math.max(0, d - 2 * c.coveCm / 100)); addLabor(room.id, 'manopera-scafa', inner);
        const led = M(c.led); if (led) items.push(materialLine(led, `${room.id}:led`, room.id, `Bandă LED scafă · ${room.name} (${r2(inner)} m)`, inner, mc.verifiedAt)); } }
    const cm = M(c.cornice); if (cm){ items.push(materialLine(cm, `${room.id}:cornice`, room.id, `Cornișă · ${room.name}`, g.perimeter, mc.verifiedAt)); addLabor(room.id, 'manopera-cornisa', g.perimeter); }
    const sm = M(c.spot); if (sm && c.spots > 0) items.push(materialLine(sm, `${room.id}:spots`, room.id, `Spoturi încastrate · ${room.name}`, c.spots, mc.verifiedAt));
    // vopsea: pereți (fără zona placată) + tavan (și marginea tavanului fals), 2 straturi
    const paintArea = Math.max(0, g.wallNet - (wt ? tileArea : 0) - featureArea) + g.ceiling + ceilingBand, pm = M(f.wallPaint);
    if (pm && pm.coverage){ const litres = paintArea * PAINT_COATS / pm.coverage; const line = materialLine(pm, `${room.id}:paint`, room.id, `Vopsea pereți + tavan · ${room.name} (${r2(paintArea)} m², ${PAINT_COATS} straturi)`, litres, mc.verifiedAt); items.push(line); addLabor(room.id, 'manopera-zugravit', paintArea); }
    // plintă (doar unde nu e placat)
    const bm = M(f.baseboard); if (bm && !WET_ROOMS.has(room.type)){ const len = Math.max(0, g.perimeter - g.doorWidth); items.push(materialLine(bm, `${room.id}:baseboard`, room.id, `Plintă · ${room.name}`, len, mc.verifiedAt)); addLabor(room.id, 'manopera-plinta', len); }
    // iluminat general
    const lm = M(f.light); if (lm){ const n = f.lights ?? (1 + Math.max(0, Math.ceil((g.floorArea - LIGHTS_EXTRA_PER_M2) / LIGHTS_EXTRA_PER_M2))); const line = materialLine(lm, `${room.id}:light`, room.id, `Iluminat · ${room.name}`, n, mc.verifiedAt); items.push(line); }
  }
  // adeziv pentru toate suprafețele placate (o singură comandă)
  const ad = mc.materials.find(m => m.category === 'tile_adhesive');
  if (ad && adhesiveArea > 0) items.push(materialLine(ad, 'adhesive', null, `Adeziv gresie/faianță (${r2(adhesiveArea)} m²)`, adhesiveArea * (ad.consumption || 0), mc.verifiedAt));
  // mobilier, sanitare, electrocasnice din plan (o linie pe piesă; electrocasnicele bucătăriei din selecții)
  // o piesă pe comandă (dimensiuni proprii) nu are preț de catalog: rămâne necunoscută până la oferta producătorului
  const lineFor = (key: string, roomId: string | null, vid: string, custom?: { w: number; d: number; h: number }) => { const rv = resolve(cat, vid); const g = groupOf(vid);
    if (!rv){ unknown.push(vid); return; } const price = custom ? null : rv.offer?.price ?? null; const label = custom ? `${rv.variant.name} — pe comandă ${custom.w}×${custom.d}×${custom.h} cm` : rv.variant.name; if (price == null) unknown.push(label);
    items.push({ key, category: SANITARY.has(g) ? 'sanitary' : APPLIANCES.has(g) ? 'appliances' : 'furniture', roomId, label, refId: vid, netQty: 1, unit: 'buc', wastePct: 0, orderedQty: 1, packs: null, packLabel: null,
      unitPrice: price, total: price, supplier: rv.offer?.provenance.source || 'UNKNOWN', sourceUrl: rv.offer?.provenance.sourceUrl ?? null, verifiedAt: rv.offer?.provenance.verifiedAt ?? null, confidence: rv.offer?.provenance.confidence ?? 'UNKNOWN' }); };
  for (const p of snap.placements) lineFor(p.id, p.roomId, p.variantId, p.size);
  const kitchen = snap.placements.find(p => p.group === 'bucatarie');
  if (kitchen) for (const g of ['plita', 'cuptor', 'hota']) lineFor(`${kitchen.id}:${g}`, kitchen.roomId, snap.selections[g] || `${g}-0`);
  return { items, labor, geometry, unknown };
}

// ---------- 3) buget ----------
export interface BudgetLine { key: string; label: string; amount: number | null; confidence: Confidence; note?: string; sourceUrl?: string | null }
export function computeBudget(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog){
  const s = budgetOf(snap), { items, labor, geometry, unknown } = computeBOQ(snap, cat, mc);
  const sum = (c: BoqCategory) => r2(items.filter(i => i.category === c).reduce((a, i) => a + (i.total ?? 0), 0));
  const cats: Record<string, number> = { furniture: sum('furniture'), finishes: sum('finishes'), lighting: sum('lighting'), appliances: sum('appliances'), sanitary: sum('sanitary') };
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
  if (kit && s.kitchenAssembly){ const k = svc('montaj-bucatarie-ikea'); extra.push({ key: 'montaj-bucatarie', label: `Montaj bucătărie (${r2(kitW)} ml)`, amount: k?.pricePerMeter ? r2(k.pricePerMeter * kitW) : null, confidence: k?.confidence ?? 'UNKNOWN', note: k?.note, sourceUrl: k?.sourceUrl }); }
  extra.push({ key: 'montaj-mobilier', label: 'Montaj mobilier', amount: s.furnitureAssembly, confidence: s.furnitureAssembly == null ? 'UNKNOWN' : 'MEDIUM', note: s.furnitureAssembly == null ? 'Depinde de ofertă (platformele de servicii arată intervale foarte largi); introdu valoarea primită.' : 'Valoare introdusă manual.' });
  if (s.design > 0) extra.push({ key: 'proiectare', label: 'Proiectare / design', amount: s.design, confidence: 'MEDIUM', note: 'Valoare introdusă manual.' });
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
