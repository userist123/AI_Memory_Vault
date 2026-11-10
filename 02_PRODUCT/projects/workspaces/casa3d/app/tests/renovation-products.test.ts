// Renovarea completă (2026-11-10): ferestrele ca produse pe gol, aparatajul electric pe puncte, electrocasnicele noi (mașini de
// spălat rufe și vase, uscătoare, microunde, centrale, boilere, hote, plite, cuptoare) — prețul citit pe pagina produsului,
// cu dimensiunile din fișa tehnică, model 3D propriu, simbol în plan, puncte tehnice și linie în deviz.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import evidence from '../data/catalog-evidence.2026-11-10.json';
import { groups, toEngineCatalog } from '../core/catalog';
import { RETAILER_HOSTS } from '../core/outbound';
import { FRONT_CLEARANCE, WALL_HIGH, ON_SURFACE } from '../core/rules';
import { planSymbol } from '../core/plan-symbols';
import { newSnapshot } from '../core/project';
import { computeBOQ } from '../core/boq';
import { doorOpenings, doorIssues, doorOptions, doorVisual } from '../core/doors';
import { windowOpenings, windowCandidates, windowIssues, sanitizeWindows, windowVisual } from '../core/windows';
import { suggestTechPoints, techBodyFields, pruneProducts, TECH_PRODUCT_CATEGORY } from '../core/technical';
import { groupsForRoom } from '../core/furniture-fit';
import { viewerInput } from '../lib/viewer-input';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';

const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const NEW: Record<string, string> = { masinaSpalat: 'washer', uscator: 'washer', masinaVase: 'dishwasher', microunde: 'microwave', centrala: 'gasboiler' };
const demo = (): Snapshot => newSnapshot(cat, 'Renovare', 'demo', 'ro', { materials: mc });

test('electrocasnicele noi: grupă, model 3D, dimensiuni întregi, simbol în plan, loc în față, camerele potrivite', () => {
  const g = groups(cat), eng = toEngineCatalog(cat);
  for (const [k, model] of Object.entries(NEW)){ assert.ok(g[k] && g[k]!.variants.length >= 3, k); assert.equal(eng[k]!.model, model, k); assert.ok(FRONT_CLEARANCE[k] !== undefined, k);
    for (const v of g[k]!.variants){ const dm = v.dimensionsCm!; assert.ok(dm && dm.w > 0 && dm.d > 0 && dm.h > 0, v.id); assert.equal(v.dimensionsConfidence, 'HIGH', v.id);
      const s = planSymbol(k, dm.w / 100, dm.d / 100); assert.ok(s.outline || s.detail, v.id); } }
  // tipurile diferite au stiluri diferite (nu un model standard): hublou frontal / încărcare verticală / încorporabilă, culori
  const kinds = new Set(g['masinaSpalat']!.variants.map(v => v.style?.kind)); assert.ok(kinds.has('washer_front') && kinds.has('washer_top') && kinds.has('washer_dryer'), [...kinds].join());
  assert.ok(new Set(g['masinaSpalat']!.variants.map(v => v.style?.col)).size >= 3);
  assert.ok(WALL_HIGH.has('centrala') && !WALL_HIGH.has('microunde') && ON_SURFACE.has('microunde'), 'microundele stau pe blat');
  assert.ok(groupsForRoom('baie', Object.keys(g)).suggested.includes('masinaSpalat')); assert.ok(groupsForRoom('bucatarie', Object.keys(g)).suggested.includes('masinaVase'));
  assert.ok(!groupsForRoom('dormitor', Object.keys(g)).suggested.includes('masinaVase'));
});
test('fiecare ofertă nouă are prețul și linkul citite pe pagina produsului (dovezile din 2026-11-10), pe magazinul declarat', () => {
  const ev = new Map(evidence.items.map(i => [i.url, i.price]));
  const fresh = cat.offers.filter(o => o.provenance.verifiedAt === '2026-11-10'); assert.ok(fresh.length >= 50, String(fresh.length));
  for (const o of fresh){ assert.equal(ev.get(o.provenance.sourceUrl!), o.price, o.id); assert.ok(RETAILER_HOSTS[o.supplierId]!.includes(new URL(o.provenance.sourceUrl!).hostname), o.id); }
  const mats = mc.materials.filter(m => m.verifiedAt === '2026-11-10'); assert.ok(mats.length >= 30);
  for (const m of mats){ assert.equal(ev.get(m.sourceUrl!), m.unitPrice, m.id); assert.ok(['window', 'socket', 'switch', 'thermostat', 'door'].includes(m.category), m.id); }
  // nimic cu dimensiuni contestate nu a intrat în plan; motivul e scris în dovezi
  assert.ok(evidence.skipped.some(s => /BelliSlimo/.test(String((s as { name?: string }).name)) && /adâncimea/.test(String(s.reason))));
  const newIds = new Set(fresh.map(o => o.variantId)); assert.ok(!cat.variants.some(v => newIds.has(v.id) && /BelliSlimo|Velis Tech|Vitodens/.test(v.name)));
});
test('ferestrele: produsele potrivite golului primele, avertisment de mărime și de ușă de balcon, linie în deviz, desen 3D', () => {
  const s = demo(), o = windowOpenings(s)[0]!; assert.ok(o, 'demo are ferestre');
  const c = windowCandidates(mc, o); assert.ok(c.length >= 15); for (let i = 1; i < c.length; i++) assert.ok(Number(c[i - 1]!.fits) >= Number(c[i]!.fits));
  const wrong = c.find(x => !x.fits && x.m.specs?.win?.kind !== 'balcony_door')!, door = c.find(x => x.m.specs?.win?.kind === 'balcony_door')!;
  s.windowUnits = { [o.id]: { product: wrong.m.id } }; assert.ok(windowIssues(s, mc).some(i => i.key === 'win.size'));
  s.windowUnits = { [o.id]: { product: door.m.id } }; assert.ok(windowIssues(s, mc).some(i => i.key === 'win.balconySill'));
  const b = computeBOQ(s, cat, mc), line = b.items.find(i => i.key === `window:${o.id}`); assert.ok(line, 'linia ferestrei'); assert.equal(line!.unitPrice ?? line!.total, line!.unitPrice ?? door.m.unitPrice);
  const v = windowVisual(s, mc, 0)[0]!; assert.equal(v.color, door.m.specs!.color); assert.equal(v.fixed, false);
  const vi = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { plan: { pereti: { goluri: { fer?: unknown }[] }[] } };
  assert.ok(vi.plan.pereti[o.wallIndex]!.goluri[o.openingIndex]!.fer, 'motorul primește tâmplăria aleasă');
});
test('ferestrele la salvare: doar goluri de fereastră existente, id-uri scurte; altceva e refuzat', () => {
  const s = demo(), o = windowOpenings(s)[0]!;
  assert.deepEqual(sanitizeWindows({ [o.id]: { product: 'x-1' }, 'nu-exista': { product: 'x-1' } }, s).value, { [o.id]: { product: 'x-1' } });
  assert.ok(sanitizeWindows([], s).error); assert.ok(sanitizeWindows({ [o.id]: { product: 5 } }, s).error); assert.ok(sanitizeWindows({ [o.id]: { product: 'x'.repeat(81) } }, s).error);
  assert.deepEqual(sanitizeWindows({ [o.id]: {} }, s).value, {});
});
test('aparatajul electric: produsul pe priză, întrerupător și termostat; categoria greșită se elimină; linie în deviz', () => {
  assert.equal(TECH_PRODUCT_CATEGORY.outlet, 'socket'); assert.equal(TECH_PRODUCT_CATEGORY.switch, 'switch');
  const s = demo(); s.tech = suggestTechPoints(s); const out = s.tech.find(p => p.kind === 'outlet')!, sw = s.tech.find(p => p.kind === 'switch')!;
  const sock = mc.materials.find(m => m.category === 'socket')!, swm = mc.materials.find(m => m.category === 'switch')!;
  assert.equal(techBodyFields({ kind: 'outlet', product: sock.id }).product, sock.id);
  out.product = sock.id; sw.product = sock.id; pruneProducts(s, mc); assert.equal(out.product, sock.id); assert.equal(sw.product, undefined, 'priza pe întrerupător se elimină');
  sw.product = swm.id; const b = computeBOQ(s, cat, mc);
  assert.ok(b.items.some(i => i.key.endsWith(`:outlet:${out.id}`))); assert.ok(b.items.some(i => i.key.endsWith(`:switch:${sw.id}`)));
});
test('punctele tehnice automate pentru electrocasnicele noi: priză, apă și scurgere unde trebuie', () => {
  const s = demo(), bath = s.floor.rooms.find(r => r.type === 'baie')!, kit = s.floor.rooms.find(r => r.type === 'bucatarie')!, g = groups(cat);
  const put = (group: string, room: typeof bath) => { const v = g[group]!.variants[0]!; s.placements.push({ id: `t-${group}`, roomId: room.id, group, variantId: v.id, x: (room.rect.x0 + room.rect.x1) / 2, z: (room.rect.z0 + room.rect.z1) / 2, rotation: 0, source: 'manual' }); };
  put('masinaSpalat', bath); put('masinaVase', kit); put('centrala', kit);
  const pts = suggestTechPoints(s), why = (re: RegExp) => pts.filter(p => re.test(p.reason)).map(p => p.kind).sort();
  assert.deepEqual(why(/mașin(ă|a) de spălat \(|robinet mașină de spălat|scurgere mașină de spălat/), ['drain', 'outlet', 'water_cold']);
  assert.deepEqual(why(/mașină de spălat vase|mașină de vase/), ['drain', 'outlet', 'water_cold']);
  assert.ok(why(/centrală/).includes('outlet'));
});
test('regresie: priza dublă = două module; rama nedeclarată pe pagină apare în deviz fără preț', () => {
  const s = demo(); s.tech = suggestTechPoints(s); const dbl = s.tech.find(p => p.kind === 'outlet_double')!; assert.ok(dbl, 'demo are prize duble');
  const sock = mc.materials.find(m => m.category === 'socket' && m.specs?.withFrame !== true)!; dbl.product = sock.id;
  const b = computeBOQ(s, cat, mc), line = b.items.find(i => i.key.endsWith(`:outlet_double:${dbl.id}`))!;
  assert.equal(line.total, Math.round(sock.unitPrice * 2 * 100) / 100 || sock.unitPrice * 2, JSON.stringify(line)); assert.ok(b.unknown.some(u => u.startsWith('Ramă dublă') && u.includes(sock.name)));
  // seria cu ramă inclusă: rama simplă nu mai apare, dar priza dublă cere tot rama dublă
  const framed = mc.materials.find(m => m.category === 'socket' && m.specs?.withFrame === true)!; dbl.product = framed.id;
  const b2 = computeBOQ(s, cat, mc); assert.ok(b2.unknown.some(u => u.startsWith('Ramă dublă') && u.includes(framed.name))); assert.ok(!b2.unknown.some(u => u.startsWith('Ramă simplă') && u.includes(framed.name)));
});
test('regresie: microundele stau pe blatul bucătăriei (0,9 m), nu pe perete; ciocnirea cu frigiderul rămâne verificată', async () => {
  const { surfaceUnder, validatePlacement } = await import('../core/validate');
  const s = demo(), kit = s.placements.find(p => p.group === 'bucatarie')!, fr = s.placements.find(p => p.group === 'frigider')!, mw = groups(cat)['microunde']!.variants[0]!;
  const onCounter = { id: 'mw1', roomId: kit.roomId, group: 'microunde', variantId: mw.id, x: kit.x, z: kit.z, rotation: kit.rotation, source: 'manual' as const };
  s.placements.push(onCounter); assert.equal(surfaceUnder(s, cat, onCounter), .9);
  const onFridge = { ...onCounter, id: 'mw2', x: fr.x, z: fr.z }; s.placements.push(onFridge);
  assert.ok(validatePlacement(s, cat, onFridge).some(i => i.code === 'OVERLAP'), 'microundele peste frigider sunt semnalate');
});
test('ușa de intrare: produs pe golul de intrare, primele în listă, avertismente pe golul greșit, deviz și desen 3D', () => {
  const s = demo(), ent = doorOpenings(s).find(d => d.entrance)!, inner = doorOpenings(s).find(d => !d.entrance)!; assert.ok(ent && inner, 'demo are intrare și uși de interior');
  const doors = mc.materials.filter(m => m.category === 'door' && m.specs?.entrance); assert.ok(doors.length >= 6, String(doors.length));
  for (const m of doors){ assert.ok(m.specs!.sizeCm![0] > 50 && m.specs!.sizeCm![1] > 180, m.id); assert.equal(m.verificationType, 'product_page', m.id); }
  assert.ok(new Set(doors.map(m => m.specs!.color)).size >= 3, 'culori diferite, nu un model standard');
  assert.ok(doorOptions(mc, true)[0]!.specs?.entrance); assert.ok(!doorOptions(mc, false)[0]!.specs?.entrance);
  const d = doors[0]!, interior = mc.materials.find(m => m.category === 'door' && !m.specs?.entrance)!;
  s.doors = { [ent.id]: { product: d.id }, [inner.id]: { product: d.id } };
  const iss = doorIssues(s, mc); assert.ok(!iss.some(i => i.openingId === ent.id && i.key === 'door.entrance')); assert.ok(iss.some(i => i.openingId === inner.id && i.key === 'door.entranceInside'));
  s.doors = { [ent.id]: { product: interior.id } }; assert.ok(doorIssues(s, mc).some(i => i.openingId === ent.id && i.key === 'door.entrance'));
  s.doors = { [ent.id]: { product: d.id } }; const b = computeBOQ(s, cat, mc); assert.ok(b.items.some(i => i.key === `door:${ent.id}`));
  const v = doorVisual(s, mc, ent.level).find(x => x.wallIndex === ent.wallIndex && x.openingIndex === ent.openingIndex) as { entrance?: boolean; color: string | null };
  assert.equal(v.entrance, true); assert.equal(v.color, d.specs!.color);
});
test('fereastra „fixă + deschidere”: primul canat e fix; cuptorul ales ajunge în desenul bucătăriei', () => {
  const s = demo(), o = windowOpenings(s)[0]!, fixPlus = mc.materials.filter(m => m.category === 'window' && /fix[aă]\s*\+/i.test(m.name));
  assert.ok(fixPlus.length >= 3); for (const m of fixPlus) assert.equal(m.specs!.win!.fixedSashes, 1, m.id);
  s.windowUnits = { [o.id]: { product: fixPlus[0]!.id } }; const v = windowVisual(s, mc, 0)[0] as { fixedSashes?: number; fixed: boolean };
  assert.equal(v.fixedSashes, 1); assert.equal(v.fixed, false);
  const plain = mc.materials.find(m => m.category === 'window' && m.specs?.win?.kind === 'double' && !/fix/i.test(m.name))!;
  s.windowUnits = { [o.id]: { product: plain.id } }; assert.equal((windowVisual(s, mc, 0)[0] as { fixedSashes?: number }).fixedSashes, undefined);
  const oven = groups(cat)['cuptor']!.variants.find(v => (v.style as { inox?: boolean } | undefined)?.inox)!; s.selections = { ...s.selections, cuptor: oven.id };
  const vi = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { items: { group: string; variant?: { s?: { oven?: { inox?: boolean; col?: string } } } }[] };
  const k = vi.items.find(i => i.group === 'bucatarie')!; assert.equal(k.variant!.s!.oven!.inox, true); assert.equal(k.variant!.s!.oven!.col, (oven.style as { col: string }).col);
});
