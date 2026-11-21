// Faza 1b din ghidul BIM (§3.1): fișierul original al planului importat are hash, nume, mărime și tip, păstrate la salvare;
// imaginea de lucru redusă nu se mai confundă cu originalul.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { sha256Hex, sourceOfFile } from '../core/underlay';
import { checkUnderlay } from '../lib/repo';

const PNG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
const base = { dataUrl: PNG, x: 0, z: 0, widthM: 10, opacity: .5, locked: false };
const H = 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'; // SHA-256("abc")

test('sha256Hex: vectorul de test standard', async () => {
  assert.equal(await sha256Hex(new TextEncoder().encode('abc').buffer as ArrayBuffer), H);
});
test('sourceOfFile: hash-ul e al fișierului original, cu numele, mărimea și tipul lui', async () => {
  const f = new File([new TextEncoder().encode('abc')], 'releveu parter.png', { type: 'image/png' });
  const s = await sourceOfFile(f, '2026-11-21T10:00:00.000Z');
  assert.deepEqual(s, { sha256: H, name: 'releveu parter.png', bytes: 3, type: 'image/png', importedAt: '2026-11-21T10:00:00.000Z' });
});
test('checkUnderlay păstrează sursa validă și o respinge pe cea falsificată', () => {
  const source = { sha256: H, name: 'plan.png', bytes: 3, type: 'image/png', importedAt: '2026-11-21T10:00:00.000Z' };
  assert.deepEqual(checkUnderlay({ ...base, source }).source, source);
  assert.equal('source' in checkUnderlay(base), false, 'proiectele vechi rămân fără sursă');
  assert.throws(() => checkUnderlay({ ...base, source: { ...source, sha256: 'xyz' } }), /sursa/i);
  assert.throws(() => checkUnderlay({ ...base, source: { ...source, bytes: -1 } }), /sursa/i);
  assert.throws(() => checkUnderlay({ ...base, source: { ...source, type: 'application/pdf' } }), /sursa/i);
  const long = checkUnderlay({ ...base, source: { ...source, name: 'a'.repeat(500), extra: 1 } }).source!;
  assert.equal(long.name.length, 200); assert.equal('extra' in long, false);
});
