// Cereri de ofertă către meseriași (core/rfq.ts), partea pură: harta tarif → meserie, agregarea pe tarif cu defalcare pe camere,
// validarea strictă a ofertei, compararea pe aceeași bază (cu / fără TVA), oferta incompletă sau parțială în afara clasamentului,
// avertismentul „mult sub piață”, incluziunile și prețurile contractate (costul manoperei în raportul de profit).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import materials from '../data/materials.v1.json';
import { TRADES, RATE_TRADE, tradeOfRate, packageLines, checkLines, publicLines, checkOffer, offerMissing, offerCoverage, offerTotals, compareOffers, contractFromOffer, sanitizeLaborContract,
  contractCost, INCLUSIONS, RfqError, type RfqLine, type RfqOffer, type LaborSource } from '../core/rfq';
import { commercialSources, profitReport } from '../core/commercial';

const lab = (rateId: string, roomId: string, qty: number, low: number, expected: number, label = rateId, unit = 'm2'): LaborSource => ({ rateId, roomId, label, unit, qty, low: qty * low, expected: qty * expected });
const LABOR: LaborSource[] = [lab('manopera-gresie', 'baie', 6, 50, 85, 'Montaj gresie 60x60'), lab('manopera-zugravit', 'living', 60, 31, 38, 'Zugrăvit'), lab('manopera-gresie', 'hol', 4, 50, 85, 'Montaj gresie 60x60'),
  lab('manopera-faianta', 'baie', 20, 38, 75, 'Montaj faianță'), lab('manopera-gresie', 'baie', 1.5, 50, 85, 'Montaj gresie 60x60'), lab('manopera-plinta', 'living', 18, 8, 20, 'Montaj plintă MDF', 'ml')];
const NAMES = { baie: 'Baie', living: 'Living', hol: 'Hol' };
const LINES = packageLines(LABOR, ['gresieFaianta', 'zugraveli'], NAMES);
// ordinea rândurilor e a meseriilor (zugrăveli înaintea gresiei și faianței), apoi a apariției în deviz
const line = (id: string) => LINES.find(l => l.rateId === id)!, rowOf = <T extends { rateId: string }>(rows: T[], id: string) => rows.find(r => r.rateId === id)!;
const INC = Object.fromEntries(INCLUSIONS.map(k => [k, { mode: 'included' }])) as RfqOffer['inclusions'];
const full = (prices: Record<string, number | null>, vatPayer = false, extra: Partial<RfqOffer> = {}): RfqOffer => ({ v: 1, prices, inclusions: INC, company: { form: vatPayer ? 'srl' : 'pfa', vatPayer },
  advancePct: 30, startDate: '2026-12-07', durationDays: 12, warrantyMonths: 24, validDays: 30, ...extra });
const bad = (fn: () => unknown, re?: RegExp) => assert.throws(fn, (e: any) => e instanceof RfqError && (!re || re.test(e.message)));

test('harta meserii: fiecare tarif din labor_rates are o meserie cunoscută (nu „altele”); tarifele noi se ghicesc după id', () => {
  const ids = (materials as any).labor.map((l: any) => l.id as string); assert.ok(ids.length >= 15);
  for (const id of ids){ assert.ok(Object.hasOwn(RATE_TRADE, id), `necartat: ${id}`); assert.notEqual(tradeOfRate(id), 'altele', id); assert.ok(TRADES.includes(tradeOfRate(id))); }
  assert.equal(tradeOfRate('manopera-gresie-mare'), 'gresieFaianta'); assert.equal(tradeOfRate('manopera-parchet-spic'), 'pardoseli'); assert.equal(tradeOfRate('manopera-scafa'), 'gipscarton');
  assert.equal(tradeOfRate('manopera-demolare-perete'), 'demolari'); assert.equal(tradeOfRate('manopera-prize-electrice'), 'electric'); assert.equal(tradeOfRate('manopera-montaj-usa'), 'usi');
  assert.equal(tradeOfRate('ceva-necunoscut'), 'altele'); assert.equal(tradeOfRate('__proto__'), 'altele'); assert.equal(tradeOfRate('constructor'), 'altele');
});

test('agregare: un rând pe tarif, cantitatea totală pe proiect, defalcare pe camere, reper pe unitate; doar meseriile alese', () => {
  assert.deepEqual(LINES.map(l => [l.rateId, l.trade, l.qty, l.unit]), [['manopera-zugravit', 'zugraveli', 60, 'm2'], ['manopera-gresie', 'gresieFaianta', 11.5, 'm2'], ['manopera-faianta', 'gresieFaianta', 20, 'm2']]);
  assert.deepEqual(line('manopera-gresie').rooms, [{ roomId: 'baie', name: 'Baie', qty: 7.5 }, { roomId: 'hol', name: 'Hol', qty: 4 }], 'cele două părți din baie se adună');
  assert.deepEqual(line('manopera-gresie').ref, { low: 50, expected: 85 }); assert.deepEqual(line('manopera-zugravit').ref, { low: 31, expected: 38 });
  assert.deepEqual(packageLines(LABOR, ['pardoseli'], NAMES).map(l => l.rateId), ['manopera-plinta']); assert.deepEqual(packageLines(LABOR, ['electric']), []);
  assert.equal(packageLines([lab('manopera-gresie', 'r9', 2, 50, 85)], ['gresieFaianta'])[0]!.rooms[0]!.name, 'r9', 'fără nume de cameră rămâne id-ul');
  assert.deepEqual(checkLines(JSON.parse(JSON.stringify(LINES))), LINES, 'rândurile înghețate trec validarea neschimbate');
  // pagina meseriașului: fără reperul de piață și fără id-urile camerelor
  const pub = publicLines(LINES); assert.ok(!JSON.stringify(pub).includes('ref') && !JSON.stringify(pub).includes('roomId')); assert.deepEqual(rowOf(pub, 'manopera-gresie').rooms, [{ name: 'Baie', qty: 7.5 }, { name: 'Hol', qty: 4 }]);
  for (const x of [null, [], [{ ...LINES[0], rateId: 'a b' }], [{ ...LINES[0], qty: 0 }], [LINES[0], LINES[0]], [{ ...LINES[0], trade: 'x' }], [{ ...LINES[0], ref: null }], [{ ...LINES[0], rateId: '__proto__' }]]) bad(() => checkLines(x));
});

test('validarea ofertei: strictă; ciorna poate fi parțială, trimiterea cere fiecare rând cu preț sau „nu ofertez” și toți termenii', () => {
  const all = { 'manopera-gresie': 90, 'manopera-faianta': 80, 'manopera-zugravit': 30 };
  assert.deepEqual(checkOffer({}, LINES, { partial: true }), { v: 1, prices: {}, inclusions: {}, company: {} });
  assert.deepEqual(checkOffer({ prices: { 'manopera-gresie': 90.004 }, company: { cui: ' ro 123 456 ' } }, LINES, { partial: true }), { v: 1, prices: { 'manopera-gresie': 90 }, inclusions: {}, company: { cui: 'RO123456' } });
  const ok = checkOffer(full(all, true, { notes: ' fără  <b>materiale</b>\r\nrând 2 ', negotiatedTotal: 3000, company: { form: 'srl', vatPayer: true, name: ' Faianțari  SRL ', cui: '123456' } }), LINES);
  assert.equal(ok.notes, 'fără b materiale /b \nrând 2'); assert.equal(ok.company.name, 'Faianțari SRL'); assert.equal(ok.negotiatedTotal, 3000);
  // strict: câmp necunoscut, rând din afara cererii, preț 0 / negativ / text, mod necunoscut, sumă pe „inclus”, CUI cu litere, dată imposibilă
  for (const x of [null, [], { extra: 1 }, { v: 2 }, { prices: { altceva: 10 } }, { prices: { 'manopera-gresie': 0 } }, { prices: { 'manopera-gresie': -5 } }, { prices: { 'manopera-gresie': '90' } }, { prices: { 'manopera-gresie': 2e6 } },
    { inclusions: { debris: { mode: 'poate' } } }, { inclusions: { altele: { mode: 'included' } } }, { inclusions: { debris: { mode: 'included', amount: 5 } } }, { inclusions: { debris: { mode: 'separate', amount: 0 } } },
    { company: { form: 'sa' } }, { company: { vatPayer: 'da' } }, { company: { cui: 'RO12AB' } }, { company: { iban: 'x' } }, { advancePct: 101 }, { startDate: '2026-02-30' }, { durationDays: 1.5 }, { durationDays: 0 },
    { warrantyMonths: -1 }, { validDays: 0 }, { negotiatedTotal: 0 }, { notes: 'x'.repeat(1001) }, JSON.parse('{"prices":{"__proto__":5}}')])
    bad(() => checkOffer(x, LINES, { partial: true }));
  // completă: lipsurile sunt numite
  assert.throws(() => checkOffer({ prices: { 'manopera-gresie': 90 } }, LINES), (e: any) => e instanceof RfqError && ['price:manopera-faianta', 'inclusion:debris', 'vatPayer', 'startDate'].every(k => (e.missing ?? []).includes(k)));
  assert.deepEqual(offerMissing(full(all), LINES), []);
  assert.deepEqual(offerMissing(full({ 'manopera-gresie': null, 'manopera-faianta': null, 'manopera-zugravit': null }), LINES), ['prices'], 'o ofertă fără niciun preț nu e ofertă');
  bad(() => checkOffer({ ...full(all), inclusions: { ...INC, debris: { mode: 'separate' } } }, LINES), /preț separat/);
  assert.equal(checkOffer({ ...full(all), inclusions: { ...INC, debris: { mode: 'separate' } } }, LINES, { partial: true }).inclusions.debris!.amount, undefined, 'ciorna acceptă suma încă necompletată');
  // „nu ofertez” explicit → parțială; rând necompletat → incompletă
  assert.deepEqual(offerCoverage(full({ ...all, 'manopera-zugravit': null }), LINES), { priced: ['manopera-gresie', 'manopera-faianta'], declined: ['manopera-zugravit'], missing: [], complete: false, partial: true });
  assert.equal(offerCoverage(full({ 'manopera-gresie': 90 }), LINES).complete, false); assert.equal(offerCoverage(full({ 'manopera-gresie': 90 }), LINES).partial, false); assert.equal(offerCoverage(full(all), LINES).complete, true);
});

test('totalurile ofertei: fără TVA, incluziuni cu preț separat, TVA doar la plătitor', () => {
  const o = full({ 'manopera-gresie': 100, 'manopera-faianta': 80, 'manopera-zugravit': 30 }, true, { inclusions: { ...INC, debris: { mode: 'separate', amount: 500 }, stairs: { mode: 'excluded' } } });
  // 11,5 × 100 + 20 × 80 + 60 × 30 = 4550; + 500 separat; TVA 21% pe 5050 = 1060,50
  assert.deepEqual(offerTotals(o, LINES), { net: 4550, separate: 500, vat: 1060.5, gross: 6110.5 });
  assert.deepEqual(offerTotals({ ...o, company: { form: 'pfa', vatPayer: false } }, LINES), { net: 4550, separate: 500, vat: 0, gross: 5050 });
  assert.deepEqual(offerTotals(checkOffer({}, LINES, { partial: true }), LINES), { net: 0, separate: 0, vat: 0, gross: 0 });
});

test('comparare: baza „persoană fizică” pune TVA-ul plătitorului în total, baza „firmă” compară fără TVA; minimul pe rând; clasament doar între ofertele complete', () => {
  const srl = full({ 'manopera-gresie': 80, 'manopera-faianta': 70, 'manopera-zugravit': 30 }, true), pfa = full({ 'manopera-gresie': 90, 'manopera-faianta': 80, 'manopera-zugravit': 35 }, false);
  const offers = [{ id: 'a', label: 'SRL plătitor', offer: srl }, { id: 'b', label: 'PFA neplătitor', offer: pfa }];
  // net: SRL 80×11,5 + 70×20 + 30×60 = 4120; PFA 90×11,5 + 80×20 + 35×60 = 4735
  const ind = compareOffers(LINES, offers, 'individual'), com = compareOffers(LINES, offers, 'company');
  assert.deepEqual(ind.offers.map(o => [o.id, o.net, o.vat, o.gross, o.total, o.rank]), [['a', 4120, 865.2, 4985.2, 4985.2, 2], ['b', 4735, 0, 4735, 4735, 1]], 'cu TVA, neplătitorul e mai ieftin pentru o persoană fizică');
  assert.deepEqual(com.offers.map(o => [o.id, o.total, o.rank]), [['a', 4120, 1], ['b', 4735, 2]], 'fără TVA, plătitorul e mai ieftin pentru o firmă');
  // pe rând: prețul unitar pe bază și minimul
  const g = rowOf(ind.rows, 'manopera-gresie'), gc = rowOf(com.rows, 'manopera-gresie'); assert.equal(g.cells.a!.unit, 80); assert.equal(g.cells.a!.basisUnit, 96.8); assert.equal(g.cells.b!.basisUnit, 90); assert.equal(g.min, 90); assert.deepEqual([g.cells.a!.min, g.cells.b!.min], [false, true]);
  assert.equal(g.cells.a!.value, 1113.2); assert.deepEqual([gc.cells.a!.min, gc.cells.b!.min], [true, false]);
  // abaterea față de reper folosește prețul cu TVA al plătitorului (ce plătește o persoană fizică), indiferent de baza aleasă
  assert.equal(g.cells.a!.refPct, gc.cells.a!.refPct); assert.equal(g.cells.a!.refPct, 13.88); assert.equal(g.cells.b!.refPct, 5.88);
  // o singură ofertă cu preț pe rând: nu există „minim” de evidențiat
  assert.ok(compareOffers(LINES, [offers[0]!], 'individual').rows.every(r => r.cells.a!.min === false));
  // egalitate la total: același loc
  assert.deepEqual(compareOffers(LINES, [{ id: 'x', label: 'X', offer: pfa }, { id: 'y', label: 'Y', offer: pfa }], 'company').offers.map(o => o.rank), [1, 1]);
});

test('oferta incompletă sau parțială nu intră în clasament, chiar dacă totalul ei e mai mic', () => {
  const complete = full({ 'manopera-gresie': 90, 'manopera-faianta': 80, 'manopera-zugravit': 35 }), partial = full({ 'manopera-gresie': 90, 'manopera-faianta': 80, 'manopera-zugravit': null });
  const incomplete = { ...full({ 'manopera-gresie': 60 }) };
  const r = compareOffers(LINES, [{ id: 'c', label: 'Completă', offer: complete }, { id: 'p', label: 'Parțială', offer: partial }, { id: 'i', label: 'Incompletă', offer: incomplete }], 'individual');
  const by = Object.fromEntries(r.offers.map(o => [o.id, o]));
  assert.ok(by.p!.total < by.c!.total && by.i!.total < by.c!.total, 'totalurile lor sunt mai mici doar pentru că lipsesc rânduri');
  assert.deepEqual([by.c!.rank, by.p!.rank, by.i!.rank], [1, null, null]);
  assert.deepEqual([by.c!.complete, by.p!.complete, by.p!.partial, by.i!.complete, by.i!.partial], [true, false, true, false, false]);
  assert.deepEqual(by.p!.declined, ['manopera-zugravit']); assert.deepEqual(by.i!.missing, ['manopera-zugravit', 'manopera-faianta']);
  assert.deepEqual(by.p!.warnings, [{ code: 'PARTIAL', n: 1 }]); assert.deepEqual(by.i!.warnings[0], { code: 'INCOMPLETE', n: 2 });
  const z = rowOf(r.rows, 'manopera-zugravit'); assert.deepEqual([z.cells.p!.declined, z.cells.p!.unit, z.cells.i!.declined, z.cells.i!.unit], [true, null, false, null]); assert.equal(z.cells.c!.min, false, 'un singur preț pe rând');
});

test('mult sub piață: peste 40% sub tariful așteptat (și sub minimul pieței) → avertisment „risc de suplimentări”, pe prețul cu TVA', () => {
  // gresie: reper 50 / 85 → prag 51 și 50; faianță: 38 / 75 → prag 45 și 38; zugrăvit: 31 / 38 → prag 22,8
  const o = (g: number, f: number, z: number, vat = false) => compareOffers(LINES, [{ id: 'a', label: 'A', offer: full({ 'manopera-gresie': g, 'manopera-faianta': f, 'manopera-zugravit': z }, vat) }], 'individual');
  assert.deepEqual(o(49, 37, 22).offers[0]!.warnings, [{ code: 'BELOW_MARKET', rateId: 'manopera-zugravit', pct: 42 }, { code: 'BELOW_MARKET', rateId: 'manopera-gresie', pct: 42 }, { code: 'BELOW_MARKET', rateId: 'manopera-faianta', pct: 51 }]);
  assert.deepEqual(o(50, 40, 23).offers[0]!.warnings, [], 'la limita pieței nu e avertisment (faianța la 40 e sub 60% din așteptat, dar peste minimul pieței)');
  assert.equal(rowOf(o(49, 37, 22).rows, 'manopera-gresie').cells.a!.below, true); assert.equal(rowOf(o(50, 40, 23).rows, 'manopera-faianta').cells.a!.below, false);
  // plătitor de TVA: 42 fără TVA = 50,82 cu TVA → nu mai e sub prag
  assert.deepEqual(o(42, 60, 30, true).offers[0]!.warnings, []); assert.equal(o(41, 60, 30, true).offers[0]!.warnings.length, 1);
});

test('incluziuni: sumele „cu preț separat” intră în totalul comparabil; cele excluse apar ca avertisment; totalul negociat e separat', () => {
  const prices = { 'manopera-gresie': 90, 'manopera-faianta': 80, 'manopera-zugravit': 35 };
  const a = full(prices, false, { inclusions: { ...INC, debris: { mode: 'separate', amount: 400 }, transport: { mode: 'separate', amount: 150 }, stairs: { mode: 'excluded' } }, negotiatedTotal: 5000 }), b = full(prices, true);
  const r = compareOffers(LINES, [{ id: 'a', label: 'A', offer: a }, { id: 'b', label: 'B', offer: b }], 'individual'), [x, y] = r.offers;
  assert.equal(x!.net, 4735); assert.equal(x!.separate, 550); assert.equal(x!.total, 5285, 'rânduri + incluziuni separate'); assert.deepEqual(x!.excluded, ['stairs']);
  assert.deepEqual(x!.warnings, [{ code: 'EXCLUDED', key: 'stairs' }]); assert.equal(x!.negotiated, 5000); assert.equal(x!.rank, 1, 'clasamentul folosește totalul pe rânduri, nu suma negociată');
  assert.equal(y!.total, 5729.35); assert.equal(y!.negotiated, null); assert.equal(y!.rank, 2);
  assert.equal(compareOffers(LINES, [{ id: 'b', label: 'B', offer: { ...b, negotiatedTotal: 5000 } }], 'individual').offers[0]!.negotiated, 6050, 'totalul negociat al plătitorului primește TVA pe baza „persoană fizică”');
});

test('prețurile contractate: doar rândurile cu preț; citire tolerantă; costul adaugă TVA doar la plătitor', () => {
  const RID = '11111111-1111-4111-8111-111111111111', at = '2026-11-22T10:00:00.000Z';
  const c = contractFromOffer(LINES, full({ 'manopera-gresie': 90, 'manopera-faianta': null, 'manopera-zugravit': 35 }, true), ' Echipa  <Ion> ', RID, at);
  assert.deepEqual(c, { 'manopera-gresie': { unitPrice: 90, vatPayer: true, label: 'Echipa Ion', at, requestId: RID }, 'manopera-zugravit': { unitPrice: 35, vatPayer: true, label: 'Echipa Ion', at, requestId: RID } });
  assert.deepEqual(sanitizeLaborContract(c), c); assert.equal(sanitizeLaborContract(null), undefined); assert.equal(sanitizeLaborContract({}), undefined); assert.equal(sanitizeLaborContract([c]), undefined);
  assert.deepEqual(sanitizeLaborContract({ ...c, 'a b': c['manopera-gresie'], x: { ...c['manopera-gresie'], unitPrice: 0 }, y: { ...c['manopera-gresie'], requestId: 'nu' }, z: { ...c['manopera-gresie'], vatPayer: 'da' }, w: { ...c['manopera-gresie'], label: '' }, q: { ...c['manopera-gresie'], at: 'ieri' } }), c);
  assert.equal(contractCost({ unitPrice: 90, vatPayer: true }, 10), 1089); assert.equal(contractCost({ unitPrice: 90, vatPayer: false }, 10), 900);
});

test('devizul: fără prețuri contractate raportul de profit e neschimbat; cu ele se schimbă doar costul manoperei, nu prețul către client', () => {
  const budget: any = { settings: { includeLabor: true, laborScenario: 'expected' }, vatRate: .21, items: [], extras: [],
    labor: [{ key: 'baie:manopera-gresie', rateId: 'manopera-gresie', label: 'Montaj gresie', qty: 10, unit: 'm2', low: 500, expected: 850, high: 1250 }, { key: 'living:manopera-zugravit', rateId: 'manopera-zugravit', label: 'Zugrăvit', qty: 60, unit: 'm2', low: 1860, expected: 2280, high: 2760 }] };
  const d = { defaults: {}, suppliers: {} }, plain = commercialSources(budget), same = commercialSources(budget, [], {}), none = commercialSources(budget, [], null);
  assert.deepEqual(same, plain); assert.deepEqual(none, plain); assert.ok(plain.labor.every(l => !('costTotal' in l) && !('contract' in l)));
  const r0 = profitReport(d, plain.items, plain.labor, 1000, 0);
  assert.equal(r0.labor, 3130); assert.equal(r0.laborCost, 3130, 'fără contract: cost = preț client'); assert.deepEqual(r0.totals, { cost: 3130, client: 4130, profit: 1000, marginPct: 24.21 });
  // gresia contractată la 70 lei/m² de un plătitor de TVA: cost 10 × 70 × 1,21 = 847 (față de 850 estimat)
  const con = commercialSources(budget, [], { 'manopera-gresie': { unitPrice: 70, vatPayer: true, label: 'Echipa Ion', at: '2026-11-22', requestId: 'x' } });
  assert.deepEqual(con.labor[0], { ...plain.labor[0], costTotal: 847, contract: { unitPrice: 70, vatPayer: true, label: 'Echipa Ion' } }); assert.deepEqual(con.labor[1], plain.labor[1]);
  const r1 = profitReport(d, con.items, con.labor, 1000, 0);
  assert.equal(r1.labor, 3130, 'prețul către client nu se schimbă'); assert.equal(r1.laborCost, 3127); assert.deepEqual(r1.totals, { cost: 3127, client: 4130, profit: 1003, marginPct: 24.29 });
  // un preț contractat peste estimare apare ca pierdere, nu e ascuns
  const over = commercialSources(budget, [], { 'manopera-gresie': { unitPrice: 100, vatPayer: false, label: 'E', at: '2026-11-22', requestId: 'x' } });
  assert.equal(profitReport(d, over.items, over.labor, 1000, 0).totals.profit, 850);
  // manopera scoasă din deviz: contractul nu aduce nimic
  assert.deepEqual(commercialSources({ ...budget, settings: { ...budget.settings, includeLabor: false } }, [], { 'manopera-gresie': { unitPrice: 70, vatPayer: true, label: 'E', at: '2026-11-22', requestId: 'x' } }).labor, []);
});
