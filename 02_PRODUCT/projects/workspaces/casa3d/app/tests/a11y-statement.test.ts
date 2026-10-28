// UI/UX P3-17: declarația de accesibilitate e publică (și prin tunel) exact la /accesibilitate și are aceeași structură
// în română și engleză.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { isPublicPath } from '../lib/public-gate';
import { statementFor, STATEMENT_DATE } from '../lib/a11y-statement';

test('/accesibilitate e publică; nimic de sub ea', () => {
  for (const p of ['/accesibilitate', '/accesibilitate/']) assert.ok(isPublicPath(p), p);
  for (const p of ['/accesibilitate/x', '/accesibilitate/../p/1', '/accesibilitateX', '/api/accesibilitate']) assert.ok(!isPublicPath(p), p);
});
test('RO și EN: aceleași secțiuni, aceleași liste, data declarației; fără promisiuni de termen', () => {
  const ro = statementFor('ro'), en = statementFor('en');
  assert.equal(ro.sections.length, en.sections.length);
  ro.sections.forEach((s, i) => { assert.equal(s.list?.length ?? 0, en.sections[i]!.list?.length ?? 0, s.h); assert.equal(s.p?.length ?? 0, en.sections[i]!.p?.length ?? 0, s.h); });
  assert.match(JSON.stringify(ro), new RegExp(STATEMENT_DATE)); assert.match(JSON.stringify(en), new RegExp(STATEMENT_DATE));
  assert.doesNotMatch(JSON.stringify(ro) + JSON.stringify(en), /\d+ (de )?(zile|days)/, 'niciun termen de răspuns promis în numele proprietarului');
});
