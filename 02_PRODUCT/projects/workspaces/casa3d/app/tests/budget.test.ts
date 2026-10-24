// Faza 2 — STOP GATE: cantități și buget calculate din geometria proiectului.
import { test, describe } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { roomGeometry, computeBOQ, computeBudget } from '../core/boq';
import { resolve } from '../core/catalog';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Buget', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const near = (a: number, b: number, eps = .011) => assert.ok(Math.abs(a - b) <= eps, `${a} ≈ ${b}`);

describe('geometrie', () => {
  test('suprafață, perimetru și pereți nete pentru living (5,6 × 3,8 m)', () => {
    const g = roomGeometry(demo(), room(demo(), 'living'));
    near(g.floorArea, 21.28); near(g.perimeter, 18.8); near(g.doorWidth, .8); near(g.windowWidth, 4.5);
    near(g.wallGross, 48.88); near(g.openings, .8 * 2.1 + 4.5 * 1.3); near(g.wallNet, 48.88 - 7.53);
  });
  test('modificarea camerei schimbă imediat suprafața', () => {
    const s = demo(); room(s, 'living').rect.x1 = 9.8; near(roomGeometry(s, room(s, 'living')).floorArea, 6.6 * 3.8);
  });
});
describe('cantități (BOQ)', () => {
  test('parchet cu 10% pierderi, rotunjit la pachete întregi', () => {
    const { items } = computeBOQ(demo(), cat, mc), p = items.find(i => i.key === 'living:floor')!;
    near(p.netQty, 21.28); assert.equal(p.wastePct, .1); assert.equal(p.packs, 12); near(p.orderedQty, 12 * 1.995); near(p.total!, 12 * 1.995 * 38.9, .02);
  });
  test('vopsea: (pereți nete + tavan) × 2 straturi / randament + 10%, în găleți întregi', () => {
    const { items } = computeBOQ(demo(), cat, mc), v = items.find(i => i.key === 'living:paint')!;
    const litres = (48.88 - 7.53 + 21.28) * 2 / 13; near(v.netQty, litres); assert.equal(v.packs, Math.ceil(litres * 1.1 / 15)); near(v.total!, v.packs! * 149);
  });
  test('faianță baie până la 2,1 m, fără ușă', () => {
    const { items } = computeBOQ(demo(), cat, mc); near(items.find(i => i.key === 'baie:walltile')!.netQty, 10.4 * 2.1 - .8 * 2.1);
  });
  test('faianță bucătărie = lungimea mobilierului × 0,6 m', () => {
    const { items } = computeBOQ(demo(), cat, mc); near(items.find(i => i.key === 'bucatarie:walltile')!.netQty, 2.43 * .6);
  });
  test('plintă doar în camerele uscate, perimetru minus uși', () => {
    const s = demo(), { items } = computeBOQ(s, cat, mc); assert.ok(!items.some(i => i.key === 'baie:baseboard'));
    const g = roomGeometry(s, room(s, 'dormitor')); near(items.find(i => i.key === 'dormitor:baseboard')!.netQty, g.perimeter - g.doorWidth);
  });
  test('fiecare linie are preț unitar, sursă și dată de verificare', () => {
    for (const i of computeBOQ(demo(), cat, mc).items){ assert.ok(i.unitPrice != null && i.unitPrice > 0, i.label); assert.ok(i.sourceUrl, i.label); assert.ok(i.verifiedAt, i.label); }
  });
});
describe('buget', () => {
  test('subtotal = suma categoriilor + manoperă + extra; rezervă și TVA corecte', () => {
    const b = computeBudget(demo(), cat, mc), c = b.chosen;
    const sumCats = Object.values(b.categories).reduce((a, v) => a + v, 0), extras = b.extras.reduce((a, e) => a + (e.amount || 0), 0);
    near(c.subtotal, sumCats + b.laborTotals.expected + extras, .05); near(c.contingency, c.subtotal * .1, .02); near(c.total, c.subtotal + c.contingency, .02);
    near(c.vat, c.total - c.total / 1.21, .02);
  });
  test('valorile necunoscute nu intră în total, dar sunt semnalate', () => {
    const b = computeBudget(demo(), cat, mc);
    assert.ok(b.unknownLines.some(l => l.key === 'transport-dedeman')); assert.ok(b.unknownLines.some(l => l.key === 'montaj-mobilier'));
    const s = demo(); s.budget = { ...b.settings, deliveryDedeman: 250, furnitureAssembly: 900 };
    near(computeBudget(s, cat, mc).chosen.subtotal - b.chosen.subtotal, 1150, .05);
  });
  test('depășire de buget: diferența e negativă și statusul „over”', () => {
    const s = demo(); s.budget = { ...computeBudget(s, cat, mc).settings, target: 10000 };
    const b = computeBudget(s, cat, mc); assert.equal(b.status, 'over'); assert.ok(b.diff! < 0);
    s.budget.target = 1e7; assert.equal(computeBudget(s, cat, mc).status, 'under');
  });
  test('scenarii de manoperă: minim ≤ așteptat ≤ maxim; fără manoperă scade totalul', () => {
    const s = demo(), b = computeBudget(s, cat, mc); assert.ok(b.scenarios.low.total <= b.scenarios.expected.total && b.scenarios.expected.total <= b.scenarios.high.total);
    s.budget = { ...b.settings, includeLabor: false }; assert.ok(computeBudget(s, cat, mc).chosen.total < b.chosen.total);
  });
  test('schimbarea unui produs recalculează bugetul exact cu diferența de preț', () => {
    const s = demo(), b0 = computeBudget(s, cat, mc), sofa = s.placements.find(p => p.group === 'canapea')!;
    const p0 = resolve(cat, sofa.variantId)!.offer!.price; sofa.variantId = 'canapea-3'; const p1 = resolve(cat, 'canapea-3')!.offer!.price;
    near(computeBudget(s, cat, mc).categories.furniture - b0.categories.furniture, p1 - p0, .01);
  });
  test('schimbarea finisajului (parchet mai scump) recalculează materialele', () => {
    const s = demo(), b0 = computeBudget(s, cat, mc); s.finishes = { living: { floor: 'parchet-pergo-5006', wallPaint: 'vopsea-innenweiss', light: 'lampa-virrmo', baseboard: 'plinta-mdf-60' } };
    const b1 = computeBudget(s, cat, mc), packs = Math.ceil(21.28 * 1.1 / 2.306);
    near(b1.categories.finishes - b0.categories.finishes, packs * 2.306 * 71 - 12 * 1.995 * 38.9, .05);
  });
});
