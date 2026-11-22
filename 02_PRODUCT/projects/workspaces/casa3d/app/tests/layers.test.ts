// Straturile planului 2D (core/layers.ts): starea per proiect, regula etapă + utilizator, ce se poate alege din plan;
// plus intrarea „Straturi…” din meniul Vizualizare și textele în ambele limbi.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { LAYER_IDS, defaultLayers, layersKey, parseLayers, serializeLayers, visible, stageAllows, finishMarksInStage, toggleLayer, hiddenLayers, layerOfKind, layerOfTool, pickable, type LayerId } from '../core/layers';
import { buildMenus } from '../components/MenuBar';
import type { Command } from '../components/CommandPalette';
import { translator } from '../lib/i18n';

const STAGES = ['survey', 'concept', 'design', 'ffe', 'finishes', 'mep', 'docs'];

test('cheia de stocare e per proiect', () => {
  assert.equal(layersKey('abc'), 'casa3d.layers.abc');
  assert.notEqual(layersKey('a'), layersKey('b'));
});

test('implicit: toate straturile vizibile; cele opt categorii cerute', () => {
  assert.deepEqual([...LAYER_IDS], ['furniture', 'annotations', 'tech', 'mep', 'finishes', 'wallItems', 'underlay', 'roomLabels']);
  const d = defaultLayers();
  for (const id of LAYER_IDS) assert.equal(d[id], true);
  assert.deepEqual(hiddenLayers(d), []);
  assert.notEqual(defaultLayers(), defaultLayers(), 'obiect nou la fiecare apel (nu se strică implicitul prin mutație)');
});

test('parse / serialize: dus-întors; se salvează doar straturile ascunse', () => {
  const s = toggleLayer(toggleLayer(defaultLayers(), 'furniture'), 'underlay');
  const raw = serializeLayers(s);
  assert.deepEqual(JSON.parse(raw), { v: 1, hidden: ['furniture', 'underlay'] });
  assert.deepEqual(parseLayers(raw), s);
  assert.deepEqual(parseLayers(serializeLayers(defaultLayers())), defaultLayers());
});

test('parse: datele stricate sau străine dau implicitul, fără excepții', () => {
  const d = defaultLayers();
  for (const raw of [null, undefined, '', 'nu e json', '[]', 'null', '42', '{"v":2,"hidden":["furniture"]}', '{"hidden":["furniture"]}', '{"v":1}', '{"v":1,"hidden":"furniture"}'])
    assert.deepEqual(parseLayers(raw), d, String(raw));
  assert.deepEqual(hiddenLayers(parseLayers('{"v":1,"hidden":["nope",7,null,"tech","__proto__","tech"]}')), ['tech'], 'straturile necunoscute se ignoră');
  assert.equal(({} as Record<string, unknown>).hidden, undefined, 'fără poluarea prototipului');
});

test('toggleLayer nu modifică starea primită', () => {
  const a = defaultLayers(), b = toggleLayer(a, 'mep');
  assert.equal(a.mep, true); assert.equal(b.mep, false); assert.equal(toggleLayer(b, 'mep').mep, true);
});

test('visible: implicit se vede exact ce se vedea în fiecare etapă', () => {
  const d = defaultLayers();
  for (const st of STAGES) for (const id of LAYER_IDS)
    assert.equal(visible(id, st, d), id === 'wallItems' ? st !== 'survey' : true, `${id} @ ${st}`);
  assert.equal(visible('furniture', 'ffe', null), true, 'fără stare (înainte de citirea din browser) = implicit');
  assert.equal(visible('furniture', 'ffe', {}), true);
  assert.deepEqual(STAGES.filter(finishMarksInStage), ['finishes', 'docs']);
});

test('visible: utilizatorul ascunde peste regula etapei, dar nu arată ce ascunde etapa', () => {
  for (const id of LAYER_IDS){ const off = toggleLayer(defaultLayers(), id);
    for (const st of STAGES){ assert.equal(visible(id, st, off), false, `${id} ascuns @ ${st}`);
      for (const other of LAYER_IDS) if (other !== id) assert.equal(visible(other, st, off), stageAllows(other, st), 'celelalte straturi nu se schimbă'); } }
  assert.equal(stageAllows('wallItems', 'survey'), false);
  assert.equal(visible('wallItems', 'survey', defaultLayers()), false, 'bifa utilizatorului nu bate etapa');
});

test('layerOfKind / pickable: pereții, golurile și camerele nu au strat și se pot alege mereu', () => {
  const none = Object.fromEntries(LAYER_IDS.map(id => [id, false])) as Record<LayerId, boolean>;
  for (const k of ['wall', 'room', 'opening', 'stair', 'column', 'beam', 'glass_wall', 'niche', 'railing']){ assert.equal(layerOfKind(k), null, k); assert.equal(pickable(k, none), true, k); }
  assert.equal(layerOfKind('placement'), 'furniture'); assert.equal(layerOfKind('annotation'), 'annotations'); assert.equal(layerOfKind('wall_item'), 'wallItems');
  assert.equal(layerOfKind('floor_zone'), 'finishes'); assert.equal(layerOfKind('ceiling_zone'), 'finishes'); assert.equal(layerOfKind('mep_run'), 'mep');
  for (const k of ['placement', 'annotation', 'wall_item', 'floor_zone', 'ceiling_zone', 'mep_run']){ assert.equal(pickable(k, none), false, k); assert.equal(pickable(k, defaultLayers()), true, k); assert.equal(pickable(k, null), true, k); }
  assert.equal(pickable('placement', toggleLayer(defaultLayers(), 'annotations')), true, 'doar stratul lui îl scoate din selecție');
});

const cmd = (id: string): Command => ({ id, label: id, group: 'g', run: () => {} });
test('meniul Vizualizare: „Straturi…” (id layers) după vederi; comenzile pe strat rămân doar în paletă', () => {
  const m = buildMenus(['v-2d', 'v-3d', 'layers', 'ws.focus', 'theme', ...LAYER_IDS.map(id => `layer-${id}`)].map(cmd), k => k);
  const view = m.find(x => x.id === 'view')!;
  assert.deepEqual(view.items.map(x => x === 'sep' ? '|' : x.id), ['v-2d', 'v-3d', '|', 'layers', '|', 'ws.focus', 'theme']);
  assert.ok(!m.flatMap(x => x.items).some(x => x !== 'sep' && x.id.startsWith('layer-')));
});

test('texte ro + en pentru fiecare strat și pentru panou', () => {
  for (const lang of ['ro', 'en'] as const){ const t = translator(lang);
    for (const k of [...LAYER_IDS.map(id => `layers.${id}`), 'layers.title', 'layers.menu', 'layers.showAll', 'layers.aria', 'layers.stageOff', 'layers.cmdHide', 'layers.cmdShow', 'layers.hiddenCount', 'layers.revealed', 'layers.close', 'layers.hidden', 'layers.shown', 'layers.autoShown', 'layers.allShown'])
      assert.ok(t(k) && t(k) !== k, `${lang}: ${k}`); }
  assert.equal(translator('ro')('layers.title'), 'Straturi'); assert.equal(translator('ro')('layers.menu'), 'Straturi…'); assert.equal(translator('ro')('layers.showAll'), 'Arată tot');
});

test('planșele tipărite și exportul DXF nu citesc straturile (documentul rămâne complet)', () => {
  const src = (f: string) => readFileSync(join(__dirname, '..', f), 'utf8');
  for (const f of ['components/PrintView.tsx', 'components/PrintPage.tsx', 'lib/dxfExport.ts', 'core/dxf.ts', 'core/drawings.ts'])
    assert.ok(!/core\/layers|casa3d\.layers/.test(src(f)), f);
});

test('layerOfTool: uneltele care desenează într-un strat; cele de structură nu au strat', () => {
  for (const tk of ['dim', 'text', 'leader', 'level_mark', 'angle_dim', 'rev_cloud']) assert.equal(layerOfTool(tk), 'annotations', tk);
  assert.equal(layerOfTool('tech'), 'tech'); assert.equal(layerOfTool('mep_run'), 'mep'); assert.equal(layerOfTool('wall_item'), 'wallItems');
  for (const tk of ['clad', 'floor_zone', 'ceiling_zone', 'rug']) assert.equal(layerOfTool(tk), 'finishes', tk);
  for (const tk of ['select', 'wall', 'room', 'door', 'window', 'measure', 'pan']) assert.equal(layerOfTool(tk), null, tk);
});
