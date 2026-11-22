// Selecția multiplă fără mouse: atingerea lungă (ecran tactil) și tastele din plan / din lista Structură (core/multi-select.ts).
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  LONG_PRESS_MS, LONG_PRESS_MOVE_PX, MARQUEE_OVER, groupAfterKeyToggle, isGroupToggleKey, isListGroupToggleKey, isLongPress,
  longPressAction, pressTravel, tabMovesCursor, toggleInGroup
} from '../core/multi-select';
import { groupIds, toggleMulti } from '../core/arrange';
import { cycleSelection, planKeyAction, type PlanItem } from '../core/plan-keys';
import { COMMANDS, commandById, helpRows, matchCommand, shortcutMatches, type KeyLike } from '../lib/commands';
import { DICTS, tp } from '../lib/i18n';

const ev = (key: string, mods: Partial<KeyLike> = {}): KeyLike => ({ key, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false, ...mods });
const src = (rel: string) => readFileSync(join(__dirname, '..', rel), 'utf8');

describe('atingerea lungă', () => {
  test('pragurile cerute: 500 ms pe loc, cel mult 8 px mișcare', () => { assert.equal(LONG_PRESS_MS, 500); assert.equal(LONG_PRESS_MOVE_PX, 8); });
  test('e atingere lungă abia de la prag, cu degetul pe loc', () => {
    assert.equal(isLongPress(1000, 1499, 0), false);
    assert.equal(isLongPress(1000, 1500, 0), true);
    assert.equal(isLongPress(1000, 4000, 8), true);     // marginea de mișcare e inclusă
    assert.equal(isLongPress(1000, 4000, 8.01), false); // peste 8 px e tragere, oricât a durat
    assert.equal(isLongPress(1000, 1000, 0), false);    // atingerea simplă
  });
  test('pragurile se pot schimba, iar valorile stricate nu dau atingere lungă', () => {
    assert.equal(isLongPress(0, 300, 0, 300), true); assert.equal(isLongPress(0, 600, 12, 500, 16), true);
    for (const bad of [NaN, Infinity, -Infinity]){ assert.equal(isLongPress(bad, 600, 0), false); assert.equal(isLongPress(0, bad, 0), false); assert.equal(isLongPress(0, 600, bad), false); }
    assert.equal(isLongPress(2000, 1000, 0), false);    // ceas dat înapoi
  });
  test('mișcarea se măsoară în linie dreaptă, în pixeli de ecran', () => {
    assert.equal(pressTravel({ x: 10, y: 10 }, { x: 13, y: 14 }), 5); assert.equal(pressTravel({ x: 0, y: 0 }, { x: 0, y: 0 }), 0);
    assert.equal(isLongPress(0, 500, pressTravel({ x: 0, y: 0 }, { x: 6, y: 6 })), false);   // 8,49 px pe diagonală
    assert.equal(isLongPress(0, 500, pressTravel({ x: 0, y: 0 }, { x: 5, y: 5 })), true);
  });
  test('pe o piesă comută grupul, pe gol / cameră / zonă pornește dreptunghiul, restul își păstrează gestul', () => {
    assert.equal(longPressAction('placement'), 'toggle');
    for (const k of [null, undefined, '', 'room', 'floor_zone', 'ceiling_zone', 'underlay']) assert.equal(longPressAction(k), 'marquee', String(k));
    for (const k of ['wall', 'opening', 'handle', 'redge', 'stair', 'column', 'beam', 'glass_wall', 'niche', 'wall_item', 'annotation', 'railing', 'mep_run', 'leadertip']) assert.equal(longPressAction(k), null, k);
    assert.deepEqual([...MARQUEE_OVER].sort(), ['ceiling_zone', 'floor_zone', 'room', 'underlay']);
  });
  test('atingerea lungă pe o piesă pornește de la grupul de dinaintea atingerii (ca Shift+clic)', () => {
    assert.deepEqual(toggleMulti(['a'], 'b'), ['a', 'b']);       // piesa principală rămâne a
    assert.deepEqual(toggleMulti(['a', 'b'], 'b'), ['a']);
    assert.deepEqual(toggleMulti([], 'b'), ['b']);
  });
});

describe('grupul de la tastatură', () => {
  test('comutarea păstrează piesa principală cât timp nu e ea cea scoasă', () => {
    assert.deepEqual(toggleInGroup(['a'], 'b'), ['a', 'b']);
    assert.deepEqual(toggleInGroup(['a', 'b', 'c'], 'b'), ['a', 'c']);
    assert.deepEqual(toggleInGroup(['a', 'b'], 'a'), ['b']);     // principala scoasă: următoarea îi ia locul
    assert.deepEqual(toggleInGroup([], 'x'), ['x']);
  });
  test('singura piesă aleasă nu se scoate dintr-o tastă (Escape renunță)', () => {
    assert.deepEqual(toggleInGroup(['a'], 'a'), ['a']);
    const g = ['a']; assert.notEqual(toggleInGroup(g, 'a'), g);   // listă nouă, cea primită rămâne neatinsă
  });
  test('rezultatul rămâne un grup valid pentru editor (principala prima, fără dubluri)', () => {
    let g: string[] = ['a']; for (const id of ['b', 'c', 'b', 'd', 'a']) g = toggleInGroup(g, id);
    assert.deepEqual(g, ['c', 'd']); assert.deepEqual(groupIds({ kind: 'placement', id: g[0]! }, g.slice(1)), g);
  });
  test('Shift+Space în plan: doar cu Shift, fără alți modificatori', () => {
    assert.equal(isGroupToggleKey({ key: ' ', shiftKey: true }), true); assert.equal(isGroupToggleKey({ key: 'Spacebar', shiftKey: true }), true);
    assert.equal(isGroupToggleKey({ key: ' ' }), false);
    for (const m of ['ctrlKey', 'metaKey', 'altKey'] as const) assert.equal(isGroupToggleKey({ key: ' ', shiftKey: true, [m]: true }), false, m);
    assert.equal(isGroupToggleKey({ key: 'Enter', shiftKey: true }), false);
  });
  test('în Structură: Shift+Enter sau Shift+Space; Enter și Space simple rămân „alege”', () => {
    assert.equal(isListGroupToggleKey({ key: 'Enter', shiftKey: true }), true); assert.equal(isListGroupToggleKey({ key: ' ', shiftKey: true }), true);
    assert.equal(isListGroupToggleKey({ key: 'Enter' }), false); assert.equal(isListGroupToggleKey({ key: ' ' }), false);
    assert.equal(isListGroupToggleKey({ key: 'Enter', shiftKey: true, ctrlKey: true }), false); assert.equal(isListGroupToggleKey({ key: 'ArrowDown', shiftKey: true }), false);
  });
  test('obiectul curent intră în grup doar dacă e piesă', () => {
    assert.deepEqual(groupAfterKeyToggle(['a'], { kind: 'placement', id: 'b' }), ['a', 'b']);
    assert.deepEqual(groupAfterKeyToggle(['a'], { kind: 'placement', id: 'a' }), ['a']);   // prima apăsare doar „prinde” grupul
    assert.deepEqual(groupAfterKeyToggle([], { kind: 'placement', id: 'a' }), ['a']);
    for (const kind of ['room', 'stair', 'column', 'wall', 'annotation']) assert.equal(groupAfterKeyToggle(['a'], { kind, id: 'r1' }), null, kind);
    assert.equal(groupAfterKeyToggle(['a'], null), null);
  });
  test('Tab mută doar cursorul abia după ce grupul a fost prins; altfel alege direct, ca înainte', () => {
    assert.equal(tabMovesCursor(false, 0), false); assert.equal(tabMovesCursor(false, 3), false);
    assert.equal(tabMovesCursor(true, 1), true); assert.equal(tabMovesCursor(true, 3), true); assert.equal(tabMovesCursor(true, 0), false);
  });
  test('un parcurs întreg: prinde piesa, Tab la următoarele, adaugă două, scoate una', () => {
    const items: PlanItem[] = [{ kind: 'room', id: 'r', x: 0, z: 0 }, { kind: 'placement', id: 'a', x: 1, z: 1 }, { kind: 'placement', id: 'b', x: 2, z: 1 }, { kind: 'stair', id: 's', x: 3, z: 3 }];
    let group = ['a'], cur: { kind: string; id: string } | null = { kind: 'placement', id: 'a' };
    group = groupAfterKeyToggle(group, cur)!; assert.deepEqual(group, ['a']);
    cur = cycleSelection(items, cur, 1); assert.equal(cur?.id, 'b'); group = groupAfterKeyToggle(group, cur)!; assert.deepEqual(group, ['a', 'b']);
    cur = cycleSelection(items, cur, 1); assert.equal(cur?.kind, 'stair'); assert.equal(groupAfterKeyToggle(group, cur), null);   // scara nu intră
    assert.equal(cycleSelection(items, cur, 1), null);   // după ultimul, Tab iese din plan
    cur = cycleSelection(items, cur, -1); group = groupAfterKeyToggle(group, cur)!; assert.deepEqual(group, ['a']);   // Shift+Tab înapoi pe b, scos
  });
  test('Tab fără grup prins face ce făcea: alege elementul următor', () => {
    const items: PlanItem[] = [{ kind: 'placement', id: 'a', x: 1, z: 1 }, { kind: 'placement', id: 'b', x: 2, z: 1 }];
    assert.deepEqual(planKeyAction({ key: 'Tab' }, items, { kind: 'placement', id: 'a' }), { type: 'select', item: items[1] });
    assert.equal(planKeyAction({ key: ' ', shiftKey: true }, items, { kind: 'placement', id: 'a' }), null);   // Shift+Space nu e o acțiune veche a planului
  });
});

describe('scurtătura în registru și textele', () => {
  test('Shift+Space e în registru, afișată în ajutor și prinsă de plan, nu de dispecerul editorului', () => {
    const c = commandById('select.toggle'); assert.ok(c); assert.equal(c!.dispatch, false);
    assert.equal(matchCommand(ev(' ', { shiftKey: true })), null); assert.equal(matchCommand(ev(' ')), null);
    const row = helpRows({ mac: false }).find(r => r.label === 'help.groupToggle'); assert.ok(row); assert.equal(row!.keysKey, 'help.groupToggleKeys'); assert.equal(row!.section, 'edit');
  });
  test('nicio altă comandă nu răspunde la Shift+Space sau la Space', () => {
    for (const e of [ev(' ', { shiftKey: true }), ev(' ')]) for (const c of COMMANDS) if (c.id !== 'select.toggle') assert.ok(!c.shortcuts.some(s => shortcutMatches(s, e)), c.id);
    assert.ok(commandById('select.toggle')!.shortcuts.some(s => shortcutMatches(s, ev(' ', { shiftKey: true }))));
    assert.ok(!commandById('select.toggle')!.shortcuts.some(s => shortcutMatches(s, ev(' '))));
  });
  test('textele noi există în ro și en; numărul de piese are singular și plural', () => {
    for (const lang of ['ro', 'en'] as const) for (const k of ['plan.groupCount', 'plan.groupCount.one', 'plan.groupCount.many', 'plan.cursorIn', 'plan.cursorOut', 'plan.cursorOther', 'help.groupToggle', 'help.groupToggleKeys'])
      assert.ok(k in DICTS[lang] && DICTS[lang][k]!.trim().length > 0, `${lang} ${k}`);
    assert.equal(tp('ro', 'plan.groupCount', 3), '3 piese selectate'); assert.equal(tp('ro', 'plan.groupCount', 1), 'O piesă selectată'); assert.equal(tp('ro', 'plan.groupCount', 25), '25 de piese selectate');
    assert.equal(tp('en', 'plan.groupCount', 3), '3 items selected'); assert.equal(tp('en', 'plan.groupCount', 1), '1 item selected');
    for (const lang of ['ro', 'en'] as const){ assert.match(DICTS[lang]['outliner.keys']!, /Shift\+Enter/); assert.match(DICTS[lang]['plan.aria']!, /Shift\+Space/); }
  });
});

describe('legăturile din interfață', () => {
  test('planul: atingerea lungă e legată de funcțiile pure, vibrează și nu deschide meniul contextual', () => {
    const s = src('components/PlanView.tsx');
    assert.match(s, /longPressAction\(/); assert.match(s, /isLongPress\(/); assert.match(s, /LONG_PRESS_MOVE_PX/); assert.match(s, /navigator\.vibrate\?\.\(15\)/);
    assert.match(s, /if \(lp\.current \|\| Date\.now\(\) - lpFiredAt\.current < 1000\) return;/);
    assert.match(s, /touches\.current\.size === 2\)\{ cancelLp\(\);/);   // al doilea deget (ciupire) oprește atingerea lungă
    assert.match(s, /isGroupToggleKey\(e\)/); assert.match(s, /tabMovesCursor\(/);
  });
  test('Structura: piesele din grup au aria-selected și semn, arborele e multiselectabil', () => {
    const s = src('components/Outliner.tsx');
    assert.match(s, /aria-multiselectable/); assert.match(s, /aria-selected=\{n\.item \? on \|\| also : undefined\}/); assert.match(s, /is-multi/); assert.match(s, /isListGroupToggleKey\(e\)/);
    assert.match(src('app/globals.css'), /\.ol-row\.is-multi\{/);
  });
  test('editorul anunță grupul într-o regiune live', () => {
    const s = src('components/Editor.tsx');
    assert.match(s, /aria-live="polite" data-live-group=""/); assert.match(s, /tp\('plan\.groupCount', n\)/);
  });
});
