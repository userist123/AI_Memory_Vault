// Faza 3 — STOP GATE: brief, propuneri, validare, respingere, aprobare, aplicare.
import { test, describe, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import catalogV1 from '../data/catalog.v1.json';
import materials from '../data/materials.v1.json';
import { newSnapshot, autoLayout } from '../core/project';
import { checkBrief, DEFAULT_BRIEF } from '../core/brief';
import { parseProposal, evaluateVariant, applyVariant } from '../core/proposal';
import { proposeByRules } from '../core/proposer-rules';
import { resolve } from '../core/catalog';
import type { Catalog, MaterialsCatalog } from '../core/types';
const cat = catalogV1 as unknown as Catalog, mc = materials as unknown as MaterialsCatalog;
const demo = () => newSnapshot(cat, 'Design', 'demo');
const brief = (o: any = {}) => checkBrief({ ...DEFAULT_BRIEF, ...o });

describe('brief', () => {
  test('validare: stil necunoscut și fără magazine → respins; text curățat; ocupanți limitați', () => {
    assert.throws(() => checkBrief({ ...DEFAULT_BRIEF, style: 'baroc' })); assert.throws(() => checkBrief({ ...DEFAULT_BRIEF, suppliers: [] }));
    const b = checkBrief({ ...DEFAULT_BRIEF, occupants: 99, notes: '<script>x</script>', priorities: ['depozitare', 'hack'] });
    assert.equal(b.occupants, 12); assert.ok(!b.notes.includes('<')); assert.deepEqual(b.priorities, ['depozitare']);
  });
});
describe('propuneri (motorul de reguli)', () => {
  test('3 variante, fără erori, doar produse din catalog, costuri crescătoare', () => {
    const s = demo(), p = proposeByRules(s, cat, mc, brief()); assert.equal(p.variants.length, 3);
    const evs = p.variants.map(v => evaluateVariant(s, cat, mc, v, brief()));
    for (const e of evs){ assert.notEqual(e.status, 'ERROR', `${e.tier}: ${JSON.stringify(e.issues.filter(i => i.severity === 'ERROR'))}`); for (const vid of Object.values(e.selections)) assert.ok(resolve(cat, vid), vid); }
    assert.ok(evs[0].total <= evs[1].total && evs[1].total <= evs[2].total, evs.map(e => e.total).join(' / '));
  });
  test('animale → fără catifea/piele; copii → fără blat de sticlă; doar IKEA → nimic de la Dedeman', () => {
    const s = demo(), b = brief({ pets: true, children: true, suppliers: ['IKEA'] });
    for (const v of proposeByRules(s, cat, mc, b).variants){
      const sofa = resolve(cat, v.selections.canapea)!; assert.ok(!['velvet', 'leather'].includes(sofa.variant.style.mat), sofa.variant.name);
      if (v.selections.masuta) assert.ok(!/sticl/i.test(resolve(cat, v.selections.masuta)!.variant.name));
      for (const vid of Object.values(v.selections)) assert.equal(resolve(cat, vid)!.offer!.provenance.source, 'IKEA');
      for (const f of Object.values(v.finishes)) for (const mid of Object.values(f)) if (mid) assert.equal(mc.materials.find(m => m.id === mid)!.supplier, 'IKEA');
    }
  });
  test('pozițiile vin din motorul geometric, nu din propunere', () => {
    const s = demo(), v = proposeByRules(s, cat, mc, brief()).variants[1], e = evaluateVariant(s, cat, mc, v, brief());
    const again = autoLayout({ ...e.candidate, placements: [] }, cat).snapshot.placements, key = (p: any) => `${p.group}|${p.roomId}|${p.x}|${p.z}|${p.rotation}`;
    assert.deepEqual(e.candidate.placements.map(key).sort(), again.map(key).sort());
  });
});
describe('validare', () => {
  test('schemă invalidă → respinsă', () => {
    assert.equal(parseProposal({}).proposal, null); assert.equal(parseProposal({ variants: [{ tier: 'lux', selections: {} }] }).proposal, null);
    const p = parseProposal({ variants: [{ tier: 'economic', selections: { canapea: 'canapea-1' }, palette: ['#fff', '#112233', 'red'] }] }); assert.deepEqual(p.proposal!.variants[0].palette, ['#112233']);
  });
  test('produs inventat → ERROR, nu poate fi aplicat', () => {
    const e = evaluateVariant(demo(), cat, mc, { tier: 'economic', title: '', summary: '', palette: [], selections: { canapea: 'canapea-99', inventat: 'x-1' }, finishes: { living: { floor: 'parchet-fals' } }, reasons: [] });
    assert.equal(e.status, 'ERROR'); assert.ok(e.issues.some(i => i.code === 'INVALID_PRODUCT')); assert.ok(e.issues.some(i => i.code === 'INVALID_MATERIAL'));
    assert.equal(applyVariant(e, true).ok, false);
  });
  test('mobilier prea mare (masa LANEBERG 130 cm) → DOES_NOT_FIT, blocat', () => {
    const e = evaluateVariant(demo(), cat, mc, { tier: 'premium', title: '', summary: '', palette: [], selections: { masa: 'masa-2' }, finishes: {}, reasons: [] });
    assert.ok(e.issues.some(i => i.code === 'DOES_NOT_FIT')); assert.equal(applyVariant(e, true).ok, false);
  });
  test('depășire de buget → WARNING; aplicarea cere confirmare', () => {
    const s = demo(), v = proposeByRules(s, cat, mc, brief({ budget: 10000 })).variants[0], e = evaluateVariant(s, cat, mc, v, brief({ budget: 10000 }));
    assert.ok(e.issues.some(i => i.code === 'OVER_BUDGET' && i.severity === 'WARNING')); assert.equal(applyVariant(e, false).ok, false); assert.equal(applyVariant(e, true).ok, true);
  });
  test('„ce se schimbă” arată diferența de preț exactă', () => {
    const s = demo(), e = evaluateVariant(s, cat, mc, { tier: 'premium', title: '', summary: '', palette: [], selections: { canapea: 'canapea-2' }, finishes: {}, reasons: [] });
    const c = e.changes.find(x => x.label === 'Canapea')!; assert.equal(c.priceDelta, resolve(cat, 'canapea-2')!.offer!.price - resolve(cat, 'canapea-0')!.offer!.price);
  });
});
describe('server: respingere, aprobare, aplicare, AI', () => {
  const A = '33333333-3333-4333-8333-333333333333';
  let repo: typeof import('../lib/repo');
  beforeAll(async () => { process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL; delete process.env.ANTHROPIC_API_KEY; (await import('../lib/db')).resetDbForTests(); repo = await import('../lib/repo'); });
  test('fără cheie AI: variante din motorul de reguli, marcate clar', async () => {
    const id = await repo.createProject(A, 'D1', 'demo'), r = await repo.generateProposal(A, id, DEFAULT_BRIEF);
    assert.equal(r.source, 'rules'); assert.equal(r.aiGenerated, false); assert.ok(r.notes[0].includes('neconfigurat')); assert.equal(r.variants.length, 3);
    assert.deepEqual((await repo.getProject(A, id)).draft.brief!.style, 'scandinav');
  });
  test('respingere: proiectul rămâne neschimbat', async () => {
    const id = await repo.createProject(A, 'D2', 'demo'), before = (await repo.getProject(A, id)).draft, r = await repo.generateProposal(A, id, DEFAULT_BRIEF);
    await repo.rejectProposal(A, id, r.id, 'premium'); const after = (await repo.getProject(A, id)).draft;
    assert.deepEqual(after.placements, before.placements); assert.deepEqual(after.selections, before.selections);
  });
  test('aprobare: se aplică în Digital Twin și creează revizie; ERROR → 409', async () => {
    const id = await repo.createProject(A, 'D3', 'demo'), r = await repo.generateProposal(A, id, DEFAULT_BRIEF), v = r.variants.find(x => x.tier === 'premium')!;
    const res = await repo.applyProposal(A, id, r.id, 'premium', true), d = (await repo.getProject(A, id)).draft;
    for (const [g, vid] of Object.entries(v.selections)) if (g !== 'plita' && g !== 'cuptor' && g !== 'hota') assert.ok(d.placements.some(p => p.group === g && p.variantId === vid), g);
    assert.equal(res.revision, 1); assert.ok((await repo.listRevisions(A, id))[0].note.includes('premium'));
    await assert.rejects(repo.applyProposal(A, id, r.id, 'lux', true), (e: any) => e.status === 400);
  });
  test('cu AI (răspuns simulat): produsul inventat e prins de validare', async () => {
    process.env.ANTHROPIC_API_KEY = 'test';
    const fake = (async () => ({ ok: true, json: async () => ({ content: [{ type: 'text', text: '```json\n' + JSON.stringify({ variants: [
      { tier: 'economic', title: 'E', summary: 's', palette: ['#aabbcc'], selections: { canapea: 'canapea-1' }, finishes: {}, reasons: [{ target: 'Canapea', reason: 'mai ieftină' }] },
      { tier: 'premium', title: 'P', summary: 's', palette: [], selections: { canapea: 'canapea-inventata' }, finishes: {}, reasons: [] }] }) + '\n```' }] }) })) as any;
    const id = await repo.createProject(A, 'D4', 'demo'), r = await repo.generateProposal(A, id, DEFAULT_BRIEF, { fetchImpl: fake });
    assert.equal(r.source, 'ai'); assert.equal(r.aiGenerated, true);
    assert.notEqual(r.variants.find(v => v.tier === 'economic')!.status, 'ERROR'); assert.equal(r.variants.find(v => v.tier === 'premium')!.status, 'ERROR');
    await assert.rejects(repo.applyProposal(A, id, r.id, 'premium', true), (e: any) => e.status === 409);
    const bad = (async () => ({ ok: true, json: async () => ({ content: [{ type: 'text', text: 'nu știu' }] }) })) as any;
    const r2 = await repo.generateProposal(A, id, DEFAULT_BRIEF, { fetchImpl: bad }); assert.equal(r2.source, 'rules'); assert.ok(r2.notes.some(n => n.includes('motorul de reguli')));
    delete process.env.ANTHROPIC_API_KEY;
  });
});
