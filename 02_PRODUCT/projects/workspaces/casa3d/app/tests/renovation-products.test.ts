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
import { computeBOQ, finishesOf } from '../core/boq';
import { kitchenIssues } from '../core/kitchen';
import { doorOpenings, doorIssues, doorOptions, doorVisual, isBalconyDoor, sanitizeDoors } from '../core/doors';
import { windowOpenings, windowCandidates, windowIssues, sanitizeWindows, windowVisual } from '../core/windows';
import { suggestTechPoints, techBodyFields, pruneProducts, TECH_PRODUCT_CATEGORY } from '../core/technical';
import { groupsForRoom } from '../core/furniture-fit';
import { viewerInput, widthInName } from '../lib/viewer-input';
import { applyStyle, STYLE_PACKAGES } from '../core/styles';
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
  assert.ok(!validatePlacement(s, cat, onCounter).some(i => i.code === 'ON_FLOOR'), 'pe blat: fără avertisment');
  const bath = s.floor.rooms.find(x => x.type === 'baie')!, r = bath.rect, onFloor = { ...onCounter, id: 'mw3', roomId: bath.id, x: (r.x0 + r.x1) / 2, z: (r.z0 + r.z1) / 2 }; s.placements.push(onFloor);
  assert.equal(surfaceUnder(s, cat, onFloor), 0); assert.ok(validatePlacement(s, cat, onFloor).some(i => i.code === 'ON_FLOOR'), 'pe pardoseală: avertisment');
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
  // regresie: canatul fix ajunge până în desenul 3D (g.fer), nu doar în windowVisual
  const fers = (viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { plan: { pereti: { goluri: { fer?: { fixedSashes?: number } }[] }[] } }).plan.pereti.flatMap(p => p.goluri).filter(g => g.fer);
  assert.equal(fers[0]!.fer!.fixedSashes, 1, 'g.fer.fixedSashes');
  const plain = mc.materials.find(m => m.category === 'window' && m.specs?.win?.kind === 'double' && !/fix/i.test(m.name))!;
  s.windowUnits = { [o.id]: { product: plain.id } }; assert.equal((windowVisual(s, mc, 0)[0] as { fixedSashes?: number }).fixedSashes, undefined);
  const oven = groups(cat)['cuptor']!.variants.find(v => (v.style as { inox?: boolean } | undefined)?.inox)!; s.selections = { ...s.selections, cuptor: oven.id };
  const vi = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { items: { group: string; variant?: { s?: { oven?: { inox?: boolean; col?: string } } } }[] };
  const k = vi.items.find(i => i.group === 'bucatarie')!; assert.equal(k.variant!.s!.oven!.inox, true); assert.equal(k.variant!.s!.oven!.col, (oven.style as { col: string }).col);
});
test('hota de tavan primește înălțimea camerei (nu o cotă fixă)', () => {
  const s = demo(), hood = groups(cat)['hota']!.variants.find(v => (v.style as { kind?: string } | undefined)?.kind === 'hood_ceiling');
  assert.ok(hood, 'catalogul are o hotă de tavan'); s.selections = { ...s.selections, hota: hood!.id }; s.floor.ceilingHeight = 2.8;
  const vi = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { items: { group: string; variant?: { s?: { hood?: { ceil?: number } } } }[] };
  assert.equal(vi.items.find(i => i.group === 'bucatarie')!.variant!.s!.hood!.ceil, 2.8);
});
test('hota de insulă: coșul până la tavanul camerei și avertisment cât timp există corpuri suspendate', () => {
  const s = demo(), hood = groups(cat)['hota']!.variants.find(v => (v.style as { kind?: string } | undefined)?.kind === 'hood_island');
  assert.ok(hood, 'catalogul are o hotă de insulă'); s.selections = { ...s.selections, hota: hood!.id }; s.floor.ceilingHeight = 2.7;
  const vi = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { items: { group: string; variant?: { s?: { hood?: { ceil?: number } } } }[] };
  assert.equal(vi.items.find(i => i.group === 'bucatarie')!.variant!.s!.hood!.ceil, 2.7);
  const room = s.placements.find(p => p.group === 'bucatarie')!.roomId, keys = (f: ReturnType<typeof finishesOf>) => kitchenIssues(s, cat, mc, room, f).map(i => i.key);
  const f0 = finishesOf(s, s.floor.rooms.find(r => r.id === room)!);
  assert.ok(keys(f0).includes('kit.islandHoodUpper'), 'cu suspendate (implicit): avertisment');
  assert.ok(!keys({ ...f0, kitchen: { ...(f0.kitchen || {}), upper: 'none' } }).includes('kit.islandHoodUpper'), 'fără suspendate: fără avertisment');
  assert.ok(keys({ ...f0, kitchen: undefined }).includes('kit.islandHoodUpper'), 'fără setări de bucătărie (suspendate implicite): avertisment');
  assert.ok(keys({ ...f0, kitchen: { ...(f0.kitchen || {}), upper: 'closed' } }).includes('kit.islandHoodUpper'), 'suspendate închise: avertisment');
  const other = s.floor.rooms.find(r => r.id !== room && !s.placements.some(p => p.roomId === r.id && p.group === 'bucatarie'))!;
  assert.deepEqual(kitchenIssues(s, cat, mc, other.id, finishesOf(s, other)), [], 'camera fără bucătărie: nimic');
  // exact condiția motorului: hota de insulă și bucătăria fără suspendate ajung împreună în desen; lățimea vine din titlu („90cm”)
  s.finishes ={ ...(s.finishes || {}), [room]: { ...f0, kitchen: { ...(f0.kitchen || {}), upper: 'none' } } };
  const v2 = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { items: { group: string; variant?: { s?: { hood?: { kind?: string; w?: number }; k?: { upper?: string } } } }[] }, ks = v2.items.find(i => i.group === 'bucatarie')!.variant!.s!;
  assert.equal(ks.hood!.kind, 'hood_island'); assert.equal(ks.k!.upper, 'none'); assert.equal(ks.hood!.w, 90);
  assert.equal(widthInName('Hotă X, 704mc/h, 60 cm, Inox'), 60); assert.equal(widthInName('Hotă fără lățime, 704mc/h'), null);
  s.selections = { ...s.selections, hota: groups(cat)['hota']!.variants.find(v => (v.style as { kind?: string } | undefined)?.kind === 'hood_chimney')!.id };
  assert.ok(!keys(f0).includes('kit.islandHoodUpper'), 'hota de perete: fără avertisment');
  const v3 = viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { items: { group: string; variant?: { s?: { hood?: { ceil?: number } } } }[] };
  assert.equal(v3.items.find(i => i.group === 'bucatarie')!.variant!.s!.hood!.ceil, 2.7, 'coșul hotei de perete urcă până la tavanul camerei');
});

test('ușa de balcon pe golul de ușă din perete exterior: prima în listă, avertisment pe gol interior, deviz și desen 3D vitrat', () => {
  const s = demo(), wl = s.floor.walls.find(w => w.exterior && !w.openings.some(o => o.entrance) && Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) > 2.4)!;
  assert.ok(wl, 'demo are un perete exterior fără intrare'); const L = Math.hypot(wl.b[0] - wl.a[0], wl.b[1] - wl.a[1]);
  wl.openings = wl.openings.filter(o => o.offset > 1.2 || o.offset + o.width < .2); wl.openings.push({ id: 'balcon-1', kind: 'door', offset: Math.min(L - 1, .25), width: .86 });
  const bd = doorOpenings(s).find(d => d.id === 'balcon-1')!, inner = doorOpenings(s).find(d => !d.entrance && !d.exterior)!;
  assert.equal(bd.exterior, true); assert.equal(inner.exterior, false);
  const opts = doorOptions(mc, false, true), first = opts[0]!; assert.ok(isBalconyDoor(first), 'ușile de balcon primele pe perete exterior');
  assert.ok(!doorOptions(mc, false, false).some(isBalconyDoor), 'pe un gol interior nu apar uși de balcon');
  s.doors = { [bd.id]: { product: first.id } }; const iss = doorIssues(s, mc).filter(i => i.openingId === bd.id);
  assert.ok(!iss.some(i => i.key === 'door.noHandle' || i.key === 'door.balconyInside' || i.key === 'door.size'), JSON.stringify(iss));
  s.doors = { ...s.doors, [inner.id]: { product: first.id } }; assert.ok(doorIssues(s, mc).some(i => i.openingId === inner.id && i.key === 'door.balconyInside'));
  s.doors = { [bd.id]: { product: first.id } }; const line = computeBOQ(s, cat, mc).items.find(i => i.key === `door:${bd.id}`);
  assert.ok(line && /balcon/i.test(line.label), 'linia ușii de balcon'); assert.equal(line!.unitPrice, first.unitPrice);
  const v = doorVisual(s, mc, 0).find(x => x.wallIndex === bd.wallIndex && x.openingIndex === bd.openingIndex)!; assert.equal(v.style, 'balcony'); assert.equal(v.color, first.specs!.color);
  const g = (viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { plan: { pereti: { goluri: { usa?: { style?: string } }[] }[] } }).plan.pereti[bd.wallIndex]!.goluri[bd.openingIndex]!;
  assert.equal(g.usa?.style, 'balcony');
  // un pachet de stil schimbă ușile de interior, nu și ușa de balcon de pe peretele exterior
  const pkg = STYLE_PACKAGES.find(p => p.door)!; assert.ok(pkg, 'un stil cu ușă'); const st = applyStyle(s, cat, mc, pkg);
  assert.equal(st.doors?.[bd.id]?.product, first.id); assert.equal(st.doors?.[inner.id]?.product, pkg.door);
});

test('ventilatorul de baie și priza de date / TV: produse reale pe punctele lor, dovezi, deviz fără ramă la ventilator, corp în 3D', async () => {
  const ev17 = (await import('../data/catalog-evidence.2026-11-17.json')).default as { items: { url: string; price: number; dimensionsCm: { w: number; h: number | null; d: number } | null }[] };
  const byUrl = new Map(ev17.items.map(i => [i.url, i])), fresh = mc.materials.filter(m => m.verifiedAt === '2026-11-17');
  assert.ok(fresh.filter(m => m.category === 'bath_fan').length >= 6 && fresh.filter(m => m.category === 'data_socket').length >= 6, String(fresh.length));
  for (const m of fresh){ const e = byUrl.get(m.sourceUrl!); assert.ok(e, m.id); assert.equal(e!.price, m.unitPrice, m.id); assert.ok(/^(www\.)?(dedeman|hornbach)\.ro$/.test(new URL(m.sourceUrl!).hostname), m.id);
    // corpul ventilatorului doar cu toate trei cotele de pe pagină
    if (m.specs?.bodyCm) assert.deepEqual(m.specs.bodyCm, [e!.dimensionsCm!.w, e!.dimensionsCm!.h, e!.dimensionsCm!.d], m.id); }
  assert.ok(fresh.some(m => m.category === 'bath_fan' && !m.specs?.bodyCm), 'un ventilator fără înălțime pe pagină rămâne fără corp 3D');
  assert.equal(TECH_PRODUCT_CATEGORY.vent_exhaust, 'bath_fan'); assert.equal(TECH_PRODUCT_CATEGORY.data, 'data_socket');
  const s = demo(); s.tech = suggestTechPoints(s); const data = s.tech.find(p => p.kind === 'data')!, room = s.floor.rooms.find(r => r.type === 'baie')!;
  assert.ok(data, 'demo are prize de date'); const fan = fresh.find(m => m.category === 'bath_fan' && m.specs?.bodyCm)!, rj = fresh.find(m => m.category === 'data_socket' && m.specs?.withFrame === false)!;
  const r = room.rect; s.tech.push({ id: 'vent-1', kind: 'vent_exhaust', roomId: room.id, x: r.x0, z: (r.z0 + r.z1) / 2, height: 2.3, reason: 'test', product: fan.id } as never);
  data.product = rj.id; pruneProducts(s, mc); assert.equal(data.product, rj.id); assert.equal(s.tech.find(p => p.id === 'vent-1')!.product, fan.id);
  // produs pe categorie greșită: eliminat la salvare
  const wrong = { ...s, tech: s.tech.map(p => p.id === 'vent-1' ? { ...p, product: rj.id } : p) }; pruneProducts(wrong, mc); assert.equal(wrong.tech!.find(p => p.id === 'vent-1')!.product, undefined);
  const b = computeBOQ(s, cat, mc);
  assert.ok(b.items.some(i => i.key === `${data.roomId}:data:${data.id}` && i.unitPrice === rj.unitPrice), 'priza de date în deviz');
  assert.ok(b.items.some(i => i.key === `${room.id}:vent_exhaust:vent-1` && i.unitPrice === fan.unitPrice), 'ventilatorul în deviz');
  const unk = JSON.stringify(b.unknown ?? b); assert.ok(unk.includes(`Montaj ventilator ${fan.name}`)); assert.ok(!unk.includes(`pentru ${fan.name}`), 'fără ramă la ventilator'); assert.ok(unk.includes(`pentru ${rj.name}`), 'rama prizei de date (nu e inclusă)');
  const t = (viewerInput(s, cat, toEngineCatalog(cat), { mc }) as { tehnice?: { id: string; kind: string; w: number; h: number; d: number }[]; plan?: { tehnice?: { id: string; kind: string; w: number }[] } });
  const list = (t.tehnice ?? t.plan?.tehnice ?? []) as { id: string; kind: string; w: number }[], f3 = list.find(x => x.id === 'vent-1');
  assert.ok(f3, 'ventilatorul ajunge în 3D'); assert.equal(f3!.kind, 'fan'); assert.equal(f3!.w, fan.specs!.bodyCm![0] / 100);
});

test('ușa de balcon (revizia Fable): fără mâner/sistem în deviz, păstrată la salvare, o linie pe gol; rama termostatului; ventilator fără cote sau culoare', () => {
  const s = demo(), wl = s.floor.walls.find(w => w.exterior && !w.openings.some(o => o.entrance) && Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) > 2.4)!;
  wl.openings = wl.openings.filter(o => o.offset > 1.2 || o.offset + o.width < .2); wl.openings.push({ id: 'balcon-2', kind: 'door', offset: .25, width: .86 });
  const bal = doorOptions(mc, false, true)[0]!, handle = mc.materials.find(m => m.category === 'door_handle')!, sys = mc.materials.find(m => m.category === 'door_system')!;
  s.doors = { 'balcon-2': { product: bal.id, handle: handle.id, system: sys.id } };
  const keys = computeBOQ(s, cat, mc).items.map(i => i.key); assert.ok(keys.includes('door:balcon-2')); assert.ok(!keys.includes('handle:balcon-2') && !keys.includes('doorsys:balcon-2'), 'feroneria ușii vechi nu intră în deviz');
  assert.equal(doorVisual(s, mc, 0).find(v => v.style === 'balcony')!.handle, null);
  assert.equal(sanitizeDoors(s.doors, s).value!['balcon-2']!.product, bal.id, 'salvarea păstrează ușa de balcon'); pruneProducts(s, mc); assert.equal(s.doors['balcon-2']!.product, bal.id);
  // tipul golului: ușa de balcon e batantă
  wl.openings.find(o => o.id === 'balcon-2')!.doorType = 'sliding'; assert.ok(doorIssues(s, mc).some(i => i.openingId === 'balcon-2' && i.key === 'door.balconyType')); delete wl.openings.find(o => o.id === 'balcon-2')!.doorType;
  // același produs pe un gol de fereastră și pe unul de ușă: câte o linie pe fiecare gol
  const wo = windowOpenings(s)[0]!; s.windowUnits = { [wo.id]: { product: bal.id } }; const b2 = computeBOQ(s, cat, mc).items;
  assert.equal(b2.filter(i => i.key === `window:${wo.id}`).length, 1); assert.equal(b2.filter(i => i.key === 'door:balcon-2').length, 1);
  // termostatul: rama ca până acum (nedeclarată pe pagină → linie fără preț)
  const t2 = demo(); t2.tech = suggestTechPoints(t2); const th = mc.materials.find(m => m.category === 'thermostat')!; const tp = t2.tech.find(p => p.kind === 'thermostat') ?? t2.tech[0]!;
  tp.kind = 'thermostat'; tp.product = th.id; const unk = JSON.stringify(computeBOQ(t2, cat, mc).unknown); assert.equal(unk.includes(`pentru ${th.name}`), th.specs?.withFrame !== true);
  // ventilator fără cote complete: în deviz, nu în 3D; produs greșit pe punctul de date: eliminat
  const flat = mc.materials.find(m => m.category === 'bath_fan' && !m.specs?.bodyCm)!, room = t2.floor.rooms.find(r => r.type === 'baie')!;
  t2.tech.push({ id: 'vent-2', kind: 'vent_exhaust', roomId: room.id, x: room.rect.x0, z: (room.rect.z0 + room.rect.z1) / 2, height: 2.3, reason: 'test', product: flat.id } as never);
  assert.ok(computeBOQ(t2, cat, mc).items.some(i => i.key === `${room.id}:vent_exhaust:vent-2`));
  const withBody = mc.materials.find(m => m.category === 'bath_fan' && m.specs?.bodyCm)!; t2.tech.push({ id: 'vent-3', kind: 'vent_exhaust', roomId: room.id, x: room.rect.x0, z: room.rect.z0 + .4, height: 2.3, reason: 'test', product: withBody.id } as never);
  const tehn = (viewerInput(t2, cat, toEngineCatalog(cat), { mc }) as { plan: { tehnice?: { id: string }[] } }).plan.tehnice ?? []; assert.ok(!tehn.some(x => x.id === 'vent-2')); assert.ok(tehn.some(x => x.id === 'vent-3'), 'cu cote: desenat');
  const dp = t2.tech.find(p => p.kind === 'data')!; dp.product = flat.id; pruneProducts(t2, mc); assert.equal(dp.product, undefined);
});