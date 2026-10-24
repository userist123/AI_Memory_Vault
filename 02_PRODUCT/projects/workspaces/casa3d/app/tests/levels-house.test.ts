// Niveluri pe toată casa: buget, tabel de suprafețe, comparația reviziilor, salvarea, revizia și proiectarea automată.
import { test, beforeAll, vi } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const OWNER = '77777777-7777-4777-8777-777777777777';
vi.mock('@/lib/owner', () => ({ ownerId: async () => OWNER }));
import catalogJson from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { addLevel, levelView, floors } from '../core/levels';
import { computeBOQ } from '../core/boq';
import { roomSchedule } from '../core/dimensions';
import { diffSnapshots } from '../core/diff';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import * as design from '../lib/design';
import { DEFAULT_BRIEF } from '../core/brief';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog, mc = materials as unknown as MaterialsCatalog, A = OWNER;
let n = 0; const id = () => `h${++n}`;
const twoLevels = (): Snapshot => addLevel(newSnapshot(cat, 'Casa', 'demo'), { name: 'Etaj 1', id });
beforeAll(() => resetDbForTests());

test('bugetul și tabelul de suprafețe cuprind toate nivelurile, fiecare cu înălțimea lui', () => {
  const s = twoLevels(); s.levels![0].ceilingHeight = 2.4;
  const g = computeBOQ(s, cat, mc).geometry, up = new Set(s.levels![0].rooms.map(r => r.id));
  assert.equal(g.length, s.floor.rooms.length + s.levels![0].rooms.length);
  assert.ok(g.filter(x => up.has(x.roomId)).every(x => x.height === 2.4)); assert.ok(g.filter(x => !up.has(x.roomId)).every(x => x.height === s.floor.ceilingHeight));
  const one = computeBOQ(levelView(s, 0), cat, mc).geometry.reduce((a, x) => a + x.floorArea, 0), all = g.reduce((a, x) => a + x.floorArea, 0);
  assert.ok(Math.abs(all - 2 * one) < 1e-6, 'etajul copiat are aceeași suprafață');
  const sch = roomSchedule(s); assert.equal(sch.rows.length, g.length); assert.ok(Math.abs(sch.totals.area - all) < .02);
  assert.deepEqual([...new Set(sch.rows.map(r => r.level))], [0, 1]);
  assert.equal(roomSchedule(levelView(s, 1)).rows.length, s.levels![0].rooms.length, 'o vedere = un singur nivel');
});
test('comparația reviziilor vede camerele și pereții de la etaj', () => {
  const a = twoLevels(), b = structuredClone(a); b.levels![0].rooms.pop(); b.levels![0].walls.pop();
  const d = diffSnapshots(a, b, cat); assert.equal(d.rooms.removed.length, 1); assert.equal(d.walls.removed, 1);
  const c = addLevel(a, { name: 'Etaj 2', id }); assert.equal(diffSnapshots(a, c, cat).rooms.added.length, a.levels![0].rooms.length);
});
test('salvarea păstrează etajele, punctele tehnice și culorile lor; refuză id-uri repetate și prea multe niveluri', async () => {
  const pid = await repo.createProject(A, 'Niveluri', 'demo'), s = twoLevels(), r = s.levels![0].rooms[0];
  s.tech = [{ id: 'tp-up', kind: 'outlet', roomId: r.id, x: r.rect.x0, z: (r.rect.z0 + r.rect.z1) / 2, height: .3, reason: '' }];
  s.appearance = { rooms: { [r.id]: { walls: { color: '#334455' } } }, wallFaces: { [`${s.levels![0].walls[0].id}@${r.id}`]: { color: '#aa0000' } } };
  s.floor.stairs = [{ id: 'sc', x: (s.floor.rooms[0].rect.x0 + s.floor.rooms[0].rect.x1) / 2, z: (s.floor.rooms[0].rect.z0 + s.floor.rooms[0].rect.z1) / 2, width: .9, length: 1, rotation: 0 }];
  await repo.saveDraft(A, pid, s); const got = (await repo.getProject(A, pid)).draft;
  assert.equal(floors(got).length, 2); assert.deepEqual(got.tech!.map(t => t.id), ['tp-up']);
  assert.equal(got.appearance!.rooms![r.id]!.walls!.color, '#334455'); assert.equal(Object.keys(got.appearance!.wallFaces!).length, 1);
  assert.equal(got.floor.stairs![0]!.id, 'sc');
  const dup = structuredClone(s); dup.levels![0].rooms[0].id = s.floor.rooms[0].id;
  await assert.rejects(repo.saveDraft(A, pid, dup), (e: any) => e.status === 400);
  const many = structuredClone(s); many.levels = [0, 1, 2, 3].map(() => structuredClone(s.levels![0]));
  await assert.rejects(repo.saveDraft(A, pid, many), (e: any) => e.status === 400);
  const badStair = structuredClone(s); (badStair.floor.stairs![0] as any).width = 'lat';
  await assert.rejects(repo.saveDraft(A, pid, badStair), (e: any) => e.status === 400);
  // golul de la etaj se verifică față de tavanul etajului, nu al parterului
  const tall = structuredClone(s); tall.levels![0].ceilingHeight = 2.2; const w = tall.levels![0].walls.find(x => x.openings.some(o => o.kind === 'door'))!;
  w.openings.find(o => o.kind === 'door')!.height = 2.3;
  await assert.rejects(repo.saveDraft(A, pid, tall), (e: any) => e.status === 400);
  const odd = structuredClone(s); (odd as any).levels = { not: 'an array' };
  await assert.rejects(repo.saveDraft(A, pid, odd), (e: any) => e.status === 400);
});
test('revizia e blocată de o eroare la etaj și de mobilier pus pe scară', async () => {
  const pid = await repo.createProject(A, 'Erori sus', 'demo'), s = twoLevels(), up = s.levels![0].rooms[0];
  s.placements.push({ id: 'retras-sus', roomId: up.id, group: 'x', variantId: 'variantă-retrasă', x: (up.rect.x0 + up.rect.x1) / 2, z: (up.rect.z0 + up.rect.z1) / 2, rotation: 0, source: 'manual' });
  await repo.saveDraft(A, pid, s);
  await assert.rejects(repo.createRevision(A, pid, ''), (e: any) => e.status === 409 && e.details.some((i: any) => i.code === 'UNKNOWN_VARIANT'));
  const t = twoLevels(), p0 = t.placements[0]!;
  t.floor.stairs = [{ id: 'sc', x: p0.x, z: p0.z, width: .9, length: 1, rotation: 0 }];
  const pid2 = await repo.createProject(A, 'Scară', 'demo'); await repo.saveDraft(A, pid2, t);
  await assert.rejects(repo.createRevision(A, pid2, ''), (e: any) => e.status === 409 && e.details.some((i: any) => i.key === 'issue.STAIR_BLOCKED'));
});
test('proiectarea automată a unei camere de la etaj nu atinge parterul, iar cea de la parter păstrează etajul', async () => {
  const pid = await repo.createProject(A, 'Design sus', 'demo'), s = twoLevels(); s.placements.push({ id: 'sus-1', roomId: s.levels![0].rooms.find(r => r.type === 'living')!.id,
    group: s.placements.find(p => p.roomId === 'living')!.group, variantId: s.placements.find(p => p.roomId === 'living')!.variantId, x: 0, z: 0, rotation: 0, source: 'manual' });
  // piesa de sus se mută în mijlocul camerei ei, ca proiectul să nu aibă erori
  const lr = s.levels![0].rooms.find(r => r.type === 'living')!.rect; s.placements.at(-1)!.x = (lr.x0 + lr.x1) / 2; s.placements.at(-1)!.z = (lr.z0 + lr.z1) / 2;
  await repo.saveDraft(A, pid, s);
  const upBed = s.levels![0].rooms.find(r => r.type === 'dormitor')!.id, ground = (await repo.getProject(A, pid)).draft;
  const r = await design.generateDesign(A, pid, { roomId: upBed, wants: ['pat'], replace: true });
  assert.ok(r.alternatives.some(a => a.ok), 'există o variantă bună sus');
  const i = r.alternatives.find(a => a.ok)!.index; await design.decideDesign(A, pid, r.id, i, 'apply', true);
  const after = (await repo.getProject(A, pid)).draft;
  assert.deepEqual(levelView(after, 0).placements, levelView(ground, 0).placements, 'parterul neschimbat');
  assert.ok(after.placements.some(p => p.roomId === upBed), 'patul a ajuns în dormitorul de sus');
  const r2 = await design.generateDesign(A, pid, { roomId: 'dormitor', wants: ['noptiera'] });
  const j = r2.alternatives.find(a => a.ok)!.index; await design.decideDesign(A, pid, r2.id, j, 'apply', true);
  const last = (await repo.getProject(A, pid)).draft;
  assert.deepEqual(levelView(last, 1).placements, levelView(after, 1).placements, 'etajul neschimbat');
  // o schimbare la etaj face propunerea de la parter învechită
  const r3 = await design.generateDesign(A, pid, { roomId: 'dormitor', wants: ['noptiera'] }), moved = structuredClone(last);
  moved.levels![0].rooms[0].name = 'Redenumită'; await repo.saveDraft(A, pid, moved);
  await assert.rejects(design.decideDesign(A, pid, r3.id, r3.alternatives.find(a => a.ok)!.index, 'apply', true), (e: any) => e.status === 409);
});
test('variantele economic/echilibrat/premium mobilează fiecare nivel și nu pierd etajul', async () => {
  const pid = await repo.createProject(A, 'Variante', 'demo'), s = twoLevels(); await repo.saveDraft(A, pid, s);
  const r = await repo.generateProposal(A, pid, DEFAULT_BRIEF), v = r.variants.find(x => x.status !== 'ERROR');
  assert.ok(v, `o variantă fără erori: ${JSON.stringify(r.variants.map(x => x.issues.filter(i => i.severity === 'ERROR').map(i => i.message)).flat().slice(0, 3))}`);
  const res = await repo.applyProposal(A, pid, r.id, v.tier, true), d = (await repo.getProject(A, pid)).draft;
  assert.equal(floors(d).length, 2); assert.equal(res.snapshot.levels!.length, 1);
  const upRooms = new Set(d.levels![0].rooms.map(x => x.id));
  assert.ok(d.placements.some(p => upRooms.has(p.roomId)), 'etajul e mobilat');
  assert.ok(d.placements.some(p => !upRooms.has(p.roomId)), 'parterul e mobilat');
});
