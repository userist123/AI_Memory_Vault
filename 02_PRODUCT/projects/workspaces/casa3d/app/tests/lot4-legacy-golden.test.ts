// Lotul 4 (tavan fals pe zonă, balustradă și tip de spațiu, boiserie, al doilea covor, piese noi): un proiect vechi, fără
// câmpurile noi, trebuie să dea exact aceleași ieșiri ca înainte de lot. Intrările (șabloanele blank și demo, cu mobilierul
// așezat automat) și amprentele ieșirilor au fost scrise din codul de pe main, înainte de lot 4, în
// tests/fixtures/lot4-legacy-golden.json. Ieșirile verificate: serverul (checkSnapshot), devizul, intrarea motorului 3D,
// DXF-ul, validările (plan, piese, finisaje, puncte tehnice) și lista pentru cititorul de ecran.
// O schimbare voită în altă parte (alt preț în catalog, alt text) cere regenerarea amprentelor, pe codul fără lot 4:
//   CASA3D_WRITE_GOLDEN=1 npx vitest run tests/lot4-legacy-golden.test.ts --maxWorkers=2
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { validateFloor, validatePlacement } from '../core/validate';
import { checkSnapshot } from '../lib/repo';
import { computeBOQ, finishesOf } from '../core/boq';
import { viewerInput } from '../lib/viewer-input';
import { describePlan } from '../core/plan-describe';
import { translator } from '../lib/i18n';
import { planToDxf } from '../core/dxf';
import { projectFinishIssues } from '../core/finishes';
import { techIssues } from '../core/technical';
import { floors } from '../core/levels';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const FILE = path.join(import.meta.dirname, 'fixtures', 'lot4-legacy-golden.json');
const sha = (v: unknown) => createHash('sha256').update(typeof v === 'string' ? v : JSON.stringify(v)).digest('hex');

function outputs(s: Snapshot): Record<string, string> {
  const t = translator('ro');
  return {
    server: sha(checkSnapshot(structuredClone(s))),
    boq: sha(computeBOQ(s, cat, mc)),
    viewer: sha(viewerInput(s, cat, undefined, { mc })),
    viewerNoMc: sha(viewerInput(s, cat)),
    dxf: sha(planToDxf(s, cat, { lang: 'ro' })),
    validateFloor: sha(floors(s).map(f => validateFloor(f))),
    validatePlacements: sha(s.placements.map(p => validatePlacement(s, cat, p))),
    finishIssues: sha(projectFinishIssues(s, mc, finishesOf, floors(s))),
    techIssues: sha(techIssues(s, cat, s.floor)),
    describe: sha(describePlan(s, cat, { t, units: 'metric', lang: 'ro' })),
    describeEn: sha(describePlan(s, cat, { t: translator('en'), units: 'imperial', lang: 'en' })),
  };
}

describe('lot 4: proiectele vechi dau aceleași ieșiri', () => {
  test('blank și demo: server, deviz, 3D, DXF, validări, cititor de ecran', () => {
    if (process.env.CASA3D_WRITE_GOLDEN === '1'){
      const inputs = Object.fromEntries(['blank', 'demo'].map(tpl => [tpl, newSnapshot(cat, 'Vechi', tpl, 'ro', { materials: mc })]));
      const golden = { inputs, outputs: Object.fromEntries(Object.entries(inputs).map(([k, s]) => [k, outputs(s)])) };
      writeFileSync(FILE, JSON.stringify(golden, null, 1) + '\n'); return; }
    assert.ok(existsSync(FILE), 'lipsește tests/fixtures/lot4-legacy-golden.json');
    const golden = JSON.parse(readFileSync(FILE, 'utf8')) as { inputs: Record<string, Snapshot>; outputs: Record<string, Record<string, string>> };
    assert.deepEqual(Object.keys(golden.inputs), ['blank', 'demo']);
    for (const [tpl, s] of Object.entries(golden.inputs)) assert.deepEqual(outputs(s), golden.outputs[tpl], tpl);
  });
});
