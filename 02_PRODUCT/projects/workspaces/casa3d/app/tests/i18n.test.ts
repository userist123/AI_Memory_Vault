import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { DICTS, LANGS, t, tp, adviceText, detectLang, isLang, type Lang } from '../lib/i18n';
import { STYLES, PRIORITIES } from '../core/brief';
import { TIER_LABEL } from '../core/proposal';
import { failText } from '../core/messages';
import { adviseProject } from '../core/advisor';
import { newSnapshot } from '../core/project';
import catalogV1 from '../data/catalog.v1.json';
import type { Catalog } from '../core/types';

const root = path.resolve(import.meta.dirname, '..');
const walk = (dir: string): string[] => readdirSync(dir).flatMap(f => { const p = path.join(dir, f); return statSync(p).isDirectory() ? (f === 'node_modules' || f === '.next' ? [] : walk(p)) : [p]; });
const sources = [...walk(path.join(root, 'components')), ...walk(path.join(root, 'app'))].filter(f => /\.tsx?$/.test(f) && !f.includes(`${path.sep}api${path.sep}`));
const vars = (s: string) => [...new Set([...s.matchAll(/\{(\w+)\}/g)].map(m => m[1]))].sort().join(',');

describe('dicționare', () => {
  test('fiecare limbă din LANGS are un dicționar cu exact aceleași chei ca româna', () => {
    const ro = Object.keys(DICTS.ro).sort();
    assert.ok(ro.length > 400);
    for (const { code } of LANGS){ const keys = Object.keys(DICTS[code]).sort();
      assert.deepEqual(keys.filter(k => !ro.includes(k)), [], `${code}: chei în plus`); assert.deepEqual(ro.filter(k => !keys.includes(k)), [], `${code}: chei lipsă`); }
    assert.deepEqual(LANGS.map(l => l.code).sort(), Object.keys(DICTS).sort());
    assert.deepEqual(LANGS.map(l => l.name), ['Română', 'English']);
  });
  test('aceleași variabile {nume} în orice limbă, și nicio valoare goală', () => {
    for (const k of Object.keys(DICTS.ro)) for (const { code } of LANGS){ const v = DICTS[code][k]!; assert.ok(v.trim().length > 0, `${code} ${k}`); assert.equal(vars(v), vars(DICTS.ro[k]!), `${code} ${k}`); }
  });
  test('engleza nu a rămas în română (nicio diacritică specifică în textele en)', () => {
    const bad = Object.entries(DICTS.en).filter(([k, v]) => /[ăâîșțĂÂÎȘȚ]/.test(v) && !k.startsWith('x.'));
    assert.deepEqual(bad, []);
  });
});

describe('t(): interpolare și rezervă', () => {
  test('interpolează {nume} și lasă variabilele necunoscute neatinse', () => {
    assert.equal(t('en', 'editor.revSaved', { n: 3 }), 'Revision 3 saved.');
    assert.equal(t('ro', 'editor.revSaved', { n: 3 }), 'Revizia 3 a fost salvată.');
    assert.equal(t('en', 'editor.revSaved'), 'Revision {n} saved.');
    assert.equal(t('en', 'editor.revSaved', { other: 1 }), 'Revision {n} saved.');
  });
  test('cheie lipsă în limba aleasă → textul românesc; lipsă peste tot → cheia', () => {
    const saved = DICTS.en['common.close']; delete DICTS.en['common.close'];
    try { assert.equal(t('en', 'common.close'), 'Închide'); } finally { DICTS.en['common.close'] = saved!; }
    assert.equal(t('en', 'nu.exista'), 'nu.exista'); assert.equal(t('ro', 'nu.exista'), 'nu.exista');
  });
  test('singular/plural prin cheia .one', () => {
    assert.equal(tp('en', 'editor.summaryRooms', 1), '1 room'); assert.equal(tp('en', 'editor.summaryRooms', 3), '3 rooms');
    assert.equal(tp('ro', 'editor.summaryPieces', 1), '1 piesă'); assert.equal(tp('ro', 'editor.summaryPieces', 0), '0 piese');
  });
  test('detectarea limbii din browser', () => {
    assert.equal(detectLang('ro-RO'), 'ro'); assert.equal(detectLang('ro'), 'ro'); assert.equal(detectLang('en-US'), 'en'); assert.equal(detectLang('de-DE'), 'en'); assert.equal(detectLang(''), 'en'); assert.equal(detectLang(undefined), 'en');
    assert.ok(isLang('ro') && isLang('en') && !isLang('de') && !isLang(null));
  });
  test('failText urmează limba', () => {
    assert.match(failText('DOES_NOT_FIT: x', 'ro'), /nu încape în cameră/); assert.match(failText('DOES_NOT_FIT: x', 'en'), /does not fit in the room/);
    assert.equal(failText(undefined, 'en'), 'The item could not be placed.'); assert.equal(failText('ALTCEVA: detaliu', 'en'), 'detaliu (ALTCEVA)');
  });
});

describe('sfaturile consilierului în ambele limbi', () => {
  test('unitățile imperiale schimbă lungimile și suprafețele din text', () => {
    const x = { code: 'WARDROBE_FRONT', vars: { rn: 'Dormitor', wc_m: 0.9 } };
    assert.match(adviceText('ro', x).why, /cel puțin 90 cm liberi/); assert.match(adviceText('en', x).why, /At least 90 cm should stay free/);
    assert.match(adviceText('en', x, 'imperial').why, /At least 2′ 11″ should stay free/);
    assert.match(adviceText('en', { code: 'OVERLAP', vars: { nm: 'Sofa', other: '', d_mu: 0.12 } }).title, /overlaps another item/);
    assert.match(adviceText('ro', { code: 'OVERLAP', vars: { nm: 'Canapea', other: '', d_mu: 0.12 } }).title, /se suprapune cu altă piesă/);
  });
  test('fiecare cod produs de consilier are titlu/motiv/remediere în toate limbile', () => {
    // sursa codurilor: toate literalele `code: '...'` din core/advisor.ts
    const src = readFileSync(path.join(root, 'core/advisor.ts'), 'utf8'), codes = new Set([...src.matchAll(/code: '([A-Z_]+)'/g)].map(m => m[1]!));
    for (const m of src.matchAll(/code: [^,]*\? '([A-Z_]+)' : '([A-Z_]+)'/g)){ codes.add(m[1]!); codes.add(m[2]!); }
    assert.ok(codes.size >= 30, String(codes.size));
    for (const c of codes) for (const { code } of LANGS) for (const part of ['title', 'why', 'fix']) assert.ok(`advice.${c}.${part}` in DICTS[code], `${code} advice.${c}.${part}`);
  });
  test('un proiect real produce sfaturi randabile în ambele limbi', () => {
    const cat = catalogV1 as unknown as Catalog, a = adviseProject(newSnapshot(cat, 'Demo', 'demo'), cat, { budget: { total: 5, target: 1, unknown: 2 } });
    assert.ok(a.length > 0);
    for (const x of a) for (const l of ['ro', 'en'] as Lang[]) assert.ok(adviceText(l, x).title.length > 0);
  });
});

describe('chei folosite în componente', () => {
  const dynamic = [...Object.keys(STYLES).map(k => `design.styles.${k}`), ...Object.keys(PRIORITIES).map(k => `design.priorities.${k}`), ...Object.keys(TIER_LABEL).map(k => `design.tier.${k}`),
    ...['PASS', 'WARNING', 'ERROR'].map(k => `design.status.${k}`)];
  test('familiile de chei dinamice există în toate limbile', () => { for (const k of dynamic) for (const { code } of LANGS) assert.ok(k in DICTS[code], `${code} ${k}`); });
  test('toate cheile din t(...), tp(...) și literalele de forma spațiu.nume din componente există în dicționar', () => {
    const prefixes = new Set(Object.keys(DICTS.ro).map(k => k.split('.')[0]));
    const used = new Map<string, string>();
    for (const f of sources){ const s = readFileSync(f, 'utf8');
      for (const m of s.matchAll(/\btp?\(\s*(?:lang\s*,\s*)?['"`]([A-Za-z0-9_.${}]+)['"`]/g)) if (!m[1]!.includes('$')) used.set(m[1]!, f);
      for (const m of s.matchAll(/['"]([a-z][A-Za-z0-9]*\.[A-Za-z0-9_.]+)['"]/g)) if (prefixes.has(m[1]!.split('.')[0]!)) used.set(m[1]!, f); }
    assert.ok(used.size > 250, String(used.size));
    for (const [k, f] of used) assert.ok(k in DICTS.ro, `${path.relative(root, f)}: cheia ${k} lipsește din dicționar`);
  });
  test('componentele nu mai conțin texte românești vizibile în JSX (sample: cuvinte cu diacritice)', () => {
    // textul românesc trăiește în lib/locales/ro.ts; componentele pot avea doar comentarii în română
    const bad: string[] = [];
    for (const f of sources) readFileSync(f, 'utf8').split('\n').forEach((line, i) => {
      const code = line.replace(/\/\/.*$/, '').replace(/\/\*.*?\*\//g, '');
      if (/>[^<>{}]*[ăâîșțĂÂÎȘȚ][^<>{}]*</.test(code) || /['"`][^'"`]*[ăâîșț][^'"`]*['"`]/.test(code.replace(/\{\/\*.*?\*\/\}/g, ''))) bad.push(`${path.relative(root, f)}:${i + 1}: ${line.trim().slice(0, 90)}`); });
    // excepții: pagina principală (rescrisă separat), pagina statică despre-linkuri și metadata din layout
    assert.deepEqual(bad.filter(b => !/app\/page\.tsx|despre-linkuri|app\/layout\.tsx/.test(b)), []);
  });
});
