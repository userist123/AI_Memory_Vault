// Regresii pentru constatările din recenzia stratului comercial (commit 12fa60d4):
// 1. onorariul are aceeași bază (net) și același TVA în contract și în proformă;
// 2. produsele proprii intră în profit, proformă și baza onorariului procentual;
// 3. furnizori numiți „__proto__” / „constructor” nu poluează Object.prototype;
// 4. orice celulă text din exporturile CSV e protejată împotriva formulelor (inclusiv tab și CR în față);
// 5. datele stocate care nu trec validarea nu sunt înlocuite tăcut cu valori goale.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { getCommercial, getCommercialData, saveCommercial, getProforma } from '../lib/commercial';
import { getContractBundle, getContractData, saveContract } from '../lib/contract';
import { addCustomItem } from '../lib/custom-items';
import { exportProject, csvTable } from '../lib/project-export';
import { profitReport, buildProforma, proformaCsv, checkCommercial, termsFor, type SourceLine } from '../core/commercial';
import { customBudget } from '../core/custom-csv';
beforeAll(() => resetDbForTests());

const A = 'cccccccc-1234-4234-8234-cccccccccccc';
const r2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;
const PY_BIN = process.platform === 'win32' ? 'python' : 'python3';
const unzip = (data: Buffer, name: string) => { const dir = mkdtempSync(path.join(tmpdir(), 'c3d-rev-')), f = path.join(dir, 'x.zip'); writeFileSync(f, data);
  return execFileSync(PY_BIN, ['-I', '-c', 'import zipfile,sys;sys.stdout.buffer.write(zipfile.ZipFile(sys.argv[1]).read(sys.argv[2]))', f, name]).toString('utf8'); };

test('1. onorariul: contractul și proforma dau același net și brut (plătitor și neplătitor de TVA; fix, pe mp, procent)', async () => {
  for (const vatPayer of [true, false]) for (const fee of [{ kind: 'fixed', amount: 10000 }, { kind: 'per_m2', amount: 100 }, { kind: 'percent', amount: 10 }] as const){
    const label = `${fee.kind} ${vatPayer ? 'plătitor' : 'neplătitor'}`;
    const id = await repo.createProject(A, `Onorariu ${label}`, 'demo');
    await repo.saveMeta(A, id, { status: 'offer', fee });
    await saveContract(A, id, { vatPayer });
    const calc = (await getContractBundle(A, id)).calc;
    assert.ok(calc.net != null && calc.net > 0, `${label}: contractul calculează onorariul`);
    assert.equal(calc.gross, vatPayer ? r2(calc.net! * 1.21) : calc.net, label);
    const c = await getCommercial(A, id);
    assert.equal(c.report.fee, calc.net, `${label}: raportul folosește onorariul NET al contractului`);
    assert.equal(c.report.feeVat, calc.vat, `${label}: TVA-ul onorariului din raport`);
    const pf = (await getProforma(A, id)).proforma, feeLine = pf.groups.find(g => g.category === 'fee')?.lines[0];
    assert.ok(feeLine, `${label}: proforma are linia de onorariu`);
    assert.equal(feeLine.net, calc.net, `${label}: net proformă = net contract`);
    assert.equal(feeLine.vat, calc.vat, `${label}: TVA proformă = TVA contract`);
    assert.equal(feeLine.gross, calc.gross, `${label}: brut proformă = brut contract`);
    assert.equal(feeLine.vatPct, vatPayer ? 21 : 0, label);
    assert.equal(pf.feeVatPayer, vatPayer, `${label}: proforma spune dacă studioul e plătitor de TVA`);
    // totalul de client conține onorariul brut; profitul doar onorariul net (TVA-ul nu e venit)
    const goods = c.report.goods, lab = c.report.labor;
    // (transportul și montajul cunoscute din buget sunt în același total, fără adaos: lanțul canonic din core/commercial.ts)
    assert.equal(c.report.totals.client, r2((goods.client ?? 0) + lab + c.report.extras + calc.gross!), label);
    assert.equal(c.report.totals.profit, r2((goods.profit ?? 0) + calc.net!), label);
  }
  // onorariul din contract (data.fee) are prioritate față de fișa proiectului
  const id = await repo.createProject(A, 'Onorariu suprascris', 'demo');
  await repo.saveMeta(A, id, { status: 'offer', fee: { kind: 'fixed', amount: 10000 } });
  await saveContract(A, id, { vatPayer: true, fee: { kind: 'fixed', amount: 7000 } });
  const c = await getCommercial(A, id), pf = (await getProforma(A, id)).proforma, fl = pf.groups.find(g => g.category === 'fee')!.lines[0];
  assert.equal(c.report.fee, 7000); assert.deepEqual([fl.net, fl.vat, fl.gross], [7000, 1470, 8470]);
});

test('1. proforma pură: neplătitorul are TVA 0 pe onorariu, produsele rămân cu TVA inclus în prețul de listă', () => {
  const d = { defaults: {}, suppliers: {} }, items: SourceLine[] = [{ key: 'a', category: 'furniture', label: 'Masă', supplier: 'F', qty: 1, unit: 'buc', unitPrice: 1210, total: 1210 }];
  const p = buildProforma(profitReport(d, items, [], 10000, 0), [], 'Onorariu', null, undefined, false);
  const [goods, fee] = p.groups;
  assert.deepEqual([goods.lines[0].net, goods.lines[0].vat, goods.lines[0].gross], [1000, 210, 1210]);
  assert.deepEqual([fee.lines[0].net, fee.lines[0].vat, fee.lines[0].gross, fee.lines[0].vatPct], [10000, 0, 10000, 0]);
  assert.equal(p.gross, 11210); assert.equal(p.feeVatPayer, false);
  const q = buildProforma(profitReport(d, items, [], 10000, 2100), [], 'Onorariu', null, undefined, true);
  assert.deepEqual([q.groups[1].lines[0].net, q.groups[1].lines[0].vat, q.groups[1].lines[0].gross], [10000, 2100, 12100]);
});

test('2. produsele proprii apar în profit (cu prețul real de achiziție), în proformă și în baza onorariului procentual', async () => {
  const id = await repo.createProject(A, 'Produse proprii', 'demo');
  await repo.saveMeta(A, id, { status: 'offer', fee: { kind: 'percent', amount: 10 } });
  const before = (await getContractBundle(A, id)).budgetBase!;
  const withCost = await addCustomItem(A, id, { name: 'Canapea proprie', supplier: 'Atelier Propriu', price: 1000, costPrice: 600, qty: 2, unit: 'buc' });
  const noCost = await addCustomItem(A, id, { name: 'Fotoliu propriu', supplier: 'Atelier Propriu', price: 500, qty: 1, unit: 'buc' });
  await saveCommercial(A, id, { suppliers: { 'Atelier Propriu': { discountPct: 20, markupPct: 10 } } });
  const c = await getCommercial(A, id);
  const l1 = c.report.lines.find(l => l.key === 'c|' + withCost.id), l2 = c.report.lines.find(l => l.key === 'c|' + noCost.id);
  assert.ok(l1 && l2, 'liniile produselor proprii sunt în raport');
  assert.deepEqual([l1.list, l1.cost, l1.client, l1.profit], [2000, 1200, 2200, 1000], 'costul = prețul de achiziție real × cantitate');
  assert.deepEqual([l2.list, l2.cost, l2.client, l2.profit], [500, 400, 550, 150], 'fără preț de achiziție: costul din discountul furnizorului');
  assert.deepEqual(c.report.bySupplier['Atelier Propriu'], { list: 2500, cost: 1600, client: 2750, profit: 1150, lines: 2, unknown: 0 });
  assert.ok(c.suppliers.includes('Atelier Propriu'));
  const pf = (await getProforma(A, id)).proforma, pl = pf.groups.flatMap(g => g.lines).find(l => l.name === 'Canapea proprie');
  assert.ok(pl, 'produsul propriu e pe proformă'); assert.equal(pl.qty, 2); assert.ok(Math.abs(pl.gross - 2200) <= 0.01);
  const bundle = await getContractBundle(A, id);
  assert.equal(bundle.budgetBase, r2(before + 2500), 'baza onorariului procentual include produsele proprii');
  assert.equal(c.report.fee, r2(bundle.budgetBase! * 10 / 100));
});

test('3. furnizorii „__proto__”, „constructor”, „prototype” nu poluează prototipul și au grupuri corecte', () => {
  const keys = ['list', 'cost', 'client', 'profit', 'lines', 'unknown', 'discountPct', 'markupPct'];
  try {
    const mk = (key: string, supplier: string, total: number): SourceLine => ({ key, category: 'furniture', label: key, supplier, qty: 1, unit: 'buc', unitPrice: total, total });
    const r = profitReport({ defaults: {}, suppliers: {} }, [mk('a', '__proto__', 100), mk('b', 'constructor', 50), mk('c', '__proto__', 10)], [], null);
    for (const k of keys) assert.equal(({} as any)[k], undefined, `Object.prototype.${k}`);
    assert.ok(Object.prototype.hasOwnProperty.call(r.bySupplier, '__proto__'));
    assert.deepEqual({ ...Object.getOwnPropertyDescriptor(r.bySupplier, '__proto__')!.value }, { list: 110, cost: 110, client: 110, profit: 0, lines: 2, unknown: 0 });
    assert.deepEqual({ ...Object.getOwnPropertyDescriptor(r.bySupplier, 'constructor')!.value }, { list: 50, cost: 50, client: 50, profit: 0, lines: 1, unknown: 0 });
    const cb = customBudget([{ supplier: '__proto__', price: 100, qty: 1, costPrice: null }, { supplier: 'constructor', price: 30, qty: 2, costPrice: null }]);
    assert.equal(Object.getOwnPropertyDescriptor(cb.bySupplier, '__proto__')?.value, 100); assert.equal(Object.getOwnPropertyDescriptor(cb.bySupplier, 'constructor')?.value, 60);
    const parsed = checkCommercial(JSON.parse('{"suppliers":{"__proto__":{"discountPct":50},"constructor":{"discountPct":40},"prototype":{"markupPct":5},"F":{"markupPct":7}}}'));
    for (const k of keys) assert.equal(({} as any)[k], undefined, `Object.prototype.${k} după validare`);
    assert.deepEqual(Object.keys(parsed.suppliers), ['F'], 'cheile periculoase sunt ignorate');
    assert.deepEqual(termsFor(parsed, 'constructor'), { discountPct: 0, markupPct: 0 });
    assert.deepEqual(termsFor(parsed, '__proto__'), { discountPct: 0, markupPct: 0 });
  } finally { for (const k of keys) delete (Object.prototype as any)[k]; }
});

test('4. CSV: formulele stocate ies neutralizate în comenzi.csv, deviz.csv și în proformă (inclusiv tab și CR în față)', async () => {
  const id = await repo.createProject(A, 'CSV injectare', 'demo');
  await addCustomItem(A, id, { name: '=HYPERLINK("http://rau.example","x")', supplier: '+Furnizor', sku: '@SUM(A1)', price: 10, qty: 1, unit: 'buc' });
  const { data } = await exportProject(A, id), comenzi = unzip(data, 'comenzi.csv'), deviz = unzip(data, 'deviz.csv');
  for (const f of [comenzi, deviz]){
    assert.ok(f.includes(`"'=HYPERLINK(""http://rau.example"",""x"")"`), 'numele primește apostrof');
    assert.ok(!/(^|;)"?[=+@]/m.test(f), 'nicio celulă nu începe cu o formulă');
  }
  assert.ok(comenzi.includes(";'+Furnizor;"), 'furnizorul e protejat în comenzi.csv');
  const t = csvTable([['\tTAB', '\rCR', '=1+1', '-2', 5, -3, null]]).replace(/^﻿/, '');
  assert.equal(t, `'\tTAB;"'\rCR";'=1+1;'-2;5;-3;\r\n`);
  const d = { defaults: {}, suppliers: {} }, mk = (label: string): SourceLine => ({ key: label.trim() || 'k', category: 'furniture', label, supplier: 'F', qty: 1, unit: '\tbuc', unitPrice: 10, total: 10 });
  const csv = proformaCsv(buildProforma(profitReport(d, [mk('\t=cmd'), mk('\r=cmd'), mk('=HYPERLINK("x")')], [], null), [], 'O', null)).replace(/^﻿/, '');
  const rows = csv.trim().split('\r\n').slice(1);
  assert.ok(rows[0].includes(";'\t=cmd;'\tbuc;"), rows[0]);
  assert.ok(csv.includes(`;"'\r=cmd";`), 'CR în față: apostrof și ghilimele');
  assert.ok(csv.includes(`;"'=HYPERLINK(""x"")";`));
});

test('5. datele stocate invalide nu sunt înlocuite tăcut: 409 și rândul rămâne neatins', async () => {
  const id = await repo.createProject(A, 'Date invalide', 'demo'), { q } = await getDb();
  const badCom = { defaults: { discountPct: 500 }, milestones: [{ id: 'a', label: 'Avans', pct: 100 }] }, badCtr = { revisions: 99, notes: 'păstrează-mă' };
  await q('insert into project_commercial(project_id, data) values($1,$2)', [id, JSON.stringify(badCom)]);
  await q('insert into project_contract(project_id, data) values($1,$2)', [id, JSON.stringify(badCtr)]);
  for (const f of [() => getCommercialData(A, id), () => getCommercial(A, id), () => getProforma(A, id), () => getContractData(A, id), () => getContractBundle(A, id)])
    await assert.rejects(f(), (e: any) => e.status === 409 && typeof e.message === 'string' && e.message.length > 10);
  assert.deepEqual((await q('select data from project_commercial where project_id=$1', [id])).rows[0].data, badCom);
  assert.deepEqual((await q('select data from project_contract where project_id=$1', [id])).rows[0].data, badCtr);
  // exportul (portabilitate) nu cade și păstrează datele brute, marcate
  const { data } = await exportProject(A, id), com = JSON.parse(unzip(data, 'comercial.json')), ctr = JSON.parse(unzip(data, 'contract.json'));
  assert.equal(com.invalid, true); assert.deepEqual(com.raw, badCom); assert.equal(ctr.invalid, true); assert.deepEqual(ctr.raw, badCtr);
});
