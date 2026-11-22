// Turul virtual 360° (core/pano-tour.ts): punctele de trecere pe uși spre camerele vecine, în convenția panoramei
// (lon 0 = −z, 90 = +x), și proiecția lor pe ecran.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { tourHotspots, lonLat, screenOf } from '../core/pano-tour';
import type { Floor } from '../core/types';

// living (0..4 × 0..3) și hol (4..6 × 0..3), despărțite de peretele x = 4 cu o ușă la mijloc; o fereastră pe peretele de sus
const floor: Pick<Floor, 'rooms' | 'walls'> = {
  rooms: [{ id: 'liv', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 3 } }, { id: 'hol', name: 'Hol', type: 'hol', rect: { x0: 4, z0: 0, x1: 6, z1: 3 } }],
  walls: [{ id: 'w1', a: [4, 0], b: [4, 3], thickness: .1, exterior: false, openings: [{ id: 'd1', kind: 'door', offset: 1.05, width: .9 }] },
    { id: 'w2', a: [0, 0], b: [4, 0], thickness: .25, exterior: true, openings: [{ id: 'f1', kind: 'window', offset: 1, width: 1.2 }] }],
};
test('din living, ușa spre hol e spre +x (lon 90) și puțin sub ochi; ferestrele nu sunt puncte de trecere', () => {
  const h = tourHotspots(floor, 'liv'); assert.equal(h.length, 1);
  assert.equal(h[0]!.toRoomId, 'hol'); assert.equal(h[0]!.openingId, 'd1'); assert.equal(h[0]!.lon, 90); assert.ok(h[0]!.lat < 0 && h[0]!.lat > -20);
  const back = tourHotspots(floor, 'hol'); assert.equal(back.length, 1); assert.equal(back[0]!.toRoomId, 'liv'); assert.equal(back[0]!.lon, -90);
  assert.deepEqual(tourHotspots(floor, 'nu-exista'), []);
});
test('lonLat: sus pe plan (−z) = 0, dreapta (+x) = 90, jos (+z) = 180', () => {
  assert.equal(lonLat(0, 0, 1.5, 0, 1.5, -1).lon, 0); assert.equal(lonLat(0, 0, 1.5, 1, 1.5, 0).lon, 90); assert.equal(Math.abs(lonLat(0, 0, 1.5, 0, 1.5, 1).lon), 180);
  assert.equal(lonLat(0, 0, 1.5, 1, 2.5, 0).lat, 45);
});
test('screenOf: punctul privit e în centrul ecranului; cel din spate nu se vede; cel din dreapta e în dreapta', () => {
  assert.deepEqual(screenOf({ lon: 30, lat: 0 }, 30, 0, 75, 1.6), { x: .5, y: .5 });
  assert.equal(screenOf({ lon: 210, lat: 0 }, 30, 0, 75, 1.6), null);
  const r = screenOf({ lon: 50, lat: 0 }, 30, 0, 75, 1.6)!; assert.ok(r.x > .5 && Math.abs(r.y - .5) < 1e-9);
  const u = screenOf({ lon: 30, lat: 10 }, 30, 0, 75, 1.6)!; assert.ok(u.y < .5, 'mai sus pe ecran');
});
