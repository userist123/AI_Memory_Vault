// Corpurile de iluminat plasate: pendule deasupra mesei, a blatului sau lângă pat, aplice lângă pat, canapea, oglindă sau pe un perete,
// șină cu spoturi pe tavan. Fiecare corp e legat de o piesă de mobilier (ancora), deci se mută odată cu ea.
// Regulile de poziționare sunt cele uzuale de proiectare (orientative): pendulul la ~75 cm deasupra mesei, aplicele de pat la ~1,1 m,
// aplicele de perete la ~1,65 m, peste oglindă la ~2,0 m; un corp atârnat într-o zonă de trecere nu coboară sub 2,1 m.
import type { Catalog, FixtureAnchor, LightFixture, MaterialsCatalog, Room, RoomFinishes, Snapshot } from './types';
import { footprintOf } from './validate';
import { materialOf, ceilingOf } from './finishes';
import { finishesOf } from './boq';
import { floorOfRoom } from './levels';

export const FIXTURE_KINDS = ['pendant', 'sconce', 'track'] as const;
export const FIXTURE_ANCHORS = ['table', 'counter', 'bed', 'sofa', 'desk', 'vanity', 'center', 'wall'] as const;
/** Piesa din plan de care se leagă fiecare ancoră. */
export const ANCHOR_GROUP: Partial<Record<FixtureAnchor, string>> = { table: 'masa', counter: 'bucatarie', bed: 'pat', sofa: 'canapea', desk: 'birou', vanity: 'lavoar' };
export const FIXTURE_RULES = { pendantAboveTableM: .75, tableTopM: .75, counterTopM: .9, bedSconceM: 1.1, wallSconceM: 1.65, vanitySconceM: 2.0, headroomM: 2.1, sideGapM: .25, maxPerFixture: 12 };
/** Câte corpuri pune o alegere: explicit, altfel două aplice lângă pat/canapea, trei pendule peste blat, unul în rest. */
export function fixtureCount(fx: LightFixture): number {
  if (fx.kind === 'pendant' && fx.anchor === 'bed') return Math.min(2, fx.count ?? 2);
  if (fx.count != null) return fx.count;
  if (fx.kind === 'sconce' && (fx.anchor === 'bed' || fx.anchor === 'sofa')) return 2;
  if (fx.kind === 'pendant' && fx.anchor === 'counter') return 3;
  return 1;
}

export interface FixturePoint { x: number; z: number; y: number; /** tavanul deasupra corpului (sub tavanul fals) */ top?: number; kind: LightFixture['kind']; /** direcția spre cameră pentru aplice (normala peretelui) */ nx: number; nz: number;
  /** pentru șină: lungimea și axa */ len?: number; alongX?: boolean; drop?: number }
type Rect = { x0: number; z0: number; x1: number; z1: number };
/** Peretele din spatele piesei, din rotația ei: rotația 0 privește spre +z, deci spatele e la N (vezi frontRect în core/geometry.ts). */
export const backSide = (rotation: number): 'N' | 'S' | 'W' | 'E' => (['N', 'W', 'S', 'E'] as const)[((Math.round(rotation / (Math.PI / 2)) % 4) + 4) % 4]!;
const NORMAL = { N: [0, 1], S: [0, -1], W: [1, 0], E: [-1, 0] } as const;

/** Unde ajunge fiecare corp, în coordonatele planului (x, z) și la ce înălțime (y, partea de jos a corpului). */
export function fixturePoints(snap: Snapshot, cat: Catalog, room: Room, fx: LightFixture): FixturePoint[] {
  const H = floorOfRoom(snap, room.id).ceilingHeight, r = room.rect, n = Math.min(FIXTURE_RULES.maxPerFixture, fixtureCount(fx)); if (n <= 0) return [];
  const group = ANCHOR_GROUP[fx.anchor], c = ceilingOf(finishesOf(snap, room)), top = H - (c.type === 'flat' ? 0 : c.dropCm / 100), pl = group ? snap.placements.find(p => p.roomId === room.id && p.group === group) : null, fp = pl ? footprintOf(cat, pl) : null;
  if (group && !fp) return [];
  const box: Rect = fp ?? r, cx = (box.x0 + box.x1) / 2, cz = (box.z0 + box.z1) / 2, alongX = box.x1 - box.x0 >= box.z1 - box.z0;
  // de-a lungul laturii lungi, distanțate egal
  const spread = (k: number, len: number) => (k + .5) / n * len - len / 2;
  if (fx.kind === 'track'){ const len = Math.min(fx.lengthM ?? 2, (alongX ? box.x1 - box.x0 : box.z1 - box.z0) || 2);
    return [{ x: cx, z: cz, y: top - .05, kind: 'track', top, nx: 0, nz: 0, len, alongX }]; }
  if (fx.kind === 'pendant'){
    const low = fx.heightM ?? (fx.anchor === 'table' ? FIXTURE_RULES.tableTopM + FIXTURE_RULES.pendantAboveTableM : fx.anchor === 'counter' ? FIXTURE_RULES.counterTopM + FIXTURE_RULES.pendantAboveTableM : fx.anchor === 'bed' ? 1.2 : FIXTURE_RULES.headroomM + .1);
    const y = Math.min(top - .25, low);
    if (fx.anchor === 'bed' && fp){ const side = backSide(pl!.rotation), along = side === 'N' || side === 'S', [nx, nz] = NORMAL[side], head = side === 'N' ? fp.z0 + .3 : side === 'S' ? fp.z1 - .3 : side === 'W' ? fp.x0 + .3 : fp.x1 - .3;
      const half = (along ? fp.x1 - fp.x0 : fp.z1 - fp.z0) / 2 + FIXTURE_RULES.sideGapM;
      return [-1, 1].slice(0, n).map(s => along ? { x: cx + s * half, z: head, y, kind: 'pendant' as const, top, nx, nz, drop: top - y } : { x: head, z: cz + s * half, y, kind: 'pendant' as const, top, nx, nz, drop: top - y }); }
    const len = (alongX ? box.x1 - box.x0 : box.z1 - box.z0) * (fx.anchor === 'center' ? .5 : .8);
    return Array.from({ length: n }, (_, k) => alongX ? { x: cx + spread(k, len), z: cz, y, kind: 'pendant' as const, top, nx: 0, nz: 0, drop: top - y } : { x: cx, z: cz + spread(k, len), y, kind: 'pendant' as const, top, nx: 0, nz: 0, drop: top - y }); }
  // aplice: pe peretele din spatele piesei (sau pe latura aleasă), de o parte și de alta a ei
  const side = fx.anchor === 'wall' || !fp ? (fx.side ?? 'N') : backSide(pl!.rotation), [nx, nz] = NORMAL[side], along = side === 'N' || side === 'S';
  const y = fx.heightM ?? (fx.anchor === 'bed' ? FIXTURE_RULES.bedSconceM : fx.anchor === 'vanity' ? FIXTURE_RULES.vanitySconceM : FIXTURE_RULES.wallSconceM);
  // fața peretelui: linia lui din plan plus jumătate din grosime, spre cameră
  const line = side === 'N' ? r.z0 : side === 'S' ? r.z1 : side === 'W' ? r.x0 : r.x1, wall = floorOfRoom(snap, room.id).walls.find(w => along ? Math.abs(w.a[1] - line) < .02 && Math.abs(w.b[1] - line) < .02 : Math.abs(w.a[0] - line) < .02 && Math.abs(w.b[0] - line) < .02);
  const wallPos = line + (along ? nz : nx) * (wall ? wall.thickness / 2 : 0);
  let offs: number[];
  if (fp && fx.anchor !== 'wall'){ const half = (along ? fp.x1 - fp.x0 : fp.z1 - fp.z0) / 2;
    offs = n === 1 ? [0] : n === 2 ? [-(half + FIXTURE_RULES.sideGapM), half + FIXTURE_RULES.sideGapM] : Array.from({ length: n }, (_, k) => spread(k, 2 * half)); }
  else { const len = along ? r.x1 - r.x0 : r.z1 - r.z0; offs = Array.from({ length: n }, (_, k) => spread(k, len)); }
  const c0 = fp && fx.anchor !== 'wall' ? (along ? cx : cz) : (along ? (r.x0 + r.x1) / 2 : (r.z0 + r.z1) / 2);
  const lo = (along ? r.x0 : r.z0) + .1, hi = (along ? r.x1 : r.z1) - .1, clamp = (v: number) => Math.max(lo, Math.min(hi, v));
  return offs.map(o => c0 + o).map(v => clamp(v)).map(v => v - c0).map(o => along ? { x: c0 + o, z: wallPos, y, kind: 'sconce' as const, nx, nz } : { x: wallPos, z: c0 + o, y, kind: 'sconce' as const, nx, nz });
}
/** Toate corpurile din proiect (pentru plan și 3D). */
export function allFixturePoints(snap: Snapshot, cat: Catalog, rooms: Room[], finishesOf: (s: Snapshot, r: Room) => RoomFinishes){
  return rooms.flatMap(room => (finishesOf(snap, room).fixtures || []).flatMap((fx, i) => fixturePoints(snap, cat, room, fx).map(p => ({ ...p, roomId: room.id, index: i, material: fx.material ?? null, color: fx.color }))));
}

export interface FixtureIssue { key: string; roomId: string; vars?: Record<string, string | number> }
export function fixtureIssues(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, room: Room, f: RoomFinishes): FixtureIssue[] {
  const out: FixtureIssue[] = [];
  (f.fixtures || []).forEach((fx, i) => { const add = (key: string, vars: Record<string, string | number> = {}) => out.push({ key, roomId: room.id, vars: { n: i + 1, ...vars } });
    const g = ANCHOR_GROUP[fx.anchor];
    if (g && !snap.placements.some(p => p.roomId === room.id && p.group === g)) add('fix.noAnchor', { anchor: fx.anchor });
    if (!fx.material) add('fix.noProduct');
    // corp atârnat în zonă de trecere (nu deasupra mesei, blatului sau lângă pat)
    if (fx.kind === 'pendant' && (fx.anchor === 'center' || fx.anchor === 'wall')){ const low = fixturePoints(snap, cat, room, fx)[0]; if (low && low.y < FIXTURE_RULES.headroomM) add('fix.headroom', { h: Math.round(low.y * 100) / 100, min: FIXTURE_RULES.headroomM }); }
    if (fx.kind === 'pendant' && fx.anchor === 'counter' && (f.kitchen?.upper ?? 'closed') !== 'none') add('fix.counterUpper');
    const m = materialOf(mc, fx.material), ip = m?.specs?.ip ? Number(m.specs.ip.replace(/\D/g, '').slice(-1)) : null;
    if (room.type === 'baie' && m && (ip == null || ip < 4)) add('fix.ip', { ip: m.specs?.ip ?? '?' }); });
  return out;
}
export function sanitizeFixtures(xs: any): string | null {
  if (xs === undefined) return null; const bad = 'Corpurile de iluminat sunt invalide.';
  if (!Array.isArray(xs) || xs.length > 24) return bad;
  for (const x of xs){ if (!x || typeof x !== 'object' || !(FIXTURE_KINDS as readonly string[]).includes(x.kind) || !(FIXTURE_ANCHORS as readonly string[]).includes(x.anchor)) return bad;
    if (x.material != null && (typeof x.material !== 'string' || x.material.length > 80)) return bad;
    if (x.count !== undefined && !(Number.isInteger(x.count) && x.count >= 1 && x.count <= FIXTURE_RULES.maxPerFixture)) return bad;
    if (x.heightM !== undefined && !(typeof x.heightM === 'number' && x.heightM >= .3 && x.heightM <= 6)) return bad;
    if (x.lengthM !== undefined && !(typeof x.lengthM === 'number' && x.lengthM >= .5 && x.lengthM <= 8)) return bad;
    if (x.side !== undefined && !['N', 'S', 'E', 'W'].includes(x.side)) return bad;
    if (x.color !== undefined && !(typeof x.color === 'string' && /^#[0-9a-fA-F]{6}$/.test(x.color))) return bad; }
  return null;
}
