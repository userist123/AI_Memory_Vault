// Redimensionarea unei camere mută și pereții ei. Camerele stau pe axele pereților (vezi data/templates): când o latură a
// dreptunghiului se mută, pereții de pe acea latură se mută odată cu ea, iar pereții care o ating perpendicular se lungesc
// sau se scurtează. Un perete comun care continuă dincolo de cameră (de ex. peretele exterior al întregului apartament) se
// împarte: bucata camerei se mută, restul rămâne, iar două bucăți scurte de perete închid conturul. Funcție pură.
//
// Rezultatul trece mereu de validateFloor (core/validate.ts) fără erori noi, altfel redimensionarea e refuzată cu
// RoomResizeError (mesaj în română, afișat de Editor):
//  - ușile și ferestrele își păstrează poziția în spațiu când se mută celălalt capăt al peretelui și sunt ținute în perete;
//  - nu apar pereți sub 20 cm: tăieturile prea aproape de capătul peretelui se lipesc de capăt, iar bucățile de închidere
//    mai scurte de 20 cm (stau oricum în grosimea peretelui) nu se mai adaugă;
//  - bucățile unui perete împărțit poartă id-ul lui (`<id>~…`), așa că, la revenirea pe aceeași linie, se lipesc la loc.
import type { Floor, Issue, Opening, Room, RoomRect, Wall } from './types';
import { r3, wallLength } from './geometry';
import { validateFloor } from './validate';
import { clampEdge, edgeGroup, spanOf, MIN_ROOM_M } from './room-neighbours';

const TOL = .03, EPS = .01;
/** Lungimea minimă a unui perete, aceeași ca în validateFloor (WALL_TOO_SHORT). */
export const MIN_WALL_M = .2;
/** Redimensionare imposibilă fără un plan invalid; `message` e textul pentru utilizator. */
export class RoomResizeError extends Error { constructor(message: string){ super(message); this.name = 'RoomResizeError'; } }

type Edge = { key: keyof RoomRect; axis: 'x' | 'z'; to: number; from: number; lo: number; hi: number };
const same = (a: number, b: number) => Math.abs(a - b) <= TOL;
const coord = (p: [number, number], axis: 'x' | 'z') => axis === 'x' ? p[0] : p[1];
const other = (p: [number, number], axis: 'x' | 'z') => axis === 'x' ? p[1] : p[0];
const at = (axis: 'x' | 'z', c: number, o: number): [number, number] => axis === 'x' ? [r3(c), r3(o)] : [r3(o), r3(c)];
const cm = (m: number) => Math.round(m * 100);
const baseId = (id: string) => id.split('~')[0]!;
const near = (p: [number, number], q: [number, number]) => Math.abs(p[0] - q[0]) < 1e-6 && Math.abs(p[1] - q[1]) < 1e-6;
const opName = (o: Opening) => o.kind === 'door' ? `Ușa ${o.id}` : `Fereastra ${o.id}`;
const refuse = (why: string): never => { throw new RoomResizeError(`Camera nu se poate redimensiona: ${why}`); };

/**
 * Laturile care s-au mutat între dreptunghiul vechi și cel nou (x0/x1 sunt laturi verticale, z0/z1 orizontale). Întâi cele
 * care strâng camera (în ordinea x0, x1, z0, z1), apoi cele care o lărgesc (în ordine inversă): așa revenirea la dreptunghiul
 * vechi aplică exact pașii inverși, în ordine inversă, și reface pereții.
 */
function movedEdges(a: RoomRect, b: RoomRect): Edge[] {
  const keys = ['x0', 'x1', 'z0', 'z1'] as const, shrinks = (k: keyof RoomRect) => k.endsWith('0') ? b[k] > a[k] : b[k] < a[k];
  const order = [...keys.filter(k => a[k] !== b[k] && shrinks(k)), ...[...keys].reverse().filter(k => a[k] !== b[k] && !shrinks(k))];
  return order.map(k => ({ key: k, axis: k[0] as 'x' | 'z', from: a[k], to: b[k], lo: 0, hi: 0 }));
}

/** Golurile unui perete împărțit trec în bucata care conține mijlocul lor, cu offsetul recalculat (poziția în spațiu rămâne). */
function shareOpenings(ops: Opening[], start: number, end: number): Opening[] {
  return ops.filter(o => { const mid = o.offset + o.width / 2; return mid >= start && mid < end; }).map(o => ({ ...o, offset: r3(o.offset - start) }));
}

function applyEdge(walls: Wall[], e: Edge, newId: () => string): Wall[] {
  const out: Wall[] = [];
  for (const w of walls){
    const ca = coord(w.a, e.axis), cb = coord(w.b, e.axis), oa = other(w.a, e.axis), ob = other(w.b, e.axis);
    const onA = same(ca, e.from), onB = same(cb, e.from);
    if (onA && onB){
      // perete pe latura mutată: se mută întreg dacă stă în dreptul camerei, altfel se împarte
      const lo = Math.min(oa, ob), hi = Math.max(oa, ob);
      if (hi <= e.lo + TOL || lo >= e.hi - TOL){ out.push(w); continue; }   // doar o atinge în colț sau e în dreptul altei camere
      if (lo >= e.lo - TOL && hi <= e.hi + TOL){ out.push({ ...w, a: at(e.axis, e.to, oa), b: at(e.axis, e.to, ob) }); continue; }
      // împărțire: [lo, max(lo, e.lo)] rămâne, [max(lo,e.lo), min(hi,e.hi)] se mută, [min(hi,e.hi), hi] rămâne.
      // O tăietură la mai puțin de 20 cm de capătul peretelui se lipește de capăt (altfel ar rămâne un ciot invalid).
      const dir = ob >= oa ? 1 : -1, L = wallLength(w.a, w.b), s0 = Math.max(lo, e.lo), s1 = Math.min(hi, e.hi);
      const along = (o: number) => dir > 0 ? o - oa : oa - o;
      const kept = (dir > 0 ? [s0, s1] : [s1, s0]).filter(c => along(c) >= MIN_WALL_M - 1e-9 && along(c) <= L - MIN_WALL_M + 1e-9);
      const cuts = [oa, ...kept, ob];
      for (let i = 0; i < cuts.length - 1; i++){
        const p = cuts[i]!, q = cuts[i + 1]!, mid = (p + q) / 2, inside = mid > e.lo && mid < e.hi, c = inside ? e.to : e.from;
        out.push({ ...w, id: i === 0 ? w.id : `${baseId(w.id)}~${newId()}`, a: at(e.axis, c, p), b: at(e.axis, c, q), openings: shareOpenings(w.openings, along(p), along(q)) });
      }
      // bucățile scurte care închid conturul, la tăieturile păstrate; sub 20 cm nu se adaugă (încap în grosimea peretelui)
      if (Math.abs(e.to - e.from) >= MIN_WALL_M - 1e-9) for (const s of kept)
        out.push({ id: newId(), a: at(e.axis, e.from, s), b: at(e.axis, e.to, s), thickness: w.thickness, exterior: w.exterior, openings: [] });
      continue;
    }
    // perete perpendicular care se termină pe latură, în dreptul camerei: capătul urmează latura. Când se mută capătul `a`,
    // offsetul golurilor (măsurat de la `a`) se corectează, ca ușile și ferestrele să rămână pe loc în spațiu. Un perete fără
    // goluri poate trece peste celălalt capăt (bucata de închidere când latura trece de restul peretelui împărțit).
    const inSpan = (o: number) => o >= e.lo - TOL && o <= e.hi + TOL;
    if (onA && !onB && inSpan(oa)){
      const a = at(e.axis, e.to, oa), d = (cb - e.to) * (cb - ca);
      if (d < 0 && w.openings.length) refuse(`peretele ${baseId(w.id)} s-ar întoarce peste capătul lui, cu tot cu goluri.`);
      const shift = wallLength(a, w.b) - wallLength(w.a, w.b);
      out.push({ ...w, a, openings: w.openings.map(o => ({ ...o, offset: r3(o.offset + shift) })) }); continue;
    }
    if (onB && !onA && inSpan(ob)){
      if ((e.to - ca) * (cb - ca) < 0 && w.openings.length) refuse(`peretele ${baseId(w.id)} s-ar întoarce peste capătul lui, cu tot cu goluri.`);
      out.push({ ...w, b: at(e.axis, e.to, ob) }); continue;
    }
    out.push(w);
  }
  return out;
}

/** Bucățile aceluiași perete (același id de bază), coliniare, cap la cap și la fel de groase se lipesc la loc. */
function mergeSplitPieces(walls: Wall[]): Wall[] {
  const ws = [...walls];
  for (let changed = true; changed;){
    changed = false;
    outer: for (let i = 0; i < ws.length; i++) for (let j = 0; j < ws.length; j++){
      const A = ws[i]!, B = ws[j]!;
      if (i === j || baseId(A.id) !== baseId(B.id) || A.thickness !== B.thickness || A.exterior !== B.exterior || !near(A.b, B.a)) continue;
      const LA = wallLength(A.a, A.b), LB = wallLength(B.a, B.b); if (LA < EPS || LB < EPS) continue;
      const cross = (A.b[0] - A.a[0]) * (B.b[1] - B.a[1]) - (A.b[1] - A.a[1]) * (B.b[0] - B.a[0]), dot = (A.b[0] - A.a[0]) * (B.b[0] - B.a[0]) + (A.b[1] - A.a[1]) * (B.b[1] - B.a[1]);
      if (Math.abs(cross) > 1e-6 * LA * LB || dot <= 0) continue;
      const id = B.id === baseId(B.id) ? B.id : A.id;
      ws[i] = { ...A, id, b: B.b, openings: [...A.openings, ...B.openings.map(o => ({ ...o, offset: r3(o.offset + LA) }))] };
      ws.splice(j, 1); changed = true; break outer;
    }
  }
  return ws;
}

/** Golurile pereților schimbați rămân în întregime în perete (offset limitat); unul care nu mai încape oprește redimensionarea. */
function fitOpenings(w: Wall): Wall {
  const L = wallLength(w.a, w.b);
  return { ...w, openings: w.openings.map(o => {
    if (o.width > L + 1e-6) refuse(`${opName(o)} (${cm(o.width)} cm) nu mai încape pe peretele ${baseId(w.id)}, care ar avea ${cm(L)} cm. Mută sau îngustează golul întâi.`);
    const offset = r3(Math.max(0, Math.min(L - o.width, o.offset)));
    return offset === o.offset ? o : { ...o, offset };
  }) };
}

const issueKey = (i: Issue) => `${i.code}|${i.vars?.id ?? ''}|${i.vars?.op ?? ''}`;

/**
 * Camera `roomId` primește dreptunghiul `rect`; pereții de pe laturile mutate o urmează. Ușile și ferestrele rămân pe
 * peretele lor, în același loc în spațiu cât încap, altfel sunt împinse înapoi în perete. Fără `neighbours`, celelalte
 * camere nu se schimbă. Cu `neighbours`, camerele care împart latura mutată (core/room-neighbours.ts) își mută latura comună
 * cu aceeași distanță, iar mutarea se oprește (clamp) unde un vecin ar scădea sub `minRoom` (60 cm): dreptunghiul camerei
 * din rezultat poate fi deci mai mic decât cel cerut.
 * Aruncă RoomResizeError când rezultatul ar avea pereți sau goluri invalide (validateFloor).
 */
export function resizeRoom(floor: Floor, roomId: string, rect: RoomRect, newId: () => string, opts: { neighbours?: boolean; minRoom?: number } = {}): Floor {
  const room = floor.rooms.find(r => r.id === roomId); if (!room) return floor;
  const next: RoomRect = { x0: r3(Math.min(rect.x0, rect.x1)), x1: r3(Math.max(rect.x0, rect.x1)), z0: r3(Math.min(rect.z0, rect.z1)), z1: r3(Math.max(rect.z0, rect.z1)) };
  if (next.x1 - next.x0 < MIN_WALL_M - 1e-9 || next.z1 - next.z0 < MIN_WALL_M - 1e-9) refuse(`laturile trebuie să aibă cel puțin ${cm(MIN_WALL_M)} cm.`);
  const original = new Set(floor.walls);
  let walls = floor.walls; const old = room.rect;
  // laturile se aplică pe rând, fiecare cu întinderea camerei de după pașii anteriori (un colț mutat pe ambele axe rămâne legat)
  // Cu vecini, grupul fiecărei laturi se calculează pe camerele de după pașii anteriori, iar pereții se mută o singură dată,
  // pe toată întinderea grupului (latura comună, golurile ei și pereții perpendiculari de ambele părți).
  const rects = new Map(floor.rooms.map(r => [r.id, { ...r.rect }]));
  const rooms = () => floor.rooms.map(r => ({ ...r, rect: rects.get(r.id)! }));
  for (const e of movedEdges(old, next)){
    const group = opts.neighbours ? edgeGroup(rooms(), roomId, e.key) : [{ id: roomId, key: e.key }];
    const to = opts.neighbours ? clampEdge(rooms(), group, roomId, e.key, e.to, opts.minRoom ?? MIN_ROOM_M) : e.to;
    next[e.key] = to; if (to === e.from) continue;
    const spans = group.map(m => spanOf(rects.get(m.id)!, m.key));
    walls = applyEdge(walls, { ...e, to, lo: Math.min(...spans.map(s => s[0])), hi: Math.max(...spans.map(s => s[1])) }, newId);
    for (const m of group) rects.set(m.id, { ...rects.get(m.id)!, [m.key]: to });
  }
  const moved = (r: Room) => { const q = rects.get(r.id)!; return q.x0 !== r.rect.x0 || q.x1 !== r.rect.x1 || q.z0 !== r.rect.z0 || q.z1 !== r.rect.z1; };
  walls = mergeSplitPieces(walls)
    // un perete schimbat, fără goluri, care a ajuns sub 20 cm (de ex. bucata de închidere când camera revine) dispare
    .filter(w => original.has(w) || w.openings.length > 0 || wallLength(w.a, w.b) >= MIN_WALL_M - 1e-9)
    .map(w => original.has(w) ? w : fitOpenings(w));
  const result: Floor = { ...floor, walls, rooms: floor.rooms.map(r => r.id === roomId ? { ...r, rect: next } : moved(r) ? { ...r, rect: rects.get(r.id)! } : r) };
  // plasa de siguranță: nicio eroare nouă de perete sau de gol (cele existente dinainte nu blochează)
  const before = new Set(validateFloor(floor).filter(i => i.severity === 'ERROR').map(issueKey));
  const fresh = validateFloor(result).find(i => i.severity === 'ERROR' && !before.has(issueKey(i)));
  if (fresh){
    const w = walls.find(x => x.id === fresh.vars?.id);
    if (fresh.code === 'WALL_TOO_SHORT' && w) refuse(`peretele ${baseId(w.id)} ar avea doar ${cm(wallLength(w.a, w.b))} cm (minimum ${cm(MIN_WALL_M)} cm).`);
    refuse(fresh.message);
  }
  return result;
}
