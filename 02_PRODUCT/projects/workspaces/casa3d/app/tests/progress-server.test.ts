// Situațiile de lucrări, pe server (lib/progress.ts): fixarea devizului de bază din oferta curentă, refuzul re-fixării cât
// există situații, CRUD doar pentru proprietar, validarea, ștergerea doar a ultimei situații și ștergerea în cascadă.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { getProgress, getProgressDoc, fixBaseline, saveStatement, deleteStatement, offerLines } from '../lib/progress';
import { getCommercial, saveCommercial } from '../lib/commercial';
import { addCustomItem } from '../lib/custom-items';
import { moveToTrash, purgeProject } from '../lib/trash';
import { computeStatement, PROGRESS_LIMITS } from '../core/progress';

beforeAll(() => resetDbForTests());
const A = '55555555-5555-4555-8555-555555555555', B = '66666666-6666-4666-8666-666666666666';
const status = (n: number) => (e: any) => e.status === n;
const r2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100;

test('fixarea devizului de bază: liniile ofertei curente la prețul către client; liniile fără preț se exclud și se numără; copia nu se mai schimbă', async () => {
  const id = await repo.createProject(A, 'Situații fixare', 'demo');
  assert.equal((await getProgress(A, id)).baseline, null);
  await assert.rejects(() => saveStatement(A, id, { from: '2026-11-01', to: '2026-11-15', qty: {}, extra: [] }), status(409), 'fără deviz de bază nu se pot întocmi situații');
  await addCustomItem(A, id, { name: 'Canapea proprie', supplier: 'Atelier Propriu', price: 1000, qty: 2, unit: 'buc' });
  await addCustomItem(A, id, { name: 'Fără preț', supplier: 'Atelier Propriu', qty: 1, unit: 'buc' });
  await saveCommercial(A, id, { suppliers: { 'Atelier Propriu': { markupPct: 10 } } });
  const c = await getCommercial(A, id), fixed = await fixBaseline(A, id), base = fixed.baseline!;
  assert.ok(base.lines.length > 0); assert.ok(fixed.skipped >= 1, 'linia fără preț e numărată');
  assert.ok(!base.lines.some(l => l.label === 'Fără preț'));
  const sofa = base.lines.find(l => l.label === 'Canapea proprie')!;
  assert.deepEqual({ qty: sofa.qty, unitPrice: sofa.unitPrice, kind: sofa.kind, unit: sofa.unit }, { qty: 2, unitPrice: 1100, kind: 'material', unit: 'buc' }, 'prețul către client (adaos 10%)');
  assert.ok(/^[\w:.@#/-]+$/.test(sofa.key), 'codul liniei e valid pentru core/progress.ts');
  assert.equal(new Set(base.lines.map(l => l.key)).size, base.lines.length);
  // materialele: câte o linie pentru fiecare linie de ofertă cu preț și cantitate
  assert.equal(base.lines.filter(l => l.kind === 'material').length, c.report.lines.filter(l => l.client != null && l.qty > 0).length);
  // manopera: preț unitar = valoare / cantitate; serviciile: cantitate 1
  const lab = c.labor.filter(l => l.qty > 0 && l.amount > 0), bl = base.lines.filter(l => l.kind === 'labor');
  assert.equal(bl.length, lab.length);
  if (lab[0]) assert.equal(bl[0]!.unitPrice, r2(lab[0].amount / lab[0].qty));
  for (const s of base.lines.filter(l => l.kind === 'service')) assert.equal(s.qty, 1);
  assert.equal(base.lines.filter(l => l.kind === 'service').length, c.extras.length);
  assert.ok(!base.lines.some(l => /onorariu/i.test(l.label)), 'onorariul de proiectare nu e lucrare decontată');
  // schimbările ulterioare din proiect nu ating copia
  await saveCommercial(A, id, { suppliers: { 'Atelier Propriu': { markupPct: 50 } } });
  await addCustomItem(A, id, { name: 'Adăugat după', supplier: 'Atelier Propriu', price: 10, qty: 1, unit: 'buc' });
  assert.deepEqual((await getProgress(A, id)).baseline, base);
  // cât nu există situații, re-fixarea înlocuiește copia cu oferta de acum
  const again = (await fixBaseline(A, id)).baseline!;
  assert.equal(again.lines.find(l => l.label === 'Canapea proprie')!.unitPrice, 1500); assert.ok(again.lines.some(l => l.label === 'Adăugat după'));
  assert.equal((await offerLines(A, id)).lines.length, again.lines.length);
});

test('proiect fără nicio linie cu preț: fixarea e refuzată cu 400', async () => {
  const id = await repo.createProject(A, 'Situații gol', 'empty');
  await assert.rejects(() => fixBaseline(A, id), status(400));
  assert.equal((await getProgress(A, id)).baseline, null);
});

test('situații: CRUD doar pentru proprietar, validare 400, număr unic, re-fixare refuzată, ștergere doar a ultimei, cascadă la ștergerea proiectului', async () => {
  const id = await repo.createProject(A, 'Situații CRUD', 'demo');
  await addCustomItem(A, id, { name: 'Parchet', supplier: 'Atelier Propriu', price: 100, qty: 40, unit: 'm²' });
  const base = (await fixBaseline(A, id)).baseline!, k = base.lines.find(l => l.label === 'Parchet')!.key;

  // alt proprietar: 404 pe orice
  for (const f of [() => getProgress(B, id), () => getProgressDoc(B, id), () => fixBaseline(B, id), () => saveStatement(B, id, { from: '2026-11-01', to: '2026-11-15', qty: {}, extra: [] }),
    () => saveStatement(B, id, {}, 'x'), () => deleteStatement(B, id, 'x')]) await assert.rejects(f, status(404));

  // validare
  const ok = { from: '2026-11-01', to: '2026-11-15', qty: { [k]: 10, 'cod-inexistent': 5 }, extra: [], measures: { [k]: [{ label: 'Living', qty: 6 }, { label: 'Hol', qty: 4 }] }, advancePct: 30, retentionPct: 5 };
  for (const badInput of [null, [], 'x', { ...ok, to: '2026-10-01' }, { ...ok, from: '2026-13-01' }, { ...ok, qty: { [k]: -1 } }, { ...ok, vatPct: 150 }, { ...ok, number: 0 }, { ...ok, id: 'cu spațiu' },
    { ...ok, extra: [{ key: 'x1', label: '', unit: 'buc', qty: 1, unitPrice: 1 }] }, { ...ok, extra: [{ key: 'x1', label: 'A', unit: 'buc', qty: 1, unitPrice: 1 }, { key: 'x1', label: 'B', unit: 'buc', qty: 1, unitPrice: 1 }] },
    { ...ok, extra: Array.from({ length: PROGRESS_LIMITS.extra + 1 }, (_, i) => ({ key: `x${i}`, label: 'A', unit: 'buc', qty: 1, unitPrice: 1 })) },
    { ...ok, measures: { [k]: [{ label: '', qty: 1 }] } }])
    await assert.rejects(() => saveStatement(A, id, badInput), status(400), JSON.stringify(badInput)?.slice(0, 80));
  assert.deepEqual((await getProgress(A, id)).statements, [], 'cererile respinse nu salvează nimic');

  // creare: serverul dă id-ul și următorul număr; codurile care nu sunt în deviz se ignoră
  const s1 = (await saveStatement(A, id, ok)).statement;
  assert.equal(s1.number, 1); assert.ok(s1.id); assert.deepEqual(s1.qty, { [k]: 10 }); assert.equal(s1.vatPct, undefined, 'TVA nedeclarat rămâne nedeclarat');
  const s2 = (await saveStatement(A, id, { from: '2026-11-16', to: '2026-11-30', qty: { [k]: 15 }, extra: [{ key: 'co:1', label: 'Deviz suplimentar nr. 1 — Glafuri', unit: 'serv', qty: 1, unitPrice: 500 }], vatPct: 21 })).statement;
  assert.equal(s2.number, 2);
  await assert.rejects(() => saveStatement(A, id, { ...ok, number: 2 }), status(409), 'număr duplicat');
  await assert.rejects(() => saveStatement(A, id, { ...ok, id: s1.id, number: 7 }), status(409), 'identificator duplicat');
  const got = await getProgress(A, id);
  assert.deepEqual(got.statements.map(s => s.number), [1, 2]); assert.deepEqual(got.issues, []);
  const c2 = computeStatement(got.baseline!, got.statements, 2), row = c2.rows.find(r => r.key === k)!;
  assert.deepEqual({ prev: row.prevQty, now: row.thisQty, cum: row.cumQty, rest: row.remainingQty, pct: row.pct }, { prev: 10, now: 15, cum: 25, rest: 15, pct: 62.5 });
  assert.equal(c2.totals.thisValue, 2000); assert.equal(c2.totals.extraThis, 500); // devizul fixat din ofertă are prețuri cu TVA inclus: TVA-ul completat pe situație nu se mai adaugă (fără dublare)
  assert.equal(got.baseline!.vatIncluded, true); assert.equal(c2.totals.vat, null); assert.equal(c2.totals.payable, 2000);

  // editare: numărul rămâne al situației, chiar dacă cererea trimite altul; situație inexistentă 404
  const up = (await saveStatement(A, id, { ...ok, number: 9, qty: { [k]: 12 }, measures: undefined }, s1.id)).statement;
  assert.equal(up.number, 1); assert.deepEqual(up.qty, { [k]: 12 }); assert.equal(up.measures, undefined);
  await assert.rejects(() => saveStatement(A, id, ok, 'nu-exista'), status(404));
  await assert.rejects(() => saveStatement(A, id, { ...ok, to: '2026-10-01' }, s1.id), status(400));
  assert.deepEqual((await getProgress(A, id)).statements[0]!.qty, { [k]: 12 }, 'editarea respinsă nu atinge situația');

  // re-fixarea e refuzată cât există situații, cu mesaj clar; devizul rămâne cel vechi
  await assert.rejects(() => fixBaseline(A, id), (e: any) => e.status === 409 && /situații/.test(e.message) && /Șterge/.test(e.message));
  assert.deepEqual((await getProgress(A, id)).baseline, base);

  // ștergere: doar ultima
  await assert.rejects(() => deleteStatement(A, id, s1.id), status(409));
  await assert.rejects(() => deleteStatement(A, id, 'nu-exista'), status(404));
  assert.deepEqual((await deleteStatement(A, id, s2.id)).statements.map(s => s.number), [1]);
  assert.deepEqual((await deleteStatement(A, id, s1.id)).statements, []);
  assert.ok((await fixBaseline(A, id)).baseline, 'fără situații, re-fixarea e din nou permisă');

  // limita de situații
  const { q } = await getDb();
  await saveStatement(A, id, ok);
  await q(`insert into progress_statements(id, project_id, number, data) select 'f' || g, $1, g, $2::jsonb from generate_series(2, ${PROGRESS_LIMITS.statements}) g`, [id, JSON.stringify({ from: '2026-12-01', to: '2026-12-02', qty: {}, extra: [] })]);
  await assert.rejects(() => saveStatement(A, id, { ...ok, from: '2027-01-01', to: '2027-01-02' }), status(409));

  // ștergerea definitivă a proiectului șterge devizul de bază și situațiile
  await moveToTrash(A, id); await purgeProject(A, id, 'Situații CRUD');
  assert.equal((await q('select count(*)::int as n from progress_baseline where project_id=$1', [id])).rows[0].n, 0);
  assert.equal((await q('select count(*)::int as n from progress_statements where project_id=$1', [id])).rows[0].n, 0);
  await assert.rejects(() => getProgress(A, id), status(404));
});
