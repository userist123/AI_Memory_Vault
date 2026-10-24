// Un singur proiect, un singur total și un singur onorariu: bugetul, panoul comercial și proforma folosesc același lanț
// (listă → preț de client cu adaos → onorariu net + TVA → rezervă → total), iar onorariul stă doar în contract.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { getCommercial, getProforma, saveCommercial } from '../lib/commercial';
import { getContractData, getContractBundle, saveContract, setFee, projectBudget } from '../lib/contract';
import { getTimesheet } from '../lib/timesheet';
import { commercialSources, profitReport, projectChain, chainRows } from '../core/commercial';
import { migratedFee } from '../core/contract';
beforeAll(() => resetDbForTests());

const A = 'aaaaaaaa-7777-4777-8777-aaaaaaaaaaaa';
const near = (a: number, b: number, tol: number, msg: string) => assert.ok(Math.abs(a - b) <= tol, `${msg}: ${a} vs ${b}`);

test('migratedFee: contractul câștigă; apoi fișa; „Proiectare” din buget doar pentru un proiect fără contract salvat', () => {
  assert.deepEqual(migratedFee({ fee: { kind: 'fixed', amount: 7000 } }, { fee: { kind: 'fixed', amount: 1 } }, 5), { kind: 'fixed', amount: 7000 });
  assert.deepEqual(migratedFee(null, { fee: { kind: 'per_m2', amount: 90 } }, 5), { kind: 'per_m2', amount: 90 });
  assert.deepEqual(migratedFee(null, {}, 4500), { kind: 'fixed', amount: 4500 });
  assert.equal(migratedFee({}, {}, 4500), undefined, 'cu un contract salvat fără onorariu, bugetul vechi nu-l readuce');
});

test('un singur onorariu: fișa veche, bugetul vechi și contractul ajung la aceeași valoare, citită și scrisă într-un loc', async () => {
  const id = await repo.createProject(A, 'Onorariu unic', 'demo');
  await repo.saveMeta(A, id, { status: 'offer', fee: { kind: 'fixed', amount: 4500 } });
  assert.deepEqual((await getContractData(A, id)).fee, { kind: 'fixed', amount: 4500 }, 'onorariul din fișă e migrat la citire');
  assert.equal((await getCommercial(A, id)).report.fee, 4500);
  // bugetul (BudgetPanel) scrie același loc: contractul; fișa nu mai ține o a doua valoare
  await setFee(A, id, { kind: 'fixed', amount: 5000 });
  assert.equal((await repo.getProject(A, id)).meta.fee, undefined, 'meta.fee e șters după migrare');
  const b = await getContractBundle(A, id); assert.equal(b.effectiveFee?.amount, 5000); assert.equal(b.calc.net, 5000);
  assert.equal((await getCommercial(A, id)).report.fee, 5000);
  assert.equal((await getProforma(A, id)).proforma.groups.find(g => g.category === 'fee')!.lines[0]!.net, 5000);
  // fișa (ProjectCard) se salvează fără `fee`: nu readuce onorariul vechi și nu-l șterge pe cel din contract
  await repo.saveMeta(A, id, { status: 'design', client: { name: 'Ana' } });
  assert.equal((await getContractData(A, id)).fee?.amount, 5000);
  // ContractPanel scrie același loc; `fee: null` = fără onorariu, ales explicit
  await saveContract(A, id, { ...(await getContractData(A, id)), fee: { kind: 'fixed', amount: 6000 } });
  assert.equal((await getCommercial(A, id)).report.fee, 6000);
  assert.equal((await getTimesheet(A, id)).summary.effectiveRate, null, 'pontajul citește același onorariu (fără ore încă)');
  await setFee(A, id, null);
  assert.equal((await getContractData(A, id)).fee, undefined); assert.equal((await getCommercial(A, id)).report.fee, null);

  // proiect vechi cu „Proiectare” în buget și fără contract: valoarea devine onorariul contractului, iar bugetul n-o mai adună separat
  const id2 = await repo.createProject(A, 'Proiectare veche', 'demo'), p2 = await repo.getProject(A, id2);
  p2.draft.budget = { ...(p2.draft.budget ?? {}), design: 3000 } as any;
  const { q } = await getDb(); await q('update projects set draft=$1 where id=$2', [JSON.stringify(p2.draft), id2]);
  assert.deepEqual((await getContractData(A, id2)).fee, { kind: 'fixed', amount: 3000 });
  const { budget } = await projectBudget(await repo.getProject(A, id2));
  assert.ok(!budget.extras.some(e => /proiectare/i.test(e.label)), 'onorariul nu mai e o linie a bugetului');
  await saveContract(A, id2, { vatPayer: false });   // fără cheia `fee`: onorariul vechi se mută în contract
  assert.equal((await getContractData(A, id2)).fee?.amount, 3000);
});

test('un singur total: bugetul, panoul comercial și proforma se reconciliază pe proiectul demo cu adaos și onorariu', async () => {
  const id = await repo.createProject(A, 'Total unic', 'demo');
  await saveContract(A, id, { vatPayer: true, fee: { kind: 'fixed', amount: 4500 } });
  const c0 = await getCommercial(A, id), sup = c0.suppliers[0]!;
  const terms = { defaults: { discountPct: 10, markupPct: 20 }, suppliers: { [sup]: { markupPct: 35 } } };
  await saveCommercial(A, id, { ...terms, milestones: [], proforma: { series: 'PF', number: 1 } });
  const c = await getCommercial(A, id), pf = (await getProforma(A, id)), ch = c.chain;

  // 1. lanțul: listă → + adaos = client → + manoperă, transport → + onorariu (net + TVA) = de facturat → + rezervă = total
  assert.ok(ch.markup > 0, 'adaosul se vede ca rând separat');
  assert.equal(ch.feeNet, 4500); assert.equal(ch.feeVat, 945); assert.equal(ch.feeGross, 5445);
  near(ch.goodsClient, ch.list + ch.markup, 0.01, 'client = listă + adaos');
  assert.equal(ch.invoiceable, c.report.totals.client, 'de facturat = totalul de client din panoul comercial');
  near(ch.invoiceable, ch.goodsClient + ch.labor + ch.extras + ch.feeGross, 0.01, 'de facturat');
  near(ch.contingency, (ch.goodsClient + ch.labor + ch.extras) * ch.contingencyPct / 100, 0.01, 'rezerva nu se aplică onorariului');
  assert.equal(ch.total, Math.round((ch.invoiceable + ch.contingency) * 100) / 100);

  // 2. proforma = de facturat (cu rotunjirile TVA pe linie), iar transportul și montajul sunt pe proformă
  const lines = pf.proforma.groups.reduce((a, g) => a + g.lines.length, 0);
  near(pf.proforma.gross, ch.invoiceable, 0.01 * lines, 'proforma ≈ de facturat');
  if (ch.extras > 0) assert.ok(pf.proforma.groups.some(g => g.category === 'extras'), 'transportul / montajul cunoscut e pe proformă');
  assert.deepEqual(pf.chain, { invoiceable: ch.invoiceable, contingency: ch.contingency, contingencyPct: ch.contingencyPct, total: ch.total });

  // 3. bugetul (în browser, cu planul curent) calculează exact același lanț din aceleași funcții
  const p = await repo.getProject(A, id), { budget, custom } = await projectBudget(p), src = commercialSources(budget, custom);
  const r = profitReport(terms, src.items, src.labor, c.fee.net, c.fee.vat, src.extras);
  assert.deepEqual(projectChain(r, budget.settings.contingencyPct), ch, 'bugetul = panoul comercial');

  // fără adaos și fără onorariu, totalul lanțului e exact totalul bugetului
  const plain = projectChain(profitReport({ defaults: {}, suppliers: {} }, src.items, src.labor, null, 0, src.extras), budget.settings.contingencyPct);
  near(plain.total, budget.chosen.total, 0.02, 'lanțul fără adaos și fără onorariu = totalul bugetului');

  // rândurile au nume și apar în ordinea canonică
  const keys = chainRows(ch, pf.proforma.gross).map(x => x.key);
  const order = ['chain.list', 'chain.markup', 'chain.goodsClient', 'chain.labor', 'chain.feeNet', 'chain.feeVat', 'chain.invoiceable', 'chain.contingency', 'chain.total'];
  assert.deepEqual(keys.filter(k => order.includes(k)), order);
});
