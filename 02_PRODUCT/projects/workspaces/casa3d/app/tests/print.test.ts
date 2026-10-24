import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import floorV1 from '../data/floor.v1.json';
import catalogV1 from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { wallDimensions, roomSchedule, printScale, floorBounds, LABEL_OFFSET } from '../core/dimensions';
import type { Catalog, Floor } from '../core/types';
const floor = floorV1 as unknown as Floor;
const snap = newSnapshot(catalogV1 as unknown as Catalog, 'Test', 'demo');

describe('cote pe pereți', () => {
  const dims = wallDimensions(floor), b = floorBounds(floor)!;
  test('o cotă pe perete, lungimi egale cu hypot', () => {
    assert.equal(dims.length, floor.walls.length);
    floor.walls.forEach((w, i) => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]);
      assert.ok(Math.abs(dims[i]!.lengthM - L) <= 0.005 + 1e-9); assert.equal(dims[i]!.lengthCm, Math.round(L * 100)); });
  });
  test('normala e unitară și etichetele pereților exteriori cad în afara planului', () => {
    for (const d of dims){ assert.ok(Math.abs(Math.hypot(...d.normal) - 1) < 1e-9);
      assert.ok(Math.abs(Math.hypot(d.label[0] - d.mid[0], d.label[1] - d.mid[1]) - LABEL_OFFSET) < 1e-9);
      if (d.exterior) assert.ok(d.label[0] < b.x0 || d.label[0] > b.x1 || d.label[1] < b.z0 || d.label[1] > b.z1, d.wallId); }
  });
});

describe('tabelul camerelor', () => {
  test('totalul = suma suprafețelor', () => {
    const { rows, totals } = roomSchedule(snap);
    assert.equal(rows.length, snap.floor.rooms.length); assert.equal(totals.rooms, rows.length);
    assert.ok(Math.abs(totals.area - rows.reduce((a, r) => a + r.area, 0)) < 0.011);
    for (const r of rows) assert.ok(Math.abs(r.area - r.width * r.depth) < 0.02);
  });
});

describe('scara de tipărire', () => {
  test('planul demo (cu marginea de cote) încape la 1:50', () => {
    const b = floorBounds(floor)!, s = printScale(b.x1 - b.x0 + 1, b.z1 - b.z0 + 1);
    assert.equal(s.denominator, 50); assert.ok(s.widthMm <= 277 && s.heightMm <= 160);
  });
  test('un plan de 30 m lățime folosește o scară mai grosieră', () => {
    const s = printScale(31, 12); assert.ok(s.denominator > 50); assert.ok(s.widthMm <= 277);
  });
});
