// Verificare Cartuș Arhitectural conform standardelor ISO 7200 / SR EN ISO 7200 / OAR.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { translator } from '../lib/i18n';

test('ArchitecturalTitleBlock: fișierul conține toate câmpurile cerute de ISO 7200 și OAR', () => {
  const src = readFileSync(path.resolve(import.meta.dirname, '../components/ArchitecturalTitleBlock.tsx'), 'utf8');
  assert.match(src, /export function ArchitecturalTitleBlock/);
  assert.match(src, /arch-title-block/);
  assert.match(src, /arch-tb-grid/);
  assert.match(src, /arch-tb-studio/);
  assert.match(src, /arch-tb-client/);
  assert.match(src, /arch-tb-proj/);
  assert.match(src, /arch-tb-phase/);
  assert.match(src, /arch-tb-signatures/);
  assert.match(src, /arch-tb-drawing/);
  assert.match(src, /arch-tb-sheet-meta/);
  assert.match(src, /projectLeader/);
  assert.match(src, /designedBy/);
  assert.match(src, /checkedBy/);
});

test('PrintView: folosește ArchitecturalTitleBlock cu titlu și scară dedicate pentru fiecare planșă tehnică', () => {
  const src = readFileSync(path.resolve(import.meta.dirname, '../components/PrintView.tsx'), 'utf8');
  assert.match(src, /import \{ ArchitecturalTitleBlock \} from '\.\/ArchitecturalTitleBlock'/);
  assert.match(src, /renderTitleBlock/);
  assert.match(src, /renderTitleBlock\(.*draw\.ceiling/);
  assert.match(src, /renderTitleBlock\(.*draw\.floor/);
  assert.match(src, /renderTitleBlock\(.*draw\.elevations/);
  assert.match(src, /renderTitleBlock\(.*so\.title/);
  assert.match(src, /renderTitleBlock\(.*sched\.title/);
});

test('Drawings: re-exportă ArchitecturalTitleBlock pentru utilizare directă în planșe', () => {
  const src = readFileSync(path.resolve(import.meta.dirname, '../components/Drawings.tsx'), 'utf8');
  assert.match(src, /export \{ ArchitecturalTitleBlock \} from '\.\/ArchitecturalTitleBlock'/);
});

test('globals.css: conține stilurile profesionale pentru cartușul arhitectural și media print', () => {
  const css = readFileSync(path.resolve(import.meta.dirname, '../app/globals.css'), 'utf8');
  assert.match(css, /\.arch-title-block/);
  assert.match(css, /\.arch-tb-grid/);
  assert.match(css, /\.arch-tb-signatures/);
  assert.match(css, /@media print\{\.arch-title-block\{opacity:1 !important\}\}/);
});

test('i18n: dicționarele ro și en conțin toate etichetele cartușului arhitectural', () => {
  const tRo = translator('ro'), tEn = translator('en');
  const keys = ['tb.title', 'tb.office', 'tb.client', 'tb.project', 'tb.phase', 'tb.phaseInterior', 'tb.drawing', 'tb.scale', 'tb.date', 'tb.rev', 'tb.sheetNo', 'tb.lead', 'tb.designed', 'tb.drawn', 'tb.checked'];
  for (const k of keys){
    assert.ok(tRo(k), `tRo has ${k}`);
    assert.ok(tEn(k), `tEn has ${k}`);
    assert.notEqual(tRo(k), tEn(k), `translations differ for ${k}`);
  }
});
