// Paritate cu prototipul (moștenite din Faza 0) + catalogul din baza de date dă aceleași rezultate.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createLayoutEngine } from '../core/layout';
import { toEngineCatalog } from '../core/catalog';
import { planToFloor, floorToPlan } from '../features/migration/legacy';
import plan from '../data/legacy-plan.json';
import legacyOpts from '../data/legacy-opts.json';
import catalogV1 from '../data/catalog.v1.json';

const catalog: any = (legacyOpts as any).groups;
// Windows checkouts may convert the prototype to CRLF; the block markers below are matched with '\n'.
const html = readFileSync(new URL('../legacy/prototype-v2.html', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const layBlock = html.slice(html.indexOf('/* =====================================================================\n   3) AMENAJAREA'), html.indexOf('/* =====================================================================\n   4) SCENA 3D'));
function legacyRun(sel: any, picked = new Set<string>()){
  const f = new Function('PLAN', 'OPTS', 'SEL', 'PICKED', `const WT = .15; let NOFIT = [];
    const cur = k => { const g = OPTS[k]; return g ? Object.assign({ model: g.model }, g.v[Math.min(SEL[k] || 0, g.v.length - 1)]) : null; };
    const CAT = new Proxy({}, { get: (_, k) => cur(k) }); const area = r => (r.x1 - r.x0) * (r.z1 - r.z0);
    ${layBlock}; const placed = autoFurnish(); return { placed, notFit: NOFIT };`);
  return f(structuredClone(plan), catalog, sel, picked);
}
const defaults = () => Object.fromEntries(Object.keys(catalog).map(k => [k, 0]));
const norm = (r: any) => ({ placed: r.placed.map((p: any) => [p.key, p.room, p.side || '-', ...[p.fp.x0, p.fp.z0, p.fp.x1, p.fp.z1].map((v: number) => v.toFixed(4))].join('|')), notFit: r.notFit.map((n: any) => n.key + '@' + n.room) });

test('paritate: amenajarea implicită e identică cu prototipul', () => {
  assert.deepEqual(norm(createLayoutEngine({ plan: structuredClone(plan) as any, catalog, selection: defaults() }).run()), norm(legacyRun(defaults())));
});
test('paritate: toate cele 68 de variante, alese pe rând', () => {
  let n = 0;
  for (const [k, g] of Object.entries<any>(catalog)) g.v.forEach((_: any, i: number) => { const s1: any = defaults(), s2: any = defaults(); s1[k] = s2[k] = i;
    assert.deepEqual(norm(createLayoutEngine({ plan: structuredClone(plan) as any, catalog, selection: s1, picked: new Set([k]) }).run()), norm(legacyRun(s2, new Set([k]))), `${k}#${i}`); n++; });
  assert.equal(n, 68);
});
test('catalogul din baza de date (v1) produce aceeași amenajare ca OPTS-ul original', () => {
  const eng = toEngineCatalog(catalogV1 as any);
  assert.deepEqual(norm(createLayoutEngine({ plan: structuredClone(plan) as any, catalog: eng, selection: defaults() }).run()), norm(legacyRun(defaults())));
  for (const k of Object.keys(catalog)) assert.equal(eng[k].v.length, catalog[k].v.length, k);
});
test('migrare dus-întors PLAN → Floor → PLAN fără pierderi', () => {
  assert.deepEqual(floorToPlan(planToFloor(plan as any), (plan as any).nume), plan);
});
