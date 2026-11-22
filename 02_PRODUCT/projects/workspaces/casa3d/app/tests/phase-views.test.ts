// Planșele pe faze ale unei renovări (core/phase-views.ts): situația existentă, demolările și situația propusă.
// Filtrarea pereților și a golurilor (golul urmează peretele), condiția de apariție, niveluri multiple, textele ro/en.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { addLevel } from '../core/levels';
import { PHASES, hasPhases, hasPhaseWork, phaseFloor, phaseLevels, phaseSheets, phaseView, wallInPhase } from '../core/phase-views';
import { translator } from '../lib/i18n';
import type { Catalog, Snapshot, Wall } from '../core/types';

const cat = catalogJson as unknown as Catalog;
// camera 5 × 4 m (blank): wall-1 = nord cu fereastra op-2; pereți interiori adăugați de teste
const blank = (): Snapshot => newSnapshot(cat, 'Faze', 'blank');
const inner = (id: string, x: number, o: Partial<Wall> = {}): Wall => ({ id, a: [x, 0], b: [x, 4], thickness: .1, exterior: false, openings: [{ id: `op-${id}`, kind: 'door', offset: 1, width: .8 }], ...o });
/** un perete nou (x = 2), unul de demolat (x = 3) și unul existent declarat explicit (x = 4) */
const reno = (): Snapshot => { const s = blank();
  s.floor.walls.push(inner('w-new', 2, { status: 'new' }), inner('w-dem', 3, { status: 'demolish' }), inner('w-keep', 4, { status: 'existing' })); return s; };
const ids = (s: Snapshot) => s.floor.walls.map(w => w.id);

describe('condiția de apariție', () => {
  test('fără pereți noi sau de demolat nu există planșe pe faze', () => {
    const s = blank(); assert.equal(hasPhases(s), false); assert.deepEqual(phaseSheets(s), []); assert.deepEqual(phaseLevels(s), []);
    s.floor.walls.push(inner('w-keep', 4, { status: 'existing' }));
    assert.equal(hasPhases(s), false, '„existing” explicit nu e lucrare'); assert.deepEqual(phaseSheets(s), []);
  });
  test('un singur perete nou sau un singur perete de demolat ajunge', () => {
    for (const status of ['new', 'demolish'] as const){ const s = blank(); s.floor.walls.push(inner('w', 2, { status }));
      assert.equal(hasPhases(s), true, status); assert.equal(hasPhaseWork(s.floor), true);
      assert.deepEqual(phaseSheets(s), [{ level: 0, phase: 'existing' }, { level: 0, phase: 'demolition' }, { level: 0, phase: 'proposed' }]); }
  });
  test('ordinea fazelor: existent, demolări, propus', () => { assert.deepEqual([...PHASES], ['existing', 'demolition', 'proposed']); });
});

describe('filtrarea pereților', () => {
  const s = reno(), base = blank().floor.walls.map(w => w.id);
  test('existent: pereții existenți și cei de demolat, fără cei noi, toți desenați ca înainte de lucrări (fără marcaj)', () => {
    const v = phaseView(s, 'existing');
    assert.deepEqual(ids(v.snap), [...base, 'w-dem', 'w-keep']);
    assert.ok(v.snap.floor.walls.every(w => w.status === undefined));
    assert.equal(v.marks['w-dem'], 'demolish', 'marcajul rămâne disponibil pe id'); assert.equal(v.marks['w-keep'], 'existing'); assert.equal('w-new' in v.marks, false);
  });
  test('demolări: aceiași pereți, cei de demolat rămân marcați, ceilalți normali', () => {
    const v = phaseView(s, 'demolition');
    assert.deepEqual(ids(v.snap), [...base, 'w-dem', 'w-keep']);
    assert.deepEqual(v.snap.floor.walls.filter(w => w.status === 'demolish').map(w => w.id), ['w-dem']);
    assert.ok(v.snap.floor.walls.every(w => w.status !== 'new'));
  });
  test('propus: pereții păstrați și cei noi (marcați), fără cei demolați', () => {
    const v = phaseView(s, 'proposed');
    assert.deepEqual(ids(v.snap), [...base, 'w-new', 'w-keep']);
    assert.equal(v.snap.floor.walls.find(w => w.id === 'w-new')!.status, 'new'); assert.equal(v.marks['w-new'], 'new');
    assert.ok(v.snap.floor.walls.every(w => w.status !== 'demolish'));
  });
  test('wallInPhase: lipsa stării = existent, pe toate planșele', () => {
    for (const p of PHASES) assert.equal(wallInPhase({}, p), true);
    assert.deepEqual(PHASES.map(p => wallInPhase({ status: 'new' }, p)), [false, false, true]);
    assert.deepEqual(PHASES.map(p => wallInPhase({ status: 'demolish' }, p)), [true, true, false]);
  });
  test('proiectul nu se modifică (funcții pure)', () => {
    const src = reno(), before = JSON.stringify(src); for (const p of PHASES) phaseView(src, p); assert.equal(JSON.stringify(src), before);
  });
});

describe('golurile urmează peretele lor', () => {
  const s = reno();
  test('golul dintr-un perete nou apare doar pe propus', () => {
    assert.deepEqual(PHASES.map(p => phaseView(s, p).openingIds.includes('op-w-new')), [false, false, true]);
  });
  test('golul dintr-un perete de demolat apare pe existent și pe demolări, nu pe propus', () => {
    assert.deepEqual(PHASES.map(p => phaseView(s, p).openingIds.includes('op-w-dem')), [true, true, false]);
    assert.equal(phaseView(s, 'demolition').snap.floor.walls.find(w => w.id === 'w-dem')!.openings.length, 1);
  });
  test('golurile pereților păstrați apar pe toate trei', () => {
    const kept = blank().floor.walls.flatMap(w => w.openings.map(o => o.id)); assert.ok(kept.length > 0);
    for (const p of PHASES){ const o = phaseView(s, p).openingIds; assert.ok(o.includes('op-w-keep'), p); for (const k of kept) assert.ok(o.includes(k), `${p} ${k}`); }
  });
  test('nișele urmează și ele peretele', () => {
    const fl = { ...s.floor, niches: [{ id: 'n-new', wallId: 'w-new', offset: .2, width: .5, height: .5, depth: .05, elevation: 1 }, { id: 'n-dem', wallId: 'w-dem', offset: .2, width: .5, height: .5, depth: .05, elevation: 1 }] };
    assert.deepEqual(PHASES.map(p => phaseFloor(fl, p).niches!.map(n => n.id)), [['n-dem'], ['n-dem'], ['n-new']]);
  });
});

describe('camere, mobilier, adnotări', () => {
  const s = newSnapshot(cat, 'Faze', 'demo'); s.floor.walls.push(inner('w-new', 2, { status: 'new' }));
  s.floor.annotations = [{ id: 'a1', kind: 'text', at: [1, 1], text: 'notă' }];
  test('proiectul demo are camere și mobilier (altfel testul n-ar dovedi nimic)', () => { assert.ok(s.floor.rooms.length > 0); assert.ok(s.placements.length > 0); });
  test('existent și demolări: fără camere, mobilier și adnotări; propus le păstrează', () => {
    for (const p of ['existing', 'demolition'] as const){ const v = phaseView(s, p);
      assert.equal(v.snap.floor.rooms.length, 0, p); assert.equal(v.snap.placements.length, 0, p); assert.equal(v.snap.floor.annotations!.length, 0, p);
      assert.equal(v.showRooms, false); assert.equal(v.showFurniture, false); }
    const v = phaseView(s, 'proposed');
    assert.equal(v.snap.floor.rooms.length, s.floor.rooms.length); assert.equal(v.snap.placements.length, s.placements.length); assert.equal(v.snap.floor.annotations!.length, 1);
    assert.equal(v.showRooms, true); assert.equal(v.showFurniture, true);
  });
});

describe('niveluri multiple', () => {
  let n = 0; const id = () => `t${++n}`;
  // etajul copiază parterul; lucrările se pun apoi doar la etaj
  const two = (): Snapshot => { const s = addLevel(blank(), { name: 'Etaj', id });
    s.levels![0]!.walls.push(inner('up-new', 2, { status: 'new' }), inner('up-dem', 3, { status: 'demolish' })); return s; };
  test('lucrări doar la etaj: planșele apar, și doar pentru etaj', () => {
    const s = two(); assert.equal(hasPhases(s), true); assert.deepEqual(phaseLevels(s), [1]);
    assert.deepEqual(phaseSheets(s).map(x => `${x.level}:${x.phase}`), ['1:existing', '1:demolition', '1:proposed']);
  });
  test('lucrări pe ambele niveluri: trei planșe pe nivel, în ordinea nivelurilor', () => {
    const s = two(); s.floor.walls.push(inner('gr-dem', 3, { status: 'demolish' }));
    assert.deepEqual(phaseSheets(s).map(x => `${x.level}:${x.phase}`), ['0:existing', '0:demolition', '0:proposed', '1:existing', '1:demolition', '1:proposed']);
  });
  test('vederea unui nivel conține doar pereții lui, filtrați pe fază', () => {
    const s = two(); s.floor.walls.push(inner('gr-dem', 3, { status: 'demolish' }));
    const up = phaseView(s, 'proposed', 1), gr = phaseView(s, 'proposed', 0);
    assert.equal(up.level, 1); assert.equal(up.snap.levels, undefined, 'vedere de un singur nivel');
    assert.ok(ids(up.snap).includes('up-new')); assert.ok(!ids(up.snap).includes('up-dem')); assert.ok(!ids(up.snap).includes('gr-dem'));
    assert.ok(!ids(gr.snap).includes('gr-dem')); assert.ok(!ids(gr.snap).includes('up-new'));
    assert.ok(ids(phaseView(s, 'existing', 0).snap).includes('gr-dem')); assert.ok(ids(phaseView(s, 'demolition', 1).snap).includes('up-dem'));
  });
  test('nivel inexistent: RangeError', () => { assert.throws(() => phaseView(two(), 'existing', 5), RangeError); });
});

describe('texte și planșă', () => {
  const KEYS = ['phv.existing', 'phv.demolition', 'phv.proposed', 'phv.legend', 'phv.legendKept', 'phv.legendDemolish', 'phv.legendNew', 'phv.note', 'phv.level', 'phv.aria'];
  test('titlurile și nota există în ro și en, fără limbaj de autorizare', () => {
    const ro = translator('ro'), en = translator('en');
    assert.equal(ro('phv.existing'), 'Situația existentă'); assert.equal(ro('phv.demolition'), 'Demolări'); assert.equal(ro('phv.proposed'), 'Situația propusă');
    assert.equal(ro('phv.note'), 'Planșe orientative pentru discuția cu beneficiarul și cu asociația; nu înlocuiesc documentația întocmită de un proiectant autorizat.');
    for (const k of KEYS) for (const t of [ro, en]){ const v = t(k); assert.notEqual(v, k, k); assert.ok(!/DTAC|autoriza[țt]ie|memoriu tehnic|building permit/i.test(v), `${k}: ${v}`); }
    assert.notEqual(en('phv.existing'), ro('phv.existing')); assert.notEqual(en('phv.note'), ro('phv.note'));
  });
  test('PrintView desenează planșele din core/phase-views, după planul principal, cu cartuș, legendă și notă', () => {
    const src = readFileSync(join(__dirname, '..', 'components', 'PrintView.tsx'), 'utf8');
    assert.match(src, /from '@\/core\/phase-views'/); assert.match(src, /phaseSheets\(snap\)/);
    const plan = src.indexOf('data-testid={`plan-${i}`}'), phase = src.indexOf('data-testid={`phase-'), ceiling = src.indexOf('data-testid={`ceiling-${i}`}');
    assert.ok(plan > 0 && phase > plan && ceiling > phase, 'ordinea: plan, faze, tavan');
    assert.match(src, /renderTitleBlock\(title, `1:\$\{sc\.denominator\}`\)/); assert.match(src, /phv\.note/); assert.match(src, /PhaseLegend/);
    assert.ok(!/DTAC|memoriu tehnic/i.test(src));
  });
});
