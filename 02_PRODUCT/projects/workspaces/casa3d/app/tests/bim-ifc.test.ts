// Faza 7 din ghidul BIM (§4.2, §5 „IFC poate fi folosit ca format interoperabil”): export IFC4 determinist, cu GlobalId stabil
// derivat din id-ul elementului, fără valori inventate. Validarea cu un instrument independent (IfcOpenShell 0.9) e descrisă
// în docs/bim/FAZA0_INVENTAR.md; aici se verifică structura și contractele.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { snapshotToIfc, ifcGuid, stepString } from '../core/ifc-export';
import type { Catalog } from '../core/types';
import { ifcSnap } from './fixtures/bim-snap';

const cat: Catalog = { suppliers: [], products: [{ id: 'p', group: 'dulap', name: 'Dulap', brand: 'b', category: 'c', model3d: '' }],
  variants: [{ id: 'dulap-0', productId: 'p', name: 'X', legacyIndex: 0, dimensionsCm: { w: 100, d: 60, h: 200 }, dimensionsConfidence: 'HIGH', style: {} }], offers: [] };

const META = { fileName: 'casa.ifc', timestamp: '2026-11-21T10:00:00' };

test('GlobalId: 22 de caractere din alfabetul IFC, stabil și diferit pe elemente', () => {
  const g = ifcGuid('Casa|w1'); assert.match(g, /^[0-3][0-9A-Za-z_$]{21}$/); assert.equal(ifcGuid('Casa|w1'), g); assert.notEqual(ifcGuid('Casa|w2'), g);
});
test('text STEP: apostroful dublat și diacriticele codate', () => {
  assert.equal(stepString("Casa lui Ion's"), "'Casa lui Ion''s'"); assert.equal(stepString('Ș'), "'\\X2\\0218\\X0\\'");
});
test('IFC4: antet, ierarhie spațială, elementele pe tip și relațiile lor', () => {
  const r = snapshotToIfc(ifcSnap(), cat, META), t = r.text;
  assert.match(t, /^ISO-10303-21;\nHEADER;/); assert.match(t, /FILE_SCHEMA\(\('IFC4'\)\);/); assert.match(t, /END-ISO-10303-21;\n$/);
  assert.deepEqual(r.counts, { IFCBUILDINGSTOREY: 2, IFCSPACE: 2, IFCWALL: 2, IFCOPENINGELEMENT: 2, IFCDOOR: 1, IFCWINDOW: 1, IFCCOLUMN: 1, IFCBEAM: 1, IFCFOOTING: 1,
    IFCCOVERING: 1, IFCFURNITURE: 1, IFCPIPESEGMENT: 1, IFCCABLECARRIERSEGMENT: 1 });
  assert.deepEqual(r.skipped, [{ id: 'pl2', reason: 'unknown-size' }]);
  for (const k of ['IFCPROJECT(', 'IFCSITE(', 'IFCBUILDING(', 'IFCRELAGGREGATES(', 'IFCRELCONTAINEDINSPATIALSTRUCTURE(', 'IFCRELVOIDSELEMENT(', 'IFCRELFILLSELEMENT(', 'IFCRELDEFINESBYPROPERTIES(']) assert.ok(t.includes(k), k);
  assert.match(t, /'LoadBearing',\$,IFCBOOLEAN\(\.T\.\)/, 'peretele structural e marcat LoadBearing');
  assert.match(t, /IFCFOOTING\('[^']+',\$,'Fundatie',\$,\$,#\d+,#\d+,'fd1',\.PAD_FOOTING\.\)/);
});
test('IFC4: traseul fără diametru are doar axa și diametrul „UNKNOWN”, nu un volum inventat', () => {
  const t = snapshotToIfc(ifcSnap(), cat, META).text;
  assert.match(t, /IFCSHAPEREPRESENTATION\(#\d+,'Axis','Curve3D'/);
  assert.match(t, /'NominalDiameter',\$,IFCLABEL\('UNKNOWN'\)/);
  assert.match(t, /'NominalDiameterMm',\$,IFCPOSITIVELENGTHMEASURE\(0\.02\)/);
});
test('IFC4: GlobalId unic în fișier, același la un export repetat; referințele #n există', () => {
  const a = snapshotToIfc(ifcSnap(), cat, META).text, b = snapshotToIfc(structuredClone(ifcSnap()), cat, META).text;
  assert.equal(a, b);
  const guids = [...a.matchAll(/^#\d+=IFC\w+\('([0-9A-Za-z_$]{22})'/gm)].map(m => m[1]); assert.ok(guids.length > 20); assert.equal(new Set(guids).size, guids.length);
  const defined = new Set([...a.matchAll(/^#(\d+)=/gm)].map(m => m[1])); for (const m of a.matchAll(/#(\d+)(?!=)/g)) assert.ok(defined.has(m[1]!), `#${m[1]} nedefinit`);
});
test('IFC4: un singur set Casa3D_Element pe element (peretele structural are LoadBearing în același set)', () => {
  const t = snapshotToIfc(ifcSnap(), cat, META).text;
  assert.equal(t.includes('#structural'), false);
  const rels = [...t.matchAll(/IFCRELDEFINESBYPROPERTIES\('[^']+',\$,\$,\$,\((#\d+)\)/g)].map(m => m[1]);
  assert.equal(new Set(rels).size, rels.length, 'fiecare element are o singură relație de proprietăți');
});
