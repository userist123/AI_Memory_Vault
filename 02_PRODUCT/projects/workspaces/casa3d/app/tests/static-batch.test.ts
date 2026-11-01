// Plan A1, rândul 5: mobilierul înghețat se unește pe material ca să scadă apelurile de desen; piesele originale rămân
// pentru selecție, coliziuni și export, dar nu se mai desenează.
import { test } from 'vitest';
import assert from 'node:assert/strict';
// @ts-expect-error three nu are tipuri în acest proiect
import * as THREE from 'three';
import { batchStatic, HIDDEN_LAYER } from '../components/static-batch.js';

const mat = (o = {}) => new THREE.MeshStandardMaterial(o);
const box = (m: unknown, x = 0, z = 0) => { const b = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), m); b.position.set(x, 0, z); b.castShadow = true; b.receiveShadow = true; return b; };
const scene = (...ms: unknown[]) => { const g = new THREE.Group(); g.add(...ms); g.updateMatrixWorld(true); return g; };
const drawn = (g: any) => g.children.filter((o: any) => o.isMesh);

test('piesele cu același material devin un singur ochi, cu geometria la locul ei', () => {
  const m = mat(), a = box(m, 0, 0), b = box(m, 1, 0), g = batchStatic(scene(a, b));
  assert.equal(drawn(g).length, 1);
  const mesh = drawn(g)[0]; assert.equal(mesh.material, m); assert.equal(mesh.geometry.attributes.position.count, 48); assert.equal(mesh.castShadow, true); assert.equal(mesh.receiveShadow, true);
  mesh.geometry.computeBoundingBox(); assert.equal(mesh.geometry.boundingBox.max.x, 1.5); assert.equal(mesh.geometry.boundingBox.min.x, -.5);
});
test('originalele nu se mai desenează, dar rămân vizibile pentru export și selectabile', () => {
  const m = mat(), a = box(m), b = box(m, .5), g = batchStatic(scene(a, b));
  assert.equal(a.visible, true); assert.equal(a.layers.isEnabled(0), false); assert.equal(a.layers.isEnabled(HIDDEN_LAYER), true);
  assert.equal(g.userData.noExport, true);
  const ray = new THREE.Raycaster(new THREE.Vector3(0, 0, 5), new THREE.Vector3(0, 0, -1)); ray.layers.enable(HIDDEN_LAYER); assert.ok(ray.intersectObject(a).length > 0);
});
test('rămân pe loc: un singur ochi pe material, materiale transparente, oglinzi, listă de materiale, oglindire', () => {
  const single = box(mat()), glass = mat({ transparent: true, opacity: .2 }), mirror = mat(); mirror.userData = { mirror: true };
  const multi = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), [mat(), mat()]), shared = mat(), flip = box(shared), other = box(shared, 3); flip.scale.x = -1;
  const list = [single, box(glass), box(glass, 1), box(mirror), box(mirror, 1), multi, flip, other], g = batchStatic(scene(...list));
  assert.equal(drawn(g).length, 0);
  for (const o of list) assert.equal(o.layers.isEnabled(0), true);
});
test('ochii cu umbre diferite nu se amestecă', () => {
  const m = mat(), a = box(m), b = box(m, 1), c = box(m, 2), d = box(m, 3); c.castShadow = false; d.castShadow = false;
  const out = drawn(batchStatic(scene(a, b, c, d))); assert.equal(out.length, 2);
  assert.deepEqual(out.map((x: any) => x.castShadow).sort(), [false, true]);
});
test('celulele separă piesele depărtate, ca o cameră în afara ecranului să nu se mai deseneze', () => {
  const m = mat(), g = batchStatic(scene(box(m, 0), box(m, 1), box(m, 20), box(m, 21)), { cell: 4 });
  assert.equal(drawn(g).length, 2);
});
test('geometriile indexate și neindexate se pot uni', () => {
  const m = mat(), a = box(m), b = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1).toNonIndexed(), m); b.castShadow = b.receiveShadow = true; const g = batchStatic(scene(a, b));
  assert.equal(drawn(g).length, 1); assert.ok(drawn(g)[0].geometry.index);
  assert.equal(drawn(g)[0].geometry.index.count, 72);
});
test('atributele diferite nu blochează: o piesă fără uv rămâne pe loc, restul se unește', () => {
  const m = mat(), a = box(m), b = box(m, 1), c = box(m, 2); c.geometry.deleteAttribute('uv');
  const out = drawn(batchStatic(scene(a, b, c))); assert.equal(out.length, 1); assert.equal(c.layers.isEnabled(0), true); assert.equal(a.layers.isEnabled(0), false);
});
test('geometriile comune nu se modifică', () => {
  const m = mat(), shared = new THREE.BoxGeometry(1, 1, 1), before = [...shared.attributes.position.array], a = new THREE.Mesh(shared, m), b = new THREE.Mesh(shared, m); b.position.x = 5;
  batchStatic(scene(a, b)); assert.deepEqual([...shared.attributes.position.array], before);
});
