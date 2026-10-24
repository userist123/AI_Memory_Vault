import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import floorJson from '../data/floor.v1.json';
import { groups, defaultWants } from '../core/catalog';
import { failText } from '../core/messages';
import type { Catalog } from '../core/types';
const keys = Object.keys(groups(catalogJson as unknown as Catalog));

test('bifele implicite din brief depind de tipul camerei și există toate în catalog', () => {
  assert.deepEqual(defaultWants('dormitor', keys), ['pat', 'noptiera', 'dulap']);
  assert.ok(!defaultWants('dormitor', keys).includes('canapea'), 'dormitorul nu pornește cu canapea');
  assert.deepEqual(defaultWants('living', keys), ['canapea', 'masuta', 'comodaTv']);
  for (const r of (floorJson as any).rooms) assert.ok(defaultWants(r.type, keys).length > 0, `camera demo ${r.type} are bife implicite`);
  assert.deepEqual(defaultWants('pod', keys), [], 'un tip necunoscut pornește fără bife');
  assert.deepEqual(defaultWants('dormitor', ['pat']), ['pat'], 'doar grupe existente în catalog');
});

test('motivul tehnic al solver-ului devine un mesaj pe înțeles, cu codul păstrat', () => {
  const t = failText('DOES_NOT_FIT: pat-0 nu are loc valid in dormitor.');
  assert.match(t, /nu încape în cameră/); assert.match(t, /\(DOES_NOT_FIT\)$/); assert.ok(!t.includes('pat-0'));
  assert.equal(failText('ALTCEVA: detaliu'), 'detaliu (ALTCEVA)');
  assert.equal(failText(undefined), 'Piesa nu a putut fi așezată.');
});
