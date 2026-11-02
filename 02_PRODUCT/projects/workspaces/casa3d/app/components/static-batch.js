// Unește mobilierul înghețat pe material (plan A1, rândul 5): timpul de cadru măsurat e aproape tot pe CPU, în apeluri de desen.
// Ochii originali rămân în scenă, pe alt strat: nu se mai desenează, dar selecția, coliziunile, oglinzile și exporturile îi folosesc
// neschimbați. Rămân pe stratul normal cei care nu se pot uni fără să arate altfel: transparenți (ordinea lor contează),
// oglinzi (materialul li se schimbă), liste de materiale, transformări oglindite (își inversează fețele).
import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

export const HIDDEN_LAYER = 1;
const canMerge = o => o.isMesh && o.visible && o.geometry && o.material && !Array.isArray(o.material) && !o.material.transparent && !(o.material.userData && (o.material.userData.mirror || o.material.userData.mirrorOf))
  && o.matrixWorld.determinant() > 0 && !!o.geometry.attributes.position && !!o.geometry.attributes.normal && !!o.geometry.attributes.uv;
const flat = g => { const out = new THREE.BufferGeometry(); for (const k of ['position', 'normal', 'uv']) out.setAttribute(k, g.attributes[k].clone()); if (g.index) out.setIndex(g.index); else out.setIndex([...Array(g.attributes.position.count).keys()]); return out; };

/** Grupul cu ochii uniți (`userData.noExport`, deci nu ajunge în exporturi). `cell`, în metri, separă piesele depărtate, ca o zonă din afara ecranului să nu se deseneze. */
export function batchStatic(root, { cell = 0 } = {}){
  const groups = new Map(), P = new THREE.Vector3();
  root.traverse(o => { if (!canMerge(o)) return; P.setFromMatrixPosition(o.matrixWorld);
    const c = cell > 0 ? `${Math.floor(P.x / cell)}:${Math.floor(P.z / cell)}` : '', k = `${o.material.uuid}|${o.castShadow ? 1 : 0}${o.receiveShadow ? 1 : 0}|${c}`;
    let l = groups.get(k); if (!l) groups.set(k, l = []); l.push(o); });
  const out = new THREE.Group(); out.name = 'batch'; out.userData = { noExport: true };
  for (const list of groups.values()){ if (list.length < 2) continue;
    const parts = list.map(o => flat(o.geometry).applyMatrix4(o.matrixWorld)), g = mergeGeometries(parts, false); parts.forEach(p => p.dispose()); if (!g) continue;
    g.computeBoundingSphere(); const m = new THREE.Mesh(g, list[0].material); m.castShadow = list[0].castShadow; m.receiveShadow = list[0].receiveShadow; m.matrixAutoUpdate = false; out.add(m);
    for (const o of list) o.layers.set(HIDDEN_LAYER); }
  return out;
}
