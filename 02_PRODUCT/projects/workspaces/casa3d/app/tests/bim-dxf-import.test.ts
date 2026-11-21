// Faza 2 din ghidul BIM (§3.1–3.3): import DXF vectorial → pereți candidați, cu unități explicite, proveniență și revizie umană.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { parseDxf, wallCandidates, applyWallCandidates, DXF_LIMITS } from '../core/dxf-import';
import { sanitizeWallOrigin } from '../core/plan-limits';
import { elementIndex } from '../core/elements';
import type { Floor } from '../core/types';

const pairs = (...kv: (string | number)[]) => { const out: string[] = []; for (let i = 0; i < kv.length; i += 2) out.push(String(kv[i]), String(kv[i + 1])); return out.join('\r\n') + '\r\n'; };
const header = (insunits: number | null) => insunits == null ? '' : pairs(0, 'SECTION', 2, 'HEADER', 9, '$INSUNITS', 70, insunits, 0, 'ENDSEC');
const line = (layer: string, x1: number, y1: number, x2: number, y2: number) => pairs(0, 'LINE', 8, layer, 10, x1, 20, y1, 30, 0, 11, x2, 21, y2, 31, 0);
const dxf = (insunits: number | null, ...ents: string[]) => header(insunits) + pairs(0, 'SECTION', 2, 'ENTITIES') + ents.join('') + pairs(0, 'ENDSEC', 0, 'EOF');
const SHA = 'a'.repeat(64);

test('parseDxf: citește LINE și LWPOLYLINE închisă, cu straturile și unitatea din $INSUNITS', () => {
  const lw = pairs(0, 'LWPOLYLINE', 8, 'ZID', 90, 3, 70, 1, 10, 0, 20, 0, 10, 1000, 20, 0, 10, 1000, 20, 1000);
  const r = parseDxf(dxf(4, line('PERETI', 0, 0, 4000, 0), lw));
  assert.equal(r.units, 'mm');
  assert.equal(r.segments.length, 1 + 3, 'polilinia închisă cu 3 vârfuri are 3 laturi');
  assert.deepEqual(r.layers.sort(), ['PERETI', 'ZID']);
  assert.deepEqual(r.segments[0], { layer: 'PERETI', a: [0, 0], b: [4000, 0] });
});
test('parseDxf: POLYLINE cu VERTEX și SEQEND', () => {
  const pl = pairs(0, 'POLYLINE', 8, 'W', 66, 1, 70, 0, 0, 'VERTEX', 8, 'W', 10, 0, 20, 0, 0, 'VERTEX', 8, 'W', 10, 0, 20, 3, 0, 'VERTEX', 8, 'W', 10, 2, 20, 3, 0, 'SEQEND');
  const r = parseDxf(dxf(6, pl));
  assert.equal(r.units, 'm'); assert.equal(r.segments.length, 2);
});
test('parseDxf: fără $INSUNITS unitatea e necunoscută (nu se ghicește)', () => {
  assert.equal(parseDxf(dxf(null, line('A', 0, 0, 1, 0))).units, null);
  assert.equal(parseDxf(dxf(0, line('A', 0, 0, 1, 0))).units, null, '$INSUNITS 0 = fără unitate');
});
test('parseDxf: respinge fișiere care nu sunt DXF ASCII, prea mari sau cu prea multe entități', () => {
  assert.throws(() => parseDxf('AutoCAD Binary DXF\r\n\x1a\x00'), /DXF/);
  assert.throws(() => parseDxf('x'.repeat(DXF_LIMITS.maxBytes + 1)), /mare/);
  const many = Array.from({ length: DXF_LIMITS.maxSegments + 1 }, (_, i) => line('A', i, 0, i, 1));
  assert.throws(() => parseDxf(dxf(4, ...many)), /entități/);
  assert.throws(() => parseDxf(dxf(4, line('A', 0, 0, 1e12, 0))), /coordonat/);
});
test('wallCandidates: două linii paralele la 25 cm devin un perete cu axa la mijloc și grosimea măsurată', () => {
  const p = parseDxf(dxf(4, line('PERETI', 0, 0, 4000, 0), line('PERETI', 0, 250, 4000, 250)));
  const c = wallCandidates(p, { unit: 'mm' });
  assert.equal(c.length, 1);
  assert.deepEqual(c[0]!.a, [0, -0.125]); assert.deepEqual(c[0]!.b, [4, -0.125]);
  assert.equal(c[0]!.thickness, 0.25); assert.equal(c[0]!.thicknessFrom, 'source'); assert.equal(c[0]!.confidence, 'MEDIUM');
});
test('wallCandidates: o linie singură nu primește grosime inventată', () => {
  const c = wallCandidates(parseDxf(dxf(6, line('AX', 0, 0, 3, 0))), { unit: 'm' });
  assert.equal(c.length, 1); assert.equal(c[0]!.thickness, null); assert.equal(c[0]!.confidence, 'LOW');
});
test('wallCandidates: fără unitate cunoscută nu produce nimic până nu o alege utilizatorul; straturile se pot filtra', () => {
  const p = parseDxf(dxf(null, line('PERETI', 0, 0, 3, 0), line('MOBILA', 0, 1, 3, 1)));
  assert.throws(() => wallCandidates(p, {}), /unitat/i);
  assert.equal(wallCandidates(p, { unit: 'm', layers: ['PERETI'] }).length, 1);
  assert.equal(wallCandidates(p, { unit: 'm', minLengthM: 5 }).length, 0, 'segmentele scurte (hașuri, simboluri) se ignoră');
});
test('applyWallCandidates: adaugă doar candidații aprobați, cu id unic și proveniență; fără grosime cere una explicită', () => {
  const floor: Floor = { id: 'f0', name: 'P', ceilingHeight: 2.6, rooms: [], walls: [{ id: 'dxf-w-1', a: [0, 0], b: [1, 0], thickness: .2, exterior: false, openings: [] }] };
  const p = parseDxf(dxf(4, line('PERETI', 0, 0, 4000, 0), line('PERETI', 0, 250, 4000, 250), line('AX', 0, 3000, 3000, 3000)));
  const c = wallCandidates(p, { unit: 'mm' });
  assert.throws(() => applyWallCandidates(floor, c, { sha256: SHA, offset: [0, 0] }), /grosime/);
  const out = applyWallCandidates(floor, c, { sha256: SHA, offset: [1, 2], manualThicknessM: .1 });
  assert.equal(out.walls.length, 3);
  const added = out.walls.slice(1);
  assert.equal(new Set(out.walls.map(w => w.id)).size, 3, 'id-urile nu se ciocnesc cu cele existente');
  assert.deepEqual(added[0]!.origin, { method: 'dxf', sha256: SHA, layer: 'PERETI', thickness: 'source' });
  assert.equal(added[1]!.origin!.thickness, 'manual'); assert.equal(added[1]!.thickness, .1);
  assert.deepEqual(added[0]!.a, [1, 1.875], 'decalajul ales de utilizator se aplică');
  assert.equal(floor.walls.length, 1, 'nivelul primit nu e modificat');
  const ix = elementIndex({ name: 'T', floor: out, placements: [], selections: {}, picked: [] });
  assert.deepEqual(ix.find(e => e.elementId === added[0]!.id)!.origin, added[0]!.origin);
});
test('sanitizeWallOrigin: păstrează doar proveniența validă', () => {
  const w: any = { origin: { method: 'dxf', sha256: SHA, layer: 'x'.repeat(300), thickness: 'source', extra: 1 } };
  sanitizeWallOrigin(w); assert.deepEqual(w.origin, { method: 'dxf', sha256: SHA, layer: 'x'.repeat(100), thickness: 'source' });
  const bad: any = { origin: { method: 'ocr', sha256: SHA, layer: 'a', thickness: 'source' } };
  assert.throws(() => sanitizeWallOrigin(bad), /proveniența/i);
  const none: any = {}; sanitizeWallOrigin(none); assert.equal('origin' in none, false);
});
test('wallCandidates: o cameră desenată cu fețe interioare și exterioare dă patru axe care se întâlnesc în colțuri', () => {
  const L = (x1: number, y1: number, x2: number, y2: number) => line('PERETI', x1, y1, x2, y2);
  const p = parseDxf(dxf(4, L(0, 0, 4000, 0), L(-250, -250, 4250, -250), L(4000, 0, 4000, 3000), L(4250, -250, 4250, 3250),
    L(4000, 3000, 0, 3000), L(4250, 3250, -250, 3250), L(0, 3000, 0, 0), L(-250, 3250, -250, -250)));
  const c = wallCandidates(p, { unit: 'mm' });
  assert.equal(c.length, 4); for (const w of c) assert.equal(w.thickness, .25);
  const ends = c.flatMap(w => [w.a, w.b]).map(q => q.join(','));
  for (const corner of ['-0.125,0.125', '4.125,0.125', '4.125,-3.125', '-0.125,-3.125']) assert.equal(ends.filter(e => e === corner).length, 2, `colțul ${corner}`);
});
