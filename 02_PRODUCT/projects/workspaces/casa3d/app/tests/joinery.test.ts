// Dulapul pe comandă: piesele au dimensiunile corecte, aria și cantul ies din piese, CSV-ul e cel cerut de debitare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { wardrobeCutList, cutListCsv, checkWardrobe, DEFAULT_WARDROBE, BOARD_MM } from '../core/joinery';

test('dulap 1800×2400×600 cu două corpuri: laterale, blat, bază, separator, rafturi, uși, spate', () => {
  const r = wardrobeCutList(1800, 2400, 600, DEFAULT_WARDROBE), by = (n: string) => r.parts.filter(p => p.name === n);
  assert.deepEqual(by('Lateral').map(p => [p.l, p.w, p.qty]), [[2400, 600, 2]]);
  assert.deepEqual(by('Blat').map(p => [p.l, p.w]), [[1800 - 2 * BOARD_MM, 600]]);
  assert.equal(by('Separator')[0]!.qty, 1); assert.equal(by('Separator')[0]!.l, 2400 - 80 - 2 * BOARD_MM);
  const innerW = (1800 - 3 * BOARD_MM) / 2; assert.equal(r.innerW, Math.round(innerW));
  assert.equal(by('Raft').reduce((a, p) => a + p.qty, 0), 1 + 4, 'un raft peste bară + 4 rafturi');
  assert.equal(by('Ușă').reduce((a, p) => a + p.qty, 0), 4, 'corpuri de ~873 mm: câte 2 uși');
  assert.ok(by('Ușă').every(p => p.edges.every(e => e === 1)), 'ușile sunt cantuite pe toate laturile');
  assert.equal(r.hardware.find(h => h.name.startsWith('Balamale'))!.qty, 4 * 5, 'uși de 2316 mm: 5 balamale');
  const area = r.parts.filter(p => p.material === 'pal').reduce((a, p) => a + p.l * p.w * p.qty, 0) / 1e6; assert.ok(Math.abs(area - r.palM2) < .01);
  assert.ok(r.sheets >= Math.ceil(r.palM2 / 5.796)); assert.ok(r.edgeM > 0);
});

test('validare și cazuri limită', () => {
  for (const bad of [{ sections: [], doors: 'hinged', plinthMm: 80 }, { sections: [{ kind: 'x' }], doors: 'hinged', plinthMm: 80 }, { sections: [{ kind: 'shelves', shelves: 40 }], doors: 'hinged', plinthMm: 80 }, { sections: [{ kind: 'hang' }], doors: 'pivot', plinthMm: 80 }, { sections: [{ kind: 'hang' }], doors: 'none', plinthMm: 500 }])
    assert.throws(() => checkWardrobe(bad), JSON.stringify(bad));
  assert.throws(() => wardrobeCutList(400, 2400, 600, { ...DEFAULT_WARDROBE, sections: [{ kind: 'hang' }, { kind: 'hang' }, { kind: 'hang' }] }), /prea mic/);
  const sl = wardrobeCutList(2400, 2400, 650, { ...DEFAULT_WARDROBE, doors: 'sliding' }); assert.equal(sl.parts.find(p => p.name.startsWith('Ușă glisantă'))!.qty, 3);
  const dr = wardrobeCutList(900, 2000, 600, { sections: [{ kind: 'drawers', drawers: 3 }], doors: 'none', plinthMm: 0, edge: 'abs04' }); assert.equal(dr.parts.find(p => p.name === 'Front sertar')!.qty, 3); assert.ok(!dr.parts.some(p => p.name === 'Soclu'));
});

test('CSV pentru debitare: o linie pe piesă, cant marcat pe laturi, decor', () => {
  const r = wardrobeCutList(1200, 2200, 600, { ...DEFAULT_WARDROBE, decor: 'Egger W1000 ST9' }), csv = cutListCsv(r.parts, { decor: 'Egger W1000 ST9', edge: 'abs2' }).split('\n');
  assert.equal(csv.length, r.parts.length + 1); assert.match(csv[0]!, /^Nr;Denumire;Lungime \(mm\);Latime \(mm\);Buc;Cant L1/);
  assert.match(csv.find(l => l.includes(';Lateral;'))!, /;2200;600;2;ABS 2mm;;;;PAL 18mm Egger W1000 ST9;da$/);
  assert.match(csv.find(l => l.includes('Spate HDF'))!, /HDF 3mm;nu$/);
});

test('dulapul se salvează în plan doar pentru piese existente; datele greșite sunt refuzate', async () => {
  process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
  const { resetDbForTests } = await import('../lib/db'); await resetDbForTests(); const repo = await import('../lib/repo');
  const A = '78787878-1111-4111-8111-787878787878', id = await repo.createProject(A, 'Dulap', 'demo'), p = await repo.getProject(A, id), w = p.draft.placements.find(x => x.group === 'dulap')!;
  await repo.saveDraft(A, id, { ...p.draft, joinery: { [w.id]: DEFAULT_WARDROBE, 'nu-exista': DEFAULT_WARDROBE } as any });
  const saved = (await repo.getProject(A, id)).draft.joinery!; assert.deepEqual(Object.keys(saved), [w.id]); assert.equal(saved[w.id]!.sections.length, 2);
  await assert.rejects(repo.saveDraft(A, id, { ...p.draft, joinery: { [w.id]: { sections: [], doors: 'hinged', plinthMm: 80 } } as any }), (e: any) => e.status === 400);
});
