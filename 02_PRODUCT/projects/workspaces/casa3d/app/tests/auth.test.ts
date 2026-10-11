// Accesul de pe telefon: parola doar ca hash, sesiuni semnate care expiră, parolă nouă = sesiuni vechi închise, limită la ghicit.
import { test, beforeEach } from 'vitest';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'casa3d-auth-')); process.env.CASA3D_AUTH_FILE = path.join(dir, 'auth.json');
import { readAuth, setPassword, removePassword, checkPassword, makeSession, verifySession, loginAllowed, loginFailed, resetLoginLimits, SESSION_DAYS } from '../lib/auth';
const OWNER = '77777777-7777-4777-8777-777777777777';
beforeEach(() => { removePassword(); resetLoginLimits(); });

test('parola se ține doar ca hash; verificarea cere parola exactă', () => {
  assert.equal(readAuth(), null);
  setPassword('o parolă lungă 2026', OWNER); const cfg = readAuth()!;
  assert.equal(cfg.owner, OWNER); assert.ok(!fs.readFileSync(process.env.CASA3D_AUTH_FILE!, 'utf8').includes('o parolă lungă 2026'));
  if (process.platform !== 'win32') assert.equal(fs.statSync(process.env.CASA3D_AUTH_FILE!).mode & 0o777, 0o600);
  assert.equal(checkPassword(cfg, 'o parolă lungă 2026'), true);
  for (const bad of ['o parolă lungă 2025', '', 'O PAROLĂ LUNGĂ 2026', null, 'x'.repeat(5000)]) assert.equal(checkPassword(cfg, bad), false, String(bad));
  for (const bad of ['scurtă', 'x'.repeat(201), 123, null]) assert.throws(() => setPassword(bad, OWNER), (e: any) => e.status === 400);
  assert.throws(() => setPassword('o parolă lungă 2026', 'nu-e-uuid'), (e: any) => e.status === 400);
});

test('sesiuni: semnate, expiră, nu se pot falsifica; parolă nouă le închide pe toate', () => {
  setPassword('prima parolă bună', OWNER); const cfg = readAuth()!, now = Date.now(), s = makeSession(cfg, now);
  assert.equal(verifySession(cfg, s, now + 1000), true);
  assert.equal(verifySession(cfg, s, now + SESSION_DAYS * 864e5 + 1), false, 'expirată');
  const [exp, sig] = s.split('.'); assert.equal(verifySession(cfg, `${Number(exp) + 864e5}.${sig}`, now), false, 'expirare prelungită de mână');
  for (const bad of [undefined, null, '', 'abc', `${exp}.${'A'.repeat(43)}`, s + 'x']) assert.equal(verifySession(cfg, bad as any, now), false, String(bad));
  assert.equal(verifySession(null, s, now), false, 'fără parolă setată nu există sesiuni');
  setPassword('a doua parolă bună', OWNER); assert.equal(verifySession(readAuth(), s, now + 1000), false, 'parola nouă închide sesiunile vechi');
});

test('ghicitul parolei e limitat pe adresă și global', () => {
  const t = Date.now();
  for (let i = 0; i < 5; i++){ assert.equal(loginAllowed('1.1.1.1', t), true); loginFailed('1.1.1.1', t); }
  assert.equal(loginAllowed('1.1.1.1', t), false); assert.equal(loginAllowed('2.2.2.2', t), true);
  assert.equal(loginAllowed('1.1.1.1', t + 16 * 60e3), true, 'după 15 minute se poate din nou');
  resetLoginLimits(); for (let i = 0; i < 30; i++) loginFailed(`10.0.0.${i}`, t); assert.equal(loginAllowed('3.3.3.3', t), false, 'limita globală');
});
