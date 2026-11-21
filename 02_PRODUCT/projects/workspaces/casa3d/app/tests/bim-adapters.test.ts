// Faza 7 din ghidul BIM: registrul de adaptoare și testele de compatibilitate (fiecare adaptor rulează pe același proiect,
// e determinist, își declară licența și ce pierde; DXF dus-întors; IFC păstrează id-urile între exporturi).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { ADAPTERS, EXTERNAL_ADAPTERS, adapter, type ExportAdapter, type ImportAdapter } from '../core/adapters';
import { ifcSnap } from './fixtures/bim-snap';
import type { Catalog } from '../core/types';

const cat: Catalog = { suppliers: [], products: [], variants: [], offers: [] };
const O = { lang: 'ro' as const, name: 'Casa', timestamp: '2026-11-21T10:00:00' };

test('registrul: id-uri unice, implementare și licență declarate, ce se păstrează și ce se pierde', () => {
  const all = [...ADAPTERS, ...EXTERNAL_ADAPTERS], ids = all.map(a => a.id);
  assert.equal(new Set(ids).size, ids.length);
  for (const a of all) { assert.ok(a.implementation.length > 5, a.id); assert.ok(a.preserves.length > 0 && a.loses.length > 0, a.id); assert.match(a.extension, /^[a-z0-9.]+$/); }
});
test('compatibilitate: fiecare export rulează pe același proiect, nevid și determinist', () => {
  for (const a of ADAPTERS.filter((x): x is ExportAdapter => x.direction === 'export')) {
    const one = a.run(ifcSnap(), cat, O), two = a.run(structuredClone(ifcSnap()), cat, O);
    assert.ok(one.length > 100, a.id); assert.equal(one, two, `${a.id} nu e determinist`);
  }
});
test('compatibilitate: exportul DXF citit de importul DXF dă pereții planului', () => {
  const dxf = (adapter('dxf-export') as ExportAdapter).run(ifcSnap(), cat, O);
  const walls = (adapter('dxf-import') as ImportAdapter).run(dxf, { layers: ['WALLS'] });
  assert.ok(walls.length >= 2, JSON.stringify(walls.length));
  assert.ok(walls.some(w => w.thickness === .25 || w.thickness === .15), 'grosimea măsurată din perechile de linii');
});
test('compatibilitate: IFC păstrează GlobalId-ul unui element când se adaugă alt element', () => {
  const ifc = adapter('ifc4-export') as ExportAdapter, guidOf = (t: string, tag: string) => new RegExp(`IFC\\w+\\('([^']{22})',\\$,[^\\n]*,'${tag}',`).exec(t)?.[1];
  const before = ifc.run(ifcSnap(), cat, O), s = ifcSnap(); s.floor.columns!.push({ id: 'c9', x: 1, z: 1, w: .3, d: .3, shape: 'rect' });
  const after = ifc.run(s, cat, O);
  for (const tag of ['w1', 'b1', 'm1', 'fd1']) { assert.ok(guidOf(before, tag), tag); assert.equal(guidOf(after, tag), guidOf(before, tag), tag); }
});
