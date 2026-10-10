import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { suggestTechPoints } from '../core/technical';
import { planToDxf, dxfText, DXF_LAYERS } from '../core/dxf';
import { roomSchedule } from '../core/dimensions';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { exportProjectDxf } from '../lib/dxfExport';
import type { Catalog, Snapshot } from '../core/types';
const cat = catalogJson as unknown as Catalog;
const demo = (): Snapshot => { const s = newSnapshot(cat, 'Demo', 'demo'); s.tech = suggestTechPoints(s); return s; };

type Pair = [number, string];
const pairs = (dxf: string): Pair[] => { const l = dxf.split('\n'), out: Pair[] = []; for (let i = 0; i + 1 < l.length; i += 2) out.push([Number(l[i]), l[i + 1]]); return out; };
interface Ent { type: string; layer: string; p: Pair[] }
function entities(dxf: string): Ent[] {
  const ps = pairs(dxf), out: Ent[] = []; let inEnt = false, cur: Ent | null = null;
  for (let i = 0; i < ps.length; i++){ const [c, v] = ps[i];
    if (c === 2 && ps[i - 1]?.[1] === 'SECTION') inEnt = v === 'ENTITIES';
    if (!inEnt) continue;
    if (c === 0){ cur = { type: v, layer: '', p: [] }; out.push(cur); } else if (cur){ if (c === 8) cur.layer = v; cur.p.push([c, v]); } }
  return out;
}
const coords = (e: Ent) => e.p.filter(([c]) => (c >= 10 && c <= 59) && c !== 40 && c !== 50 && c !== 51).map(([, v]) => Number(v));

test('structura: HEADER la început, EOF la sfârșit, straturile definite și folosite', () => {
  const dxf = planToDxf(demo(), cat, { lang: 'ro' }), ps = pairs(dxf);
  assert.deepEqual(ps[0], [0, 'SECTION']); assert.deepEqual(ps[1], [2, 'HEADER']);
  assert.ok(dxf.trimEnd().endsWith('\n0\nEOF'));
  assert.ok(dxf.includes('$INSUNITS\n70\n4') && dxf.includes('$EXTMIN') && dxf.includes('$EXTMAX') && dxf.includes('AC1009'));
  const table = dxf.slice(dxf.indexOf('2\nLAYER'), dxf.indexOf('2\nENTITIES')), ents = entities(dxf);
  for (const { name } of DXF_LAYERS){ assert.ok(table.includes(`2\n${name}\n`), `${name} în tabelul LAYER`); assert.ok(ents.some(e => e.layer === name), `${name} are entități`); }
  assert.equal(new Set(DXF_LAYERS.map(l => l.color)).size, DXF_LAYERS.length, 'culori ACI distincte');
});
test('toate coordonatele sunt finite', () => {
  const ents = entities(planToDxf(demo(), cat, { lang: 'en' })); assert.ok(ents.length > 50);
  for (const e of ents) for (const n of coords(e)) assert.ok(Number.isFinite(n), `${e.type} ${e.layer}`);
});
test('extinderea stratului WALLS = caseta pereților ×1000 (y inversat), ±1 mm', () => {
  const s = demo(), ents = entities(planToDxf(s, cat, { lang: 'ro' })).filter(e => e.layer === 'WALLS');
  const xs: number[] = [], ys: number[] = [];
  for (const e of ents){ const v = e.p; for (let i = 0; i < v.length; i++) if (v[i][0] === 10 && v[i + 1]?.[0] === 20){ xs.push(Number(v[i][1])); ys.push(Number(v[i + 1][1])); } }
  let ex0 = Infinity, ex1 = -Infinity, ez0 = Infinity, ez1 = -Infinity;
  for (const w of s.floor.walls){ const h = w.thickness / 2; // bandă de grosimea peretelui în jurul axei
    const horiz = Math.abs(w.b[0] - w.a[0]) >= Math.abs(w.b[1] - w.a[1]);
    ex0 = Math.min(ex0, w.a[0] - (horiz ? 0 : h), w.b[0] - (horiz ? 0 : h)); ex1 = Math.max(ex1, w.a[0] + (horiz ? 0 : h), w.b[0] + (horiz ? 0 : h));
    ez0 = Math.min(ez0, w.a[1] - (horiz ? h : 0), w.b[1] - (horiz ? h : 0)); ez1 = Math.max(ez1, w.a[1] + (horiz ? h : 0), w.b[1] + (horiz ? h : 0)); }
  const close = (a: number, b: number) => assert.ok(Math.abs(a - b) <= 1, `${a} vs ${b}`);
  close(Math.min(...xs), ex0 * 1000); close(Math.max(...xs), ex1 * 1000); close(Math.min(...ys), -ez1 * 1000); close(Math.max(...ys), -ez0 * 1000);
});
test('un ARC per ușă, un TEXT cu aria per cameră, un CERC per punct tehnic', () => {
  const s = demo(), ents = entities(planToDxf(s, cat, { lang: 'ro' }));
  const doors = s.floor.walls.flatMap(w => w.openings).filter(o => o.kind === 'door').length;
  assert.ok(doors > 0); assert.equal(ents.filter(e => e.type === 'ARC').length, doors);
  assert.ok(ents.filter(e => e.layer === 'WINDOWS').length % 3 === 0);
  const texts = ents.filter(e => e.type === 'TEXT' && e.layer === 'ROOMS').map(e => e.p.find(([c]) => c === 1)![1]);
  for (const r of roomSchedule(s).rows) assert.ok(texts.some(t => t.includes(r.area.toFixed(2))), `aria ${r.id}`);
  assert.ok(s.tech!.length > 0); assert.equal(ents.filter(e => e.type === 'CIRCLE' && e.layer === 'SERVICES').length, s.tech!.length);
  assert.equal(ents.filter(e => e.layer === 'FURNITURE' && e.type === 'POLYLINE').length, s.placements.length);
});
test('eticheta camerei: nume și arie pe rânduri separate, încap în lățimea camerei', () => {
  const s = demo(), ents = entities(planToDxf(s, cat, { lang: 'en' }));
  const val = (e: { p: [number, string][] }, c: number) => e.p.find(([k]) => k === c)![1];
  const labels = ents.filter(e => e.type === 'TEXT' && e.layer === 'ROOMS');
  assert.equal(labels.length, s.floor.rooms.length * 2);
  for (const r of s.floor.rooms){ const w = (r.rect.x1 - r.rect.x0) * 1000, cx = (r.rect.x0 + r.rect.x1) / 2 * 1000;
    const mine = labels.filter(e => Math.abs(Number(val(e, 10)) - cx) < 1 && Number(val(e, 20)) < -r.rect.z0 * 1000 && Number(val(e, 20)) > -r.rect.z1 * 1000);
    assert.equal(mine.length, 2, `două rânduri pentru ${r.id}`);
    for (const e of mine){ const chars = val(e, 1).replace(/\\U\+[0-9A-F]{4}/g, '#').length;
      assert.ok(chars * Number(val(e, 40)) * .9 <= w, `${val(e, 1)} depășește ${r.id}`); } }
});
test('non-ASCII este codat \\U+XXXX, fără ă/ș/ț brute', () => {
  const s = demo(); s.floor.rooms[0].name = 'Bucătărie ășț'; const dxf = planToDxf(s, cat, { lang: 'ro' });
  assert.ok(/^[\x00-\x7f]*$/.test(dxf), 'doar ASCII'); assert.ok(dxf.includes('Buc\\U+0103t\\U+0103rie \\U+0103\\U+0219\\U+021B'));
  assert.equal(dxfText('a\nb%%c\\d'), 'a b%c/d');
});
test('proiect fără puncte tehnice: fără CIRCLE, fișier valid', () => {
  const s = newSnapshot(cat, 'Gol', 'blank'), ents = entities(planToDxf(s, cat, { lang: 'en' }));
  assert.equal(ents.filter(e => e.type === 'CIRCLE').length, 0); assert.ok(ents.some(e => e.layer === 'WALLS'));
});
// ---- la nivel de repo (aceeași cale ca ruta: proprietar + 404) ----
beforeAll(() => resetDbForTests());
const A = '11111111-1111-4111-8111-111111111111', B = '22222222-2222-4222-8222-222222222222';
test('export proiect: nume de fișier sigur, 404 pentru alt proprietar', async () => {
  const id = await repo.createProject(A, 'Casă ș/ț "test"', 'demo');
  const r = await exportProjectDxf(A, id, 'ro'); assert.match(r.filename, /^[A-Za-z0-9._-]+\.dxf$/); assert.ok(r.body.startsWith('0\nSECTION\n2\nHEADER'));
  await assert.rejects(exportProjectDxf(B, id, 'ro'), (e: any) => e.status === 404);
  await assert.rejects(exportProjectDxf(A, '33333333-3333-4333-8333-333333333333', 'ro'), (e: any) => e.status === 404);
});
