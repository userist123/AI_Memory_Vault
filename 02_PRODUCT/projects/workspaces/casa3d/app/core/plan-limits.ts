// Limitele elementelor de plan (structură, nișe, adnotări): o singură sursă pentru interfață (câmpurile din panou,
// uneltele) și pentru server (checkSnapshot din lib/repo.ts). Un client cu o limită mai largă decât serverul ar bloca
// salvarea întregului proiect (400), de aceea ambele citesc de aici. Funcții pure, fără I/O.
import type { Annotation, Beam, Column, Floor, GlassWall, Niche } from './types';

export const PLAN_LIMITS = {
  coordM: 1000,
  column: { max: 200, minM: .05, maxM: 3, heightMinM: .3, heightMaxM: 6 },
  beam: { max: 200, minM: .05, maxM: 1.5 },
  glassWall: { max: 100, thickMinM: .01, thickMaxM: .3, mullionsMax: 20 },
  niche: { max: 100, widthMinM: .1, widthMaxM: 3, heightMinM: .1, heightMaxM: 3, depthMinM: .03, depthMaxM: .5, elevMaxM: 3 },
  annotation: { max: 300, textMax: 120, dimMinM: .01, offsetMaxM: 50 },
  passage: { widthMinM: .3, widthMaxM: 4 },
} as const;
/** Dimensiunile implicite la plasare (m). */
export const PLAN_DEFAULTS = { passageWidth: .9, shaft: .3, niche: { width: .6, height: .4, depth: .1, elevation: 1.1 }, dimOffset: .3 } as const;

const ID = /^[\w-]{1,80}$/;
const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const inBox = (v: unknown) => num(v) && Math.abs(v) <= PLAN_LIMITS.coordM;
const pt = (p: unknown): p is [number, number] => Array.isArray(p) && p.length === 2 && inBox(p[0]) && inBox(p[1]);
const within = (v: unknown, lo: number, hi: number) => num(v) && v >= lo - 1e-9 && v <= hi + 1e-9;
const HEX = /^#[0-9a-f]{6}$/i;

/** Textul liber de pe plan: fără caractere de control, fără `<>` (ajunge în SVG, DXF și PDF) și fără comenzi de direcție
 *  (bidi, ar inversa vizual textul pe planșă), cel mult 120 de caractere. */
export const cleanPlanText = (s: unknown) => String(s ?? '').replace(/[\u0000-\u001f\u007f<>\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, PLAN_LIMITS.annotation.textMax);

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
    const text = cleanPlanText(a.text); if (!text) return null;
    return { id: a.id, kind: 'text', at: [a.at[0], a.at[1]], text, ...(a.size ? { size: a.size } : {}), ...(a.rotation ? { rotation: ((a.rotation % 360) + 360) % 360 } : {}) }; }
  return bad('Tipul adnotării este invalid.');
}
/** Curăță pe loc stâlpii, grinzile, pereții de sticlă, nișele și adnotările unui nivel: doar câmpurile modelului, limite
 *  comune cu interfața, cel mult N pe nivel; o nișă pe un perete care nu mai există se elimină. Aruncă PlanLimitError. */
export function sanitizeFloorElements(f: Floor & Record<string, any>): void {
  const L = PLAN_LIMITS, walls = new Set((f.walls || []).map(w => w.id));
  const set = <K extends 'columns' | 'beams' | 'glassWalls' | 'niches' | 'annotations'>(k: K, v: any[] | null) => { if (v && v.length) (f as any)[k] = v; else delete f[k]; };
  const cols = list(f.columns, L.column.max, 'Stâlpii'); set('columns', cols && cols.map(sanitizeColumn));
  const bms = list(f.beams, L.beam.max, 'Grinzile'); set('beams', bms && bms.map(sanitizeBeam));
  const gws = list(f.glassWalls, L.glassWall.max, 'Pereții de sticlă'); set('glassWalls', gws && gws.map(sanitizeGlassWall));
  const ns = list(f.niches, L.niche.max, 'Nișele'); set('niches', ns && ns.map(sanitizeNiche).filter(n => walls.has(n.wallId)));
  const an = list(f.annotations, L.annotation.max, 'Adnotările'); set('annotations', an && an.map(sanitizeAnnotation).filter((x): x is Annotation => !!x));
  for (const w of f.walls || []) for (const o of w.openings || []){
    if (o.passage == null){ delete o.passage; continue; }
    if (o.passage !== true || o.kind !== 'door') bad('Golul de trecere este invalid (doar pe un gol de ușă).');
    if (!within(o.width, L.passage.widthMinM, L.passage.widthMaxM)) bad(`Golul de trecere trebuie să aibă între ${L.passage.widthMinM * 100} și ${L.passage.widthMaxM * 100} cm.`); }
}
