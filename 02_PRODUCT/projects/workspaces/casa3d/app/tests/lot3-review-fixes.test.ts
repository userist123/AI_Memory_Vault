// Reparațiile din review-ul lotului 3 (PR #344): golurile pereților de demolat nu mai contează nicăieri (descriere, elevații,
// planșe, verificări, consilier, mobilare, twin), nivelul nou nu urcă pereții de demolat, fișa de finisaje are zonele și
// pereții noi din gips-carton, bara din Planșe rămâne plată, câmpurile caloriferului aplică la blur/Enter, materialul zonei
// se validează ca pardoseala camerei.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { describePlan } from '../core/plan-describe';
import { wallElevations } from '../core/drawings';
import { finishSchedule } from '../core/finish-schedule';
import { adviseProject } from '../core/advisor';
import { refineFurnishing } from '../core/furnish';
import { snapshotToTwin } from '../lib/twin';
import { addLevel } from '../core/levels';
import { activeOpenings } from '../core/renovation';
import { cmToClampedM } from '../core/tech-geometry';
import { sanitizeZone, isMaterialRef, PlanLimitError, FLOOR_PATTERNS } from '../core/plan-limits';
import { sanitizeFinishes, PATTERNS } from '../core/finishes';
import { Walls } from '../components/Drawings';
import { translator } from '../lib/i18n';
import type { Catalog, MaterialsCatalog, Snapshot, Wall } from '../core/types';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const t = translator('ro');
// camera 5 × 4 m: wall-1 = nord cu fereastra op-2, wall-4 = vest cu ușa de intrare op-1
const blank = (): Snapshot => { const s = newSnapshot(cat, 'Lot 3', 'blank'); s.placements = []; return s; };
const inner = (o: Partial<Wall> = {}): Wall => ({ id: 'wall-in', a: [2.5, 0], b: [2.5, 4], thickness: .1, exterior: false, openings: [{ id: 'op-in', kind: 'door', offset: 1, width: .8 }], ...o });
const src = (f: string) => readFileSync(path.resolve(import.meta.dirname, '..', f), 'utf8');

describe('golurile active (perete de demolat = fără goluri)', () => {
  test('helperul comun: peretele de demolat nu are goluri, celelalte își păstrează lista', () => {
    const w = inner(); assert.equal(activeOpenings(w), w.openings); assert.equal(activeOpenings({ ...w, status: 'new' }), w.openings);
    assert.deepEqual(activeOpenings({ ...w, status: 'demolish' }), []);
  });
  test('lista pentru cititorul de ecran: peretele de demolat rămâne, cu starea lui, dar fără golurile lui', () => {
    const s = blank(); s.floor.walls[0]!.status = 'demolish';
    const items = describePlan(s, cat, { t, units: 'metric', lang: 'ro' }).find(g => g.id === 'structure')!.items;
    assert.match(items.find(i => i.id === 'wall-1')!.label, / · De demolat$/);
    assert.ok(!items.some(i => i.kind === 'opening' && i.id === 'op-2'), 'fereastra peretelui demolat nu mai apare');
    assert.ok(items.some(i => i.kind === 'opening' && i.id === 'op-1'), 'ușa de pe peretele existent rămâne');
  });
  test('elevațiile: golurile peretelui de demolat nu se desenează', () => {
    const s = blank(), room = s.floor.rooms[0]!, north = () => wallElevations(s, mc, cat, s.floor, room).find(e => e.side === 'N')!;
    assert.equal(north().openings.length, 1); s.floor.walls[0]!.status = 'demolish'; assert.deepEqual(north().openings, []);
    assert.equal(wallElevations(s, mc, cat, s.floor, room).find(e => e.side === 'W')!.openings.length, 1, 'ușa de intrare rămâne');
  });
  test('planșele (Drawings): peretele de demolat punctat, fără goluri; peretele nou hașurat; existentul plin', () => {
    const s = blank(); s.floor.walls[0]!.status = 'demolish'; s.floor.walls.push(inner({ status: 'new', openings: [] }));
    const html = renderToStaticMarkup(createElement('svg', null, createElement(Walls, { fl: s.floor })));
    assert.match(html, /data-status="demolish"[^>]*stroke-dasharray="\.08 \.05"/);
    assert.match(html, /data-status="new"/); assert.equal((html.match(/stroke-dasharray="\.03 \.03"/g) ?? []).length, 1);
    // goluri: doar ușa de intrare (fereastra peretelui demolat nu se mai desenează)
    assert.equal((html.match(/stroke="#5b8fb9"/g) ?? []).length, 0); assert.equal((html.match(/stroke="#fff"/g) ?? []).length, 2, 'ușa de intrare + hașura peretelui nou');
  });
  test('baia fără altă fereastră decât una pe un perete de demolat cere ventilator (VENT_BATH)', () => {
    const s = blank(); s.floor.rooms[0]!.type = 'baie'; const codes = () => adviseProject(s, cat).map(a => a.code);
    assert.ok(!codes().includes('VENT_BATH')); s.floor.walls[0]!.status = 'demolish'; assert.ok(codes().includes('VENT_BATH'));
  });
  test('consilierul: ușa unui perete de demolat nu mai cere întrerupător (TECH_NO_SWITCH)', () => {
    const s = blank(); s.tech = []; const has = () => adviseProject(s, cat).some(a => a.code === 'TECH_NO_SWITCH');
    assert.ok(has()); s.floor.walls[3]!.status = 'demolish'; assert.ok(!has());
  });
  test('mobilarea holului: intrarea de pe un perete de demolat nu mai rezervă loc liber (ca fără goluri)', () => {
    const hall = (mut: (s: Snapshot) => void) => { const s = blank(); s.floor.rooms[0]!.type = 'hol'; mut(s);
      return refineFurnishing(cat, { floor: s.floor, placements: [], selections: s.selections, roomId: 'camera-1' }).placements.map(p => [p.group, p.x, p.z, p.rotation]); };
    const demolished = hall(s => { s.floor.walls[3]!.status = 'demolish'; }), none = hall(s => { s.floor.walls[3]!.openings = []; });
    assert.deepEqual(demolished, none); assert.notDeepEqual(hall(() => {}), none, 'intrarea activă schimbă mobilarea');
  });
  test('twin: golurile peretelui de demolat nu intră; peretele rămâne', () => {
    const s = blank(); s.floor.walls[0]!.status = 'demolish'; const tw = snapshotToTwin(s, cat);
    assert.ok(tw.walls.some(w => w.id === 'wall-1')); assert.deepEqual(tw.openings.map(o => o.id), ['op-1']);
  });
  test('locurile reparate folosesc același helper, nu un filtru copiat', () => {
    for (const f of ['core/furnish.ts', 'core/advisor.ts', 'core/trade-checks.ts', 'lib/twin.ts', 'core/plan-describe.ts', 'core/drawings.ts']) assert.match(src(f), /activeOpenings\(/, f);
  });
});

describe('nivelul nou (addLevel)', () => {
  test('pereții de demolat nu urcă (nici golurile și nișele lor); pereții noi urcă, cu starea lor', () => {
    const s = blank(); s.floor.walls.push(inner({ status: 'demolish' }), inner({ id: 'wall-new', a: [0, 2], b: [5, 2], status: 'new', openings: [] }));
    s.floor.niches = [{ id: 'nisa-1', wallId: 'wall-in', offset: 2, width: .6, height: .4, depth: .1, elevation: 1.1 }];
    const up = addLevel(s, { name: 'Etaj' }).levels![0]!;
    assert.equal(up.walls.length, 5); assert.ok(!up.walls.some(w => w.status === 'demolish'));
    assert.ok(!up.walls.some(w => w.openings.some(o => o.id === 'op-in' || o.width === .8 && o.offset === 1)), 'nici golul peretelui demolat');
    assert.equal(up.walls.filter(w => w.status === 'new').length, 1); assert.ok(!up.niches?.length);
  });
});

describe('fișa de finisaje', () => {
  test('zona de pardoseală e un rând „Pardoseală”, cu materialul și așezarea zonei', () => {
    const s = blank(); s.finishes = { 'camera-1': { ...s.finishes?.['camera-1'], floor: 'parchet-egger-h2099', floorLayout: { pattern: 'straight' } } as NonNullable<Snapshot['finishes']>[string] };
    s.floor.zones = [{ id: 'zona-1', roomId: 'camera-1', rect: { x0: 0, z0: 0, x1: 2, z1: 1.5 }, floor: 'gresie-mckinley', layout: { pattern: 'checker' }, label: 'Intrare' }];
    const rows = finishSchedule(s, cat, mc).filter(r => r.element === 'floor'), z = rows.find(r => r.details.some(d => d.key === 'sched.zone'));
    assert.equal(rows.length, 2, 'camera și zona');
    assert.ok(z, 'rândul zonei există'); assert.equal(z!.product, mc.materials.find(m => m.id === 'gresie-mckinley')!.name);
    assert.ok(z!.details.some(d => d.key === 'fin.pattern.checker'), 'așezarea zonei, nu a camerei');
    assert.equal(t('sched.zone', z!.details.find(d => d.key === 'sched.zone')!.vars as Record<string, string | number>), 'zonă Intrare 2 × 1.5 m');
  });
  test('peretele nou din gips-carton apare în fișă, cu eticheta lui', () => {
    const s = blank(); s.floor.walls.push(inner({ status: 'new', openings: [] }));
    const row = finishSchedule(s, cat, mc).find(r => r.element === 'drywall');
    assert.ok(row); assert.equal(row!.product, mc.materials.find(m => m.category === 'plasterboard')!.name); assert.equal(row!.roomId, null);
    assert.equal(t(`sched.el.${row!.element}`), 'Perete nou gips-carton'); assert.equal(translator('en')(`sched.el.${row!.element}`), 'New plasterboard wall');
    const d = row!.details.find(d => d.key === 'sched.newWall'); assert.ok(d); assert.equal(d!.vars?.t, 10); assert.equal(row!.level, 0);
    assert.match(translator('en')('sched.newWall', d!.vars as Record<string, string | number>), /^New wall 10 cm/);
  });
  test('peretele nou de la etaj apare la nivelul lui în fișă', () => {
    const s0 = blank(), s = addLevel(s0, { name: 'Etaj' }), up = s.levels![0]!; up.walls.push(inner({ status: 'new', openings: [] }));
    const row = finishSchedule(s, cat, mc).find(r => r.element === 'drywall'); assert.ok(row); assert.equal(row!.level, 1);
  });
});

describe('caloriferul: câmpurile de mărime', () => {
  test('cm → m, adus în interval și rotunjit; text gol sau nenumeric = nicio schimbare', () => {
    assert.equal(cmToClampedM('80', .2, 3), .8); assert.equal(cmToClampedM('5', .2, 3), .2); assert.equal(cmToClampedM('900', .2, 3), 3);
    assert.equal(cmToClampedM('', .2, 3), null); assert.equal(cmToClampedM('abc', .2, 3), null); assert.equal(cmToClampedM('Infinity', .2, 3), null);
  });
  test('valoarea se aplică doar la blur sau Enter; eticheta citită include camera', () => {
    const tp = src('components/TechPanel.tsx'), field = tp.slice(tp.indexOf('function CmField'), tp.indexOf('function RadiatorFields'));
    assert.match(field, /onChange=\{e => setDraft\(e\.target\.value\)\}/); assert.match(field, /onBlur=\{commit\}/); assert.match(field, /e\.key === 'Enter'\) commit\(\)/);
    assert.ok(!/onChange=\{[^}]*onPatch\(\{ (widthM|heightM)/.test(tp), 'onChange nu mai trimite mărimea');
    assert.equal(t('radiator.widthAria', { room: 'Bucătărie' }), 'Lățime calorifer, Bucătărie (cm)');
    assert.equal(t('radiator.heightAria', { room: 'Bucătărie' }), 'Înălțime calorifer, Bucătărie (cm)');
    assert.equal(translator('en')('radiator.widthAria', { room: 'Kitchen' }), 'Radiator width, Kitchen (cm)');
  });
});

describe('materialul zonei: aceeași regulă ca pardoseala camerei', () => {
  const zone = (floor: unknown) => ({ id: 'zona-1', roomId: 'camera-1', rect: { x0: 0, z0: 0, x1: 2, z1: 1.5 }, floor });
  const zoneOk = (v: unknown) => { try { sanitizeZone(zone(v)); return true; } catch (e){ assert.ok(e instanceof PlanLimitError); return false; } };
  test('aceleași valori acceptate și respinse (zona cere în plus un material)', () => {
    for (const v of ['gresie-mckinley', 'x'.repeat(80), 'x'.repeat(81), 42, {}, ['a'], true]){
      const room = sanitizeFinishes({ 'camera-1': { floor: v } }) === null;
      assert.equal(zoneOk(v), room, JSON.stringify(v)); assert.equal(isMaterialRef(v), room, JSON.stringify(v)); }
    assert.equal(zoneOk(''), false); assert.equal(zoneOk(null), false);
  });
  test('o singură listă de moduri de așezare', () => { assert.deepEqual(PATTERNS, [...FLOOR_PATTERNS]); });
});
