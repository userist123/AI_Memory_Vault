// Niveluri (parter + etaje) și scări. Parterul rămâne `snap.floor`, ca proiectele salvate și API-ul să nu se schimbe;
// etajele stau în `snap.levels`. Codul care lucrează pe un singur nivel (editor, plan, 3D, validare, consilier, strat
// tehnic, DXF) primește o *vedere*: un snapshot cu un singur nivel în `floor` și doar piesele/punctele camerelor lui.
// Totalurile pe casă (buget, tabel de suprafețe) parcurg `floors()`.
import type { Catalog, Floor, Issue, RoomRect, Snapshot, Stair } from './types';
import { footprintAt, insideRect, rectHit } from './geometry';
import { footprintOf } from './validate';

/** Grosimea plăcii dintre niveluri (m); cota unui nivel = înălțimile de sub el + câte o placă. */
export const SLAB = 0.2;
export const MAX_LEVELS = 4;

export const floors = (s: Snapshot): Floor[] => [s.floor, ...(s.levels ?? [])];
const check = (s: Snapshot, i: number) => { if (!Number.isInteger(i) || i < 0 || i >= floors(s).length) throw new RangeError(`Nivel inexistent: ${i}`); };

/** Nivelul fiecărei camere de la etaj; o cameră care nu e pe niciun etaj (sau lipsește) ține de parter. */
function upperRooms(s: Snapshot): Map<string, number> {
  const m = new Map<string, number>(); (s.levels ?? []).forEach((f, k) => { for (const r of f.rooms) m.set(r.id, k + 1); }); return m; }
export function levelOfRoom(s: Snapshot, roomId: string): number {
  const up = upperRooms(s).get(roomId); if (up != null) return up; return s.floor.rooms.some(r => r.id === roomId) ? 0 : -1; }
/** Nivelul pe care stă camera (parterul pentru o cameră necunoscută). */
export const floorOfRoom = (s: Snapshot, roomId: string): Floor => floors(s)[Math.max(0, levelOfRoom(s, roomId))]!;
export function elevationOf(s: Snapshot, i: number): number {
  check(s, i); return floors(s).slice(0, i).reduce((a, f) => a + f.ceilingHeight + SLAB, 0); }

/** Snapshot cu un singur nivel: `floor` = nivelul i, piesele și punctele tehnice ale camerelor lui, fără `levels`. */
export function levelView(s: Snapshot, i: number): Snapshot {
  check(s, i); if (!s.levels?.length) return s;
  const up = upperRooms(s), mine = (roomId: string) => (up.get(roomId) ?? 0) === i;
  const { levels: _l, underlay, tech, ...g } = s;
  return { ...g, floor: floors(s)[i], placements: s.placements.filter(p => mine(p.roomId)),
    ...(tech ? { tech: tech.filter(t => mine(t.roomId)) } : {}),
    // imaginea de calc ține de parter; etajele nu au încă una proprie
    ...(i === 0 && underlay ? { underlay } : {}) };
}
/** Pune înapoi o vedere editată: nivelul i, piesele și punctele lui; restul casei rămâne cum era. */
export function mergeLevel(full: Snapshot, i: number, view: Snapshot): Snapshot {
  check(full, i); if (!full.levels?.length) return view;
  const up = upperRooms(full), other = (roomId: string) => (up.get(roomId) ?? 0) !== i;
  const { levels: _l, underlay: viewUnderlay, tech: viewTech, ...g } = view;
  const levels = full.levels.map((f, k) => k + 1 === i ? view.floor : f), floor = i === 0 ? view.floor : full.floor;
  const tech = [...(full.tech ?? []).filter(t => other(t.roomId)), ...(viewTech ?? [])];
  const underlay = i === 0 ? viewUnderlay : full.underlay;
  return { ...g, floor, levels, placements: [...full.placements.filter(p => other(p.roomId)), ...view.placements],
    ...(full.tech || viewTech ? { tech } : {}), ...(underlay ? { underlay } : {}) };
}

/** Adaugă un nivel deasupra celui mai de sus, cu aceiași pereți, goluri și camere (id-uri noi), fără mobilier și fără intrare. */
export function addLevel(s: Snapshot, o: { name: string; id?: () => string }): Snapshot {
  const fl = floors(s); if (fl.length >= MAX_LEVELS) throw new RangeError(`Cel mult ${MAX_LEVELS} niveluri.`);
  const id = o.id ?? (() => globalThis.crypto.randomUUID().slice(0, 8)), top = fl[fl.length - 1];
  const floor: Floor = { id: `nivel-${id()}`, name: o.name, ceilingHeight: top.ceilingHeight,
    rooms: top.rooms.map(r => ({ ...r, id: `camera-${id()}`, rect: { ...r.rect } })),
    walls: top.walls.map(w => ({ ...w, id: `wall-${id()}`, a: [...w.a] as [number, number], b: [...w.b] as [number, number],
      openings: w.openings.map(({ entrance: _e, ...op }) => ({ ...op, id: `gol-${id()}` })) })) };
  return { ...s, levels: [...(s.levels ?? []), floor] };
}
/** Șterge un etaj (nu parterul) cu tot ce ține de el; scările care urcau la el dispar dacă era ultimul. */
export function removeLevel(s: Snapshot, i: number): Snapshot {
  check(s, i); if (i === 0) throw new RangeError('Parterul nu se poate șterge.');
  const f = floors(s)[i], up = upperRooms(s), gone = (roomId: string) => up.get(roomId) === i;
  const rooms = new Set(f.rooms.map(r => r.id)), walls = new Set(f.walls.map(w => w.id)), ops = new Set(f.walls.flatMap(w => w.openings.map(o => o.id)));
  const pieces = new Set(s.placements.filter(p => gone(p.roomId)).map(p => p.id));
  const levels = s.levels!.filter((_, k) => k + 1 !== i), last = i === floors(s).length - 1;
  const drop = <T>(rec: Record<string, T> | undefined, bad: (k: string) => boolean) => rec ? Object.fromEntries(Object.entries(rec).filter(([k]) => !bad(k))) : rec;
  const below = (fl: Floor): Floor => { const { stairs: _s, ...rest } = fl; return rest; };
  const fls = [s.floor, ...levels].map((fl, k) => last && k === i - 1 ? below(fl) : fl);
  const { levels: _l, ...g } = s;
  const out: Snapshot = { ...g, floor: fls[0], placements: s.placements.filter(p => !pieces.has(p.id)) };
  if (fls.length > 1) out.levels = fls.slice(1);
  if (s.tech) out.tech = s.tech.filter(t => !gone(t.roomId));
  if (s.finishes) out.finishes = drop(s.finishes, k => rooms.has(k))!;
  if (s.appearance){ const a = s.appearance; out.appearance = {
    ...(a.rooms ? { rooms: drop(a.rooms, k => rooms.has(k))! } : {}),
    ...(a.wallFaces ? { wallFaces: drop(a.wallFaces, k => { const [w, r] = k.split('@'); return walls.has(w) || rooms.has(r); })! } : {}),
    ...(a.openings ? { openings: drop(a.openings, k => ops.has(k))! } : {}),
    ...(a.items ? { items: drop(a.items, k => pieces.has(k))! } : {}) }; }
  return out;
}

export const stairRect = (st: Stair): RoomRect => footprintAt(st.x, st.z, st.rotation, st.width, st.length);
/** Treapta confortabilă: contratreaptă ≤ 19 cm, lățime de călcare ≥ 25 cm (2·h + l ≈ 63 cm). */
export const STAIR_RISER_MAX = 0.19, STAIR_GOING_MIN = 0.25, STAIR_RISER_TARGET = 0.175;
/** Geometria comună planului, 3D-ului și DXF-ului. Rotația 0 urcă spre −z, apoi, la fiecare 90° (ca rotation.y din
 *  three.js aplicat lui −z): −x, +z, +x. `rise` = înălțimea de urcat (tavanul nivelului + placa). */
export function stairGeometry(st: Stair, rise: number){
  const q = ((Math.round(st.rotation / (Math.PI / 2)) % 4) + 4) % 4, dir = ([[0, -1], [-1, 0], [0, 1], [1, 0]] as const)[q]!;
  const steps = Math.max(2, Math.ceil(rise / STAIR_RISER_TARGET - 1e-9)), h = st.length / 2;
  return { dir: [dir[0], dir[1]] as [number, number], bottom: [st.x - dir[0] * h, st.z - dir[1] * h] as [number, number], top: [st.x + dir[0] * h, st.z + dir[1] * h] as [number, number],
    steps, riser: rise / steps, going: st.length / steps, rect: stairRect(st) };
}
/** Lungimea unei scări drepte cu trepte confortabile pentru o înălțime dată. */
export const comfortableStairLength = (rise: number) => Math.ceil(rise / STAIR_RISER_TARGET - 1e-9) * 0.27;
/** Golurile din placa nivelului i: scările care urcă de la nivelul de dedesubt. */
export const stairVoids = (s: Snapshot, i: number): RoomRect[] => i <= 0 ? [] : (floors(s)[i - 1]?.stairs ?? []).map(stairRect);

/** Scări: în interiorul unei camere, cu un nivel deasupra, fără mobilier pe ele sau în golul lor de la etaj. */
export function stairIssues(s: Snapshot, cat: Catalog): Issue[] {
  const fl = floors(s), out: Issue[] = [];
  const err = (key: string, message: string, vars: Record<string, string | number>, w?: string): Issue =>
    ({ code: 'STAIR', severity: 'ERROR', message, key, vars, ...(w ? { with: w } : {}) });
  fl.forEach((f, i) => { for (const st of f.stairs ?? []){ const r = stairRect(st);
    if (i === fl.length - 1) out.push(err('issue.STAIR_NO_LEVEL', 'Scara nu duce nicăieri: adaugă un nivel deasupra.', { stair: st.id }));
    if (!f.rooms.some(room => insideRect(room.rect, r))) out.push(err('issue.STAIR_OUTSIDE', 'Scara trebuie să stea în întregime într-o cameră.', { stair: st.id }));
    // abruptă: se poate construi, dar e incomodă și periculoasă (avertisment, nu blochează)
    const g = stairGeometry(st, f.ceilingHeight + SLAB), riser = Math.round(g.riser * 100), going = Math.round(g.going * 100);
    if (g.riser > STAIR_RISER_MAX + 1e-9 || g.going < STAIR_GOING_MIN - 1e-9) out.push({ code: 'STAIR', severity: 'WARNING', key: 'issue.STAIR_STEEP',
      message: `Scara e prea abruptă: trepte de ${riser} cm înălțime și ${going} cm adâncime (confortabil: cel mult 19 și cel puțin 25 cm). Lungește scara la ${Math.round(comfortableStairLength(f.ceilingHeight + SLAB) * 100)} cm.`,
      vars: { stair: st.id, riser, going, length: Math.round(comfortableStairLength(f.ceilingHeight + SLAB) * 100) } });
    const hits = (k: number, key: string, msg: string) => { if (k >= fl.length) return;
      for (const p of levelView(s, k).placements){ const fp = footprintOf(cat, p); if (fp && rectHit(fp, r)) out.push(err(key, msg, { stair: st.id }, p.id)); } };
    hits(i, 'issue.STAIR_BLOCKED', 'Piesa stă pe scară.');
    hits(i + 1, 'issue.STAIR_VOID', 'Piesa stă în golul scării de la etaj.'); } });
  return out;
}
