import { test } from 'vitest';
import assert from 'node:assert/strict';
// @ts-ignore — proiectul nu are tipurile three (motorul 3D e în JS)
import * as THREE from 'three';
// @ts-ignore — motor three.js în JS, fără tipuri
import { panoramaGeometry } from '../components/panorama-engine.js';

// Direcția de privire a fiecărei coloane din imaginea randată (viewer3d-engine.js, EquiShader):
// lon = (u − 0,5)·2π, dir = (sin lon, 0, −cos lon) la ecuator → u 0,5 = −z, u 0,75 = +x, u 0,25 = −x.
const rendered = (u: number) => { const lon = (u - .5) * 2 * Math.PI; return new THREE.Vector3(Math.sin(lon), 0, -Math.cos(lon)); };

test('sfera vizualizatorului pune fiecare coloană a panoramei în direcția din care a fost randată', () => {
  const g = panoramaGeometry(), pos = g.getAttribute('position'), uv = g.getAttribute('uv');
  const at = (dir: any) => { let best = -1, bi = 0; const v = new THREE.Vector3();
    for (let i = 0; i < pos.count; i++){ v.fromBufferAttribute(pos, i).normalize(); const d = v.dot(dir); if (d > best){ best = d; bi = i; } }
    return uv.getX(bi); };
  for (const u of [.25, .5, .75]){ const got = at(rendered(u)); assert.ok(Math.abs(got - u) < .02, `direcția coloanei ${u} arată coloana ${got.toFixed(3)}`); }
  g.dispose();
});
