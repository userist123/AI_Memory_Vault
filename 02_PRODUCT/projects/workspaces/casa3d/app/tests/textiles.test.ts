// Textile: ferestrele camerei, draperii/perdele (bară, încrețire, panouri, pachete, cădere), storuri, covor; buget, fișă, 3D, validare.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import { computeBOQ, computeBudget, finishesOf } from '../core/boq';
import { roomWindows, curtainPlan, blindPlan, rugSize, textileIssues, sanitizeTextiles } from '../core/textiles';
import { materialOf } from '../core/finishes';
import { finishSchedule } from '../core/finish-schedule';
import { viewerInput } from '../lib/viewer-input';
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from '../core/types';

const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Textile', 'demo');
const room = (s: Snapshot, id: string) => s.floor.rooms.find(r => r.id === id)!;
const set = (s: Snapshot, id: string, p: Partial<RoomFinishes>) => { s.finishes = { ...(s.finishes || {}), [id]: { ...finishesOf(s, room(s, id)), ...p } }; return s; };

test('ferestrele camerei, cu lățimea și partea dinspre cameră', () => {
  const s = demo(), w = roomWindows(s.floor, room(s, 'living'));
  assert.ok(w.length >= 1); assert.ok(Math.abs(w.reduce((a, x) => a + x.widthM, 0) - 4.5) < 1e-6, 'living are 4,5 m de ferestre');
  for (const x of w){ assert.equal(x.sillM, .9); assert.ok(x.topM > x.sillM); assert.ok(x.inward === 1 || x.inward === -1); }
});
test('draperie: bara + 2×20 cm, panouri după încrețire, pachete de câte 2, căderea până la 1 cm de podea', () => {
  const s = demo(), w = roomWindows(s.floor, room(s, 'living'))[0]!, m = materialOf(mc, 'draperie-pelarkorsbar-floral');
  const p = curtainPlan(w, 2.6, m, 2); assert.equal(p.rodW, Math.round((w.widthM + .4) * 100) / 100);
  assert.equal(p.panels, Math.ceil((w.widthM + .4) * 2 / 1.45)); assert.equal(p.packs, Math.ceil(p.panels / 2));
  assert.equal(p.rodH, Math.min(2.57, w.topM + .15)); assert.equal(p.drop, Math.round((p.rodH - .01) * 100) / 100); assert.equal(p.short, false);
  assert.ok(curtainPlan(w, 2.6, m, 2.5).panels >= p.panels); assert.equal(curtainPlan({ ...w, topM: 2.6 }, 3.2, m, 2).short, true, 'la o fereastră înaltă, 250 cm nu ajung');
});
test('storuri alăturate pe lățimea ferestrei; covorul orientat după cameră', () => {
  const w = { openingId: 'x', wallIndex: 0, openingIndex: 0, side: 'N' as const, widthM: 2.25, sillM: .9, topM: 2.2, inward: 1 as const };
  assert.deepEqual(blindPlan(w, materialOf(mc, 'stor-fonsterblad-80')), { count: 3, coverM: 2.4, gapM: -.15, short: false });
  assert.equal(blindPlan(w, materialOf(mc, 'stor-ringblomma-140')).count, 2);
  const s = demo(); assert.deepEqual(rugSize(room(s, 'living'), materialOf(mc, 'covor-morum-bej-160x230')), { w: 2.3, d: 1.6 });
  assert.deepEqual(rugSize(room(s, 'living'), materialOf(mc, 'covor-morum-bej-160x230'), true), { w: 1.6, d: 2.3 });
});
test('buget: categoria „textile”, pachete de draperii, storuri, covor; fișa de finisaje le are', () => {
  const s = demo(), w = roomWindows(s.floor, room(s, 'living'))[0]!;
  set(s, 'living', { windows: [{ openingId: w.openingId, curtain: 'draperie-pelarkorsbar-floral', sheer: 'perdea-bergnejlika-alb', blind: 'stor-fonsterblad-100' }], rug: { material: 'covor-arende-gri-160x230' } });
  const b = computeBOQ(s, cat, mc), tx = b.items.filter(i => i.category === 'textiles');
  assert.equal(tx.length, 4); const cur = tx.find(i => i.key.startsWith('living:curtain'))!; assert.equal(cur.orderedQty, curtainPlan(w, 2.6, materialOf(mc, 'draperie-pelarkorsbar-floral'), 2).packs);
  assert.match(cur.label, /bară .* panouri, cădere/); assert.equal(tx.find(i => i.key === 'living:rug')!.total, 249);
  assert.ok(computeBudget(s, cat, mc).categories.textiles! > 0);
  const els = finishSchedule(s, cat, mc).filter(r => r.roomId === 'living').map(r => r.element); for (const e of ['curtain', 'sheer', 'blind', 'rug']) assert.ok(els.includes(e as any), e);
});
test('3D: textilele stau pe golul lor, covorul în cameră', () => {
  const s = demo(), w = roomWindows(s.floor, room(s, 'living'))[0]!;
  set(s, 'living', { windows: [{ openingId: w.openingId, curtain: 'draperie-annakajsa-bej', blind: 'jaluzea-vecklarfly-80' }], rug: { material: 'covor-stoense-200x300' } });
  const plan = viewerInput(s, cat, undefined, { mc }).plan, g = plan.pereti[w.wallIndex].goluri[w.openingIndex];
  assert.equal(g.trat.inward, w.inward); assert.equal(g.trat.blind.kind, 'venetian'); assert.equal(g.trat.curtain.color, '#c9b9a0'); assert.equal(g.trat.sheer, null);
  assert.deepEqual(plan.camere.find((c: any) => c.id === 'living').fin.rug, { w: 3, d: 2, color: '#e3ddd0' });
});
test('avertismente și validare', () => {
  const s = demo(); set(s, 'baie', { rug: { material: 'covor-stoense-200x300' } });
  assert.ok(textileIssues(mc, s.floor, room(s, 'baie'), finishesOf(s, room(s, 'baie'))).some(i => i.key === 'tex.rugTooBig'));
  set(s, 'living', { windows: [{ openingId: 'nu-exista', curtain: 'draperie-pelarkorsbar-floral' }] });
  assert.ok(textileIssues(mc, s.floor, room(s, 'living'), finishesOf(s, room(s, 'living'))).some(i => i.key === 'tex.noWindow'));
  assert.equal(sanitizeTextiles({ windows: [{ openingId: 'a' }, { openingId: 'a' }] }), 'Textilele de la ferestre sunt invalide.');
  assert.equal(sanitizeTextiles({ windows: [{ openingId: 'a', fullness: 9 }] }), 'Textilele de la ferestre sunt invalide.');
  assert.equal(sanitizeTextiles({ rug: { material: 5 } }), 'Covorul este invalid.'); assert.equal(sanitizeTextiles({ rug: null, windows: [] }), null);
});
