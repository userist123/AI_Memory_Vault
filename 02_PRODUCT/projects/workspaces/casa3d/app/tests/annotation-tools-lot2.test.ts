// Lotul 2 de unelte: „Săgeată cu notă” (leader) și „Cotă de nivel” (level_mark). Ambele sunt câmpuri opționale pe adnotarea
// de tip text, deci testele urmăresc drumul lor prin sanitizator, checkSnapshot, descriere, încadrare, DXF și bară.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { checkSnapshot } from '../lib/repo';
import { sanitizeAnnotation } from '../core/plan-limits';
import { annotationBox, annotationCenter, leaderGeometry, levelMarkGeometry, moveAnnotation, rotateAbout, textAnchor, textAnchorAt, textHeight, LEADER_MIN_M, formatLevelValue } from '../core/annotations';
import { describePlan } from '../core/plan-describe';
import { selectionBox } from '../core/frame';
import { planToDxf } from '../core/dxf';
import { translator, DICTS } from '../lib/i18n';
import { railSlots, TOOL_GROUPS } from '../lib/tool-groups';
import { TOOLS, toolShortcut, matchCommand, formatShortcut } from '../lib/commands';
import type { Annotation, Catalog, Snapshot, TextAnnotation } from '../core/types';
import type { Tool } from '../components/PlanView';

const cat = catalogJson as unknown as Catalog;
const blank = (): Snapshot => newSnapshot(cat, 'Adnotări', 'blank');
const ents = (dxf: string, type: string, layer: string) => dxf.split(`0\n${type}\n8\n${layer}\n`).length - 1;
const near = (a: number, b: number) => assert.ok(Math.abs(a - b) < 1e-9, `${a} ≠ ${b}`);
const note = (o: Partial<TextAnnotation> = {}): TextAnnotation => ({ id: 'text-1', kind: 'text', at: [2, 2], text: 'parchet', ...o });

describe('geometria săgeții cu notă și a cotei de nivel', () => {
  test('săgeata: linia de la notă la vârf, capul pe linie, simetric, cel mult jumătate din linie; fără vârf sau prea scurtă = nimic', () => {
    const g = leaderGeometry(note({ leader: [5, 6] }))!; assert.deepEqual(g.from, [2, 2]); assert.deepEqual(g.tip, [5, 6]); near(g.length, 5);
    assert.deepEqual(g.head[0], [5, 6]);
    // baza capului e la 80 % din înălțimea textului (0,15 m → 0,12 m) în urma vârfului, perpendiculară pe linie
    const [, b1, b2] = g.head, mid = [(b1[0] + b2[0]) / 2, (b1[1] + b2[1]) / 2] as const;
    near(Math.hypot(5 - mid[0], 6 - mid[1]), .12); near((b1[0] - b2[0]) * 3 + (b1[1] - b2[1]) * 4, 0); near(Math.hypot(b1[0] - b2[0], b1[1] - b2[1]), 2 * .12 * .3);
    // linia scurtă: capul nu trece de mijlocul ei
    const s = leaderGeometry(note({ leader: [2.1, 2] }))!, sm = (s.head[1][0] + s.head[2][0]) / 2; near(sm, 2.05);
    assert.equal(leaderGeometry(note()), null); assert.equal(leaderGeometry(note({ leader: [2, 2] })), null);
    assert.equal(leaderGeometry(note({ leader: [2 + LEADER_MIN_M / 2, 2] })), null); assert.ok(leaderGeometry(note({ leader: [2 + LEADER_MIN_M, 2] })));
  });
  test('textul stă de partea opusă vârfului (linia nu-l taie), și la textul rotit; cota de nivel scrie mereu spre dreapta', () => {
    assert.equal(textAnchor(note()), 'start'); assert.equal(textAnchor(note({ leader: [1, 3] })), 'start'); assert.equal(textAnchor(note({ leader: [3, 1] })), 'end');
    // text rotit la 90° (scris în jos, spre +z): vârful de dedesubt e „în față”, cel de deasupra „în spate”
    assert.equal(textAnchor(note({ rotation: 90, leader: [2, 4] })), 'end'); assert.equal(textAnchor(note({ rotation: 90, leader: [2, 0] })), 'start');
    assert.equal(textAnchor(note({ symbol: 'level', leader: [3, 1] })), 'start');
  });
  test('cota de nivel pe plan: valoarea într-un dreptunghi (practica românească; triunghiul e pentru secțiuni), cu margine în jurul textului', () => {
    const an = note({ size: 'l', text: '+2,80' }), g = levelMarkGeometry(an), h = textHeight({ size: 'l' }), pad = h * .3, w = h * .6 * 5;
    assert.equal(g.outline.length, 4); assert.deepEqual(g.textAt, [2, 2]);
    near(g.outline[0][0], 2 - pad); near(g.outline[0][1], 2 - h - pad); near(g.outline[2][0], 2 + w + pad); near(g.outline[2][1], 2 + pad);
    // chenarul cuprinde textul (linia de bază în at, înălțimea h deasupra)
    assert.ok(g.outline[0][1] < 2 - h && g.outline[2][1] > 2 && g.outline[2][0] > 2 + w);
    assert.deepEqual(textAnchorAt(note({ symbol: 'level', size: 'l' })), [2, 2]); assert.deepEqual(textAnchorAt(note()), [2, 2]);
  });
  test('valoarea cotei de nivel în forma de planșă: metri, două zecimale, semn; textul nenumeric rămâne neatins', () => {
    for (const [inp, out] of [['2.8', '+2,80'], ['+2,8', '+2,80'], ['-0.15', '-0,15'], ['0', '±0,00'], ['±0,00', '±0,00'], ['-0', '±0,00'], ['  3 ', '+3,00'], ['12,345', '+12,35']] as const) assert.equal(formatLevelValue(inp), out, inp);
    assert.equal(formatLevelValue('2.8', '.'), '+2.80'); assert.equal(formatLevelValue('cota terasă'), 'cota terasă'); assert.equal(formatLevelValue('+2,80 PFF'), '+2,80 PFF'); assert.equal(formatLevelValue('5000'), '5000');
  });
  test('rotirea în jurul unui punct urmează rotate() din SVG (z în jos): 90° duce +x în +z', () => {
    const q = rotateAbout([3, 2], [2, 2], 90); near(q[0], 2); near(q[1], 3); assert.deepEqual(rotateAbout([3, 2], [2, 2], 0), [3, 2]);
  });
  test('conturul cuprinde vârful săgeții și simbolul; textul simplu are același contur ca înainte', () => {
    const plain = annotationBox(note()), h = .15, w = h * .6 * 7;
    near(plain.x0, 2); near(plain.x1, 2 + w); near(plain.z0, 2 - h); near(plain.z1, 2);
    const left = annotationBox(note({ leader: [-1, 5] })); near(left.x0, -1); near(left.z1, 5); near(left.x1, 2 + w); near(left.z0, 2 - h);
    // vârful în dreapta: textul se termină în `at`, deci conturul pornește cu o lățime de text mai la stânga
    const right = annotationBox(note({ leader: [6, 1] })); near(right.x0, 2 - w); near(right.x1, 6); near(right.z0, 1); near(right.z1, 2);
    const lv = annotationBox(note({ symbol: 'level', text: '+0,00' })); near(lv.x0, 2 - h * .3); near(lv.z0, 2 - h - h * .3); near(lv.z1, 2 + h * .3); near(lv.x1, 2 + h * .6 * 5 + h * .3);
    const c = annotationCenter(note({ leader: [-1, 5] })); near(c[0], (-1 + 2 + w) / 2);
  });
  test('mutarea: textul își ia vârful cu el (drag din poziția de la început și săgețile de la tastatură); cota își mută capetele', () => {
    const from = note({ leader: [5, 6] }), an: Annotation = JSON.parse(JSON.stringify(from));
    moveAnnotation(an, from, .25, -.5); moveAnnotation(an, from, .5, -1);   // a doua mișcare pleacă tot de la original, nu se adună
    assert.deepEqual(an, note({ at: [2.5, 1], leader: [5.5, 5] }));
    const self = note({ leader: [5, 6] }); moveAnnotation(self, self, .01, .01); assert.deepEqual(self.at, [2.01, 2.01]); assert.deepEqual(self.leader, [5.01, 6.01]);
    const plain = note(); moveAnnotation(plain, plain, 1, 1); assert.deepEqual(plain, note({ at: [3, 3] })); assert.ok(!('leader' in plain));
    const d: Annotation = { id: 'cota-1', kind: 'dim', a: [0, 0], b: [3, 0], offset: .3 }; moveAnnotation(d, d, 1, 2); assert.deepEqual(d, { id: 'cota-1', kind: 'dim', a: [1, 2], b: [4, 2], offset: .3 });
  });
});

describe('câmpurile noi nu se pierd la salvare', () => {
  test('sanitizatorul păstrează vârful și simbolul valide; orice altceva se elimină, iar adnotarea rămâne text', () => {
    assert.deepEqual(sanitizeAnnotation(note({ leader: [5, 6] })), note({ leader: [5, 6] }));
    assert.deepEqual(sanitizeAnnotation(note({ symbol: 'level', text: '+0,00', size: 'l', rotation: 90 })), note({ symbol: 'level', text: '+0,00', size: 'l', rotation: 90 }));
    assert.deepEqual(sanitizeAnnotation(note({ leader: [-3.5, 0], symbol: 'level' })), note({ leader: [-3.5, 0], symbol: 'level' }));
    for (const bad of [[5], [5, 6, 7], ['5', 6], [NaN, 6], [Infinity, 0], [5000, 0], [0, -1001], 'x', { 0: 1, 1: 2 }, null])
      assert.deepEqual(sanitizeAnnotation({ ...note(), leader: bad }), note(), JSON.stringify(bad));
    for (const bad of ['Level', 'slope', 1, true, { level: 1 }, null]) assert.deepEqual(sanitizeAnnotation({ ...note(), symbol: bad }), note(), JSON.stringify(bad));
    // câmpuri necunoscute se elimină ca înainte; vârful e copiat, nu același tablou
    const src: any = { ...note({ leader: [5, 6] }), extra: 1 }, out: any = sanitizeAnnotation(src); assert.ok(!('extra' in out)); assert.notEqual(out.leader, src.leader);
    // cota (dim) nu primește câmpurile textului
    const d: any = sanitizeAnnotation({ id: 'cota-1', kind: 'dim', a: [0, 0], b: [3, 0], offset: .3, leader: [1, 1], symbol: 'level' }); assert.ok(!('leader' in d) && !('symbol' in d));
    // valoarea cotei de nivel e text curățat; goală = adnotarea se aruncă (nu se inventează o valoare)
    assert.equal(sanitizeAnnotation(note({ symbol: 'level', text: '   ' })), null);
  });
  test('drum dus-întors prin checkSnapshot (server): vârful și simbolul rămân, și după a doua trecere', () => {
    const s = blank(); s.floor.annotations = [note({ id: 'text-1', leader: [5.125, 6] }), note({ id: 'text-2', symbol: 'level', text: '-0,15' }), note({ id: 'text-3' })];
    const once: any = checkSnapshot(JSON.parse(JSON.stringify(s))), twice: any = checkSnapshot(JSON.parse(JSON.stringify(once)));
    for (const r of [once, twice]){
      assert.deepEqual(r.floor.annotations[0].leader, [5.125, 6]); assert.ok(!('symbol' in r.floor.annotations[0]));
      assert.equal(r.floor.annotations[1].symbol, 'level'); assert.equal(r.floor.annotations[1].text, '-0,15'); assert.ok(!('leader' in r.floor.annotations[1]));
      assert.deepEqual(r.floor.annotations[2], note({ id: 'text-3' })); }
    assert.deepEqual(twice.floor.annotations, once.floor.annotations);
    const bad: any = blank(); bad.floor.annotations = [{ ...note(), leader: [1e9, 0], symbol: 'x' }]; assert.deepEqual((checkSnapshot(bad) as any).floor.annotations, [note()]);
  });
});

describe('consumatorii adnotării text', () => {
  const withNotes = (): Snapshot => { const s = blank(); s.floor.annotations = [note({ id: 'text-1' }), note({ id: 'text-2', at: [1, 1], leader: [3, 4], text: 'grindă' }), note({ id: 'text-3', at: [4, 1], symbol: 'level', text: '+0,00' })]; return s; };
  test('descrierea planului numește săgeata cu notă și cota de nivel; încadrarea cuprinde vârful', () => {
    for (const [lang, want] of [['ro', ['Text · parchet', 'Săgeată cu notă · grindă', 'Cotă de nivel · +0,00']], ['en', ['Text · parchet', 'Arrow with note · grindă', 'Level mark · +0,00']]] as const){
      const g = describePlan(withNotes(), cat, { t: translator(lang), units: 'metric', lang }).find(x => x.id === 'notes')!; assert.deepEqual(g.items.map(i => i.label).sort(), [...want].sort()); }
    const box = selectionBox(withNotes(), cat, { kind: 'annotation', id: 'text-2' })!; assert.ok(box.x1 >= 3 && box.z1 >= 4 && box.x0 <= 1 && box.z0 < 1);
  });
  test('DXF: săgeata = 3 linii pe NOTES (linia și vârful), cota de nivel = un contur pe NOTES; numărul de TEXT rămâne câte unul pe adnotare', () => {
    const base = planToDxf(blank(), cat, { lang: 'ro' }), plain = blank(); plain.floor.annotations = [note()];
    const p = planToDxf(plain, cat, { lang: 'ro' }), dxf = planToDxf(withNotes(), cat, { lang: 'ro' });
    assert.equal(ents(p, 'TEXT', 'NOTES'), 1); assert.equal(ents(p, 'LINE', 'NOTES'), ents(base, 'LINE', 'NOTES')); assert.equal(ents(p, 'POLYLINE', 'NOTES'), ents(base, 'POLYLINE', 'NOTES'));
    assert.equal(ents(dxf, 'TEXT', 'NOTES'), 3); assert.equal(ents(dxf, 'LINE', 'NOTES'), ents(base, 'LINE', 'NOTES') + 3); assert.equal(ents(dxf, 'POLYLINE', 'NOTES'), ents(base, 'POLYLINE', 'NOTES') + 1);
    assert.ok(dxf.includes('\n1\n+0,00\n'));
    // textul simplu nu primește aliniere; nota cu vârful în dreapta se termină în punctul ei (72 = 2)
    assert.ok(!p.includes('\n72\n2\n')); const r = blank(); r.floor.annotations = [note({ leader: [6, 1] })]; assert.ok(planToDxf(r, cat, { lang: 'ro' }).includes('\n72\n2\n'));
  });
});

describe('uneltele în bară și scurtăturile lor', () => {
  test('sunt în registru, cu Shift+B și Shift+Q, și au etichete în ro și en', () => {
    assert.ok(TOOLS.includes('leader') && TOOLS.includes('level_mark'));
    assert.deepEqual(toolShortcut('leader'), { key: 'b', shift: true }); assert.deepEqual(toolShortcut('level_mark'), { key: 'q', shift: true });
    const ev = (key: string, shiftKey = false) => ({ key, shiftKey, ctrlKey: false, metaKey: false, altKey: false });
    assert.equal(matchCommand(ev('B', true)), 'tool.leader'); assert.equal(matchCommand(ev('Q', true)), 'tool.level_mark');
    assert.equal(matchCommand(ev('b')), 'tool.text'); assert.equal(matchCommand(ev('q')), 'tool.dim');
    assert.equal(formatShortcut(toolShortcut('leader'), { mac: false }), 'Shift+B'); assert.equal(formatShortcut(toolShortcut('level_mark'), { mac: false }), 'Shift+Q');
    for (const lang of ['ro', 'en'] as const) for (const k of ['tool.leader', 'tool.level_mark', 'editor.hintLeader', 'editor.hintLevelMark', 'mirror.leader', 'mirror.level', 'level.inputAria', 'level.placeholder', 'leader.tipHint'])
      assert.ok(DICTS[lang][k], `${lang} ${k}`);
    // câmpul cotei de nivel pornește gol: nici textul ajutător nu propune o valoare (CONSTITUTION art. 4)
    for (const lang of ['ro', 'en'] as const) assert.ok(!/\d/.test(DICTS[lang]['level.placeholder']!));
  });
  test('stau în grupa „Cote și texte”: Releveul nu primește un loc nou, iar în Planșe adnotările rămân butoane separate', () => {
    // Schimbat conștient (lotul 3): grupa a primit la coadă cota unghiulară și norul de revizie; primele cinci rămân în aceeași ordine
    assert.deepEqual(TOOL_GROUPS.find(g => g.id === 'notes')!.tools.slice(0, 5), ['measure', 'dim', 'text', 'leader', 'level_mark']);
    const names = (tools: Tool[]) => railSlots(tools, true).map(s => 'tool' in s ? s.tool : s.group);
    const before: Tool[] = ['select', 'pan', 'zoom_area', 'wall', 'room', 'door', 'window', 'passage', 'niche', 'column', 'shaft', 'beam', 'glass_wall', 'railing', 'demolish', 'split', 'stair', 'measure', 'dim', 'text'];
    assert.deepEqual(names([...before, 'leader', 'level_mark']), names(before));
    assert.deepEqual(names(['select', 'pan', 'zoom_area', 'measure', 'dim', 'text', 'leader', 'level_mark']), ['select', 'view', 'measure', 'dim', 'text', 'leader', 'level_mark']);
  });
});
