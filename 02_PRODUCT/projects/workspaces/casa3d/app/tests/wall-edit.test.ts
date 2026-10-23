import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import { setWallLength, WallEditError } from '../core/wall-edit';
import type { Floor, Wall } from '../core/types';

const wall = (id: string, a: [number, number], b: [number, number], openings: Wall['openings'] = []): Wall => ({ id, a, b, thickness: .15, exterior: false, openings });
// camera 4×5 închisă: w1 de sus, w2 dreapta, w3 jos, w4 stânga
const room = (): Floor => ({ id: 'f', name: 'P', ceilingHeight: 2.7, rooms: [], walls: [
  wall('w1', [0, 0], [4, 0]), wall('w2', [4, 0], [4, 5]), wall('w3', [4, 5], [0, 5]), wall('w4', [0, 5], [0, 0])] });
const byId = (f: Floor, id: string) => f.walls.find(w => w.id === id)!;

describe('setWallLength', () => {
  test('capătul b se mută pe direcția peretelui, a rămâne fix', () => {
    const f = setWallLength(room(), 'w1', 4.5);
    assert.deepEqual(byId(f, 'w1').a, [0, 0]); assert.deepEqual(byId(f, 'w1').b, [4.5, 0]);
  });
  test('colțul rămâne închis: peretele lipit de vechiul b îl urmează', () => {
    const f = setWallLength(room(), 'w1', 4.5);
    assert.deepEqual(byId(f, 'w2').a, [4.5, 0]); assert.deepEqual(byId(f, 'w2').b, [4, 5]);
    assert.deepEqual(byId(f, 'w4').b, [0, 0]);   // celălalt colț nu se atinge
  });
  test('un perete care pornește în T din mijlocul peretelui nu se mută', () => {
    const fl = room(); fl.walls.push(wall('t', [2, 0], [2, 3]));
    const f = setWallLength(fl, 'w1', 3);
    assert.deepEqual(byId(f, 't').a, [2, 0]);
  });
  test('peretele oblic: lungimea exactă, rotunjită la mm', () => {
    const fl = room(); fl.walls = [wall('d', [1, 1], [4, 5])];
    const b = byId(setWallLength(fl, 'd', 2.5), 'd').b;
    assert.deepEqual(b, [2.5, 3]);
  });
  test('documentul primit nu se modifică', () => {
    const fl = room(), before = JSON.stringify(fl);
    setWallLength(fl, 'w1', 6); assert.equal(JSON.stringify(fl), before);
  });
  test('refuză sub 20 cm, peste 1 km și un perete inexistent', () => {
    assert.throws(() => setWallLength(room(), 'w1', .1), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.tooShort');
    assert.throws(() => setWallLength(room(), 'w1', 1001), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.tooLong');
    assert.throws(() => setWallLength(room(), 'nu', 3), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.missing');
  });
  test('refuză când o ușă n-ar mai încăpea; o acceptă la limită', () => {
    const fl = room(); byId(fl, 'w1').openings.push({ id: 'u', kind: 'door', offset: 2.5, width: .9 });
    assert.throws(() => setWallLength(fl, 'w1', 3.3), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.openingOut');
    assert.deepEqual(byId(setWallLength(fl, 'w1', 3.4), 'w1').b, [3.4, 0]);
  });
  test('peretele de lungime zero e refuzat, nu scrie NaN în plan', () => {
    const fl = room(); fl.walls.push(wall('z', [4, 0], [4, 0]));
    assert.throws(() => setWallLength(fl, 'z', 3), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.degenerate');
  });
  test('refuză când vecinul coliniar care urmează colțul s-ar strânge sub 20 cm', () => {
    const fl = room(); fl.walls = [wall('a', [0, 0], [4, 0]), wall('b', [4, 0], [8, 0])];
    assert.throws(() => setWallLength(fl, 'a', 8), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.neighbourShort');
    assert.deepEqual(byId(setWallLength(fl, 'a', 7.8), 'b').a, [7.8, 0]);
  });
  test('refuză când fereastra vecinului n-ar mai încăpea pe el', () => {
    const fl = room(); fl.walls = [wall('a', [0, 0], [4, 0]), wall('b', [4, 0], [8, 0], [{ id: 'f', kind: 'window', offset: 2.5, width: 1.2 }])];
    assert.throws(() => setWallLength(fl, 'a', 6), (e: unknown) => e instanceof WallEditError && e.key === 'wallEdit.openingOut');
  });
});
