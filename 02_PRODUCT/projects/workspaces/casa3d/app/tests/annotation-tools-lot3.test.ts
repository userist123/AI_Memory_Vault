// Lotul 3 de unelte de adnotare: „Cotă unghiulară” (angle_dim, kind 'angle') și „Nor de revizie” (rev_cloud, kind 'cloud').
// Spre deosebire de lotul 2, sunt tipuri noi de adnotare, deci testele urmăresc geometria lor pură, sanitizatorul strict,
// drumul prin checkSnapshot și fiecare consumator care înainte știa doar „text, altfel cotă liniară”.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { checkSnapshot } from '../lib/repo';
import { sanitizeAnnotation, cleanCloudLabel, PlanLimitError, PLAN_LIMITS, PLAN_DEFAULTS } from '../core/plan-limits';
import { annotationBox, annotationCenter, moveAnnotation, angleGeometry, angleArcPoints, angleArcPath, angleLabelAt, angleOk, angleRadiusAt, formatAngle,
  cloudArcCount, cloudNodes, cloudArcs, cloudOutline, cloudPath, cloudBulge, cloudLabelAt, cloudOk, CLOUD_ARC_M, CLOUD_BULGE, CLOUD_MAX_ARCS } from '../core/annotations';
import { describePlan } from '../core/plan-describe';
import { planItems } from '../core/plan-keys';
import { selectionBox } from '../core/frame';
import { planToDxf } from '../core/dxf';
import { translator, DICTS } from '../lib/i18n';
import { railSlots, TOOL_GROUPS } from '../lib/tool-groups';
import { TOOLS, COMMANDS, toolShortcut, matchCommand, formatShortcut } from '../lib/commands';
import type { AngleAnnotation, Annotation, Catalog, CloudAnnotation, Snapshot } from '../core/types';
import type { Tool } from '../components/PlanView';

const cat = catalogJson as unknown as Catalog;
const blank = (): Snapshot => newSnapshot(cat, 'Adnotări 3', 'blank');
const ents = (dxf: string, type: string, layer: string) => dxf.split(`0\n${type}\n8\n${layer}\n`).length - 1;
const near = (a: number, b: number, eps = 1e-9) => assert.ok(Math.abs(a - b) < eps, `${a} ≠ ${b}`);
const angle = (o: Partial<AngleAnnotation> = {}): AngleAnnotation => ({ id: 'unghi-1', kind: 'angle', v: [1, 1], a: [3, 1], b: [1, 4], r: .5, ...o });
const cloud = (o: Partial<CloudAnnotation> = {}): CloudAnnotation => ({ id: 'nor-1', kind: 'cloud', rect: { x0: 1, z0: 2, x1: 3, z1: 3 }, ...o });
const rejects = (v: unknown, why: string) => assert.throws(() => sanitizeAnnotation(v), PlanLimitError, why);

describe('geometria cotei unghiulare', () => {
  test('valoarea în grade și sensul: de la brațul a spre brațul b, pe drumul scurt', () => {
    // a spre dreapta, b în jos (z crește în jos): 90°, în sensul acelor de ceas pe ecran (sweep pozitiv)
    const g = angleGeometry(angle()); near(g.deg, 90); near(g.sweep, Math.PI / 2); near(g.start, 0);
    // brațele inversate: același unghi, sens opus
    const r = angleGeometry(angle({ a: [1, 4], b: [3, 1] })); near(r.deg, 90); near(r.sweep, -Math.PI / 2); near(r.start, Math.PI / 2);
    near(angleGeometry(angle({ b: [3, 3] })).deg, 45); near(angleGeometry(angle({ b: [-1, 1] })).deg, 180); near(angleGeometry(angle({ b: [5, 1] })).deg, 0);
    // se măsoară mereu unghiul mic: brațe la 10° și la 300° dau 70°, nu 290°, iar arcul merge în sens invers acelor de ceas
    const d = (deg: number): [number, number] => [1 + Math.cos(deg * Math.PI / 180), 1 + Math.sin(deg * Math.PI / 180)];
    const s = angleGeometry(angle({ a: d(10), b: d(300) })); near(s.deg, 70, 1e-6); assert.ok(s.sweep < 0);
    // lungimea brațelor nu schimbă unghiul
    near(angleGeometry(angle({ a: [100, 1], b: [1, 1.02] })).deg, 90);
  });
  test('arcul: capetele pe brațe la raza r, mijlocul pe bisectoare, punctele la distanța r de vârf', () => {
    const an = angle({ r: 1 }), g = angleGeometry(an);
    near(g.p1[0], 2); near(g.p1[1], 1); near(g.p2[0], 1); near(g.p2[1], 2); near(g.mid[0], 1 + Math.SQRT1_2); near(g.mid[1], 1 + Math.SQRT1_2);
    near(g.bis[0], Math.SQRT1_2); near(g.bis[1], Math.SQRT1_2);
    const pts = angleArcPoints(an); assert.equal(pts.length, 19);   // un segment la 5°
    for (const q of pts) near(Math.hypot(q[0] - 1, q[1] - 1), 1);
    near(pts[0]![0], g.p1[0]); near(pts[0]![1], g.p1[1]); near(pts.at(-1)![0], g.p2[0]); near(pts.at(-1)![1], g.p2[1]);
    // punctele merg într-un singur sens, de la a spre b
    for (let i = 1; i < pts.length; i++) assert.ok(pts[i]![1] > pts[i - 1]![1] - 1e-12);
    assert.equal(angleArcPoints(angle({ b: [5, 1] })).length, 2);   // 0°: un singur segment degenerat, nu o listă goală
    // calea SVG: un arc de cerc cu raza r; sweep-flag 1 pentru sensul acelor de ceas, 0 pentru celălalt
    assert.equal(angleArcPath(an), 'M2 1A1 1 0 0 1 1 2'); assert.equal(angleArcPath(angle({ r: 1, a: [1, 4], b: [3, 1] })), 'M1 2A1 1 0 0 0 2 1');
  });
  test('brațele pornesc din vârf și depășesc puțin arcul; valoarea stă pe bisectoare, în afara arcului', () => {
    const g = angleGeometry(angle({ r: .4 }));
    for (const [from, to] of g.arms){ assert.deepEqual(from, [1, 1]); near(Math.hypot(to[0] - 1, to[1] - 1), .5); }   // .4 + un sfert
    near(g.arms[0][1][1], 1); near(g.arms[1][1][0], 1);
    const big = angleGeometry(angle({ r: 4 })); near(Math.hypot(big.arms[0][1][0] - 1, big.arms[0][1][1] - 1), 4.15);   // cel mult 15 cm peste arc
    const lb = angleLabelAt(angle({ r: 1 }), .2); near(Math.hypot(lb[0] - 1, lb[1] - 1), 1.2); near(lb[0], lb[1]);
  });
  test('formatul valorii: o zecimală, virgulă în română și punct în engleză', () => {
    assert.equal(formatAngle(90), '90,0°'); assert.equal(formatAngle(90, '.'), '90.0°'); assert.equal(formatAngle(33.349), '33,3°');
    assert.equal(formatAngle(89.96), '90,0°'); assert.equal(formatAngle(0), '0,0°'); assert.equal(formatAngle(180, '.'), '180.0°'); assert.equal(formatAngle(7.25, '.'), '7.3°');
  });
  test('validarea din unealtă: brațe de cel puțin 1 cm, raza adusă în interval și rotunjită la mm', () => {
    assert.ok(angleOk([0, 0], [1, 0], [0, 1])); assert.ok(!angleOk([0, 0], [0, 0], [0, 1])); assert.ok(!angleOk([0, 0], [1, 0], [.005, 0]));
    assert.equal(angleRadiusAt([0, 0], [.3, .4]), .5); assert.equal(angleRadiusAt([0, 0], [0, 0]), PLAN_LIMITS.annotation.angleRadiusMinM);
    assert.equal(angleRadiusAt([0, 0], [900, 0]), PLAN_LIMITS.annotation.angleRadiusMaxM); assert.equal(angleRadiusAt([0, 0], [.12345, 0]), .123);
    assert.ok(PLAN_DEFAULTS.angleRadius >= PLAN_LIMITS.annotation.angleRadiusMinM && PLAN_DEFAULTS.angleRadius <= PLAN_LIMITS.annotation.angleRadiusMaxM);
  });
});

describe('geometria norului de revizie', () => {
  test('numărul de arce urmează perimetrul: coarda de 20 cm, cel puțin un arc pe latură, cel mult 60 în total', () => {
    assert.deepEqual(cloudArcCount(1, .6), { nx: 5, nz: 3, total: 16 }); assert.deepEqual(cloudArcCount(2, 1), { nx: 10, nz: 5, total: 30 });
    assert.deepEqual(cloudArcCount(.1, .1), { nx: 1, nz: 1, total: 4 }); assert.deepEqual(cloudArcCount(4, 2), { nx: 20, nz: 10, total: 60 });
    // crește cu perimetrul până la limită, apoi rămâne limitat (coarda crește)
    let last = 0; for (const k of [.2, .5, 1, 2, 3]){ const n = cloudArcCount(k, k).total; assert.ok(n > last, `${k}`); last = n; }
    for (const [w, d] of [[100, 50], [2000, 2000], [1000, .1], [7.3, 5.1]] as const){ const n = cloudArcCount(w, d); assert.ok(n.total <= CLOUD_MAX_ARCS && n.nx >= 1 && n.nz >= 1, `${w}×${d}: ${n.total}`); }
    assert.equal(CLOUD_ARC_M, .2);
  });
  test('capetele arcelor stau pe conturul dreptunghiului, în sensul acelor de ceas, din colțul stânga-sus', () => {
    const r = cloud().rect, n = cloudNodes(r), c = cloudArcCount(2, 1); assert.equal(n.length, c.total); assert.deepEqual(n[0], [1, 2]);
    for (const q of n) assert.ok((Math.abs(q[1] - 2) < 1e-9 || Math.abs(q[1] - 3) < 1e-9 || Math.abs(q[0] - 1) < 1e-9 || Math.abs(q[0] - 3) < 1e-9) && q[0] >= 1 - 1e-9 && q[0] <= 3 + 1e-9 && q[1] >= 2 - 1e-9 && q[1] <= 3 + 1e-9);
    for (const corner of [[1, 2], [3, 2], [3, 3], [1, 3]]) assert.ok(n.some(q => Math.abs(q[0] - corner[0]!) < 1e-9 && Math.abs(q[1] - corner[1]!) < 1e-9), `colț ${corner}`);
    near(n[1]![0], 1.2); near(n[1]![1], 2);   // primul pas merge spre dreapta, pe latura de sus
  });
  test('fiecare arc e bombat spre exterior cu 30 % din coardă; conturul aproximat nu intră în dreptunghi', () => {
    const r = cloud().rect, arcs = cloudArcs(r), inside = (q: [number, number]) => q[0] > 1 + 1e-9 && q[0] < 3 - 1e-9 && q[1] > 2 + 1e-9 && q[1] < 3 - 1e-9;
    assert.equal(arcs.length, 30);
    for (const [i, a] of arcs.entries()){ const c = Math.hypot(a.to[0] - a.from[0], a.to[1] - a.from[1]); near(c, .2);
      assert.deepEqual(a.to, arcs[(i + 1) % arcs.length]!.from); assert.ok(!inside(a.apex));
      const out = Math.max(1 - a.apex[0], a.apex[0] - 3, 2 - a.apex[1], a.apex[1] - 3); near(out, c * CLOUD_BULGE);
      // cercul arcului trece prin capete și prin vârf
      near(a.radius, (c * c / 4 + (c * CLOUD_BULGE) ** 2) / (2 * c * CLOUD_BULGE)); }
    near(arcs[0]!.apex[1], 2 - .06); near(arcs[0]!.apex[0], 1.1); near(cloudBulge(r), .06);
    const o = cloudOutline(r); assert.equal(o.length, 30 * 6);
    for (const q of o){ assert.ok(!inside(q), `${q}`); assert.ok(q[0] >= 1 - .06 - 1e-9 && q[0] <= 3 + .06 + 1e-9 && q[1] >= 2 - .06 - 1e-9 && q[1] <= 3 + .06 + 1e-9, `${q}`); }
    near(o[0]![0], 1); near(o[0]![1], 2); near(o[3]![0], arcs[0]!.apex[0]); near(o[3]![1], arcs[0]!.apex[1]);   // începe în colț; mijlocul arcului e vârful lui
    assert.equal(cloudOutline(r, 4).length, 30 * 4);
    // calea SVG: câte un arc de cerc pe fiecare coardă, toate în același sens, închisă
    const p = cloudPath(r); assert.ok(p.startsWith('M1 2A') && p.endsWith('Z')); assert.equal(p.split('A').length - 1, 30); assert.equal(p.split(' 0 0 1 ').length - 1, 30);
    assert.deepEqual(cloudLabelAt(r), [1, 2 - .06 - .05]);
    assert.ok(cloudOk(r)); assert.ok(cloudOk({ x0: 0, z0: 0, x1: .1, z1: .1 })); assert.ok(!cloudOk({ x0: 0, z0: 0, x1: .09, z1: 1 })); assert.ok(!cloudOk({ x0: 1, z0: 0, x1: 0, z1: 1 }));
  });
});

describe('încadrarea și mutarea noilor adnotări', () => {
  test('conturul cotei unghiulare cuprinde vârful, brațele, arcul și valoarea; al norului cuprinde arcele și eticheta', () => {
    const b = annotationBox(angle({ r: 1 })); assert.ok(b.x0 <= 1 && b.z0 <= 1 && b.x1 >= 2.15 - 1e-9 && b.z1 >= 2.15 - 1e-9);
    for (const k of ['x0', 'x1', 'z0', 'z1'] as const) assert.ok(Number.isFinite(b[k]));
    const c = annotationCenter(angle({ r: 1 })); assert.ok(c[0] > 1 && c[0] < 2.2 && c[1] > 1 && c[1] < 2.2);
    const plain = annotationBox(cloud()); near(plain.x0, 1 - .06); near(plain.x1, 3 + .06); near(plain.z0, 2 - .06); near(plain.z1, 3 + .06);
    const tagged = annotationBox(cloud({ label: 'R2' })); assert.ok(tagged.z0 < plain.z0); near(tagged.x0, plain.x0); near(tagged.z1, plain.z1);
    // eticheta lungă pe un nor îngust lărgește conturul spre dreapta
    const narrow = annotationBox(cloud({ rect: { x0: 0, z0: 0, x1: .2, z1: .2 }, label: 'REV-2026' })); assert.ok(narrow.x1 > .2 + .06);
  });
  test('moveAnnotation mută toate punctele (rotunjit la mm) și nu atinge raza sau eticheta', () => {
    const from = angle(), an: Annotation = JSON.parse(JSON.stringify(from)); moveAnnotation(an, from, .5, -.9996);
    assert.deepEqual(an, angle({ v: [1.5, 0], a: [3.5, 0], b: [1.5, 3] }));
    const self = angle(); moveAnnotation(self, self, .01, .01); assert.deepEqual(self, angle({ v: [1.01, 1.01], a: [3.01, 1.01], b: [1.01, 4.01] }));
    const cf = cloud({ label: 'R2' }), c: Annotation = JSON.parse(JSON.stringify(cf)); moveAnnotation(c, cf, -1, .25);
    assert.deepEqual(c, cloud({ label: 'R2', rect: { x0: 0, z0: 2.25, x1: 2, z1: 3.25 } }));
    // tipuri diferite între original și copie: nimic nu se schimbă (ca la cotă și text)
    const mixed = angle(); moveAnnotation(mixed, cloud(), 1, 1); assert.deepEqual(mixed, angle());
  });
  test('Tab și încadrarea (F) le găsesc: planItems și selectionBox', () => {
    const s = blank(); s.floor.annotations = [angle(), cloud({ label: 'R2' })];
    const items = planItems(s).filter(x => x.kind === 'annotation'); assert.deepEqual(items.map(x => x.id).sort(), ['nor-1', 'unghi-1']);
    for (const it of items) assert.ok(Number.isFinite(it.x) && Number.isFinite(it.z));
    const box = selectionBox(s, cat, { kind: 'annotation', id: 'nor-1' })!; assert.ok(box.x0 < 1 && box.x1 > 3 && box.z0 < 2 && box.z1 > 3);
    const ab = selectionBox(s, cat, { kind: 'annotation', id: 'unghi-1' })!; assert.ok(ab.x0 <= 1 && ab.z0 <= 1 && ab.x1 > 1.5 && ab.z1 > 1.5);
  });
});

describe('sanitizatorul și drumul prin server', () => {
  test('cota unghiulară validă trece neschimbată; câmpurile străine se elimină', () => {
    assert.deepEqual(sanitizeAnnotation(angle()), angle());
    const src: any = { ...angle(), extra: 1, text: 'x', leader: [0, 0], label: 'R1', offset: 2 }, out: any = sanitizeAnnotation(src);
    assert.deepEqual(out, angle()); assert.notEqual(out.v, src.v); assert.notEqual(out.a, src.a); assert.notEqual(out.b, src.b);
    assert.deepEqual(sanitizeAnnotation(angle({ r: PLAN_LIMITS.annotation.angleRadiusMinM })), angle({ r: .05 })); assert.deepEqual(sanitizeAnnotation(angle({ r: 50 })), angle({ r: 50 }));
  });
  test('cota unghiulară stricată se respinge: puncte nefinite sau în afara planului, brațe sub 1 cm, raza ≤ 0, nefinită sau peste limită', () => {
    for (const k of ['v', 'a', 'b'] as const) for (const bad of [[NaN, 0], [Infinity, 0], [0, 1e9], [-1001, 0], ['1', 2], [1], [1, 2, 3], null, undefined, 'x', { 0: 1, 1: 2 }])
      rejects({ ...angle(), [k]: bad }, `${k} ${JSON.stringify(bad)}`);
    rejects(angle({ a: [1.005, 1] }), 'braț a scurt'); rejects(angle({ b: [1, 1] }), 'braț b nul');
    for (const r of [0, -1, NaN, Infinity, -Infinity, '0.5', null, undefined, .049, 50.01, 1e9, [1], {}]) rejects({ ...angle(), r }, `r ${String(r)}`);
    rejects({ ...angle(), id: 'a b' }, 'id'); rejects({ ...angle(), id: 7 }, 'id numeric');
  });
  test('norul valid trece; eticheta e curățată și tăiată la 8 caractere, una stricată sau goală dispare fără să blocheze', () => {
    assert.deepEqual(sanitizeAnnotation(cloud()), cloud()); assert.ok(!('label' in (sanitizeAnnotation(cloud()) as any)));
    assert.deepEqual(sanitizeAnnotation(cloud({ label: 'R2' })), cloud({ label: 'R2' }));
    assert.deepEqual(sanitizeAnnotation(cloud({ label: '  R2 \n' })), cloud({ label: 'R2' }));
    assert.deepEqual(sanitizeAnnotation(cloud({ label: 'REVIZIA-2026-11' })), cloud({ label: 'REVIZIA-' })); assert.equal(PLAN_LIMITS.annotation.cloudLabelMax, 8);
    assert.deepEqual(sanitizeAnnotation(cloud({ label: '<b>R‮2\u0000</b>' })), cloud({ label: 'b R 2 /b' }));
    assert.equal((sanitizeAnnotation(cloud({ label: 'R  2      xyz' })) as CloudAnnotation).label, 'R 2 xyz');
    for (const bad of ['', '   ', '\n\t', 7, null, undefined, ['R2'], { a: 1 }, true]) assert.deepEqual(sanitizeAnnotation({ ...cloud(), label: bad }), cloud(), JSON.stringify(bad));
    for (const l of ['R2', 'abcdefghijkl', ' x ', '<<<<<<<<<<a']) assert.ok(cleanCloudLabel(l).length <= 8 && cleanCloudLabel(l) === cleanCloudLabel(l).trim());
    const src: any = { ...cloud({ label: 'R2' }), text: 'x', r: 1, extra: true }, out: any = sanitizeAnnotation(src);
    assert.deepEqual(out, cloud({ label: 'R2' })); assert.notEqual(out.rect, src.rect);
    const srcRect: any = { ...cloud(), rect: { ...cloud().rect, w: 5 } }; assert.deepEqual(sanitizeAnnotation(srcRect), cloud());
  });
  test('norul stricat se respinge: contur lipsă, nefinit, în afara planului, inversat sau sub 10 cm', () => {
    for (const rect of [null, undefined, 'x', 7, [1, 2, 3, 4], {}, { x0: 0, z0: 0, x1: 1 }, { x0: NaN, z0: 0, x1: 1, z1: 1 }, { x0: 0, z0: 0, x1: Infinity, z1: 1 }, { x0: '0', z0: 0, x1: 1, z1: 1 },
      { x0: 0, z0: 0, x1: 1001, z1: 1 }, { x0: -1e9, z0: 0, x1: 1, z1: 1 }, { x0: 2, z0: 0, x1: 1, z1: 1 }, { x0: 0, z0: 2, x1: 1, z1: 1 }, { x0: 0, z0: 0, x1: .09, z1: 1 }, { x0: 0, z0: 0, x1: 1, z1: 0 }])
      rejects({ ...cloud(), rect }, JSON.stringify(rect));
    assert.deepEqual(sanitizeAnnotation(cloud({ rect: { x0: 0, z0: 0, x1: .1, z1: .1 } })), cloud({ rect: { x0: 0, z0: 0, x1: .1, z1: .1 } }));
  });
  test('un tip necunoscut se respinge ca înainte, iar cota și textul rămân neschimbate', () => {
    for (const kind of ['blob', 'ANGLE', 'clouds', '', null, undefined, 7]) rejects({ ...angle(), kind }, String(kind));
    assert.deepEqual(sanitizeAnnotation({ id: 'cota-1', kind: 'dim', a: [0, 0], b: [3, 0], offset: .3 }), { id: 'cota-1', kind: 'dim', a: [0, 0], b: [3, 0], offset: .3 });
    assert.deepEqual(sanitizeAnnotation({ id: 'text-1', kind: 'text', at: [2, 2], text: 'parchet' }), { id: 'text-1', kind: 'text', at: [2, 2], text: 'parchet' });
    // câmpurile noilor tipuri nu se lipesc de cele vechi
    const d: any = sanitizeAnnotation({ id: 'cota-1', kind: 'dim', a: [0, 0], b: [3, 0], offset: .3, v: [1, 1], r: 1, rect: cloud().rect, label: 'R2' });
    assert.ok(!('v' in d) && !('r' in d) && !('rect' in d) && !('label' in d));
  });
  test('drum dus-întors prin checkSnapshot (server): rămân identice, și după a doua trecere, alături de adnotările vechi', () => {
    const s = blank(); s.floor.annotations = [angle({ v: [1.125, 1], r: .735 }), cloud({ label: 'R2' }), cloud({ id: 'nor-2', rect: { x0: -4.5, z0: -2, x1: -4, z1: -1.25 } }),
      { id: 'cota-1', kind: 'dim', a: [0, 0], b: [3, 0], offset: .3 }, { id: 'text-1', kind: 'text', at: [2, 2], text: 'parchet' }];
    const want = JSON.parse(JSON.stringify(s.floor.annotations));
    const once: any = checkSnapshot(JSON.parse(JSON.stringify(s))), twice: any = checkSnapshot(JSON.parse(JSON.stringify(once)));
    assert.deepEqual(once.floor.annotations, want); assert.deepEqual(twice.floor.annotations, want);
    assert.ok(!('label' in once.floor.annotations[2]));
    // eticheta murdară se curăță pe server, norul rămâne
    const dirty: any = blank(); dirty.floor.annotations = [{ ...cloud(), label: '  REVIZIA-2026  ', junk: 1 }]; assert.deepEqual((checkSnapshot(dirty) as any).floor.annotations, [cloud({ label: 'REVIZIA-' })]);
    // o cotă unghiulară sau un nor stricat blochează salvarea, ca o cotă liniară stricată (nu se salvează geometrie inventată)
    for (const bad of [{ ...angle(), r: 0 }, { ...angle(), v: [NaN, 0] }, { ...cloud(), rect: { x0: 0, z0: 0, x1: 1e9, z1: 1 } }, { ...angle(), kind: 'blob' }]){
      const b: any = blank(); b.floor.annotations = [bad]; assert.throws(() => checkSnapshot(b), JSON.stringify(bad)); }
  });
});

describe('consumatorii: descrierea planului și DXF', () => {
  const withNotes = (): Snapshot => { const s = blank(); s.floor.annotations = [angle(), cloud({ label: 'R2' }), cloud({ id: 'nor-2', rect: { x0: 5, z0: 5, x1: 6, z1: 5.6 } })]; return s; };
  test('descrierea planului numește cota unghiulară cu valoarea ei și norul cu eticheta', () => {
    for (const [lang, want] of [['ro', ['Cotă unghiulară · 90,0°', 'Nor de revizie · R2', 'Nor de revizie']], ['en', ['Angular dimension · 90.0°', 'Revision cloud · R2', 'Revision cloud']]] as const){
      const g = describePlan(withNotes(), cat, { t: translator(lang), units: 'metric', lang }).find(x => x.id === 'notes')!; assert.deepEqual(g.items.map(i => i.label).sort(), [...want].sort());
      assert.ok(g.items.every(i => i.kind === 'annotation')); }
  });
  test('DXF: cota unghiulară = 2 LINE + ARC + TEXT pe NOTES, cu arcul în sens trigonometric între cele două brațe', () => {
    const base = planToDxf(blank(), cat, { lang: 'ro' }), s = blank(); s.floor.annotations = [angle()];
    const dxf = planToDxf(s, cat, { lang: 'ro' });
    assert.equal(ents(dxf, 'LINE', 'NOTES'), ents(base, 'LINE', 'NOTES') + 2); assert.equal(ents(dxf, 'ARC', 'NOTES'), ents(base, 'ARC', 'NOTES') + 1); assert.equal(ents(dxf, 'TEXT', 'NOTES'), ents(base, 'TEXT', 'NOTES') + 1);
    assert.equal(ents(dxf, 'POLYLINE', 'NOTES'), ents(base, 'POLYLINE', 'NOTES')); assert.equal(ents(dxf, 'LINE', 'DIMENSIONS'), ents(base, 'LINE', 'DIMENSIONS'));
    // centrul în vârf (mm, y = −z), raza 500 mm; brațul a e spre +x (0°), brațul b spre −y în DXF (270°): arcul merge de la 270° la 0°
    assert.ok(dxf.includes('0\nARC\n8\nNOTES\n10\n1000\n20\n-1000\n30\n0\n40\n500\n50\n270\n51\n0'), dxf.slice(dxf.indexOf('0\nARC\n8\nNOTES'), dxf.indexOf('0\nARC\n8\nNOTES') + 80));
    // brațele: din vârf până la 625 mm (raza + un sfert)
    assert.ok(dxf.includes('0\nLINE\n8\nNOTES\n10\n1000\n20\n-1000\n30\n0\n11\n1625\n21\n-1000\n31\n0')); assert.ok(dxf.includes('0\nLINE\n8\nNOTES\n10\n1000\n20\n-1000\n30\n0\n11\n1000\n21\n-1625\n31\n0'));
    assert.ok(dxf.includes('\n1\n90,0\\U+00B0\n')); assert.ok(planToDxf(s, cat, { lang: 'en' }).includes('\n1\n90.0\\U+00B0\n'));
    // brațele inversate dau același arc (DXF nu are sens orar)
    const r = blank(); r.floor.annotations = [angle({ a: [1, 4], b: [3, 1] })]; assert.ok(planToDxf(r, cat, { lang: 'ro' }).includes('\n40\n500\n50\n270\n51\n0'));
    // un unghi oarecare: brațe la 0° și la 135° pe ecran (spre stânga-jos) → în DXF de la 225° la 0°/360°
    const o = blank(); o.floor.annotations = [angle({ b: [-1, 3] })]; assert.ok(planToDxf(o, cat, { lang: 'ro' }).includes('\n40\n500\n50\n225\n51\n0')); assert.ok(planToDxf(o, cat, { lang: 'ro' }).includes('\n1\n135,0\\U+00B0\n'));
  });
  test('DXF: norul = un POLYLINE închis pe NOTES cu arcele aproximate; TEXT doar când are etichetă', () => {
    const base = planToDxf(blank(), cat, { lang: 'ro' }), plain = blank(); plain.floor.annotations = [cloud()];
    const p = planToDxf(plain, cat, { lang: 'ro' }), verts = (d: string) => d.split('0\nVERTEX\n8\nNOTES\n').length - 1;
    assert.equal(ents(p, 'POLYLINE', 'NOTES'), ents(base, 'POLYLINE', 'NOTES') + 1); assert.equal(verts(p), verts(base) + 30 * 6);
    assert.equal(ents(p, 'TEXT', 'NOTES'), ents(base, 'TEXT', 'NOTES')); assert.equal(ents(p, 'LINE', 'NOTES'), ents(base, 'LINE', 'NOTES')); assert.equal(ents(p, 'ARC', 'NOTES'), ents(base, 'ARC', 'NOTES'));
    assert.ok(p.includes('0\nPOLYLINE\n8\nNOTES\n66\n1\n70\n1\n0\nVERTEX\n8\nNOTES\n10\n1000\n20\n-2000\n'));   // închis, pornește din colțul (1, 2)
    const tagged = planToDxf(withNotes(), cat, { lang: 'ro' });
    assert.equal(ents(tagged, 'POLYLINE', 'NOTES'), ents(base, 'POLYLINE', 'NOTES') + 2); assert.equal(ents(tagged, 'TEXT', 'NOTES'), ents(base, 'TEXT', 'NOTES') + 2);   // valoarea unghiului + „R2”
    assert.ok(tagged.includes('\n1\nR2\n')); assert.ok(!p.includes('\n1\nR2\n'));
    // stratul NOTES e declarat în tabelul LAYER
    assert.ok(tagged.includes('\n2\nNOTES\n'));
  });
});

describe('uneltele în bară și scurtăturile lor', () => {
  test('sunt în registru, cu Shift+U și Shift+N, fără să ia tastele ușii și nișei, și au texte în ro și en', () => {
    assert.ok(TOOLS.includes('angle_dim') && TOOLS.includes('rev_cloud'));
    assert.deepEqual(toolShortcut('angle_dim'), { key: 'u', shift: true }); assert.deepEqual(toolShortcut('rev_cloud'), { key: 'n', shift: true });
    const ev = (key: string, shiftKey = false) => ({ key, shiftKey, ctrlKey: false, metaKey: false, altKey: false });
    assert.equal(matchCommand(ev('U', true)), 'tool.angle_dim'); assert.equal(matchCommand(ev('N', true)), 'tool.rev_cloud'); assert.equal(matchCommand(ev('u', true)), 'tool.angle_dim');
    assert.equal(matchCommand(ev('u')), 'tool.door'); assert.equal(matchCommand(ev('n')), 'tool.niche');
    assert.equal(formatShortcut(toolShortcut('angle_dim'), { mac: false }), 'Shift+U'); assert.equal(formatShortcut(toolShortcut('rev_cloud'), { mac: false }), 'Shift+N');
    // nicio altă comandă nu are aceeași scurtătură
    for (const [key, id] of [['u', 'tool.angle_dim'], ['n', 'tool.rev_cloud']] as const)
      assert.deepEqual(COMMANDS.filter(c => c.shortcuts.some(s => s.key === key && s.shift === true && !s.ctrlOrMeta)).map(c => c.id), [id]);
    for (const lang of ['ro', 'en'] as const) for (const k of ['tool.angle_dim', 'tool.rev_cloud', 'editor.hintAngleDim', 'editor.hintRevCloud', 'mirror.angle', 'mirror.cloud', 'mirror.cloudLabel', 'cloud.inputAria', 'cloud.placeholder', 'cloud.label', 'angle.radius'])
      assert.ok(DICTS[lang][k], `${lang} ${k}`);
    assert.equal(DICTS.ro['tool.angle_dim'], 'Cotă unghiulară'); assert.equal(DICTS.ro['tool.rev_cloud'], 'Nor de revizie');
    // câmpul etichetei pornește gol: nici textul ajutător nu propune o revizie (fără cifre, fără „R2”)
    for (const lang of ['ro', 'en'] as const) assert.ok(!/\d/.test(DICTS[lang]['cloud.placeholder']!));
  });
  test('stau în grupa „Cote și texte”: Releveul nu primește un loc nou, iar în Planșe adnotările rămân butoane separate', () => {
    assert.deepEqual(TOOL_GROUPS.find(g => g.id === 'notes')!.tools, ['measure', 'dim', 'text', 'leader', 'level_mark', 'angle_dim', 'rev_cloud']);
    const names = (tools: Tool[]) => railSlots(tools, true).map(s => 'tool' in s ? s.tool : s.group);
    const survey: Tool[] = ['select', 'pan', 'zoom_area', 'wall', 'room', 'door', 'window', 'passage', 'niche', 'column', 'shaft', 'beam', 'glass_wall', 'railing', 'demolish', 'split', 'stair', 'measure', 'dim', 'text', 'leader', 'level_mark'];
    assert.deepEqual(names([...survey, 'angle_dim', 'rev_cloud']), names(survey));
    const slot = railSlots([...survey, 'angle_dim', 'rev_cloud'], true).find(s => 'group' in s && s.group === 'notes')!;
    assert.deepEqual('group' in slot && slot.tools, ['measure', 'dim', 'text', 'leader', 'level_mark', 'angle_dim', 'rev_cloud']);
    // Planșe: 10 unelte, dar adnotările sunt uneltele principale ale etapei și rămân la vedere
    assert.deepEqual(names(['select', 'pan', 'zoom_area', 'measure', 'dim', 'text', 'leader', 'level_mark', 'angle_dim', 'rev_cloud']), ['select', 'view', 'measure', 'dim', 'text', 'leader', 'level_mark', 'angle_dim', 'rev_cloud']);
    // pe ecran îngust (fără grupare) fiecare unealtă are locul ei
    assert.equal(railSlots([...survey, 'angle_dim', 'rev_cloud'], false).length, survey.length + 2);
  });
});

// ---------- reparațiile după review (2026-11-21) ----------
import { cleanCloudLabel as cleanLabel2, cleanPlanText as cleanText2, armsAngleDeg as armsDeg, cutChars as cut } from '../core/plan-limits';
import { angleOk as okAngle } from '../core/annotations';
import { test as t2 } from 'vitest';
import assert2 from 'node:assert/strict';
t2('etichetele nu rup un emoji în două și nu rămân invizibile', () => {
  const l = cleanLabel2('R2 abcd🔴'); assert2.ok(!/[\uD800-\uDFFF]/.test(l.replace(/\uD83D\uDD34/g, '')), 'fără jumătăți de emoji'); assert2.equal(Array.from(l).length <= 8, true);
  assert2.equal(cut('ab🔴c', 3), 'ab🔴'); assert2.equal(cleanLabel2('\u200B\u200B'), ''); assert2.equal(cleanText2('a\u200Bb'), 'ab');
  assert2.equal(cleanText2('x\uD83D'), 'x', 'o jumătate rămasă singură dispare');
});
t2('cota unghiulară cu brațele pe aceeași dreaptă nu e acceptată', () => {
  assert2.ok(Math.abs(armsDeg([0, 0], [1, 0], [0, 1]) - 90) < 1e-9); assert2.equal(armsDeg([0, 0], [1, 0], [2, 0]), 0);
  assert2.equal(okAngle([0, 0], [1, 0], [3, 0]), false); assert2.equal(okAngle([0, 0], [1, 0], [0, 1]), true);
});
