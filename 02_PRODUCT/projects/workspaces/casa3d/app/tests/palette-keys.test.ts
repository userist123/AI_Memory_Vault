// Review fixes, round 7: keys typed in the command palette never reach the editor's shortcuts, Enter on a button keeps
// its native meaning, macOS Ctrl+K in text fields is not hijacked, Romanian plural of the result count.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { paletteKeyAction, editorIgnoresKey } from '../lib/palette-keys';
import { isPaletteShortcut } from '../lib/fuzzy';
import { tp, pluralForm } from '../lib/i18n';

test('tastele din paletă sau dintr-un dialog deschis nu mută / șterg / rotesc piesa aleasă din plan', () => {
  const base = { preview: false, inField: false, inDialog: false, paletteOpen: false };
  assert.equal(editorIgnoresKey(base), false, 'pe plan, scurtăturile merg');
  assert.equal(editorIgnoresKey({ ...base, inDialog: true }), true, 'focus pe „Închide paleta” (buton din dialog)');
  assert.equal(editorIgnoresKey({ ...base, paletteOpen: true }), true, 'paleta deschisă, oriunde ar fi focusul');
  assert.equal(editorIgnoresKey({ ...base, inField: true }), true);
  assert.equal(editorIgnoresKey({ ...base, preview: true }), true);
});

test('Enter rulează comanda doar din câmpul de căutare sau de pe o opțiune; pe un buton rămâne clic nativ', () => {
  assert.equal(paletteKeyAction('Enter', 'input', true), 'run');
  assert.equal(paletteKeyAction('Enter', 'option', false), 'run');
  assert.equal(paletteKeyAction('Enter', 'button', true), null, 'Enter pe „Închide paleta” închide, nu navighează');
  assert.equal(paletteKeyAction(' ', 'button', true), null);
  assert.equal(paletteKeyAction('Escape', 'button', true), 'close');
  assert.equal(paletteKeyAction('Tab', 'input', true), 'trap');
  assert.equal(paletteKeyAction('ArrowDown', 'button', true), 'move');
  assert.equal(paletteKeyAction('Home', 'input', false), null, 'cu text scris, Home mută cursorul în câmp');
  assert.equal(paletteKeyAction('End', 'input', true), 'move');
  assert.equal(paletteKeyAction('Delete', 'input', false), null);
});

test('pe macOS, Ctrl+K într-un câmp de text rămâne al sistemului; Cmd+K deschide oriunde', () => {
  const ev = (o: Partial<{ ctrlKey: boolean; metaKey: boolean }>) => ({ key: 'k', ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, ...o });
  assert.equal(isPaletteShortcut(ev({ ctrlKey: true }), { mac: true, inField: true }), false);
  assert.equal(isPaletteShortcut(ev({ metaKey: true }), { mac: true, inField: true }), true);
  assert.equal(isPaletteShortcut(ev({ ctrlKey: true }), { mac: true, inField: false }), true);
  assert.equal(isPaletteShortcut(ev({ ctrlKey: true }), { mac: false, inField: true }), true, 'Windows/Linux: Ctrl+K oriunde');
});

test('numărul de rezultate la plural, după regula românească', () => {
  assert.equal(tp('ro', 'cmdk.results', 1), '1 rezultat');
  assert.equal(tp('ro', 'cmdk.results', 0), '0 rezultate');
  assert.equal(tp('ro', 'cmdk.results', 2), '2 rezultate');
  assert.equal(tp('ro', 'cmdk.results', 19), '19 rezultate');
  assert.equal(tp('ro', 'cmdk.results', 20), '20 de rezultate');
  assert.equal(tp('ro', 'cmdk.results', 100), '100 de rezultate');
  assert.equal(tp('ro', 'cmdk.results', 101), '101 rezultate');
  assert.equal(tp('ro', 'cmdk.results', 120), '120 de rezultate');
  assert.equal(tp('en', 'cmdk.results', 1), '1 result'); assert.equal(tp('en', 'cmdk.results', 20), '20 results');
  assert.equal(pluralForm('en', 25), 'few');
});
