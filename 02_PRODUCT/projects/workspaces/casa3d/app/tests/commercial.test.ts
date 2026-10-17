// Stratul comercial: discount trade și adaos pe furnizor, profit, grafic de plăți, proformă (doar prețuri de client),
// validare strictă, acces doar pentru proprietar, export ZIP și ștergere în cascadă.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { saveProfile } from '../lib/studio-profile';
import { getCommercial, getCommercialData, saveCommercial, getProforma, getProformaCsv } from '../lib/commercial';
import { exportProject } from '../lib/project-export';
import { moveToTrash, purgeProject } from '../lib/trash';
import { CommercialError, costOf, clientPriceOf, profitReport, paymentSchedule, buildProforma, proformaCsv, checkCommercial, readCommercial, pctOk, pctSum,
  VAT_RATE, COMMERCIAL_LIMITS, PROFORMA_CSV_HEADER, type SourceLine, type Milestone } from '../core/commercial';
import { VAT_RATE as BOQ_VAT } from '../core/rules.boq';
beforeAll(() => resetDbForTests());

const A = 'aaaaaaaa-2222-4222-8222-aaaaaaaaaaaa', B = 'bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb';
const line = (key: string, supplier: string, total: number | null, category: SourceLine['category'] = 'furniture', qty = 1): SourceLine =>
  ({ key, category, label: `Produs ${key}`, supplier, qty, unit: 'buc', unitPrice: total == null ? null : total / qty, total });

test('prețuri: cost = listă × (1 − discount), client = listă × (1 + adaos), rotunjit la 2 zecimale; necunoscut rămâne null', () => {
  assert.equal(costOf(1000, 30), 700); assert.equal(clientPriceOf(1000, 35), 1350);
  assert.equal(costOf(99.99, 33.3), 66.69); assert.equal(clientPriceOf(10.005, 0), 10.01);
  assert.equal(costOf(null, 30), null); assert.equal(clientPriceOf(null, 30), null);
  assert.equal(VAT_RATE, 0.21); assert.equal(VAT_RATE, BOQ_VAT);
});

test('profit pe linie, furnizor, categorie și proiect; onorariul e venit, manopera trece fără adaos; null nu devine 0', () => {
  const d = { defaults: { discountPct: 10, markupPct: 20 }, suppliers: { 'Furnizor A': { discountPct: 30, markupPct: 35 } } };
  const r = profitReport(d, [line('1', 'Furnizor A', 1000), line('2', 'Furnizor A', null), line('3', 'Furnizor B', 500, 'finishes', 2)], [{ key: 'l', label: 'Zugrăvit', qty: 10, unit: 'm²', amount: 400 }], 3000);
  const [l1, l2, l3] = r.lines;
  assert.deepEqual([l1.cost, l1.client, l1.profit, l1.clientUnit], [700, 1350, 650, 1350]);
  assert.deepEqual([l2.list, l2.cost, l2.client, l2.profit, l2.clientUnit], [null, null, null, null, null]);
  assert.deepEqual([l3.discountPct, l3.markupPct, l3.cost, l3.client, l3.profit, l3.clientUnit], [10, 20, 450, 600, 150, 300]);
  assert.deepEqual(r.bySupplier['Furnizor A'], { list: 1000, cost: 700, client: 1350, profit: 650, lines: 2, unknown: 1 });
  assert.deepEqual(r.byCategory.finishes, { list: 500, cost: 450, client: 600, profit: 150, lines: 1, unknown: 0 });
  assert.equal(r.unknownLines, 1); assert.equal(r.labor, 400);
  assert.deepEqual(r.totals, { cost: 1550, client: 5350, profit: 3800, marginPct: 71.03 });
  // grup doar cu necunoscute: totalurile lui rămân null
  const u = profitReport({ defaults: {}, suppliers: {} }, [line('x', 'Nimeni', null)], [], null);
  assert.deepEqual(u.bySupplier.Nimeni, { list: null, cost: null, client: null, profit: null, lines: 1, unknown: 1 });
  assert.equal(u.fee, null); assert.ok(u.feeUnknown); assert.equal(u.totals.marginPct, null);
});

test('graficul de plăți: procentele fac 100%, suma etapelor = contractul, restanțele se văd', () => {
  const ms: Milestone[] = [{ id: 'a', label: 'Avans', pct: 30, due: '2026-01-10', status: 'paid', paidAt: '2026-01-09' },
    { id: 'b', label: 'La livrarea mobilierului', pct: 50, due: '2026-02-01', status: 'pending' }, { id: 'c', label: 'La predare', pct: 20, due: '2026-12-01', status: 'pending' }];
  const s = paymentSchedule(ms, 10000.01, '2026-03-01');
  assert.deepEqual(s.rows.map(r => r.amount), [3000, 5000.01, 2000]);
  assert.equal(s.scheduled, 10000.01); assert.equal(s.paid, 3000); assert.equal(s.outstanding, 7000.01);
  assert.deepEqual(s.rows.map(r => r.overdue), [false, true, false]); assert.equal(s.rows[1].daysLate, 28); assert.equal(s.overdue, 1);
  // rotunjirea: 3 × 33,33% din 100 → ultima ia diferența
  const t = paymentSchedule([{ id: 'x', label: 'x', pct: 33.33, status: 'pending' }, { id: 'y', label: 'y', pct: 33.33, status: 'pending' }, { id: 'z', label: 'z', pct: 33.34, status: 'pending' }], 100, '2026-01-01');
  assert.deepEqual(t.rows.map(r => r.amount), [33.33, 33.33, 33.34]); assert.equal(t.scheduled, 100);
  // fix + procente pe rest
  const f = paymentSchedule([{ id: 'f', label: 'Avans fix', fixed: 2000, status: 'pending' }, { id: 'p', label: 'Rest', pct: 100, status: 'pending' }], 10000, '2026-01-01');
  assert.deepEqual(f.rows.map(r => r.amount), [2000, 8000]);
  assert.equal(pctSum([{ pct: 30 }, { pct: 50 }]), 80); assert.ok(!pctOk([{ pct: 30 }, { pct: 50 }])); assert.ok(pctOk([{ pct: 30 }, { pct: 70 }])); assert.ok(pctOk([]));
  assert.ok(!paymentSchedule([{ id: 'a', label: 'a', pct: 40, status: 'pending' }], 100, '2026-01-01').pctOk);
});

test('validarea: intervale, lungimi, maximum 20 etape și 100 de furnizori, procente = 100%, date', () => {
  const ok = checkCommercial({ defaults: { discountPct: 30, markupPct: 35 }, suppliers: { ' Furnizor A ': { markupPct: 40.555 } },
    milestones: [{ id: 'a', label: ' Avans ', pct: 30, status: 'paid', paidAt: '2026-01-05' }, { id: 'b', label: 'Rest', pct: 70 }], proforma: { series: 'pf', number: 7, milestoneId: 'a' } });
  assert.deepEqual(ok.suppliers, { 'Furnizor A': { markupPct: 40.56 } }); assert.equal(ok.milestones[0].label, 'Avans'); assert.equal(ok.milestones[1].status, 'pending');
  assert.deepEqual(ok.proforma, { series: 'PF', number: 7, milestoneId: 'a' });
  const bad: unknown[] = [null, [], { defaults: { discountPct: 100 } }, { defaults: { discountPct: -1 } }, { defaults: { markupPct: 1001 } }, { defaults: { markupPct: '35' } }, { defaults: { markupPct: NaN } },
    { suppliers: Object.fromEntries(Array.from({ length: COMMERCIAL_LIMITS.suppliers + 1 }, (_, i) => [`F${i}`, { markupPct: 1 }])) },
    { suppliers: { ['x'.repeat(121)]: { markupPct: 1 } } }, { suppliers: { '': { markupPct: 1 } } },
    { milestones: Array.from({ length: 21 }, (_, i) => ({ id: `m${i}`, label: 'x', fixed: 1 })) },
    { milestones: [{ id: 'a', label: 'Avans', pct: 30 }, { id: 'b', label: 'Rest', pct: 60 }] },
    { milestones: [{ id: 'a', label: 'Avans', pct: 30, fixed: 10 }] }, { milestones: [{ id: 'a', label: 'Avans' }] },
    { milestones: [{ id: 'a', label: 'x'.repeat(121), fixed: 1 }] }, { milestones: [{ id: 'a', label: 'x', fixed: 1 }, { id: 'a', label: 'y', fixed: 1 }] },
    { milestones: [{ id: 'a b', label: 'x', fixed: 1 }] }, { milestones: [{ id: 'a', label: 'x', fixed: 1, due: '2026-02-30' }] },
    { milestones: [{ id: 'a', label: 'x', fixed: 1, status: 'paid' }] }, { milestones: [{ id: 'a', label: 'x', fixed: 1, status: 'late' }] },
    { milestones: [{ id: 'a', label: 'x', fixed: 2e9 }] }, { proforma: { series: 'P F' } }, { proforma: { series: 'PF', number: 1.5 } },
    { proforma: { series: 'PF', number: 1, milestoneId: 'nu' } }, { proforma: { series: 'PF', number: 1, issueDate: '2026-05-02', dueDate: '2026-05-01' } }];
  for (const b of bad) assert.throws(() => checkCommercial(b), CommercialError, JSON.stringify(b)?.slice(0, 80));
  assert.deepEqual(readCommercial({ defaults: { discountPct: 500 } }).defaults, {}, 'citirea tolerantă nu aruncă');
});

test('proforma: doar prețuri de client, TVA 21% pe valoarea fără TVA, grupe pe categorie, etapa de plată', () => {
  const d = { defaults: { discountPct: 30, markupPct: 21 }, suppliers: {} };
  const r = profitReport(d, [line('1', 'F', 1000), line('2', 'F', 200, 'finishes', 4), line('3', 'F', null)], [{ key: 'l', label: 'Zugrăvit', qty: 10, unit: 'm²', amount: 121 }], 1000, 210);   // onorariul e net (1000) + TVA 210 (studio plătitor)
  const p = buildProforma(r, [{ key: 'l', label: 'Zugrăvit', qty: 10, unit: 'm²', amount: 121 }], 'Onorariu', null);
  assert.deepEqual(p.groups.map(g => g.category), ['furniture', 'finishes', 'labor', 'fee']);
  const [f] = p.groups[0].lines; assert.deepEqual([f.unitNet, f.net, f.vat, f.gross, f.vatPct], [1000, 1000, 210, 1210, 21]);
  const [fin] = p.groups[1].lines; assert.deepEqual([fin.qty, fin.unitNet, fin.net, fin.vat], [4, 50, 200, 42]);
  assert.equal(p.net, 1000 + 200 + 100 + 1000); assert.equal(p.vat, 210 + 42 + 21 + 210); assert.equal(p.gross, 2783); assert.equal(p.due, p.gross);
  assert.equal(p.unknownLines, 1);
  // nicio cheie de cost/discount/adaos/profit nu ajunge în documentul clientului
  const json = JSON.stringify(p); for (const k of ['cost', 'discount', 'markup', 'profit', '"list"']) assert.ok(!json.includes(k), k);
  const ms = paymentSchedule([{ id: 'a', label: 'Avans', pct: 30, status: 'pending' }, { id: 'b', label: 'Rest', pct: 70, status: 'pending' }], p.gross, '2026-01-01').rows[0];
  const pa = buildProforma(r, [], 'Onorariu', ms); assert.equal(pa.milestone!.label, 'Avans'); assert.equal(pa.due, pa.milestone!.amount);
  assert.ok(Math.abs(pa.milestone!.amount - ms.amount) <= 0.01);
  const csv = proformaCsv(p), rows = csv.replace(/^﻿/, '').trim().split('\r\n');
  assert.equal(rows[0], PROFORMA_CSV_HEADER.join(';')); assert.equal(rows.length, 1 + 4); assert.match(rows[1], /^1;Produs 1;buc;1;1000;21;1000$/);
  const csvA = proformaCsv(buildProforma(r, [], 'Onorariu', ms)).trim().split('\r\n'); assert.equal(csvA.length, 2); assert.match(csvA[1], /^avans-a;Avans \(30%\) conform contract;serv;1;/);
  // formulele nu se execută în Excel
  const evil = profitReport(d, [{ ...line('k', 'F', 10), label: '=HYPERLINK("x")' }], [], null);
  assert.match(proformaCsv(buildProforma(evil, [], 'O', null)), /;"'=HYPERLINK\(""x""\)";/);
});

test('API: doar proprietarul citește și scrie (altul primește 404); salvarea validează; proforma și CSV-ul din proiect', async () => {
  const id = await repo.createProject(A, 'Comercial', 'demo');
  await repo.saveMeta(A, id, { status: 'offer', client: { name: 'Client Exemplu', address: 'Strada Exemplu 1' }, fee: { kind: 'fixed', amount: 5000 } });
  const empty = await getCommercial(A, id); assert.deepEqual(empty.data.milestones, []); assert.ok(empty.report.lines.length > 0); assert.equal(empty.report.fee, 5000);
  assert.ok(empty.suppliers.length > 0);
  const sup = empty.suppliers[0];
  const saved = await saveCommercial(A, id, { defaults: { discountPct: 20, markupPct: 30 }, suppliers: { [sup]: { discountPct: 35 } },
    milestones: [{ id: 'avans', label: 'Avans', pct: 30, due: '2020-01-01' }, { id: 'rest', label: 'La predare', pct: 70 }], proforma: { series: 'PF', number: 12, milestoneId: 'avans' } });
  assert.equal(saved.suppliers[sup].discountPct, 35);
  const c = await getCommercial(A, id, '2026-10-15');
  assert.ok(c.report.lines.filter(l => l.supplier === sup).every(l => l.discountPct === 35 && l.markupPct === 30));
  assert.equal(c.schedule.rows[0].overdue, true); assert.equal(c.schedule.scheduled, c.report.totals.client);
  for (const f of [() => getCommercial(B, id), () => getCommercialData(B, id), () => saveCommercial(B, id, {}), () => getProforma(B, id), () => getProformaCsv(B, id)])
    await assert.rejects(f(), (e: any) => e.status === 404);
  await assert.rejects(saveCommercial(A, id, { milestones: [{ id: 'a', label: 'Avans', pct: 30 }] }), (e: any) => e.status === 400 && /100%/.test(e.message));
  await assert.rejects(saveCommercial(A, 'nu-e-uuid', {}), (e: any) => e.status === 404 || e.status === 400);
  await saveProfile(A, { name: 'Studio Exemplu', cui: '2816464', iban: 'RO49 AAAA 1B31 0075 9384 0000', bank: 'Banca Exemplu' });
  const pf = await getProforma(A, id, '2026-10-15');
  assert.equal(pf.seller.name, 'Studio Exemplu'); assert.equal(pf.client?.name, 'Client Exemplu'); assert.equal(`${pf.series}${pf.number}`, 'PF12');
  assert.equal(pf.dueDate, '2020-01-01'); assert.equal(pf.issueDate, '2026-10-15'); assert.equal(pf.proforma.vatPct, 21);
  assert.ok(pf.proforma.milestone && Math.abs(pf.proforma.milestone.amount - c.schedule.rows[0].amount) <= 0.01);
  assert.ok(Math.abs(pf.proforma.gross - c.report.totals.client) <= 0.05 * pf.proforma.groups.reduce((a, g) => a + g.lines.length, 0), 'proforma ≈ totalul de client (rotunjiri pe linie)');
  const json = JSON.stringify(pf); for (const k of ['"cost"', 'discountPct', 'markupPct', 'profit', 'suppliers']) assert.ok(!json.includes(k), k);
  const csv = await getProformaCsv(A, id); assert.equal(csv.filename, 'proforma-PF-12.csv'); assert.ok(csv.data.startsWith('﻿cod;denumire;UM;'));
});

test('exportul ZIP conține comercial.json; ștergerea definitivă șterge și datele comerciale', async () => {
  const id = await repo.createProject(A, 'Export comercial', 'demo');
  await saveCommercial(A, id, { defaults: { markupPct: 25 }, milestones: [{ id: 'avans', label: 'Avans', pct: 100 }] });
  const { data } = await exportProject(A, id), dir = mkdtempSync(path.join(tmpdir(), 'casa3d-com-')), f = path.join(dir, 'x.zip'); writeFileSync(f, data);
  const pyBin = process.platform === 'win32' ? 'python' : 'python3';
  const out = JSON.parse(execFileSync(pyBin, ['-I', '-c', 'import zipfile,sys;print(zipfile.ZipFile(sys.argv[1]).read("comercial.json").decode("utf-8"))', f], { env: { ...process.env, PYTHONIOENCODING: 'utf-8', PYTHONUTF8: '1' } }).toString('utf8'));
  assert.equal(out.defaults.markupPct, 25); assert.equal(out.milestones[0].label, 'Avans');
  await moveToTrash(A, id); await purgeProject(A, id, 'Export comercial');
  const { q } = await getDb(); assert.equal((await q('select count(*)::int as n from project_commercial where project_id=$1', [id])).rows[0].n, 0);
});
