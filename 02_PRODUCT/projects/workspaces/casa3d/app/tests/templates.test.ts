import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { TEMPLATES, templateSummary, floorArea } from '../core/templates';
import { newSnapshot } from '../core/project';
import { validateFloor } from '../core/validate';
import { formatArea } from '../core/format';
import type { Floor, Room } from '../core/types';

beforeAll(() => resetDbForTests());
const OWNER = '33333333-3333-4333-8333-333333333333';
const RANGES: Record<string, [number, number]> = { studio: [28, 35], 'one-bedroom': [45, 55], 'two-bedroom': [65, 80], 'three-bedroom': [90, 110] };
const NEW_IDS = Object.keys(RANGES);
const EPS = 1e-6;

// Un segment de perete (orizontal sau vertical) pe linia dată; întoarce intervalul [lo,hi] pe axa lui.
function spans(f: Floor, horiz: boolean, line: number): [number, number][] {
  return f.walls.filter(w => (horiz ? Math.abs(w.a[1] - w.b[1]) < EPS && Math.abs(w.a[1] - line) < EPS : Math.abs(w.a[0] - w.b[0]) < EPS && Math.abs(w.a[0] - line) < EPS))
    .map(w => horiz ? [Math.min(w.a[0], w.b[0]), Math.max(w.a[0], w.b[0])] : [Math.min(w.a[1], w.b[1]), Math.max(w.a[1], w.b[1])]);
}
// Laturile unei camere sunt acoperite complet dacă reuniunea pereților coliniari include tot intervalul laturii.
function covered(f: Floor, horiz: boolean, line: number, lo: number, hi: number){
  let at = lo; for (const [a, b] of spans(f, horiz, line).sort((p, q) => p[0] - q[0])) if (a <= at + EPS && b > at) at = b;
  return at >= hi - EPS;
}
const sides = (r: Room) => [[true, r.rect.z0, r.rect.x0, r.rect.x1], [true, r.rect.z1, r.rect.x0, r.rect.x1], [false, r.rect.x0, r.rect.z0, r.rect.z1], [false, r.rect.x1, r.rect.z0, r.rect.z1]] as [boolean, number, number, number][];
// Ușile care ating latura unei camere (poziție absolută pe perete, în interiorul laturii).
function doorsOf(f: Floor, r: Room){
  const out: { entrance: boolean; exterior: boolean }[] = [];
  for (const w of f.walls){ const horiz = Math.abs(w.a[1] - w.b[1]) < EPS, line = horiz ? w.a[1] : w.a[0];
    for (const [h, l, lo, hi] of sides(r)){ if (h !== horiz || Math.abs(l - line) > EPS) continue;
      const a = horiz ? w.a[0] : w.a[1], b = horiz ? w.b[0] : w.b[1], dir = Math.sign(b - a);
      for (const o of w.openings){ if (o.kind !== 'door') continue; const p = a + dir * o.offset, q = a + dir * (o.offset + o.width);
        if (Math.min(p, q) >= lo - EPS && Math.max(p, q) <= hi + EPS) out.push({ entrance: !!o.entrance, exterior: w.exterior }); } } }
  return out;
}

test('registrul conține demo și cele patru șabloane, cu nume și descriere ro/en', () => {
  assert.deepEqual(TEMPLATES.map(t => t.id), ['demo', ...NEW_IDS]);
  for (const t of TEMPLATES){ assert.ok(t.name.ro && t.name.en && t.description.ro && t.description.en); assert.match(t.description.ro, /orientativ/); assert.match(t.description.en, /approximate/); }
  const s = templateSummary(); assert.equal(s.length, 5);
  for (const x of s) assert.equal(x.area, floorArea(TEMPLATES.find(t => t.id === x.id)!.floor));
  assert.match(formatArea(s[1].area, 'metric', 'ro'), /m²/);
});

for (const id of NEW_IDS){
  test(`șablon ${id}: geometrie, uși, suprafață`, () => {
    const f = TEMPLATES.find(t => t.id === id)!.floor;
    assert.deepEqual(validateFloor(f).filter(i => i.severity === 'ERROR'), []);
    assert.ok([2.55, 2.6].includes(f.ceilingHeight));
    assert.equal(new Set(f.rooms.map(r => r.id)).size, f.rooms.length, 'id-uri de cameră unice');
    const area = floorArea(f); assert.ok(area >= RANGES[id][0] && area <= RANGES[id][1], `suprafața ${area} în ${RANGES[id]}`);
    for (const r of f.rooms) for (const [h, l, lo, hi] of sides(r)) assert.ok(covered(f, h, l, lo, hi), `${r.id}: latura ${h ? 'orizontală' : 'verticală'} la ${l} nu e acoperită de pereți`);
    // camerele nu se suprapun
    for (const a of f.rooms) for (const b of f.rooms) if (a.id < b.id) assert.ok(a.rect.x1 <= b.rect.x0 + EPS || b.rect.x1 <= a.rect.x0 + EPS || a.rect.z1 <= b.rect.z0 + EPS || b.rect.z1 <= a.rect.z0 + EPS, `${a.id} se suprapune cu ${b.id}`);
    for (const r of f.rooms) assert.ok(doorsOf(f, r).length >= 1, `${r.id} nu are ușă`);
    const hol = f.rooms.find(r => r.type === 'hol')!, ent = f.walls.flatMap(w => w.openings.filter(o => o.entrance).map(o => ({ w, o })));
    assert.equal(ent.length, 1); assert.ok(ent[0].w.exterior && ent[0].o.kind === 'door');
    assert.ok(doorsOf(f, hol).some(d => d.entrance && d.exterior), 'intrarea e pe un perete exterior al holului');
    for (const r of f.rooms.filter(r => r.type !== 'hol')) assert.ok(doorsOf(f, r).some(d => !d.exterior) , `${r.id} se deschide dintr-o cameră vecină`);
    for (const r of f.rooms.filter(r => ['living', 'dormitor', 'bucatarie'].includes(r.type))){
      const hasWin = sides(r).some(([h, l, lo, hi]) => f.walls.some(w => w.exterior && (Math.abs(w.a[1] - w.b[1]) < EPS) === h && Math.abs((h ? w.a[1] : w.a[0]) - l) < EPS && w.openings.some(o => {
        if (o.kind !== 'window') return false; const a = h ? w.a[0] : w.a[1], dir = Math.sign((h ? w.b[0] : w.b[1]) - a), p = a + dir * o.offset, q = a + dir * (o.offset + o.width); return Math.min(p, q) >= lo - EPS && Math.max(p, q) <= hi + EPS; })));
      assert.ok(hasWin, `${r.id} nu are fereastră pe perete exterior`);
    }
  });
}
test('camerele cerute în fiecare șablon', () => {
  const types = (id: string) => TEMPLATES.find(t => t.id === id)!.floor.rooms.map(r => r.type).sort().join(',');
  assert.equal(types('studio'), 'baie,bucatarie,hol,living');
  assert.equal(types('one-bedroom'), 'baie,bucatarie,dormitor,hol,living');
  assert.equal(types('two-bedroom'), 'baie,bucatarie,dormitor,dormitor,hol,living');
  assert.equal(types('three-bedroom'), 'baie,baie,bucatarie,dormitor,dormitor,dormitor,hol,living');
});

test('newSnapshot + projectErrors: proiect mobilat fără erori pentru fiecare șablon', async () => {
  const cat = await repo.getCatalog();
  for (const t of TEMPLATES){ const snap = newSnapshot(cat, 'Test', t.id); assert.ok(snap.placements.length > 0, `${t.id}: mobilier plasat`);
    assert.deepEqual(repo.projectErrors(snap, cat), [], t.id); }
  assert.equal(newSnapshot(cat, 'Gol', 'blank').placements.length >= 0, true);
  assert.throws(() => newSnapshot(cat, 'X', 'nu-exista'));
});
test('repo.createProject cu fiecare șablon; id necunoscut → 400', async () => {
  for (const t of [...TEMPLATES.map(x => x.id), 'blank']){
    const id = await repo.createProject(OWNER, `Proiect ${t}`, t); const p = await repo.getProject(OWNER, id);
    assert.equal(p.draft.floor.rooms.length, t === 'blank' ? 1 : TEMPLATES.find(x => x.id === t)!.floor.rooms.length);
  }
  for (const bad of ['nu-exista', '', 42, null, '__proto__'])
    await assert.rejects(repo.createProject(OWNER, 'Rău', bad), (e: any) => e.status === 400 && /Șablon necunoscut/.test(e.message));
});
