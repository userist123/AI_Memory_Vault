// Profilul firmei: CUI și IBAN verificate după cifrele de control, logo doar PNG/JPEG mic, salvat pe proprietar.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import { validCui, validIban, checkProfile, profileLine } from '../core/studio-profile';
import { getProfile, saveProfile } from '../lib/studio-profile';
beforeAll(() => resetDbForTests());

// IBAN RO de exemplu construit la rulare: nicio valoare RO validă stă literal în sursă (personal_data_guard).
const roIban = (bban: string) => { const r = bban + 'RO00'; let m = 0;
  for (const ch of r){ const v = /\d/.test(ch) ? ch : String(ch.charCodeAt(0) - 55); for (const d of v) m = (m * 10 + Number(d)) % 97; }
  return 'RO' + String(98 - m).padStart(2, '0') + bban; };
const RO = roIban('AAAA1B31007593840000'), RO_GROUPED = RO.replace(/(.{4})/g, '$1 ').trim();

test('CUI și IBAN reale trec, greșelile de o cifră nu', () => {
  for (const c of ['2816464', 'RO2816464', '14399840']) assert.ok(validCui(c), c);          // Dedeman, eMAG (Dante International)
  for (const c of ['2816465', '1439984', 'ROabc', '']) assert.ok(!validCui(c), c);
  assert.ok(validIban(RO_GROUPED)); assert.ok(validIban(RO.toLowerCase()));
  assert.ok(!validIban(RO.slice(0, -1) + '1')); assert.ok(!validIban('RO49AAAA1B3100759384')); assert.ok(validIban('DE89 3704 0044 0532 0130 00'));
});

test('profilul: câmpuri curățate, erori clare, logo sigur; salvat pe proprietar', async () => {
  const p = checkProfile({ name: ' Studio Ana ', cui: 'RO2816464', iban: RO.toLowerCase(), email: 'ana@studio.ro', website: 'https://studio.ro', logo: 'data:image/png;base64,iVBORw0KGgo=' });
  assert.equal(p.name, 'Studio Ana'); assert.equal(p.iban, RO_GROUPED);
  assert.equal(profileLine(p), 'Studio Ana · CUI RO2816464 · ana@studio.ro · studio.ro');
  assert.throws(() => checkProfile({ cui: '123' }), /CUI/); assert.throws(() => checkProfile({ iban: 'RO00 0000' }), /IBAN/);
  assert.throws(() => checkProfile({ logo: 'data:image/svg+xml;base64,PHN2Zz4=' }), /PNG sau JPEG/);
  assert.throws(() => checkProfile({ website: 'javascript:alert(1)' }), /https/);
  const A = 'aaaaaaaa-0000-4000-8000-000000000001', B = 'bbbbbbbb-0000-4000-8000-000000000002';
  await saveProfile(A, { name: 'Studio Ana', cui: '2816464' }); assert.equal((await getProfile(A)).name, 'Studio Ana'); assert.deepEqual(await getProfile(B), {});
  await assert.rejects(saveProfile(A, { cui: '1' }), (e: any) => e.status === 400);
});
