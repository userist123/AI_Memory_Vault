// Redimensionarea unei camere mută pereții ei (audit: valul anterior, Editor.tsx „lățime/lungime”).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { resizeRoom, RoomResizeError, MIN_WALL_M } from '../core/room-resize';
import { validateFloor } from '../core/validate';
import { wallLength } from '../core/geometry';
import type { Floor } from '../core/types';
import oneBedroom from '../data/templates/one-bedroom.json';
import twoBedroom from '../data/templates/two-bedroom.json';
import threeBedroom from '../data/templates/three-bedroom.json';
import studio from '../data/templates/studio.json';

const box = (): Floor => ({ id: 'f', name: 'Parter', ceilingHeight: 2.6, rooms: [{ id: 'r', name: 'Cameră', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 3 } }],
  walls: [{ id: 'n', a: [0, 0], b: [4, 0], thickness: .2, exterior: true, openings: [] }, { id: 'e', a: [4, 0], b: [4, 3], thickness: .2, exterior: true, openings: [{ id: 'w1', kind: 'window', offset: 1, width: 1.2 }] },
    { id: 's', a: [4, 3], b: [0, 3], thickness: .2, exterior: true, openings: [{ id: 'd1', kind: 'door', offset: 1, width: .8 }] }, { id: 'v', a: [0, 3], b: [0, 0], thickness: .2, exterior: true, openings: [] }] });
let n = 0; const id = () => `nou-${++n}`;
const w = (f: Floor, i: string) => f.walls.find(x => x.id === i)!;

test('lățimea mai mare: peretele din dreapta se mută, pereții de sus și de jos se lungesc, golurile rămân pe pereții lor', () => {
  const f = resizeRoom(box(), 'r', { x0: 0, z0: 0, x1: 5, z1: 3 }, id);
  assert.deepEqual(f.rooms[0]!.rect, { x0: 0, z0: 0, x1: 5, z1: 3 });
  assert.deepEqual([w(f, 'e').a, w(f, 'e').b], [[5, 0], [5, 3]]); assert.deepEqual(w(f, 'e').openings, box().walls[1]!.openings);
  assert.deepEqual(w(f, 'n').b, [5, 0]); assert.deepEqual(w(f, 's').a, [5, 3]); assert.deepEqual(w(f, 's').openings[0]!.offset, 2, 'capătul a s-a mutat cu 1 m: ușa rămâne în același loc în spațiu (x 3 → 2,2)');
  assert.deepEqual([w(f, 'v').a, w(f, 'v').b], [[0, 3], [0, 0]], 'peretele din stânga nu se mișcă'); assert.equal(f.walls.length, 4);
});
test('lungimea mai mică: peretele de jos urcă, pereții laterali se scurtează; revenirea la dimensiunea veche refacă planul', () => {
  const f = resizeRoom(box(), 'r', { x0: 0, z0: 0, x1: 4, z1: 2.5 }, id);
  assert.deepEqual([w(f, 's').a, w(f, 's').b], [[4, 2.5], [0, 2.5]]); assert.deepEqual(w(f, 'e').b, [4, 2.5]); assert.deepEqual(w(f, 'v').a, [0, 2.5]);
  const back = resizeRoom(f, 'r', { x0: 0, z0: 0, x1: 4, z1: 3 }, id); assert.deepEqual(back.walls, box().walls);
});
test('un perete comun mai lung decât camera se împarte: bucata camerei se mută, restul rămâne, iar conturul se închide', () => {
  const f0 = oneBedroom as unknown as Floor;   // living x 3..8, z 0..3.6; peretele exterior est (wall-2) merge de la z 0 la 6,4
  const f = resizeRoom(f0, 'living', { x0: 3, z0: 0, x1: 8.5, z1: 3.6 }, id);
  const east = f.walls.filter(x => x.a[0] === 8.5 && x.b[0] === 8.5), stay = f.walls.filter(x => x.a[0] === 8 && x.b[0] === 8);
  assert.equal(east.length, 1, 'bucata livingului s-a mutat la x = 8,5'); assert.deepEqual([east[0]!.a[1], east[0]!.b[1]].sort((a, b) => a - b), [0, 3.6]);
  assert.equal(stay.length, 1, 'bucata dormitorului a rămas la x = 8'); assert.deepEqual([stay[0]!.a[1], stay[0]!.b[1]].sort((a, b) => a - b), [3.6, 6.4]);
  const jog = f.walls.find(x => x.a[1] === 3.6 && x.b[1] === 3.6 && Math.min(x.a[0], x.b[0]) === 8 && Math.max(x.a[0], x.b[0]) === 8.5);
  assert.ok(jog, 'o bucată scurtă închide conturul la z = 3,6'); assert.equal(jog!.exterior, true);
  assert.deepEqual(f.walls.find(x => x.id === 'wall-1')!.b, [8.5, 0], 'peretele de nord se lungește');
  assert.deepEqual(f.walls.find(x => x.id === 'wall-8')!.b, [8.5, 3.6], 'peretele dintre living și dormitor se lungește');
  assert.deepEqual(f.rooms.find(r => r.id === 'dormitor')!.rect, f0.rooms.find(r => r.id === 'dormitor')!.rect, 'celelalte camere nu se schimbă');
});
test('o cameră inexistentă lasă planul neschimbat', () => { const f = box(); assert.equal(resizeRoom(f, 'nu', { x0: 0, z0: 0, x1: 1, z1: 1 }, id), f); });

// Regresii din recenzie: redimensionările obișnuite nu mai produc pereți sau goluri pe care validateFloor le marchează ERROR
// (lib/repo.ts → projectErrors ar bloca salvarea reviziei).
const errors = (f: Floor) => validateFloor(f).filter(i => i.severity === 'ERROR');
const world = (f: Floor, opId: string) => { for (const x of f.walls){ const o = x.openings.find(o => o.id === opId); if (!o) continue; const L = wallLength(x.a, x.b), ux = (x.b[0] - x.a[0]) / L, uz = (x.b[1] - x.a[1]) / L;
  return [x.a[0] + ux * o.offset, x.a[1] + uz * o.offset, x.a[0] + ux * (o.offset + o.width), x.a[1] + uz * (o.offset + o.width)].map(v => Math.round(v * 1000) / 1000); } return null; };
const byId = <T extends { id: string }>(xs: T[]) => [...xs].sort((p, q) => p.id.localeCompare(q.id));
const sameWalls = (p: Floor, q: Floor) => assert.deepEqual(p.walls.map(x => ({ ...x, openings: byId(x.openings) })), q.walls.map(x => ({ ...x, openings: byId(x.openings) })));
const living = () => structuredClone(oneBedroom) as unknown as Floor, LR = { x0: 3, z0: 0, x1: 8, z1: 3.6 };

test('living x1 = 8,1 / 7,9 / 6: fără pereți sub 20 cm și fără goluri în afara peretelui', () => {
  for (const x1 of [8.1, 7.9, 6, 8.19, 7.81]){
    const f = resizeRoom(living(), 'living', { ...LR, x1 }, id);
    assert.deepEqual(errors(f), [], `x1 = ${x1}`);
    assert.ok(f.walls.every(x => wallLength(x.a, x.b) >= MIN_WALL_M - 1e-9), `x1 = ${x1}: niciun perete sub 20 cm`);
  }
});
test('golurile își păstrează poziția în spațiu când se mută capătul a al peretelui și sunt ținute în perete', () => {
  const f0 = box(), before = world(f0, 'd1');   // ușa d1 e pe peretele de jos (a = [4,3]), la x 3 → 2,2
  assert.deepEqual(world(resizeRoom(f0, 'r', { x0: 0, z0: 0, x1: 4.6, z1: 3 }, id), 'd1'), before, 'camera mai lată');
  assert.deepEqual(world(resizeRoom(f0, 'r', { x0: 0, z0: 0, x1: 3.2, z1: 3 }, id), 'd1'), before, 'camera mai îngustă, ușa încă încape');
  const f = resizeRoom(f0, 'r', { x0: 0, z0: 0, x1: 2, z1: 3 }, id), s = w(f, 's');   // ușa ar ieși din perete: e împinsă înapoi
  assert.deepEqual(s.openings[0]!.offset, 0); assert.ok(s.openings[0]!.offset + s.openings[0]!.width <= wallLength(s.a, s.b) + 1e-9); assert.deepEqual(errors(f), []);
});
test('o redimensionare imposibilă e refuzată cu un mesaj în română, iar planul nu se schimbă', () => {
  const f0 = box(), snapshot = JSON.stringify(f0);
  assert.throws(() => resizeRoom(f0, 'r', { x0: 0, z0: 0, x1: .6, z1: 3 }, id), (e: unknown) => e instanceof RoomResizeError && /Ușa d1 \(80 cm\) nu mai încape/.test(e.message));
  assert.throws(() => resizeRoom(f0, 'r', { x0: 0, z0: 0, x1: 4, z1: .6 }, id), (e: unknown) => e instanceof RoomResizeError && /Fereastra w1 \(120 cm\) nu mai încape/.test(e.message));
  assert.throws(() => resizeRoom(f0, 'r', { x0: 0, z0: 0, x1: .1, z1: 3 }, id), (e: unknown) => e instanceof RoomResizeError && /cel puțin 20 cm/.test(e.message));
  assert.equal(JSON.stringify(f0), snapshot, 'planul primit nu e modificat');
});
test('micșorare apoi revenire: pereții împărțiți se lipesc la loc (8 → 10 → 8 pereți, aceiași pereți)', () => {
  for (const x1 of [7.5, 7.9, 8.5, 8.1, 6]){
    const f0 = living(), f = resizeRoom(f0, 'living', { ...LR, x1 }, id), back = resizeRoom(f, 'living', LR, id);
    assert.equal(back.walls.length, f0.walls.length, `x1 = ${x1}`);
    if (x1 !== 6) sameWalls(back, f0);   // la 6 m o fereastră a fost împinsă înapoi în perete, deci nu mai stă unde era
  }
  for (const rect of [{ ...LR, x1: 7.5, z1: 3.2 }, { ...LR, x1: 8.4, z1: 3.9 }, { ...LR, x1: 7.6, z1: 4 }]){
    const f0 = living(), back = resizeRoom(resizeRoom(f0, 'living', rect, id), 'living', LR, id); sameWalls(back, f0);
  }
  const f0 = living(), shrunk = resizeRoom(f0, 'living', { ...LR, x1: 7.5 }, id); assert.equal(shrunk.walls.length, 10, 'bucata dormitorului și închiderea');
  const steps = [7, 7.4, 8.3, 7.9, 8].reduce((f, x1) => resizeRoom(f, 'living', { ...LR, x1 }, id), shrunk);
  assert.equal(steps.walls.length, f0.walls.length, 'mai mulți pași, înapoi la 8 pereți'); assert.deepEqual(errors(steps), []);
});
test('toate camerele din toate șabloanele, la mai multe lățimi și lungimi (inclusiv minimul): fără ERROR, revenirea reface pereții', () => {
  let ok = 0, refused = 0;
  for (const [name, t] of Object.entries({ oneBedroom, twoBedroom, threeBedroom, studio })){
    const f0 = t as unknown as Floor; assert.deepEqual(errors(f0), [], `${name} pornește valid`);
    for (const room of f0.rooms){
      const r = room.rect, W = r.x1 - r.x0, D = r.z1 - r.z0, rects: [string, typeof r][] = [];
      for (const w2 of [.6, W - 1.5, W - .5, W - .1, W + .1, W + .5, W + 1].filter(v => v >= .6)) rects.push([`lățime ${w2.toFixed(2)}`, { ...r, x1: r.x0 + w2 }]);
      for (const d2 of [.6, D - 1.5, D - .5, D - .1, D + .1, D + .5, D + 1].filter(v => v >= .6)) rects.push([`lungime ${d2.toFixed(2)}`, { ...r, z1: r.z0 + d2 }]);
      rects.push(['ambele −0,3', { ...r, x1: r.x1 - .3, z1: r.z1 - .3 }], ['ambele +0,3', { ...r, x1: r.x1 + .3, z1: r.z1 + .3 }]);
      for (const [what, rect] of rects){
        const tag = `${name}/${room.id} ${what}`;
        let f: Floor;
        try { f = resizeRoom(f0, room.id, rect, id); } catch (e){ assert.ok(e instanceof RoomResizeError, `${tag}: doar refuz explicit`); assert.match(e.message, /^Camera nu se poate redimensiona: /); refused++; continue; }
        assert.deepEqual(errors(f), [], tag); ok++;
        const back = resizeRoom(f, room.id, r, id);
        assert.equal(back.walls.length, f0.walls.length, `${tag}: revenirea reface numărul de pereți`); assert.deepEqual(errors(back), [], `${tag}: revenirea e validă`);
      }
    }
  }
  assert.ok(ok > 8 * refused, `aproape toate redimensionările reușesc (${ok} reușite, ${refused} refuzate)`);
});
