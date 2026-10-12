import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { checkSnapshot, MAX_UNDERLAY_BYTES } from '../lib/repo';
import { scaleFromPoints, anchorAfterScale, fitWithin } from '../core/underlay';
import type { Catalog, Snapshot, Underlay } from '../core/types';
const cat = catalogJson as unknown as Catalog;
const PNG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
const u = (o: Partial<Underlay> = {}): Underlay => ({ dataUrl: PNG, x: 0, z: 0, widthM: 10, opacity: .5, locked: false, ...o });
const withU = (o: any): Snapshot => { const s = newSnapshot(cat, 'T', 'demo'); (s as any).underlay = o; return s; };
const status = (fn: () => unknown) => { try { fn(); return 0; } catch (e: any){ return e.status as number; } };

test('serverul acceptă un PNG mic valid și îl păstrează', () => {
  const r = checkSnapshot(withU(u())); assert.equal(r.underlay!.dataUrl, PNG); assert.equal(r.underlay!.widthM, 10);
  assert.doesNotThrow(() => checkSnapshot(withU(u({ dataUrl: 'data:image/jpeg;base64,' + Buffer.from([0xff, 0xd8, 0xff, 0xe0, 0, 16, 0x4a, 0x46, 0x49, 0x46, 0, 1]).toString('base64') }))), 'JPEG cu semnătură JPEG');
  assert.equal(checkSnapshot(newSnapshot(cat, 'fără', 'demo')).underlay, undefined);
});
test('serverul respinge ce nu e imagine, depășirea de mărime și numerele greșite (400)', () => {
  assert.equal(status(() => checkSnapshot(withU(u({ dataUrl: 'data:text/html;base64,PGI+aGk8L2I+' })))), 400);
  assert.equal(status(() => checkSnapshot(withU(u({ dataUrl: 'data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=' })))), 400);
  assert.equal(status(() => checkSnapshot(withU(u({ dataUrl: 'https://exemplu.ro/a.png' })))), 400);
  assert.equal(status(() => checkSnapshot(withU(u({ dataUrl: 'data:image/png;base64,' + Buffer.from('nu sunt un png deloc').toString('base64') })))), 400, 'semnătură greșită');
  const big = 'data:image/png;base64,' + Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47]), Buffer.alloc(MAX_UNDERLAY_BYTES + 10)]).toString('base64');
  assert.equal(status(() => checkSnapshot(withU(u({ dataUrl: big })))), 400, 'peste 1,5 MB');
  const ok = 'data:image/png;base64,' + Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47]), Buffer.alloc(MAX_UNDERLAY_BYTES - 4 - 3)]).toString('base64');
  assert.equal(status(() => checkSnapshot(withU(u({ dataUrl: ok })))), 0, 'aproape de limită trece');
  for (const bad of [{ widthM: 0.5 }, { widthM: 101 }, { widthM: NaN }, { opacity: 0.05 }, { opacity: 1.2 }, { x: Infinity }, { z: 'a' as any }, { locked: 'da' as any }]) assert.equal(status(() => checkSnapshot(withU(u(bad)))), 400, JSON.stringify(bad));
  assert.equal(status(() => checkSnapshot(withU('x'))), 400); assert.equal(status(() => checkSnapshot(withU([]))), 400);
});
test('calibrarea: distanța de pe imagine devine distanța reală', () => {
  // 2 m pe plan la scara curentă (10 m lățime), în realitate 4 m → imaginea trebuie să fie de 20 m
  assert.equal(scaleFromPoints([1, 1], [3, 1], 400, 10), 20);
  assert.equal(scaleFromPoints([0, 0], [3, 4], 250, 10), 5, 'diagonală 5 m → 2,5 m');
  assert.equal(scaleFromPoints([1, 1], [1, 1], 400, 10), null, 'puncte identice'); assert.equal(scaleFromPoints([0, 0], [1, 0], 0, 10), null);
  assert.equal(scaleFromPoints([0, 0], [1, 0], 100000, 10), 100, 'limitat la 100 m'); assert.equal(scaleFromPoints([0, 0], [10, 0], 1, 10), 1, 'limitat la 1 m');
  // punctul de ancorare rămâne pe loc
  const [x, z] = anchorAfterScale({ x: 2, z: 1, widthM: 10 }, [4, 3], 20); assert.deepEqual([x, z], [0, -1]);
  assert.deepEqual(fitWithin(4000, 1000), { w: 2000, h: 500 }); assert.deepEqual(fitWithin(800, 600), { w: 800, h: 600 });
});
