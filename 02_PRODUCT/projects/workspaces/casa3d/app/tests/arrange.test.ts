// Selecția multiplă de piese în plan: grupul, dreptunghiul de selecție, alinierea, distribuirea și operațiile de grup.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { footprintOf, validatePlacement, severityOf } from '../core/validate';
import { DUPLICATE_STEP_CM, duplicatePlacement } from '../core/edit-ops';
import { ALIGN_MODES, addToGroup, alignPieces, applyMoves, arrangeItems, distributePieces, duplicateGroup, groupIds, nudgeGroup, piecesInRect, rotateGroup, toggleMulti, type ArrangeItem, type GroupResult } from '../core/arrange';
import { ARRANGE_IDS, COMMANDS, arrangeCommands, commandById, contextCommands, groupContextCommands, helpRows, matchCommand } from '../lib/commands';
import { DICTS } from '../lib/i18n';
import { buildMenus } from '../components/MenuBar';
import type { Catalog, FurniturePlacement, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog, demo = (): Snapshot => newSnapshot(cat, 'Test', 'demo');
const piece = (s: Snapshot, id: string) => s.placements.find(p => p.id === id)!;
// o piesă de w × d cu centrul în (x, z)
const it = (id: string, x: number, z: number, w: number, d: number): ArrangeItem => ({ id, x, z, box: { x0: x - w / 2, x1: x + w / 2, z0: z - d / 2, z1: z + d / 2 } });
// cutia după mutare: piesa își păstrează mărimea
const moved = (items: ArrangeItem[], out: { id: string; x: number; z: number }[]) => out.map(m => { const i = items.find(q => q.id === m.id)!; return it(m.id, m.x, m.z, i.box.x1 - i.box.x0, i.box.z1 - i.box.z0); });
const mm = (a: number, b: number) => assert.ok(Math.abs(a - b) <= .001 + 1e-9, `${a} ≠ ${b} (la milimetru)`);
const near = (a: number, b: number, msg?: string) => assert.ok(Math.abs(a - b) < 1e-6, `${msg ?? ''} ${a} ≠ ${b}`);

describe('grupul selectat (funcții pure)', () => {
  test('groupIds: piesa principală e prima; altă selecție decât o piesă nu are grup', () => {
    assert.deepEqual(groupIds({ kind: 'placement', id: 'a' }, ['b', 'c']), ['a', 'b', 'c']);
    assert.deepEqual(groupIds({ kind: 'placement', id: 'a' }, ['b', 'a', 'b']), ['a', 'b'], 'fără dubluri, fără principala repetată');
    assert.deepEqual(groupIds({ kind: 'room', id: 'r' }, ['b']), []); assert.deepEqual(groupIds(null, ['b']), []);
  });
  test('toggleMulti: Shift+clic adaugă la coadă sau scoate; dacă iese principala, următoarea îi ia locul', () => {
    assert.deepEqual(toggleMulti([], 'a'), ['a']); assert.deepEqual(toggleMulti(['a'], 'b'), ['a', 'b']);
    assert.deepEqual(toggleMulti(['a', 'b', 'c'], 'b'), ['a', 'c']); assert.deepEqual(toggleMulti(['a', 'b', 'c'], 'a'), ['b', 'c']);
    assert.deepEqual(toggleMulti(['a'], 'a'), [], 'ultima piesă scoasă = nimic selectat');
    const g = ['a', 'b']; toggleMulti(g, 'c'); assert.deepEqual(g, ['a', 'b'], 'lista primită nu se modifică');
  });
  test('addToGroup: ce era rămâne în ordine, piesele noi vin la coadă, fără dubluri', () => {
    assert.deepEqual(addToGroup(['a', 'b'], ['b', 'c', 'c', 'd']), ['a', 'b', 'c', 'd']); assert.deepEqual(addToGroup([], ['x', 'y']), ['x', 'y']);
  });
  test('piecesInRect: centrul în dreptunghi (marginea inclusă), colțurile în orice ordine', () => {
    const ps = [{ id: 'a', x: 1, z: 1 }, { id: 'b', x: 2, z: 3 }, { id: 'c', x: 5, z: 5 }, { id: 'd', x: 3, z: 1 }];
    assert.deepEqual(piecesInRect(ps, [0, 0], [3, 3]), ['a', 'b', 'd']); assert.deepEqual(piecesInRect(ps, [3, 3], [0, 0]), ['a', 'b', 'd'], 'tras de jos în sus');
    assert.deepEqual(piecesInRect(ps, [1.01, 0], [4, 4]), ['b', 'd']); assert.deepEqual(piecesInRect(ps, [10, 10], [11, 11]), []);
  });
  test('arrangeItems: cutia vine din footprintOf (rotația schimbă lățimea cu adâncimea); id-urile necunoscute lipsesc', () => {
    const s = demo(), p = s.placements[0]!, items = arrangeItems(s, cat, [p.id, 'nu-exista']);
    assert.equal(items.length, 1); assert.deepEqual(items[0]!.box, footprintOf(cat, p)); assert.equal(items[0]!.x, p.x);
    const fp = footprintOf(cat, p)!, r = arrangeItems({ placements: [{ ...p, rotation: p.rotation + Math.PI / 2 }] }, cat, [p.id])[0]!;
    near(r.box.x1 - r.box.x0, fp.z1 - fp.z0); near(r.box.z1 - r.box.z0, fp.x1 - fp.x0);
  });
});

describe('alignPieces', () => {
  const items = [it('a', 1, 1, 1, .5), it('b', 3, 2.5, 2, 1), it('c', 6, 4, .6, 2)];   // cutia grupului: x .5 … 6.3, z .75 … 5
  test('stânga / dreapta / sus / jos: marginea cutiei fiecărei piese ajunge la marginea grupului, cealaltă axă nu se mișcă', () => {
    const edge = { left: ['x0', .5], right: ['x1', 6.3], top: ['z0', .75], bottom: ['z1', 5] } as const;
    for (const [mode, [k, v]] of Object.entries(edge) as ['left' | 'right' | 'top' | 'bottom', readonly ['x0' | 'x1' | 'z0' | 'z1', number]][]){ const out = alignPieces(items, mode);
      for (const b of moved(items, out)) near(b.box[k], v, `${mode} ${b.id}`);
      for (const m of out){ const o = items.find(q => q.id === m.id)!; if (k[0] === 'x') assert.equal(m.z, o.z, mode); else assert.equal(m.x, o.x, mode); } }
  });
  test('centerX pune centrele cutiilor pe mijlocul grupului pe x (aceeași verticală); centerZ, pe z', () => {
    for (const m of alignPieces(items, 'centerX')) near(m.x, 3.4); for (const m of alignPieces(items, 'centerZ')) near(m.z, 2.875);
    assert.deepEqual(alignPieces(items, 'centerX').map(m => m.z), items.map(i => i.z));
  });
  test('ordinea și id-urile se păstrează; o piesă deja pe margine nu se mișcă; intrarea nu se modifică', () => {
    const before = structuredClone(items);
    for (const mode of ALIGN_MODES){ const out = alignPieces(items, mode); assert.deepEqual(out.map(m => m.id), ['a', 'b', 'c'], mode); }
    assert.deepEqual(alignPieces(items, 'left')[0], { id: 'a', x: 1, z: 1 }); assert.deepEqual(items, before);
  });
  test('sub două piese nu e nimic de aliniat; alinierea repetată nu mai schimbă nimic', () => {
    assert.deepEqual(alignPieces([], 'left'), []); assert.deepEqual(alignPieces([items[1]!], 'right'), [{ id: 'b', x: 3, z: 2.5 }]);
    const once = alignPieces(items, 'bottom'); assert.deepEqual(alignPieces(moved(items, once), 'bottom'), once);
  });
  test('rezultatul e rotunjit la milimetru', () => {
    for (const m of alignPieces([it('a', .1234567, 0, .333, 1), it('b', 2, 0, .777, 1)], 'centerX')) assert.equal(m.x, Math.round(m.x * 1000) / 1000);
  });
});

describe('distributePieces', () => {
  const gaps = (bs: ArrangeItem[], ax: 'x' | 'z') => { const s = [...bs].sort((p, q) => p.box[`${ax}0`] - q.box[`${ax}0`]); return s.slice(1).map((b, i) => b.box[`${ax}0`] - s[i]!.box[`${ax}1`]); };
  test('spații egale între cutii pe x: extremele rămân pe loc, mijlocul se mută', () => {
    const items = [it('a', 0, 0, 1, 1), it('b', 1.2, 3, 1, 1), it('c', 5, 1, 2, 1), it('d', 9, 0, 1, 1)], out = distributePieces(items, 'x');
    assert.deepEqual(out[0], { id: 'a', x: 0, z: 0 }); assert.deepEqual(out[3], { id: 'd', x: 9, z: 0 });
    const g = gaps(moved(items, out), 'x'); for (const v of g) mm(v, (10 - 5) / 3);   // 10 m între margini, 5 m de piese, 3 spații; pozițiile sunt rotunjite la mm
    assert.deepEqual(out.map(m => m.z), [0, 3, 1, 0], 'axa z neatinsă'); assert.deepEqual(out.map(m => m.id), ['a', 'b', 'c', 'd']);
  });
  test('pe z, cu piesele date în altă ordine decât cea de pe axă', () => {
    const items = [it('m', 0, 2, 1, 1), it('last', 0, 8, 1, 2), it('first', 0, 0, 1, 1)], out = distributePieces(items, 'z');
    assert.deepEqual(out.map(m => m.id), ['m', 'last', 'first']); assert.equal(out[1]!.z, 8); assert.equal(out[2]!.z, 0); near(out[0]!.z, 3.75);   // 9,5 m între margini, 4 m de piese, 2 spații de 2,75 m
    const g = gaps(moved(items, out), 'z'); near(g[0]!, g[1]!); assert.deepEqual(out.map(m => m.x), [0, 0, 0]);
  });
  test('sub trei piese nu se distribuie nimic', () => {
    const two = [it('a', 0, 0, 1, 1), it('b', 4, 2, 1, 1)]; assert.deepEqual(distributePieces(two, 'x'), [{ id: 'a', x: 0, z: 0 }, { id: 'b', x: 4, z: 2 }]); assert.deepEqual(distributePieces([], 'z'), []);
  });
  test('piese care nu încap între extreme: suprapuneri egale (spațiu negativ), extremele tot pe loc', () => {
    const items = [it('a', 0, 0, 2, 1), it('b', .5, 0, 2, 1), it('c', 3, 0, 2, 1)], out = distributePieces(items, 'x');
    assert.equal(out[0]!.x, 0); assert.equal(out[2]!.x, 3); near(out[1]!.x, 1.5); const g = gaps(moved(items, out), 'x'); near(g[0]!, -.5); near(g[1]!, -.5);
  });
  test('deja distribuite: nu se schimbă nimic; intrarea nu se modifică', () => {
    const items = [it('a', 0, 0, 1, 1), it('b', 2, 0, 1, 1), it('c', 4, 0, 1, 1)], before = structuredClone(items);
    assert.deepEqual(distributePieces(items, 'x'), items.map(i => ({ id: i.id, x: i.x, z: i.z }))); assert.deepEqual(items, before);
  });
});

describe('operațiile de grup pe proiectul demo (cameră strâmtă: aici se verifică refuzul întreg și cazul fără schimbare; reușita e verificată pe fixture-ul explicit de mai jos)', () => {
  // id-urile pieselor sunt aleatoare la fiecare proiect nou: testele lucrează pe copii ale aceluiași proiect
  const s0 = demo(), fresh = () => structuredClone(s0), room = s0.floor.rooms.find(r => s0.placements.filter(p => p.roomId === r.id).length >= 2)!, ids = s0.placements.filter(p => p.roomId === room.id).map(p => p.id);
  test('proiectul demo are o cameră cu cel puțin două piese', () => { assert.ok(room && ids.length >= 2); });
  test('applyMoves: aceleași poziții = neschimbat, nimic nu devine „manual”; snapshotul primit nu se modifică', () => {
    const s = fresh(), before = structuredClone(s), r = applyMoves(s, cat, s.placements.map(p => ({ id: p.id, x: p.x, z: p.z })));
    assert.ok(r.ok && !r.changed); if (r.ok) assert.deepEqual(r.snapshot.placements, before.placements); assert.deepEqual(s, before);
    assert.deepEqual(applyMoves(s, cat, [{ id: 'nu-exista', x: 0, z: 0 }]), { ok: false, key: 'edit.noPiece' });
  });
  test('nudgeGroup: toate piesele se mută cu același pas, sau grupul e refuzat întreg (o piesă cu ERROR oprește tot)', () => {
    const s = fresh(), before = structuredClone(s); let okSeen = false;
    for (const [dx, dz] of [[5, 0], [-5, 0], [0, 5], [0, -5]] as const){ const r = nudgeGroup(s, cat, ids, dx, dz);
      if (!r.ok){ assert.equal(r.key, 'edit.refused'); assert.equal(r.cause?.severity, 'ERROR'); continue; } okSeen = true; assert.ok(r.changed); assert.deepEqual(r.ids, ids);
      for (const id of ids){ const a = piece(s, id), b = piece(r.snapshot, id); near(b.x, a.x + dx / 100); near(b.z, a.z + dz / 100); assert.equal(b.source, 'manual');
        assert.notEqual(severityOf(validatePlacement(r.snapshot, cat, b)), 'ERROR'); }
      for (const p of r.snapshot.placements.filter(q => !ids.includes(q.id))) assert.deepEqual(p, s.placements.find(q => q.id === p.id), 'piesele din afara grupului rămân'); }
    assert.deepEqual(s, before);
    const far = nudgeGroup(s, cat, ids, 5000, 0); assert.ok(!far.ok, 'în afara camerei: refuzat'); if (!far.ok) assert.equal(far.cause?.severity, 'ERROR');
    assert.ok(okSeen, 'cel puțin o direcție e liberă în proiectul demo');
  });
  test('rotateGroup în demo: fie toate piesele se rotesc pe loc, fie grupul e refuzat întreg cu un ERROR; intrarea nu se modifică; id necunoscut = edit.noPiece', () => {
    const s = fresh(), before = structuredClone(s), r = rotateGroup(s, cat, ids); assert.deepEqual(s, before);
    if (r.ok) for (const id of ids){ const a = piece(s, id), b = piece(r.snapshot, id);
      near(b.rotation, Math.round(((a.rotation + Math.PI / 2) % (Math.PI * 2)) * 1000) / 1000); assert.equal(b.x, a.x); assert.equal(b.z, a.z); }
    else assert.equal(r.cause?.severity, 'ERROR');
    assert.deepEqual(rotateGroup(s, cat, ['nu-exista']), { ok: false, key: 'edit.noPiece' });
  });
  test('duplicateGroup în demo: fie copii cu același decalaj, fie edit.noFreeSpot când nu e loc; intrarea nu se modifică', () => {
    const s = fresh(); s.appearance = { items: { [ids[0]!]: { color: '#c94f3d', material: 'wood' } } };
    const before = structuredClone(s), r = duplicateGroup(s, cat, ids, i => `copie-${i}`); assert.deepEqual(s, before);
    if (!r.ok){ assert.equal(r.key, 'edit.noFreeSpot'); return; }
    assert.deepEqual(r.ids, ids.map((_, i) => `copie-${i}`)); assert.equal(r.snapshot.placements.length, s.placements.length + ids.length);
    const d = ids.map((id, i) => { const a = s.placements.find(p => p.id === id)!, b = r.snapshot.placements.find(p => p.id === `copie-${i}`)!;
      assert.equal(b.variantId, a.variantId); assert.equal(b.rotation, a.rotation); assert.equal(b.source, 'manual'); assert.notEqual(severityOf(validatePlacement(r.snapshot, cat, b)), 'ERROR');
      return [b.x - a.x, b.z - a.z] as const; });
    for (const [dx, dz] of d){ near(dx, d[0]![0]); near(dz, d[0]![1]); }
    const step = DUPLICATE_STEP_CM / 100, k = Math.max(Math.abs(d[0]![0]), Math.abs(d[0]![1])) / step; near(k, Math.round(k), 'decalajul e un multiplu de 30 cm'); assert.ok(k >= 1);
    assert.deepEqual(r.snapshot.appearance?.items?.['copie-0'], { color: '#c94f3d', material: 'wood' });
    assert.deepEqual(r.snapshot.placements.slice(0, s.placements.length), s.placements, 'originalele rămân');
  });
  test('aliniere + applyMoves în demo: fie refuzat întreg cu un ERROR, fie marginile stângi ajung pe aceeași linie', () => {
    const s = fresh(), items = arrangeItems(s, cat, ids), r = applyMoves(s, cat, alignPieces(items, 'left'));
    if (!r.ok){ assert.equal(r.cause?.severity, 'ERROR'); return; }
    const x0 = Math.min(...items.map(i => i.box.x0)); for (const b of arrangeItems(r.snapshot, cat, ids)) near(b.box.x0, x0);
  });
});

// Drumurile de reușită, pe un fixture construit explicit: o cameră goală de 12 × 10 m (fără pereți, deci fără uși) și patru piese
// din catalogul real, așezate în scară, cu loc liber în jur. Cutiile (m), la rotația 0:
//   canapea 2,28 × 0,95: x 0,86 … 3,14, z 1,025 … 1,975      măsuță 1,18 × 0,78: x 4,01 … 5,19, z 3,01 … 3,79
//   birou   1,05 × 0,50: x 5,975 … 7,025, z 4,95 … 5,45      comodă pe comandă 0,90 × 0,45: x 9,06 … 9,96, z 7,395 … 7,845
// Spațiile dintre ele sunt inegale pe ambele axe, iar locul liber total se împarte exact la milimetru (3 × 1,23 m pe x, 3 × 1,38 m pe z).
describe('operațiile de grup reușesc pe un fixture explicit (cameră goală, piese decalate)', () => {
  const ROOM = { x0: 0, z0: 0, x1: 12, z1: 10 };
  // id, variantă, x, z, lățime și adâncime (cm) așteptate, dimensiune pe comandă
  const SPEC: [string, string, number, number, number, number, FurniturePlacement['size']?][] = [
    ['canapea', 'canapea-0', 2, 1.5, 228, 95], ['masuta', 'masuta-0', 4.6, 3.4, 118, 78], ['birou', 'birou-0', 6.5, 5.2, 105, 50],
    ['comoda', 'comoda-0', 9.51, 7.62, 90, 45, { w: 90, d: 45, h: 98 }]];
  const IDS = SPEC.map(q => q[0]);
  const put = (id: string, variantId: string, x: number, z: number, roomId = 'cam', size?: FurniturePlacement['size']): FurniturePlacement =>
    ({ id, roomId, group: variantId.slice(0, variantId.lastIndexOf('-')), variantId, x, z, rotation: 0, source: 'auto', ...(size ? { size } : {}) });
  const fixture = (extra: FurniturePlacement[] = []): Snapshot => ({ name: 'Fixture',
    floor: { id: 'floor-1', name: 'Parter', ceilingHeight: 2.6, walls: [], rooms: [{ id: 'cam', name: 'Camera mare', type: 'living', rect: { ...ROOM } }, { id: 'cam-mica', name: 'Camera mică', type: 'hol', rect: { x0: 20, z0: 0, x1: 22.4, z1: 1.1 } }] },
    placements: [...SPEC.map(([id, v, x, z, , , size]) => put(id, v, x, z, 'cam', size)), ...extra], selections: {}, picked: [],
    appearance: { items: { canapea: { color: '#c94f3d', material: 'wood' }, comoda: { color: '#224466' } } } });
  // reușită obligatorie: testul cade dacă operația e refuzată sau nu schimbă nimic
  const good = (r: GroupResult, what: string) => { assert.equal(r.ok, true, `${what}: trebuie să reușească (${r.ok ? '' : r.key})`); if (!r.ok) throw new Error(what); assert.equal(r.changed, true, `${what}: trebuie să schimbe ceva`); return r; };
  const refused = (r: GroupResult, code: string, what: string) => { assert.equal(r.ok, false, `${what}: trebuie refuzat`); if (r.ok) throw new Error(what); assert.equal(r.key, 'edit.refused', what); assert.equal(r.cause?.severity, 'ERROR', what); assert.equal(r.cause?.code, code, what); return r; };
  const boxes = (s: Snapshot, ids: readonly string[] = IDS) => arrangeItems(s, cat, ids);
  const noError = (s: Snapshot, what: string) => { for (const p of s.placements) assert.notEqual(severityOf(validatePlacement(s, cat, p)), 'ERROR', `${what}: ${p.id}`); };
  const W = (b: ArrangeItem) => b.box.x1 - b.box.x0, D = (b: ArrangeItem) => b.box.z1 - b.box.z0;

  test('fixture-ul: dimensiunile vin din catalogul real, cutiile sunt cele din comentariu, nicio piesă nu are ERROR', () => {
    const s = fixture(), bs = boxes(s); assert.equal(bs.length, 4);
    for (const [id, v, x, z, w, d, size] of SPEC){ const dm = cat.variants.find(q => q.id === v)!.dimensionsCm!; if (size) assert.deepEqual([size.w, size.d], [w, d]); else assert.deepEqual([dm.w, dm.d], [w, d], v);
      const b = bs.find(q => q.id === id)!; near(b.box.x0, x - w / 200, id); near(b.box.x1, x + w / 200, id); near(b.box.z0, z - d / 200, id); near(b.box.z1, z + d / 200, id); }
    near(Math.min(...bs.map(b => b.box.x0)), .86); near(Math.max(...bs.map(b => b.box.x1)), 9.96); near(Math.min(...bs.map(b => b.box.z0)), 1.025); near(Math.max(...bs.map(b => b.box.z1)), 7.845);
    noError(s, 'fixture');
  });

  test('alignPieces + applyMoves pe fiecare mod: pozițiile se schimbă, marginile / centrele cutiilor ajung exact pe aceeași linie, validarea trece', () => {
    // linia așteptată pe fiecare mod, calculată de mână din cutiile de mai sus
    const want: Record<typeof ALIGN_MODES[number], number> = { left: .86, right: 9.96, centerX: 5.41, top: 1.025, bottom: 7.845, centerZ: 4.435 };
    const line = (b: ArrangeItem, mode: typeof ALIGN_MODES[number]) => mode === 'left' ? b.box.x0 : mode === 'right' ? b.box.x1 : mode === 'centerX' ? (b.box.x0 + b.box.x1) / 2 : mode === 'top' ? b.box.z0 : mode === 'bottom' ? b.box.z1 : (b.box.z0 + b.box.z1) / 2;
    assert.deepEqual([...ALIGN_MODES].sort(), Object.keys(want).sort(), 'toate modurile sunt acoperite');
    for (const mode of ALIGN_MODES){ const s = fixture(), before = structuredClone(s), b0 = boxes(s), onX = mode === 'left' || mode === 'right' || mode === 'centerX';
      const r = good(applyMoves(s, cat, alignPieces(b0, mode)), mode); assert.deepEqual(s, before, `${mode}: intrarea nu se modifică`); assert.deepEqual(r.ids, IDS, mode);
      const b1 = boxes(r.snapshot); let movedCount = 0;
      for (const b of b1){ const o = b0.find(q => q.id === b.id)!; near(line(b, mode), want[mode], `${mode} ${b.id}`); near(W(b), W(o), `${mode} ${b.id} lățimea`); near(D(b), D(o), `${mode} ${b.id} adâncimea`);
        if (onX) assert.equal(b.z, o.z, `${mode} ${b.id}: z neatins`); else assert.equal(b.x, o.x, `${mode} ${b.id}: x neatins`);
        const was = Math.abs(line(o, mode) - want[mode]) < 1e-6, p = piece(r.snapshot, b.id), stays = b.x === o.x && b.z === o.z;
        assert.equal(stays, was, `${mode} ${b.id}: se mută exact piesele care nu erau pe linie`); assert.equal(p.source, was ? 'auto' : 'manual', `${mode} ${b.id}`); if (!stays) movedCount++; }
      assert.ok(movedCount >= 3, `${mode}: cel puțin trei piese s-au mutat (${movedCount})`); if (mode === 'centerX' || mode === 'centerZ') assert.equal(movedCount, 4, mode);
      noError(r.snapshot, mode); }
  });

  test('distributePieces + applyMoves pe x și pe z: spațiile dintre cutii devin egale (1e-6), extremele rămân pe loc, mijlocul se mută', () => {
    const gapsOf = (bs: ArrangeItem[], ax: 'x' | 'z') => { const o = [...bs].sort((p, q) => p.box[`${ax}0`] - q.box[`${ax}0`]); return o.slice(1).map((b, i) => b.box[`${ax}0`] - o[i]!.box[`${ax}1`]); };
    for (const [ax, gap, mid] of [['x', 1.23, { masuta: 4.96, birou: 7.305 }], ['z', 1.38, { masuta: 3.745, birou: 5.765 }]] as const){ const s = fixture(), before = structuredClone(s), b0 = boxes(s), g0 = gapsOf(b0, ax);
      assert.ok(Math.max(...g0) - Math.min(...g0) > .5, `${ax}: fixture-ul pornește cu spații inegale`);
      const r = good(applyMoves(s, cat, distributePieces(b0, ax)), `distribuire ${ax}`); assert.deepEqual(s, before); const b1 = boxes(r.snapshot), g1 = gapsOf(b1, ax);
      assert.equal(g1.length, 3); for (const g of g1) near(g, gap, `spațiul pe ${ax}`);
      for (const id of ['canapea', 'comoda']) assert.deepEqual(piece(r.snapshot, id), piece(s, id), `${ax}: extrema ${id} rămâne pe loc (și „auto”)`);
      for (const id of ['masuta', 'birou'] as const){ const a = piece(s, id), b = piece(r.snapshot, id); near(b[ax], mid[id], `${ax} ${id}`); assert.notEqual(b[ax], a[ax], `${ax}: ${id} s-a mutat`);
        assert.equal(ax === 'x' ? b.z : b.x, ax === 'x' ? a.z : a.x, `${ax}: cealaltă axă neatinsă`); assert.equal(b.source, 'manual'); }
      for (const b of b1){ const o = b0.find(q => q.id === b.id)!; near(W(b), W(o)); near(D(b), D(o)); }
      noError(r.snapshot, `distribuire ${ax}`); }
  });

  test('duplicateGroup reușește: copiile păstrează distanțele dintre ele, au id-uri noi, copiază tot ce copiază duplicarea simplă (variantă, rotație, dimensiune, aspect)', () => {
    const s = fixture(), before = structuredClone(s), r = good(duplicateGroup(s, cat, IDS, i => `copie-${i}`), 'duplicare'); assert.deepEqual(s, before, 'intrarea nu se modifică');
    const copyIds = IDS.map((_, i) => `copie-${i}`); assert.deepEqual(r.ids, copyIds); assert.equal(new Set([...IDS, ...copyIds]).size, 8, 'id-uri noi, fără dubluri');
    assert.equal(r.snapshot.placements.length, 8); assert.deepEqual(r.snapshot.placements.slice(0, 4), s.placements, 'originalele rămân neatinse');
    const rest = ({ id: _i, x: _x, z: _z, ...o }: FurniturePlacement) => o, copies = copyIds.map(id => piece(r.snapshot, id));
    IDS.forEach((id, i) => { const a = piece(s, id), b = copies[i]!; assert.ok(b, id);
      assert.deepEqual(rest(b), { ...rest(a), source: 'manual' }, `${id}: copia e aceeași piesă`);
      // aceleași câmpuri ca la duplicarea unei singure piese
      const one = duplicatePlacement(s, cat, id, 'una'); assert.equal(one.ok, true, id); if (!one.ok) return;
      assert.deepEqual(rest(b), rest(one.snapshot.placements.find(p => p.id === 'una')!), `${id}: ca duplicarea simplă`);
      assert.deepEqual(r.snapshot.appearance?.items?.[b.id], one.snapshot.appearance?.items?.['una'], `${id}: aspectul, ca la duplicarea simplă`); });
    assert.deepEqual(copies[3]!.size, { w: 90, d: 45, h: 98 }, 'dimensiunea pe comandă se copiază');
    assert.deepEqual(r.snapshot.appearance?.items, { ...s.appearance!.items, 'copie-0': { color: '#c94f3d', material: 'wood' }, 'copie-3': { color: '#224466' } }, 'aspectul se copiază doar unde există');
    const looks: Record<string, unknown> = r.snapshot.appearance!.items!; assert.ok(looks['copie-0'] && looks['canapea']); assert.notEqual(looks['copie-0'], looks['canapea'], 'aspectul e o copie, nu aceeași referință');
    // același decalaj la toate, multiplu de 30 cm, deci distanțele relative dintre copii sunt cele dintre originale
    const off = IDS.map((id, i) => [copies[i]!.x - piece(s, id).x, copies[i]!.z - piece(s, id).z] as const);
    for (const [dx, dz] of off){ near(dx, off[0]![0], 'decalaj x'); near(dz, off[0]![1], 'decalaj z'); }
    const step = DUPLICATE_STEP_CM / 100, k = Math.max(Math.abs(off[0]![0]), Math.abs(off[0]![1])) / step; assert.ok(k >= 1 - 1e-9, 'copiile nu stau peste originale'); near(k, Math.round(k), 'multiplu de 30 cm');
    for (let i = 0; i < 4; i++) for (let j = i + 1; j < 4; j++){ const a = piece(s, IDS[i]!), b = piece(s, IDS[j]!); near(copies[j]!.x - copies[i]!.x, b.x - a.x, `x ${i}-${j}`); near(copies[j]!.z - copies[i]!.z, b.z - a.z, `z ${i}-${j}`); }
    const cb = boxes(r.snapshot, copyIds), ob = boxes(s); cb.forEach((b, i) => { near(W(b), W(ob[i]!)); near(D(b), D(ob[i]!)); });
    noError(r.snapshot, 'duplicare'); assert.equal(r.issues.some(i => i.severity === 'ERROR'), false);
  });

  test('rotateGroup reușește: fiecare piesă se rotește cu 90° în jurul centrului ei (centrul rămâne, lățimea și adâncimea cutiei se schimbă între ele), validarea trece', () => {
    const s = fixture(), before = structuredClone(s), b0 = boxes(s), r = good(rotateGroup(s, cat, IDS), 'rotire'); assert.deepEqual(s, before, 'intrarea nu se modifică'); assert.deepEqual(r.ids, IDS);
    const b1 = boxes(r.snapshot);
    for (const [id, , x, z, w, d] of SPEC){ const p = piece(r.snapshot, id), b = b1.find(q => q.id === id)!, o = b0.find(q => q.id === id)!;
      assert.equal(p.rotation, Math.round(Math.PI / 2 * 1000) / 1000, `${id}: 90°`); assert.equal(p.x, x, id); assert.equal(p.z, z, id); assert.equal(p.source, 'manual');
      near(W(b), d / 100, `${id}: lățimea = fosta adâncime`); near(D(b), w / 100, `${id}: adâncimea = fosta lățime`); assert.ok(Math.abs(W(b) - W(o)) > .03, `${id}: cutia chiar s-a schimbat`);
      near((b.box.x0 + b.box.x1) / 2, x, `${id}: centrul cutiei pe x`); near((b.box.z0 + b.box.z1) / 2, z, `${id}: centrul cutiei pe z`); }
    noError(r.snapshot, 'rotire');
    // a doua rotire: 180°, cutiile revin la mărimea de la început, centrele tot pe loc
    const r2 = good(rotateGroup(r.snapshot, cat, IDS), 'a doua rotire');
    for (const b of boxes(r2.snapshot)){ const o = b0.find(q => q.id === b.id)!; assert.equal(piece(r2.snapshot, b.id).rotation, Math.round(Math.PI * 1000) / 1000); near(W(b), W(o)); near(D(b), D(o)); assert.equal(b.x, o.x); assert.equal(b.z, o.z); }
    noError(r2.snapshot, 'a doua rotire');
    // doar o parte din piese: restul rămân exact cum erau
    const part = good(rotateGroup(s, cat, ['masuta', 'nu-exista']), 'rotire parțială'); assert.deepEqual(part.ids, ['masuta']);
    for (const id of ['canapea', 'birou', 'comoda']) assert.deepEqual(piece(part.snapshot, id), piece(s, id));
  });

  test('nudgeGroup reușește: toate piesele se mută cu același pas, în toate cele patru direcții', () => {
    for (const [dx, dz] of [[5, 0], [-5, 0], [0, 50], [0, -50]] as const){ const s = fixture(), r = good(nudgeGroup(s, cat, IDS, dx, dz), `pas ${dx},${dz}`);
      for (const id of IDS){ const a = piece(s, id), b = piece(r.snapshot, id); near(b.x, a.x + dx / 100); near(b.z, a.z + dz / 100); assert.equal(b.source, 'manual'); }
      noError(r.snapshot, `pas ${dx},${dz}`); }
  });

  describe('o singură piesă cu ERROR refuză tot grupul (aceleași piese, fără ea, trec)', () => {
    // un fotoliu exact unde ar ajunge măsuța la alinierea la stânga; o comodă TV lungă lipită de peretele de sus (rotită, iese din cameră);
    // o canapea care umple o cameră mică (nicio copie nu mai are loc acolo)
    const tight = () => fixture([put('fotoliu', 'fotoliu-0', 1.4, 3.4), put('tv', 'comodaTv-0', 10, .3), put('canapea-mica', 'canapea-0', 21.2, .55, 'cam-mica')]);
    test('fixture-ul cu obstacole pornește fără niciun ERROR', () => { noError(tight(), 'obstacole'); });
    test('aliniere la stânga: măsuța ar ajunge peste fotoliu, deci nu se mută nicio piesă', () => {
      const s = tight(), before = structuredClone(s), moves = alignPieces(boxes(s), 'left'), r = refused(applyMoves(s, cat, moves), 'OVERLAP', 'aliniere'); assert.equal(r.cause?.with, 'fotoliu'); assert.deepEqual(s, before);
      const rest = good(applyMoves(s, cat, moves.filter(m => m.id !== 'masuta')), 'fără măsuță'); for (const b of boxes(rest.snapshot, ['canapea', 'birou', 'comoda'])) near(b.box.x0, .86, b.id);
    });
    test('rotire: comoda TV ar ieși din cameră, deci nu se rotește nicio piesă', () => {
      const s = tight(), before = structuredClone(s); refused(rotateGroup(s, cat, ['canapea', 'masuta', 'tv']), 'OUT_OF_ROOM', 'rotire'); assert.deepEqual(s, before);
      good(rotateGroup(s, cat, ['canapea', 'masuta']), 'fără comoda TV');
    });
    test('mutare cu săgețile: comoda TV ar trece prin perete, deci nu se mută nicio piesă', () => {
      const s = tight(), before = structuredClone(s); refused(nudgeGroup(s, cat, ['canapea', 'tv'], 0, -10), 'OUT_OF_ROOM', 'mutare'); assert.deepEqual(s, before);
      good(nudgeGroup(s, cat, ['canapea'], 0, -10), 'fără comoda TV'); good(nudgeGroup(s, cat, ['canapea', 'tv'], 0, 10), 'în direcția liberă');
    });
    test('duplicare: canapeaua din camera mică nu are loc pentru copie, deci nu se copiază nicio piesă (edit.noFreeSpot)', () => {
      const s = tight(), before = structuredClone(s); assert.deepEqual(duplicateGroup(s, cat, ['masuta', 'canapea-mica'], i => `c-${i}`), { ok: false, key: 'edit.noFreeSpot' }); assert.deepEqual(s, before);
      const one = good(duplicateGroup(s, cat, ['masuta'], i => `c-${i}`), 'doar măsuța'); assert.equal(one.snapshot.placements.length, s.placements.length + 1);
    });
  });
});

describe('comenzile de așezare din registru', () => {
  test('alinierea de la două piese, distribuirea de la trei', () => {
    assert.deepEqual(arrangeCommands(0), []); assert.deepEqual(arrangeCommands(1), []);
    assert.deepEqual(arrangeCommands(2), ['arrange.left', 'arrange.centerX', 'arrange.right', 'arrange.top', 'arrange.centerZ', 'arrange.bottom']);
    assert.deepEqual(arrangeCommands(3), [...ARRANGE_IDS]); assert.equal(ARRANGE_IDS.length, 8);
    assert.deepEqual(ARRANGE_IDS.filter(id => !id.includes('distribute')).map(id => id.slice(8)).sort(), [...ALIGN_MODES].sort(), 'fiecare mod de aliniere are comanda lui');
  });
  test('fiecare comandă e în registru, fără tastă, cu etichetă în ro și en', () => {
    for (const id of ARRANGE_IDS){ const c = commandById(id)!; assert.ok(c, id); assert.equal(c.group, 'context'); assert.equal(c.shortcuts.length, 0);
      for (const lang of ['ro', 'en'] as const) assert.ok((DICTS[lang][c.label] ?? '').trim().length > 0, `${lang} ${id}`); }
    assert.equal(new Set(COMMANDS.map(c => c.id)).size, COMMANDS.length);
  });
  test('meniul contextual al unei piese din grup: comenzile piesei, apoi așezarea, cu „șterge” la final; o singură piesă = meniul vechi', () => {
    assert.deepEqual(groupContextCommands(1), contextCommands('placement'));
    const two = groupContextCommands(2), three = groupContextCommands(3);
    assert.equal(two.at(-1), 'delete'); assert.equal(three.at(-1), 'delete'); assert.equal(two.length, contextCommands('placement').length + 6); assert.equal(three.length, contextCommands('placement').length + 8);
    assert.ok(!two.includes('arrange.distributeX') && three.includes('arrange.distributeX') && three.includes('arrange.distributeZ'));
    for (const id of three) assert.ok(commandById(id), id); assert.equal(new Set(three).size, three.length);
  });
  test('Ctrl/⌘+A: afișată din registru și în ajutor, dar prinsă de plan (nu de dispecerul editorului); A simplu rămâne liber pentru mersul în 3D', () => {
    const ev = (key: string, m = {}) => ({ key, shiftKey: false, ctrlKey: false, metaKey: false, altKey: false, ...m });
    assert.equal(commandById('select.all')?.dispatch, false); assert.equal(matchCommand(ev('a', { ctrlKey: true })), null); assert.equal(matchCommand(ev('a')), null);
    assert.ok(helpRows({ mac: false }).some(r => r.label === 'help.selectAll' && r.keys === 'Ctrl+A' && r.section === 'edit'));
    const hit = COMMANDS.filter(c => c.shortcuts.some(s => s.key === 'a')); assert.deepEqual(hit.map(c => c.id), ['select.all']);
  });
  test('meniul Editare primește comenzile de așezare doar când există (grup de cel puțin două piese)', () => {
    const cmd = (id: string) => ({ id, group: 'g', label: id, run: () => {} });
    const edit = (cs: ReturnType<typeof cmd>[]) => buildMenus(cs, k => k).find(m => m.id === 'edit')!.items.map(x => x === 'sep' ? '|' : x.id);
    assert.deepEqual(edit([cmd('undo'), cmd('redo')]), ['undo', 'redo']);
    assert.deepEqual(edit([cmd('undo'), cmd('redo'), cmd('arrange.left'), cmd('arrange.distributeX')]), ['undo', 'redo', '|', 'arrange.left', 'arrange.distributeX']);
  });
});
