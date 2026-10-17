// Command palette search: diacritics-insensitive, fuzzy, best first; keyboard helpers.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { fold, fuzzyScore, fuzzyFilter, moveActive, isPaletteShortcut } from '../lib/fuzzy';

test('fără diacritice și fără majuscule, în ambele forme ale lui ș/ț', () => {
  assert.equal(fold('Continuă'), 'continua'); assert.equal(fold('Șantier'), 'santier'); assert.equal(fold('Ştergere ţiglă'), 'stergere tigla');
  assert.equal(fold('Înapoi la ÎNCĂPERI'), 'inapoi la incaperi');
});

test('„continua” găsește „Continuă”, „santier” găsește „Șantier”, „plnse” găsește „Planșe”', () => {
  assert.ok(fuzzyScore('continua', 'Continuă') != null);
  assert.ok(fuzzyScore('santier', '10 Șantier') != null);
  assert.ok(fuzzyScore('plnse', 'Planșe') != null);
  assert.equal(fuzzyScore('xyz', 'Planșe'), null);
  assert.equal(fuzzyScore('', 'orice'), 0);
});

test('prefixul și începutul de cuvânt câștigă; egalitățile păstrează ordinea', () => {
  const items = ['Mergi la Deviz', 'Mobilier', 'Mută în coș', 'Mod 3D'];
  assert.deepEqual(fuzzyFilter(items, 'mob', x => [x]), ['Mobilier']);
  assert.equal(fuzzyFilter(['Revizie nouă', 'Salvează revizia'], 'salv', x => [x])[0], 'Salvează revizia');
  assert.deepEqual(fuzzyFilter(['b', 'a'], '', x => [x]), ['b', 'a']);
  assert.deepEqual(fuzzyFilter(['Pachet planșe PDF', 'Plan'], 'pdf plan', x => [x]), ['Pachet planșe PDF']);
  assert.deepEqual(fuzzyFilter([{ t: 'Etapa 03', k: ['Finisaje', 'pardoseli'] }], 'pardos', x => x.k).length, 1, 'cuvinte cheie');
});

test('săgețile se învârt în listă; lista goală nu are element activ', () => {
  assert.equal(moveActive('ArrowDown', -1, 3), 0); assert.equal(moveActive('ArrowDown', 2, 3), 0);
  assert.equal(moveActive('ArrowUp', 0, 3), 2); assert.equal(moveActive('End', 0, 3), 2); assert.equal(moveActive('Home', 2, 3), 0);
  assert.equal(moveActive('ArrowDown', 0, 0), -1); assert.equal(moveActive('x', 7, 3), 2);
});

test('Ctrl+K și Cmd+K deschid; cu Shift sau Alt nu', () => {
  const k = (o: Partial<{ ctrlKey: boolean; metaKey: boolean; altKey: boolean; shiftKey: boolean }>, key = 'k') => isPaletteShortcut({ key, ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, ...o });
  assert.equal(k({ ctrlKey: true }), true); assert.equal(k({ metaKey: true }, 'K'), true);
  assert.equal(k({ ctrlKey: true, shiftKey: true }), false); assert.equal(k({ ctrlKey: true, altKey: true }), false); assert.equal(k({}), false);
});
