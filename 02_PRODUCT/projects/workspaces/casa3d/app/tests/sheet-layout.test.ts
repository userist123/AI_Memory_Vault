// Așezarea pe plăci: fără suprapuneri (cu tot cu lățimea tăieturii), în zona utilă, fibra respectată, tăiabil doar cap-coadă,
// determinist, piesele prea mari raportate, număr de plăci rezonabil, rapid.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { layoutSheets, layoutCutParts, isGuillotine, LAYOUT_DEFAULT, type LayoutPart, type LayoutOptions, type LayoutResult } from '../core/sheet-layout';
import { wardrobeCutList, DEFAULT_WARDROBE } from '../core/joinery';

const P = (id: string, l: number, w: number, qty = 1, grain = true, material = 'pal'): LayoutPart => ({ id, name: `P${id}`, l, w, qty, grain, material });
// generator determinist (LCG), ca testele să nu depindă de Math.random
const rng = (seed: number) => () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32;

/** Verificările comune pe orice rezultat: fiecare bucată plasată o dată, în zona utilă, fără suprapuneri (cu kerf), fibra, ghilotină. */
function verify(parts: LayoutPart[], r: LayoutResult, o: LayoutOptions){
  const placed = new Map<string, number>(), byId = new Map(parts.map(p => [p.id, p]));
  for (const s of r.sheets){
    assert.ok(s.placements.length > 0, 'nicio placă goală');
    for (const q of s.placements){ const p = byId.get(q.id)!; placed.set(q.id, (placed.get(q.id) ?? 0) + 1);
      assert.equal(s.material, p.material, 'piesa stă pe placa materialului ei');
      assert.ok(q.x >= o.trim - 1e-6 && q.y >= o.trim - 1e-6 && q.x + q.l <= o.sheetL - o.trim + 1e-6 && q.y + q.w <= o.sheetW - o.trim + 1e-6, `piesa ${q.id} iese din zona utilă`);
      if (q.rotated){ assert.equal(p.grain, false, `piesa ${q.id} are fibră și a fost rotită`); assert.deepEqual([q.l, q.w], [p.w, p.l]); } else assert.deepEqual([q.l, q.w], [p.l, p.w]); }
    for (let i = 0; i < s.placements.length; i++) for (let k = i + 1; k < s.placements.length; k++){ const a = s.placements[i]!, b = s.placements[k]!;
      const apart = a.x + a.l + o.kerf <= b.x + 1e-6 || b.x + b.l + o.kerf <= a.x + 1e-6 || a.y + a.w + o.kerf <= b.y + 1e-6 || b.y + b.w + o.kerf <= a.y + 1e-6;
      assert.ok(apart, `piesele ${a.id} și ${b.id} se suprapun sau nu au loc de tăietură`); }
    assert.ok(isGuillotine(s, o.kerf), 'placa se taie doar cap-coadă');
    const used = s.placements.reduce((a, q) => a + q.l * q.w, 0); assert.ok(Math.abs(used - s.usedArea) < 1e-3); assert.ok(s.waste >= 0 && s.waste < 1);
  }
  const lost = new Map(r.unplaced.map(u => [u.id, u]));
  for (const p of parts) assert.equal(placed.get(p.id) ?? 0, lost.has(p.id) ? 0 : p.qty, `piesa ${p.id}: toate bucățile plasate o singură dată`);
  assert.equal(r.stats.total, r.sheets.length); assert.equal(Object.values(r.stats.sheets).reduce((a, b) => a + b, 0), r.sheets.length);
}
const estimate = (parts: LayoutPart[], o: LayoutOptions) => Math.ceil(parts.reduce((a, p) => a + p.l * p.w * p.qty, 0) / (o.sheetL * o.sheetW));

test('un corp de bucătărie încape pe o placă; piesele rămân în zona utilă, cu loc de tăietură între ele', () => {
  const parts = [P('1', 720, 560, 2), P('2', 564, 560, 1), P('3', 564, 100, 2), P('4', 562, 540, 1), P('5', 716, 596, 1, false, 'hdf')];
  const r = layoutSheets(parts, LAYOUT_DEFAULT); verify(parts, r, LAYOUT_DEFAULT);
  assert.deepEqual(r.stats.sheets, { pal: 1, hdf: 1 }); assert.deepEqual(r.unplaced, []); assert.equal(r.stats.pieces, 7);
  assert.ok(r.sheets.every(s => s.placements.every(q => q.x >= 10 && q.y >= 10)), 'marginea tunsă rămâne liberă');
});

test('fibra: piesa cu fibră nu se rotește niciodată; fără fibră se rotește când doar așa încape', () => {
  const o = { sheetL: 2800, sheetW: 2070, kerf: 4, trim: 10 };
  const long = [P('1', 2050, 600, 3, true), P('2', 600, 2040, 1, false)], r = layoutSheets(long, o); verify(long, r, o);
  assert.ok(r.sheets.flatMap(s => s.placements).filter(q => q.id === '1').every(q => !q.rotated && q.l === 2050));
  const tall = [P('1', 2040, 2700, 1, false)], rt = layoutSheets(tall, o); verify(tall, rt, o);   // 2700 nu încape pe lățimea utilă de 2050: doar rotită
  assert.equal(rt.sheets[0]!.placements[0]!.rotated, true); assert.deepEqual([rt.sheets[0]!.placements[0]!.l, rt.sheets[0]!.placements[0]!.w], [2700, 2040]);
  // pătratele și piesele care intră oricum: rotirea e opțională, dar cu fibră e interzisă pe orice ordonare
  const mix = Array.from({ length: 40 }, (_, i) => P(String(i + 1), 300 + (i * 37) % 900, 150 + (i * 53) % 500, 1 + i % 3, i % 2 === 0)), rm = layoutSheets(mix, o); verify(mix, rm, o);
});

test('piesele prea mari sunt raportate cu motiv, nu aruncă; restul se așază', () => {
  const parts = [P('1', 2900, 500), P('2', 600, 2100, 1, true), P('3', 2060, 2700, 1, true), P('4', 500, 400, 2), P('5', 0, 100), P('6', 100, 100, 1.5), P('7', NaN, 5)];
  const r = layoutSheets(parts, LAYOUT_DEFAULT); verify(parts, r, LAYOUT_DEFAULT);
  assert.deepEqual(r.unplaced.map(u => [u.id, u.reason]), [['1', 'tooBig'], ['2', 'grain'], ['3', 'tooBig'], ['5', 'invalid'], ['6', 'invalid'], ['7', 'invalid']]);
  assert.equal(r.unplaced[0]!.name, 'P1'); assert.equal(r.sheets.length, 1); assert.equal(r.sheets[0]!.placements.length, 2);
  // placă imposibilă (tunderea mănâncă tot) sau parametri greșiți: totul neplasat, fără excepție
  for (const bad of [{ sheetL: 100, sheetW: 100, kerf: 4, trim: 60 }, { sheetL: NaN, sheetW: 2070, kerf: 4, trim: 10 }, { sheetL: 2800, sheetW: 2070, kerf: -1, trim: 10 }]){
    const x = layoutSheets([P('1', 50, 50)], bad); assert.equal(x.sheets.length, 0); assert.equal(x.unplaced[0]!.reason, 'tooBig'); }
  assert.deepEqual(layoutSheets([], LAYOUT_DEFAULT), { sheets: [], unplaced: [], stats: { sheets: {}, total: 0, pieces: 0, partArea: 0, sheetArea: 2800 * 2070, utilization: 0 } });
});

test('determinist: aceleași piese dau exact același plan, indiferent de câte ori rulează', () => {
  const rnd = rng(7), parts = Array.from({ length: 60 }, (_, i) => P(String(i + 1), 200 + Math.floor(rnd() * 1400), 100 + Math.floor(rnd() * 600), 1 + Math.floor(rnd() * 3), rnd() < .6, rnd() < .8 ? 'pal' : 'front'));
  const a = layoutSheets(parts, LAYOUT_DEFAULT), b = layoutSheets(parts.map(p => ({ ...p })), LAYOUT_DEFAULT); verify(parts, a, LAYOUT_DEFAULT);
  assert.deepEqual(a, b); assert.equal(JSON.stringify(a), JSON.stringify(b));
});

test('număr de plăci rezonabil: cel mult 1,5 × estimarea pe arie, pe cazuri tipice (bucătărie, dulap, amestec)', () => {
  const kitchen = [P('1', 720, 560, 12), P('2', 564, 560, 4), P('3', 764, 560, 1), P('4', 564, 100, 8), P('5', 764, 100, 2), P('6', 562, 540, 4), P('7', 720, 320, 10), P('8', 564, 320, 10), P('9', 562, 300, 5), P('10', 2100, 560, 2), P('11', 2700, 100, 2)];
  const w = wardrobeCutList(2000, 2400, 600, { ...DEFAULT_WARDROBE, sections: [{ kind: 'hang' }, { kind: 'shelves', shelves: 5 }, { kind: 'drawers', drawers: 4 }] });
  const wardrobe = w.parts.map((p, i) => ({ ...p, id: String(i + 1) }));
  const rnd = rng(11), mixed = Array.from({ length: 80 }, (_, i) => P(String(i + 1), 250 + Math.floor(rnd() * 900), 120 + Math.floor(rnd() * 480), 1 + Math.floor(rnd() * 2), true));
  for (const [name, parts] of [['bucătărie', kitchen], ['dulap', wardrobe], ['amestec', mixed]] as const){
    const r = layoutSheets(parts, LAYOUT_DEFAULT); verify(parts, r, LAYOUT_DEFAULT); assert.deepEqual(r.unplaced, [], name);
    for (const m of Object.keys(r.stats.sheets)){ const est = estimate(parts.filter(p => p.material === m), LAYOUT_DEFAULT);
      assert.ok(r.stats.sheets[m]! >= est, `${name}/${m}: nu poate fi sub arie`); assert.ok(r.stats.sheets[m]! <= Math.ceil(est * 1.5), `${name}/${m}: ${r.stats.sheets[m]} plăci față de ${est} pe arie`); }
  }
  // piese identice care umplu exact: 4 × (1388 × 1023) cu kerf 4 pe 2780 × 2050 → o singură placă
  const exact = [P('1', 1388, 1023, 4)], re = layoutSheets(exact, LAYOUT_DEFAULT); verify(exact, re, LAYOUT_DEFAULT); assert.equal(re.sheets.length, 1);
  assert.ok(re.sheets[0]!.waste > 0 && re.sheets[0]!.waste < .03);
});

test('validatorul de ghilotină: recunoaște planurile tăiabile și respinge „morișca” și suprapunerile', () => {
  const q = (x: number, y: number, l: number, w: number) => ({ id: '1', name: 'p', x, y, l, w, rotated: false });
  assert.equal(isGuillotine({ placements: [] }), true); assert.equal(isGuillotine({ placements: [q(0, 0, 10, 10)] }), true);
  assert.equal(isGuillotine({ placements: [q(0, 0, 100, 50), q(0, 50, 60, 50), q(60, 50, 40, 50)] }), true);
  // morișca: patru piese în jurul unui pătrat central — nicio tăietură nu traversează placa
  const pin = [q(0, 0, 200, 100), q(200, 0, 100, 200), q(100, 200, 200, 100), q(0, 100, 100, 200), q(100, 100, 100, 100)];
  assert.equal(isGuillotine({ placements: pin }), false);
  assert.equal(isGuillotine({ placements: [q(0, 0, 100, 100), q(50, 50, 100, 100)] }), false, 'suprapunere');
  assert.equal(isGuillotine({ placements: [q(0, 0, 100, 100), q(102, 0, 100, 100)] }, 4), false, 'tăietura de 4 mm nu are loc în 2 mm');
  assert.equal(isGuillotine({ placements: [q(0, 0, 100, 100), q(104, 0, 100, 100)] }, 4), true);
});

test('performanță: 500 de piese sub 500 ms, cu toate verificările trecute', () => {
  const rnd = rng(3), parts = Array.from({ length: 500 }, (_, i) => P(String(i + 1), 150 + Math.floor(rnd() * 1200), 80 + Math.floor(rnd() * 520), 1, rnd() < .7, i % 5 === 0 ? 'front' : 'pal'));
  layoutSheets(parts.slice(0, 50), LAYOUT_DEFAULT);   // încălzire (JIT)
  const t0 = performance.now(), r = layoutSheets(parts, LAYOUT_DEFAULT), ms = performance.now() - t0;
  assert.ok(ms < 500, `${Math.round(ms)} ms`); verify(parts, r, LAYOUT_DEFAULT); assert.equal(r.stats.pieces, 500);
  for (const m of ['pal', 'front']) assert.ok(r.stats.sheets[m]! <= Math.ceil(estimate(parts.filter(p => p.material === m), LAYOUT_DEFAULT) * 1.5));
});

test('lista de debitare a dulapului: piesele sunt numerotate ca în CSV și încap pe plăci', () => {
  const w = wardrobeCutList(1800, 2400, 600, DEFAULT_WARDROBE), r = layoutCutParts(w.parts);
  assert.deepEqual(r.unplaced, []); assert.ok(r.stats.sheets.pal! >= 1 && r.stats.sheets.hdf === 1);
  assert.ok(r.stats.sheets.pal! <= w.sheets + 1, 'planul real e aproape de estimarea cu 15%');
  assert.deepEqual([...new Set(r.sheets.flatMap(s => s.placements.map(q => q.id)))].sort((a, b) => +a - +b), w.parts.map((_, i) => String(i + 1)));
  // dulap de 3 m: blatul și baza (2964 mm) nu încap pe placa de 2800 → raportate, nu ascunse
  const big = layoutCutParts(wardrobeCutList(3000, 2400, 600, DEFAULT_WARDROBE).parts); assert.ok(big.unplaced.some(u => u.name === 'Blat' && u.reason === 'tooBig'));
});
