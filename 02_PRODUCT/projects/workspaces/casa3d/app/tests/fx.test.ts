// Monedă străină pe produsele la comandă: cursurile proiectului (validare, conversie, rotunjire), parserul BNR pe un eșantion
// static (fără rețea), comenzile / devizul / profitul / proforma cu EUR (cu curs → convertit; fără → necunoscut, neadunat),
// revizia care îngheață cursul și garanția că fără monede străine rezultatele rămân identice.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { addCustomItem, getCustomItems, listCustomItems } from '../lib/custom-items';
import { getOrders } from '../lib/procurement';
import { getCommercial, getProforma } from '../lib/commercial';
import { getContractBundle } from '../lib/contract';
import { offerLines } from '../lib/progress';
import { exportProject } from '../lib/project-export';
import { computeBudget } from '../core/boq';
import { procurementLines, procurementRows, bySupplier, procurementSummary } from '../core/procurement';
import { commercialSources, profitReport, buildProforma } from '../core/commercial';
import { customBudget } from '../core/custom-csv';
import { adviseProject } from '../core/advisor';
import { adviceText } from '../lib/i18n';
import { checkFxRates, toProjectCurrency, fxItem, fxUsage, parseBnrXml, fxRowText, formatRate, cleanRate, FxError, FX_LIMITS, type FxRates } from '../core/fx';
import type { Snapshot } from '../core/types';
beforeAll(() => resetDbForTests());

const A = 'aaaaaaaa-f0f0-4f0f-8f0f-aaaaaaaaaaaa';
const EUR: FxRates = { EUR: { rate: 4.9771, date: '2026-11-20', source: 'manual' } };
// eșantion din https://curs.bnr.ro/nbrfxrates.xml (2026-11-20), scurtat; HUF și JPY au multiplier="100"
const BNR_XML = `<?xml version="1.0" encoding="utf-8"?>
<DataSet xmlns="https://www.bnr.ro/xsd" xmlns:xsi="https://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://curs.bnr.ro/xsd/nbrfxrates.xsd"><Header><Publisher>National Bank of Romania</Publisher><PublishingDate>2026-11-20</PublishingDate><MessageType>DR</MessageType></Header><Body><Subject>Reference rates</Subject><OrigCurrency>RON</OrigCurrency><Cube date="2026-11-20"><Rate currency="CHF">5.7953</Rate><Rate currency="EUR">5.3482</Rate><Rate currency="GBP">6.3916</Rate><Rate currency="HUF" multiplier="100">1.4341</Rate><Rate currency="JPY" multiplier="100">3.0695</Rate><Rate currency="PLN">1.1999</Rate><Rate currency="USD">4.8782</Rate><Rate currency="XAU">563.6174</Rate></Cube></Body></DataSet>`;

test('cursurile proiectului: validare strictă, rotunjire, moneda proiectului nu are curs', () => {
  assert.deepEqual(checkFxRates(null), {}); assert.deepEqual(checkFxRates({}), {});
  assert.deepEqual(checkFxRates({ USD: { rate: 4.8782, date: '2026-11-20', source: 'bnr', extra: 1 }, EUR: { rate: 4.9771, date: '2026-11-20', source: 'manual' } }),
    { EUR: { rate: 4.9771, date: '2026-11-20', source: 'manual' }, USD: { rate: 4.8782, date: '2026-11-20', source: 'bnr' } }, 'chei sortate, câmpuri necunoscute eliminate');
  // rotunjire: 6 zecimale (cele 4 ale BNR + 2 pentru monedele cu multiplier 100); 4 zecimale rămân neschimbate
  assert.equal(checkFxRates({ EUR: { rate: 4.9771234567, date: '2026-11-20', source: 'manual' } }).EUR!.rate, 4.977123);
  assert.equal(checkFxRates({ EUR: { rate: 4.9771, date: '2026-11-20', source: 'manual' } }).EUR!.rate, 4.9771);
  assert.equal(checkFxRates({ HUF: { rate: 0.014341, date: '2026-11-20', source: 'bnr' } }).HUF!.rate, 0.014341);
  assert.equal(cleanRate(1000), 1000); assert.equal(cleanRate(1000.0000001), 1000); assert.equal(cleanRate(0.0000004), null, 'rotunjit la 0 → invalid');
  const ok = { rate: 5, date: '2026-11-20', source: 'manual' };
  for (const bad of [[], 'x', 5, { eur: ok }, { EURO: ok }, { 'E1R': ok }, { RON: ok }, { EUR: null }, { EUR: [] }, { EUR: { ...ok, rate: 0 } }, { EUR: { ...ok, rate: -1 } }, { EUR: { ...ok, rate: 1000.01 } },
    { EUR: { ...ok, rate: NaN } }, { EUR: { ...ok, rate: Infinity } }, { EUR: { ...ok, rate: '5' } }, { EUR: { ...ok, date: '2026-02-30' } }, { EUR: { ...ok, date: '20.11.2026' } }, { EUR: { ...ok, date: undefined } },
    { EUR: { ...ok, source: 'ecb' } }, { EUR: { ...ok, source: undefined } }, { __proto__: null, constructor: ok }])
    assert.throws(() => checkFxRates(bad), FxError, JSON.stringify(bad));
  assert.throws(() => checkFxRates(Object.fromEntries(Array.from({ length: FX_LIMITS.currencies + 1 }, (_, i) => [`A${String.fromCharCode(65 + i % 26)}${String.fromCharCode(65 + Math.floor(i / 26))}`, ok]))), /Cel mult/);
  // moneda proiectului e parametru: într-un proiect în EUR, EUR nu are curs, dar RON poate avea
  assert.throws(() => checkFxRates({ EUR: ok }, 'EUR'), FxError); assert.deepEqual(Object.keys(checkFxRates({ RON: ok }, 'EUR')), ['RON']);
});

test('conversia: monedă lipsă sau a proiectului → neschimbat; cu curs → × curs la 2 zecimale; fără curs → lipsă, nu sumă', () => {
  assert.deepEqual(toProjectCurrency(1234.567, null, 'RON', EUR), { value: 1234.567, missing: false }, 'nici măcar nu se rotunjește');
  assert.deepEqual(toProjectCurrency(10, 'RON', 'RON', EUR), { value: 10, missing: false }); assert.deepEqual(toProjectCurrency(10, '', 'RON'), { value: 10, missing: false });
  assert.deepEqual(toProjectCurrency(1200, 'EUR', 'RON', EUR), { value: 5972.52, missing: false }); assert.deepEqual(toProjectCurrency(1200, 'eur', 'RON', EUR), { value: 5972.52, missing: false });
  assert.deepEqual(toProjectCurrency(1.005, 'EUR', 'RON', { EUR: { rate: 1, date: '2026-01-01', source: 'manual' } }), { value: 1.01, missing: false }, 'jumătatea se rotunjește în sus, fără eroare binară');
  assert.deepEqual(toProjectCurrency(1200, 'USD', 'RON', EUR), { value: null, missing: true }); assert.deepEqual(toProjectCurrency(1200, 'EUR', 'RON'), { value: null, missing: true });
  assert.deepEqual(toProjectCurrency(null, 'EUR', 'RON', EUR), { value: null, missing: false }, 'preț necunoscut rămâne necunoscut, cursul există');
  assert.deepEqual(toProjectCurrency(1200, 'constructor', 'RON', EUR), { value: null, missing: true }, 'cheile moștenite nu sunt cursuri');
  // produsul: același obiect când nu e monedă străină; altfel prețuri unitare în lei + originalul; a doua trecere nu mai schimbă
  const ron = { price: 100, costPrice: 60, currency: null as string | null }; assert.equal(fxItem(ron, EUR), ron); const ron2 = { price: 100, currency: 'RON' }; assert.equal(fxItem(ron2, EUR), ron2);
  const e = fxItem({ price: 1200, costPrice: 900, currency: 'EUR', qty: 2 }, EUR);
  assert.deepEqual(e, { price: 5972.52, costPrice: 4479.39, currency: 'RON', qty: 2, fx: { currency: 'EUR', rate: 4.9771, date: '2026-11-20', source: 'manual', price: 1200, costPrice: 900 } });
  assert.deepEqual(fxItem(e, EUR), e); assert.deepEqual(fxItem(e, { EUR: { rate: 9, date: '2026-11-21', source: 'manual' } }), e, 'un produs deja convertit nu se convertește a doua oară');
  const m = fxItem({ price: 50, costPrice: 40, currency: 'USD' }, EUR); assert.equal(m.price, null); assert.equal(m.costPrice, null); assert.equal(m.fx!.rate, null); assert.equal(m.fx!.price, 50);
  assert.deepEqual(fxUsage([{ currency: 'EUR' }, { currency: 'usd' }, { currency: null }, { currency: 'RON' }, e, m], EUR), { used: ['EUR', 'USD'], missing: ['USD'] });
  assert.deepEqual(fxUsage([{ currency: null }], EUR), { used: [], missing: [] });
  assert.equal(fxRowText(e.fx!, e.price, 'ro-RO').text, '1.200 EUR × 4,9771 = 5.972,52 lei'); assert.equal(fxRowText(e.fx!, e.price, 'en-GB').text, '1,200 EUR × 4.9771 = 5,972.52 RON');
  assert.deepEqual(fxRowText(m.fx!, null, 'ro-RO'), { orig: '50 USD', text: null }); assert.equal(formatRate(0.014341, 'ro-RO'), '0,014341');
});

test('parserul BNR: data, cursurile, multiplier; orice altă formă e refuzată (fără rețea)', () => {
  const r = parseBnrXml(BNR_XML);
  assert.equal(r.date, '2026-11-20'); assert.equal(r.rates.EUR, 5.3482); assert.equal(r.rates.USD, 4.8782); assert.equal(r.rates.PLN, 1.1999);
  assert.equal(r.rates.HUF, 0.014341, '1,4341 lei pentru 100 HUF'); assert.equal(r.rates.JPY, 0.030695); assert.equal(r.rates.XAU, 563.6174);
  assert.equal(Object.keys(r.rates).length, 8); assert.ok(!('RON' in r.rates));
  // cursurile citite trec validarea proiectului
  assert.equal(checkFxRates({ HUF: { rate: r.rates.HUF, date: r.date, source: 'bnr' } }).HUF!.rate, 0.014341);
  for (const bad of ['', null, 42, '<html><body>Pagina BNR</body></html>', '<Cube date="2026-11-20"></Cube>', '<Cube date="2026-13-45"><Rate currency="EUR">5.1</Rate></Cube>',
    '<Cube date="2026-11-20"><Rate currency="EUR">5.1</Rate></Cube><Cube date="2026-11-19"><Rate currency="EUR">5.0</Rate></Cube>', '<Rate currency="EUR">5.1</Rate>',
    '<Cube date="2026-11-20"><Rate currency="EUR">abc</Rate><Rate currency="eur">5</Rate><Rate currency="USD">-4</Rate><Rate currency="GBP">0</Rate><Rate currency="CHF">1e3</Rate></Cube>'])
    assert.throws(() => parseBnrXml(bad), FxError, String(bad).slice(0, 60));
  // rândurile cu altă formă se sar; prima apariție a unei monede câștigă; cursul peste limită nu intră; entitățile nu se interpretează
  const odd = parseBnrXml('<!DOCTYPE x [<!ENTITY e "9">]><Cube date="2026-11-20"><Rate currency="EUR">5.3482</Rate><Rate currency="EUR">9.9</Rate><Rate currency="USD">&e;</Rate><Rate currency="GBP" multiplier="7">6</Rate><Rate currency="RON">1</Rate><Rate currency="XXX">5000</Rate></Cube>');
  assert.deepEqual(odd, { date: '2026-11-20', rates: { EUR: 5.3482 } });
});

const custom = (over: Record<string, unknown> = {}) => ({ id: 'c1', name: 'Canapea import', supplier: 'Atelier Milano', url: null, image: null, sku: null, price: 1200 as number | null, qty: 2, unit: 'buc', roomId: null, costPrice: 900 as number | null, currency: 'EUR' as string | null, ...over });

test('comenzi, deviz, profit și proformă cu un produs în EUR: cu curs e convertit; fără curs e necunoscut și nu se adună', async () => {
  const id = await repo.createProject(A, 'FX pur', 'demo'), p = await repo.getProject(A, id), [cat, mc] = await Promise.all([repo.getCatalog(), repo.getMaterials()]);
  const snap = p.draft, withFx: Snapshot = { ...snap, fx: EUR }, c = custom();
  // --- cu curs
  const line = procurementLines(withFx, cat, mc, [c]).find(l => l.key === 'c|c1')!;
  assert.equal(line.unitPrice, 5972.52); assert.equal(line.total, 11945.04); assert.equal(line.costPrice, 4479.39); assert.equal(line.costTotal, 8958.78);
  assert.deepEqual(line.fx, { currency: 'EUR', rate: 4.9771, date: '2026-11-20', source: 'manual', price: 1200, costPrice: 900 });
  const base = computeBudget(snap, cat, mc), b = computeBudget(withFx, cat, mc, [c]);
  assert.equal(b.categories.custom, 11945.04); assert.equal(b.fxMissing, undefined); assert.equal(b.chosen.subtotal, Math.round((base.chosen.subtotal + 11945.04) * 100) / 100);
  const src = commercialSources(b, [c]), sl = src.items.find(i => i.key === 'c|c1')!;
  assert.equal(sl.unitPrice, 5972.52); assert.equal(sl.total, 11945.04); assert.equal(sl.costTotal, 8958.78); assert.equal(sl.fx!.currency, 'EUR');
  const rep = profitReport({ defaults: { markupPct: 10 }, suppliers: {} }, src.items, src.labor, null, 0, src.extras), pl = rep.lines.find(l => l.key === 'c|c1')!;
  assert.equal(pl.list, 11945.04); assert.equal(pl.cost, 8958.78, 'costul real, convertit'); assert.equal(pl.client, 13139.54); assert.equal(pl.profit, 4180.76);
  const pf = buildProforma(rep, src.labor, 'Onorariu', null); assert.ok(pf.groups.find(g => g.category === 'custom')!.lines.some(l => l.name === 'Canapea import' && l.gross > 13139 && l.gross < 13140));
  assert.ok(!/\bEUR\b|4\.9771|"fx"|8958\.78|4479\.39/.test(JSON.stringify(pf)), 'proforma nu poartă moneda originală, cursul sau costul');
  assert.equal(customBudget([c], EUR).sell, 11945.04); assert.equal(customBudget([c], EUR).cost, 8958.78); assert.equal(customBudget([c], EUR).noPrice, 0);
  // --- fără curs: preț necunoscut, ca un produs fără preț
  const noRate = procurementLines(snap, cat, mc, [c]).find(l => l.key === 'c|c1')!;
  assert.equal(noRate.unitPrice, null); assert.equal(noRate.total, null); assert.equal(noRate.costTotal, null); assert.equal(noRate.fx!.rate, null); assert.equal(noRate.fx!.price, 1200);
  const rows = procurementRows([noRate], { 'c|c1': { status: 'ordered' } }, '2026-11-22'), g = bySupplier(rows)[0]!;
  assert.equal(g.total, null, 'totalul furnizorului e necunoscut, nu 2400'); assert.equal(procurementSummary(rows).ordersValue, 0, '2400 EUR nu se adună ca 2400 lei');
  const b0 = computeBudget(snap, cat, mc, [c]); assert.equal(b0.categories.custom, 0); assert.deepEqual(b0.fxMissing, ['EUR']); assert.equal(b0.chosen.total, computeBudget(snap, cat, mc, [custom({ price: null, costPrice: null, currency: null })]).chosen.total);
  const src0 = commercialSources(b0, [c]), rep0 = profitReport({ defaults: {}, suppliers: {} }, src0.items, src0.labor, null, 0, src0.extras), repBase = profitReport({ defaults: {}, suppliers: {} }, commercialSources(base).items, src0.labor, null, 0, src0.extras);
  assert.equal(rep0.unknownLines, repBase.unknownLines + 1); assert.deepEqual(rep0.totals, repBase.totals, 'totalurile nu cuprind produsul fără curs');
  assert.equal(buildProforma(rep0, src0.labor, 'Onorariu', null).groups.find(g => g.category === 'custom'), undefined, 'linia fără curs nu apare pe proformă');
  assert.equal(customBudget([c]).sell, 0); assert.equal(customBudget([c]).noPrice, 1);
  // curs pentru altă monedă decât a produsului: tot lipsă
  assert.deepEqual(computeBudget({ ...snap, fx: { USD: { rate: 4.8, date: '2026-11-20', source: 'manual' } } }, cat, mc, [c]).fxMissing, ['EUR']);
  // verificările dinaintea predării
  const adv = adviseProject(snap, cat, { fxMissing: ['EUR', 'USD'] }).find(a => a.code === 'FX_MISSING')!;
  assert.equal(adv.severity, 'WARNING'); assert.match(adviceText('ro', adv).title, /Lipsește cursul valutar: EUR, USD/); assert.match(adviceText('en', adv).title, /Missing exchange rate: EUR, USD/);
  assert.equal(adviseProject(snap, cat, { fxMissing: [] }).some(a => a.code === 'FX_MISSING'), false); assert.equal(adviseProject(snap, cat, {}).some(a => a.code === 'FX_MISSING'), false);
});

test('fără produse în altă monedă rezultatele sunt identice, cu sau fără cursuri pe proiect', async () => {
  const id = await repo.createProject(A, 'FX identic', 'demo'), p = await repo.getProject(A, id), [cat, mc] = await Promise.all([repo.getCatalog(), repo.getMaterials()]), snap = p.draft;
  const { currency: _c, ...plain } = custom({ price: 1200.5, costPrice: 900.25 }), items = [plain, { ...plain, id: 'c2', price: null, costPrice: null }];
  const variants = [items, items.map(i => ({ ...i, currency: null })), items.map(i => ({ ...i, currency: 'RON' }))];
  const ref = { lines: procurementLines(snap, cat, mc, items), budget: computeBudget(snap, cat, mc, items) }, refSrc = commercialSources(ref.budget, items), refRep = profitReport({ defaults: { discountPct: 5, markupPct: 12 }, suppliers: {} }, refSrc.items, refSrc.labor, 1000, 210, refSrc.extras);
  assert.equal(ref.lines.find(l => l.key === 'c|c1')!.total, 2401); assert.ok(!('fx' in ref.budget) && !('fxMissing' in ref.budget)); assert.ok(ref.lines.every(l => !('fx' in l)));
  for (const v of variants){
    assert.deepEqual(procurementLines(snap, cat, mc, v), ref.lines); const b = computeBudget(snap, cat, mc, v); assert.deepEqual(b, ref.budget);
    const s = commercialSources(b, v); assert.deepEqual(s, refSrc); assert.deepEqual(profitReport({ defaults: { discountPct: 5, markupPct: 12 }, suppliers: {} }, s.items, s.labor, 1000, 210, s.extras), refRep);
    assert.deepEqual(customBudget(v), customBudget(items)); assert.deepEqual(customBudget(v, EUR), customBudget(items));
    // cu un curs pe proiect, dar nicio monedă străină folosită: aceleași cifre (bugetul doar ține minte cursurile)
    const withFx = { ...snap, fx: EUR }; assert.deepEqual(procurementLines(withFx, cat, mc, v), ref.lines);
    const { fx, ...bf } = computeBudget(withFx, cat, mc, v); assert.deepEqual(fx, EUR); assert.deepEqual(bf, ref.budget); assert.deepEqual(commercialSources({ ...bf, fx }, v), refSrc);
  }
  // și fără produse proprii deloc
  assert.deepEqual(computeBudget({ ...snap, fx: EUR }, cat, mc).chosen, computeBudget(snap, cat, mc).chosen); assert.deepEqual(procurementLines({ ...snap, fx: EUR }, cat, mc), procurementLines(snap, cat, mc));
});

test('pe server: cursul se salvează în ciornă (validat), intră în comenzi, comercial, proformă, situații și export; revizia îl îngheață', async () => {
  const id = await repo.createProject(A, 'FX server', 'demo'), before = await getCommercial(A, id), ordersBefore = await getOrders(A, id);
  const item = await addCustomItem(A, id, { name: 'Corp iluminat Flos', supplier: 'Flos Milano', price: 1200, costPrice: 900, currency: 'eur', qty: 2, unit: 'buc' });
  assert.equal(item.currency, 'EUR'); assert.equal(item.price, 1200, 'în baza de date rămâne prețul original');
  // fără curs: marcat, neadunat nicăieri
  let o = await getOrders(A, id), row = o.rows.find(r => r.itemId === item.id)!;
  assert.equal(row.total, null); assert.equal(row.fx!.rate, null); assert.deepEqual({ used: o.fx.used, missing: o.fx.missing }, { used: ['EUR'], missing: ['EUR'] });
  assert.equal(o.groups.find(g => g.supplier === 'Flos Milano')!.total, null);
  let c = await getCommercial(A, id); assert.deepEqual(c.fx.missing, ['EUR']); assert.equal(c.report.unknownLines, before.report.unknownLines + 1); assert.equal(c.chain.total, before.chain.total, 'totalul proiectului nu cuprinde 2400 „lei”');
  assert.deepEqual((await getProforma(A, id)).fxMissing, ['EUR']); assert.deepEqual((await getContractBundle(A, id)).fxMissing, ['EUR']);
  const items0 = await getCustomItems(A, id); assert.deepEqual(items0.fx, { used: ['EUR'], missing: ['EUR'] }); assert.equal(items0.budget.sell, 0); assert.equal(items0.budget.noPrice, 1);
  const off0 = await offerLines(A, id); assert.ok(!off0.lines.some(l => l.label.startsWith('Corp iluminat Flos')), 'devizul de bază nu primește linia fără curs');
  // cursul greșit e refuzat la salvare; cel valid e rotunjit și păstrat
  const p0 = await repo.getProject(A, id);
  for (const fx of [{ EUR: { rate: 0, date: '2026-11-20', source: 'manual' } }, { RON: { rate: 1, date: '2026-11-20', source: 'manual' } }, { EUR: { rate: 5, date: 'ieri', source: 'manual' } }, { EUR: { rate: 5, date: '2026-11-20', source: 'x' } }, 'EUR=5'])
    await assert.rejects(repo.saveDraft(A, id, { ...p0.draft, fx }), (e: any) => e.status === 400, JSON.stringify(fx));
  await repo.saveDraft(A, id, { ...p0.draft, fx: { EUR: { rate: 4.9771000001, date: '2026-11-20', source: 'manual', junk: true } } });
  const p1 = await repo.getProject(A, id); assert.deepEqual(p1.draft.fx, EUR);
  // cu curs: peste tot la 4,9771
  o = await getOrders(A, id); row = o.rows.find(r => r.itemId === item.id)!;
  assert.equal(row.unitPrice, 5972.52); assert.equal(row.total, 11945.04); assert.equal(row.costTotal, 8958.78); assert.deepEqual(o.fx.missing, []); assert.deepEqual(o.fx.rates, EUR);
  assert.equal(o.groups.find(g => g.supplier === 'Flos Milano')!.total, 11945.04); assert.equal(o.rows.length, ordersBefore.rows.length + 1);
  c = await getCommercial(A, id); assert.deepEqual(c.fx.missing, []); assert.equal(c.report.unknownLines, before.report.unknownLines);
  assert.equal(c.report.bySupplier['Flos Milano']!.list, 11945.04); assert.equal(c.report.bySupplier['Flos Milano']!.cost, 8958.78);
  assert.equal(c.report.goods.list, Math.round(((before.report.goods.list ?? 0) + 11945.04) * 100) / 100);
  const pf = await getProforma(A, id); assert.deepEqual(pf.fxMissing, []); assert.ok(pf.proforma.groups.find(g => g.category === 'custom')!.lines.some(l => l.name === 'Corp iluminat Flos' && Math.abs(l.gross - 11945.04) < 0.02));
  assert.deepEqual((await getCustomItems(A, id)).fx, { used: ['EUR'], missing: [] }); assert.equal((await getCustomItems(A, id)).budget.sell, 11945.04);
  const off = (await offerLines(A, id)).lines.find(l => l.label.startsWith('Corp iluminat Flos'))!; assert.equal(off.unitPrice, 5972.52); assert.equal(off.qty, 2);
  assert.equal((await listCustomItems(id))[0]!.price, 1200, 'conversia nu atinge datele salvate');
  const zip = (await exportProject(A, id)).data.toString('latin1'); assert.ok(zip.includes('comenzi.csv') && zip.includes('deviz.csv'));
  // revizia îngheață cursul: schimbarea ulterioară din ciornă nu o atinge
  const rev = await repo.createRevision(A, id, 'ofertă trimisă');
  const p2 = await repo.getProject(A, id); await repo.saveDraft(A, id, { ...p2.draft, fx: { EUR: { rate: 5.3482, date: '2026-11-22', source: 'bnr' } } });
  const { q } = await getDb(), frozen = (await q('select snapshot from revisions where project_id=$1 and number=$2', [id, rev.number])).rows[0].snapshot as Snapshot;
  assert.deepEqual(frozen.fx, EUR, 'revizia păstrează cursul de atunci');
  const [cat, mc] = await Promise.all([repo.getCatalog(), repo.getMaterials()]), live = (await repo.getProject(A, id)).draft, list = await listCustomItems(id);
  assert.equal(procurementLines(frozen, cat, mc, list).find(l => l.itemId === item.id)!.total, 11945.04); assert.equal(procurementLines(live, cat, mc, list).find(l => l.itemId === item.id)!.total, 12835.68);
  assert.equal(computeBudget(frozen, cat, mc, list).categories.custom, 11945.04); assert.equal(computeBudget(live, cat, mc, list).categories.custom, 12835.68);
  assert.equal((await getOrders(A, id)).rows.find(r => r.itemId === item.id)!.total, 12835.68);
  // restaurarea reviziei readuce și cursul ei
  assert.deepEqual((await repo.restoreRevision(A, id, rev.number)).fx, EUR);
  // ștergerea cursului (cheie absentă) e validă: produsul redevine „fără curs”
  const p3 = await repo.getProject(A, id), { fx: _gone, ...noFx } = p3.draft; await repo.saveDraft(A, id, { ...noFx, fx: {} });
  assert.equal((await repo.getProject(A, id)).draft.fx, undefined); assert.deepEqual((await getOrders(A, id)).fx.missing, ['EUR']);
});
