// CSV pentru produsele proprii: citire, limite, erori pe rând, export sigur, buget.
import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import { parseCustomCsv, parseNumber, customItemsCsv, customTemplateCsv, customBudget, safeCell, CSV_LIMITS, TEMPLATE_HEADER } from '../core/custom-csv';
import { ClipError } from '../core/clip';

const rooms = (n: string) => n.toLowerCase() === 'living' ? 'room-1' : null;
describe('citirea CSV', () => {
  test('separator ; și virgulă zecimală, BOM, antete RO', () => {
    const r = parseCustomCsv('﻿Denumire;Furnizor;Cod;Link;UM;Cantitate;Pret_Achizitie;Pret_Vanzare;Camera;Termen_Zile\r\nCanapea;Atelier Exemplu;C-1;https://exemplu.ro/c;buc;2;1.234,50;2000,5;Living;42\r\n', rooms);
    assert.equal(r.ok, 1); const i = r.rows[0]!.item!;
    assert.deepEqual([i.name, i.supplier, i.sku, i.url, i.unit, i.qty, i.costPrice, i.price, i.roomId, i.leadDays], ['Canapea', 'Atelier Exemplu', 'C-1', 'https://exemplu.ro/c', 'buc', 2, 1234.5, 2000.5, 'room-1', 42]);
  });
  test('separator , cu ghilimele, antete EN, ghilimele duble și rând nou în câmp', () => {
    const r = parseCustomCsv('name,supplier,qty,cost,price\n"Masă, ""extensibilă""","Furn ""X""",3,"10,5",20.25\n"Linie\nnouă",S,1,,\n');
    assert.equal(r.ok, 2); assert.equal(r.rows[0]!.item!.name, 'Masă, "extensibilă"'); assert.equal(r.rows[0]!.item!.supplier, 'Furn "X"');
    assert.equal(r.rows[0]!.item!.costPrice, 10.5); assert.equal(r.rows[0]!.item!.price, 20.25); assert.equal(r.rows[1]!.item!.costPrice, null); assert.equal(r.rows[1]!.item!.name, 'Linie nouă');
  });
  test('antete fără diacritice, majuscule, aliasuri; coloane necunoscute ignorate; cantitate goală = 1, UM goală = buc', () => {
    const r = parseCustomCsv('NUME;FURNIZOR;Preț vânzare;Preț achiziție;Termen zile;altceva\nX;Y;5;3;7;zzz');
    assert.deepEqual(r.columns.sort(), ['costPrice', 'leadDays', 'name', 'price', 'supplier']); const i = r.rows[0]!.item!;
    assert.equal(i.qty, 1); assert.equal(i.unit, 'buc'); assert.equal(i.price, 5); assert.equal(i.costPrice, 3); assert.equal(i.leadDays, 7);
  });
  test('numere', () => { assert.equal(parseNumber('1.234,56'), 1234.56); assert.equal(parseNumber('1,234.56'), 1234.56); assert.equal(parseNumber('12,5'), 12.5); assert.equal(parseNumber('12.5'), 12.5); assert.equal(parseNumber(' '), null); assert.ok(Number.isNaN(parseNumber('abc'))); assert.ok(Number.isNaN(parseNumber('1e5'))); });
  test('antet fără denumire sau furnizor, CSV gol, ghilimele neînchise', () => {
    assert.throws(() => parseCustomCsv('cod;um\n1;buc'), ClipError); assert.throws(() => parseCustomCsv(''), ClipError); assert.throws(() => parseCustomCsv('﻿\n\n'), ClipError); assert.throws(() => parseCustomCsv('name;supplier\n"x;y'), ClipError);
  });
  test('erori pe rând, cu numărul rândului din fișier', () => {
    const r = parseCustomCsv('name;supplier;url;qty;cost;price;room;lead_days\nA;S;ftp://x.ro;1;;;;\nB;S;http://ok.ro;0;;;;\nC;S;;1;abc;;;\nD;S;;1;;-5;;\nE;S;;1;;;Inexistenta;\nF;S;;1;;;;400\n;S;;1;;;;\nG;S;javascript:alert(1);1;;;;\nH;S;https://ok.ro;2;1;2;Living;9', rooms);
    assert.deepEqual(r.rows.map(x => x.line), [2, 3, 4, 5, 6, 7, 8, 9, 10]);
    assert.deepEqual(r.rows.map(x => x.errors.length > 0), [true, true, true, true, true, true, true, true, false]);
    assert.equal(r.ok, 1); assert.equal(r.failed, 8); assert.match(r.rows[4]!.errors[0]!, /Inexistenta/); assert.match(r.rows[0]!.errors[0]!, /http/); assert.equal(r.rows[1]!.item, null);
    assert.equal(r.rows[2]!.errors[0], 'Prețul de achiziție nu e număr.');
  });
  test('limite: 500 de rânduri, 200 KB', () => {
    const rows = (n: number) => 'name;supplier\n' + Array.from({ length: n }, (_, i) => `P${i};S`).join('\n');
    assert.equal(parseCustomCsv(rows(CSV_LIMITS.rows)).ok, 500); assert.throws(() => parseCustomCsv(rows(501)), /500/);
    assert.throws(() => parseCustomCsv('name;supplier\n' + 'x;'.padEnd(CSV_LIMITS.bytes + 1, 'y')), /KB/);
    assert.equal(parseCustomCsv('name;supplier\nA;' + 'ă'.repeat(10)).ok, 1);
    assert.throws(() => parseCustomCsv('name;supplier\nA;' + 'ă'.repeat(CSV_LIMITS.bytes / 2)), /KB/, 'contează octeții, nu caracterele');
  });
});
describe('export', () => {
  test('celulele care încep cu = + - @ primesc apostrof', () => {
    for (const c of ['=1+1', '+cmd', '-2', '@SUM(A1)', '\tx', '\rx']) assert.equal(safeCell(c), "'" + c);
    assert.equal(safeCell('normal'), 'normal'); assert.equal(safeCell(null), '');
    const base = { url: null, image: null, sku: null, price: 10.5, currency: null, qty: 2, unit: 'buc', roomId: null, costPrice: 7, leadDays: 14, dimension: null };
    const csv = customItemsCsv([{ ...base, name: '=HYPERLINK("http://x")', supplier: '@evil', sku: '+1', dimension: '-5x5', roomName: '=Camera' }]);
    const lines = csv.replace(/^﻿/, '').trim().split('\r\n'); assert.equal(lines[0], TEMPLATE_HEADER.join(';'));
    for (const cell of ['\'=HYPERLINK(""http://x"")', "'@evil", "'+1", "'-5x5", "'=Camera"]) assert.ok(lines[1]!.includes(cell), cell);
    const back = parseCustomCsv(csv, () => 'r'); assert.equal(back.rows[0]!.item!.name, '=HYPERLINK("http://x")', 'apostroful se scoate la reimport'); assert.equal(back.rows[0]!.item!.costPrice, 7);
  });
  test('rotund: export apoi import, cu virgulă zecimală', () => {
    const it = { name: 'Dulap', supplier: 'S', url: 'https://a.ro/x', image: null, sku: 'D1', price: 1234.5, currency: null, qty: 1.5, unit: 'buc', roomId: null, costPrice: 800, leadDays: 30, dimension: '100x50' };
    const csv = customItemsCsv([it]); assert.ok(csv.startsWith('﻿')); assert.ok(csv.includes('1234,5;') || csv.includes(';1234,5;'));
    const r = parseCustomCsv(csv); assert.deepEqual(r.rows[0]!.item, { ...it }); });
  test('șablonul are antetul și se citește', () => {
    const t = customTemplateCsv(); assert.equal(t.replace(/^﻿/, '').split('\r\n')[0], TEMPLATE_HEADER.join(';'));
    assert.equal(parseCustomCsv(t, () => 'r').ok, 1); });
});
test('buget: vânzare, achiziție, marjă', () => {
  const b = customBudget([{ price: 100, costPrice: 60, qty: 2, supplier: 'A' }, { price: 50, costPrice: null, qty: 1, supplier: 'A' }, { price: null, costPrice: 5, qty: 1, supplier: 'B' }]);
  assert.deepEqual(b, { count: 3, sell: 250, cost: 125, margin: 125, noPrice: 1, noCost: 1, bySupplier: { A: 250 } });
});
