import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { wallFaceRooms, wallFaceColor, roomLook, itemStyle, itemSizeCm, similarVariants, colorDistance, sanitizeAppearance, appearanceColors, normalizeHex, DEFAULT_LOOK } from '../core/appearance';
import { furnitureTotal, resolve } from '../core/catalog';
import { footprintOf, validatePlacement } from '../core/validate';
import { computeBOQ } from '../core/boq';
import { checkSnapshot } from '../lib/repo';
import { snapshotToTwin, placementsFromTwin } from '../lib/twin';
import { planWithLook } from '../lib/plan-look';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Test', 'demo');

test('fiecare față de perete știe camerele în care se vede, inclusiv pereții lungi care trec prin mai multe camere', () => {
  const s = demo(), faces = wallFaceRooms(s.floor);
  assert.equal(faces.length, s.floor.walls.length);
  for (const [i, f] of faces.entries()){ const w = s.floor.walls[i]!; if (w.exterior) assert.ok(!(f.a.length && f.b.length), `${w.id}: un perete exterior are o singură față interioară`); else assert.ok(f.a.length && f.b.length, `${w.id}: un perete interior are camere pe ambele fețe`); }
  assert.ok(faces.some(f => f.a.length > 1 || f.b.length > 1), 'demo-ul are un perete exterior lung lângă două camere');
});
test('culoarea feței: accentul bate culoarea camerei, care bate implicitul', () => {
  const s = demo(), f = wallFaceRooms(s.floor).find(x => x.a.length === 1 && x.b.length === 1)!, A = f.a[0]!, Bk = f.b[0]!;
  assert.equal(wallFaceColor(s, f.wallId, A), DEFAULT_LOOK.wall);
  s.appearance = { rooms: { [A]: { walls: { color: '#C5D8E0' } } } };
  assert.equal(wallFaceColor(s, f.wallId, A), '#c5d8e0'); assert.equal(wallFaceColor(s, f.wallId, Bk), DEFAULT_LOOK.wall, 'cealaltă cameră nu se schimbă');
  s.appearance.wallFaces = { [`${f.wallId}@${A}`]: { color: '#c94f3d' } };
  assert.equal(wallFaceColor(s, f.wallId, A), '#c94f3d', 'peretele accent');
  const p = planWithLook(s), i = s.floor.walls.findIndex(w => w.id === f.wallId);
  assert.deepEqual(p.pereti[i].accente, { [A]: '#c94f3d' }); assert.equal(p.camere.find((c: any) => c.id === A).pereti, '#c5d8e0'); assert.equal(p.camere.find((c: any) => c.id === Bk).pereti, undefined);
});
test('podeaua, tavanul și ramele ajung în planul pentru 3D', () => {
  const s = demo(), r = s.floor.rooms[0]!, w = s.floor.walls.find(w => w.openings.length)!, o = w.openings[0]!;
  s.appearance = { rooms: { [r.id]: { floor: { color: '#9a5b3c' }, ceiling: { color: '#e3ecef' } } }, openings: { [o.id]: { color: '#1f1f1f' } } };
  assert.deepEqual(roomLook(s, r.id), { walls: DEFAULT_LOOK.wall, floorTint: '#9a5b3c', ceiling: '#e3ecef' });
  const p = planWithLook(s); assert.equal(p.camere.find((c: any) => c.id === r.id).podea, '#9a5b3c'); assert.equal(p.camere.find((c: any) => c.id === r.id).tavan, '#e3ecef');
  const wi = s.floor.walls.indexOf(w); assert.equal(p.pereti[wi].goluri[0].culoare, '#1f1f1f');
});
test('culoarea și materialul piesei suprascriu stilul variantei doar unde modelul permite', () => {
  const s = demo(), sofa = s.placements.find(p => p.group === 'canapea')!, base = resolve(cat, sofa.variantId)!.variant.style;
  s.appearance = { items: { [sofa.id]: { color: '#3B4F8C', material: 'velvet' } } };
  assert.deepEqual(itemStyle(s, sofa, base, 'sofa'), { ...base, col: '#3b4f8c', mat: 'velvet' });
  const bed = s.placements.find(p => p.group === 'pat')!; s.appearance.items![bed.id] = { material: 'wood' };
  assert.equal(itemStyle(s, bed, {}, 'bed').wood, true);
  s.appearance.items![bed.id] = { material: 'velvet' }; assert.equal(itemStyle(s, bed, {}, 'bed').wood, undefined, 'material nepermis pentru pat: ignorat');
});
test('piesa pe comandă: dimensiuni proprii în plan și validare, preț necunoscut în buget', () => {
  const s = demo(), p = s.placements.find(x => x.group === 'masuta')!, rv = resolve(cat, p.variantId)!;
  assert.deepEqual(itemSizeCm(cat, p), rv.variant.dimensionsCm);
  p.size = { w: 60, d: 60, h: 45 };
  const fp = footprintOf(cat, p)!; assert.ok(Math.abs((fp.x1 - fp.x0) - 0.6) < 1e-9 && Math.abs((fp.z1 - fp.z0) - 0.6) < 1e-9);
  assert.equal(furnitureTotal(cat, [p]).unknown, 1);
  const line = computeBOQ(s, cat, mc).items.find(i => i.key === p.id)!; assert.equal(line.total, null); assert.match(line.label, /pe comandă 60×60×45 cm/);
  p.size = { w: 900, d: 60, h: 45 }; assert.ok(validatePlacement(s, cat, p).some(i => i.code === 'OUT_OF_ROOM'), 'o piesă uriașă iese din cameră');
});
test('serverul acceptă doar dimensiuni și aspect plauzibile', () => {
  const s = demo(); s.placements[0]!.size = { w: 5, d: 60, h: 45 };
  assert.throws(() => checkSnapshot(structuredClone(s)), (e: any) => e.status === 400);
  s.placements[0]!.size = { w: 120, d: 60, h: 45 }; assert.doesNotThrow(() => checkSnapshot(structuredClone(s)));
  const w = s.floor.walls.find(w => w.openings.length)!; w.openings[0]!.height = 5; assert.throws(() => checkSnapshot(structuredClone(s)), (e: any) => e.status === 400); w.openings[0]!.height = w.openings[0]!.kind === 'door' ? 2.2 : 1.5;
  s.appearance = { rooms: { [s.floor.rooms[0]!.id]: { walls: { color: 'roșu' }, floor: { color: '#abc' } }, 'nu-exista': { walls: { color: '#ffffff' } } }, items: { [s.placements[0]!.id]: { color: '#123456', material: 'plutoniu' } }, wallFaces: { 'x@y': { color: '#ffffff' } } } as any;
  const c = checkSnapshot(structuredClone(s));
  assert.deepEqual(c.appearance, { rooms: { [s.floor.rooms[0]!.id]: { floor: { color: '#aabbcc' } } }, items: { [s.placements[0]!.id]: { color: '#123456' } } });
  assert.equal(sanitizeAppearance({ rooms: {} }, s), undefined); assert.equal(normalizeHex('#FFF'), '#ffffff'); assert.equal(normalizeHex('red'), null);
});
test('culoarea aleasă găsește produsele reale cele mai apropiate din aceeași categorie', () => {
  const r = similarVariants(cat, 'canapea', '#3c6a6b');
  assert.ok(r.length >= 2); assert.equal(r[0]!.variantId, 'canapea-2', 'gri-turcoaz e cea mai apropiată de turcoaz');
  for (let i = 1; i < r.length; i++) assert.ok(r[i]!.distance >= r[i - 1]!.distance);
  assert.ok(colorDistance('#ffffff', '#ffffff') === 0 && colorDistance('#000000', '#ffffff') > 90);
  assert.deepEqual(similarVariants(cat, 'canapea', 'nu-e-culoare'), []);
});
test('culorile proiectului pentru consilier includ pereții, accentele și mobila', () => {
  const s = demo(), f = wallFaceRooms(s.floor).find(x => x.a.length)!, A = f.a[0]!; s.appearance = { wallFaces: { [`${f.wallId}@${A}`]: { color: '#c94f3d' } } };
  const cs = appearanceColors(s, cat);
  assert.ok(cs.some(c => c.kind === 'wall' && c.hex === '#c94f3d' && c.roomId === A));
  assert.equal(cs.filter(c => c.kind === 'item').length, s.placements.filter(p => resolve(cat, p.variantId)?.variant.style?.col).length);
});
test('twin-ul păstrează dimensiunile pe comandă la dus-întors', () => {
  const s = demo(), p = s.placements.find(x => x.group === 'masuta')!; p.size = { w: 70, d: 70, h: 40 };
  const t = snapshotToTwin(s, cat), q = t.placements.find(x => x.id === p.id)!; assert.equal(q.w, 0.7); assert.equal(q.d, 0.7);
  const back = placementsFromTwin(s, cat, t).find(x => x.id === p.id)!; assert.deepEqual(back.size, p.size); assert.ok(Math.abs(back.x - p.x) < 1e-6 && Math.abs(back.z - p.z) < 1e-6);
});
