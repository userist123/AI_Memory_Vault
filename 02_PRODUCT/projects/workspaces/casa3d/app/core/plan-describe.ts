// Echivalentul text al planului (UI/UX P3-16, după Figma / tldraw): fiecare obiect al nivelului are o descriere scurtă
// („Perete exterior · 5,60 m · Living, Dormitor”), ordonată ca la citire (camere de sus în jos, de la stânga la dreapta;
// în fiecare cameră piesele ei), plus structura (pereți, goluri, stâlpi, grinzi, scări). Funcție pură: textele vin prin
// `t`, deci aceeași listă în limba interfeței și în teste.
import type { Catalog, Snapshot, Wall } from './types';
import { formatArea, formatLength, type Units } from './format';
import { footprintOf } from './validate';
import { resolve } from './catalog';
import { nicheRoom } from './niches';
import { wallItemRoom } from './wall-items';
import type { WallItem } from './types';
import { annotationCenter, dimGeometry } from './annotations';
import { activeOpenings } from './renovation';
import { railingHeight, railingKind } from './railings';

type T = (key: string, vars?: Record<string, string | number>) => string;
export type MirrorItem = { kind: 'room' | 'placement' | 'wall' | 'opening' | 'column' | 'beam' | 'stair' | 'glass_wall' | 'niche' | 'annotation' | 'wall_item' | 'floor_zone' | 'ceiling_zone' | 'railing'; id: string; wallId?: string; parent?: string; label: string };
export type MirrorGroup = { id: string; title: string; items: MirrorItem[] };

const ord = (a: { x: number; z: number; id: string }, b: { x: number; z: number; id: string }) => (Math.abs(a.z - b.z) > .05 ? a.z - b.z : a.x - b.x) || a.id.localeCompare(b.id);
const len = (w: { a: [number, number]; b: [number, number] }) => Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]);

/** Camerele de-a lungul unui segment de perete (de la `a` la `b`): au o latură pe linia lui care se suprapune cu el cel
 *  puțin `minOverlap` (toleranță pe linie: grosimea peretelui). Pereți oblici: nicio cameră (camerele sunt dreptunghiuri). */
export function roomsAlongSegment(snap: Pick<Snapshot, 'floor'>, a: [number, number], b: [number, number], thickness: number, minOverlap = .2): string[] {
  const tol = Math.max(.05, thickness), horiz = Math.abs(a[1] - b[1]) < 1e-3, vert = Math.abs(a[0] - b[0]) < 1e-3;
  if (!horiz && !vert) return [];
  const lo = horiz ? Math.min(a[0], b[0]) : Math.min(a[1], b[1]), hi = horiz ? Math.max(a[0], b[0]) : Math.max(a[1], b[1]), at = horiz ? a[1] : a[0];
  return snap.floor.rooms.filter(r => { const [e1, e2, s0, s1] = horiz ? [r.rect.z0, r.rect.z1, r.rect.x0, r.rect.x1] : [r.rect.x0, r.rect.x1, r.rect.z0, r.rect.z1];
    return (Math.abs(e1 - at) <= tol || Math.abs(e2 - at) <= tol) && Math.min(hi, s1) - Math.max(lo, s0) > minOverlap; }).map(r => r.name);
}
export const roomsAlongWall = (snap: Pick<Snapshot, 'floor'>, w: Wall) => roomsAlongSegment(snap, w.a, w.b, w.thickness);
/** Camerele în dreptul unui gol (ușă, fereastră): doar porțiunea golului din perete, nu tot peretele. */
export function roomsAtOpening(snap: Pick<Snapshot, 'floor'>, w: Wall, o: { offset: number; width: number }): string[] {
  const L = len(w) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
  const pa: [number, number] = [w.a[0] + ux * o.offset, w.a[1] + uz * o.offset], pb: [number, number] = [w.a[0] + ux * (o.offset + o.width), w.a[1] + uz * (o.offset + o.width)];
  return roomsAlongSegment(snap, pa, pb, w.thickness, Math.min(.2, o.width / 2));
}

export function describePlan(snap: Pick<Snapshot, 'floor' | 'placements'>, cat: Catalog, o: { t: T; units: Units; lang: 'ro' | 'en' }): MirrorGroup[] {
  const { t, units, lang } = o, L = (m: number) => formatLength(m, units, lang), f = snap.floor;
  const groups: MirrorGroup[] = [];
  const wallItemEntry = (it: WallItem): MirrorItem => ({ kind: 'wall_item', id: it.id, label: t('mirror.wallItem', { kind: it.label || t(`wallItem.kind.${it.kind}`), w: L(it.width), h: L(it.height), y: L(it.elevation) }) });
  for (const r of [...f.rooms].sort((a, b) => ord({ x: a.rect.x0, z: a.rect.z0, id: a.id }, { x: b.rect.x0, z: b.rect.z0, id: b.id }))){
    const w = r.rect.x1 - r.rect.x0, d = r.rect.z1 - r.rect.z0;
    // tipul spațiului (balcon, terasă) la finalul descrierii camerei; camera interioară rămâne ca înainte
    const items: MirrorItem[] = [{ kind: 'room', id: r.id, label: t('mirror.room', { name: r.name, area: formatArea(w * d, units, lang), w: L(w), d: L(d) }) + (r.spaceKind && r.spaceKind !== 'interior' ? ` · ${t(`space.${r.spaceKind}`)}` : '') }];
    // zonele de pardoseală ale camerei, cu mărimea și suprafața
    for (const z of (f.zones ?? []).filter(x => x.roomId === r.id)){ const zw = z.rect.x1 - z.rect.x0, zd = z.rect.z1 - z.rect.z0;
      items.push({ kind: 'floor_zone', id: z.id, label: t('mirror.zone', { name: z.label || t('tool.floor_zone'), area: formatArea(zw * zd, units, lang), w: L(zw), d: L(zd) }) }); }
    // tavanele false pe zonă ale camerei: mărimea și coborârea
    for (const z of (f.ceilingZones ?? []).filter(x => x.roomId === r.id)){ const zw = z.rect.x1 - z.rect.x0, zd = z.rect.z1 - z.rect.z0;
      items.push({ kind: 'ceiling_zone', id: z.id, label: t('mirror.ceilingZone', { name: z.label || t('tool.ceiling_zone'), w: L(zw), d: L(zd), drop: L(z.dropCm / 100) }) }); }
    for (const p of snap.placements.filter(x => x.roomId === r.id).sort((a, b) => ord(a, b))){
      const rv = resolve(cat, p.variantId), fp = footprintOf(cat, p), name = rv ? rv.product.name : p.group;
      items.push({ kind: 'placement', id: p.id, label: fp ? t('mirror.piece', { name, w: L(fp.x1 - fp.x0), d: L(fp.z1 - fp.z0) }) : name });
    }
    // obiectele de pe pereții camerei (tablou, oglindă, raft, TV), cu mărimea și cota centrului
    for (const it of (f.wallItems ?? []).filter(x => wallItemRoom(f, x)?.id === r.id)) items.push(wallItemEntry(it));
    groups.push({ id: r.id, title: r.name, items });
  }
  // piesele care nu sunt în nicio cameră existentă (cameră ștearsă, piesă mutată afară)
  const loose = snap.placements.filter(p => !f.rooms.some(r => r.id === p.roomId));
  if (loose.length) groups.push({ id: 'loose', title: t('mirror.loose'), items: loose.sort((a, b) => ord(a, b)).map(p => ({ kind: 'placement' as const, id: p.id, label: resolve(cat, p.variantId)?.product.name ?? p.group })) });
  const struct: MirrorItem[] = [];
  const mid = (w: { a: [number, number]; b: [number, number] }) => ({ x: (w.a[0] + w.b[0]) / 2, z: (w.a[1] + w.b[1]) / 2 });
  for (const wl of [...f.walls].sort((a, b) => ord({ ...mid(a), id: a.id }, { ...mid(b), id: b.id }))){
    const rooms = roomsAlongWall(snap, wl), where = rooms.length ? rooms.join(', ') : t('mirror.noRoom');
    // starea peretelui (nou / de demolat) la finalul descrierii; peretele existent rămâne ca înainte
    struct.push({ kind: 'wall', id: wl.id, label: t(wl.exterior ? 'mirror.wallExt' : 'mirror.wall', { len: L(len(wl)), rooms: where }) + (wl.status === 'new' || wl.status === 'demolish' ? ` · ${t(`wallStatus.${wl.status}`)}` : '') });
    for (const op of [...activeOpenings(wl)].sort((a, b) => a.offset - b.offset))
      { const at = roomsAtOpening(snap, wl, op); struct.push({ kind: 'opening', id: op.id, wallId: wl.id, parent: wl.id, label: t(op.kind === 'door' ? (op.passage ? 'mirror.passage' : op.entrance ? 'mirror.entrance' : 'mirror.door') : 'mirror.window', { w: L(op.width), rooms: at.length ? at.join(', ') : where }) + (op.kind === 'door' && !op.passage && (op.doorType && op.doorType !== 'swing' || op.flush) ? ` · ${[op.doorType && op.doorType !== 'swing' ? t(`door.type.${op.doorType}`) : '', op.flush ? t('door.flushShort') : ''].filter(Boolean).join(', ')}` : '') }); }
    // nișele peretelui, după poziție, cu camera în care dau
    for (const n of (f.niches ?? []).filter(x => x.wallId === wl.id).sort((a, b) => a.offset - b.offset))
      struct.push({ kind: 'niche', id: n.id, parent: wl.id, label: t('mirror.niche', { w: L(n.width), h: L(n.height), d: L(n.depth), rooms: nicheRoom(f, n)?.name ?? where }) });
    // obiectele de pe perete care nu dau în nicio cameră rămân lângă peretele lor
    for (const it of (f.wallItems ?? []).filter(x => x.wallId === wl.id && !wallItemRoom(f, x)).sort((a, b) => a.offset - b.offset)) struct.push({ ...wallItemEntry(it), parent: wl.id });
  }
  for (const c of f.columns ?? []) struct.push({ kind: 'column', id: c.id, label: t(c.role === 'shaft' ? 'mirror.shaft' : c.role === 'boxing' ? 'mirror.boxing' : 'mirror.column', { w: L(c.w), d: L(c.d) }) });
  for (const b of f.beams ?? []) struct.push({ kind: 'beam', id: b.id, label: t('mirror.beam', { len: L(len(b)) }) });
  for (const g of f.glassWalls ?? []) struct.push({ kind: 'glass_wall', id: g.id, label: t('mirror.glass', { len: L(len(g)) }) });
  for (const b of f.railings ?? []) struct.push({ kind: 'railing', id: b.id, label: t('mirror.railing', { kind: t(`railing.kind.${railingKind(b)}`), len: L(len(b)), h: L(railingHeight(b)) }) });
  for (const s of f.stairs ?? []) struct.push({ kind: 'stair', id: s.id, label: t('mirror.stair', { w: L(s.width), len: L(s.length) }) });
  if (struct.length) groups.push({ id: 'structure', title: t('mirror.structure'), items: struct });
  // cotele și textele planșei, în ordinea citirii
  const notes = [...(f.annotations ?? [])].sort((a, b) => { const [ax, az] = annotationCenter(a), [bx, bz] = annotationCenter(b); return ord({ x: ax, z: az, id: a.id }, { x: bx, z: bz, id: b.id }); })
    .map((a): MirrorItem => ({ kind: 'annotation', id: a.id, label: a.kind === 'dim' ? t(a.mode === 'ortho' ? 'mirror.dimOrtho' : 'mirror.dim', { len: L(dimGeometry(a).value) }) : t('mirror.text', { text: a.text }) }));
  if (notes.length) groups.push({ id: 'notes', title: t('mirror.notes'), items: notes });
  return groups;
}
/** Descrierea unui obiect selectat (pentru anunțul din regiunea live), sau null. */
export function describeSelection(groups: readonly MirrorGroup[], sel: { kind: string; id: string } | null): string | null {
  if (!sel) return null; for (const g of groups) for (const i of g.items) if (i.kind === sel.kind && i.id === sel.id) return i.label; return null;
}
