// Variante A/B pe cameră: păstrare, aplicare, comparare cu diferența de cost, validarea la salvare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { captureOption, applyOption, compareOptions, activeOption, removeOption, updateOption, sanitizeRoomOptions, MAX_OPTIONS, OptionError } from '../core/room-options';
import { groups } from '../core/catalog';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog, G = groups(cat);
const NOW = '2026-10-11T20:00:00.000Z';

function demo(){ const s = newSnapshot(cat, 'Demo', 'demo', 'ro'), room = s.floor.rooms.find(r => r.type === 'living')!;
  const sofa = s.placements.find(p => p.roomId === room.id && p.group === 'canapea')!; return { s, room, sofa }; }
const other = (group: string, cur: string) => G[group]!.variants.find(v => v.id !== cur)!.id;

test('A păstrează camera, B e altă canapea și alt parchet; aplicarea lui A readuce exact starea inițială', () => {
  const { s, room, sofa } = demo();
  let x = captureOption(s, room.id, 'Ca în ofertă', 'oa', NOW);
  assert.equal(activeOption(x, room.id)?.label, 'A');
  const floor = mc.materials.find(m => m.category === 'parquet' && m.id !== x.roomOptions![room.id]![0]!.finishes.floor)!.id;
  x = { ...x, placements: x.placements.map(p => p.id === sofa.id ? { ...p, variantId: other('canapea', sofa.variantId) } : p), finishes: { ...x.finishes, [room.id]: { ...x.roomOptions![room.id]![0]!.finishes, floor } } };
  assert.equal(activeOption(x, room.id), null);   // camera s-a schimbat, nu mai e A
  x = captureOption(x, room.id, '', 'ob', NOW);
  assert.deepEqual(x.roomOptions![room.id]!.map(o => o.label), ['A', 'B']);
  const back = applyOption(x, room.id, 'oa');
  assert.equal(back.placements.find(p => p.id === sofa.id)!.variantId, sofa.variantId);
  assert.equal(activeOption(back, room.id)?.label, 'A');
  // celelalte camere nu se ating
  assert.deepEqual(back.placements.filter(p => p.roomId !== room.id), s.placements.filter(p => p.roomId !== room.id));
});

test('compararea: diferența de cost a lui B față de A e chiar diferența totalului proiectului, iar schimbările se numesc', () => {
  const { s, room, sofa } = demo();
  let x = captureOption(s, room.id, '', 'oa', NOW);
  x = { ...x, placements: x.placements.map(p => p.id === sofa.id ? { ...p, variantId: other('canapea', sofa.variantId) } : p) };
  x = captureOption(x, room.id, '', 'ob', NOW);
  const [a, b] = compareOptions(x, cat, mc, room.id);
  assert.equal(a!.delta, 0); assert.equal(b!.active, true); assert.equal(a!.active, false);
  assert.ok(Math.abs(b!.delta - (b!.total - a!.total)) < 0.01);
  assert.ok(b!.changes.some(c => c.kind === 'product' && c.label === 'canapea' && c.from !== c.to));
});

test('cel mult trei variante; ștergerea ultimei variante scoate câmpul; actualizarea preia starea curentă', () => {
  const { s, room, sofa } = demo();
  let x: Snapshot = s; for (let i = 0; i < MAX_OPTIONS; i++) x = captureOption(x, room.id, '', `o${i}`, NOW);
  assert.throws(() => captureOption(x, room.id, '', 'o9', NOW), OptionError);
  x = { ...x, placements: x.placements.map(p => p.id === sofa.id ? { ...p, variantId: other('canapea', sofa.variantId) } : p) };
  x = updateOption(x, room.id, 'o1', NOW);
  assert.equal(x.roomOptions![room.id]![1]!.variants[sofa.id], other('canapea', sofa.variantId));
  for (const id of ['o0', 'o1', 'o2']) x = removeOption(x, room.id, id);
  assert.equal(x.roomOptions, undefined);
});

test('o piesă cu dimensiune pe comandă primește dimensiunea din catalog când varianta o schimbă', () => {
  const { s, room, sofa } = demo();
  let x = captureOption(s, room.id, '', 'oa', NOW);
  x = { ...x, placements: x.placements.map(p => p.id === sofa.id ? { ...p, variantId: other('canapea', sofa.variantId), size: { w: 250, d: 95, h: 80 } } : p) };
  const back = applyOption(x, room.id, 'oa').placements.find(p => p.id === sofa.id)!;
  assert.equal(back.size, undefined); assert.equal(back.variantId, sofa.variantId);
});

test('validarea la salvare: camera ștearsă își pierde variantele, o etichetă sau un finisaj invalid e respins', () => {
  const { s, room } = demo();
  const x = captureOption(s, room.id, '<b>nota</b>', 'oa', NOW) as any;
  x.roomOptions.fantoma = x.roomOptions[room.id];
  assert.equal(sanitizeRoomOptions(x), null); assert.deepEqual(Object.keys(x.roomOptions), [room.id]);
  assert.equal(x.roomOptions[room.id][0].note.includes('<'), false);
  const y = structuredClone(x); y.roomOptions[room.id][0].label = 'Z'; assert.ok(sanitizeRoomOptions(y));
  const z = structuredClone(x); z.roomOptions[room.id][0].finishes = { floor: 7 }; assert.ok(sanitizeRoomOptions(z));
  const w = structuredClone(x); w.roomOptions[room.id] = [1, 2, 3, 4]; assert.ok(sanitizeRoomOptions(w));
});

test('ordinea cheilor din baza de date și null în loc de lipsă nu fac din A o variantă diferită', () => {
  const { s, room } = demo(); const x = captureOption(s, room.id, '', 'oa', NOW);
  const f = x.roomOptions![room.id]![0]!.finishes as any, reordered = Object.fromEntries(Object.keys(f).reverse().map(k => [k, f[k]]));
  const y = { ...x, finishes: { ...x.finishes, [room.id]: { ...reordered, rug: null } } } as Snapshot;
  assert.equal(activeOption(y, room.id)?.label, 'A');
  const [, b] = compareOptions(captureOption({ ...y, roomOptions: undefined }, room.id, '', 'oa', NOW), cat, mc, room.id); assert.equal(b, undefined);
});
