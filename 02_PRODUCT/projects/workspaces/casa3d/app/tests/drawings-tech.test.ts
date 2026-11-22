// Desfășuratele de pereți: punctele tehnice DECLARATE (snap.tech) pe latura lor, cu înălțimea de la pardoseala finită,
// și lanțul de cote de la colțul stâng. Fără sugestii: un proiect fără snap.tech nu arată niciun punct (art. 4).
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { wallElevations, dimensionChain, techOnSide, ELEV_TECH_WALL_M } from '../core/drawings';
import { suggestTechPoints, TECH_KINDS, type TechKind, type TechPoint } from '../core/technical';
import { addLevel, levelView } from '../core/levels';
import { ElevationSheet } from '../components/Drawings';
import { translator } from '../lib/i18n';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Desfășurate', 'demo');
const living = (s: Snapshot) => s.floor.rooms.find(r => r.id === 'living')!;
const pt = (id: string, kind: TechKind, roomId: string, x: number, z: number, height = TECH_KINDS[kind].height, extra: Partial<TechPoint> = {}): TechPoint => ({ id, kind, roomId, x, z, height, reason: 'manual', ...extra });
const elev = (s: Snapshot, side: 'N' | 'E' | 'S' | 'W') => wallElevations(s, mc, cat, s.floor, living(s)).find(e => e.side === side)!;
const near = (a: number, b: number, msg?: string) => assert.ok(Math.abs(a - b) < 1e-6, `${msg ?? ''} ${a} ≠ ${b}`);

describe('desfășurate: punctele tehnice declarate', () => {
  test('un punct pe fiecare latură apare doar pe latura lui, la poziția corectă (S și V se citesc oglindit)', () => {
    const s = demo(), r = living(s).rect, lx = r.x1 - r.x0, lz = r.z1 - r.z0; assert.ok(lx > 2 && lz > 2, 'camera demo e destul de mare pentru test');
    s.tech = [pt('n', 'outlet', 'living', r.x0 + 1.2, r.z0 + .02), pt('e', 'switch', 'living', r.x1 - .02, r.z0 + .8),
      pt('s', 'data', 'living', r.x0 + 1, r.z1 - .02), pt('w', 'water_cold', 'living', r.x0 + .02, r.z0 + .7)];
    const by = Object.fromEntries((['N', 'E', 'S', 'W'] as const).map(k => [k, elev(s, k).tech]));
    assert.deepEqual(by.N!.map(p => p.id), ['n']); assert.deepEqual(by.E!.map(p => p.id), ['e']); assert.deepEqual(by.S!.map(p => p.id), ['s']); assert.deepEqual(by.W!.map(p => p.id), ['w']);
    near(by.N![0]!.u, 1.2, 'N: de la x0'); near(by.E![0]!.u, .8, 'E: de la z0');
    near(by.S![0]!.u, lx - 1, 'S: oglindit, de la x1'); near(by.W![0]!.u, lz - .7, 'V: oglindit, de la z1');
    assert.ok(Math.abs(by.S![0]!.u - 1) > .01 && Math.abs(by.W![0]!.u - .7) > .01, 'oglindirea chiar schimbă poziția');
    for (const k of ['N', 'E', 'S', 'W'] as const) assert.equal(elev(s, k).techDeclared, true);
  });
  test('cota de înălțime e câmpul height al punctului (implicitul tipului sau valoarea declarată), nu alta', () => {
    const s = demo(), r = living(s).rect;
    s.tech = [pt('a', 'switch', 'living', r.x0 + .6, r.z0 + .02), pt('b', 'outlet', 'living', r.x0 + 1.6, r.z0 + .02, .95)];
    const n = elev(s, 'N').tech; assert.equal(n.find(p => p.id === 'a')!.y, TECH_KINDS.switch.height); assert.equal(n.find(p => p.id === 'b')!.y, .95);
  });
  test('punctul de pe alt nivel nu apare; pe nivelul lui apare', () => {
    const s0 = demo(), r = living(s0).rect, s = addLevel(s0, { name: 'Etaj' }), up = s.levels![0]!, upRoom = up.rooms.find(q => q.rect.x0 === r.x0 && q.rect.z0 === r.z0 && q.rect.x1 === r.x1)!;
    s.tech = [pt('jos', 'outlet', 'living', r.x0 + 1, r.z0 + .02), pt('sus', 'outlet', upRoom.id, r.x0 + 2, r.z0 + .02)];
    assert.deepEqual(elev(s, 'N').tech.map(p => p.id), ['jos']);
    assert.deepEqual(wallElevations(s, mc, cat, up, upRoom).find(e => e.side === 'N')!.tech.map(p => p.id), ['sus']);
    const v0 = levelView(s, 0), v1 = levelView(s, 1);   // exact cum le cheamă planșa de tipar
    assert.deepEqual(wallElevations(v0, mc, cat, v0.floor, living(v0)).flatMap(e => e.tech.map(p => p.id)), ['jos']);
    assert.deepEqual(wallElevations(v1, mc, cat, v1.floor, upRoom).flatMap(e => e.tech.map(p => p.id)), ['sus']);
    assert.deepEqual(techOnSide(s, s.floor, upRoom, 'N'), [], 'camera de pe alt nivel decât planșa nu primește puncte');
  });
  test('fără snap.tech: niciun punct, deși sugestiile ar exista (fără valori inventate)', () => {
    const s = demo(); assert.equal(s.tech, undefined); assert.ok(suggestTechPoints(s).some(p => p.roomId === 'living' && p.kind !== 'light_point'), 'sugestii ar fi');
    const el = wallElevations(s, mc, cat, s.floor, living(s));
    assert.ok(el.every(e => e.tech.length === 0 && e.techDeclared === false));
    assert.ok(el.every(e => e.dims.every(d => d.ref === 'opening')), 'lanțul are doar golurile');
    s.tech = []; assert.ok(wallElevations(s, mc, cat, s.floor, living(s)).every(e => e.tech.length === 0 && e.techDeclared === true), 'listă declarată goală: tot niciun punct, dar declarat');
  });
  test('punctul de lumină (tavan) și punctul departe de perete nu se proiectează pe un perete', () => {
    const s = demo(), r = living(s).rect;
    s.tech = [pt('l', 'light_point', 'living', r.x0 + 1, r.z0 + .02, 2.6), pt('mijloc', 'drain', 'living', (r.x0 + r.x1) / 2, r.z0 + ELEV_TECH_WALL_M + .2, 0), pt('ok', 'drain', 'living', r.x0 + 1.5, r.z0 + .05, 0)];
    assert.deepEqual(wallElevations(s, mc, cat, s.floor, living(s)).flatMap(e => e.tech.map(p => p.id)), ['ok']);
  });
  test('caloriferul cu mărimi are corpul lui; două puncte în același loc au simbolurile alăturate, cu aceeași cotă', () => {
    const s = demo(), r = living(s).rect;
    s.tech = [pt('cal', 'radiator', 'living', r.x0 + 2, r.z0 + .02, .15, { widthM: 1, heightM: .6, model: 'panel' }), pt('racord', 'radiator', 'living', r.x0 + .6, r.z0 + .02),
      pt('p', 'outlet_double', 'living', r.x0 + 1, r.z0 + .02), pt('d', 'data', 'living', r.x0 + 1, r.z0 + .02)];
    const n = elev(s, 'N'), get = (id: string) => n.tech.find(p => p.id === id)!;
    assert.deepEqual(get('cal').body, { w: 1, h: .6 }); assert.equal(get('cal').y, .15); assert.equal(get('racord').body, undefined);
    assert.equal(get('p').u, get('d').u); assert.notEqual(get('p').du, get('d').du);
    assert.equal(n.dims.filter(d => d.cm === 100).length, 1, 'o singură cotă pentru două puncte în același loc');
  });
});

describe('desfășurate: lanțul de cote de la colțul stâng', () => {
  test('ordonat crescător, în cm, cu punctele tehnice și marginile golurilor', () => {
    const s = demo(), r = living(s).rect; s.tech = [pt('b', 'outlet', 'living', r.x0 + 2.4, r.z0 + .02), pt('a', 'switch', 'living', r.x0 + .5, r.z0 + .02)];
    for (const e of wallElevations(s, mc, cat, s.floor, living(s))){ const cms = e.dims.map(d => d.cm);
      assert.deepEqual(cms, [...cms].sort((x, y) => x - y)); assert.equal(new Set(cms).size, cms.length);
      assert.ok(cms.every(c => c > 0 && c < Math.round(e.lengthM * 100)));
      for (const p of e.tech) assert.ok(cms.includes(Math.round(p.u * 100)), `punctul ${p.id} e cotat`);
      for (const o of e.openings) for (const u of [o.u0, o.u1]){ const c = Math.round(u * 100); if (c > 0 && c < Math.round(e.lengthM * 100)) assert.ok(cms.includes(c), `golul la ${c} cm e cotat`); } }
    const n = elev(s, 'N'); assert.ok(n.dims.some(d => d.cm === 50 && d.ref === 'tech') && n.dims.some(d => d.cm === 240 && d.ref === 'tech'));
    assert.ok(wallElevations(s, mc, cat, s.floor, living(s)).some(e => e.dims.some(d => d.ref === 'opening')), 'camera demo are goluri cotate');
  });
  test('cotele prea apropiate trec pe alt rând; cele depărtate rămân pe primul', () => {
    const d = dimensionChain(4, [{ u0: 3, u1: 3.9 }], [{ u: 1.2 }, { u: 1.26 }, { u: 1.3 }, { u: 2 }]);
    assert.deepEqual(d.map(x => x.cm), [120, 126, 130, 200, 300, 390]);
    assert.deepEqual(d.map(x => x.row), [0, 1, 2, 0, 0, 0]);
    assert.deepEqual(dimensionChain(4, [{ u0: 0, u1: 4 }], [{ u: 0 }, { u: 4 }]), [], 'colțurile nu se cotează (lungimea totală e deja pe planșă)');
  });
});

describe('desfășurate: desenul și legenda', () => {
  const render = (s: Snapshot, lang: 'ro' | 'en') => renderToStaticMarkup(createElement(ElevationSheet, { elevs: wallElevations(s, mc, cat, s.floor, living(s)), t: translator(lang), units: 'metric', locale: lang }));
  test('simbolul, înălțimea în cm, lanțul și legenda apar; ro + en', () => {
    const s = demo(), r = living(s).rect; s.tech = [pt('a', 'switch', 'living', r.x0 + .5, r.z0 + .02), pt('b', 'outlet', 'living', r.x0 + 2.4, r.z0 + .02, .95)];
    const ro = render(s, 'ro'), en = render(s, 'en');
    assert.match(ro, /data-tech="switch"/); assert.match(ro, /data-tech="outlet"/); assert.match(ro, />110<\/text>/); assert.match(ro, />95<\/text>/); assert.match(ro, />240<\/text>/);
    assert.match(ro, /Puncte tehnice/); assert.match(ro, /Întrerupător/); assert.match(ro, /pardoseala finită/);
    assert.match(en, /Technical points/); assert.match(en, /Light switch/); assert.match(en, /finished floor/);
    assert.ok(!ro.includes('draw.tech') && !en.includes('draw.tech'), 'toate cheile au text');
  });
  test('fără puncte declarate: nicio bulină tehnică, doar nota că nu sunt declarate', () => {
    const s = demo(), ro = render(s, 'ro'), en = render(s, 'en');
    assert.ok(!ro.includes('data-tech=') && !ro.includes('elev-tech-legend'));
    assert.match(ro, /nu sunt declarate/); assert.match(en, /not declared/);
  });
});
