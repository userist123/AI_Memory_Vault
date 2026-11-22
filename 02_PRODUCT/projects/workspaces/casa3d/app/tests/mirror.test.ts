// Oglindirea pieselor (core/mirror.ts): comutarea câmpului `mirrored` pe o piesă și pe un grup, păstrarea la salvare
// (checkSnapshot), transformarea simbolului din plan și planșe, intrarea vederii 3D, DXF, comanda din registru și meniuri.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { toggleMirror, mirrorLocal, symbolTransform, symbolPoint } from '../core/mirror';
import { footprintOf, validatePlacement } from '../core/validate';
import { duplicatePlacement } from '../core/edit-ops';
import { duplicateGroup, arrayGroup } from '../core/arrange';
import { planToDxf } from '../core/dxf';
import { checkSnapshot } from '../lib/repo';
import { viewerInput } from '../lib/viewer-input';
import { COMMANDS, matchCommand, commandHint, contextCommands, groupContextCommands, type KeyLike } from '../lib/commands';
import { buildMenus } from '../components/MenuBar';
import { DICTS } from '../lib/i18n';
import type { Catalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog;
const demo = (): Snapshot => newSnapshot(cat, 'Test', 'demo');
const ev = (key: string, m: Partial<Omit<KeyLike, 'key'>> = {}): KeyLike => ({ key, shiftKey: false, ctrlKey: false, metaKey: false, altKey: false, ...m });

describe('comutarea', () => {
  test('o piesă: se oglindește, apoi revine; câmpul lipsește când nu e oglindită; originalul nu se atinge', () => {
    const s = demo(), id = s.placements[0]!.id, before = JSON.stringify(s);
    const a = toggleMirror(s, [id]); assert.equal(a.changed, 1); assert.equal(a.snapshot.placements[0]!.mirrored, true);
    assert.equal(JSON.stringify(s), before, 'instantaneul de intrare rămâne neschimbat (un pas de anulare)');
    const b = toggleMirror(a.snapshot, [id]); assert.equal(b.changed, 1); assert.ok(!('mirrored' in b.snapshot.placements[0]!));
    assert.equal(JSON.stringify(b.snapshot), before, 'dus-întors = proiectul de la început');
  });
  test('doar câmpul `mirrored` se schimbă: poziția, rotația, sursa și celelalte piese rămân', () => {
    const s = demo(), p = s.placements[1]!, r = toggleMirror(s, [p.id]), q = r.snapshot.placements[1]!;
    assert.deepEqual({ ...q, mirrored: undefined }, { ...p, mirrored: undefined });
    r.snapshot.placements.forEach((x, i) => { if (i !== 1) assert.deepEqual(x, s.placements[i]); });
  });
  test('grupul: fiecare piesă își comută starea, într-un singur instantaneu', () => {
    const s = demo(), ids = s.placements.slice(0, 3).map(p => p.id); s.placements[1]!.mirrored = true;
    const r = toggleMirror(s, ids); assert.equal(r.changed, 3);
    assert.deepEqual(r.snapshot.placements.slice(0, 3).map(p => p.mirrored === true), [true, false, true]);
    assert.ok(r.snapshot.placements.slice(3).every(p => p.mirrored !== true));
  });
  test('fără piese sau cu id necunoscut: nimic de anulat (același instantaneu, changed 0)', () => {
    const s = demo(); for (const ids of [[], ['nu-exista']]){ const r = toggleMirror(s, ids); assert.equal(r.changed, 0); assert.equal(r.snapshot, s); }
  });
  test('amprenta și validarea nu se schimbă la oglindire', () => {
    const s = demo(); for (const p of s.placements){ const m = toggleMirror(s, [p.id]).snapshot, q = m.placements.find(x => x.id === p.id)!;
      assert.deepEqual(footprintOf(cat, q), footprintOf(cat, p)); assert.deepEqual(validatePlacement(m, cat, q), validatePlacement(s, cat, p)); }
  });
});

describe('salvarea și copiile', () => {
  test('checkSnapshot păstrează doar `mirrored: true`, și după a doua trecere', () => {
    const s: any = demo(); s.placements[0].mirrored = true; s.placements[1].mirrored = false; s.placements[2].mirrored = 'da';
    const once: any = checkSnapshot(JSON.parse(JSON.stringify(s))), twice: any = checkSnapshot(JSON.parse(JSON.stringify(once)));
    for (const o of [once, twice]){ assert.equal(o.placements[0].mirrored, true); assert.ok(!('mirrored' in o.placements[1])); assert.ok(!('mirrored' in o.placements[2])); }
  });
  test('dublarea, dublarea grupului și repetarea în rând copiază oglindirea', () => {
    const base = demo(), id = base.placements[0]!.id, s = toggleMirror(base, [id]).snapshot;
    const d = duplicatePlacement(s, cat, id, 'copie'); assert.ok(d.ok); if (d.ok) assert.equal(d.snapshot.placements.find(p => p.id === 'copie')!.mirrored, true);
    const g = duplicateGroup(s, cat, [id], i => `g${i}`); assert.ok(g.ok); if (g.ok) assert.equal(g.snapshot.placements.find(p => p.id === 'g0')!.mirrored, true);
    const a = arrayGroup(s, cat, [id], 2, 5, 0, i => `a${i}`);
    if (a.ok){ const copies = a.snapshot.placements.filter(p => !s.placements.some(q => q.id === p.id)); assert.ok(copies.length > 0); assert.ok(copies.every(p => p.mirrored === true)); }
    const plain = duplicatePlacement(base, cat, id, 'copie2'); assert.ok(plain.ok); if (plain.ok) assert.ok(!('mirrored' in plain.snapshot.placements.find(p => p.id === 'copie2')!));
  });
});

describe('simbolul oglindit', () => {
  test('punctul local: x își schimbă semnul, y (fața piesei) rămâne', () => {
    assert.deepEqual(mirrorLocal([.4, .25], true), [-.4, .25]); assert.deepEqual(mirrorLocal([.4, .25], false), [.4, .25]); assert.deepEqual(mirrorLocal([.4, .25]), [.4, .25]);
  });
  test('transformarea SVG: oglindirea vine ultima (în sistemul piesei, înainte de rotație), doar la piesele oglindite', () => {
    assert.equal(symbolTransform({ x: 2, z: 3, rotation: 0 }), 'translate(2 3) rotate(0)');
    assert.equal(symbolTransform({ x: 2, z: 3, rotation: Math.PI / 2, mirrored: true }), 'translate(2 3) rotate(-90) scale(-1 1)');
  });
  test('punctele în plan: brațul din dreapta ajunge în stânga, la orice rotație; fața rămâne pe aceeași parte', () => {
    // rotația 0: lățimea pe +x, fața spre +z
    assert.deepEqual(symbolPoint({ x: 2, z: 3, rotation: 0 }, [.5, .2]), [2.5, 3.2]);
    assert.deepEqual(symbolPoint({ x: 2, z: 3, rotation: 0, mirrored: true }, [.5, .2]), [1.5, 3.2]);
    // rotația 90°: fața spre +x, lățimea pe −z; oglindită, lățimea trece pe +z
    assert.deepEqual(symbolPoint({ x: 2, z: 3, rotation: Math.PI / 2 }, [.5, .2]), [2.2, 2.5]);
    assert.deepEqual(symbolPoint({ x: 2, z: 3, rotation: Math.PI / 2, mirrored: true }, [.5, .2]), [2.2, 3.5]);
    // oglindirea e reflexia față de axa de adâncime a piesei: punctele de pe axă nu se mișcă, iar distanța la centru se păstrează
    for (const rotation of [0, .3, Math.PI / 2, 2, Math.PI, 4.1]){ const a = { x: 1, z: -2, rotation }, b = { ...a, mirrored: true as const };
      assert.deepEqual(symbolPoint(b, [0, .7]), symbolPoint(a, [0, .7]));
      assert.deepEqual(symbolPoint(b, [.6, .7]), symbolPoint(a, [-.6, .7]));
      const [px, pz] = symbolPoint(b, [.6, .7]); assert.ok(Math.abs(Math.hypot(px - 1, pz + 2) - Math.hypot(.6, .7)) < 2e-3); }
  });
  test('colțurile cutiei oglindite sunt aceleași (dreptunghi simetric): amprenta nu se mută', () => {
    const w = .9, d = .45; for (const rotation of [0, Math.PI / 2, Math.PI, 1.1]){
      const corners = (m: boolean) => ([[-w, -d], [w, -d], [w, d], [-w, d]] as [number, number][]).map(q => symbolPoint({ x: 3, z: 1, rotation, ...(m ? { mirrored: true as const } : {}) }, q).join(',')).sort();
      assert.deepEqual(corners(true), corners(false)); }
  });
});

describe('3D și DXF', () => {
  test('intrarea vederii 3D poartă `mirrored` doar la piesele oglindite (proiectele vechi dau aceeași intrare)', () => {
    const s = demo(), id = s.placements[0]!.id, plain = viewerInput(s, cat), m = viewerInput(toggleMirror(s, [id]).snapshot, cat);
    assert.ok(plain.items.every(it => !('mirrored' in it)));
    assert.equal((m.items.find(it => it.id === id) as any).mirrored, true); assert.ok(m.items.filter(it => it.id !== id).every(it => !('mirrored' in it)));
    const strip = (v: typeof m) => JSON.stringify({ ...v, items: v.items.map(it => { const c: any = { ...it }; delete c.mirrored; return c; }) });
    assert.equal(strip(m), strip(plain), 'restul intrării (poziție, rotație, amprentă) e identic');
  });
  test('DXF: piesa e desenată prin cutia ei (strat FURNITURE), care e simetrică — exportul unei piese oglindite e identic', () => {
    const s = demo(), all = toggleMirror(s, s.placements.map(p => p.id)).snapshot; assert.ok(all.placements.every(p => p.mirrored === true));
    assert.equal(planToDxf(all, cat, { lang: 'ro' }), planToDxf(s, cat, { lang: 'ro' }));
  });
});

describe('comanda', () => {
  test('Shift+O oglindește; O simplu rămâne unealta Fereastră, iar Ctrl/Alt nu o declanșează', () => {
    assert.equal(matchCommand(ev('O', { shiftKey: true })), 'mirror'); assert.equal(matchCommand(ev('o', { shiftKey: true })), 'mirror');
    assert.equal(matchCommand(ev('o')), 'tool.window');
    assert.notEqual(matchCommand(ev('O', { shiftKey: true, ctrlKey: true })), 'mirror'); assert.notEqual(matchCommand(ev('O', { shiftKey: true, altKey: true })), 'mirror');
    assert.equal(commandHint('mirror', { mac: false }), 'Shift+O');
  });
  test('e în meniul contextual al piesei și al grupului, cu „șterge” tot ultima, și în meniul Editare lângă „repetă”', () => {
    assert.ok(contextCommands('placement').includes('mirror')); assert.equal(contextCommands('placement').at(-1), 'delete');
    assert.ok(groupContextCommands(3).includes('mirror')); assert.equal(groupContextCommands(3).at(-1), 'delete'); assert.ok(!contextCommands('stair').includes('mirror'));
    const cmd = (id: string) => ({ id, group: 'g', label: id, run(){} });
    const edit = buildMenus(['undo', 'redo', 'frame-other', 'mirror', 'array'].map(cmd), k => k).find(m => m.id === 'edit')!.items.map(x => x === 'sep' ? '|' : x.id);
    assert.deepEqual(edit, ['undo', 'redo', '|', 'frame-other', 'mirror', 'array']);
  });
  test('texte în ro și en', () => {
    const c = COMMANDS.find(x => x.id === 'mirror')!;
    for (const lang of ['ro', 'en'] as const) for (const k of [c.label, c.menuLabel!, 'editor.mirror', 'mirror.on', 'mirror.off', 'mirror.group']) assert.ok(((DICTS[lang] as Record<string, string>)[k] ?? '').trim().length > 0, `${lang} ${k}`);
  });
});
