// Undo/redo: invariante pe un document realist, imutabilitate, limită, begin/rollback, discardLast.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { History } from '../core/history';

type Doc = { walls: { id: number; a: [number, number]; b: [number, number] }[]; rooms: { id: number; name: string; rect: number[] }[] };

function mulberry32(seed: number) {
  return () => { seed |= 0; seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const clone = <T,>(x: T): T => JSON.parse(JSON.stringify(x));

function edit(d: Doc, r: () => number, nextId: { v: number }): Doc {
  const n = clone(d); const k = Math.floor(r() * 4); const pt = (): [number, number] => [Math.round(r() * 1000), Math.round(r() * 1000)];
  if (k === 0 || n.walls.length === 0) n.walls.push({ id: nextId.v++, a: pt(), b: pt() });
  else if (k === 1) n.walls.splice(Math.floor(r() * n.walls.length), 1);
  else if (k === 2) { const w = n.walls[Math.floor(r() * n.walls.length)]; w.a = pt(); w.b = pt(); }
  else { if (n.rooms.length === 0 || r() < 0.3) n.rooms.push({ id: nextId.v++, name: 'Camera ' + nextId.v, rect: [0, 0, 300, 400] });
    else n.rooms[Math.floor(r() * n.rooms.length)].name = 'Nume ' + Math.floor(r() * 1e6); }
  return n;
}

describe('History', () => {
  test('N=60 editări aleatoare: undo până la start, redo până la final', () => {
    const r = mulberry32(12345); const ids = { v: 1 }; const N = 60;
    const initial: Doc = { walls: [{ id: 0, a: [0, 0], b: [100, 0] }], rooms: [{ id: 0, name: 'Living', rect: [0, 0, 500, 400] }] };
    const h = new History<Doc>(); let cur = clone(initial); const saved: Doc[] = [];
    for (let i = 0; i < N; i++) { saved.push(clone(cur)); h.push(cur); cur = edit(cur, r, ids); }
    const final = clone(cur);
    const seen: Doc[] = [];
    for (let i = 0; i < N; i++) { const p = h.undo(cur); assert.ok(p); seen.push(clone(p)); cur = p; }
    assert.deepEqual(seen, [...saved].reverse());
    assert.deepEqual(cur, initial); assert.equal(h.canUndo, false); assert.equal(h.canRedo, true);
    const redone: Doc[] = [];
    for (let i = 0; i < N; i++) { const n = h.redo(cur); assert.ok(n); redone.push(clone(n)); cur = n; }
    assert.deepEqual(redone, [...saved.slice(1), final]);
    assert.deepEqual(cur, final); assert.equal(h.canRedo, false); assert.equal(h.canUndo, true);
  });

  test('imutabilitate: instantaneele sunt copii', () => {
    const h = new History<{ v: number[] }>(); const s = { v: [1] };
    h.push(s); s.v.push(2);
    const u = h.undo({ v: [9] })!; assert.deepEqual(u, { v: [1] });
    u.v.push(7);
    const rd = h.redo({ v: [1] })!; assert.deepEqual(rd, { v: [9] });
    rd.v.push(8);
    const u2 = h.undo({ v: [9] })!; assert.deepEqual(u2, { v: [1] });
    const rd2 = h.redo(u2)!; assert.deepEqual(rd2, { v: [9] });
  });

  test('limita 100: al 101-lea push întoarce cel mai vechi', () => {
    const h = new History<number>(100); const out: (string | undefined)[] = [];
    for (let i = 1; i <= 101; i++) out.push(h.push(i));
    assert.ok(out.slice(0, 100).every(x => x === undefined)); assert.equal(out[100], '1');
    let cur = 102; for (let i = 0; i < 100; i++) cur = h.undo(cur)!;
    assert.equal(cur, 2); assert.equal(h.undo(cur), null);
  });

  test('limita 3', () => {
    const h = new History<number>(3);
    assert.equal(h.push(1), undefined); h.push(2); h.push(3); assert.equal(h.push(4), '1');
    let cur = 5; const got: number[] = [];
    for (let i = 0; i < 3; i++) { cur = h.undo(cur)!; got.push(cur); }
    assert.deepEqual(got, [4, 3, 2]); assert.equal(h.undo(cur), null);
  });

  test('un push nou golește redo', () => {
    const h = new History<number>(); h.push(1); h.undo(2); assert.equal(h.canRedo, true);
    h.push(1); assert.equal(h.canRedo, false); assert.equal(h.redo(1), null);
  });

  describe('begin/rollback', () => {
    test('rollback după undo restaurează redo și past', () => {
      const h = new History<number>(); h.push(1); h.push(2); h.push(3);
      assert.equal(h.undo(4), 3); // past [1,2], future [4], cur 3
      const m = h.begin(3); assert.equal(h.canRedo, false);
      assert.equal(h.rollback(m), true);
      assert.equal(h.canRedo, true);
      assert.equal(h.redo(3), 4);
      assert.equal(h.undo(4), 3); assert.equal(h.undo(3), 2); assert.equal(h.undo(2), 1); assert.equal(h.undo(1), null);
    });
    test('rollback refuzat după push/undo/redo/clear', () => {
      const mk = () => { const h = new History<number>(); h.push(1); h.push(2); return h; };
      const check = (touch: (h: History<number>) => void) => {
        const h = mk(); const m = h.begin(3); touch(h);
        const u = [h.canUndo, h.canRedo];
        assert.equal(h.rollback(m), false); assert.deepEqual([h.canUndo, h.canRedo], u);
      };
      check(h => { h.push(9); });
      check(h => { h.undo(50); });
      check(h => { h.undo(50); h.redo(60); });
      check(h => { h.clear(); });
    });
    test('rollback refuzat lasă istoria neschimbată (conținut)', () => {
      const h = new History<number>(); h.push(1); const m = h.begin(2); h.push(3);
      assert.equal(h.rollback(m), false);
      assert.equal(h.undo(4), 3); assert.equal(h.undo(3), 2); assert.equal(h.undo(2), 1); assert.equal(h.undo(1), null);
    });
    test('rollback la limită readuce cel mai vechi', () => {
      const h = new History<number>(3); h.push(1); h.push(2); h.push(3);
      const m = h.begin(4); // aruncă 1
      assert.equal(h.rollback(m), true);
      let cur = 4; const got: number[] = [];
      for (let i = 0; i < 3; i++) { cur = h.undo(cur)!; got.push(cur); }
      assert.deepEqual(got, [3, 2, 1]); assert.equal(h.undo(cur), null);
    });
  });

  test('discardLast scoate ultimul push și invalidează mark-ul', () => {
    const h = new History<number>(); h.push(1); const m = h.begin(2); h.discardLast();
    assert.equal(h.rollback(m), false);
    assert.equal(h.undo(5), 1); assert.equal(h.undo(1), null);
  });

  test('undo/redo pe stive goale întorc null', () => {
    const h = new History<number>();
    assert.equal(h.undo(1), null); assert.equal(h.redo(1), null);
    assert.equal(h.canUndo, false); assert.equal(h.canRedo, false);
  });
});
