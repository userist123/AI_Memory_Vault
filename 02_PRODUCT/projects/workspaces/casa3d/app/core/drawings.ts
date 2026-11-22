// Planșele pentru echipe, calculate din același proiect ca bugetul și modelul 3D:
//  • planul de tavan și iluminat (tipul tavanului, scafa, spoturile, corpurile plasate, iluminarea estimată),
//  • planul de pardoseli (produsul, modul de așezare, direcția, rostul, suprafața),
//  • elevațiile pereților (desfășurarea fiecărui perete văzut din cameră: goluri, benzi de placare, faianță, baghetă, aplice).
// Aici e doar geometria; desenul SVG e în components/Drawings.tsx.
import type { Catalog, Floor, MaterialsCatalog, Room, Snapshot, WallFeature } from './types';
import { finishesOf } from './boq';
import { ceilingOf, layoutOf, materialOf, pieceSizeCm, bandOf, bathTiles, alongOf, frameRects } from './finishes';
import { fixturePoints, fixtureCount, type FixturePoint } from './fixtures';
import { lightReport, autoLightCount } from './light-design';
import { DOOR_HEIGHT } from './rules.boq';
import { wallItemsOnSide } from './wall-items';
import type { WallItemKind } from './types';
import { activeOpenings } from './renovation';
import type { TechKind, TechPoint } from './technical';
import { techFrame, isSizedRadiator, radiatorSize, hvacSize } from './tech-geometry';

type Side = WallFeature['side'];
export const SIDES: Side[] = ['N', 'E', 'S', 'W'];

// ---------- tavan și iluminat ----------
/** Spoturile încastrate pe o grilă regulată (aceeași regulă ca în modelul 3D). */
export function spotGrid(r: Room['rect'], n: number): [number, number][] {
  if (n <= 0) return []; const w = r.x1 - r.x0, d = r.z1 - r.z0, cols = Math.max(1, Math.round(Math.sqrt(n * w / d))), rows = Math.ceil(n / cols);
  return Array.from({ length: n }, (_, i) => [r.x0 + w * ((i % cols) + .5) / cols, r.z0 + d * (Math.floor(i / cols) + .5) / rows]);
}
export interface CeilingRoom { roomId: string; name: string; rect: Room['rect']; type: 'flat' | 'drop' | 'cove'; heightM: number; dropCm: number;
  cove: Room['rect'] | null; ledM: number | null; spots: [number, number][]; spotId: string | null; centerLights: number; lightId: string;
  fixtures: (FixturePoint & { material: string | null; index: number })[]; cornice: boolean; lux: number; target: [number, number] | null }
export function ceilingPlan(snap: Snapshot, mc: MaterialsCatalog, cat: Catalog, fl: Floor): CeilingRoom[] {
  return fl.rooms.map(room => { const f = finishesOf(snap, room), c = ceilingOf(f), r = room.rect, k = c.coveCm / 100;
    const cove = c.type === 'cove' && r.x1 - r.x0 - 2 * k > .3 && r.z1 - r.z0 - 2 * k > .3 ? { x0: r.x0 + k, z0: r.z0 + k, x1: r.x1 - k, z1: r.z1 - k } : null;
    const rep = lightReport(mc, fl, room, f);
    return { roomId: room.id, name: room.name, rect: r, type: c.type, heightM: Math.round((fl.ceilingHeight - (c.type === 'flat' ? 0 : c.dropCm / 100)) * 100) / 100, dropCm: c.dropCm,
      cove, ledM: cove && c.led ? Math.round(2 * ((cove.x1 - cove.x0) + (cove.z1 - cove.z0)) * 100) / 100 : null,
      spots: c.spot ? spotGrid(r, c.spots) : [], spotId: c.spot ?? null, centerLights: f.lights ?? autoLightCount((r.x1 - r.x0) * (r.z1 - r.z0)), lightId: f.light,
      fixtures: (f.fixtures || []).flatMap((fx, i) => fixturePoints(snap, cat, room, fx).map(p => ({ ...p, material: fx.material ?? null, index: i }))),
      cornice: !!c.cornice, lux: rep.lux, target: rep.target }; });
}
/** Legenda planului de iluminat: fiecare produs cu numărul de bucăți din tot nivelul. */
export function lightingLegend(snap: Snapshot, mc: MaterialsCatalog, fl: Floor){
  const m = new Map<string, { kind: string; id: string | null; name: string; count: number; unit: string }>();
  const add = (kind: string, id: string | null, count: number, unit = 'buc') => { if (count <= 0) return; const key = `${kind}:${id}`, cur = m.get(key);
    if (cur) cur.count = Math.round((cur.count + count) * 100) / 100; else m.set(key, { kind, id, name: materialOf(mc, id)?.name ?? '', count, unit }); };
  for (const room of fl.rooms){ const f = finishesOf(snap, room), c = ceilingOf(f), r = room.rect;
    add('center', f.light, f.lights ?? autoLightCount((r.x1 - r.x0) * (r.z1 - r.z0)));
    if (c.spot) add('spot', c.spot, c.spots);
    if (c.type === 'cove' && c.led){ const k = c.coveCm / 100; add('led', c.led, Math.round(2 * (Math.max(0, r.x1 - r.x0 - 2 * k) + Math.max(0, r.z1 - r.z0 - 2 * k)) * 100) / 100, 'm'); }
    for (const fx of f.fixtures || []) add(fx.kind, fx.material ?? null, fixtureCount(fx)); }
  return [...m.values()];
}

// ---------- pardoseli ----------
export interface FloorRoom { roomId: string; name: string; rect: Room['rect']; materialId: string; name2: string; kind: 'parquet' | 'tile' | 'other'; pattern: string; angle: 0 | 90;
  pieceCm: [number, number]; groutMm: number | null; groutColor: string | null; areaM2: number; color: string }
export function floorPlan(snap: Snapshot, mc: MaterialsCatalog, fl: Floor): FloorRoom[] {
  return fl.rooms.map(room => { const f = finishesOf(snap, room), m = materialOf(mc, f.floor), lay = layoutOf(f, m), r = room.rect, tile = m?.category === 'floor_tile';
    return { roomId: room.id, name: room.name, rect: r, materialId: f.floor, name2: m?.name ?? f.floor, kind: m?.category === 'parquet' ? 'parquet' : tile ? 'tile' : 'other', pattern: lay.pattern, angle: lay.angle ?? 0,
      pieceCm: pieceSizeCm(m), groutMm: tile ? lay.groutMm ?? 3 : null, groutColor: tile ? lay.groutColor ?? '#bdb8ae' : null, areaM2: Math.round((r.x1 - r.x0) * (r.z1 - r.z0) * 100) / 100, color: m?.specs?.color ?? '#d9d2c4' }; });
}

// ---------- elevații ----------
export interface ElevOpening { kind: 'door' | 'window'; u0: number; u1: number; y0: number; y1: number }
/** `u0`–`u1` = porțiunea pe lungime, în coordonata elevației (de la stânga la dreapta, privind peretele din cameră). */
/** `frames` doar la boiserie: chenarele casetelor [u0, y0, u1, y1], în coordonatele elevației (citită ca banda). */
export interface ElevBand { kind: WallFeature['kind'] | 'walltile'; y0: number; y1: number; u0: number; u1: number; material: string; name: string; color: string | null; frames?: [number, number, number, number][] }
/** `items` = obiectele de pe perete (centrul `u`, `y`, lățimea și înălțimea, m), cu cota centrului desenată pe elevație. */
export interface Elevation { side: Side; lengthM: number; heightM: number; openings: ElevOpening[]; bands: ElevBand[]; baseboardM: number | null; sconces: { u: number; y: number }[]; ceilingDropM: number;
  items: { id: string; kind: WallItemKind; u: number; y: number; w: number; h: number }[];
  /** punctele tehnice DECLARATE (`snap.tech`) de pe latura aceasta; niciodată sugestii (art. 4: fără valori inventate) */
  tech: ElevTech[];
  /** `false` = proiectul nu are `snap.tech`: desfășurata nu arată niciun punct, iar planșa o spune */
  techDeclared: boolean;
  /** lanțul de cote orizontale, de la colțul stâng la fiecare punct tehnic și margine de gol, ordonat de la stânga */
  dims: ElevDim[] }
/** Punct tehnic pe desfășurată: `u` = poziția reală de la colțul stâng (m), `y` = înălțimea de la pardoseala finită (câmpul `height`),
 *  `du` = decalajul DOAR al simbolului desenat când mai multe puncte stau în același loc (cota din lanț rămâne cea reală),
 *  `body` = corpul (calorifer cu mărimi, unitate AC): lățime și înălțime, cu marginea de jos la `y`. */
export interface ElevTech { id: string; kind: TechKind; u: number; y: number; du: number; body?: { w: number; h: number } }
/** O cotă din lanț: `cm` de la colțul stâng; `row` = rândul (0 = cel de pe linia pardoselii), urcat când textele s-ar suprapune. */
export interface ElevDim { u: number; cm: number; row: number; ref: 'tech' | 'opening' }
/** Punctul de lumină e pe tavan, nu pe perete: nu apare pe desfășurate (e pe planul de tavan). */
export const ELEV_TECH_KINDS: TechKind[] = ['outlet', 'outlet_double', 'switch', 'data', 'cooker', 'water_cold', 'water_hot', 'drain', 'hvac_split', 'thermostat', 'vent_exhaust', 'radiator'];
/** Cât de departe de latura camerei mai e „pe perete” un punct (m) — un sifon în mijlocul camerei nu se proiectează pe un perete. */
export const ELEV_TECH_WALL_M = .3;
/** Mărimea textului din lanțul de cote și pasul dintre rânduri (m în desen; la 1:50 = 1,4 mm pe hârtie). */
export const ELEV_DIM_FONT_M = .07, ELEV_DIM_ROW_M = .09;
const SYMBOL_STEP_M = .125;
/** Punctele tehnice declarate ale camerei, pe latura lor (cea mai apropiată, ca în 3D: `techFrame`), în coordonata elevației. */
export function techOnSide(snap: Pick<Snapshot, 'tech'>, fl: Floor, room: Room, side: Side): ElevTech[] {
  if (!snap.tech || !fl.rooms.some(q => q.id === room.id)) return [];
  const r = room.rect, horiz = side === 'N' || side === 'S', len = horiz ? r.x1 - r.x0 : r.z1 - r.z0, edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[side], r3 = (v: number) => Math.round(v * 1000) / 1000;
  const pts = snap.tech.filter((p: TechPoint) => p.roomId === room.id && ELEV_TECH_KINDS.includes(p.kind) && techFrame(fl, p)?.side === side && Math.abs((horiz ? p.z : p.x) - edge) <= ELEV_TECH_WALL_M)
    .map(p => { const u = r3(Math.max(0, Math.min(len, alongWall(r, side, p.x, p.z)))), body = p.kind === 'hvac_split' ? hvacSize(p) : isSizedRadiator(p) ? radiatorSize(p) : null;
      return { id: p.id, kind: p.kind, u, y: body ? Math.max(0, Math.min(fl.ceilingHeight - body.h - .02, p.height)) : p.height, du: 0, ...(body ? { body: { w: body.w, h: body.h } } : {}) }; })
    .sort((a, b) => a.u - b.u || a.y - b.y || (a.id < b.id ? -1 : 1));
  // simbolurile care ar cădea unul peste altul (priză + date în același loc) se așază alăturat; poziția cotată rămâne `u`
  const placed: [number, number][] = [];
  for (const p of pts){ const dir = p.u > len - .5 ? -1 : 1; let k = 0;
    while (k < 12 && placed.some(([x, y]) => Math.abs(x - (p.u + dir * k * SYMBOL_STEP_M)) < SYMBOL_STEP_M - .005 && Math.abs(y - p.y) < .19)) k++;
    p.du = r3(dir * k * SYMBOL_STEP_M); placed.push([p.u + p.du, p.y]); }
  return pts;
}
/** Lanțul de cote: de la colțul stâng la fiecare punct tehnic și la marginile golurilor, în cm, ordonat; două cote prea apropiate
 *  ca textele să încapă pe același rând trec pe rânduri diferite. */
export function dimensionChain(lengthM: number, openings: Pick<ElevOpening, 'u0' | 'u1'>[], tech: Pick<ElevTech, 'u'>[]): ElevDim[] {
  const seen = new Map<number, ElevDim>();
  const add = (u: number, ref: ElevDim['ref']) => { const cm = Math.round(u * 100); if (cm <= 0 || cm >= Math.round(lengthM * 100) || seen.has(cm)) return; seen.set(cm, { u: Math.round(u * 1000) / 1000, cm, row: 0, ref }); };
  for (const t of tech) add(t.u, 'tech'); for (const o of openings){ add(o.u0, 'opening'); add(o.u1, 'opening'); }
  const dims = [...seen.values()].sort((a, b) => a.cm - b.cm), right: number[] = [];
  for (const d of dims){ const half = (String(d.cm).length * .6 * ELEV_DIM_FONT_M + .03) / 2, left = d.u - half; let row = 0; while (right[row] != null && right[row]! > left) row++;
    d.row = row; right[row] = d.u + half; }
  return dims;
}
/** Poziția de-a lungul peretelui, de la stânga la dreapta, privind peretele din cameră. */
export function alongWall(r: Room['rect'], side: Side, x: number, z: number): number {
  return side === 'N' ? x - r.x0 : side === 'S' ? r.x1 - x : side === 'E' ? z - r.z0 : r.z1 - z;
}
export function wallElevations(snap: Snapshot, mc: MaterialsCatalog, cat: Catalog, fl: Floor, room: Room): Elevation[] {
  const f = finishesOf(snap, room), r = room.rect, H = fl.ceilingHeight, c = ceilingOf(f), base = materialOf(mc, f.baseboard), wet = room.type === 'baie' || room.type === 'bucatarie';
  const fixtures = (f.fixtures || []).flatMap(fx => fixturePoints(snap, cat, room, fx)).filter(p => p.kind === 'sconce');
  const tiles = bathTiles(f, room.type, room), tileM = materialOf(mc, f.wallTile);
  return SIDES.map(side => { const horiz = side === 'N' || side === 'S', len = horiz ? r.x1 - r.x0 : r.z1 - r.z0, edge = { N: r.z0, S: r.z1, W: r.x0, E: r.x1 }[side];
    const openings: ElevOpening[] = [];
    for (const w of fl.walls){ const h = Math.abs(w.a[1] - w.b[1]) < 1e-6; if (h !== horiz || Math.abs((h ? w.a[1] : w.a[0]) - edge) > 1e-6) continue;
      const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
      for (const o of activeOpenings(w)){ const pa = [w.a[0] + ux * o.offset, w.a[1] + uz * o.offset], pb = [w.a[0] + ux * (o.offset + o.width), w.a[1] + uz * (o.offset + o.width)];
        const ua = alongWall(r, side, pa[0]!, pa[1]!), ub = alongWall(r, side, pb[0]!, pb[1]!), u0 = Math.max(0, Math.min(ua, ub)), u1 = Math.min(len, Math.max(ua, ub)); if (u1 - u0 < .01) continue;
        const sill = o.kind === 'window' ? o.sill ?? .9 : 0, top = o.kind === 'window' ? sill + (o.height ?? 1.3) : Math.min(H, DOOR_HEIGHT);
        openings.push({ kind: o.kind === 'door' ? 'door' : 'window', u0, u1, y0: sill, y1: Math.min(H, top) }); } }
    // porțiunea se măsoară de la colțul x0/z0; elevația laturilor S și V se citește invers (de la x1/z1)
    const flip = side === 'S' || side === 'W', r3 = (v: number) => Math.round(v * 1000) / 1000;
    const bands: ElevBand[] = [...(f.wallFeatures || []).filter(w => w.side === side), ...tiles.filter(w => w.side === side)].map(w => { const m = materialOf(mc, w.material), [y0, y1] = bandOf(w, H), tile = tiles.includes(w), [a, b] = alongOf(w, len);
      return { kind: tile ? 'walltile' as const : w.kind, y0, y1: w.kind === 'rail' ? y0 + (m?.specs?.sizeCm?.[1] ?? 4) / 100 : y1, u0: r3(flip ? len - b : a), u1: r3(flip ? len - a : b), material: w.material, name: m?.name ?? w.material, color: w.color ?? m?.specs?.color ?? (tile ? tileM?.specs?.color ?? null : null),
        ...(w.kind === 'frames' ? { frames: frameRects(fl, room, w).map(([p, q, s, t]): [number, number, number, number] => [r3(flip ? len - s : p), q, r3(flip ? len - p : s), t]) } : {}) }; })
      .sort((a, b) => a.y0 - b.y0 || a.u0 - b.u0);
    const sconces = fixtures.filter(p => (horiz ? Math.abs(p.z - edge) : Math.abs(p.x - edge)) < .2).map(p => ({ u: Math.round(alongWall(r, side, p.x, p.z) * 1000) / 1000, y: p.y }));
    const tech = techOnSide(snap, fl, room, side);
    return { side, lengthM: Math.round(len * 1000) / 1000, heightM: H, openings: openings.sort((a, b) => a.u0 - b.u0), bands, baseboardM: !wet && base ? (base.specs?.sizeCm?.[1] ?? Number(base.name.match(/x (\d{2,3}) x \d+ mm/)?.[1] ?? 60) / 10) / 100 : null, sconces,
      ceilingDropM: c.type === 'flat' ? 0 : c.dropCm / 100, items: wallItemsOnSide(fl, room, side), tech, techDeclared: !!snap.tech, dims: dimensionChain(len, openings, tech) }; });
}
