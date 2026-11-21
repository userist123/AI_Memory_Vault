// Faza 1 din ghidul BIM (docs/bim/GHID_BIM.md): identitatea elementelor, disciplinele, diff-ul pe structură și instalații,
// constatările de coordonare cu ID stabil și raportul care spune ce reguli au fost verificate efectiv.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { diffSnapshots } from '../core/diff';
import { elementIndex, elementsOf, duplicateElementIds, DISCIPLINES } from '../core/elements';
import { reviewProjectCoordination, runCoordination, findingId } from '../core/coordination-review';
import type { Catalog, Snapshot } from '../core/types';
import type { TechPoint } from '../core/technical';

const cat: Catalog = { suppliers: [], products: [], variants: [], offers: [] };
const tp = (id: string, kind: TechPoint['kind'], x = 1, roomId = 'r1'): TechPoint => ({ id, kind, roomId, x, z: 1, height: .3, reason: 't' });
const base = (): Snapshot => ({
  name: 'T',
  floor: { id: 'f0', name: 'Parter', ceilingHeight: 2.6,
    rooms: [{ id: 'r1', name: 'Living', type: 'living', rect: { x0: 0, z0: 0, x1: 4, z1: 3 } }],
    walls: [{ id: 'w1', a: [0, 0], b: [4, 0], thickness: .2, exterior: true, openings: [{ id: 'o1', kind: 'door', offset: 1, width: .9 }] }],
    columns: [{ id: 'c1', x: 1, z: 1, w: .3, d: .3, shape: 'rect' }, { id: 'g1', x: 3, z: 2, w: .4, d: .4, shape: 'rect', role: 'shaft' }],
    beams: [{ id: 'b1', a: [0, 1.5], b: [4, 1.5], width: .25, depth: .4 }] },
  levels: [{ id: 'f1', name: 'Etaj', ceilingHeight: 2.5, rooms: [{ id: 'r2', name: 'Dormitor', type: 'bedroom', rect: { x0: 0, z0: 0, x1: 3, z1: 3 } }], walls: [] }],
  placements: [{ id: 'p1', roomId: 'r2', group: 'pat', variantId: 'pat-0', x: 1, z: 1, rotation: 0, source: 'manual' }],
  selections: {}, picked: [],
  tech: [tp('t1', 'outlet'), tp('t2', 'water_cold'), tp('t3', 'hvac_split'), tp('t4', 'outlet', 1, 'r2')],
});

// ---------- diff: structură și instalații ----------
test('diff: un stâlp mutat, o grindă ștearsă și un stâlp nou apar la structură', () => {
  const a = base(), b = structuredClone(a);
  b.floor.columns![0]!.x = 1.5; b.floor.beams = []; b.floor.columns!.push({ id: 'c2', x: 2, z: 2, w: .3, d: .3, shape: 'round' });
  const d = diffSnapshots(a, b, cat);
  assert.deepEqual(d.structure, { added: 1, removed: 1, changed: 1 });
  assert.equal(d.isEmpty, false);
});
test('diff: punctele tehnice adăugate, șterse și mutate se numără separat', () => {
  const a = base(), b = structuredClone(a);
  b.tech = b.tech!.filter(t => t.id !== 't2'); b.tech[0]!.x = 2; b.tech.push(tp('t9', 'drain'));
  const d = diffSnapshots(a, b, cat);
  assert.deepEqual(d.tech, { added: 1, removed: 1, changed: 1 });
  assert.equal(d.isEmpty, false);
});
test('diff: doar o schimbare de structură sau de instalații nu mai trece drept „nicio diferență”', () => {
  const a = base(), onlyBeam = structuredClone(a); onlyBeam.floor.beams![0]!.depth = .5;
  assert.equal(diffSnapshots(a, onlyBeam, cat).isEmpty, false);
  const noTech = structuredClone(a); delete noTech.tech;
  assert.equal(diffSnapshots(a, noTech, cat).isEmpty, false);
  assert.equal(diffSnapshots(a, structuredClone(a), cat).isEmpty, true);
});
test('diff: proiecte vechi fără stâlpi, grinzi și strat tehnic dau zero, nu eroare', () => {
  const a = base(); delete a.floor.columns; delete a.floor.beams; delete a.tech; delete a.levels;
  const d = diffSnapshots(a, structuredClone(a), cat);
  assert.deepEqual(d.structure, { added: 0, removed: 0, changed: 0 });
  assert.deepEqual(d.tech, { added: 0, removed: 0, changed: 0 });
});

// ---------- indexul de elemente ----------
test('index: fiecare element are id, disciplină, tip și nivel; ghena nu e structură', () => {
  const ix = elementIndex(base());
  const by = new Map(ix.map(e => [e.elementId, e]));
  assert.deepEqual(by.get('w1'), { elementId: 'w1', discipline: 'architecture', elementType: 'wall', levelId: 'f0', units: 'm' });
  assert.equal(by.get('o1')!.elementType, 'door');
  assert.equal(by.get('c1')!.discipline, 'structure');
  assert.equal(by.get('g1')!.discipline, 'architecture'); assert.equal(by.get('g1')!.elementType, 'shaft');
  assert.equal(by.get('b1')!.elementType, 'beam');
  assert.equal(by.get('t1')!.discipline, 'electrical');
  assert.equal(by.get('t2')!.discipline, 'plumbing');
  assert.equal(by.get('t3')!.discipline, 'mechanical');
  assert.equal(by.get('t4')!.levelId, 'f1', 'punctul tehnic ia nivelul camerei lui');
  assert.equal(by.get('p1')!.discipline, 'interior'); assert.equal(by.get('p1')!.levelId, 'f1');
  for (const e of ix) assert.ok(DISCIPLINES.includes(e.discipline));
});
test('index: filtrul pe disciplină întoarce doar elementele ei', () => {
  const s = base();
  assert.deepEqual(elementsOf(s, 'structure').map(e => e.elementId).sort(), ['b1', 'c1']);
  assert.deepEqual(elementsOf(s, 'electrical').map(e => e.elementId).sort(), ['t1', 't4']);
});
test('index: un id folosit de două elemente e semnalat (identitatea trebuie să fie unică pe toată casa)', () => {
  const s = base(); assert.deepEqual(duplicateElementIds(s), []);
  s.levels![0]!.walls.push({ id: 'w1', a: [0, 0], b: [1, 0], thickness: .1, exterior: false, openings: [] });
  assert.deepEqual(duplicateElementIds(s), ['w1']);
});
test('index: proveniența nu e inventată — elementele existente nu au sursă declarată', () => {
  for (const e of elementIndex(base())) assert.equal('confidence' in e || 'reviewStatus' in e || 'sourceRef' in e, false);
});

// ---------- coordonarea ----------
test('coordonare: fiecare constatare are id stabil, același la o a doua rulare și independent de ordinea referințelor', () => {
  const s = base(); s.underlay = { dataUrl: 'data:image/png;base64,AAAA', x: 0, z: 0, widthM: 5, opacity: .5, locked: false, confirmed: false };
  const a = reviewProjectCoordination(s, cat), b = reviewProjectCoordination(structuredClone(s), cat);
  assert.ok(a.length > 0);
  for (const f of a) assert.match(f.id, /^cf-[0-9a-f]{8}$/);
  assert.deepEqual(a.map(f => f.id), b.map(f => f.id));
  assert.equal(findingId('X', 'r', ['b', 'a']), findingId('X', 'r', ['a', 'b']));
  assert.notEqual(findingId('X', 'r', ['a']), findingId('X', 'r2', ['a']));
});
test('coordonare: constatările duplicate (același cod, regulă și elemente) apar o singură dată', () => {
  const ids = reviewProjectCoordination(base(), cat).map(f => f.id);
  assert.equal(new Set(ids).size, ids.length);
});
test('coordonare: raportul spune revizia, regulile rulate și numără pe severitate — fără „verde” pentru reguli nerulate', () => {
  const s = base(); delete s.tech;
  const r = runCoordination(s, cat, { revision: 7 });
  assert.equal(r.revision, 7);
  assert.ok(r.rulesChecked.includes('casa3d.geometry.floor-validator'));
  assert.equal(r.rulesChecked.includes('casa3d.technical.switch-group-check'), false, 'fără strat tehnic, regula nu a rulat');
  assert.ok(r.rulesNotChecked.includes('casa3d.technical.switch-group-check'));
  const total = Object.values(r.counts).reduce((x, y) => x + y, 0);
  assert.equal(total, r.findings.length);
  assert.match(r.statement, /revizia 7/);
  assert.match(r.statement, /nu înseamnă conformitate/);
  const draft = runCoordination(base(), cat);
  assert.equal(draft.revision, 'draft');
  assert.ok(draft.rulesChecked.includes('casa3d.technical.switch-group-check'));
});

// ---------- corecturi după revizia independentă ----------
import { elementIdOf } from '../core/scene-ids';
import { readFileSync } from 'node:fs';
test('scenă: id-ul elementului se ia de pe obiect sau de pe părinți; pereții fără id dau null', () => {
  const group = { userData: { pid: 'p1' }, parent: null }, mesh = { userData: {}, parent: group };
  assert.equal(elementIdOf(mesh), 'p1');
  assert.equal(elementIdOf({ userData: { eid: 'c1', wall: true } }), 'c1');
  assert.equal(elementIdOf({ userData: { tech: 't1' } }), 't1');
  assert.equal(elementIdOf({ userData: { wall: true } }), null);
  assert.equal(elementIdOf({ userData: { pid: 'x'.repeat(100) } })!.length, 64);
});
test('scenă: exportScene folosește elementIdOf importat, nu o copie rămasă într-un comentariu', () => {
  const src = readFileSync(new URL('../components/viewer3d-engine.js', import.meta.url), 'utf8');
  assert.match(src, /^import \{ elementIdOf \} from '\.\.\/core\/scene-ids';$/m);
  assert.ok(/id = elementIdOf\(o\)/.test(src));
  assert.equal(/\/\/[^\n]*\\n/.test(src.split('function exportScene')[1]!.split('function exportGLB')[0]!), false, 'fără „\\n” literal în comentariile din exportScene');
});
test('coordonare: constatările fără elemente țintă, cu mesaje diferite, nu se contopesc', () => {
  assert.notEqual(findingId('X', 'r', [], 'pe parter'), findingId('X', 'r', [], 'pe etaj'));
  assert.equal(findingId('X', 'r', [], ' a   b '), findingId('X', 'r', [], 'a b'));
  assert.equal(findingId('X', 'r', ['e1'], 'm1'), findingId('X', 'r', ['e1'], 'm2'), 'cu elemente, mesajul nu contează');
});
test('diff: un id repetat (stâlp copiat cu același id) nu ascunde schimbarea copiei', () => {
  const a = base(); a.levels![0]!.columns = [{ ...a.floor.columns![0]! }];
  const b = structuredClone(a); b.levels![0]!.columns![0]!.x = 2;
  assert.deepEqual(diffSnapshots(a, b, cat).structure, { added: 0, removed: 0, changed: 1 });
});
test('index: o piesă cu cameră inexistentă are nivelul null, nu unul ghicit', () => {
  const s = base(); s.placements[0]!.roomId = 'nu-exista';
  assert.equal(elementIndex(s).find(e => e.elementId === 'p1')!.levelId, null);
});
