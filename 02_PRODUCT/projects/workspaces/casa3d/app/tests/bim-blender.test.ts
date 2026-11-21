// Faza 3 din ghidul BIM (§4.1): identitatea elementelor ajunge în vederea 3D și în scena pentru Blender; pereții DXF trec prin
// validarea serverului cu proveniența lor. Scriptul Blender a fost verificat manual pe Blender 5.2 (docs/bim/FAZA0_INVENTAR.md).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { viewerInput } from '../lib/viewer-input';
import { checkSnapshot } from '../lib/repo';
import { readFileSync } from 'node:fs';
import type { Catalog } from '../core/types';

const cat = catalogJson as unknown as Catalog;
const SHA = 'b'.repeat(64);

test('viewer-input: fiecare perete din planul vederii 3D are id-ul peretelui din model', () => {
  const s = newSnapshot(cat, 'T', 'studio');
  const plan = viewerInput(s, cat).plan as any;
  assert.equal(plan.pereti.length, s.floor.walls.length);
  plan.pereti.forEach((pw: any, i: number) => assert.equal(pw.id, s.floor.walls[i]!.id));
});
test('motorul 3D etichetează cutiile peretelui, stâlpii și grinzile cu id-ul elementului', () => {
  const src = readFileSync(new URL('../components/viewer3d-engine.js', import.meta.url), 'utf8');
  assert.match(src, /if \(wl\.id\) wb\.userData\.eid = wl\.id;/);
  assert.match(src, /userData\.eid = col\.id/);
  assert.match(src, /bMesh\.userData\.eid = bm\.id/);
});
test('scriptul Blender pune obiectele cu id în colecția disciplinei, cu aceleași discipline ca core/elements.ts', async () => {
  const py = readFileSync(new URL('../tools/blender/casa3d_render.py', import.meta.url), 'utf8');
  const { DISCIPLINES } = await import('../core/elements');
  const m = /^DISCIPLINES = \(([^)]*)\)/m.exec(py); assert.ok(m, 'DISCIPLINES lipsește din script');
  assert.deepEqual(m![1]!.split(',').map(x => x.trim().replace(/'/g, '')).filter(Boolean), [...DISCIPLINES]);
  assert.match(py, /collection_for\(ob\.get\('c3d_id'\)\)\.objects\.link\(ob\)/);
});
test('un perete importat din DXF își păstrează proveniența la salvare; una falsificată e respinsă', () => {
  const s = newSnapshot(cat, 'T', 'studio');
  s.floor.walls[0]!.origin = { method: 'dxf', sha256: SHA, layer: 'PERETI', thickness: 'source' };
  assert.deepEqual(checkSnapshot(structuredClone(s)).floor.walls[0]!.origin, { method: 'dxf', sha256: SHA, layer: 'PERETI', thickness: 'source' });
  (s.floor.walls[0] as any).origin = { method: 'dxf', sha256: 'nu', layer: 'x', thickness: 'source' };
  assert.throws(() => checkSnapshot(structuredClone(s)));
});
test('vederile pentru client (prezentare, print, imagini de link) nu primesc traseele și fundațiile; editorul da', () => {
  const s = newSnapshot(cat, 'T', 'studio');
  s.floor.mepRuns = [{ id: 'm1', system: 'water_cold', diameterMm: 20, points: [[1, 1, 1], [2, 1, 1]] }]; s.floor.footings = [{ id: 'fd1', x: 1, z: 1, w: 1, d: 1, depth: .5 }];
  const ed = viewerInput(s, cat).plan as any, client = viewerInput(s, cat, undefined, { services: false }).plan as any;
  assert.equal(ed.trasee.length, 1); assert.equal(ed.fundatii.length, 1);
  assert.equal('trasee' in client, false); assert.equal('fundatii' in client, false);
  for (const f of ['components/PresentView.tsx', 'components/PrintView.tsx', 'lib/share-images.ts'])
    assert.match(readFileSync(new URL(`../${f}`, import.meta.url), 'utf8'), /services: false/, f);
});
