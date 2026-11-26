import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import { toggle, add, remove, replace, inMarquee, primary, serialize } from '../core/selection-set';

describe('selection-set', () => {
  test('replace dedups and keeps order', () => assert.deepEqual(serialize(replace(['a', 'b', 'a', 'c'])), ['a', 'b', 'c']));
  test('add has no duplicates', () => assert.deepEqual(add(replace(['a', 'b']), ['b', 'c', 'c']).ids, ['a', 'b', 'c']));
  test('toggle twice restores', () => {
    const s = replace(['a', 'b']);
    assert.deepEqual(toggle(toggle(s, 'c'), 'c').ids, ['a', 'b']);
    assert.deepEqual(toggle(toggle(s, 'a'), 'a').ids, ['b', 'a']); // re-added at the end
  });
  test('immutability', () => {
    const s = replace(['a']);
    add(s, ['b']); remove(s, ['a']); toggle(s, 'z');
    assert.deepEqual(s.ids, ['a']);
  });
  test('primary after add/remove', () => {
    assert.equal(primary(replace([])), null);
    const s = add(replace(['a', 'b']), ['c']);
    assert.equal(primary(s), 'c');
    assert.equal(primary(remove(s, ['c'])), 'b');
    assert.equal(primary(remove(s, ['a'])), 'c');
  });
  const items = [
    { id: 'in', x0: 2, y0: 2, x1: 4, y1: 4 },
    { id: 'over', x0: 8, y0: 8, x1: 12, y1: 12 },
    { id: 'out', x0: 20, y0: 20, x1: 30, y1: 30 },
  ];
  test('marquee inside vs touch, any corner order', () => {
    const r = { x0: 10, y0: 10, x1: 0, y1: 0 };
    assert.deepEqual(inMarquee(r, items, 'inside'), ['in']);
    assert.deepEqual(inMarquee(r, items, 'touch'), ['in', 'over']);
    assert.deepEqual(inMarquee({ x0: 0, y0: 0, x1: 10, y1: 10 }, items, 'inside'), ['in']);
  });
  test('marquee with reversed item corners', () => {
    assert.deepEqual(inMarquee({ x0: 0, y0: 0, x1: 5, y1: 5 }, [{ id: 'r', x0: 4, y0: 4, x1: 1, y1: 1 }], 'inside'), ['r']);
  });
});
