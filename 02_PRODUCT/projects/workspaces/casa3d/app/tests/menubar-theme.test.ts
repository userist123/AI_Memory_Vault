// Bara de meniuri a editorului (components/MenuBar.tsx) și tema „pro”: meniurile se construiesc din aceleași comenzi ca
// paleta Ctrl+K, iar stilul întunecat generat din globals.css trebuie să fie la zi.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { buildMenus } from '../components/MenuBar';
import type { Command } from '../components/CommandPalette';

const cmd = (id: string, extra: Partial<Command> = {}): Command => ({ id, label: id, group: 'g', run: () => {}, ...extra });
test('buildMenus: fiecare comandă în meniul ei, în ordinea dată, cu separatoare între grupuri; meniurile goale dispar', () => {
  const cs = ['undo', 'redo', 'rev', 'print', 'doc-offer', 'home', 'v-2d', 'v-3d', 'ws.focus', 'theme', 'st-survey', 'st-design', 'tool-wall', 'lang', 'units', 'help', 'palette', 'health', 'room-r1'].map(id => cmd(id));
  const m = buildMenus(cs, k => k), ids = (i: number) => m[i]!.items.map(x => x === 'sep' ? '|' : x.id);
  assert.deepEqual(m.map(x => x.label), ['menu.file', 'menu.edit', 'menu.view', 'menu.stages', 'menu.tools', 'menu.settings', 'menu.help']);
  assert.deepEqual(ids(0), ['rev', '|', 'print', 'doc-offer', '|', 'home']);
  assert.deepEqual(ids(1), ['undo', 'redo']);
  assert.deepEqual(ids(2), ['v-2d', 'v-3d', '|', 'ws.focus', 'theme']);
  assert.deepEqual(ids(3), ['st-survey', 'st-design']);
  assert.deepEqual(ids(6), ['palette', 'help', '|', 'health']);
  assert.ok(!m.flatMap(x => x.items).some(x => x !== 'sep' && x.id === 'room-r1'), 'obiectele din plan rămân doar în paleta de căutare');
  assert.deepEqual(buildMenus([cmd('undo')], k => k).map(x => x.id), ['edit'], 'pe paginile fără unelte meniul Unelte nu apare');
});
test('tema pro generată (components/theme-pro.gen.css) e la zi cu app/globals.css', () => {
  execFileSync(process.execPath, [join(__dirname, '..', 'scripts', 'gen-theme-pro.mjs'), '--check'], { stdio: 'pipe' });
});
