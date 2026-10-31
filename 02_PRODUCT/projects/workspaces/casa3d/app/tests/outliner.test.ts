// A1-3: arborele outliner din grupele oglinzii text; căutarea căii selecției; rândurile vizibile.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { describePlan, type MirrorGroup, type MirrorItem } from '../core/plan-describe';
import { buildOutliner, findPath, visibleRows, type OutlinerNode } from '../core/outliner';
import { translator } from '../lib/i18n';
import type { Catalog, MaterialsCatalog } from '../core/types';

const it = (kind: MirrorItem['kind'], id: string, extra: Partial<MirrorItem> = {}): MirrorItem => ({ kind, id, label: `${kind} ${id}`, ...extra });
const G = (id: string, title: string, items: MirrorItem[]): MirrorGroup => ({ id, title, items });
const level0: MirrorGroup[] = [
  G('r1', 'Cameră 1', [it('room', 'r1'), it('placement', 'p1'), it('placement', 'p2')]),
  G('r2', 'Cameră 2', [it('placement', 'p3')]),
  G('loose', 'Piese libere', [it('placement', 'p4')]),
  G('structure', 'Structură', [it('wall', 'w1'), it('opening', 'o1', { parent: 'w1', wallId: 'w1' }), it('niche', 'n1', { parent: 'w1' }), it('wall', 'w2'), it('column', 'c1'), it('opening', 'o9', { parent: 'missing' })]),
  G('notes', 'Note', [it('annotation', 'a1')]),
];
const tree = buildOutliner([{ name: 'Parter', groups: level0 }, { name: 'Etaj', groups: [G('r1', 'X', [it('room', 'r1')])] }]);
const keys = (ns: readonly { key: string }[]) => ns.map(n => n.key);

test('rădăcini: câte una pe nivel, fără item', () => {
  assert.deepEqual(keys(tree), ['L0', 'L1']);
  assert.equal(tree[0]!.label, 'Parter'); assert.equal(tree[1]!.level, 1); assert.equal(tree[0]!.item, undefined);
});
test('cameră: nod unic cu piesele drept copii; fără cameră: antet cu toate elementele', () => {
  const [r1, r2, loose, structure, notes] = tree[0]!.children as [OutlinerNode, OutlinerNode, OutlinerNode, OutlinerNode, OutlinerNode];
  assert.equal(r1.key, 'L0:room:r1'); assert.equal(r1.item!.kind, 'room'); assert.deepEqual(keys(r1.children), ['L0:placement:p1', 'L0:placement:p2']);
  assert.equal(r1.children[0]!.level, 0); assert.deepEqual(r1.children[0]!.children, []);
  // camera arată numele (titlul grupei); descrierea completă rămâne pentru cititorul de ecran; piesele n-au descriere separată
  assert.equal(r1.label, 'Cameră 1'); assert.equal(r1.desc, 'room r1'); assert.equal(r1.children[0]!.desc, undefined);
  assert.equal(r2.key, 'L0:g:r2'); assert.equal(r2.label, 'Cameră 2'); assert.equal(r2.item, undefined); assert.deepEqual(keys(r2.children), ['L0:placement:p3']);
  assert.equal(loose.key, 'L0:g:loose'); assert.equal(loose.label, 'Piese libere'); assert.deepEqual(keys(loose.children), ['L0:placement:p4']);
  assert.equal(notes.key, 'L0:g:notes'); assert.deepEqual(keys(notes.children), ['L0:annotation:a1']);
  assert.equal(structure.key, 'L0:g:structure');
});
test('structură: golul și nișa sub peretele lor; părinte lipsă rămâne la nivelul antetului', () => {
  const s = tree[0]!.children[3]!;
  assert.deepEqual(keys(s.children), ['L0:wall:w1', 'L0:wall:w2', 'L0:column:c1', 'L0:opening:o9']);
  assert.deepEqual(keys(s.children[0]!.children), ['L0:opening:o1', 'L0:niche:n1']);
  assert.deepEqual(s.children[1]!.children, []);
});
test('două niveluri: cheile nu se ciocnesc', () => {
  assert.deepEqual(keys(tree[1]!.children), ['L1:room:r1']);
});
test('findPath: gol imbricat, cameră, piesă din cameră, id lipsă, sel null, nivel greșit', () => {
  assert.deepEqual(findPath(tree, 0, { kind: 'opening', id: 'o1' }), { key: 'L0:opening:o1', ancestors: ['L0', 'L0:g:structure', 'L0:wall:w1'] });
  assert.deepEqual(findPath(tree, 0, { kind: 'room', id: 'r1' }), { key: 'L0:room:r1', ancestors: ['L0'] });
  assert.deepEqual(findPath(tree, 0, { kind: 'placement', id: 'p2' }), { key: 'L0:placement:p2', ancestors: ['L0', 'L0:room:r1'] });
  assert.equal(findPath(tree, 0, { kind: 'opening', id: 'nope' }), null);
  assert.equal(findPath(tree, 0, { kind: 'wall', id: 'o1' }), null, 'tipul contează');
  assert.equal(findPath(tree, 0, null), null);
  assert.equal(findPath(tree, 1, { kind: 'opening', id: 'o1' }), null);
  assert.deepEqual(findPath(tree, 1, { kind: 'room', id: 'r1' }), { key: 'L1:room:r1', ancestors: ['L1'] });
  assert.equal(findPath(tree, 7, { kind: 'room', id: 'r1' }), null);
});
test('visibleRows: nimic deschis, rădăcină deschisă, expandare adâncă, posinset/setsize', () => {
  const none = visibleRows(tree, new Set());
  assert.deepEqual(none.map(r => [r.node.key, r.depth, r.parent, r.index, r.setSize]), [['L0', 0, null, 1, 2], ['L1', 0, null, 2, 2]]);
  const root = visibleRows(tree, new Set(['L0']));
  assert.deepEqual(keys(root.map(r => r.node)), ['L0', 'L0:room:r1', 'L0:g:r2', 'L0:g:loose', 'L0:g:structure', 'L0:g:notes', 'L1']);
  assert.deepEqual(root.slice(1, 6).map(r => [r.depth, r.parent, r.index, r.setSize]), [[1, 'L0', 1, 5], [1, 'L0', 2, 5], [1, 'L0', 3, 5], [1, 'L0', 4, 5], [1, 'L0', 5, 5]]);
  const deep = visibleRows(tree, new Set(['L0', 'L0:g:structure', 'L0:wall:w1']));
  const o1 = deep.find(r => r.node.key === 'L0:opening:o1')!;
  assert.deepEqual([o1.depth, o1.parent, o1.index, o1.setSize], [3, 'L0:wall:w1', 1, 2]);
  assert.equal(deep.find(r => r.node.key === 'L0:niche:n1')!.index, 2);
  assert.ok(!deep.some(r => r.node.key === 'L0:placement:p1'), 'camera 1 rămâne închisă');
  assert.equal(visibleRows(tree, new Set(['L0:g:structure'])).length, 2, 'copil deschis sub părinte închis nu apare');
});
test('integrare: describePlan real -> golurile stau sub pereți', () => {
  const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
  const snap = newSnapshot(cat, 'Outliner', 'demo', 'ro', { materials: mc });
  const groups = describePlan(snap, cat, { t: translator('ro'), units: 'metric', lang: 'ro' });
  const [root] = buildOutliner([{ name: 'Parter', groups }]);
  const struct = root!.children.find(n => n.key === 'L0:g:structure')!;
  assert.ok(struct.children.every(n => n.item?.kind !== 'opening'), 'niciun gol la nivelul antetului');
  const nested = struct.children.filter(n => n.item?.kind === 'wall').flatMap(n => n.children);
  assert.equal(nested.filter(n => n.item?.kind === 'opening').length, snap.floor.walls.reduce((a, w) => a + w.openings.length, 0));
  const firstRoom = groups[0]!.items[0]!;
  assert.deepEqual(findPath([root!], 0, { kind: 'room', id: firstRoom.id })?.ancestors, ['L0']);
});
