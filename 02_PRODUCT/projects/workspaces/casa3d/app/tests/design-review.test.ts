// Regresii pentru constatările review-ului independent: aplicare atomică, id-uri unice, piese păstrate,
// limită de mărime, numele din share, prețul necunoscut și lista albă de acțiuni a rutei.
import { test, beforeAll, vi } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
const OWNER = '66666666-6666-4666-8666-666666666666';
vi.mock('@/lib/owner', () => ({ ownerId: async () => OWNER }));
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import * as design from '../lib/design';
import * as share from '../lib/share';
import { furnitureTotal } from '../core/catalog';
import { POST } from '../app/api/projects/[id]/design/[pid]/route';
beforeAll(() => resetDbForTests());
const A = OWNER;
const ids = (ps: { id: string }[]) => ps.map(p => p.id);

test('două aplicări succesive în aceeași cameră nu produc id-uri duplicate', async () => {
  const id = await repo.createProject(A, 'Id-uri', 'demo');
  const r1 = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['noptiera'] });
  await design.decideDesign(A, id, r1.id, 0, 'apply', true);
  const r2 = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['noptiera'] });
  await design.decideDesign(A, id, r2.id, 0, 'apply', true);
  const all = ids((await repo.getProject(A, id)).draft.placements);
  assert.equal(new Set(all).size, all.length, 'fiecare piesă are id propriu');
  assert.ok(!all.some(x => x.startsWith('ai-')), 'id-urile solver-ului nu ajung în proiect');
});

test('dublu-click pe aceeași variantă: o singură revizie, al doilea apel 409', async () => {
  const id = await repo.createProject(A, 'Dublu', 'demo');
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat', 'noptiera'], replace: true });
  const out = await Promise.allSettled([0, 0].map(i => design.decideDesign(A, id, r.id, i, 'apply', true)));
  assert.equal(out.filter(o => o.status === 'fulfilled').length, 1);
  assert.equal((out.find(o => o.status === 'rejected') as PromiseRejectedResult).reason.status, 409);
  assert.equal((await repo.getProject(A, id)).currentRevision, 1);
});

test('două propuneri aplicate concurent: câștigă una, cealaltă primește 409 și proiectul nu e amestecat', async () => {
  const id = await repo.createProject(A, 'Concurent', 'demo');
  const a = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat'], replace: true });
  const b = await design.generateDesign(A, id, { roomId: 'living', wants: ['canapea'], replace: true });
  const out = await Promise.allSettled([design.decideDesign(A, id, a.id, 0, 'apply', true), design.decideDesign(A, id, b.id, 0, 'apply', true)]);
  assert.equal(out.filter(o => o.status === 'fulfilled').length, 1);
  assert.equal((out.find(o => o.status === 'rejected') as PromiseRejectedResult).reason.status, 409);
  const p = await repo.getProject(A, id); assert.equal(p.currentRevision, 1);
  const won = out[0].status === 'fulfilled' ? a : b, lost = won === a ? b : a;
  const statuses = Object.fromEntries((await design.listDesigns(A, id)).map(l => [l.id, l.status]));
  assert.equal(statuses[won.id], 'APPLIED'); assert.equal(statuses[lost.id], 'STALE', 'perdantul nu rămâne revendicat');
});

test('piesele pe care twin-ul nu le poate reprezenta (fără dimensiuni) sunt păstrate la aplicare', async () => {
  const id = await repo.createProject(A, 'Păstrare', 'demo'); const p = await repo.getProject(A, id);
  const k = p.draft.floor.rooms.find(r => r.id === 'bucatarie')!.rect;
  p.draft.placements.push({ id: 'plita-pastrata', roomId: 'bucatarie', group: 'plita', variantId: 'plita-0', x: (k.x0 + k.x1) / 2, z: (k.z0 + k.z1) / 2, rotation: 0, source: 'manual' } as any);
  await repo.saveDraft(A, id, p.draft);
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat'], replace: true });
  await design.decideDesign(A, id, r.id, 0, 'apply', true);
  assert.ok((await repo.getProject(A, id)).draft.placements.some(x => x.id === 'plita-pastrata'));
});

test('o eroare în altă cameră blochează aplicarea înainte de a scrie draftul', async () => {
  const id = await repo.createProject(A, 'Eroare', 'demo'); const p = await repo.getProject(A, id);
  p.draft.placements.push({ id: 'retras', roomId: 'living', group: 'x', variantId: 'variantă-retrasă', x: 1, z: 1, rotation: 0, source: 'manual' } as any);
  await repo.saveDraft(A, id, p.draft); const before = (await repo.getProject(A, id)).draft;
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat'], replace: true });
  await assert.rejects(design.decideDesign(A, id, r.id, 0, 'apply', true), (e: any) => e.status === 409 && e.details.some((i: any) => i.code === 'UNKNOWN_VARIANT'));
  assert.deepEqual((await repo.getProject(A, id)).draft, before, 'draftul rămâne neatins');
  assert.equal((await design.listDesigns(A, id))[0].status, 'PREVIEW', 'propunerea nu rămâne revendicată');
});

test('o cameră uriașă e refuzată cu 422 înainte de solver', async () => {
  const id = await repo.createProject(A, 'Hală', 'demo'), p = await repo.getProject(A, id);
  const room = p.draft.floor.rooms.find(r => r.id === 'living')!; room.rect = { x0: 0, z0: 0, x1: 1000, z1: 1000 };
  assert.throws(() => design.checkTwinBrief({ roomId: 'living', wants: ['canapea'] }, p.draft, ['canapea']), (e: any) => e.status === 422);
});

test('linkul partajat arată numele reviziei, nu pe cel curent al proiectului', async () => {
  const id = await repo.createProject(A, 'Nume vechi', 'demo'); await repo.createRevision(A, id, 'r1');
  const s = await share.createShare(A, id, 1); const p = await repo.getProject(A, id);
  await repo.saveDraft(A, id, { ...p.draft, name: 'Nume nou' });
  assert.equal((await share.resolveShare(s.token)).projectName, 'Nume vechi');
});

test('prețul necunoscut se numără separat, nu ca 0', async () => {
  const cat = await repo.getCatalog(), priced = cat.offers[0]!;
  const t = furnitureTotal(cat, [{ variantId: priced.variantId }, { variantId: 'fără-preț' }]);
  assert.deepEqual(t, { known: priced.price, unknown: 1 });
});

test('ruta de decizie acceptă doar apply/reject și un index întreg', async () => {
  const id = await repo.createProject(A, 'Rută', 'demo');
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat'], replace: true });
  const call = (b: unknown) => POST(new Request('http://x/', { method: 'POST', body: JSON.stringify(b) }), { params: Promise.resolve({ id, pid: r.id }) });
  assert.equal((await call({ action: 'delete', index: 0 })).status, 400);
  assert.equal((await call({ action: 'apply', index: '0' })).status, 400);
  assert.equal((await repo.getProject(A, id)).currentRevision, 0, 'cererile respinse nu ating proiectul');
  const ok = await call({ action: 'reject', index: 2 }); assert.equal(ok.status, 200); assert.deepEqual(await ok.json(), { ok: true });
});
