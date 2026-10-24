// Ce citește aplicația din numele produsului ca să-l deseneze ca la comandă: culoare, sertare, uși, oglinzi, vitrine, formă.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { deriveStyle } from '../core/product-style';
import { toEngineCatalog } from '../core/catalog';
import type { Catalog } from '../core/types';

test('nume reale de produse → forma lor', () => {
  assert.deepEqual(deriveStyle('dulap', 'Dulap dormitor Nevis 3K1F1O cu oglinda, stejar artisan'), { col: '#c4a77d', wood: true, doors: 3, drawers: 1, mirrors: 1 });
  const tv = deriveStyle('comodaTv', 'Comoda TV Darwen 3K2F2V cu usi si sertare, bej cashmere + stejar artisan');
  assert.equal(tv.doors, 3); assert.equal(tv.drawers, 2); assert.equal(tv.glass, 2); assert.equal(tv.wood, true);
  assert.equal(deriveStyle('comoda', 'Comoda dormitor Duero 5F 90 cu 5 sertare, stejar gri').drawers, 5);
  assert.equal(deriveStyle('comoda', 'Comoda dormitor Duero 5F 90 cu 5 sertare, stejar gri').col, '#8d8a85');
  assert.deepEqual(deriveStyle('noptiera', 'Noptiera Corsica cu raft + sertar, bej (specs: crem)'), { col: '#d8ccb8', drawers: 1, shelf: true });
  assert.equal(deriveStyle('noptiera', 'Noptiera Duero 3F cu 3 sertare, stejar gri').drawers, 3);
  assert.equal(deriveStyle('masuta', 'Masuta cafea Cirko rotunda set 2 in 1, alb marmorat + negru').round, true);
  assert.equal(deriveStyle('masuta', 'Masuta cafea Cirko rotunda set 2 in 1, alb marmorat + negru').marble, true);
  assert.equal(deriveStyle('birou', 'Birou calculator cu sertare si dulap Alfa, stejar sonoma').col, '#c8a97e');
  assert.equal(deriveStyle('pat', 'Pat dormitor Fidelio matrimonial tapitat cu lada 160x200, bej deschis').uph, true);
  assert.equal(deriveStyle('scaunBirou', 'Scaun birou ergonomic cu suport lombar Ericsson, negru, textil').col, '#1e1e20');
  assert.equal(deriveStyle('dulap', 'Dulap dormitor / dressing Sierra 220 cu usi glisante si oglinda, gri g').sliding, true);
  assert.equal(deriveStyle('comodaTv', 'BESTÅ comodă TV, alb, 120x40x48 (deschisă)').shelf, true);
  assert.equal(deriveStyle('comodaTv', 'Comoda TV Rex cu lumini si usi, alb mat + negru').lights, true);
  // mesele: scaunele se desenează doar dacă vin în set
  assert.equal(deriveStyle('masa', 'Masa bucatarie / dining fixa Eva AB2820, natur + negru').chairs, 0);
  assert.equal(deriveStyle('masa', 'EKEDALEN masă extensibilă + 2 scaune, maro-închis, 80/120').chairs, 2);
});

test('catalogul: ce e scris de mână are întâietate, restul se completează din nume', () => {
  const eng = toEngineCatalog(catalogJson as unknown as Catalog);
  const kivik = eng['canapea']!.v.find((v: any) => /KIVIK.*Kelinge/.test(v.nume))!;
  assert.equal(kivik.s!.col, '#3c6a6b', 'culoarea aleasă de mână rămâne');
  const nevis = eng['dulap']!.v.find((v: any) => /Nevis/.test(v.nume))!;
  assert.equal(nevis.s!.doors, 3); assert.equal(nevis.s!.mirrors, 1);
});
