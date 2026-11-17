import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
import { evaluateProjectHealth } from '../core/project-health';

const cat = catalogJson as unknown as Catalog;
const mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Demo Project', 'demo');

test('evaluateProjectHealth evaluates clean demo project with high health score', () => {
  const s = demo();
  const health = evaluateProjectHealth(s, cat, mc);

  assert.ok(health.score >= 70 && health.score <= 100);
  assert.equal(health.facts.errorsCount, 0);
  assert.ok(Array.isArray(health.actionsQueue));
});

test('evaluateProjectHealth catches geometric errors and flags blocker actions', () => {
  const s = demo();
  // Corrupt placement to be outside room
  s.placements[0]!.x = -50; // out of room
  const health = evaluateProjectHealth(s, cat, mc);

  assert.ok(health.facts.errorsCount > 0);
  assert.ok(health.score < 80);
  const blocker = health.actionsQueue.find(a => a.severity === 'BLOCKER');
  assert.ok(blocker);
  assert.equal(blocker.targetKind, 'placement');
});

test('mesajele pentru utilizator nu conțin identificatori interni (grupa „dus”), ci numele produsului; DXF românesc fără simbolul „K”', async () => {
  const s = demo(); s.tech = s.tech ?? []; const shower = s.placements.find(p => p.group === 'dus')!;
  const drain = evaluateProjectHealth({ ...s, tech: [] }, cat, mc).actionsQueue.find(a => a.id === `tech-drain-${shower.id}` || a.id.startsWith('tech-drain-'))!;
  assert.ok(drain, 'demo fără scurgeri: acțiune de scurgere');
  for (const txt of [drain.title.ro, drain.title.en]){ assert.ok(!/\((dus|wc|lavoar)\)/.test(txt), txt); assert.ok(!/\b(dus|lavoar)\b:/.test(txt), txt); }
  const { planToDxf } = await import('../core/dxf'); const { suggestTechPoints } = await import('../core/technical');
  const t = demo(); t.tech = suggestTechPoints(t); assert.ok(t.tech.some(p => p.kind === 'cooker'), 'demo are plită');
  const ro = planToDxf(t, cat, { lang: 'ro' }), en = planToDxf(t, cat, { lang: 'en' });
  assert.ok(/\n1\nPL\n/.test(ro) && !/\n1\nK\n/.test(ro), 'plita = PL în română'); assert.ok(/\n1\nK\n/.test(en), 'K rămâne în engleză');
});