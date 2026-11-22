// Lista de debitare a bucătăriei: piesele și cotele fiecărui fel de corp, suspendatele, cantul, feroneria, validarea
// parametrilor, CSV-ul pentru atelier și așezarea pe plăci.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { kitchenCutList, kitchenCutCsv, checkKitchenCut, kitchenCutOptions, kitchenCutDiff, kitchenMaterialLabel, csvCell, KITCHEN_CUT_DEFAULT, KITCHEN_CSV_CONVENTION, type KitchenCutPart } from '../core/kitchen-cut';
import { sanitizeKitchen } from '../core/kitchen';
import { defaultModules, type KitchenModule } from '../core/kitchen-modules';
import { layoutCutParts, isGuillotine } from '../core/sheet-layout';
import type { KitchenSpec } from '../core/types';

const NONE: KitchenSpec = { upper: 'none', handle: 'bar' }, M = (type: KitchenModule['type'], w: number): KitchenModule => ({ type, w });
const of = (parts: KitchenCutPart[], group: string) => parts.filter(p => p.group === group).map(p => [p.kind, p.l, p.w, p.qty, p.material, p.edges.join('')]);
const hwOf = (r: ReturnType<typeof kitchenCutList>) => Object.fromEntries(r.hardware.map(h => [h.key, h.qty]));

test('corp de jos de 60: laterale, fund, traverse, raft, spate HDF, front — cote exacte', () => {
  const r = kitchenCutList([M('base', 60)], { ...NONE }, { plinth: 0 });
  assert.deepEqual(of(r.parts, 'J1'), [
    ['side', 720, 560, 2, 'pal', '1000'], ['bottom', 564, 560, 1, 'pal', '1000'], ['rail', 564, 100, 2, 'pal', '1000'], ['shelf', 562, 540, 1, 'pal', '1000'],
    ['back', 716, 596, 1, 'hdf', '0000'], ['door', 717, 597, 1, 'front', '1111']]);
  assert.equal(r.groups[0]!.label, 'J1 Corp 60'); assert.ok(r.parts.every(p => p.module === 'J1 Corp 60'));
  assert.deepEqual(r.parts.map(p => p.grain), [true, true, true, true, false, true], 'HDF-ul nu are fibră');
  assert.deepEqual(hwOf(r), { hinge: 2, leg: 4, shelfPin: 4, handle: 1, confirmat: 12 });
  // arii și cant: carcasa doar pe muchia din față, frontul pe tot conturul
  assert.equal(r.areas.pal, Math.round((720 * 560 * 2 + 564 * 560 + 564 * 100 * 2 + 562 * 540) / 1e4) / 100); assert.equal(r.areas.hdf, .43); assert.equal(r.areas.front, .43);
  assert.deepEqual(r.edgeM, [{ mm: .4, m: Math.round((720 * 2 + 564 + 564 * 2 + 562) / 10) / 100 }, { mm: 2, m: Math.round(2 * (717 + 597) / 10) / 100 }]);
  assert.deepEqual(r.notes.map(n => n.code), ['verify']); assert.deepEqual(r.small, []);
});

test('două uși peste 60 cm; chiuveta fără raft și fără spate; mașina de vase doar front', () => {
  const r = kitchenCutList([M('base', 80), M('sink', 80), M('dishwasher', 60), M('base', 60.1)], NONE, { plinth: 0 });
  assert.deepEqual(of(r.parts, 'J1').find(p => p[0] === 'door'), ['door', 717, 397, 2, 'front', '1111']);
  assert.deepEqual(of(r.parts, 'J2'), [['side', 720, 560, 2, 'pal', '1000'], ['bottom', 764, 560, 1, 'pal', '1000'], ['rail', 764, 100, 2, 'pal', '1000'], ['door', 717, 397, 2, 'front', '1111']]);
  assert.ok(r.notes.some(n => n.code === 'sinkNoBack' && n.group === 'J2' && n.text.startsWith('J2 Chiuvetă 80: ')));
  assert.deepEqual(of(r.parts, 'J3'), [['applianceFront', 717, 597, 1, 'front', '1111']]); assert.ok(r.notes.some(n => n.code === 'dishwasher' && n.group === 'J3'));
  assert.equal(of(r.parts, 'J4').find(p => p[0] === 'door')![3], 2, '601 mm: deja două uși'); assert.equal(r.groups[3]!.label, 'J4 Corp 60,1');
  assert.equal(hwOf(r).leg, 12, 'mașina de vase nu are picioare de corp'); assert.equal(hwOf(r).hinge, 2 * 2 * 3); assert.equal(hwOf(r).handle, 7);
});

test('sertare, plită (2 sertare), cuptor (nișă + un sertar), colț orb, completare', () => {
  const r = kitchenCutList([M('drawers', 60), M('hob', 60), M('oven', 60), M('corner', 90), M('filler', 5), M('filler', 3), M('base', 15)], NONE, { plinth: 0 });
  assert.deepEqual(of(r.parts, 'J1').slice(-2), [['back', 716, 596, 1, 'hdf', '0000'], ['drawerFront', 597, 237, 3, 'front', '1111']]); assert.ok(!of(r.parts, 'J1').some(p => p[0] === 'shelf'));
  assert.deepEqual(of(r.parts, 'J2').at(-1), ['drawerFront', 597, 357, 2, 'front', '1111']);
  assert.deepEqual(of(r.parts, 'J3'), [['side', 720, 560, 2, 'pal', '1000'], ['bottom', 564, 560, 1, 'pal', '1000'], ['rail', 564, 100, 2, 'pal', '1000'], ['fixedShelf', 564, 560, 1, 'pal', '1000'], ['drawerFront', 597, 122, 1, 'front', '1111']]);
  assert.deepEqual(of(r.parts, 'J4').slice(-2), [['door', 717, 447, 1, 'front', '1111'], ['blind', 717, 447, 1, 'front', '1111']]); assert.equal(of(r.parts, 'J4')[1]![1], 864, 'carcasa colțului pe toată lățimea');
  assert.deepEqual(of(r.parts, 'J5'), [['filler', 717, 50, 1, 'front', '1111']]);
  assert.deepEqual(r.notes.map(n => n.code), ['oven', 'cornerBlind', 'tooNarrow', 'verify']); assert.deepEqual(of(r.parts, 'J7'), [], 'corp de 15 cm: fără piese, cu avertisment');
  assert.deepEqual(hwOf(r), { hinge: 2, runner: 6, drawerBox: 6, leg: 16, shelfPin: 4, handle: 7, confirmat: 12 * 3 + 16 });
  assert.deepEqual(r.small, [r.parts.findIndex(p => p.group === 'J6') + 1], 'banda de 30 mm e sub minimul atelierului (50 mm), cea de 50 nu');
  assert.deepEqual(kitchenCutList([M('filler', 3)], NONE, { plinth: 0, minPartMm: 0 }).small, []);
});

test('corp înalt și frigider: capac, rafturi, două fronturi suprapuse; soclul pe toată lungimea', () => {
  const r = kitchenCutList([M('tall', 60), M('fridge', 60), M('tall', 90)], NONE);
  assert.deepEqual(of(r.parts, 'J1'), [['side', 2100, 560, 2, 'pal', '1000'], ['bottom', 564, 560, 1, 'pal', '1000'], ['top', 564, 560, 1, 'pal', '1000'], ['shelf', 562, 540, 4, 'pal', '1000'],
    ['back', 2096, 596, 1, 'hdf', '0000'], ['door', 717, 597, 1, 'front', '1111'], ['door', 1377, 597, 1, 'front', '1111']]);
  assert.deepEqual(of(r.parts, 'J2').map(p => p[0]), ['side', 'bottom', 'top', 'fixedShelf', 'door', 'door'], 'frigiderul nu are spate și nici rafturi mobile');
  assert.deepEqual(of(r.parts, 'J3').filter(p => p[0] === 'door').map(p => [p[2], p[3]]), [[447, 2], [447, 2]]);
  assert.deepEqual(of(r.parts, 'SOC'), [['plinth', 2100, 100, 1, 'pal', '1000']]); assert.equal(hwOf(r).plinthClip, 4);
  assert.equal(hwOf(r).hinge, (2 + 3) + (2 + 3) + 2 * (2 + 3)); assert.equal(hwOf(r).shelfPin, 32); assert.ok(!r.hardware.some(h => h.key === 'hanger'));
  // soclu lung: se împarte în bucăți egale care încap pe placă
  const long = kitchenCutList(Array.from({ length: 7 }, () => M('base', 80)), NONE); assert.deepEqual(of(long.parts, 'SOC'), [['plinth', 1867, 100, 3, 'pal', '1000']]);
});

test('corpurile suspendate: doar când există, nu deasupra plitei și a corpurilor înalte; deschise = fără uși', () => {
  const mods = [M('base', 60), M('hob', 60), M('sink', 80), M('fridge', 60), M('corner', 90), M('filler', 5)];
  assert.ok(!kitchenCutList(mods, NONE).groups.some(g => g.row === 'upper'));
  const c = kitchenCutList(mods, { upper: 'closed' }, { plinth: 0 });
  assert.deepEqual(c.groups.filter(g => g.row === 'upper').map(g => g.label), ['S1 Suspendat 60', 'S2 Suspendat 60', 'S3 Suspendat 80', 'S5 Suspendat 90', 'S6 Suspendat 5']);
  assert.deepEqual(of(c.parts, 'S1'), [['side', 720, 320, 2, 'pal', '1000'], ['bottom', 564, 320, 1, 'pal', '1000'], ['top', 564, 320, 1, 'pal', '1000'], ['shelf', 562, 300, 1, 'pal', '1000'],
    ['back', 716, 596, 1, 'hdf', '0000'], ['door', 717, 597, 1, 'front', '1111']]);
  assert.deepEqual(of(c.parts, 'S2'), []); assert.ok(c.notes.some(n => n.code === 'hood' && n.group === 'S2'), 'deasupra plitei: hotă');
  assert.deepEqual(of(c.parts, 'S3').at(-1), ['door', 717, 397, 2, 'front', '1111']); assert.ok(of(c.parts, 'S3').some(p => p[0] === 'back'), 'suspendatul de peste chiuvetă are spate');
  assert.deepEqual(of(c.parts, 'S5').slice(-2).map(p => p[0]), ['door', 'blind']); assert.deepEqual(of(c.parts, 'S6'), [['filler', 717, 50, 1, 'front', '1111']]);
  assert.equal(hwOf(c).hanger, 6, '2 suporți pe fiecare din cele 3 corpuri suspendate');
  const o = kitchenCutList(mods, { upper: 'open' }, { plinth: 0, upperH: 600, upperD: 300 }), up = o.parts.filter(p => p.group.startsWith('S'));
  assert.ok(!up.some(p => p.material === 'front'), 'deschise: niciun front sus'); assert.ok(up.filter(p => p.kind === 'shelf').every(p => p.edges[0] === 1 && p.w === 280), 'rafturile au cant pe față');
  assert.deepEqual(of(o.parts, 'S1')[0], ['side', 600, 300, 2, 'pal', '1000']); assert.deepEqual(of(o.parts, 'S6'), [['filler', 600, 50, 1, 'pal', '1111']]);
  assert.equal(kitchenCutList(mods, {}, { plinth: 0 }).groups.filter(g => g.row === 'upper').length, 5, 'implicit: suspendate deschise');
});

test('mânere după alegerea din bucătărie; fronturi din MDF vopsit: fără cant și fără fibră', () => {
  const mods = [M('base', 60), M('drawers', 80)];
  assert.equal(hwOf(kitchenCutList(mods, { upper: 'none', handle: 'knob' })).handle, 4); assert.equal(kitchenCutList(mods, { upper: 'none', handle: 'knob' }).hardware.find(h => h.key === 'handle')!.name, 'Butoni');
  const gola = kitchenCutList(mods, { upper: 'none', handle: 'profile' }); assert.equal(hwOf(gola).gola, Math.round((597 + 3 * 797) / 100) / 10); assert.equal(gola.hardware.find(h => h.key === 'gola')!.unit, 'm'); assert.ok(!('handle' in hwOf(gola)));
  const push = kitchenCutList(mods, { upper: 'none', handle: 'none' }); assert.equal(hwOf(push).push, 4); assert.ok(!('handle' in hwOf(push)) && !('gola' in hwOf(push)));
  const mdf = kitchenCutList(mods, NONE, { frontMaterial: 'mdf', edgeCarcass: 1 }), fr = mdf.parts.filter(p => p.material === 'front');
  assert.ok(fr.length > 0 && fr.every(p => p.edges.join('') === '0000' && !p.grain)); assert.deepEqual(mdf.edgeM.map(e => e.mm), [1]); assert.ok(mdf.notes.some(n => n.code === 'mdf'));
  assert.match(kitchenMaterialLabel('front', { frontColor: '#112233', frontFinish: 'gloss' }, mdf.options), /^Front MDF 18 mm vopsit #112233 lucios$/);
  // aceeași grosime de cant pe carcasă și fronturi: un singur total
  const same = kitchenCutList(mods, NONE, { edgeCarcass: 2 }); assert.equal(same.edgeM.length, 1); assert.equal(same.edgeM[0]!.mm, 2);
});

test('parametrii: implicit + ce diferă; validare strictă cu mesaje în română', () => {
  assert.deepEqual(kitchenCutOptions(undefined), KITCHEN_CUT_DEFAULT); assert.equal(kitchenCutOptions({ depth: 520 }).depth, 520); assert.equal(kitchenCutOptions({ depth: 520 }).carcassH, 720);
  assert.equal(kitchenCutDiff(KITCHEN_CUT_DEFAULT), undefined); assert.deepEqual(kitchenCutDiff({ ...KITCHEN_CUT_DEFAULT, kerf: 3.2, plinth: 150 }), { plinth: 150, kerf: 3.2 });
  assert.deepEqual(checkKitchenCut({}), {}); assert.deepEqual(checkKitchenCut({ depth: 519.6, kerf: 3.24, frontMaterial: 'mdf', edgeFront: 1, plinth: undefined }), { depth: 520, kerf: 3.2, frontMaterial: 'mdf', edgeFront: 1 });
  const bad: [unknown, RegExp][] = [[null, /invalizi/], [[], /invalizi/], ['x', /invalizi/], [{ carcassH: 500 }, /Înălțimea corpului de jos: între 600 și 900 mm/], [{ depth: '560' }, /Adâncimea/], [{ depth: NaN }, /Adâncimea/],
    [{ plinth: -1 }, /Soclul/], [{ upperD: 500 }, /suspendat/], [{ tallH: 2500 }, /înalt/], [{ edgeCarcass: .5 }, /0,4, 1 sau 2 mm/], [{ edgeFront: '2' }, /cantului/], [{ frontMaterial: 'sticla' }, /PAL sau MDF/],
    [{ kerf: 12 }, /tăieturii: între 0 și 8 mm/], [{ trim: 50 }, /Tunderea/], [{ sheetL: 500 }, /Lungimea plăcii/], [{ sheetW: 3000 }, /Lățimea plăcii/], [{ minPartMm: 999 }, /Latura minimă/], [{ altceva: 1 }, /necunoscut: altceva/],
    [{ __proto__: null, constructor: 1 }, /necunoscut/], [{ carcassH: 900, tallH: 1400 }, /Înălțimea corpului înalt/], [{ sheetL: 1000, sheetW: 2000 }, /latura mare/]];
  for (const [x, re] of bad) assert.throws(() => checkKitchenCut(x), re, JSON.stringify(x));
  // prin validarea bucătăriei: valorile bune rămân (normalizate), obiectul gol dispare, cele greșite opresc salvarea cu mesajul lor
  const k: any = { upper: 'closed', cut: { depth: 519.6 } }; assert.equal(sanitizeKitchen(k), null); assert.deepEqual(k.cut, { depth: 520 });
  const e: any = { cut: {} }; assert.equal(sanitizeKitchen(e), null); assert.ok(!('cut' in e));
  assert.match(sanitizeKitchen({ cut: { depth: 10 } })!, /Adâncimea corpului de jos/); assert.match(sanitizeKitchen({ cut: 5 })!, /invalizi/);
});

test('CSV pentru atelier: BOM, separator „;”, convenția declarată în antet, coloanele cerute, escape corect', () => {
  const k: KitchenSpec = { upper: 'none', frontColor: '#f4f4f1', frontFinish: 'matt' }, r = kitchenCutList([M('base', 60), M('sink', 80)], k, { plinth: 0 });
  const csv = kitchenCutCsv(r, k, 'Casa "Popescu"; etaj 1'); assert.equal(csv.charCodeAt(0), 0xFEFF); assert.ok(csv.endsWith('\r\n'));
  const lines = csv.slice(1).trimEnd().split('\r\n'); assert.equal(lines.length, 4 + 1 + r.parts.length);
  assert.equal(lines[0], '"Lista de debitare bucătărie — Casa ""Popescu""; etaj 1"');
  assert.equal(lines[1], `"${KITCHEN_CSV_CONVENTION}"`); assert.equal(KITCHEN_CSV_CONVENTION, 'Cote finale, cu cant inclus; L = latura pe fibră; Fibră 1 = nu se rotește');
  assert.match(lines[2]!, /^"Cant carcasă ABS 0,4 mm; cant fronturi ABS 2 mm;/);
  assert.equal(lines[4], 'Nr;Denumire;Material/decor;L (mm);l (mm);Buc;Fibră (1/0);Cant L1;Cant L2;Cant l1;Cant l2;Observații');
  assert.equal(lines[5], '1;Lateral;PAL 18 mm carcasă;720;560;2;1;0,4;;;;J1 Corp 60');
  assert.equal(lines.find(l => l.includes(';Spate HDF;')), '5;Spate HDF;HDF 3 mm;716;596;1;0;;;;;J1 Corp 60');
  assert.equal(lines.find(l => l.includes(';Ușă;')), '6;Ușă;Front PAL 18 mm #f4f4f1 mat;717;597;1;1;2;2;2;2;J1 Corp 60');
  assert.ok(lines.slice(5).every(l => l.split(';').length === 12), 'fiecare piesă are cele 12 coloane');
  // celule: ghilimele dublate, separator și rând nou între ghilimele, formulele Excel dezamorsate, numerele neatinse
  assert.equal(csvCell('a;b'), '"a;b"'); assert.equal(csvCell('zi "x"'), '"zi ""x"""'); assert.equal(csvCell('a\nb'), '"a\nb"'); assert.equal(csvCell('=1+1'), "'=1+1"); assert.equal(csvCell('@x;y'), `"'@x;y"`);
  assert.equal(csvCell(-5), '-5'); assert.equal(csvCell('simplu'), 'simplu');
  assert.match(kitchenCutCsv(r, k, '=HYPERLINK("x")').slice(1), /^"Lista de debitare bucătărie — =HYPERLINK/, 'numele proiectului nu ajunge la începutul celulei');
  assert.match(kitchenCutCsv(kitchenCutList([M('base', 60)], k, { frontMaterial: 'mdf' }), k), /cant fronturi fără \(MDF vopsit\)/);
});

test('propunerea implicită a unei bucătării de 3,6 m: toate piesele încap pe plăci, plan tăiabil cap-coadă', () => {
  const k: KitchenSpec = { upper: 'closed', handle: 'bar' }, r = kitchenCutList(defaultModules(360), k), o = r.options;
  assert.ok(r.parts.length > 30); assert.ok(r.groups.filter(g => g.row === 'base').length === defaultModules(360).length);
  const lay = layoutCutParts(r.parts, o); assert.deepEqual(lay.unplaced, []); assert.deepEqual(Object.keys(lay.stats.sheets).sort(), ['front', 'hdf', 'pal']);
  assert.ok(lay.sheets.every(s => isGuillotine(s, o.kerf)));
  for (const m of ['pal', 'front', 'hdf'] as const) assert.ok(lay.stats.sheets[m]! <= Math.ceil(Math.ceil(r.areas[m] / 5.796) * 1.5), `${m}: ${lay.stats.sheets[m]} plăci la ${r.areas[m]} m²`);
  assert.equal(lay.stats.pieces, r.parts.reduce((a, p) => a + p.qty, 0));
});

test('parametrii de debitare se salvează în proiect odată cu bucătăria; valorile greșite sunt refuzate', async () => {
  process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
  const { resetDbForTests } = await import('../lib/db'); await resetDbForTests(); const repo = await import('../lib/repo');
  const A = '79797979-1111-4111-8111-797979797979', id = await repo.createProject(A, 'Debitare', 'demo'), p = await repo.getProject(A, id), room = p.draft.placements.find(x => x.group === 'bucatarie')!.roomId;
  const withCut = (cut: unknown) => ({ ...p.draft, finishes: { ...(p.draft.finishes || {}), [room]: { ...(p.draft.finishes?.[room] || {}), kitchen: { upper: 'closed', modules: [{ type: 'base', w: 60 }], cut } } } }) as any;
  await repo.saveDraft(A, id, withCut({ depth: 520, kerf: 3.2, frontMaterial: 'mdf' }));
  assert.deepEqual((await repo.getProject(A, id)).draft.finishes![room]!.kitchen!.cut, { depth: 520, kerf: 3.2, frontMaterial: 'mdf' });
  await assert.rejects(repo.saveDraft(A, id, withCut({ depth: 5 })), (e: any) => e.status === 400 && /Adâncimea/.test(e.message));
  await assert.rejects(repo.saveDraft(A, id, withCut({ rm: '-rf' })), (e: any) => e.status === 400);
});
