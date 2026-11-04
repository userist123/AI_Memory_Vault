// Limitele elementelor de plan (structură, nișe, adnotări): o singură sursă pentru interfață (câmpurile din panou,
// uneltele) și pentru server (checkSnapshot din lib/repo.ts). Un client cu o limită mai largă decât serverul ar bloca
// salvarea întregului proiect (400), de aceea ambele citesc de aici. Funcții pure, fără I/O.
import type { Annotation, Beam, CeilingZone, Column, DoorType, Floor, FloorLayout, FloorPattern, FloorZone, GlassWall, Niche, Railing, RailingKind, RoomSpaceKind, WallItem, WallStatus } from './types';
import { DOOR_TYPES, DOOR_TYPE_RULES } from './rules';

export const PLAN_LIMITS = {
  coordM: 1000,
  column: { max: 200, minM: .05, maxM: 3, heightMinM: .3, heightMaxM: 6 },
  beam: { max: 200, minM: .05, maxM: 1.5 },
  glassWall: { max: 100, thickMinM: .01, thickMaxM: .3, mullionsMax: 20 },
  niche: { max: 100, widthMinM: .1, widthMaxM: 3, heightMinM: .1, heightMaxM: 3, depthMinM: .03, depthMaxM: .5, elevMaxM: 3 },
  annotation: { max: 300, textMax: 120, dimMinM: .01, offsetMaxM: 50 },
  passage: { widthMinM: .3, widthMaxM: 4 },
  wallItem: { max: 300, widthMinM: .05, widthMaxM: 4, heightMinM: .02, heightMaxM: 3, depthMaxM: .6, labelMax: 60 },
  /** zone de pardoseală: cel mult 100 pe nivel, latura de cel puțin 10 cm */
  zone: { max: 100, minM: .1, labelMax: 40, groutMaxMm: 20 },
  /** caloriferul (punct tehnic `radiator` cu mărimi): lățime 20–300 cm, înălțime 20–220 cm, cel mult 100 în proiect */
  radiator: { max: 100, widthMinM: .2, widthMaxM: 3, heightMinM: .2, heightMaxM: 2.2 },
  /** unitatea interioară de aer condiționat (`hvac_split`): lățime 50–150 cm, cel mult 40 în proiect */
  hvac: { max: 40, widthMinM: .5, widthMaxM: 1.5 },
  /** tavanul fals pe zonă: cel mult 100 pe nivel, latura de cel puțin 30 cm, coborât 5–60 cm (ca tavanul fals al camerei,
   *  FINISH_RULES.maxDropCm), scafa de cel mult 60 cm */
  ceilingZone: { max: 100, minM: .3, dropMinCm: 5, dropMaxCm: 60, coveMaxCm: 60, labelMax: 40 },
  /** balustrada: cel mult 200 pe nivel, înălțimea 30–200 cm; sub 90 cm e un avertisment (înălțimea minimă uzuală la balcon) */
  railing: { max: 200, minLenM: .2, heightMinM: .3, heightMaxM: 2, minSafeM: .9 },
} as const;
/** Dimensiunile implicite la plasare (m). */
export const PLAN_DEFAULTS = { passageWidth: .9, shaft: .3, niche: { width: .6, height: .4, depth: .1, elevation: 1.1 }, dimOffset: .3,
  radiator: { width: .8, height: .6, elevation: .15 }, hvac: { width: .8, height: .29, depth: .2 },
  ceilingZone: { dropCm: 15, coveCm: 10 }, railing: { height: 1 } } as const;
export const RAILING_KINDS: readonly RailingKind[] = ['metal', 'glass'];
/** Tipul spațiului camerei (Room.spaceKind): lipsă = interior, ca proiectele de până acum. */
export const SPACE_KINDS: readonly RoomSpaceKind[] = ['interior', 'open_balcony', 'covered_balcony', 'terrace', 'outdoor'];
export const WALL_STATUSES: readonly WallStatus[] = ['existing', 'new', 'demolish'];
export const RADIATOR_MODELS = ['panel', 'towel', 'decorative'] as const;
/** Modurile de așezare a pardoselii: o singură listă, folosită și de finisajele camerei (core/finishes.ts, PATTERNS). */
export const FLOOR_PATTERNS: readonly FloorPattern[] = ['straight', 'brick', 'third', 'diagonal', 'herringbone', 'chevron', 'checker'];
/** Referința la un material (pardoseală, vopsea, faianță...): text de cel mult 80 de caractere. Aceeași regulă pentru
 *  finisajele camerei (sanitizeFinishes) și pentru materialul unei zone de pardoseală (sanitizeZone). */
export const isMaterialRef = (x: unknown): x is string => typeof x === 'string' && x.length <= 80;

const ID = /^[\w-]{1,80}$/;
const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const inBox = (v: unknown) => num(v) && Math.abs(v) <= PLAN_LIMITS.coordM;
const pt = (p: unknown): p is [number, number] => Array.isArray(p) && p.length === 2 && inBox(p[0]) && inBox(p[1]);
const within = (v: unknown, lo: number, hi: number) => num(v) && v >= lo - 1e-9 && v <= hi + 1e-9;
const HEX = /^#[0-9a-f]{6}$/i;

/** Textul liber de pe plan: fără caractere de control, fără `<>` (ajunge în SVG, DXF și PDF) și fără comenzi de direcție
 *  (bidi, ar inversa vizual textul pe planșă), cel mult 120 de caractere. */
/** Doar fără caracterele interzise (pentru câmpurile în care se tastează: spațiile rămân); cleanPlanText curăță și marginile. */
export const stripPlanText = (s: unknown) => String(s ?? '').replace(/[\u0000-\u001f\u007f<>\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/g, ' ');
export const cleanPlanText = (s: unknown) => stripPlanText(s).replace(/\s+/g, ' ').trim().slice(0, PLAN_LIMITS.annotation.textMax);

export class PlanLimitError extends Error {}
const bad = (m: string): never => { throw new PlanLimitError(m); };
/** Câmpurile care existau înainte de aceste limite (stâlp, grindă, perete de sticlă): interfața veche le lăsa fără maxim,
 *  deci o valoare finită în afara intervalului se aduce la interval, nu blochează salvarea întregului proiect. */
const clamp = (v: unknown, lo: number, hi: number, what: string): number => num(v) && v > 0 ? Math.min(hi, Math.max(lo, v)) : bad(what);
const list = (v: unknown, max: number, what: string): any[] | null => { if (v == null) return null; if (!Array.isArray(v) || v.length > max) bad(`${what}: cel mult ${max} pe nivel.`); return v as any[]; };

export function sanitizeColumn(c: any): Column {
  const L = PLAN_LIMITS.column;
  if (!c || typeof c !== 'object' || typeof c.id !== 'string' || !ID.test(c.id) || !inBox(c.x) || !inBox(c.z)) bad('Un stâlp are poziția invalidă.');
  const w = clamp(c.w, L.minM, L.maxM, 'Un stâlp are dimensiuni invalide.'), d = clamp(c.d, L.minM, L.maxM, 'Un stâlp are dimensiuni invalide.');
  if (c.shape !== 'rect' && c.shape !== 'round') bad('Forma stâlpului este invalidă.');
  if (c.role != null && !['structural', 'shaft', 'boxing'].includes(c.role)) bad('Rolul stâlpului este invalid.');
  if (c.height != null && !within(c.height, L.heightMinM, L.heightMaxM)) bad(`Înălțimea ghenei trebuie să fie între ${L.heightMinM * 100} și ${L.heightMaxM * 100} cm.`);
  if (c.rotation != null && !num(c.rotation)) bad('Rotația stâlpului este invalidă.');
  return { id: c.id, x: c.x, z: c.z, w, d, shape: c.shape, ...(c.rotation != null ? { rotation: c.rotation } : {}),
    ...(typeof c.material === 'string' && c.material.length <= 80 ? { material: c.material } : {}),
    ...(c.role && c.role !== 'structural' ? { role: c.role } : {}), ...(c.height != null ? { height: c.height } : {}) };
}
export function sanitizeBeam(b: any): Beam {
  const L = PLAN_LIMITS.beam;
  if (!b || typeof b !== 'object' || typeof b.id !== 'string' || !ID.test(b.id) || !pt(b.a) || !pt(b.b)) bad('O grindă are capetele invalide.');
  const width = clamp(b.width, L.minM, L.maxM, 'O grindă are dimensiuni invalide.'), depth = clamp(b.depth, L.minM, L.maxM, 'O grindă are dimensiuni invalide.');
  return { id: b.id, a: [b.a[0], b.a[1]], b: [b.b[0], b.b[1]], width, depth };
}
export function sanitizeGlassWall(g: any): GlassWall {
  const L = PLAN_LIMITS.glassWall;
  if (!g || typeof g !== 'object' || typeof g.id !== 'string' || !ID.test(g.id) || !pt(g.a) || !pt(g.b)) bad('Un perete de sticlă are capetele invalide.');
  const thickness = clamp(g.thickness, L.thickMinM, L.thickMaxM, 'Grosimea peretelui de sticlă este invalidă.');
  if (g.mullions != null && !(Number.isInteger(g.mullions) && g.mullions >= 0)) bad('Numărul de montanți este invalid.');
  const mullions = g.mullions != null ? Math.min(L.mullionsMax, g.mullions) : null;
  return { id: g.id, a: [g.a[0], g.a[1]], b: [g.b[0], g.b[1]], thickness, ...(mullions != null ? { mullions } : {}), ...(typeof g.frameColor === 'string' && HEX.test(g.frameColor) ? { frameColor: g.frameColor } : {}) };
}
export function sanitizeNiche(n: any): Niche {
  const L = PLAN_LIMITS.niche;
  if (!n || typeof n !== 'object' || typeof n.id !== 'string' || !ID.test(n.id) || typeof n.wallId !== 'string' || !inBox(n.offset)) bad('O nișă este invalidă.');
  if (!within(n.width, L.widthMinM, L.widthMaxM) || !within(n.height, L.heightMinM, L.heightMaxM) || !within(n.depth, L.depthMinM, L.depthMaxM) || !within(n.elevation, 0, L.elevMaxM))
    bad(`O nișă are dimensiuni invalide (lățime ${L.widthMinM * 100}–${L.widthMaxM * 100} cm, adâncime ${L.depthMinM * 100}–${L.depthMaxM * 100} cm).`);
  if (n.side != null && n.side !== 1 && n.side !== -1) bad('Fața nișei este invalidă.');
  if (n.finish != null && !['tile', 'paint', 'led'].includes(n.finish)) bad('Finisajul nișei este invalid.');
  return { id: n.id, wallId: n.wallId, offset: n.offset, width: n.width, height: n.height, depth: n.depth, elevation: n.elevation, ...(n.side != null ? { side: n.side } : {}), ...(n.finish ? { finish: n.finish } : {}) };
}
const WALL_ITEM_KINDS = ['art', 'mirror', 'shelf', 'tv'];
/** Obiectul de pe perete curățat: doar câmpurile modelului. Poziția pe perete și cota se aduc în interval (un perete scurtat sau
 *  un tavan coborât nu blochează salvarea întregului proiect; validarea arată „iese din perete”); mărimile în afara limitelor se resping. */
export function sanitizeWallItem(it: any, wallLen: number | undefined, ceiling: number): WallItem {
  const L = PLAN_LIMITS.wallItem;
  if (!it || typeof it !== 'object' || typeof it.id !== 'string' || !ID.test(it.id) || typeof it.wallId !== 'string' || !WALL_ITEM_KINDS.includes(it.kind) || (it.side !== 1 && it.side !== -1)) bad('Un obiect de pe perete este invalid.');
  if (!within(it.width, L.widthMinM, L.widthMaxM) || !within(it.height, L.heightMinM, L.heightMaxM) || (it.depth != null && !within(it.depth, 0, L.depthMaxM)))
    bad(`Un obiect de pe perete are dimensiuni invalide (lățime ${L.widthMinM * 100}–${L.widthMaxM * 100} cm, înălțime ${L.heightMinM * 100}–${L.heightMaxM * 100} cm).`);
  if (!num(it.offset) || !num(it.elevation)) bad('Poziția unui obiect de pe perete este invalidă.');
  if (it.product != null && (typeof it.product !== 'string' || it.product.length > 80)) bad('Produsul unui obiect de pe perete este invalid.');
  if (it.color != null && (typeof it.color !== 'string' || !HEX.test(it.color))) bad('Culoarea unui obiect de pe perete este invalidă.');
  if (it.label != null && typeof it.label !== 'string') bad('Numele unui obiect de pe perete este invalid.');
  const label = it.label != null ? cleanPlanText(it.label).slice(0, L.labelMax) : '';
  return { id: it.id, wallId: it.wallId, side: it.side, kind: it.kind, offset: Math.max(0, Math.min(wallLen ?? Math.abs(it.offset), it.offset)), elevation: Math.max(0, Math.min(ceiling, it.elevation)),
    width: it.width, height: it.height, ...(it.depth != null ? { depth: it.depth } : {}), ...(typeof it.product === 'string' && it.product ? { product: it.product } : {}),
    ...(it.color ? { color: it.color.toLowerCase() } : {}), ...(label ? { label } : {}) };
}
/** Starea peretelui: lipsă sau `existing` = existent (câmpul se șterge, ca proiectele vechi să rămână identice); altceva decât
 *  `new` / `demolish` se respinge. */
export function sanitizeWallStatus(w: any): void {
  if (w.status == null || w.status === 'existing'){ delete w.status; return; }
  if (!(WALL_STATUSES as readonly string[]).includes(w.status)) bad('Starea peretelui este invalidă (existent, nou sau de demolat).');
}
/** Un număr nou din model: NaN, Infinity și șirurile se resping; o valoare finită în afara intervalului se aduce la interval. */
export const clampNum = (v: unknown, lo: number, hi: number, what: string): number => num(v) ? Math.min(hi, Math.max(lo, v)) : bad(what);
function sanitizeLayout(l: any): FloorLayout {
  if (!l || typeof l !== 'object' || !FLOOR_PATTERNS.includes(l.pattern)) bad('Modul de așezare al zonei este invalid.');
  if (l.angle != null && l.angle !== 0 && l.angle !== 90) bad('Unghiul de așezare al zonei este invalid.');
  if (l.groutColor != null && (typeof l.groutColor !== 'string' || !HEX.test(l.groutColor))) bad('Culoarea rostului zonei este invalidă.');
  const grout = l.groutMm != null ? clampNum(l.groutMm, 0, PLAN_LIMITS.zone.groutMaxMm, 'Rostul zonei este invalid.') : null;
  return { pattern: l.pattern, ...(l.angle ? { angle: 90 as const } : {}), ...(grout != null ? { groutMm: grout } : {}), ...(l.groutColor ? { groutColor: l.groutColor.toLowerCase() } : {}) };
}
/** Zona de pardoseală curățată: doar câmpurile modelului, coordonate în pătratul planului, latura de cel puțin 10 cm. */
export function sanitizeZone(z: any): FloorZone {
  const L = PLAN_LIMITS.zone;
  if (!z || typeof z !== 'object' || typeof z.id !== 'string' || !ID.test(z.id) || typeof z.roomId !== 'string' || !ID.test(z.roomId) || !z.rect || typeof z.rect !== 'object') bad('O zonă de pardoseală este invalidă.');
  const r = z.rect; if (!['x0', 'z0', 'x1', 'z1'].every(k => inBox(r[k]))) bad('O zonă de pardoseală are coordonatele invalide.');
  if (r.x1 - r.x0 < L.minM - 1e-9 || r.z1 - r.z0 < L.minM - 1e-9) bad(`O zonă de pardoseală trebuie să aibă cel puțin ${L.minM * 100} cm pe fiecare latură.`);
  // aceeași regulă ca pardoseala camerei; în plus zona trebuie să aibă un material (altfel nu are rost)
  if (!isMaterialRef(z.floor) || !z.floor) bad('Materialul zonei de pardoseală este invalid.');
  if (z.label != null && typeof z.label !== 'string') bad('Numele zonei de pardoseală este invalid.');
  const label = z.label != null ? cleanPlanText(z.label).slice(0, L.labelMax) : '';
  return { id: z.id, roomId: z.roomId, rect: { x0: r.x0, z0: r.z0, x1: r.x1, z1: r.z1 }, floor: z.floor, ...(z.layout != null ? { layout: sanitizeLayout(z.layout) } : {}), ...(label ? { label } : {}) };
}
/** Tavanul fals pe zonă curățat: doar câmpurile modelului; coborârea și scafa noi (NaN, Infinity, șir) se resping, o valoare finită
 *  în afara intervalului se aduce la interval; scafa 0 și LED-ul gol dispar. */
export function sanitizeCeilingZone(z: any): CeilingZone {
  const L = PLAN_LIMITS.ceilingZone;
  if (!z || typeof z !== 'object' || typeof z.id !== 'string' || !ID.test(z.id) || typeof z.roomId !== 'string' || !ID.test(z.roomId) || !z.rect || typeof z.rect !== 'object') bad('Un tavan fals pe zonă este invalid.');
  const r = z.rect; if (!['x0', 'z0', 'x1', 'z1'].every(k => inBox(r[k]))) bad('Un tavan fals pe zonă are coordonatele invalide.');
  if (r.x1 - r.x0 < L.minM - 1e-9 || r.z1 - r.z0 < L.minM - 1e-9) bad(`Un tavan fals pe zonă trebuie să aibă cel puțin ${L.minM * 100} cm pe fiecare latură.`);
  const dropCm = clampNum(z.dropCm, L.dropMinCm, L.dropMaxCm, 'Coborârea tavanului fals pe zonă este invalidă.');
  const coveCm = z.coveCm != null ? clampNum(z.coveCm, 0, L.coveMaxCm, 'Scafa tavanului fals pe zonă este invalidă.') : 0;
  if (z.led != null && (!isMaterialRef(z.led))) bad('Banda LED a tavanului fals pe zonă este invalidă.');
  if (z.label != null && typeof z.label !== 'string') bad('Numele tavanului fals pe zonă este invalid.');
  const label = z.label != null ? cleanPlanText(z.label).slice(0, L.labelMax) : '';
  return { id: z.id, roomId: z.roomId, rect: { x0: r.x0, z0: r.z0, x1: r.x1, z1: r.z1 }, dropCm, ...(coveCm > 0 ? { coveCm } : {}), ...(z.led ? { led: z.led } : {}), ...(label ? { label } : {}) };
}
/** Balustrada curățată: capete în pătratul planului, înălțimea adusă în interval (NaN / Infinity / șir se resping), tipul din enumerare. */
export function sanitizeRailing(b: any): Railing {
  const L = PLAN_LIMITS.railing;
  if (!b || typeof b !== 'object' || typeof b.id !== 'string' || !ID.test(b.id) || !pt(b.a) || !pt(b.b)) bad('O balustradă are capetele invalide.');
  // ca în plan (unealta nu adaugă segmente sub 20 cm): o balustradă mai scurtă se respinge
  if (Math.hypot(b.b[0] - b.a[0], b.b[1] - b.a[1]) < L.minLenM - 1e-9) bad('O balustradă are sub 20 cm.');
  const height = b.height != null ? clampNum(b.height, L.heightMinM, L.heightMaxM, 'Înălțimea balustradei este invalidă.') : null;
  if (b.kind != null && !(RAILING_KINDS as readonly string[]).includes(b.kind)) bad('Tipul balustradei este invalid (metal sau sticlă).');
  // produsul (id de material) doar la balustrada de metal; un id stricat se elimină, nu blochează salvarea
  const product = b.kind !== 'glass' && typeof b.product === 'string' && ID.test(b.product) ? b.product : null;
  return { id: b.id, a: [b.a[0], b.a[1]], b: [b.b[0], b.b[1]], ...(height != null ? { height } : {}), ...(b.kind ? { kind: b.kind } : {}), ...(product ? { product } : {}) };
}
/** Adnotarea curățată, sau null pentru un text gol (se aruncă). */
export function sanitizeAnnotation(a: any): Annotation | null {
  const L = PLAN_LIMITS.annotation;
  if (!a || typeof a !== 'object' || typeof a.id !== 'string' || !ID.test(a.id)) bad('O adnotare este invalidă.');
  if (a.kind === 'dim'){
    if (!pt(a.a) || !pt(a.b) || !num(a.offset) || Math.abs(a.offset) > L.offsetMaxM) bad('O cotă are punctele invalide.');
    if (Math.hypot(a.b[0] - a.a[0], a.b[1] - a.a[1]) < L.dimMinM) bad('O cotă trebuie să aibă cel puțin 1 cm.');
    if (a.mode != null && a.mode !== 'aligned' && a.mode !== 'ortho') bad('Modul cotei este invalid.');
    return { id: a.id, kind: 'dim', a: [a.a[0], a.a[1]], b: [a.b[0], a.b[1]], offset: a.offset, ...(a.mode ? { mode: a.mode } : {}) }; }
  if (a.kind === 'text'){
    if (!pt(a.at)) bad('Un text de pe plan are poziția invalidă.');
    if (a.size != null && !['s', 'm', 'l'].includes(a.size)) bad('Mărimea textului este invalidă.');
    if (a.rotation != null && !num(a.rotation)) bad('Rotația textului este invalidă.');
    if (typeof a.text !== 'string') bad('Textul de pe plan este invalid.');
    const text = cleanPlanText(a.text); if (!text) return null;
    return { id: a.id, kind: 'text', at: [a.at[0], a.at[1]], text, ...(a.size ? { size: a.size } : {}), ...(a.rotation ? { rotation: ((a.rotation % 360) + 360) % 360 } : {}) }; }
  return bad('Tipul adnotării este invalid.');
}
/** Curăță pe loc stâlpii, grinzile, pereții de sticlă, nișele și adnotările unui nivel: doar câmpurile modelului, limite
 *  comune cu interfața, cel mult N pe nivel; o nișă pe un perete care nu mai există se elimină. Aruncă PlanLimitError. */
export function sanitizeFloorElements(f: Floor & Record<string, any>): void {
  const L = PLAN_LIMITS, walls = new Set((f.walls || []).map(w => w.id));
  const set = <K extends 'columns' | 'beams' | 'glassWalls' | 'niches' | 'annotations' | 'wallItems' | 'zones' | 'ceilingZones' | 'railings'>(k: K, v: any[] | null) => { if (v && v.length) (f as any)[k] = v; else delete f[k]; };
  const cols = list(f.columns, L.column.max, 'Stâlpii'); set('columns', cols && cols.map(sanitizeColumn));
  const bms = list(f.beams, L.beam.max, 'Grinzile'); set('beams', bms && bms.map(sanitizeBeam));
  const gws = list(f.glassWalls, L.glassWall.max, 'Pereții de sticlă'); set('glassWalls', gws && gws.map(sanitizeGlassWall));
  const ns = list(f.niches, L.niche.max, 'Nișele'); set('niches', ns && ns.map(sanitizeNiche).filter(n => walls.has(n.wallId)));
  // obiectele de pe perete: id unic, peretele existent pe acest nivel (altfel se elimină, ca nișa), poziția adusă pe perete
  const lens = new Map((f.walls || []).map(w => [w.id, Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1])])), ceil = num(f.ceilingHeight) ? f.ceilingHeight : 10;
  const wis = list(f.wallItems, L.wallItem.max, 'Obiectele de pe perete'), seen = new Set<string>();
  set('wallItems', wis && wis.map(x => sanitizeWallItem(x, lens.get(x?.wallId), ceil)).filter(x => { if (seen.has(x.id)) bad('Două obiecte de pe perete au același id.'); seen.add(x.id); return walls.has(x.wallId); }));
  const an = list(f.annotations, L.annotation.max, 'Adnotările'); set('annotations', an && an.map(sanitizeAnnotation).filter((x): x is Annotation => !!x));
  // zonele de pardoseală: id unic, camera existentă pe acest nivel (o zonă a unei camere șterse se elimină, ca nișa)
  const rooms = new Set((f.rooms || []).map(r => r.id)), zs = list(f.zones, L.zone.max, 'Zonele de pardoseală'), zseen = new Set<string>();
  set('zones', zs && zs.map(sanitizeZone).filter(z => { if (zseen.has(z.id)) bad('Două zone de pardoseală au același id.'); zseen.add(z.id); return rooms.has(z.roomId); }));
  // tavanele false pe zonă: la fel ca zonele de pardoseală (id unic, camera existentă); balustradele: ca grinzile, cu id unic
  const cz = list(f.ceilingZones, L.ceilingZone.max, 'Tavanele false pe zonă'), cseen = new Set<string>();
  set('ceilingZones', cz && cz.map(sanitizeCeilingZone).filter(z => { if (cseen.has(z.id)) bad('Două tavane false pe zonă au același id.'); cseen.add(z.id); return rooms.has(z.roomId); }));
  const rl = list(f.railings, L.railing.max, 'Balustradele'), rseen = new Set<string>();
  set('railings', rl && rl.map(sanitizeRailing).filter(x => { if (rseen.has(x.id)) bad('Două balustrade au același id.'); rseen.add(x.id); return true; }));
  // tipul spațiului (balcon, terasă): o valoare necunoscută se elimină (camera rămâne interior), nu blochează salvarea
  for (const r of f.rooms || []) if ((r as any).spaceKind != null && !(SPACE_KINDS as readonly unknown[]).includes((r as any).spaceKind)) delete (r as any).spaceKind;
  for (const w of f.walls || []) sanitizeWallStatus(w);
  for (const w of f.walls || []) for (const o of w.openings || []){
    if (o.passage == null) delete o.passage;
    else { if (o.passage !== true || o.kind !== 'door') bad('Golul de trecere este invalid (doar pe un gol de ușă).');
      if (!within(o.width, L.passage.widthMinM, L.passage.widthMaxM)) bad(`Golul de trecere trebuie să aibă între ${L.passage.widthMinM * 100} și ${L.passage.widthMaxM * 100} cm.`);
      // trecerea câștigă: n-are foaie, deci nici tip, nici filomuro (se șterg, nu se resping, ca un client vechi să poată salva)
      delete o.doorType; delete o.flush; continue; }
    sanitizeDoorType(o); }
}
/** Tipul ușii și filomuro pe un gol (fără trecere): doar pe ușă, din enumerare; lățimea pe tip se verifică doar când tipul e ales. */
export function sanitizeDoorType(o: any): void {
  if (o.doorType == null) delete o.doorType;
  else { if (o.kind !== 'door' || !(DOOR_TYPES as readonly string[]).includes(o.doorType)) bad('Tipul ușii este invalid.');
    const r = DOOR_TYPE_RULES[o.doorType as DoorType]; if (!within(o.width, r.minM, r.maxM)) bad(`Ușa de acest tip trebuie să aibă între ${Math.round(r.minM * 100)} și ${Math.round(r.maxM * 100)} cm.`); }
  if (o.flush == null) delete o.flush; else if (o.flush !== true || o.kind !== 'door') bad('Ușa ascunsă (filomuro) este invalidă.');
}
