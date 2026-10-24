// Produse proprii cu prețuri: coloanele noi, import/export prin server, izolare pe proprietar, buget, comenzi, linkul clientului.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { addCustomItem, updateCustomItem, importCustomItems, exportCustomItemsCsv, getCustomItems, listCustomItems } from '../lib/custom-items';
import { getOrders } from '../lib/procurement';
import { createShare } from '../lib/share';
import { progressForShare } from '../lib/client-progress';
import { computeBudget } from '../core/boq';
import { getCatalog, getMaterials } from '../lib/repo';
beforeAll(() => resetDbForTests());
const A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

test('prețuri pe produs: achiziție, termen, dimensiune; comenzile și devizul le văd', async () => {
  const id = await repo.createProject(A, 'Prețuri', 'demo'), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!;
  const it = await addCustomItem(A, id, { name: 'Dulap la comandă', supplier: 'Atelier Exemplu', sku: 'D-1', price: 1000, costPrice: 640.5, qty: 2, leadDays: 45, dimension: '200x60x240 cm', roomId: room.id });
  assert.equal(it.costPrice, 640.5); assert.equal(it.leadDays, 45); assert.equal(it.dimension, '200x60x240 cm');
  const line = (await getOrders(A, id)).rows.find(r => r.key === 'c|' + it.id)!;
  assert.equal(line.total, 2000); assert.equal(line.costTotal, 1281); assert.equal(line.leadDays, 45, 'termenul propriu înlocuiește cel implicit'); assert.equal(line.dimension, '200x60x240 cm');
  const { items, budget } = await getCustomItems(A, id); assert.equal(items.length, 1); assert.equal(budget.sell, 2000); assert.equal(budget.cost, 1281); assert.equal(budget.margin, 719);
  const b = computeBudget((await repo.getProject(A, id)).draft, await getCatalog(), await getMaterials(), items), b0 = computeBudget((await repo.getProject(A, id)).draft, await getCatalog(), await getMaterials());
  assert.equal(b.categories.custom, 2000); assert.equal(b0.categories.custom, undefined); assert.ok(b.scenarios.expected.subtotal - b0.scenarios.expected.subtotal >= 2000 - 0.01);
  await updateCustomItem(A, id, it.id, { costPrice: 600, leadDays: null }); const u = (await listCustomItems(id))[0]!; assert.equal(u.costPrice, 600); assert.equal(u.leadDays, null);
  for (const bad of [{ costPrice: -1 }, { costPrice: '5' }, { leadDays: 1.5 }, { leadDays: 400 }]) await assert.rejects(updateCustomItem(A, id, it.id, bad), (e: any) => e.status === 400, JSON.stringify(bad));
});

test('importul CSV: previzualizare, scriere, camere pe nume, erori fără scriere parțială', async () => {
  const id = await repo.createProject(A, 'Import', 'demo'), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!;
  const csv = `Denumire;Furnizor;Cod;Cantitate;Pret_Achizitie;Pret_Vanzare;Camera;Termen_Zile\nMasă;Atelier;M1;1;1.000,50;1500;${room.name.toUpperCase()};30\nScaun;Atelier;S1;4;100;150;;`;
  const dry = await importCustomItems(A, id, { csv, dryRun: true }) as any; assert.equal(dry.ok, 2); assert.equal((await listCustomItems(id)).length, 0, 'previzualizarea nu scrie');
  assert.equal(dry.rows[0].item.roomId, room.id);
  const bad = `name;supplier;price\nOk;S;1\nRău;S;abc\n;S;1`;
  const pv = await importCustomItems(A, id, { csv: bad, dryRun: true }) as any; assert.equal(pv.failed, 2); assert.deepEqual(pv.rows.map((r: any) => r.line), [2, 3, 4]);
  await assert.rejects(importCustomItems(A, id, { csv: bad }), (e: any) => e.status === 422 && e.details.rows.length === 2); assert.equal((await listCustomItems(id)).length, 0);
  assert.deepEqual(await importCustomItems(A, id, { csv }), { dryRun: false, imported: 2 });
  const got = await listCustomItems(id); assert.equal(got.length, 2); assert.equal(got[0]!.costPrice, 1000.5);
  for (const b of [null, {}, { csv: 5 }, { csv: '' }, { csv: 'name;supplier\n' }]) await assert.rejects(importCustomItems(A, id, b), (e: any) => e.status === 400, JSON.stringify(b));
  await assert.rejects(importCustomItems(A, id, { csv: 'name;supplier\n' + 'x;y\n'.repeat(501) }), (e: any) => e.status === 400 && /500/.test(e.message));
  await assert.rejects(importCustomItems(A, id, { csv: 'name;supplier\nx;' + 'y'.repeat(201 * 1024) }), (e: any) => e.status === 413);
});

test('exportul: doar proprietarul; formulele sunt neutralizate', async () => {
  const id = await repo.createProject(A, 'Export', 'demo');
  await addCustomItem(A, id, { name: '=cmd|\' /C calc\'!A0', supplier: '@x', qty: 1, price: 5, costPrice: 3 });
  const csv = await exportCustomItemsCsv(A, id); const row = csv.trim().split('\r\n')[1]!; assert.ok(row.startsWith("'=cmd"), row); assert.ok(row.includes(";'@x;"));
  for (const o of [B]) { await assert.rejects(exportCustomItemsCsv(o, id), (e: any) => e.status === 404); await assert.rejects(importCustomItems(o, id, { csv: 'name;supplier\na;b' }), (e: any) => e.status === 404);
    await assert.rejects(getCustomItems(o, id), (e: any) => e.status === 404); }
});

test('linkul clientului nu arată prețurile de achiziție', async () => {
  const id = await repo.createProject(A, 'Client', 'demo'), { number } = await repo.createRevision(A, id, 'v1'), { token } = await createShare(A, id, number);
  await addCustomItem(A, id, { name: 'Fotoliu unicat', supplier: 'Atelier', qty: 1, price: 1111, costPrice: 777.77, sku: 'SECRET-SKU-ABC', leadDays: 5 });
  await repo.saveMeta(A, id, { status: 'execution', shareOrders: true });
  const p = (await progressForShare(token))!, json = JSON.stringify(p); assert.ok(p.items.some(i => i.product === 'Fotoliu unicat'));
  for (const s of ['777', 'costPrice', 'costTotal', 'cost', 'SECRET-SKU-ABC', '1111']) assert.ok(!json.includes(s), s);
});
