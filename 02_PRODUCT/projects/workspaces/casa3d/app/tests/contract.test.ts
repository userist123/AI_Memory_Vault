// Contract și ofertă: calculatorul de onorariu, validarea, izolarea pe proprietar, secțiunile documentelor, exportul.
import { test, beforeAll, describe } from 'vitest';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { computeFee, paymentSchedule, checkContract, readContract, buildContract, buildOffer, hintPosition, FEE_HINTS, PHASE_IDS, CONTRACT_NOTICE, DEFAULT_CONTRACT, type DocContext } from '../core/contract';
import { checkMeta, feeEstimate, FEE_KINDS } from '../core/project-meta';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { getContractData, saveContract, getContractBundle } from '../lib/contract';
import { exportProject } from '../lib/project-export';
import { saveProfile } from '../lib/studio-profile';
beforeAll(() => resetDbForTests());
const A = 'aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa', B = 'bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb';

describe('calculatorul de onorariu', () => {
  test('lei/mp, procent din buget, pachet și pe oră; fără TVA dacă studioul nu e plătitor', () => {
    const f = (fee: any, extra: any = {}) => computeFee({ fee, areaM2: 60, budgetBase: 40000, vatPayer: false, ...extra });
    assert.deepEqual(f({ kind: 'per_m2', amount: 100 }), { kind: 'per_m2', net: 6000, vat: 0, gross: 6000, vatRate: .21, vatPayer: false, base: 60 });
    assert.equal(f({ kind: 'percent', amount: 10 }).net, 4000);
    assert.equal(f({ kind: 'fixed', amount: 7500.5 }).gross, 7500.5);
    assert.equal(f({ kind: 'hourly', amount: 250, hours: 12.5 }).net, 3125);
  });
  test('TVA: se adaugă doar pentru plătitor, cu rotunjire la bani', () => {
    const r = computeFee({ fee: { kind: 'fixed', amount: 1000.55 }, areaM2: 0, vatPayer: true }); assert.equal(r.vat, 210.12); assert.equal(r.gross, 1210.67);
    assert.equal(computeFee({ fee: { kind: 'fixed', amount: 100 }, areaM2: 0, vatPayer: true, vatRate: .19 }).gross, 119);
  });
  test('cazuri care nu se pot calcula nu inventează sume', () => {
    assert.equal(computeFee({ areaM2: 50, vatPayer: false }).reason, 'no_fee');
    assert.equal(computeFee({ fee: { kind: 'hourly', amount: 200 }, areaM2: 50, vatPayer: true }).net, null);
    const p = computeFee({ fee: { kind: 'percent', amount: 8 }, areaM2: 50, budgetBase: 0, vatPayer: true }); assert.equal(p.net, null); assert.equal(p.reason, 'no_budget'); assert.equal(p.gross, null);
  });
  test('planul de plată: suma e exactă, ultima tranșă preia rotunjirea', () => {
    for (const [tot, dep, n] of [[10000, 30, 2], [1000.01, 33, 3], [999.99, 0, 0], [5000, 100, 4], [12345.67, 25.5, 5]] as const){
      const rows = paymentSchedule(tot, dep, n), sum = Math.round(rows.reduce((a, r) => a + r.amount, 0) * 100) / 100, pct = Math.round(rows.reduce((a, r) => a + r.pct, 0) * 100) / 100;
      assert.equal(sum, tot, JSON.stringify([tot, dep, n])); assert.equal(pct, 100); }
    assert.deepEqual(paymentSchedule(10000, 30, 2).map(r => [r.key, r.amount]), [['deposit', 3000], ['installment', 3500], ['final', 3500]]);
    assert.deepEqual(paymentSchedule(8000, 0, 0).map(r => r.key), ['final']); assert.deepEqual(paymentSchedule(null, 30, 2), []);
  });
  test('intervalele orientative sunt doar indicații', () => {
    assert.equal(FEE_HINTS.per_m2.unit, 'lei/mp'); assert.equal(hintPosition({ kind: 'per_m2', amount: 20 }), 'below'); assert.equal(hintPosition({ kind: 'percent', amount: 10 }), 'within');
    assert.equal(hintPosition({ kind: 'hourly', amount: 900 }), 'above'); assert.equal(hintPosition(undefined), null);
  });
});

describe('fișa proiectului: compatibilitate', () => {
  test('tipurile vechi rămân valide, percent e nou și limitat la 100', () => {
    for (const kind of ['fixed', 'per_m2', 'hourly']) assert.equal(checkMeta({ fee: { kind, amount: 10 } }).fee?.kind, kind);
    assert.ok((FEE_KINDS as readonly string[]).includes('percent')); assert.equal(checkMeta({ fee: { kind: 'percent', amount: 12.5 } }).fee?.amount, 12.5);
    assert.throws(() => checkMeta({ fee: { kind: 'percent', amount: 101 } })); assert.throws(() => checkMeta({ fee: { kind: 'lunar', amount: 1 } }));
    assert.equal(feeEstimate(checkMeta({ fee: { kind: 'percent', amount: 10 } }), 50, 30000), 3000); assert.equal(feeEstimate(checkMeta({ fee: { kind: 'percent', amount: 10 } }), 50), null);
    assert.equal(feeEstimate(checkMeta({ fee: { kind: 'per_m2', amount: 50 } }), 61.6), 3080);
  });
});

describe('validarea datelor contractului', () => {
  test('valori bune curățate; implicit pentru câmpuri lipsă; etapele în ordine canonică', () => {
    const d = checkContract({ fee: { kind: 'hourly', amount: 200.555, hours: 10 }, phases: ['drawings', 'survey', 'survey'], revisions: 3, depositPct: 40, installments: 2, contractNo: '  C-1‮ ', validUntil: '2026-12-01', vatPayer: true });
    assert.deepEqual(d.phases, ['survey', 'drawings']); assert.equal(d.fee?.amount, 200.56); assert.equal(d.fee?.hours, 10); assert.equal(d.contractNo, 'C-1'); assert.equal(d.vatPayer, true);
    assert.deepEqual(checkContract({}), DEFAULT_CONTRACT); assert.equal(checkContract({ fee: { kind: 'fixed', amount: 5, hours: 9 } }).fee?.hours, undefined);
  });
  test('valorile greșite sunt refuzate', () => {
    const bad: any[] = [null, [], 'x', { revisions: -1 }, { revisions: 21 }, { revisions: 1.5 }, { depositPct: 101 }, { depositPct: -1 }, { depositPct: 'x' }, { installments: 13 }, { phases: [] }, { phases: ['nu-exista'] }, { phases: 'survey' },
      { fee: { kind: 'lunar', amount: 5 } }, { fee: { kind: 'fixed', amount: -1 } }, { fee: { kind: 'fixed', amount: 1e9 } }, { fee: { kind: 'percent', amount: 150 } }, { fee: { kind: 'hourly', amount: 5, hours: 1e6 } },
      { validUntil: '2026-02-30' }, { contractDate: 'ieri' }, { vatPayer: 'da' }, { contractNo: 'x'.repeat(200) }, { notes: 'x'.repeat(5000) }, { deliverables: 5 }];
    for (const b of bad) assert.throws(() => checkContract(b), JSON.stringify(b)?.slice(0, 60));
    assert.deepEqual(readContract({ revisions: 99 }), DEFAULT_CONTRACT, 'citirea nu aruncă');
  });
});

describe('persistență și izolare pe proprietar', () => {
  test('salvare, citire, 404 pentru alt proprietar, 400 pentru date greșite', async () => {
    const id = await repo.createProject(A, 'Apartament Exemplu', 'demo');
    assert.deepEqual(await getContractData(A, id), DEFAULT_CONTRACT);
    const s = await saveContract(A, id, { phases: ['survey', 'concept'], depositPct: 50, fee: { kind: 'fixed', amount: 6000 } }); assert.equal(s.depositPct, 50);
    assert.equal((await getContractData(A, id)).fee?.amount, 6000);
    await assert.rejects(getContractData(B, id), (e: any) => e.status === 404); await assert.rejects(saveContract(B, id, {}), (e: any) => e.status === 404); await assert.rejects(getContractBundle(B, id), (e: any) => e.status === 404);
    await assert.rejects(saveContract(A, id, { phases: ['x'] }), (e: any) => e.status === 400);
    assert.equal((await getContractData(A, id)).depositPct, 50, 'salvarea invalidă nu schimbă nimic');
  });
  test('pachetul pentru pagini: calcul din proiect (mp și buget), ambele documente, ștergerea proiectului îl șterge', async () => {
    const id = await repo.createProject(A, 'Casa Exemplu', 'demo');
    await repo.saveMeta(A, id, { status: 'offer', client: { name: 'Client Exemplu', email: 'client@exemplu.ro' }, siteAddress: 'Str. Exemplu 1, Oraș', startDate: '2026-11-01', dueDate: '2026-12-15', fee: { kind: 'per_m2', amount: 100 } });
    await saveProfile(A, { name: 'Studio Exemplu SRL', email: 'studio@exemplu.ro', address: 'Str. Studio 2' });
    const b = await getContractBundle(A, id, '2026-10-15'); assert.ok(b.areaM2 > 0);
    assert.equal(b.calc.net, Math.round(b.areaM2 * 10000) / 100); assert.equal(b.effectiveFee?.kind, 'per_m2');
    assert.ok(b.budgetBase && b.budgetBase > 0); assert.equal(b.schedule.at(-1)!.key, 'final');
    const text = JSON.stringify(b.contract); assert.match(text, /Client Exemplu/); assert.match(text, /Str\. Exemplu 1/); assert.match(text, /01\.11\.2026/); assert.match(text, /15\.12\.2026/); assert.match(text, /Studio Exemplu SRL/);
    const pct = await saveContract(A, id, { fee: { kind: 'percent', amount: 10 }, vatPayer: true }); assert.equal(pct.vatPayer, true);
    const b2 = await getContractBundle(A, id, '2026-10-15'); assert.equal(b2.calc.net, Math.round(b2.budgetBase! * 10) / 100); assert.ok(b2.calc.vat > 0);
  });
  test('contract.json intră în arhiva de export, doar pentru proprietar', async () => {
    const id = await repo.createProject(A, 'Export Exemplu', 'demo'); await saveContract(A, id, { revisions: 4 });
    const { data } = await exportProject(A, id), dir = mkdtempSync(path.join(os.tmpdir(), 'c3d-ctr-')), f = path.join(dir, 'e.zip'); writeFileSync(f, data);
    const pyBin = process.platform === 'win32' ? 'python' : 'python3';
    const out = execFileSync(pyBin, ['-c', 'import zipfile,sys;z=zipfile.ZipFile(sys.argv[1]);print(z.read("contract.json").decode("utf-8"))', f], { env: { ...process.env, PYTHONIOENCODING: 'utf-8', PYTHONUTF8: '1' } }).toString('utf8');
    assert.equal(JSON.parse(out).revisions, 4); await assert.rejects(exportProject(B, id), (e: any) => e.status === 404);
  });
});

describe('textul documentelor', () => {
  const ctx = (over: Partial<DocContext> = {}): DocContext => ({ studio: { name: 'Studio Exemplu SRL', cui: 'RO1234567', address: 'Str. Studio 2', email: 's@exemplu.ro', iban: 'RO49 AAAA 1B31 0075 9384 0000', bank: 'Banca Exemplu' }, client: { name: 'Client Exemplu', address: 'Str. Client 3' }, projectName: 'Apartament Exemplu',
    rooms: [{ name: 'Living', areaM2: 21.3 }, { name: 'Dormitor', areaM2: 12 }], areaM2: 33.3, meta: { startDate: '2026-11-01', dueDate: '2026-12-15' }, data: { ...DEFAULT_CONTRACT, fee: { kind: 'per_m2', amount: 120 }, contractNo: '7/2026' }, budgetBase: 20000, today: '2026-10-15', ...over });
  test('contractul are toate secțiunile, avertismentul și semnăturile', () => {
    const d = buildContract(ctx()), ids = d.sections.map(s => s.id);
    for (const id of ['parties', 'scope', 'phases', 'deliverables', 'timeline', 'fee', 'payment', 'revisions', 'ip', 'gdpr', 'termination', 'signatures']) assert.ok(ids.includes(id), id);
    assert.equal(d.notice, 'Model orientativ, nu constituie consultanță juridică; verificați-l cu un jurist.'); assert.equal(CONTRACT_NOTICE, d.notice);
    const all = JSON.stringify(d); assert.match(all, /ANSPDCP/); assert.match(all, /2 runde de modificări/); assert.match(all, /Living/); assert.match(all, /nr\. 7\/2026/); assert.match(d.signatures.client, /Client Exemplu/);
    assert.equal(d.sections.find(s => s.id === 'phases')!.items!.length, DEFAULT_CONTRACT.phases.length);
    assert.equal(d.sections.find(s => s.id === 'payment')!.table!.rows.length, 3);
  });
  test('etapele alese schimbă textul; TVA și fără onorariu; câmpuri lipsă devin spații de completat', () => {
    const all = buildContract(ctx({ data: { ...DEFAULT_CONTRACT, phases: [...PHASE_IDS], vatPayer: true, fee: { kind: 'fixed', amount: 5000 }, revisions: 0 } }));
    assert.equal(all.sections.find(s => s.id === 'phases')!.items!.length, 6); assert.match(JSON.stringify(all.sections.find(s => s.id === 'fee')), /TVA \(21%\)/); assert.match(JSON.stringify(all.sections.find(s => s.id === 'revisions')), /nu include runde/);
    const empty = buildContract(ctx({ studio: {}, client: {}, meta: {}, data: { ...DEFAULT_CONTRACT }, rooms: [], areaM2: 0 })); assert.match(JSON.stringify(empty), /………/); assert.match(JSON.stringify(empty.sections.find(s => s.id === 'fee')), /urmează să fie stabilit/);
  });
  test('oferta e mai scurtă și are valabilitate', () => {
    const o = buildOffer(ctx({ data: { ...DEFAULT_CONTRACT, fee: { kind: 'percent', amount: 8 }, validUntil: '2026-11-15' } })), c = buildContract(ctx());
    for (const id of ['scope', 'phases', 'fee', 'validity']) assert.ok(o.sections.some(s => s.id === id), id);
    assert.ok(o.sections.length < c.sections.length); assert.match(JSON.stringify(o.sections.find(s => s.id === 'validity')), /15\.11\.2026/); assert.equal(o.notice, CONTRACT_NOTICE);
    assert.match(JSON.stringify(buildOffer(ctx())), /30 de zile/);
  });
});
