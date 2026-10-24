// Materialele și sistemele cu preț verificat pe 2026-10-24 (casete și șine de ușă glisantă, uși pliante, calorifere cu panouri și
// port-prosop, balustradă, aer condiționat split, baghete pentru boiserie): fiecare ofertă are dovada ei, iar devizul folosește
// prețul doar când produsul e ales; fără produs ales, liniile rămân necunoscute ca înainte.
import { describe, test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import evidence24 from '../data/materials-evidence.2026-10-24.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, finishesOf } from '../core/boq';
import { finishSchedule } from '../core/finish-schedule';
import { placeTechPoint, techBodyFields, sanitizeTech } from '../core/technical';
import { sanitizeRailing } from '../core/plan-limits';
import { checkSnapshot } from '../lib/repo';
import { RETAILER_HOSTS, isAllowedUrl } from '../core/outbound';
import { procurementLines } from '../core/procurement';
import type { Catalog, MaterialsCatalog, Opening, Railing, Snapshot } from '../core/types';
import type { TechPoint } from '../core/technical';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const M = (id: string) => mc.materials.find(m => m.id === id)!;
const blank = (): Snapshot => { const s = newSnapshot(cat, 'Catalog', 'blank'); s.placements = []; return s; };
const withInner = (o: Partial<Opening>): Snapshot => { const s = blank();
  s.floor.walls.push({ id: 'in-1', a: [0, 2], b: [5, 2], thickness: .15, exterior: false, openings: [{ id: 'd-1', kind: 'door', offset: 2, width: .8, ...o }] }); return s; };
const NEW = mc.materials.filter(m => m.verifiedAt === '2026-10-24');
const SUPPLIER_ID: Record<string, string> = { Dedeman: 'dedeman', IKEA: 'ikea-ro', Hornbach: 'hornbach' };
const unknownIds = (b: ReturnType<typeof computeBOQ>) => b.unknown;

describe('dovezi și date (2026-10-24)', () => {
  test('fiecare material nou are dovada lui (URL, preț citit, specificații) și un link permis spre magazin', () => {
    const ev = new Map((evidence24 as any).items.map((x: any) => [x.id, x]));
    assert.equal(NEW.length, (evidence24 as any).items.length);
    for (const m of NEW){ const e: any = ev.get(m.id); assert.ok(e && e.sourceUrl === m.sourceUrl && e.evidence && e.verifiedAt === '2026-10-24', m.id);
      // prețul din catalog e cel scris în dovadă (Dedeman „358.99 lei”, Hornbach „1210,00 lei”)
      const shown = Number(String(e.evidence).match(/(\d+(?:[.,]\d+)?)\s*lei/)![1]!.replace(',', '.')); assert.equal(shown, m.unitPrice, m.id);
      assert.equal(m.verificationType, 'product_page', m.id); assert.equal(m.unit, 'buc', m.id);
      const sid = SUPPLIER_ID[m.supplier]!; assert.ok(RETAILER_HOSTS[sid]!.includes(new URL(m.sourceUrl!).hostname), m.id); assert.ok(isAllowedUrl(m.sourceUrl!, 'direct', null, sid), m.id); }
    assert.equal(new Set(NEW.map(m => m.sourceUrl)).size, NEW.length, 'URL-uri unice');
    // Hornbach intră ca furnizor: în catalog (tabelul retailers) și cu nota de disponibilitate păstrată
    assert.ok(cat.suppliers.some(s => s.id === 'hornbach' && s.name === 'Hornbach'));
    assert.match(M('hornbach-aparat-aer-conditionat-chigo-12000-btu-alb-incl-kit-de-instalare-3m').note!, /indisponibil/);
    assert.match(M('hornbach-balustrada-pertura-forma-g-1-5-m-prindere-podea').note!, /Nu poate fi comandat online/);
    for (const m of NEW) assert.doesNotMatch(m.note ?? '', /nu trece de teste/, m.id);
  });
  test('categoriile: door_system pe sistem, uși pliante, calorifere, balustradă pe parte, split, baghete de 2 m', () => {
    const by = (c: string) => mc.materials.filter(m => m.category === c);
    assert.ok(by('door_system').length >= 4 && by('panel_radiator').length >= 4 && by('railing').length >= 3 && by('hvac_split').length >= 3);
    for (const m of by('door_system')) assert.ok(['sliding', 'pocket', 'folding'].includes(m.specs?.system as string), m.id);
    assert.ok(by('door_system').filter(m => m.specs?.system === 'pocket').length >= 4 && by('door_system').filter(m => m.specs?.system === 'sliding').length >= 5);
    for (const m of by('railing')) assert.ok(['handrail', 'post', 'panel', 'set'].includes(m.specs?.railPart as string), m.id);
    for (const m of by('panel_radiator')) assert.deepEqual(m.specs?.sizeCm, [80, 60], m.id);
    for (const m of NEW.filter(x => x.category === 'door')){ assert.deepEqual(m.specs?.doorTypes, ['folding'], m.id); assert.match(m.note!, /accesoriile de prindere nu sunt incluse/, m.id); }
    for (const m of NEW.filter(x => x.category === 'moulding')) assert.equal(m.specs?.pieceM, 2, m.id);
  });
});

describe('devizul folosește prețul doar când produsul e ales', () => {
  test('ușa în perete: caseta aleasă intră în deviz; șina doar pe glisantă; ușa pliantă cu accesoriile neincluse', () => {
    const cas = 'dedeman-caseta-cu-sistem-culisare-megadoor-140-x-205-cm', rail = 'dedeman-sistem-culisare-usa-interior-loft-otel-2-m-100-kg';
    const s = withInner({ doorType: 'pocket' }); s.doors = { 'd-1': { system: cas } };
    const it = computeBOQ(s, cat, mc).items.find(i => i.key === 'doorsys:d-1')!; assert.equal(it.unitPrice, 1135); assert.equal(it.total, 1135); assert.equal(it.supplier, 'Dedeman');
    assert.ok(finishSchedule(s, cat, mc).some(r => r.element === 'door_system' && r.unitPrice === 1135));
    s.doors = { 'd-1': { system: rail } }; assert.ok(!computeBOQ(s, cat, mc).items.some(i => i.key === 'doorsys:d-1'), 'șina nu se pune pe ușa în perete');
    const g = withInner({ doorType: 'sliding' }); g.doors = { 'd-1': { system: rail } }; assert.equal(computeBOQ(g, cat, mc).items.find(i => i.key === 'doorsys:d-1')!.total, 459.99);
    const hb = withInner({ doorType: 'pocket' }); hb.doors = { 'd-1': { system: 'hornbach-sistem-eclisse-cu-dimensiunea-exterioara-174-6x212-cm-pentru-usi-glisa' } };
    const h = computeBOQ(hb, cat, mc).items.find(i => i.key === 'doorsys:d-1')!; assert.equal(h.total, 1210); assert.equal(h.supplier, 'Hornbach');
    // comanda pe furnizori: sistemul de la Hornbach e o linie a magazinului Hornbach, cu linkul paginii
    const ord = procurementLines(hb, cat, mc).find(l => l.supplier === 'Hornbach')!; assert.ok(ord && ord.total === 1210 && ord.url!.startsWith('https://www.hornbach.ro/'));
    const f = withInner({ doorType: 'folding', width: .85 }); f.doors = { 'd-1': { product: 'dedeman-usa-de-interior-plianta-italbox-alb-85-x-203-cm' } };
    const fb = computeBOQ(f, cat, mc); assert.equal(fb.items.find(i => i.key === 'door:d-1')!.total, 226); assert.ok(fb.unknown.some(u => /Accesorii de prindere ușă pliantă/.test(u)));
  });
  test('caloriferul: cu panouri sau port-prosop, ales → linia de 1 buc; fără produs → necunoscut, ca înainte', () => {
    const s = blank(), p = placeTechPoint(s, 'radiator', 2.5, .3)!; s.tech = [p];
    const before = computeBOQ(s, cat, mc); assert.ok(before.unknown.some(u => /^Calorifer panou 80×60 cm .*fără preț verificat$/.test(u))); assert.ok(!before.items.some(i => i.key.includes(':radiator:')));
    s.tech = [{ ...p, product: 'dedeman-calorifer-otel-mastas-22pkkp-600-x-800-mm-accesorii-incluse' }];
    const b = computeBOQ(s, cat, mc), it = b.items.find(i => i.key === `camera-1:radiator:${p.id}`)!;
    assert.equal(it.total, 269); assert.equal(it.category, 'finishes'); assert.ok(b.unknown.some(u => /Racordare și montaj calorifer/.test(u))); assert.ok(!b.unknown.some(u => /^Calorifer panou/.test(u)));
    assert.ok(finishSchedule(s, cat, mc).some(r => r.element === 'radiator' && r.total === 269));
    // tipul și categoria trebuie să se potrivească: un port-prosop ales pe un calorifer cu panouri nu se socotește
    s.tech = [{ ...p, product: 'dedeman-calorifer-baie-hirapan-hs4001000wf-portprosop-drept-alb-400-x-1000-mm-' }]; assert.ok(!computeBOQ(s, cat, mc).items.some(i => i.key.includes(':radiator:')));
    s.tech = [{ ...p, model: 'towel', widthM: .4, heightM: 1, product: 'dedeman-calorifer-baie-hirapan-hs4001000wf-portprosop-drept-alb-400-x-1000-mm-' }];
    assert.equal(computeBOQ(s, cat, mc).items.find(i => i.key.includes(':radiator:'))!.total, 143);
  });
  test('aerul condiționat: aparatul ales intră în deviz, traseul și montajul rămân necunoscute', () => {
    const s = blank(), p: TechPoint = { id: 'hvac_split:camera-1:m1', kind: 'hvac_split', roomId: 'camera-1', x: 2.5, z: .2, height: 2.2, reason: 'manual' }; s.tech = [p];
    assert.ok(computeBOQ(s, cat, mc).unknown.some(u => /^Unitate AC interioară \+ traseu/.test(u)));
    s.tech = [{ ...p, product: 'dedeman-aparat-aer-conditionat-ariston-nevis-12000-btu-a-a-inverter-wi-fi-r32-' }];
    const b = computeBOQ(s, cat, mc); assert.equal(b.items.find(i => i.key === `camera-1:hvac:${p.id}`)!.total, 2199);
    assert.ok(b.unknown.some(u => /Traseu frigorific și montaj AC/.test(u)) && !b.unknown.some(u => /^Unitate AC interioară/.test(u)));
    s.tech = [{ ...p, product: 'hornbach-aparat-aer-conditionat-bosch-ac-climate-2000-12000-btu-fara-kit-de-ins' }]; assert.equal(computeBOQ(s, cat, mc).items.find(i => i.key.includes(':hvac:'))!.supplier, 'Hornbach');
  });
  test('balustrada de metal: bucăți de profil sau stâlpi după lungime; sticla și lipsa produsului rămân necunoscute', () => {
    const rail = (o: Partial<Railing> = {}): Railing => ({ id: 'bal-1', a: [0, 4.5], b: [3, 4.5], ...o }), s = blank();
    s.floor.railings = [rail()]; assert.ok(computeBOQ(s, cat, mc).unknown.some(u => /^Balustradă metal 3 ml, h 100 cm \(material și montaj\): fără preț verificat$/.test(u)));
    s.floor.railings = [rail({ product: 'dedeman-mana-curenta-balustrada-akdemir-aluminiu-eloxat-gri-antracit-2000-x-60' })];
    let b = computeBOQ(s, cat, mc), it = b.items.find(i => i.key === 'railing:bal-1')!; assert.equal(it.orderedQty, 2); assert.equal(it.total, 143); assert.ok(b.unknown.some(u => /^Stâlpi, umplutură și montaj balustradă/.test(u)));
    s.floor.railings = [rail({ product: 'dedeman-kit-stalp-balcon-akdemir-p5-aluminiu-eloxat-gri-antracit-1010-x-60-x-6' })];
    it = computeBOQ(s, cat, mc).items.find(i => i.key === 'railing:bal-1')!; assert.equal(it.orderedQty, 4); assert.equal(it.total, 4 * 364);
    s.floor.railings = [rail({ product: 'hornbach-balustrada-pertura-forma-g-1-5-m-prindere-podea' })]; b = computeBOQ(s, cat, mc);
    assert.equal(b.items.find(i => i.key === 'railing:bal-1')!.total, 2 * 2199); assert.ok(b.unknown.some(u => /^Montaj balustradă/.test(u)));
    assert.ok(finishSchedule(s, cat, mc).some(r => r.element === 'railing'));
    s.floor.railings = [rail({ kind: 'glass', product: 'hornbach-balustrada-pertura-forma-g-1-5-m-prindere-podea' })]; b = computeBOQ(s, cat, mc);
    assert.ok(!b.items.some(i => i.key === 'railing:bal-1') && b.unknown.some(u => /^Balustradă sticlă/.test(u)));
  });
  test('boiseria cu bagheta nouă de 2 m: bucăți întregi la prețul paginii', () => {
    const s = blank(), room = s.floor.rooms[0]!, id = 'dedeman-bagheta-decorativa-polistiren-a40-clasic-alb-200-x-3-x-3-cm';
    s.finishes = { [room.id]: { ...finishesOf(s, room), wallFeatures: [{ side: 'S', kind: 'frames', material: id, fromM: .1, heightM: 1 }] } };
    const it = computeBOQ(s, cat, mc).items.find(i => i.refId === id)!; assert.ok(it && it.unit === 'buc' && it.unitPrice === 5.9 && it.orderedQty === Math.ceil(it.orderedQty));
    assert.equal(it.total, Math.round(it.orderedQty * 5.9 * 100) / 100);
  });
  test('serverul păstrează produsul ales pe calorifer, split și balustrada de metal; îl scoate la sticlă sau când id-ul e stricat', () => {
    const s = blank(), p = placeTechPoint(s, 'radiator', 2.5, .3)!;
    assert.equal(techBodyFields({ ...p, product: 'dedeman-calorifer-otel-pekpan-22k-600-x-800-accesorii-incluse' }).product, 'dedeman-calorifer-otel-pekpan-22k-600-x-800-accesorii-incluse');
    assert.equal(techBodyFields({ ...p, product: '<script>' }).product, undefined);
    assert.equal(techBodyFields({ kind: 'hvac_split', product: 'x-1' }).product, 'x-1'); assert.deepEqual(techBodyFields({ kind: 'outlet', product: 'x-1' }), {});
    assert.equal(sanitizeTech([{ ...p, product: 'x-1' }], s)![0]!.product, 'x-1');
    const r: Railing = { id: 'bal-1', a: [0, 4.5], b: [3, 4.5], product: 'x-1' };
    assert.equal(sanitizeRailing(r).product, 'x-1'); assert.equal(sanitizeRailing({ ...r, kind: 'glass' }).product, undefined); assert.equal(sanitizeRailing({ ...r, product: 'a b' }).product, undefined);
    const ok = checkSnapshot(structuredClone({ ...blank(), tech: [{ ...p, product: 'x-1' }] })); assert.equal(ok.tech![0]!.product, 'x-1');
  });
  test('fără produs ales, devizul unui proiect nou nu se schimbă (nicio linie nouă din catalog)', () => {
    const s = blank(), b = computeBOQ(s, cat, mc);
    assert.ok(!b.items.some(i => NEW.some(m => m.id === i.refId))); assert.ok(unknownIds(b).every(u => !/Hornbach/.test(u)));
  });
});
