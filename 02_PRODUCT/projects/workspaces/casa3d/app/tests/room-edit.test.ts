// Runda 9: o redimensionare sau o tragere fără efect nu lasă pas de anulare; mânerul laturii nu stă peste o ușă.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { edgeHandleAt, edgeHasFreeSpan, edgeHandlePoint, planRoomResize, sameState } from '../core/room-edit';
import { History } from '../core/history';
import type { Floor, Room, RoomRect, Wall } from '../core/types';

let n = 0; const id = () => `nou-${++n}`;
const room = (rid: string, rect: RoomRect): Room => ({ id: rid, name: rid, type: 'living', rect });
const wall = (wid: string, a: [number, number], b: [number, number], extra: Partial<Wall> = {}): Wall => ({ id: wid, a, b, thickness: .2, exterior: false, openings: [], ...extra });

/** A (x 0..4) și B (x 4..bx1), z 0..3; peretele comun `m` are o ușă (implicit la z 1..1.8). */
const pair = (bx1 = 5, door = { offset: 1, width: .8 }): Floor => ({ id: 'f', name: 'Parter', ceilingHeight: 2.6,
  rooms: [room('A', { x0: 0, z0: 0, x1: 4, z1: 3 }), room('B', { x0: 4, z0: 0, x1: bx1, z1: 3 })],
  walls: [wall('nA', [0, 0], [4, 0], { exterior: true }), wall('nB', [4, 0], [bx1, 0], { exterior: true }), wall('e', [bx1, 0], [bx1, 3], { exterior: true }),
    wall('sB', [bx1, 3], [4, 3], { exterior: true }), wall('sA', [4, 3], [0, 3], { exterior: true }), wall('v', [0, 3], [0, 0], { exterior: true }),
    wall('m', [4, 0], [4, 3], { thickness: .1, openings: [{ id: 'd1', kind: 'door', ...door }] })] });

test('sameState: ordinea cheilor și câmpurile undefined nu contează; valorile da', () => {
  assert.ok(sameState({ x0: 1, x1: 2, z0: 0, z1: 3 }, { z1: 3, z0: 0, x1: 2, x0: 1 }));
  assert.ok(sameState({ a: [1, { b: 2 }], c: undefined }, { a: [1, { b: 2 }] }));
  assert.ok(!sameState({ a: [1, 2] }, { a: [2, 1] }));
  assert.ok(!sameState({ a: 1 }, { a: '1' }));
  assert.ok(!sameState([1], { 0: 1 }));
});

test('panou: vecinul deja la 60 cm → nicio schimbare (fără pas de anulare), dar mesajul de limitare rămâne', () => {
  const f0 = pair(4.6), r = planRoomResize(f0, 'A', { x0: 0, z0: 0, x1: 4.5, z1: 3 }, id);
  assert.equal(r.changed, false, 'clampul lasă totul neschimbat'); assert.equal(r.clamped, true, 'omul află de ce nu s-a mutat');
  assert.ok(sameState(r.floor, f0));
  // simularea Editorului: un pas se înregistrează doar când `changed`
  const h = new History<Floor>(); if (r.changed) h.push(f0); assert.equal(h.canUndo, false);
});

test('panou: o mutare limitată parțial se aplică și se semnalează; una normală nu e „limitată”', () => {
  const part = planRoomResize(pair(5), 'A', { x0: 0, z0: 0, x1: 4.8, z1: 3 }, id);
  assert.equal(part.changed, true); assert.equal(part.clamped, true); assert.deepEqual(part.floor.rooms[0]!.rect, { x0: 0, z0: 0, x1: 4.4, z1: 3 });
  const ok = planRoomResize(pair(6), 'A', { x0: 0, z0: 0, x1: 4.5, z1: 3 }, id);
  assert.equal(ok.changed, true); assert.equal(ok.clamped, false);
  const same = planRoomResize(pair(6), 'A', { x0: 0, z0: 0, x1: 4, z1: 3 }, id);
  assert.equal(same.changed, false); assert.equal(same.clamped, false);
});

test('tragere: o tragere care se termină cu planul neschimbat e o editare fără efect', () => {
  const before = { floor: pair(4.6), placements: [] }, after = structuredClone(before);
  after.floor.rooms[0]!.rect = { z1: 3, x1: 4, z0: 0, x0: 0 };   // refăcut prin resizeRoom, aceleași valori
  assert.ok(sameState(before, after));
  after.floor.rooms[0]!.rect.x1 = 4.05; assert.ok(!sameState(before, after));
});

test('mânerul laturii: pe cea mai lungă porțiune liberă, la o rază de apucare de ușă', () => {
  const f = pair(6), R = .3;   // ușa d1 ocupă z 1..1.8 pe latura x1 a lui A (x = 4); liber: 0..0.7 și 2.1..3
  const z = edgeHandleAt(f, f.rooms[0]!.rect, 'x1', R);
  assert.equal(z, 2.55); assert.ok(z - R >= 1.8 && z + R <= 3 + 1e-9, 'cercul de 44 px nu acoperă ușa');
  assert.equal(edgeHandleAt(f, f.rooms[1]!.rect, 'x0', R), 2.55, 'aceeași linie văzută din camera vecină');
  assert.equal(edgeHandleAt(f, f.rooms[0]!.rect, 'z0', R), 2, 'latura fără goluri: mijlocul');
});

test('mânerul laturii: perete orientat invers și fără loc liber → mijlocul', () => {
  const f = pair(6); const m = f.walls.find(w => w.id === 'm')!; m.a = [4, 3]; m.b = [4, 0];   // offsetul se măsoară acum de jos
  assert.equal(edgeHandleAt(f, f.rooms[0]!.rect, 'x1', .3), .45, 'ușa e la z 1.2..2; liber 0..0.9 e cel mai lung');
  const full = pair(6, { offset: 0, width: 3 });
  assert.equal(edgeHandleAt(full, full.rooms[0]!.rect, 'x1', .3), 1.5, 'ușa ocupă toată latura: rămâne mijlocul');
});

test('mânerul laturii: două uși cu un spațiu mic între ele → mijlocul spațiului, nu peste o ușă', () => {
  const f = pair(6, { offset: .1, width: 1.2 }); f.walls.find(w => w.id === 'm')!.openings.push({ id: 'd2', kind: 'door', offset: 1.6, width: 1.3 });
  // zona de apucare (0.3) nu încape nicăieri: goluri la z 0.1..1.3 și 1.6..2.9 → spațiul dintre ele, 1.3..1.6
  assert.equal(edgeHandleAt(f, f.rooms[0]!.rect, 'x1', .3), 1.45);
});

test('edgeHandlePoint: când o ușă acoperă 100% din latură, mânerul e decalat perpendicular în afara camerei', () => {
  const partial = pair(6, { offset: 1, width: .8 });
  assert.equal(edgeHasFreeSpan(partial, partial.rooms[0]!.rect, 'x1'), true);
  assert.deepEqual(edgeHandlePoint(partial, partial.rooms[0]!.rect, 'x1', .3, .35), { x: 4, z: 2.55, offset: false });

  const full = pair(6, { offset: 0, width: 3 });
  assert.equal(edgeHasFreeSpan(full, full.rooms[0]!.rect, 'x1'), false);
  assert.deepEqual(edgeHandlePoint(full, full.rooms[0]!.rect, 'x1', .3, .35), { x: 4.35, z: 1.5, offset: true });
  assert.deepEqual(edgeHandlePoint(full, full.rooms[1]!.rect, 'x0', .3, .35), { x: 3.65, z: 1.5, offset: true });
});

test('WARNING revert: anularea unei mutări cu avertisment elimină pasul redundant din istoric și păstrează redo', () => {
  const h = new History<number>();
  h.push(1); // stare 1 -> 2
  const afterUndo = h.undo(2);
  assert.equal(afterUndo, 1); assert.equal(h.canRedo, true);
  // o tragere cu WARNING pornește cu begin(1), dar la Revert apelează rollback(mark)
  const mark = h.begin(1);
  assert.equal(h.canUndo, true);
  h.rollback(mark);
  assert.equal(h.canUndo, false, 'niciun pas fantomă rămas în Undo după Revert');
  assert.equal(h.canRedo, true, 'stiva Redo dinaintea tragerii revocate e restaurată');
});

