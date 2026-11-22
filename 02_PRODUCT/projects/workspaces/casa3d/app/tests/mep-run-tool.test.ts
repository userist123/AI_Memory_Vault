// Unealta „Traseu instalații” (mep_run, etapa Instalații): traseul desenat în plan clic cu clic. Funcțiile pure din
// core/mep-draw.ts, drumul traseului prin checkSnapshot, descrierea din Structură, încadrarea, verificarea de coliziuni,
// scurtătura și locul din bară.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { checkSnapshot } from '../lib/repo';
import { appendRunPoint, dropLastRunPoint, orthoPoint, planRunLength, nextRunId, runFromPlanPoints, optionalNumber, type P2 } from '../core/mep-draw';
import { MEP_LIMITS, MEP_SYSTEMS, runLength, sanitizeMepRun, mepIssues } from '../core/mep';
import { describePlan } from '../core/plan-describe';
import { selectionBox } from '../core/frame';
import { detectClashes } from '../core/clash';
import { translator, DICTS } from '../lib/i18n';
import { railSlots } from '../lib/tool-groups';
import { TOOLS, toolShortcut, matchCommand, formatShortcut, contextCommands } from '../lib/commands';
import type { Catalog, MepRun, MepSystem, Snapshot } from '../core/types';
import type { Tool } from '../components/PlanView';

const cat = catalogJson as unknown as Catalog;
const blank = (): Snapshot => newSnapshot(cat, 'Trasee', 'blank');
const ok = (r: ReturnType<typeof runFromPlanPoints>): MepRun => { assert.ok('run' in r, JSON.stringify(r)); return (r as { run: MepRun }).run; };
const err = (r: ReturnType<typeof runFromPlanPoints>) => { assert.ok('error' in r, JSON.stringify(r)); return (r as { error: string }).error; };
const base = { id: 'mep-1', system: 'water_cold' as MepSystem, points: [[0, 0], [2, 0], [2, 1.5]] as P2[], heightM: 2.4, ceilingM: 2.6 };

describe('lanțul de puncte din plan', () => {
  test('appendRunPoint: rotunjește la mm, respinge punctul căzut peste ultimul (dublu-clic) și oprește la numărul maxim de puncte', () => {
    const a = appendRunPoint([], [1.00049, 2.0004]); assert.deepEqual(a, [[1, 2]]);
    const same = appendRunPoint(a, [1.0002, 2]); assert.equal(same, a, 'același tablou = punct respins');
    const b = appendRunPoint(a, [3, 2]); assert.deepEqual(b, [[1, 2], [3, 2]]); assert.notEqual(b, a); assert.deepEqual(a, [[1, 2]], 'intrarea nu se modifică');
    // un punct egal cu unul mai vechi (nu ultimul) e permis: traseul se poate întoarce
    assert.deepEqual(appendRunPoint(b, [1, 2]), [[1, 2], [3, 2], [1, 2]]);
    let full: P2[] = []; for (let i = 0; i < MEP_LIMITS.maxPoints; i++) full = appendRunPoint(full, [i, 0]);
    assert.equal(full.length, MEP_LIMITS.maxPoints); assert.equal(appendRunPoint(full, [999, 0]), full);
  });
  test('dropLastRunPoint (Esc): scoate ultimul punct; lanțul gol rămâne gol', () => {
    const pts: P2[] = [[0, 0], [1, 0], [1, 1]];
    assert.deepEqual(dropLastRunPoint(pts), [[0, 0], [1, 0]]); assert.equal(pts.length, 3);
    assert.deepEqual(dropLastRunPoint([[0, 0]]), []); assert.deepEqual(dropLastRunPoint([]), []);
  });
  test('orthoPoint (Shift): punctul ajunge pe orizontala sau verticala ancorei, pe axa cu deplasarea mai mare', () => {
    assert.deepEqual(orthoPoint([1, 1], [4, 1.8]), [4, 1]);
    assert.deepEqual(orthoPoint([1, 1], [1.3, -2.5]), [1, -2.5]);
    assert.deepEqual(orthoPoint([1, 1], [-3, 2]), [-3, 1]);
    assert.deepEqual(orthoPoint([0, 0], [2, 2]), [2, 0], 'la egalitate: orizontala');
    assert.deepEqual(orthoPoint([0, 0], [2, -2]), [2, 0]);
    assert.deepEqual(orthoPoint([1, 1], [1, 1]), [1, 1]);
    assert.deepEqual(orthoPoint([0.5, 0.25], [3.14159, 0.3]), [3.142, 0.25], 'rotunjit la mm; coordonata ancorei rămâne neatinsă');
  });
  test('planRunLength: suma segmentelor; sub două puncte = 0; egală cu runLength a traseului construit', () => {
    assert.equal(planRunLength([]), 0); assert.equal(planRunLength([[1, 1]]), 0);
    assert.equal(planRunLength([[0, 0], [3, 4]]), 5); assert.equal(planRunLength([[0, 0], [2, 0], [2, 1.5]]), 3.5);
    assert.equal(runLength(ok(runFromPlanPoints(base))), planRunLength(base.points));
  });
  test('nextRunId: primul mep-N liber, ca la traseele din panou', () => {
    assert.equal(nextRunId([]), 'mep-1'); assert.equal(nextRunId(['mep-1', 'w1', 'mep-3']), 'mep-2'); assert.equal(nextRunId(new Set(['mep-1', 'mep-2'])), 'mep-3');
  });
  test('optionalNumber: gol = nedeclarat, text nevalid = NaN (nu trece nicio verificare de interval)', () => {
    assert.equal(optionalNumber(''), null); assert.equal(optionalNumber('   '), null); assert.equal(optionalNumber('240'), 240); assert.equal(optionalNumber(' 2,5 '), 2.5);
    assert.ok(Number.isNaN(optionalNumber('abc')));
  });
});

describe('traseul construit din punctele desenate', () => {
  test('toate punctele la cota declarată, în ordinea clicurilor; nimic completat în locul utilizatorului', () => {
    const run = ok(runFromPlanPoints(base));
    assert.deepEqual(run, { id: 'mep-1', system: 'water_cold', points: [[0, 2.4, 0], [2, 2.4, 0], [2, 2.4, 1.5]] });
    for (const k of ['diameterMm', 'material', 'slopePct', 'fromId', 'toId'] as const) assert.equal(k in run, false, k);
    assert.deepEqual(ok(runFromPlanPoints({ ...base, diameterMm: 32 })).diameterMm, 32);
    assert.equal('diameterMm' in ok(runFromPlanPoints({ ...base, diameterMm: null })), false);
    // cota 0 (în șapă) și cota tavanului sunt valide, ca în panou
    assert.deepEqual(ok(runFromPlanPoints({ ...base, heightM: 0 })).points[0], [0, 0, 0]);
    assert.deepEqual(ok(runFromPlanPoints({ ...base, heightM: 2.6 })).points[0], [0, 2.6, 0]);
  });
  test('fiecare sistem al modelului se poate desena și trece de sanitizeMepRun neschimbat', () => {
    for (const system of Object.keys(MEP_SYSTEMS) as MepSystem[]){ const run = ok(runFromPlanPoints({ ...base, system })); assert.deepEqual(sanitizeMepRun(run, 2.6), run, system); }
  });
  test('punctele duble consecutive se strâng; sub două puncte distincte nu iese niciun traseu', () => {
    assert.deepEqual(ok(runFromPlanPoints({ ...base, points: [[0, 0], [0, 0], [1, 0], [1, 0.0004]] })).points, [[0, 2.4, 0], [1, 2.4, 0]]);
    assert.equal(err(runFromPlanPoints({ ...base, points: [] })), 'points');
    assert.equal(err(runFromPlanPoints({ ...base, points: [[1, 1]] })), 'points');
    assert.equal(err(runFromPlanPoints({ ...base, points: [[1, 1], [1, 1]] })), 'points');
    assert.equal(err(runFromPlanPoints({ ...base, points: [[0, 0], [NaN, 1]] })), 'points');
    assert.equal(err(runFromPlanPoints({ ...base, points: Array.from({ length: MEP_LIMITS.maxPoints + 1 }, (_, i): P2 => [i, 0]) })), 'points');
    assert.equal(ok(runFromPlanPoints({ ...base, points: Array.from({ length: MEP_LIMITS.maxPoints }, (_, i): P2 => [i, 0]) })).points.length, MEP_LIMITS.maxPoints);
  });
  test('fără cotă declarată sau cu cota în afara nivelului nu iese niciun traseu; la fel cu un diametru în afara limitelor', () => {
    for (const heightM of [null, undefined, NaN, -0.01, 2.61, Infinity]) assert.equal(err(runFromPlanPoints({ ...base, heightM })), 'height', String(heightM));
    for (const diameterMm of [MEP_LIMITS.minDiameterMm - 1, MEP_LIMITS.maxDiameterMm + 1, NaN, 0]) assert.equal(err(runFromPlanPoints({ ...base, diameterMm })), 'diameter', String(diameterMm));
    assert.equal(ok(runFromPlanPoints({ ...base, diameterMm: MEP_LIMITS.minDiameterMm })).diameterMm, MEP_LIMITS.minDiameterMm);
    assert.equal(ok(runFromPlanPoints({ ...base, diameterMm: MEP_LIMITS.maxDiameterMm })).diameterMm, MEP_LIMITS.maxDiameterMm);
    assert.equal(err(runFromPlanPoints({ ...base, system: 'gaz' as MepSystem })), 'system');
  });
});

describe('traseul desenat în restul aplicației', () => {
  const withRun = (o: Partial<Parameters<typeof runFromPlanPoints>[0]> = {}) => { const s = blank(); const run = ok(runFromPlanPoints({ ...base, ceilingM: s.floor.ceilingHeight, heightM: Math.min(2.4, s.floor.ceilingHeight), ...o })); s.floor.mepRuns = [run]; return { s, run }; };
  test('drum dus-întors prin checkSnapshot (server): traseul rămâne identic, și după a doua trecere', () => {
    const { s, run } = withRun({ diameterMm: 110, system: 'drain' });
    const once: any = checkSnapshot(JSON.parse(JSON.stringify(s))), twice: any = checkSnapshot(JSON.parse(JSON.stringify(once)));
    assert.deepEqual(once.floor.mepRuns, [run]); assert.deepEqual(twice.floor.mepRuns, [run]);
    const bare = withRun(); const back: any = checkSnapshot(JSON.parse(JSON.stringify(bare.s)));
    assert.deepEqual(back.floor.mepRuns, [bare.run]); assert.equal('diameterMm' in back.floor.mepRuns[0], false, 'diametrul nedeclarat rămâne nedeclarat');
  });
  test('diametrul nedeclarat e semnalat ca necunoscut, nu inventat; canalizarea fără pantă primește avertismentul ei', () => {
    assert.deepEqual(mepIssues(withRun().s.floor).map(i => i.code), ['MEP_DIAMETER_UNKNOWN']);
    assert.deepEqual(mepIssues(withRun({ diameterMm: 110 }).s.floor), []);
    assert.deepEqual(mepIssues(withRun({ system: 'drain', diameterMm: 110 }).s.floor).map(i => i.code), ['MEP_DRAIN_SLOPE_UNDECLARED']);
  });
  test('apare în Structură cu sistemul și lungimea; se încadrează pe conturul punctelor', () => {
    const { s, run } = withRun({ diameterMm: 32 });
    for (const lang of ['ro', 'en'] as const){ const items = describePlan(s, cat, { t: translator(lang), units: 'metric', lang }).flatMap(g => g.items), it = items.find(i => i.kind === 'mep_run' && i.id === run.id);
      assert.ok(it, lang); assert.ok(it!.label.includes(MEP_SYSTEMS.water_cold.label[lang]), it!.label); assert.ok(it!.label.includes('32'), it!.label); }
    assert.deepEqual(selectionBox(s, cat, { kind: 'mep_run', id: run.id }), { x0: 0, x1: 2, z0: 0, z1: 1.5 });
    assert.equal(selectionBox(s, cat, { kind: 'mep_run', id: 'lipsa' }), null);
  });
  test('verificarea de coliziuni primește traseul desenat: două trasee care se încrucișează la aceeași cotă sunt găsite', () => {
    const s = blank(), h = Math.min(2.4, s.floor.ceilingHeight);
    const a = ok(runFromPlanPoints({ ...base, id: 'mep-1', points: [[0, 1], [4, 1]], heightM: h, ceilingM: s.floor.ceilingHeight, diameterMm: 50 }));
    const b = ok(runFromPlanPoints({ ...base, id: 'mep-2', system: 'drain', points: [[2, 0], [2, 3]], heightM: h, ceilingM: s.floor.ceilingHeight, diameterMm: 110 }));
    s.floor.mepRuns = [a, b];
    const found = detectClashes(s, cat); assert.ok(found.some(c => JSON.stringify(c).includes('mep-1') && JSON.stringify(c).includes('mep-2')), JSON.stringify(found));
    s.floor.mepRuns = [a]; assert.equal(detectClashes(s, cat).some(c => JSON.stringify(c).includes('mep-2')), false);
  });
});

describe('unealta în registru și în bară', () => {
  const ev = (key: string, m: Partial<{ shiftKey: boolean; ctrlKey: boolean; metaKey: boolean; altKey: boolean }> = {}) => ({ key, shiftKey: false, ctrlKey: false, metaKey: false, altKey: false, ...m });
  test('Shift+I alege unealta; I rămâne la punctele tehnice; cu Ctrl/⌘/Alt nimic', () => {
    assert.ok(TOOLS.includes('mep_run')); assert.deepEqual(toolShortcut('mep_run'), { key: 'i', shift: true });
    assert.equal(formatShortcut(toolShortcut('mep_run'), { mac: false }), 'Shift+I');
    assert.equal(matchCommand(ev('I', { shiftKey: true })), 'tool.mep_run'); assert.equal(matchCommand(ev('i', { shiftKey: true })), 'tool.mep_run');
    assert.equal(matchCommand(ev('i')), 'tool.tech');
    for (const m of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }]) assert.equal(matchCommand(ev('i', { shiftKey: true, ...m })), null);
    // nicio altă unealtă nu are aceeași scurtătură
    assert.deepEqual(TOOLS.filter(tk => JSON.stringify(toolShortcut(tk)) === JSON.stringify(toolShortcut('mep_run'))), ['mep_run']);
  });
  test('traseul ales are meniu contextual (încadrează, șterge)', () => assert.deepEqual(contextCommands('mep_run'), ['frame', 'delete']));
  test('etapa Instalații: unealta are locul ei în bară, lângă punctele tehnice', () => {
    // ordinea din ICON (components/Editor.tsx): shaft, measure, tech, mep_run
    const mep: Tool[] = ['select', 'pan', 'zoom_area', 'shaft', 'measure', 'tech', 'mep_run'];
    assert.deepEqual(railSlots(mep, true).map(s => 'tool' in s ? s.tool : s.group), ['select', 'view', 'shaft', 'measure', 'tech', 'mep_run']);
    assert.deepEqual(railSlots(mep, false).map(s => 'tool' in s ? s.tool : s.group), mep);
  });
  test('textele uneltei există în română și în engleză', () => {
    for (const lang of ['ro', 'en'] as const) for (const k of ['tool.mep_run', 'editor.hintMepRun', 'mepRun.height', 'mepRun.needHeight', 'mepRun.tooShort']) assert.ok(DICTS[lang][k], `${lang} ${k}`);
    assert.equal(translator('ro')('tool.mep_run'), 'Traseu instalații');
  });
});
