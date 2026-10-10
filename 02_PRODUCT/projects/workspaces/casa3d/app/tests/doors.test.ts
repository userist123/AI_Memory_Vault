// Uși de interior din catalog: golurile de ușă, mărimea, mânerul, bugetul, fișa, 3D și salvarea.
import { test } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ } from '../core/boq';
import { doorOpenings, doorIssues, sanitizeDoors } from '../core/doors';
import { finishSchedule } from '../core/finish-schedule';
import { viewerInput } from '../lib/viewer-input';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Uși', 'demo');

test('golurile de ușă, cu camerele de o parte și de alta; intrarea e marcată', () => {
  const d = doorOpenings(demo()); assert.ok(d.length >= 4); assert.ok(d.some(x => x.entrance));
  for (const x of d.filter(x => !x.entrance)) assert.equal(x.roomIds.length, 2, x.id);
});
test('buget și fișă: ușa cu toc și mânerul, pe camera ușii; montajul rămâne necunoscut', () => {
  const s = demo(), d = doorOpenings(s).find(x => !x.entrance)!; s.doors = { [d.id]: { product: 'usa-r80-stejar-gri-86', handle: 'maner-kuchinox-sombra-negru' } };
  const b = computeBOQ(s, cat, mc), door = b.items.find(i => i.key === `door:${d.id}`)!, h = b.items.find(i => i.key === `handle:${d.id}`)!;
  assert.equal(door.total, 789); assert.equal(h.total, 217); assert.equal(door.roomId, d.roomIds[0]); assert.ok(b.unknown.some(u => /Montaj ușă/.test(u)));
  const rows = finishSchedule(s, cat, mc).filter(r => r.element === 'door' || r.element === 'door_handle'); assert.equal(rows.length, 2); assert.equal(rows[0]!.code, '6022716');
});
test('avertismente: ușă de interior pe intrare, mărime diferită de gol, fără mâner', () => {
  const s = demo(), ds = doorOpenings(s), ent = ds.find(x => x.entrance)!, inner = ds.find(x => !x.entrance)!;
  s.doors = { [ent.id]: { product: 'usa-superdoor-f12-alb-88', handle: 'maner-sterk-sm1703-inox' }, [inner.id]: { product: 'usa-megadoor-clasic-alb-77' } };
  const k = doorIssues(s, mc); assert.ok(k.some(i => i.key === 'door.entrance' && i.openingId === ent.id));
  assert.ok(k.some(i => i.key === 'door.noHandle' && i.openingId === inner.id));
  const small = { ...s, doors: { [inner.id]: { product: 'usa-megadoor-clasic-alb-77', handle: 'x' } } };
  const wantSize = Math.abs(.77 - inner.widthM) > .1; assert.equal(doorIssues(small, mc).some(i => i.key === 'door.size'), wantSize);
});
test('3D: stilul foii, culoarea și mânerul ajung pe golul ușii', () => {
  const s = demo(), d = doorOpenings(s).find(x => !x.entrance)!; s.doors = { [d.id]: { product: 'usa-bestimp-g6-gri-88', handle: 'maner-kuchinox-sombra-negru' } };
  const g = viewerInput(s, cat, undefined, { mc }).plan.pereti[d.wallIndex].goluri[d.openingIndex];
  assert.deepEqual(g.usa, { style: 'glass', color: '#8d8b86', wood: false, handle: '#1d1d1d' });
});
test('salvare: golurile inexistente se elimină; forma greșită dă 400', async () => {
  const s = demo(), d = doorOpenings(s)[0]!;
  assert.deepEqual(sanitizeDoors({ [d.id]: { product: 'a' }, 'nu-exista': { product: 'b' } }, s).value, { [d.id]: { product: 'a' } });
  assert.equal(sanitizeDoors([], s).error, 'Ușile sunt invalide.'); assert.equal(sanitizeDoors({ [d.id]: { product: 5 } }, s).error, 'Ușile sunt invalide.');
  resetDbForTests(); const A = '88888888-8888-4888-8888-888888888888', id = await repo.createProject(A, 'U', 'demo');
  s.doors = { [d.id]: { product: 'usa-r80-stejar-gri-86' }, ghost: { product: 'x' } }; await repo.saveDraft(A, id, s);
  assert.deepEqual((await repo.getProject(A, id)).draft.doors, { [d.id]: { product: 'usa-r80-stejar-gri-86' } });
  (s as any).doors = 'rău'; await assert.rejects(repo.saveDraft(A, id, s), (e: any) => e.status === 400);
});
