import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { floors, levelView, mergeLevel, addLevel, removeLevel, elevationOf, levelOfRoom, stairRect, stairVoids, stairIssues, stairGeometry, comfortableStairLength, stairwells, SLAB, MAX_LEVELS } from '../core/levels';
import type { Catalog, Snapshot, Stair } from '../core/types';

const cat = catalogJson as unknown as Catalog;
let n = 0; const id = () => `t${++n}`;
const house = (): Snapshot => newSnapshot(cat, 'Casa', 'two-bedroom', 'en');
const twoLevels = () => { const s = addLevel(house(), { name: 'Level 1', id }); const up = s.levels![0];
  // o piesă și un punct tehnic pe etaj, în prima cameră copiată
  const r = up.rooms[0], cx = (r.rect.x0 + r.rect.x1) / 2, cz = (r.rect.z0 + r.rect.z1) / 2;
  s.placements.push({ id: 'up-1', roomId: r.id, group: s.placements[0].group, variantId: s.placements[0].variantId, x: cx, z: cz, rotation: 0, source: 'manual' });
  s.tech = [...(s.tech ?? []), { id: 'tp-up', kind: 'outlet', roomId: r.id, x: r.rect.x0, z: cz, height: .3, reason: 'manual' }];
  return s; };

test('fără niveluri: floors are doar parterul, vederea nivelului 0 e proiectul neschimbat', () => {
  const s = house(); assert.equal(floors(s).length, 1); assert.deepEqual(levelView(s, 0), s);
  assert.deepEqual(mergeLevel(s, 0, levelView(s, 0)), s);
});
test('addLevel copiază conturul cu id-uri noi, unice pe toată casa, fără mobilier', () => {
  const s0 = house(), s = addLevel(s0, { name: 'Level 1', id }), up = s.levels![0];
  assert.equal(floors(s).length, 2); assert.equal(up.name, 'Level 1');
  assert.equal(up.walls.length, s0.floor.walls.length); assert.equal(up.rooms.length, s0.floor.rooms.length);
  const ids = floors(s).flatMap(f => [f.id, ...f.rooms.map(r => r.id), ...f.walls.map(w => w.id), ...f.walls.flatMap(w => w.openings.map(o => o.id))]);
  assert.equal(new Set(ids).size, ids.length, 'id-uri unice');
  assert.ok(up.walls.every(w => w.openings.every(o => !o.entrance)), 'nicio intrare la etaj');
  assert.equal(s.placements.length, s0.placements.length, 'mobilierul nu se copiază');
  assert.equal(s0.levels, undefined, 'originalul nu se modifică');
});
test('vederea unui etaj are doar camerele, piesele și punctele lui; merge le pune la loc', () => {
  const s = twoLevels(), v1 = levelView(s, 1), v0 = levelView(s, 0);
  assert.equal(v1.floor, s.levels![0]); assert.deepEqual(v1.placements.map(p => p.id), ['up-1']); assert.deepEqual(v1.tech!.map(t => t.id), ['tp-up']);
  assert.ok(!v0.placements.some(p => p.id === 'up-1')); assert.ok(!v0.tech!.some(t => t.id === 'tp-up'));
  assert.equal(v0.placements.length + v1.placements.length, s.placements.length);
  // o mutare pe etaj nu atinge parterul
  const moved = { ...v1, placements: v1.placements.map(p => ({ ...p, x: p.x + .1 })) }, m = mergeLevel(s, 1, moved);
  assert.equal(m.placements.find(p => p.id === 'up-1')!.x, s.placements.find(p => p.id === 'up-1')!.x + .1);
  assert.deepEqual(levelView(m, 0).placements, v0.placements); assert.equal(m.floor, s.floor);
  // ștergerea unei camere de pe etaj în vedere se vede după merge
  const fewer = { ...v1, floor: { ...v1.floor, rooms: v1.floor.rooms.slice(1) } }, m2 = mergeLevel(s, 1, fewer);
  assert.equal(m2.levels![0].rooms.length, s.levels![0].rooms.length - 1);
});
test('piesele fără cameră rămân pe parter (ca înainte de niveluri)', () => {
  const s = twoLevels(); s.placements.push({ ...s.placements[0], id: 'orphan', roomId: 'nu-exista' });
  assert.ok(levelView(s, 0).placements.some(p => p.id === 'orphan')); assert.ok(!levelView(s, 1).placements.some(p => p.id === 'orphan'));
  assert.equal(levelOfRoom(s, 'nu-exista'), -1); assert.equal(levelOfRoom(s, s.levels![0].rooms[0].id), 1); assert.equal(levelOfRoom(s, s.floor.rooms[0].id), 0);
});
test('cota fiecărui nivel = înălțimile de dedesubt + placa', () => {
  const s = addLevel(addLevel(house(), { name: 'L1', id }), { name: 'L2', id }); s.levels![0].ceilingHeight = 2.8;
  assert.equal(elevationOf(s, 0), 0); assert.equal(elevationOf(s, 1), s.floor.ceilingHeight + SLAB);
  assert.ok(Math.abs(elevationOf(s, 2) - (s.floor.ceilingHeight + 2.8 + 2 * SLAB)) < 1e-9);
});
test('removeLevel șterge nivelul, piesele, punctele, aspectul lui și scările care duceau la el', () => {
  const s = twoLevels(), up = s.levels![0], rid = up.rooms[0].id;
  s.appearance = { rooms: { [rid]: { walls: { color: '#112233' } }, [s.floor.rooms[0].id]: { walls: { color: '#445566' } } }, items: { 'up-1': { color: '#000000' } } };
  s.finishes = { [rid]: { floor: 'x' } as any };
  s.floor.stairs = [{ id: 'st', x: 1, z: 1, width: 1, length: 3, rotation: 0 }];
  const r = removeLevel(s, 1);
  assert.equal(floors(r).length, 1); assert.equal(r.levels, undefined);
  assert.ok(!r.placements.some(p => p.id === 'up-1')); assert.ok(!r.tech!.some(t => t.id === 'tp-up'));
  assert.equal(r.appearance!.rooms![rid], undefined); assert.ok(r.appearance!.rooms![s.floor.rooms[0].id]); assert.equal(r.appearance!.items!['up-1'], undefined);
  assert.equal(r.finishes![rid], undefined); assert.equal(r.floor.stairs, undefined, 'scara spre nivelul șters dispare');
  assert.throws(() => removeLevel(s, 0)); assert.throws(() => removeLevel(s, 5));
});
test('cel mult MAX_LEVELS niveluri', () => {
  let s = house(); for (let i = 1; i < MAX_LEVELS; i++) s = addLevel(s, { name: `L${i}`, id });
  assert.equal(floors(s).length, MAX_LEVELS); assert.throws(() => addLevel(s, { name: 'prea mult', id }));
});
test('scara: dreptunghiul ei, golul de deasupra și verificările', () => {
  const s = twoLevels(), r = s.floor.rooms.find(x => x.type === 'hol') ?? s.floor.rooms[0];
  const st: Stair = { id: 'st1', x: (r.rect.x0 + r.rect.x1) / 2, z: (r.rect.z0 + r.rect.z1) / 2, width: .9, length: 1.2, rotation: 0 };
  assert.deepEqual(stairRect(st), { x0: st.x - .45, x1: st.x + .45, z0: st.z - .6, z1: st.z + .6 });
  assert.deepEqual(stairRect({ ...st, rotation: Math.PI / 2 }), { x0: st.x - .6, x1: st.x + .6, z0: st.z - .45, z1: st.z + .45 });
  s.floor.stairs = [st];
  assert.deepEqual(stairVoids(s, 1), [stairRect(st)]); assert.deepEqual(stairVoids(s, 0), []);
  assert.deepEqual(stairIssues(s, cat).filter(i => i.key === 'issue.STAIR_OUTSIDE'), []);
  // în afara camerelor -> eroare; pe ultimul nivel -> eroare (nu duce nicăieri)
  const out = structuredClone(s); out.floor.stairs = [{ ...st, x: -50 }];
  assert.ok(stairIssues(out, cat).some(i => i.key === 'issue.STAIR_OUTSIDE' && i.severity === 'ERROR'));
  const top = structuredClone(s); top.levels![0].stairs = [{ ...st, id: 'st2' }];
  assert.ok(stairIssues(top, cat).some(i => i.key === 'issue.STAIR_NO_LEVEL' && i.severity === 'ERROR'));
  // o piesă pe scară (jos) sau în golul scării (sus) -> eroare
  const blocked = structuredClone(s); blocked.placements.push({ ...s.placements[0], id: 'on-stair', roomId: r.id, x: st.x, z: st.z });
  assert.ok(stairIssues(blocked, cat).some(i => i.key === 'issue.STAIR_BLOCKED' && i.with === 'on-stair'));
  const voided = structuredClone(s), upRoom = voided.levels![0].rooms.find(x => x.rect.x0 <= st.x && st.x <= x.rect.x1 && x.rect.z0 <= st.z && st.z <= x.rect.z1)!;
  voided.placements.push({ ...s.placements[0], id: 'in-void', roomId: upRoom.id, x: st.x, z: st.z });
  assert.ok(stairIssues(voided, cat).some(i => i.key === 'issue.STAIR_VOID' && i.with === 'in-void'));
});
test('geometria scării: direcția după rotație, număr de trepte, avertisment pentru scara abruptă', () => {
  const st: Stair = { id: 's', x: 2, z: 3, width: 1, length: 4.32, rotation: 0 };
  const g = stairGeometry(st, 2.8);
  assert.deepEqual(g.dir, [0, -1]); assert.deepEqual(g.bottom, [2, 3 + 2.16]); assert.deepEqual(g.top, [2, 3 - 2.16]);
  assert.equal(g.steps, 16); assert.ok(Math.abs(g.riser - .175) < 1e-9); assert.ok(Math.abs(g.going - .27) < 1e-9);
  assert.deepEqual(stairGeometry({ ...st, rotation: Math.PI / 2 }, 2.8).dir, [-1, 0]);
  assert.deepEqual(stairGeometry({ ...st, rotation: Math.PI }, 2.8).dir, [0, 1]);
  assert.deepEqual(stairGeometry({ ...st, rotation: -Math.PI / 2 }, 2.8).dir, [1, 0]);
  assert.ok(Math.abs(comfortableStairLength(2.8) - 4.32) < 1e-9);
  // o scară scurtă: avertisment cu lungimea recomandată, nu eroare
  const s = twoLevels(), r = s.floor.rooms.find(x => x.type === 'living')!;
  s.floor.stairs = [{ id: 'scurta', x: (r.rect.x0 + r.rect.x1) / 2, z: (r.rect.z0 + r.rect.z1) / 2, width: .9, length: 1.2, rotation: 0 }];
  const steep = stairIssues(s, cat).filter(i => i.key === 'issue.STAIR_STEEP');
  assert.equal(steep.length, 1); assert.equal(steep[0]!.severity, 'WARNING'); assert.equal(steep[0]!.vars!.length, Math.round(comfortableStairLength(s.floor.ceilingHeight + SLAB) * 100));
});
test('scara care blochează deschiderea unei uși e eroare; etajul nou nu copiază ușa de la intrare', () => {
  const s = twoLevels(), hall = s.floor.rooms.find(x => x.type === 'hol')!;
  // scara pe toată lungimea holului trece prin dreptul ușilor care se deschid în hol
  s.floor.stairs = [{ id: 'hol', x: (hall.rect.x0 + hall.rect.x1) / 2, z: (hall.rect.z0 + hall.rect.z1) / 2, width: .9, length: Math.min(4.3, hall.rect.x1 - hall.rect.x0 - .1), rotation: Math.PI / 2 }];
  assert.ok(stairIssues(s, cat).some(i => i.key === 'issue.STAIR_DOOR' && i.severity === 'ERROR'));
  const ground = house(), entrances = ground.floor.walls.flatMap(w => w.openings).filter(o => o.entrance).length, doors = ground.floor.walls.flatMap(w => w.openings).filter(o => o.kind === 'door').length;
  const up = addLevel(ground, { name: 'L1', id }).levels![0]!.walls.flatMap(w => w.openings).filter(o => o.kind === 'door').length;
  assert.ok(entrances > 0); assert.equal(up, doors - entrances, 'ușa de la intrare nu urcă la etaj');
});
test('golul scării în dreptul unei uși de la etaj e eroare (cine iese pe ușă calcă în gol)', () => {
  const s = twoLevels(), hall = s.floor.rooms.find(x => x.type === 'hol')!;
  s.floor.stairs = [{ id: 'hol', x: (hall.rect.x0 + hall.rect.x1) / 2, z: (hall.rect.z0 + hall.rect.z1) / 2, width: .9, length: Math.min(4.3, hall.rect.x1 - hall.rect.x0 - .1), rotation: Math.PI / 2 }];
  assert.ok(stairIssues(s, cat).some(i => i.key === 'issue.STAIR_VOID_DOOR' && i.severity === 'ERROR'));
  // fără uși la etaj în dreptul golului: nicio eroare de acest fel
  const t = structuredClone(s); for (const w of t.levels![0]!.walls) w.openings = w.openings.filter(o => o.kind !== 'door');
  assert.ok(!stairIssues(t, cat).some(i => i.key === 'issue.STAIR_VOID_DOOR'));
});
test('stairwells: golurile nivelului cu direcția scării de dedesubt', () => {
  const s = twoLevels(), st: Stair = { id: 'w', x: 3, z: 3, width: 1, length: 3, rotation: Math.PI / 2 }; s.floor.stairs = [st];
  assert.deepEqual(stairwells(s, 1), [{ ...stairRect(st), dir: [-1, 0] }]); assert.deepEqual(stairwells(s, 0), []);
  assert.deepEqual(stairwells(s, 1).map(({ dir: _d, ...r }) => r), stairVoids(s, 1));
});
