// Fluxul designerului de interior (auditul „Apartament Popescu”): comenzi cu contoare imediate, „Neincluse în total”
// curat, prea multă lumină, mobilier potrivit camerei, variante A/B cu descriere, numele clientului o dată pe link,
// diacritice în catalog și stilul aplicat care schimbă și brieful.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { finishesOf } from '../core/boq';
import { finishIssues } from '../core/finishes';
import { lightReport } from '../core/light-design';
import { withOrderState, procurementSummary, bySupplier, procurementRows, type ProcurementLine } from '../core/procurement';
import { excludedGroups, colorName } from '../core/budget-excluded';
import { groupsForRoom, defaultGroupForRoom, cardNames, swapFitHint } from '../core/furniture-fit';
import { optionDescription } from '../core/room-options';
import { STYLE_PACKAGES, applyStyle, appliedStyleId, STYLE_BRIEF } from '../core/styles';
import { STYLES } from '../core/brief';
import { cleanClientName, clientNameKey, readClientName, writeClientName } from '../lib/client-name';
import { ro } from '../lib/locales/ro';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Flux', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;

test('comenzi: un clic pe „Comandat” schimbă imediat starea și contoarele (înainte de răspunsul serverului)', () => {
  const line = (k: string, total: number): ProcurementLine => ({ key: `f|${k}`, kind: 'furniture', product: k, supplier: 'IKEA', code: null, url: null, image: null, qty: 1, unit: 'buc', packs: null, unitPrice: total, total, rooms: [], leadDays: 14 } as any);
  const rows = procurementRows([line('pat', 1000), line('dulap', 240)], {}, '2026-10-15', '2026-11-02');
  assert.deepEqual([procurementSummary(rows).todo, procurementSummary(rows).ordered], [2, 0]);
  const next = withOrderState(rows, 'f|dulap', { status: 'ordered', orderedAt: '2026-10-15' }, '2026-10-15'), s = procurementSummary(next);
  assert.deepEqual([s.todo, s.ordered, s.ordersValue], [1, 1, 240]);
  assert.equal(next.find(r => r.key === 'f|dulap')!.orderUrgency, null, 'o linie comandată nu mai e „de comandat urgent”');
  assert.equal(bySupplier(next)[0]!.open, 1);
  assert.equal(next.find(r => r.key === 'f|pat'), rows.find(r => r.key === 'f|pat'), 'celelalte linii rămân neatinse');
});

test('„Neincluse în total”: listă fără dubluri, grupată pe tip, fără coduri hex brute', () => {
  const g = excludedGroups(['Transport JYSK', 'Montaj mobilier'], ['Montaj ușă Ușă Megadoor', 'Montaj ușă Ușă Megadoor', 'Montaj ușă Ușă Megadoor', 'Montaj ușă Ușă Megadoor',
    'Blat bucătărie 2.4 m, 38 mm, culoare #b8936a (ofertă)', 'Fronturi bucătărie 8 buc, gloss, #3a5f4b (diferență față de varianta din catalog)', 'Living: slats N', 'Aplică ×2 (produs neales) · Dormitor']);
  const all = g.flatMap(x => x.items), text = JSON.stringify(all.map(i => i.label));
  assert.ok(!/#[0-9a-f]{6}/i.test(text), text);
  const door = all.find(i => /Montaj ușă/.test(i.label))!; assert.equal(door.count, 4, 'aceeași ușă o singură dată, ×4');
  const top = all.find(i => /Blat/.test(i.label))!; assert.equal(top.color, '#b8936a'); assert.match(top.label, /culoare stejar/); assert.equal(top.note, 'onOrder');
  assert.match(all.find(i => /Fronturi/.test(i.label))!.label, /lucios, verde/);
  assert.match(all.find(i => /Living/.test(i.label))!.label, /riflaj, perete nord/);
  assert.deepEqual(g.map(x => x.kind), ['finishes', 'kitchen', 'lighting', 'labor', 'services']);
  assert.equal(colorName('#ffffff'), 'alb'); assert.equal(colorName('#1a1a1a'), 'gri închis');
});

test('iluminat: avertisment și când lumina e PESTE maximul recomandat, nu doar sub minim', () => {
  const s = demo(), r = room(s, 'dormitor'), f: RoomFinishes = { ...finishesOf(s, r), ceiling: { type: 'drop', dropCm: 10, spot: 'spot-mt143-9w', spots: 12 } };
  const rep = lightReport(mc, s.floor, r, f); assert.ok(rep.target && rep.lux > rep.target[1], `lux ${rep.lux}`);
  s.finishes = { ...(s.finishes || {}), [r.id]: f };
  const issues = finishIssues(s, mc, s.floor, r, f), high = issues.find(i => i.key === 'light.high');
  assert.ok(high, JSON.stringify(issues.map(i => i.key))); assert.equal(high!.vars!.max, rep.target![1]);
  assert.ok(!issues.some(i => i.key === 'light.low'));
  assert.ok((ro as Record<string, string>)['light.high']);
});

test('mobilier: grupele potrivite camerei (dormitor → pat, dulap, noptieră; niciodată mobilă de baie), carduri fără nume repetat, mesaj la o variantă prea mare', () => {
  const keys = [...new Set(cat.variants.map(v => v.id.slice(0, v.id.lastIndexOf('-'))))];
  const bed = groupsForRoom('dormitor', keys);
  assert.deepEqual(bed.suggested.slice(0, 3), ['pat', 'dulap', 'noptiera']);
  for (const g of ['lavoar', 'wc', 'dus', 'cada', 'dulapOglinda', 'coloanaBaie', 'bucatarie', 'frigider']) assert.ok(![...bed.suggested, ...bed.others].includes(g), g);
  assert.equal(defaultGroupForRoom('dormitor', keys), 'pat'); assert.equal(defaultGroupForRoom('baie', keys), 'lavoar');
  assert.ok(groupsForRoom('baie', keys).suggested.includes('dulapOglinda'));
  assert.deepEqual(cardNames('KIVIK canapea 3 locuri', 'KIVIK canapea 3 locuri, Tibbleby bej/gri'), { title: 'KIVIK canapea 3 locuri, Tibbleby bej/gri', sub: null });
  assert.deepEqual(cardNames('Pat Fidelio', 'Pat Fidelio'), { title: 'Pat Fidelio', sub: null });
  assert.deepEqual(swapFitHint({ w: 200, d: 90 }, { w: 228, d: 95 }), { dim: 'width', overCm: 28, newCm: 228, oldCm: 200 });
  assert.equal(swapFitHint({ w: 200, d: 90 }, { w: 180, d: 90 }), null);
});

test('variante A/B: fără notă, descrierea e „Varianta B: altă alegere la …”, niciodată „fără descriere”', () => {
  assert.deepEqual(optionDescription('', 'A', []), { key: 'ropt.defaultNote', vars: { l: 'A' } });
  assert.deepEqual(optionDescription(undefined, 'B', ['Canapea', 'Covor', 'Canapea']), { key: 'ropt.defaultNoteChanges', vars: { l: 'B', list: 'Canapea, Covor' } });
  assert.equal(optionDescription(' catifea verde ', 'B', ['Canapea']).text, 'catifea verde');
  const L = ro as Record<string, string>;
  assert.equal(L['ropt.defaultNote']!.replace('{l}', 'A'), 'Varianta A');
  // pe partea clientului: „dumneavoastră”, nu „tu”
  for (const k of ['ropt.clientTitle', 'ropt.youChose', 'ropt.namePh', 'ropt.nameNeeded', 'ropt.error', 'fb.name', 'co.nameFirst', 'fb.nameFirst'])
    assert.ok(!/(^|\s)(tău|Alege|Ai ales|Scrie-ți|Încearcă)(?=[\s.,]|$)/.test(L[k]!), `${k}: ${L[k]}`);
});

test('numele clientului: o singură dată pe link, ținut minte pe link (nu pe alt link), curățat', () => {
  const store = new Map<string, string>(), events: any[] = [];
  (globalThis as any).localStorage = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v); }, removeItem: (k: string) => { store.delete(k); } };
  (globalThis as any).window = { dispatchEvent: (e: any) => events.push(e) };
  (globalThis as any).CustomEvent ??= class { type: string; detail: any; constructor(t: string, o: any){ this.type = t; this.detail = o?.detail; } };
  try {
    assert.equal(readClientName('tok1'), '');
    assert.equal(writeClientName('tok1', '  Ana   Popescu  '), 'Ana Popescu');
    assert.equal(readClientName('tok1'), 'Ana Popescu'); assert.equal(store.get(clientNameKey('tok1')), 'Ana Popescu');
    assert.equal(readClientName('tok2'), '', 'alt link nu primește numele');
    assert.equal(events.at(-1).detail.name, 'Ana Popescu', 'celelalte casete de pe pagină află numele');
    writeClientName('tok1', ''); assert.equal(readClientName('tok1'), '');
    assert.equal(cleanClientName('x'.repeat(80)).length, 60);
  } finally { delete (globalThis as any).localStorage; delete (globalThis as any).window; }
});

test('catalog: numele de produse și materiale arătate clientului au diacritice (ș ț ă î â), id-urile nu se schimbă', () => {
  const names = [...cat.products.map(p => p.name), ...cat.variants.map(v => v.name), ...mc.materials.map(m => m.name)];
  for (const bad of [/\bFaianta\b/, /\bportelanata\b/, /\bCabina dus\b/, /\bOglinda baie\b/, /\bbucatarie\b/, /\bsi\b/, /[şţŞŢ]/]) assert.ok(!names.some(n => bad.test(n)), String(bad));
  assert.ok(names.some(n => /Faianță/.test(n)) && names.some(n => /porțelanată/.test(n)) && names.some(n => /Cabină duș/.test(n)));
  assert.ok(cat.variants.every(v => /^[a-zA-Z0-9-]+$/.test(v.id)) && mc.materials.every(m => /^[a-z0-9-]+$/.test(m.id)), 'id-urile rămân ASCII');
});

test('stil: același nume în Concept și în brief; „Aplică” arată starea aplicată și schimbă stilul din brief', () => {
  for (const p of STYLE_PACKAGES) assert.ok(STYLE_BRIEF[p.id] in STYLES, p.id);
  assert.equal(STYLES[STYLE_BRIEF.japandi], 'Japandi'); assert.equal(STYLES[STYLE_BRIEF.industrial], 'Industrial');
  const s0 = demo(); assert.equal(appliedStyleId(s0, mc), null);
  const pkg = STYLE_PACKAGES.find(p => p.id === 'japandi')!, s1 = applyStyle(s0, cat, mc, pkg);
  assert.equal(appliedStyleId(s1, mc), 'japandi'); assert.equal(s1.brief?.style, 'natural');
  const r = s1.floor.rooms.find(x => x.type === 'living')!; s1.finishes![r.id] = { ...s1.finishes![r.id]!, wallPaint: 'vopsea-savana-super' };
  assert.equal(appliedStyleId(s1, mc), null, 'după o schimbare manuală, pachetul nu mai apare „aplicat”');
});
