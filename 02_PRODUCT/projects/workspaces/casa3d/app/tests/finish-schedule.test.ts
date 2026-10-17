// Fișa de finisaje: aceleași cantități și prețuri ca bugetul, cameră cu cameră, cu cod, link, model și data verificării.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { finishSchedule, productCode } from '../core/finish-schedule';
import { scheduleCsv, detailText, csvCell } from '../lib/finish-schedule-text';
import { t as tr } from '../lib/i18n';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const t = (k: string, v?: Record<string, string | number>) => tr('ro', k, v);
const project = (): Snapshot => { const s = newSnapshot(cat, 'Fișă', 'demo'), living = s.floor.rooms.find(r => r.id === 'living')!;
  s.finishes = { living: { ...finishesOf(s, living), floor: 'parchet-krono-herringbone-k450', floorLayout: { pattern: 'herringbone' },
    wallFeatures: [{ side: 'N', kind: 'wallpaper', material: 'tapet-grandeco-marmor', heightM: 1.2 }], ceiling: { type: 'cove', dropCm: 12, coveCm: 25, cornice: 'cornisa-nmc-nc109' } } };
  return s; };

test('codul de produs se ia din link', () => {
  assert.equal(productCode('https://www.dedeman.ro/ro/parchet-laminat/p/4026660'), '4026660');
  assert.equal(productCode('https://www.dedeman.ro/ro/banda-led/p/1070874-1048524'), '1070874-1048524'); assert.equal(productCode(null), null);
  assert.equal(productCode('https://www.ikea.com/ro/ro/p/virrmo-plafoniera-led-nichelat-70430780/'), '70430780');
});
test('o linie pentru fiecare material din buget, cu aceleași totaluri, ordonată pe camere', () => {
  const s = project(), rows = finishSchedule(s, cat, mc), b = computeBOQ(s, cat, mc), mats = b.items.filter(i => i.category === 'finishes' || i.category === 'lighting');
  assert.equal(rows.length, mats.length);
  assert.equal(rows.reduce((a, r) => a + (r.total ?? 0), 0).toFixed(2), mats.reduce((a, i) => a + (i.total ?? 0), 0).toFixed(2));
  assert.equal(rows.at(-1)!.element, 'adhesive'); assert.equal(rows.at(-1)!.roomId, null);
  const living = rows.filter(r => r.roomId === 'living').map(r => r.element);
  assert.deepEqual(living, ['floor', 'wall', 'paint', 'baseboard', 'ceiling', 'cornice', 'led', 'light']);
});
test('detaliile spun modelul, mărimea, înălțimea și partea peretelui, în română', () => {
  const rows = finishSchedule(project(), cat, mc), floor = rows.find(r => r.roomId === 'living' && r.element === 'floor')!, wall = rows.find(r => r.element === 'wall')!;
  assert.equal(floor.code, '4026660'); assert.equal(floor.verifiedAt, '2026-10-10');
  assert.equal(detailText(floor, t), 'Spic (herringbone) · 63 × 12.6 cm');
  assert.match(detailText(wall, t), /^Tapet · Peretele de sus · până la 1.2 m · 11 fâșii, 8 pe rolă · Raportul modelului nu e declarat/);
  assert.match(detailText(rows.find(r => r.element === 'ceiling')!, t), /Fals cu scafă luminoasă · coborât 12 cm · scafă 25 cm/);
  assert.match(detailText(rows.find(r => r.element === 'led')!, t), /3000 K · IP20/);
});
test('CSV: antet, separator „;”, BOM, câte o linie pe produs', () => {
  const s = project(), rows = finishSchedule(s, cat, mc), csv = scheduleCsv(rows, t, 'RON', i => i === 0 ? 'Parter' : `Etaj ${i}`);
  assert.ok(csv.startsWith('﻿"Nivel";"Cameră";"Element";"Produs"')); assert.equal(csv.split('\n').length, rows.length + 1);
  assert.match(csv, /"Parter";"Living";"Pardoseală";"Parchet laminat 8 mm Krono Original Herringbone K450/);
  assert.match(csv, /"Toată casa";"Adeziv"/);
});
test('CSV: textul care ar porni o formulă în Excel e neutralizat; numerele rămân numere', () => {
  assert.equal(csvCell('=HYPERLINK("http://x","y")'), '"\'=HYPERLINK(""http://x"",""y"")"'); assert.equal(csvCell('@SUM(1)'), '"\'@SUM(1)"');
  assert.equal(csvCell(-12.5), '"-12.5"'); assert.equal(csvCell('Living'), '"Living"');
  const s = project(); s.floor.rooms.find(r => r.id === 'living')!.name = '+cmd|calc';
  assert.match(scheduleCsv(finishSchedule(s, cat, mc), t, 'RON', () => 'Parter'), /"'\+cmd\|calc"/);
});

// regresie din review: fiecare linie de finisaj, iluminat și textile din buget apare în fișă (baie, bucătărie, corpuri)
test('fișa are același total ca bugetul pentru finisaje, iluminat și textile, în fiecare stil', async () => {
  const { applyStyle, STYLE_PACKAGES } = await import('../core/styles'); const { SCHEDULE_CATEGORIES } = await import('../core/finish-schedule');
  for (const pkg of STYLE_PACKAGES){ const s = applyStyle(newSnapshot(cat, 'F', 'demo'), cat, mc, pkg), b = computeBOQ(s, cat, mc);
    const boq = b.items.filter(i => SCHEDULE_CATEGORIES.has(i.category)).reduce((a, i) => a + (i.total ?? 0), 0), sch = finishSchedule(s, cat, mc).reduce((a, r) => a + (r.total ?? 0), 0);
    assert.ok(Math.abs(boq - sch) < .01, `${pkg.id}: ${boq} vs ${sch}`); }
});
