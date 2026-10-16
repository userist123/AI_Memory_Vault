// Redimensionarea unei camere mută și latura comună a camerei vecine (runda 8): camerele rămân lipite, fără gol și fără
// suprapunere; pereții, ușile și ferestrele de pe latura comună urmează o singură dată (core/room-resize.ts).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { resizeRoom, RoomResizeError } from '../core/room-resize';
import { edgeGroup, clampEdge, MIN_ROOM_M, SHARE_TOL } from '../core/room-neighbours';
import { validateFloor } from '../core/validate';
import { wallLength } from '../core/geometry';
import { History } from '../core/history';
import { levelView, mergeLevel } from '../core/levels';
import type { Floor, Room, RoomRect, Snapshot, Wall } from '../core/types';
import oneBedroom from '../data/templates/one-bedroom.json';
import twoBedroom from '../data/templates/two-bedroom.json';
import threeBedroom from '../data/templates/three-bedroom.json';
import studio from '../data/templates/studio.json';

let n = 0; const id = () => `nou-${++n}`;
const N = { neighbours: true } as const;
const room = (rid: string, rect: RoomRect): Room => ({ id: rid, name: rid, type: 'living', rect });
const wall = (wid: string, a: [number, number], b: [number, number], extra: Partial<Wall> = {}): Wall => ({ id: wid, a, b, thickness: .2, exterior: false, openings: [], ...extra });
const rectOf = (f: Floor, rid: string) => f.rooms.find(r => r.id === rid)!.rect;
const w = (f: Floor, wid: string) => f.walls.find(x => x.id === wid)!;
const errors = (f: Floor) => validateFloor(f).filter(i => i.severity === 'ERROR');
const world = (f: Floor, opId: string) => { for (const x of f.walls){ const o = x.openings.find(o => o.id === opId); if (!o) continue; const L = wallLength(x.a, x.b), ux = (x.b[0] - x.a[0]) / L, uz = (x.b[1] - x.a[1]) / L;
  return [x.a[0] + ux * o.offset, x.a[1] + uz * o.offset, x.a[0] + ux * (o.offset + o.width), x.a[1] + uz * (o.offset + o.width)].map(v => Math.round(v * 1000) / 1000); } return null; };

/** A (x 0..4) și B (x 4..8), z 0..3; pereții de sus și de jos sunt separați pe cameră, peretele comun are o ușă. */
const pair = (bx0 = 4): Floor => ({ id: 'f', name: 'Parter', ceilingHeight: 2.6,
  rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 }), room('B', { x0: bx0, z0: 0, x1: 8, z1: 3 })],
  walls: [wall('nA', [0, 0], [4, 0], { exterior: true }), wall('nB', [4, 0], [8, 0], { exterior: true, openings: [{ id: 'w1', kind: 'window', offset: 1.5, width: 1 }] }),
    wall('e', [8, 0], [8, 3], { exterior: true }), wall('sB', [8, 3], [4, 3], { exterior: true }), wall('sA', [4, 3], [0, 3], { exterior: true }),
    wall('v', [0, 3], [0, 0], { exterior: true }), wall('m', [4, 0], [4, 3], { thickness: .1, openings: [{ id: 'd1', kind: 'door', offset: 1, width: .8 }] })] });

test('stânga/dreapta: A mai lată împinge latura din stânga a lui B; peretele comun, ușa și pereții perpendiculari urmează', () => {
  const f0 = pair(), f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 5, z1: 3 }, id, N);
  assert.deepEqual(rectOf(f, 'A'), { x0: 0, z0: 0, x1: 5, z1: 3 });
  assert.deepEqual(rectOf(f, 'B'), { x0: 5, z0: 0, x1: 8, z1: 3 }, 'B se îngustează cu exact același metru');
  assert.deepEqual([w(f, 'm').a, w(f, 'm').b], [[5, 0], [5, 3]], 'peretele comun s-a mutat o singură dată');
  assert.deepEqual(w(f, 'm').openings, f0.walls.find(x => x.id === 'm')!.openings, 'ușa rămâne pe peretele comun');
  assert.deepEqual(w(f, 'nA').b, [5, 0]); assert.deepEqual(w(f, 'nB').a, [5, 0]); assert.deepEqual(w(f, 'sB').b, [5, 3]); assert.deepEqual(w(f, 'sA').a, [5, 3]);
  assert.deepEqual(world(f, 'w1'), world(f0, 'w1'), 'fereastra lui B rămâne în același loc în spațiu');
  assert.equal(f.walls.length, f0.walls.length); assert.deepEqual(errors(f), []);
  // și invers: B mai lată spre stânga îngustează A
  const g = resizeRoom(f0, 'B', { x0: 3.5, z0: 0, x1: 8, z1: 3 }, id, N);
  assert.deepEqual(rectOf(g, 'A'), { x0: 0, z0: 0, x1: 3.5, z1: 3 }); assert.deepEqual(w(g, 'm').a, [3.5, 0]); assert.deepEqual(errors(g), []);
});

test('sus/jos: A mai scurtă trage latura de sus a camerei C de dedesubt', () => {
  const f0: Floor = { id: 'f', name: 'P', ceilingHeight: 2.6, rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 }), room('C', { x0: 0, z0: 3, x1: 4, z1: 6 })],
    walls: [wall('n', [0, 0], [4, 0], { exterior: true }), wall('eA', [4, 0], [4, 3], { exterior: true }), wall('eC', [4, 3], [4, 6], { exterior: true }),
      wall('s', [4, 6], [0, 6], { exterior: true }), wall('vC', [0, 6], [0, 3], { exterior: true }), wall('vA', [0, 3], [0, 0], { exterior: true }), wall('m', [0, 3], [4, 3])] };
  const f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 4, z1: 2.5 }, id, N);
  assert.deepEqual(rectOf(f, 'C'), { x0: 0, z0: 2.5, x1: 4, z1: 6 });
  assert.deepEqual([w(f, 'm').a, w(f, 'm').b], [[0, 2.5], [4, 2.5]]);
  assert.deepEqual(w(f, 'eA').b, [4, 2.5]); assert.deepEqual(w(f, 'eC').a, [4, 2.5]); assert.deepEqual(w(f, 'vC').b, [0, 2.5]); assert.deepEqual(w(f, 'vA').a, [0, 2.5]);
  assert.deepEqual(errors(f), []);
});

test('doi vecini pe aceeași latură: amândoi se mută, peretele dintre ei se scurtează', () => {
  const f0: Floor = { id: 'f', name: 'P', ceilingHeight: 2.6,
    rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 6 }), room('B1', { x0: 4, z0: 0, x1: 8, z1: 3 }), room('B2', { x0: 4, z0: 3, x1: 8, z1: 6 })],
    walls: [wall('n', [0, 0], [8, 0], { exterior: true }), wall('e', [8, 0], [8, 6], { exterior: true }), wall('s', [8, 6], [0, 6], { exterior: true }), wall('v', [0, 6], [0, 0], { exterior: true }),
      wall('m', [4, 0], [4, 6]), wall('b', [4, 3], [8, 3])] };
  const f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 4.5, z1: 6 }, id, N);
  assert.deepEqual(rectOf(f, 'B1'), { x0: 4.5, z0: 0, x1: 8, z1: 3 }); assert.deepEqual(rectOf(f, 'B2'), { x0: 4.5, z0: 3, x1: 8, z1: 6 });
  assert.deepEqual([w(f, 'm').a, w(f, 'm').b], [[4.5, 0], [4.5, 6]]); assert.deepEqual(w(f, 'b').a, [4.5, 3]);
  assert.deepEqual([w(f, 'n').a, w(f, 'n').b], [[0, 0], [8, 0]], 'peretele exterior continuu nu se schimbă');
  assert.equal(f.walls.length, f0.walls.length); assert.deepEqual(errors(f), []);
});

test('vecin cu suprapunere parțială: B mai înalt decât A se mută întreg, iar C (lipit tot de B) se lărgește odată cu A', () => {
  const f0: Floor = { id: 'f', name: 'P', ceilingHeight: 2.6,
    rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 }), room('B', { x0: 4, z0: 0, x1: 8, z1: 6 }), room('C', { x0: 0, z0: 3, x1: 4, z1: 6 })],
    walls: [wall('n', [0, 0], [8, 0], { exterior: true }), wall('e', [8, 0], [8, 6], { exterior: true }), wall('s', [8, 6], [0, 6], { exterior: true }), wall('v', [0, 6], [0, 0], { exterior: true }),
      wall('m', [4, 0], [4, 6]), wall('ac', [0, 3], [4, 3])] };
  const f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 5, z1: 3 }, id, N);
  assert.deepEqual(rectOf(f, 'B'), { x0: 5, z0: 0, x1: 8, z1: 6 }); assert.deepEqual(rectOf(f, 'C'), { x0: 0, z0: 3, x1: 5, z1: 6 });
  assert.deepEqual([w(f, 'm').a, w(f, 'm').b], [[5, 0], [5, 6]], 'peretele comun întreg, fără bucăți'); assert.deepEqual(w(f, 'ac').b, [5, 3]);
  assert.equal(f.walls.length, f0.walls.length); assert.deepEqual(errors(f), []);
  // vecin mai scurt decât latura: doar el se mută, peretele lui A se mută întreg
  const g0: Floor = { id: 'g', name: 'P', ceilingHeight: 2.6, rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 }), room('B', { x0: 4, z0: 1, x1: 6, z1: 2.5 })],
    walls: [wall('n', [0, 0], [4, 0], { exterior: true }), wall('m', [4, 0], [4, 3], { exterior: true }), wall('s', [4, 3], [0, 3], { exterior: true }), wall('v', [0, 3], [0, 0], { exterior: true }),
      wall('bn', [4, 1], [6, 1]), wall('be', [6, 1], [6, 2.5]), wall('bs', [6, 2.5], [4, 2.5])] };
  const g = resizeRoom(g0, 'A', { x0: 0, z0: 0, x1: 4.5, z1: 3 }, id, N);
  assert.deepEqual(rectOf(g, 'B'), { x0: 4.5, z0: 1, x1: 6, z1: 2.5 }); assert.deepEqual(w(g, 'bn').a, [4.5, 1]); assert.deepEqual(w(g, 'bs').b, [4.5, 2.5]);
  assert.deepEqual(errors(g), []);
});

test('vecinul nu scade sub dimensiunea minimă: mutarea se oprește (clamp), nu se refuză', () => {
  const f0 = pair(); f0.rooms[1]!.rect.x1 = 5; f0.walls = f0.walls.filter(x => !['nB', 'e', 'sB'].includes(x.id))
    .concat([wall('nB', [4, 0], [5, 0], { exterior: true }), wall('e', [5, 0], [5, 3], { exterior: true }), wall('sB', [5, 3], [4, 3], { exterior: true })]);
  assert.deepEqual(errors(f0), []);
  const f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 4.8, z1: 3 }, id, N);
  assert.equal(MIN_ROOM_M, .6);
  assert.deepEqual(rectOf(f, 'B'), { x0: 4.4, z0: 0, x1: 5, z1: 3 }, 'B rămâne la 60 cm'); assert.deepEqual(rectOf(f, 'A'), { x0: 0, z0: 0, x1: 4.4, z1: 3 }, 'A se oprește la latura permisă');
  assert.deepEqual(w(f, 'm').a, [4.4, 0]); assert.deepEqual(errors(f), []);
  assert.deepEqual(clampEdge(f0.rooms, edgeGroup(f0.rooms, 'A', 'x1'), 'A', 'x1', 9), 4.4);
  assert.deepEqual(clampEdge(f0.rooms, edgeGroup(f0.rooms, 'A', 'x1'), 'A', 'x1', 3), 3, 'micșorarea lui A nu e limitată de vecin');
});

test('fără vecin: același rezultat ca redimensionarea simplă', () => {
  const f0: Floor = { id: 'f', name: 'P', ceilingHeight: 2.6, rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 })],
    walls: [wall('n', [0, 0], [4, 0]), wall('e', [4, 0], [4, 3]), wall('s', [4, 3], [0, 3]), wall('v', [0, 3], [0, 0])] };
  const rect = { x0: 0, z0: 0, x1: 5, z1: 2.5 };
  assert.deepEqual(resizeRoom(f0, 'A', rect, id, N), resizeRoom(f0, 'A', rect, id));
  assert.deepEqual(edgeGroup(f0.rooms, 'A', 'x1'), [{ id: 'A', key: 'x1' }]);
});

test('o cameră apropiată, dar fără latură comună, nu se mișcă (gol de 5 cm, doar colț, alt rând)', () => {
  const gap = pair(4.05), f = resizeRoom(gap, 'A', { x0: 0, z0: 0, x1: 3.8, z1: 3 }, id, N);
  assert.deepEqual(rectOf(f, 'B'), rectOf(gap, 'B'), 'gol de 5 cm > toleranța de 1 cm');
  const tight = pair(4.008), g = resizeRoom(tight, 'A', { x0: 0, z0: 0, x1: 3.8, z1: 3 }, id, N);
  assert.deepEqual(rectOf(g, 'B'), { x0: 3.8, z0: 0, x1: 8, z1: 3 }, 'sub 1 cm (toleranța) e latură comună');
  assert.equal(SHARE_TOL, .01);
  const corner: Room[] = [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 }), room('D', { x0: 4, z0: 3, x1: 8, z1: 6 }), room('E', { x0: 4, z0: 3.5, x1: 6, z1: 5 })];
  assert.deepEqual(edgeGroup(corner, 'A', 'x1'), [{ id: 'A', key: 'x1' }], 'D atinge doar colțul, E e pe alt rând');
});

test('o singură anulare reface tot: camera, vecinul și pereții', () => {
  const h = new History<Floor>(), f0 = pair();
  h.push(f0); const f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 5, z1: 3 }, id, N);
  const back = h.undo(f)!;
  assert.deepEqual(back, f0); assert.equal(h.canUndo, false, 'un singur pas în istoric');
});

test('mai multe niveluri: doar camerele nivelului editat se schimbă', () => {
  const ground = pair(), upper = { ...pair(), id: 'up', rooms: pair().rooms.map(r => ({ ...r, id: `${r.id}-sus` })) };
  const house = { name: 'casa', floor: ground, levels: [upper], placements: [], selections: {}, picked: [] } as unknown as Snapshot;
  const view = levelView(house, 1); view.floor = resizeRoom(view.floor, 'A-sus', { x0: 0, z0: 0, x1: 5, z1: 3 }, id, N);
  const merged = mergeLevel(house, 1, view);
  assert.deepEqual(merged.floor, ground, 'parterul nu se schimbă');
  assert.deepEqual(rectOf(merged.levels![0]!, 'B-sus'), { x0: 5, z0: 0, x1: 8, z1: 3 });
});

test('revenirea la dimensiunea veche aduce și vecinul înapoi', () => {
  const f0 = pair(), f = resizeRoom(f0, 'A', { x0: 0, z0: 0, x1: 5.2, z1: 3 }, id, N), back = resizeRoom(f, 'A', rectOf(f0, 'A'), id, N);
  assert.deepEqual(back.rooms, f0.rooms); assert.deepEqual(back.walls, f0.walls);
});

test('toate camerele din toate șabloanele, cu vecini: fără ERROR, camerele rămân lipite, revenirea reface pereții', () => {
  let ok = 0, refused = 0;
  const touching = (f: Floor) => f.rooms.flatMap(a => f.rooms.filter(b => b.id > a.id).map(b => {
    const ox = Math.min(a.rect.x1, b.rect.x1) - Math.max(a.rect.x0, b.rect.x0), oz = Math.min(a.rect.z1, b.rect.z1) - Math.max(a.rect.z0, b.rect.z0);
    return ox > .011 && oz > .011 ? `${a.id}/${b.id}` : null; })).filter(Boolean);
  for (const [name, t] of Object.entries({ oneBedroom, twoBedroom, threeBedroom, studio })){
    const f0 = t as unknown as Floor;
    for (const rm of f0.rooms){
      const r = rm.rect, rects: RoomRect[] = [];
      for (const d of [-1, -.5, -.1, .1, .5, 1]) rects.push({ ...r, x1: r3(r.x1 + d) }, { ...r, z1: r3(r.z1 + d) }, { ...r, x0: r3(r.x0 + d) }, { ...r, z0: r3(r.z0 + d) });
      for (const rect of rects){
        const tag = `${name}/${rm.id} ${JSON.stringify(rect)}`;
        if (rect.x1 - rect.x0 < .6 || rect.z1 - rect.z0 < .6) continue;
        let f: Floor;
        try { f = resizeRoom(f0, rm.id, rect, id, N); } catch (e){ assert.ok(e instanceof RoomResizeError, `${tag}: doar refuz explicit`); refused++; continue; }
        assert.deepEqual(errors(f), [], tag); assert.deepEqual(touching(f), touching(f0), `${tag}: nicio suprapunere nouă`); ok++;
        for (const x of f.rooms) assert.ok(x.id === rm.id || (x.rect.x1 - x.rect.x0 >= Math.min(.6, sizeOf(f0, x.id, 'x')) - 1e-9 && x.rect.z1 - x.rect.z0 >= Math.min(.6, sizeOf(f0, x.id, 'z')) - 1e-9), `${tag}: ${x.id} sub minim`);
        const back = resizeRoom(f, rm.id, r, id, N);
        assert.deepEqual(errors(back), [], `${tag}: revenirea e validă`);
      }
    }
  }
  assert.ok(ok > 4 * refused, `aproape toate redimensionările reușesc (${ok} reușite, ${refused} refuzate)`);
});
const r3 = (v: number) => Math.round(v * 1000) / 1000;
const sizeOf = (f: Floor, rid: string, axis: 'x' | 'z') => { const r = rectOf(f, rid); return axis === 'x' ? r.x1 - r.x0 : r.z1 - r.z0; };
