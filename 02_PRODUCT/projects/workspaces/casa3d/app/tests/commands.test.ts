import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { DICTS, t } from '../lib/i18n';
import { isPaletteShortcut } from '../lib/fuzzy';
import { COMMANDS, TOOLS, matchCommand, formatShortcut, helpRows, toolShortcut, shortcutMatches, type KeyLike } from '../lib/commands';
import type { Tool } from '../components/PlanView';

// Registrul de comenzi (lib/commands.ts): tastatura editorului, paleta, tooltip-urile și ajutorul citesc același tabel.
const ev = (key: string, m: Partial<Omit<KeyLike, 'key'>> = {}): KeyLike => ({ key, shiftKey: false, ctrlKey: false, metaKey: false, altKey: false, ...m });
const MODS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15].map(b => ({ ctrlKey: !!(b & 1), metaKey: !!(b & 2), shiftKey: !!(b & 4), altKey: !!(b & 8) }));

describe('registrul de comenzi', () => {
  test('fiecare combinație de taste duce la cel mult o comandă în domeniul ei (globalul se vede și din editor)', () => {
    const keys = [...new Set(COMMANDS.flatMap(c => c.shortcuts.flatMap(s => [s.key, s.key.toUpperCase(), s.key.toLowerCase()])))];
    for (const dom of ['global', 'editor'] as const){ const cs = COMMANDS.filter(c => c.domain === dom || c.domain === 'global');
      for (const k of keys) for (const m of MODS){ const e = ev(k, m), hit = cs.filter(c => c.shortcuts.some(s => shortcutMatches(s, e))).map(c => c.id);
        assert.ok(hit.length <= 1, `${dom} ${k} ${JSON.stringify(m)}: ${hit.join(', ')}`); } }
    assert.equal(new Set(COMMANDS.map(c => c.id)).size, COMMANDS.length, 'id-uri unice');
  });
  test('fiecare comandă are etichetă nevidă în ro și en', () => {
    for (const c of COMMANDS) for (const lang of ['ro', 'en'] as const){ assert.ok(c.label in DICTS[lang], `${lang} ${c.id}: ${c.label}`); assert.ok(t(lang, c.label).trim().length > 0, `${lang} ${c.id}`); }
    for (const r of helpRows({ mac: false })) for (const lang of ['ro', 'en'] as const){ assert.ok(r.label in DICTS[lang], `${lang} ${r.label}`); if (r.keysKey) assert.ok(r.keysKey in DICTS[lang], `${lang} ${r.keysKey}`); }
  });
  test('regresie: tastele vechi dau aceleași acțiuni', () => {
    const cases: [KeyLike, string | null][] = [
      [ev('z', { ctrlKey: true }), 'undo'], [ev('z', { metaKey: true }), 'undo'], [ev('Z', { ctrlKey: true }), 'undo'],
      [ev('y', { ctrlKey: true }), 'redo'], [ev('y', { metaKey: true }), 'redo'], [ev('Z', { ctrlKey: true, shiftKey: true }), 'redo'], [ev('z', { metaKey: true, shiftKey: true }), 'redo'],
      [ev('d', { ctrlKey: true }), 'duplicate'], [ev('d', { metaKey: true }), 'duplicate'],
      [ev('?'), 'help'], [ev('?', { shiftKey: true }), 'help'],
      ...(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].flatMap(k => [[ev(k), 'nudge'], [ev(k, { shiftKey: true }), 'nudge.big'],
        [ev(k, { ctrlKey: true }), null], [ev(k, { metaKey: true }), null], [ev(k, { altKey: true }), null], [ev(k, { ctrlKey: true, shiftKey: true }), null]]) as [KeyLike, string | null][]),
      [ev('Delete'), 'delete'], [ev('Backspace'), 'delete'],
      [ev('r'), 'rotate'], [ev('R'), 'rotate'], [ev('R', { shiftKey: true }), 'rotate'],
      [ev('Escape'), 'escape'],
      // Ctrl/⌘K rămâne la isPaletteShortcut: registrul îl afișează, dar dispecerul nu îl dublează
      [ev('k', { ctrlKey: true }), null], [ev('k', { metaKey: true }), null],
      [ev('x'), null], [ev('Enter'), null]
    ];
    for (const [e, id] of cases) assert.equal(matchCommand(e), id, JSON.stringify(e));
    assert.ok(isPaletteShortcut(ev('k', { ctrlKey: true })) && isPaletteShortcut(ev('k', { metaKey: true })));
    const pal = COMMANDS.find(c => c.id === 'palette')!; assert.equal(pal.dispatch, false); assert.ok(pal.shortcuts.some(s => shortcutMatches(s, ev('k', { ctrlKey: true }))));
  });
  test('100 % din unelte sunt în registru', () => {
    const all: Record<Tool, true> = { select: true, wall: true, room: true, door: true, window: true, measure: true, tech: true, column: true, beam: true, glass_wall: true, stair: true };
    assert.deepEqual([...TOOLS].sort(), Object.keys(all).sort());
    assert.deepEqual(COMMANDS.filter(c => c.id.startsWith('tool.')).map(c => c.id.slice(5)).sort(), [...TOOLS].sort());
    for (const tk of TOOLS){ const c = COMMANDS.find(x => x.id === `tool.${tk}`)!; assert.equal(c.tool, tk); assert.equal(c.label, `tool.${tk}`); assert.ok(toolShortcut(tk)); }
  });
  test('tastele uneltelor: literă fără modificator, mare sau mică; cu modificator nimic; R rămâne rotire', () => {
    const map: Record<string, Tool> = { v: 'select', p: 'wall', c: 'room', u: 'door', f: 'window', t: 'column', g: 'beam', l: 'glass_wall', s: 'stair', m: 'measure', i: 'tech' };
    for (const [k, tk] of Object.entries(map)){
      assert.equal(matchCommand(ev(k)), `tool.${tk}`, k); assert.equal(matchCommand(ev(k.toUpperCase())), `tool.${tk}`, k.toUpperCase());
      for (const m of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }, { shiftKey: true }]){ assert.equal(matchCommand(ev(k, m)), null, `${k} ${JSON.stringify(m)}`); assert.equal(matchCommand(ev(k.toUpperCase(), m)), null); }
    }
    assert.equal(matchCommand(ev('r')), 'rotate');
  });
  test('formatarea scurtăturii', () => {
    const undo = COMMANDS.find(c => c.id === 'undo')!.shortcuts[0]!, redoZ = COMMANDS.find(c => c.id === 'redo')!.shortcuts.find(s => s.key === 'z')!;
    assert.equal(formatShortcut(undo, { mac: true }), '⌘Z'); assert.equal(formatShortcut(undo, { mac: false }), 'Ctrl+Z');
    assert.equal(formatShortcut(redoZ, { mac: false }), 'Ctrl+Shift+Z'); assert.equal(formatShortcut(redoZ, { mac: true }), '⇧⌘Z');
    assert.equal(formatShortcut(toolShortcut('wall'), { mac: false }), 'P'); assert.equal(formatShortcut(toolShortcut('wall'), { mac: true }), 'P');
    assert.equal(formatShortcut(COMMANDS.find(c => c.id === 'escape')!.shortcuts[0]!, { mac: false }), 'Esc');
    assert.equal(formatShortcut(COMMANDS.find(c => c.id === 'help')!.shortcuts[0]!, { mac: false }), '?');
  });
  test('ajutorul: rândurile vechi plus uneltele, rotița și Alt+trage', () => {
    const rows = helpRows({ mac: false }), labels = rows.map(r => r.label);
    for (const k of ['help.undo', 'help.redo', 'help.delete', 'help.rotate', 'help.duplicate', 'help.nudge', 'help.nudgeBig', 'help.escape', 'help.zoom', 'help.pan', 'help.toggle']) assert.ok(labels.includes(k), k);
    for (const tk of TOOLS) assert.ok(labels.includes(`tool.${tk}`), tk);
    assert.equal(rows.find(r => r.label === 'help.undo')!.keysKey, 'help.undoKeys'); assert.equal(rows.find(r => r.label === 'tool.wall')!.keys, 'P'); assert.equal(rows.find(r => r.label === 'help.rotate')!.keys, 'R');
    assert.equal(new Set(labels).size, labels.length);
  });
  test('ajutorul are trei secțiuni în ordine (editare, desenare precisă, unelte) și documentează lungimea tastată și Shift', () => {
    const rows = helpRows({ mac: false }), order = rows.map(r => r.section);
    assert.deepEqual([...new Set(order)], ['edit', 'draw', 'tools']); assert.deepEqual(order, [...order].sort((x, y) => ['edit', 'draw', 'tools'].indexOf(x) - ['edit', 'draw', 'tools'].indexOf(y)));
    for (const k of ['help.typeLength', 'help.shiftDraw']) assert.equal(rows.find(r => r.label === k)?.section, 'draw', k);
    assert.ok(rows.filter(r => r.section === 'tools').every(r => r.label.startsWith('tool.')));
  });
});
